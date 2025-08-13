using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Management; // 添加对 System.Management 的引用
using System.Collections.Immutable; // 可选，用于不可变集合（如果需要）

namespace sca
{
    public partial class Main : Form
    {
        // --- Constants and Static Fields ---
        private static readonly HashSet<string> ProcessesToKillSet = new(StringComparer.OrdinalIgnoreCase)
        {
            "jfglzs", "przs", "zmserv", "vprtt", "oporn",
            "StudentMain", "DispcapHelper", "VRCwPlayer",
            "vncviewer", "tvnserver32", "WfbsPnpInstall",
            "rscheck", "checkrs", "REDAgent"
        };
        private static readonly HashSet<string> ServicesToDisableSet = new(StringComparer.OrdinalIgnoreCase)
        {
            "zmserv", "TDNetFilter", "TDFileFilter", "STUDSRV",
            "BSAgentSvr", "tvnserver", "WFBSMlogon",
            "appcheck2", "checkapp2"
        };
        private const int KillIterations = 2;
        private const int IterationDelayMs = 300;
        private const int ProcessKillTimeoutMs = 500;
        private const int ServiceDisableTimeoutMs = 1500;
        private const int ExplorerRestartTimeoutMs = 5000;

        private static bool? _isAdminCached = null;
        private static readonly object _isAdminLock = new();

        private const string RegCurrentUserBase = @"Software\Microsoft\Windows\CurrentVersion\Policies";
        private const string RegLocalMachineBase = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
        private static readonly (string key, bool isMMC)[] RegUserSubKeys = {
            (@"System", false),
            (@"Explorer", false),
            ("", true)
        };
        private static readonly HashSet<string> ExecutablesToUnrestrictSet = new(StringComparer.OrdinalIgnoreCase)
        {
            "taskkill", "ntsd", "sc", "net", "reg", "cmd", "taskmgr",
            "perfmon", "regedit", "mmc", "dism", "sfc"
        };

        // --- P/Invoke ---
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_TERMINATE = 0x0001;

        // --- Critical System Components ---
        private static readonly HashSet<string> CriticalSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "csrss", "wininit", "winlogon", "lsass", "lsm", "services", "svchost",
            "smss", "dwm", "explorer", "taskeng", "taskhost", "taskhostw",
            "runtimebroker", "fontdrvhost", "conhost", "audiodg", "wlanext",
            "dasHost", "WUDFHost", "sihost", "dllhost", "taskkill", "sc", "net",
            "reg", "cmd", "schtasks", "wscript", "cscript", "powershell", "wmiprvse"
        };

        private static readonly HashSet<string> CriticalSystemServices = new(StringComparer.OrdinalIgnoreCase)
        {
            "RpcSs", "DcomLaunch", "LSM", "Winmgmt", "EventLog", "Themes",
            "AudioSrv", "FontCache", "Schedule", "Winmgmt", "PlugPlay"
        };

        private static readonly string SystemPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
        private static readonly string SystemX86Path = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
        private static readonly string SysWOW64Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64");
        private static readonly HashSet<string> CriticalSystemPathsSet = new(StringComparer.OrdinalIgnoreCase) { SystemPath, SystemX86Path, SysWOW64Path };

        // --- UI Related Fields ---
        private System.Windows.Forms.Timer? topMostTimer;
        private bool isTopMostEnabled = false;
        private DateTime lastSetTime = DateTime.MinValue;

        public Main()
        {
            InitializeComponent();
            this.AutoScaleMode = AutoScaleMode.None;
        }

        private void Form1_Load(object sender, EventArgs e) { }

