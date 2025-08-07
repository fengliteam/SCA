using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

public class UpdateConfig
{
    public string GitHubVersionUrl { get; set; }
    public string LanzouUrl { get; set; }
    public string CurrentVersion { get; set; } // 这个可以不要了，从程序自动获取
}

public class UpdateButtonHandler
{
    private readonly Form _parentForm;
    private readonly string _configUrl;

    public UpdateButtonHandler(Form parentForm, string configUrl)
    {
        _parentForm = parentForm;
        _configUrl = configUrl; 
    }


    private string GetCurrentVersion()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
        catch
        {
            return "1.0.0";
        }
    }


    public async void ExecuteUpdateCheck()
    {
        try
        {
            UpdateConfig config = null;
            using (var httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromSeconds(15);
                var json = await httpClient.GetStringAsync(_configUrl);
                config = JsonConvert.DeserializeObject<UpdateConfig>(json);
            }

            string currentVersion = GetCurrentVersion();

            var choiceForm = new Form
            {
                Text = "选择更新方式",
                Size = new System.Drawing.Size(350, 180),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                ControlBox = true,
                TopMost = true
            };

            var label = new Label
            {
                Text = $"当前版本: {currentVersion}\n请选择更新方式：",
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 50,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(10)
            };

            var autoBtn = new Button
            {
                Text = "自动更新 (GitHub)",
                Size = new System.Drawing.Size(300, 35),
                Location = new System.Drawing.Point(25, 55),
                BackColor = System.Drawing.Color.LightBlue
            };

            var manualBtn = new Button
            {
                Text = "手动更新 (蓝奏云，密码为sca)",
                Size = new System.Drawing.Size(300, 35),
                Location = new System.Drawing.Point(25, 100),
                BackColor = System.Drawing.Color.LightGreen
            };

            autoBtn.Click += (s, e) => {
                choiceForm.Close();
                ExecuteAutoUpdate(config, currentVersion);
            };

            manualBtn.Click += (s, e) => {
                choiceForm.Close();
                ExecuteManualUpdate(config);
            };

            choiceForm.Controls.Add(label);
            choiceForm.Controls.Add(autoBtn);
            choiceForm.Controls.Add(manualBtn);

            choiceForm.ShowDialog(_parentForm);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                _parentForm,
                $"获取更新配置失败:\n{ex.Message}",
                "错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async void ExecuteAutoUpdate(UpdateConfig config, string currentVersion)
    {
        try
        {
            var waitForm = new Form
            {
                Text = "请稍候",
                Size = new System.Drawing.Size(300, 100),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                ControlBox = false,
                TopMost = true
            };

            var label = new Label
            {
                Text = "正在检查更新 (GitHub)...",
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };

            waitForm.Controls.Add(label);
            waitForm.Show(_parentForm);
            waitForm.Refresh();

            using (var httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromSeconds(15);
                var json = await httpClient.GetStringAsync(config.GitHubVersionUrl);
                var latestVersion = JsonConvert.DeserializeObject<VersionInfo>(json);

                waitForm.Close();
                waitForm.Dispose();

                VersionComparisonResult comparison = CompareVersions(latestVersion.Version, currentVersion);

                if (comparison == VersionComparisonResult.Newer)
                {
                    var result = MessageBox.Show(
                        _parentForm,
                        $"发现新版本 {latestVersion.Version}\n当前版本: {currentVersion}\n\n更新说明: {latestVersion.ReleaseNotes}\n\n是否现在自动更新？",
                        "发现更新",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);

                    if (result == DialogResult.Yes)
                    {
                        var downloadForm = new Form
                        {
                            Text = "请稍候",
                            Size = new System.Drawing.Size(300, 100),
                            FormBorderStyle = FormBorderStyle.FixedDialog,
                            StartPosition = FormStartPosition.CenterParent,
                            ControlBox = false,
                            TopMost = true
                        };

                        var downloadLabel = new Label
                        {
                            Text = "正在下载更新 (GitHub)...",
                            AutoSize = false,
                            Dock = DockStyle.Fill,
                            TextAlign = ContentAlignment.MiddleCenter
                        };

                        downloadForm.Controls.Add(downloadLabel);
                        downloadForm.Show(_parentForm);
                        downloadForm.Refresh();

                        try
                        {
                            string tempPath = Path.GetTempFileName();

                            using (var downloadClient = new HttpClient())
                            {
                                downloadClient.Timeout = TimeSpan.FromMinutes(10); 
                                var fileBytes = await downloadClient.GetByteArrayAsync(latestVersion.DownloadUrl);
                                await File.WriteAllBytesAsync(tempPath, fileBytes);
                            }

                            downloadForm.Close();
                            downloadForm.Dispose();

                            var confirmResult = MessageBox.Show(
                                _parentForm,
                                "下载完成！程序将自动重启以完成更新。\n\n点击确定开始更新...",
                                "准备更新",
                                MessageBoxButtons.OKCancel,
                                MessageBoxIcon.Information);

                            if (confirmResult == DialogResult.OK)
                            {
                                string currentAppPath = Assembly.GetExecutingAssembly().Location;
                                string scriptPath = Path.Combine(Path.GetTempPath(), "update.bat");

                                string scriptContent = $@"
@echo off
title 更新程序
echo 正在更新应用程序...
timeout /t 3 /nobreak >nul
taskkill /f /im ""{Path.GetFileName(currentAppPath)}"" >nul 2>&1
del ""{currentAppPath}""
move ""{tempPath}"" ""{currentAppPath}""
echo 更新完成，正在启动程序...
start """" ""{currentAppPath}""
del ""%~f0""
";

                                File.WriteAllText(scriptPath, scriptContent);

                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = "cmd.exe",
                                    Arguments = $"/c {scriptPath}",
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                });

                                _parentForm.Close();
                            }
                        }
                        catch (Exception ex)
                        {
                            downloadForm.Close();
                            downloadForm.Dispose();
                            MessageBox.Show(
                                _parentForm,
                                $"下载失败 (GitHub可能较慢):\n{ex.Message}",
                                "错误",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                        }
                    }
                }
                else if (comparison == VersionComparisonResult.Older)
                {
                    MessageBox.Show(
                        _parentForm,
                        $"当前版本 ({currentVersion}) 比服务器版本 ({latestVersion.Version}) 更新！\n您使用的是测试版本或开发版本。",
                        "版本信息",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(
                        _parentForm,
                        "当前已是最新版本！",
                        "检查完成",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                _parentForm,
                $"自动更新失败:\n{ex.Message}",
                "错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }


    private enum VersionComparisonResult
    {
        Newer,   
        Older,  
        Same      
    }

    private VersionComparisonResult CompareVersions(string serverVersion, string currentVersion)
    {
        try
        {
            var serverVer = new Version(serverVersion);
            var currentVer = new Version(currentVersion);

            if (serverVer > currentVer)
                return VersionComparisonResult.Newer;
            else if (serverVer < currentVer)
                return VersionComparisonResult.Older;
            else
                return VersionComparisonResult.Same;
        }
        catch
        {
            return VersionComparisonResult.Same; 
        }
    }

    private void ExecuteManualUpdate(UpdateConfig config)
    {
        try
        {
            var result = MessageBox.Show(
                _parentForm,
                "即将打开蓝奏云下载页面\n请手动下载最新版本并替换当前程序\n\n点击确定打开链接...",
                "手动更新",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);

            if (result == DialogResult.OK)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = config.LanzouUrl,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                _parentForm,
                $"打开蓝奏云失败:\n{ex.Message}",
                "错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}

public class VersionInfo
{
    public string Version { get; set; }
    public string DownloadUrl { get; set; }
    public string ReleaseNotes { get; set; }
    public string PublishDate { get; set; }
}