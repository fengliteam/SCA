using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using System.Drawing;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;

public static class UpdateChannels
{
    public const string Stable = "https://github.com/fengliteam/SCA/raw/refs/heads/main/version.json";
    public const string Test = "https://github.com/fengliteam/SCA/raw/refs/heads/dev/version.json";
}

public class UpdateConfig
{
    public string GitHubVersionUrl { get; set; }
    public string LanzouUrl { get; set; }
}

public class VersionInfo
{
    public string Version { get; set; }
    public string DownloadUrl { get; set; }
    public string ReleaseNotes { get; set; }
    public string PublishDate { get; set; }
}

public class UpdateButtonHandler
{
    private readonly Form _parentForm;
    private static readonly HttpClient _httpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 10
    })
    {
        Timeout = TimeSpan.FromSeconds(120)
    };

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
    private const int DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    private static void SetProcessDpiAwareness()
    {
        try
        {
            SetProcessDpiAwarenessContext((IntPtr)DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
        }
    }

    public UpdateButtonHandler(Form parentForm)
    {
        _parentForm = parentForm ?? throw new ArgumentNullException(nameof(parentForm));
        SetProcessDpiAwareness();
    }

    private string GetCurrentVersion()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;

            if (version.Major == 0 && version.Minor == 0 && version.Build == 0 && version.Revision == 0)
            {
                var location = assembly.Location;
                if (!string.IsNullOrEmpty(location) && File.Exists(location))
                {
                    var fvi = FileVersionInfo.GetVersionInfo(location);
                    if (!string.IsNullOrEmpty(fvi.FileVersion))
                        return fvi.FileVersion;
                }
            }

            return $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
        }
        catch
        {
            return "1.8.0";
            MessageBox.Show(
                _parentForm,
                "获取当前版本信息失败，使用默认版本号 1.8.0",
                "错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    public void SelectUpdateChannel()
    {
        try
        {
            using var channelForm = new Form
            {
                Text = "选择更新通道",
                Size = new Size(350, 160),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                ControlBox = true,
                TopMost = true,
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96, 96)
            };

            var label = new Label
            {
                Text = "请选择更新通道：",
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(10),
                Font = new Font("Microsoft Sans Serif", 9, FontStyle.Bold)
            };

            var stableBtn = new Button
            {
                Text = "稳定版 (推荐)",
                Size = new Size(300, 35),
                Location = new Point(25, 40),
                BackColor = Color.FromArgb(144, 238, 144),
                Font = new Font("Microsoft Sans Serif", 9),
                UseVisualStyleBackColor = false
            };

            var testBtn = new Button
            {
                Text = "测试通道 (开发者)",
                Size = new Size(300, 35),
                Location = new Point(25, 85),
                BackColor = Color.FromArgb(173, 216, 230),
                Font = new Font("Microsoft Sans Serif", 9),
                UseVisualStyleBackColor = false
            };

            stableBtn.Click += async (s, e) =>
            {
                channelForm.Close();
                await ExecuteUpdateCheckAsync(UpdateChannels.Stable);
            };

            testBtn.Click += async (s, e) =>
            {
                var confirmResult = MessageBox.Show(
                    _parentForm,
                    "测试通道可能包含不稳定的功能，且通常无法访问。\n您确定要切换到测试通道吗？",
                    "确认切换",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirmResult == DialogResult.Yes)
                {
                    channelForm.Close();
                    await ExecuteUpdateCheckAsync(UpdateChannels.Test);
                }
            };

            channelForm.Controls.AddRange(new Control[] { label, stableBtn, testBtn });
            channelForm.ShowDialog(_parentForm);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                _parentForm,
                $"选择更新通道时出错:\n{ex.Message}",
                "错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    public async Task ExecuteUpdateCheckAsync(string configUrl)
    {
        Form? waitForm = null;
        try
        {
            waitForm = CreateWaitForm("正在获取更新配置...");
            waitForm.Show(_parentForm);
            waitForm.Refresh();

            string json;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                json = await _httpClient.GetStringAsync(configUrl, cts.Token);
            }
            catch (OperationCanceledException) when (new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token.IsCancellationRequested)
            {
                throw new TimeoutException("获取更新配置超时 (60秒)");
            }

            var config = JsonConvert.DeserializeObject<UpdateConfig>(json) ?? throw new InvalidOperationException("配置解析失败");

            if (waitForm != null)
            {
                waitForm.Close();
                waitForm.Dispose();
                waitForm = null;
            }

            string currentVersion = GetCurrentVersion();

            using var choiceForm = new Form
            {
                Text = "选择更新方式",
                Size = new Size(350, 180),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                ControlBox = true,
                TopMost = true,
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96, 96)
            };

            var label = new Label
            {
                Text = $"当前版本: {currentVersion}\n请选择更新方式：",
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 60,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(10),
                Font = new Font("Microsoft Sans Serif", 9)
            };

            var autoBtn = new Button
            {
                Text = "自动更新 (GitHub)",
                Size = new Size(300, 35),
                Location = new Point(25, 65),
                BackColor = Color.FromArgb(173, 216, 230),
                Font = new Font("Microsoft Sans Serif", 9, FontStyle.Bold),
                UseVisualStyleBackColor = false
            };

            var manualBtn = new Button
            {
                Text = "手动更新 (蓝奏云，密码为sca)",
                Size = new Size(300, 35),
                Location = new Point(25, 110),
                BackColor = Color.FromArgb(144, 238, 144),
                Font = new Font("Microsoft Sans Serif", 9),
                UseVisualStyleBackColor = false
            };

            autoBtn.Click += async (s, e) =>
            {
                choiceForm.Close();
                await ExecuteAutoUpdateAsync(config.GitHubVersionUrl, currentVersion);
            };

            manualBtn.Click += (s, e) =>
            {
                choiceForm.Close();
                ExecuteManualUpdate(config);
            };

            choiceForm.Controls.AddRange(new Control[] { label, autoBtn, manualBtn });
            choiceForm.ShowDialog(_parentForm);

        }
        catch (Exception ex)
        {
            if (waitForm != null)
            {
                try { waitForm.Close(); } catch { }
                try { waitForm.Dispose(); } catch { }
            }

            MessageBox.Show(
                _parentForm,
                $"获取更新配置失败:\n{ex.Message}\n配置URL: {configUrl}",
                "错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task ExecuteAutoUpdateAsync(string directExeUrl, string currentVersion)
    {
        Form? downloadForm = null;
        string? tempPath = null;
        FileStream? fileStream = null;

        try
        {
            downloadForm = CreateWaitForm("正在从 GitHub 下载更新...");
            downloadForm.Show(_parentForm);
            downloadForm.Refresh();

            tempPath = Path.GetTempFileName();
            fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            using var response = await _httpClient.GetAsync(directExeUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using var contentStream = await response.Content.ReadAsStreamAsync();
            var buffer = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var bytesRead = 0L;

                while (true)
                {
                    var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                    if (read == 0) break;

                    await fileStream.WriteAsync(buffer, 0, read);
                    bytesRead += read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            await fileStream.DisposeAsync();
            fileStream = null;

            if (downloadForm != null)
            {
                downloadForm.Close();
                downloadForm.Dispose();
                downloadForm = null;
            }

            var confirmResult = MessageBox.Show(
                _parentForm,
                $"更新文件已下载完成！\n程序将自动重启以完成更新。\n点击确定开始更新...",
                "准备更新",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);

            if (confirmResult == DialogResult.OK)
            {
                string currentAppPath = Assembly.GetEntryAssembly()?.Location ??
                                      Process.GetCurrentProcess().MainModule?.FileName ??
                                      Application.ExecutablePath;

                string scriptPath = Path.Combine(Path.GetTempPath(), "update_" + Guid.NewGuid().ToString("N") + ".bat");

                string scriptContent = $@"
@echo off
setlocal

set ""APP_PATH={currentAppPath}""
set ""TEMP_FILE={tempPath}""
set ""SCRIPT_PATH=%~f0""

echo 正在更新应用程序...

timeout /t 3 /nobreak >nul

taskkill /f /im ""{Path.GetFileName(currentAppPath)}"" >nul 2>&1
timeout /t 2 /nobreak >nul

if exist ""%APP_PATH%"" (
    del ""%APP_PATH%""
    if errorlevel 1 (
        echo 错误：无法删除旧文件
        timeout /t 5 >nul
        exit /b 1
    )
)

if exist ""%TEMP_FILE%"" (
    move ""%TEMP_FILE%"" ""%APP_PATH%""
    if errorlevel 1 (
        echo 错误：无法移动新文件
        timeout /t 5 >nul
        exit /b 1
    )
) else (
    echo 错误：临时文件不存在
    timeout /t 5 >nul
    exit /b 1
)

echo 更新完成，正在启动程序...
start """" ""%APP_PATH%""

del ""%SCRIPT_PATH%""
";

                await File.WriteAllTextAsync(scriptPath, scriptContent);

                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process.Start(startInfo);
                _parentForm?.BeginInvoke(new Action(() => _parentForm.Close()));
            }
            else
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            if (downloadForm != null)
            {
                try { downloadForm.Close(); } catch { }
                try { downloadForm.Dispose(); } catch { }
            }

            if (fileStream != null)
            {
                try { await fileStream.DisposeAsync(); } catch { }
            }

            if (tempPath != null && File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }

            MessageBox.Show(
                _parentForm,
                $"自动更新失败:\n{ex.Message}",
                "错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ExecuteManualUpdate(UpdateConfig config)
    {
        try
        {
            var result = MessageBox.Show(
                _parentForm,
                "即将打开蓝奏云下载页面\n请手动下载最新版本并替换当前程序\n点击确定打开链接...",
                "手动更新",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);

            if (result == DialogResult.OK && !string.IsNullOrEmpty(config.LanzouUrl))
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

    private Form CreateWaitForm(string message)
    {
        var form = new Form
        {
            Text = "请稍候",
            Size = new Size(300, 100),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ControlBox = false,
            TopMost = true,
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96, 96)
        };

        var label = new Label
        {
            Text = message,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft Sans Serif", 9)
        };

        form.Controls.Add(label);
        return form;
    }
}

internal static class FormExtensions
{
    public static void SafeClose(this Form? form)
    {
        if (form != null)
        {
            try { form.Close(); } catch { }
            try { form.Dispose(); } catch { }
        }
    }
}