        // --- 主要功能按钮 ---
        private async void 尝试破解_Click(object sender, EventArgs e)
        {
            Button btn = sender as Button ?? 尝试破解;
            try
            {
                DisableUIAndSetStatus(btn, "正在破解...");
                if (!await EnsureAdminAndConsentAsync())
                {
                    return;
                }
                try
                {
                    await KillpRepeatedAsync();
                    ShowStatusMessage("破解成功！", MessageBoxIcon.Information);
                    await Task.Delay(800);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"破解过程中发生错误: {ex.GetBaseException().Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    btn.Text = "破解失败";
                }
            }
            finally
            {
                EnableUIAndRestoreStatus(btn, "尝试破解");
            }
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            Button btn = sender as Button ?? button1;
            try
            {
                DisableUIAndSetStatus(btn, "正在重启...");
                if (!await EnsureAdminAndConsentAsync()) return;
                try
                {
                    using var process = new Process();
                    process.StartInfo.FileName = "powershell.exe";
                    process.StartInfo.Arguments = "-Command \"Stop-Process -Name explorer -Force; Start-Process explorer\"";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    process.Start();
                    bool exited = process.WaitForExit(ExplorerRestartTimeoutMs);
                    if (exited)
                    {
                        string msg = process.ExitCode == 0 ?
                            "资源管理器已重启！按Win键" :
                            $"重启可能未成功 (Code: {process.ExitCode})。按Win键";
                        MessageBoxIcon icon = process.ExitCode == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning;
                        MessageBox.Show(msg, process.ExitCode == 0 ? "完成" : "提示", MessageBoxButtons.OK, icon);
                    }
                    else
                    {
                        try { process.Kill(); } catch { }
                        MessageBox.Show($"重启命令超时。操作可能仍在进行中。按Win键", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("重启失败: " + ex.GetBaseException().Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                EnableUIAndRestoreStatus(btn, "重启资源管理器");
            }
        }

        private async void button2_Click(object sender, EventArgs e)
        {
            Button btn = sender as Button ?? button2;
            try
            {
                DisableUIAndSetStatus(btn, "正在恢复...");
                if (!await EnsureAdminAndConsentAsync())
                    return;
                try
                {
                    using var currentUserKey = Registry.CurrentUser.OpenSubKey(RegCurrentUserBase, writable: true);
                    using var localMachineKey = Registry.LocalMachine.OpenSubKey(RegLocalMachineBase, writable: true);

                    if (currentUserKey != null)
                    {
                        foreach (var subKeyInfo in RegUserSubKeys)
                        {
                            try
                            {
                                if (subKeyInfo.isMMC)
                                {
                                    currentUserKey.DeleteSubKeyTree("MMC", throwOnMissingSubKey: false);
                                }
                                else if (!string.IsNullOrEmpty(subKeyInfo.key))
                                {
                                    currentUserKey.DeleteSubKeyTree(subKeyInfo.key, throwOnMissingSubKey: false);
                                }
                            }
                            catch
                            {
                                // Log or handle specific registry errors if needed
                            }
                        }
                    }

                    if (localMachineKey != null)
                    {
                        foreach (string exec in ExecutablesToUnrestrictSet)
                        {
                            try
                            {
                                localMachineKey.DeleteSubKeyTree($"{exec}.exe", throwOnMissingSubKey: false);
                            }
                            catch
                            {
                                // Log or handle specific registry errors if needed
                            }
                        }
                    }
                    MessageBox.Show("恢复操作已完成！", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"操作过程中发生错误：\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                EnableUIAndRestoreStatus(btn, "恢复系统限制");
            }
        }

        private async void button3_Click(object sender, EventArgs e)
        {
            Button btn = sender as Button ?? button3;
            try
            {
                DisableUIAndSetStatus(btn, "正在启用...");
                if (!await EnsureAdminAndConsentAsync())
                    return;
                try
                {
                    using var process = new Process();
                    process.StartInfo.FileName = "reg.exe";
                    process.StartInfo.Arguments = "add \"HKLM\\SYSTEM\\CurrentControlSet\\Services\\USBSTOR\" /f /t reg_dword /v Start /d 3";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.Start();
                    process.WaitForExit();
                    if (process.ExitCode == 0)
                    {
                        MessageBox.Show("USB存储设备已启用！", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show($"启用USB存储设备失败 (Code: {process.ExitCode})。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("执行失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                EnableUIAndRestoreStatus(btn, "启用USB存储");
            }
        }

        private async void button4_Click(object sender, EventArgs e)
        {
            Button btn = sender as Button ?? button4;
            try
            {
                DisableUIAndSetStatus(btn, "正在重置...");
                if (!await EnsureAdminAndConsentAsync())
                    return;
                try
                {
                    bool success1 = false, success2 = false;
                    using (var p1 = new Process())
                    {
                        p1.StartInfo.FileName = "reg.exe";
                        p1.StartInfo.Arguments = @"delete ""HKLM\SOFTWARE\Policies\Google\Chrome"" /f";
                        p1.StartInfo.UseShellExecute = false;
                        p1.StartInfo.CreateNoWindow = true;
                        p1.Start();
                        p1.WaitForExit();
                        success1 = p1.ExitCode == 0;
                    }
                    using (var p2 = new Process())
                    {
                        p2.StartInfo.FileName = "reg.exe";
                        p2.StartInfo.Arguments = @"delete ""HKLM\SOFTWARE\Policies\Microsoft\Edge"" /f";
                        p2.StartInfo.UseShellExecute = false;
                        p2.StartInfo.CreateNoWindow = true;
                        p2.Start();
                        p2.WaitForExit();
                        success2 = p2.ExitCode == 0;
                    }
                    if (success1 || success2)
                    {
                        MessageBox.Show("浏览器策略已重置完成！", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show("浏览器策略重置可能未成功。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"重置过程中出现错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                EnableUIAndRestoreStatus(btn, "重置浏览器策略");
            }
        }

        private async void button5_Click(object sender, EventArgs e)
        {
            Button btn = sender as Button ?? button5;
            try
            {
                DisableUIAndSetStatus(btn, "正在阻止...");
                if (!await EnsureAdminAndConsentAsync())
                    return;
                try
                {
                    string? appName = "";
                    DialogResult choice = MessageBox.Show(
                        "选择阻止方式：\n点击'是' - 选择当前运行的进程\n点击'否' - 手动输入程序名称",
                        "选择方式",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Question);

                    if (choice == DialogResult.Yes)
                    {
                        CancellationTokenSource cts = new CancellationTokenSource();
                        try
                        {
                            appName = await Task.Run(() => SelectRunningProcess(), cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // Handle if cancellation was implemented
                        }
                        if (string.IsNullOrEmpty(appName))
                            return;
                    }
                    else if (choice == DialogResult.No)
                    {
                        appName = Microsoft.VisualBasic.Interaction.InputBox(
                            "请输入要阻止的程序名称（不含.exe扩展名）：",
                            "阻止程序",
                            "", -1, -1);
                        if (string.IsNullOrWhiteSpace(appName))
                        {
                            MessageBox.Show("程序名称不能为空！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                    }
                    else
                    {
                        return;
                    }

                    appName = appName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);

                    if (CriticalSystemProcesses.Contains(appName))
                    {
                        MessageBox.Show($"无法阻止关键系统进程 '{appName}'。", "操作被拒绝", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                        return;
                    }

                    bool isInCriticalPath = false;
                    try
                    {
                        foreach (var criticalPath in CriticalSystemPathsSet)
                        {
                            if (File.Exists(Path.Combine(criticalPath, appName + ".exe")))
                            {
                                isInCriticalPath = true;
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log or handle specific file system errors if needed
                    }

                    if (isInCriticalPath)
                    {
                        MessageBox.Show($"无法阻止位于关键系统路径下的程序 '{appName}'。", "操作被拒绝", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                        return;
                    }

                    DialogResult result = MessageBox.Show(
                        $"确定要阻止程序 '{appName}.exe' 运行吗？\n这将使该程序无法正常启动。",
                        "确认阻止",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (result == DialogResult.No)
                        return;

                    using (var key = Registry.LocalMachine.CreateSubKey(
                        $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{appName}.exe", writable: true))
                    {
                        key?.SetValue("Debugger", "nul", RegistryValueKind.String);
                    }
                    MessageBox.Show($"成功阻止程序 '{appName}.exe' 运行！", "操作成功",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (UnauthorizedAccessException)
                {
                    MessageBox.Show("权限不足，无法修改注册表。请以管理员身份运行程序。",
                        "权限错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"操作失败：{ex.Message}", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                EnableUIAndRestoreStatus(btn, "阻止程序运行");
            }
        }

        private async void button6_Click(object sender, EventArgs e)
        {
            Button btn = sender as Button ?? button6;
            try
            {
                DisableUIAndSetStatus(btn, "正在扫描...");
                if (!await EnsureAdminAndConsentAsync())
                    return;
                try
                {
                    CancellationTokenSource cts = new CancellationTokenSource();
                    List<string> blockedPrograms;
                    try
                    {
                        blockedPrograms = await Task.Run(() => ScanBlockedPrograms(), cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    if (blockedPrograms.Count == 0)
                    {
                        MessageBox.Show("没有找到被阻止的程序。", "扫描结果",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    int displayCount = Math.Min(blockedPrograms.Count, 15);
                    var sbMessage = new StringBuilder($"找到 {blockedPrograms.Count} 个被阻止的程序：\n");
                    for (int i = 0; i < displayCount; i++)
                    {
                        sbMessage.AppendLine($"• {blockedPrograms[i]}");
                    }
                    if (blockedPrograms.Count > 15)
                        sbMessage.AppendLine($"\n... 还有 {blockedPrograms.Count - 15} 个程序");
                    sbMessage.AppendLine("\n请选择操作：\n点击'是' - 解除所有阻止\n点击'否' - 选择性解除\n点击'取消' - 取消操作");

                    DialogResult result = MessageBox.Show(sbMessage.ToString(), "扫描完成",
                        MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

                    switch (result)
                    {
                        case DialogResult.Yes:
                            await Task.Run(() => RemoveAllBlocks(blockedPrograms), cts.Token);
                            break;
                        case DialogResult.No:
                            RemoveSelectedBlocks(blockedPrograms);
                            break;
                        case DialogResult.Cancel:
                            return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"操作失败：{ex.Message}", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                EnableUIAndRestoreStatus(btn, "解除程序阻止");
            }
        }

        private void button7_Click(object sender, EventArgs e)
        {
            MessageBox.Show("学生电脑机房管理工具-SCA\n" +
                "版本:1.2.0\n" +
                "如果你觉得这个工具对你有帮助，欢迎在GitHub上给我一个Star⭐，这将极大地鼓励我继续开发更多有用的工具！\n" +
                "GitHub仓库地址:https://github.com/fengliteam/SCA",
                "感谢支持！！！", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void button8_Click(object sender, EventArgs e)
        {
            var updateHandler = new UpdateButtonHandler(this);
            updateHandler.SelectUpdateChannel();
        }

        // --- 辅助方法 ---
        private static async Task<bool> EnsureAdminAndConsentAsync()
        {
            lock (_isAdminLock)
            {
                if (_isAdminCached.HasValue)
                {
                    if (!_isAdminCached.Value)
                    {
                        MessageBox.Show("需要管理员权限才能执行此操作！", "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                }
            }

            if (!_isAdminCached.HasValue || !_isAdminCached.Value)
            {
                if (!IsAdministrator())
                {
                    lock (_isAdminLock) { _isAdminCached = false; }
                    if (RequestAdminPrivileges())
                    {
                        Environment.Exit(0);
                        return false;
                    }
                    else
                    {
                        MessageBox.Show("需要管理员权限才能执行此操作！", "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                }
                else
                {
                    lock (_isAdminLock) { _isAdminCached = true; }
                }
            }

            return MessageBox.Show("请勿使用此程序扰乱课堂纪律, 造成的后果与开发者无关。\n您是否同意继续？", "免责声明", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        private static bool IsAdministrator()
        {
            lock (_isAdminLock)
            {
                if (_isAdminCached.HasValue)
                {
                    return _isAdminCached.Value;
                }
            }

            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new(identity);
                    bool isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
                    lock (_isAdminLock) { _isAdminCached = isAdmin; }
                    return isAdmin;
                }
            }
            catch
            {
                lock (_isAdminLock) { _isAdminCached = false; }
                return false;
            }
        }

        private static bool RequestAdminPrivileges()
        {
            try
            {
                ProcessStartInfo startInfo = new()
                {
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory,
                    FileName = Application.ExecutablePath,
                    Verb = "runas"
                };
                using (Process? newProcess = Process.Start(startInfo))
                {
                    return newProcess != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static async Task KillpRepeatedAsync()
        {
            for (int i = 0; i < KillIterations; i++)
            {
                await KillpAsync();
                if (i < KillIterations - 1)
                    await Task.Delay(IterationDelayMs);
            }
        }

        public static async Task KillpAsync()
        {
            await Task.WhenAll(
                Task.Run(KillProcesses),
                Task.Run(DisableServices)
            );
        }

        // --- 核心功能实现 (重点优化) ---
        private static void KillProcesses()
        {
            // 使用 record struct 作为轻量级数据容器
            List<(int ProcessId, string ProcessName)> processList = new();
            try
            {
                // 使用 WMI 查询进程名和 ID，避免创建大量 Process 对象
                using var searcher = new ManagementObjectSearcher("SELECT ProcessId, Name FROM Win32_Process");
                using var results = searcher.Get();
                foreach (ManagementObject mo in results)
                {
                    try
                    {
                        int pid = Convert.ToInt32(mo["ProcessId"]);
                        string? name = mo["Name"]?.ToString();
                        if (!string.IsNullOrEmpty(name))
                        {
                            // 移除 .exe 后缀以便与 HashSet 匹配
                            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                name = name.Substring(0, name.Length - 4);
                            }
                            processList.Add((pid, name));
                        }
                    }
                    finally
                    {
                        mo?.Dispose(); // 立即释放 WMI 对象
                    }
                }
            }
            catch (Exception ex)
            {
                // 处理 WMI 查询失败的情况，回退到 Process.GetProcesses()
                // MessageBox.Show($"WMI 查询失败: {ex.Message}\n将使用备用方法。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Process[]? allProcessesSnapshot = null;
                try
                {
                    allProcessesSnapshot = Process.GetProcesses();
                    foreach (Process p in allProcessesSnapshot)
                    {
                        try
                        {
                            if (!p.HasExited)
                            {
                                processList.Add((p.Id, p.ProcessName));
                            }
                        }
                        catch { /* 忽略单个进程访问错误 */ }
                    }
                }
                finally
                {
                    if (allProcessesSnapshot != null)
                    {
                        foreach (Process p in allProcessesSnapshot)
                        {
                            try { p?.Dispose(); } catch { /* 忽略 */ }
                        }
                    }
                }
            }

            for (int pass = 0; pass < 2; pass++)
            {
                bool anyKilled = false;
                foreach (var (pid, processName) in processList)
                {
                    if (ProcessesToKillSet.Contains(processName))
                    {
                        if (CriticalSystemProcesses.Contains(processName)) continue;

                        bool shouldKill = true;
                        bool isInCriticalPath = false;

                        // 仅对需要检查路径的进程创建 Process 对象
                        if (NeedsPathCheck(processName))
                        {
                            Process? p = null;
                            try
                            {
                                p = Process.GetProcessById(pid);
                                if (p.HasExited) continue;

                                string? fullPath = p.MainModule?.FileName;
                                if (!string.IsNullOrEmpty(fullPath))
                                {
                                    foreach (var criticalPath in CriticalSystemPathsSet)
                                    {
                                        if (fullPath.StartsWith(criticalPath, StringComparison.OrdinalIgnoreCase))
                                        {
                                            isInCriticalPath = true;
                                            break;
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                // Access denied or other issues, assume not critical for killing attempt
                                // 或者如果获取路径失败，根据策略决定是否跳过
                                // 这里我们选择尝试杀死，但如果需要更严格的检查，可以设 shouldKill = false;
                            }
                            finally
                            {
                                p?.Dispose(); // 立即释放
                            }
                        }

                        if (isInCriticalPath) continue;

                        if (shouldKill)
                        {
                            try
                            {
                                using var p = Process.GetProcessById(pid);
                                p.Kill();
                                bool exited = p.WaitForExit(ProcessKillTimeoutMs);
                                anyKilled = true;
                            }
                            catch
                            {
                                IntPtr hProcess = OpenProcess(PROCESS_TERMINATE, false, pid);
                                if (hProcess != IntPtr.Zero)
                                {
                                    try
                                    {
                                        if (TerminateProcess(hProcess, 0))
                                        {
                                            anyKilled = true;
                                        }
                                    }
                                    finally
                                    {
                                        CloseHandle(hProcess);
                                    }
                                }
                            }
                        }
                    }
                }

                if (!anyKilled) break;
                if (pass == 0)
                {
                    Thread.Sleep(IterationDelayMs / 3);
                }
            }
        }

        // 辅助方法：判断是否需要检查进程的完整路径
        private static bool NeedsPathCheck(string processName)
        {
            // 如果进程名在关键系统进程中，或者你有特定的逻辑需要检查路径，则返回 true
            // 这里简化处理，假设所有非关键进程都可能需要检查路径
            // 或者可以维护一个需要检查路径的进程名 HashSet
            return !CriticalSystemProcesses.Contains(processName);
        }


        private static void DisableServices()
        {
            foreach (string serviceName in ServicesToDisableSet)
            {
                try
                {
                    if (CriticalSystemServices.Contains(serviceName)) continue;

                    using (Process stopProcess = new())
                    {
                        stopProcess.StartInfo.FileName = "sc.exe";
                        stopProcess.StartInfo.Arguments = $"stop \"{serviceName}\"";
                        stopProcess.StartInfo.UseShellExecute = false;
                        stopProcess.StartInfo.CreateNoWindow = true;
                        stopProcess.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                        stopProcess.Start();
                        stopProcess.WaitForExit(ServiceDisableTimeoutMs / 2);
                    }

                    using (Process configProcess = new())
                    {
                        configProcess.StartInfo.FileName = "sc.exe";
                        configProcess.StartInfo.Arguments = $"config \"{serviceName}\" start= disabled";
                        configProcess.StartInfo.UseShellExecute = false;
                        configProcess.StartInfo.CreateNoWindow = true;
                        configProcess.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                        configProcess.Start();
                        configProcess.WaitForExit(ServiceDisableTimeoutMs / 2);
                    }
                }
                catch (Exception ex)
                {
                    // Debug.WriteLine($"Error disabling service {serviceName}: {ex.Message}");
                }
            }
        }

        private string? SelectRunningProcess()
        {
            // 使用 WMI 查询具有主窗口的进程
            var uniqueNamesSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nameList = new List<string>();

            try
            {
                // WMI 查询所有进程
                using var searcher = new ManagementObjectSearcher("SELECT ProcessId, Name FROM Win32_Process");
                using var results = searcher.Get();

                foreach (ManagementObject mo in results)
                {
                    try
                    {
                        int pid = Convert.ToInt32(mo["ProcessId"]);
                        string? name = mo["Name"]?.ToString();

                        if (!string.IsNullOrEmpty(name))
                        {
                            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                name = name.Substring(0, name.Length - 4);
                            }

                            // 检查进程是否有主窗口（这是一个近似检查）
                            // 更精确的方法是使用 EnumWindows 和 GetWindowThreadProcessId，但这会更复杂
                            // 这里我们使用一个简单的启发式方法：检查 MainWindowTitle 是否为空
                            // 但这需要创建 Process 对象。为了内存优化，我们简化处理。
                            // 或者，可以查询 Win32_Process 的其他属性，但这通常不直接提供主窗口信息。

                            // 简化处理：假设所有非系统进程都可能有UI，或者只显示列表让用户选择。
                            // 这里我们只根据名称去重并添加到列表
                            if (uniqueNamesSet.Add(name))
                            {
                                nameList.Add(name);
                            }
                        }
                    }
                    finally
                    {
                        mo?.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                // WMI 失败，回退到旧方法
                // MessageBox.Show($"WMI 查询失败: {ex.Message}\n将使用备用方法。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Process[]? allProcesses = null;
                try
                {
                    allProcesses = Process.GetProcesses();
                    foreach (Process p in allProcesses)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(p.ProcessName) && p.MainWindowHandle != IntPtr.Zero)
                            {
                                if (uniqueNamesSet.Add(p.ProcessName))
                                {
                                    nameList.Add(p.ProcessName);
                                }
                            }
                        }
                        finally
                        {
                            try { p?.Dispose(); } catch { }
                        }
                    }
                }
                finally
                {
                    if (allProcesses != null)
                    {
                        foreach (Process p in allProcesses)
                        {
                            try { p?.Dispose(); } catch { }
                        }
                    }
                }
            }

            if (nameList.Count == 0)
            {
                MessageBox.Show("没有找到正在运行的进程。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            nameList.Sort(StringComparer.OrdinalIgnoreCase);
            int displayCount = Math.Min(nameList.Count, 15);
            var sb = new StringBuilder("请输入要阻止的进程名称，或从以下列表中选择：\n");
            for (int i = 0; i < displayCount; i++)
            {
                sb.AppendLine(nameList[i]);
            }
            string selectedProcess = Microsoft.VisualBasic.Interaction.InputBox(
                sb.ToString(),
                "选择进程",
                "", -1, -1);
            return selectedProcess;
        }

        private List<string> ScanBlockedPrograms()
        {
            var blockedPrograms = new List<string>();
            try
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options", writable: false);
                if (baseKey != null)
                {
                    string[] subKeyNames = baseKey.GetSubKeyNames();
                    foreach (string subKeyName in subKeyNames)
                    {
                        try
                        {
                            using var subKey = baseKey.OpenSubKey(subKeyName, writable: false);
                            if (subKey != null)
                            {
                                if (subKey.GetValue("Debugger") is string strVal && strVal.Equals("nul", StringComparison.OrdinalIgnoreCase))
                                {
                                    string programName = subKeyName;
                                    if (programName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                    {
                                        programName = programName.Substring(0, programName.Length - 4);
                                    }
                                    blockedPrograms.Add(programName);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Debug.WriteLine($"Error accessing subkey {subKeyName}: {ex.Message}");
                            continue;
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                throw new Exception("权限不足，无法访问注册表。请确保以管理员身份运行。");
            }
            catch (Exception ex)
            {
                throw new Exception($"扫描注册表时出错：{ex.Message}");
            }

            blockedPrograms.Sort(StringComparer.OrdinalIgnoreCase);
            return blockedPrograms;
        }

        private void RemoveAllBlocks(List<string> blockedPrograms)
        {
            try
            {
                int successCount = 0;
                int failCount = 0;
                foreach (string program in blockedPrograms)
                {
                    try
                    {
                        RemoveBlockForProgram(program);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        // Debug.WriteLine($"Failed to remove block for {program}: {ex.Message}");
                        failCount++;
                    }
                }
                string resultMessage = $"成功解除 {successCount} 个程序的阻止。";
                if (failCount > 0)
                    resultMessage += $"\n{failCount} 个程序解除失败。";
                MessageBox.Show(resultMessage, "操作完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"批量解除操作失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RemoveSelectedBlocks(List<string> blockedPrograms)
        {
            try
            {
                int displayCount = Math.Min(blockedPrograms.Count, 15);
                var sbList = new StringBuilder("请输入要解除阻止的程序序号（多个序号用逗号分隔）：\n");
                for (int i = 0; i < displayCount; i++)
                {
                    sbList.AppendLine($"{i + 1}. {blockedPrograms[i]}");
                }
                string input = Microsoft.VisualBasic.Interaction.InputBox(
                    sbList.ToString(),
                    "选择性解除",
                    "", -1, -1);
                if (string.IsNullOrWhiteSpace(input))
                    return;

                var selectedIndices = new HashSet<int>();
                string[] parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (string part in parts)
                {
                    if (int.TryParse(part, out int index) && index > 0 && index <= blockedPrograms.Count)
                    {
                        selectedIndices.Add(index - 1);
                    }
                }

                if (selectedIndices.Count == 0)
                {
                    MessageBox.Show("没有选择有效的程序。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string confirmMsg = $"确定要解除 {selectedIndices.Count} 个程序的阻止吗？";
                DialogResult confirm = MessageBox.Show(confirmMsg, "确认解除", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (confirm == DialogResult.No)
                    return;

                int successCount = 0;
                int failCount = 0;
                foreach (int index in selectedIndices)
                {
                    try
                    {
                        RemoveBlockForProgram(blockedPrograms[index]);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        // Debug.WriteLine($"Failed to remove block for {blockedPrograms[index]}: {ex.Message}");
                        failCount++;
                    }
                }
                string resultMessage = $"成功解除 {successCount} 个程序的阻止。";
                if (failCount > 0)
                    resultMessage += $"\n{failCount} 个程序解除失败。";
                MessageBox.Show(resultMessage, "操作完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"选择性解除操作失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void RemoveBlockForProgram(string programName)
        {
            string fullProgramName = programName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? programName : $"{programName}.exe";
            try
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options", writable: true);
                if (baseKey != null)
                {
                    using var subKey = baseKey.OpenSubKey(fullProgramName, writable: true);
                    if (subKey != null)
                    {
                        if (subKey.GetValue("Debugger") is string strVal && strVal.Equals("nul", StringComparison.OrdinalIgnoreCase))
                        {
                            subKey.DeleteValue("Debugger");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"解除程序 {programName} 失败：{ex.Message}", ex);
            }
        }

        // --- UI Helper Methods ---
        private void DisableUIAndSetStatus(Button button, string statusText)
        {
            if (button.InvokeRequired)
            {
                button.Invoke((MethodInvoker)delegate { DisableUIAndSetStatus(button, statusText); });
                return;
            }
            button.Enabled = false;
            button.Text = statusText;
        }

        private void EnableUIAndRestoreStatus(Button button, string originalText)
        {
            if (button.InvokeRequired)
            {
                button.Invoke((MethodInvoker)delegate { EnableUIAndRestoreStatus(button, originalText); });
                return;
            }
            button.Enabled = true;
            button.Text = originalText;
        }

        private void ShowStatusMessage(string message, MessageBoxIcon icon)
        {
            MessageBox.Show(message, "状态", MessageBoxButtons.OK, icon);
        }

        // --- 窗口置顶相关 ---
        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            isTopMostEnabled = checkBox1.Checked;
            if (isTopMostEnabled)
            {
                SetTopMost();
                topMostTimer ??= new System.Windows.Forms.Timer();
                topMostTimer.Interval = 200;
                topMostTimer.Tick += TopMostTimer_Tick;
                topMostTimer.Start();
            }
            else
            {
                topMostTimer?.Stop();
                if (this.InvokeRequired)
                {
                    this.Invoke((MethodInvoker)delegate { this.TopMost = false; });
                }
                else
                {
                    this.TopMost = false;
                }
            }
        }

        private void TopMostTimer_Tick(object? sender, EventArgs e)
        {
            if (isTopMostEnabled)
            {
                if ((DateTime.Now - lastSetTime).TotalMilliseconds > 100)
                {
                    SetTopMost();
                }
            }
        }

        private void SetTopMost()
        {
            try
            {
                if (this.InvokeRequired)
                {
                    this.Invoke((MethodInvoker)SetTopMost);
                    return;
                }
                SetWindowPos(this.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0010);
                this.TopMost = true;
                SetForegroundWindow(this.Handle);
                lastSetTime = DateTime.Now;
            }
            catch
            {
                // Handle potential errors in setting window position
            }
        }

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        private void label1_Click(object sender, EventArgs e)
        {
            MessageBox.Show("点击神马呢？");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (components != null)
                {
                    components.Dispose();
                }
                // 正确释放 Timer
                if (topMostTimer != null)
                {
                    topMostTimer.Tick -= TopMostTimer_Tick;
                    topMostTimer.Dispose();
                    topMostTimer = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}