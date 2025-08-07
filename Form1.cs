using System;
using System.Diagnostics;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Security.Principal;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Linq;
using System.IO;

namespace sca
{
    public partial class Main : Form
    {
        // --- 原有代码常量和变量 ---
        private static readonly string[] ProcessesToKill = {
            "jfglzs", "przs", "zmserv", "vprtt", "oporn",
            "StudentMain", "DispcapHelper", "VRCwPlayer",
            "vncviewer", "tvnserver32", "WfbsPnpInstall",
            "rscheck", "checkrs", "REDAgent"
        };
        private static readonly string[] ServicesToDisable = {
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
        private static readonly object _isAdminLock = new object();
        private const string RegCurrentUserBase = @"Software\Microsoft\Windows\CurrentVersion\Policies";
        private const string RegLocalMachineBase = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
        private static readonly (string key, bool isMMC)[] RegUserSubKeys = {
            (@"System", false),
            (@"System", false),
            (@"Explorer", false),
            ("", true)
        };
        private static readonly string[] ExecutablesToUnrestrict = {
            "taskkill", "ntsd", "sc", "net", "reg", "cmd", "taskmgr",
            "perfmon", "regedit", "mmc", "dism", "sfc"
        };
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);
        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);
        private const uint PROCESS_TERMINATE = 0x0001;

        // --- 新增安全相关代码 ---
        // 定义一组关键系统进程，这些进程不应被终止
        private static readonly HashSet<string> CriticalSystemProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "csrss", "wininit", "winlogon", "lsass", "lsm", "services", "svchost",
            "smss", "dwm", "explorer", "taskeng", "taskhost", "taskhostw",
            "runtimebroker", "fontdrvhost", "conhost", "audiodg", "wlanext",
            "dasHost", "WUDFHost", "sihost", "dllhost", "taskkill", "sc", "net",
            "reg", "cmd", "schtasks", "wscript", "cscript", "powershell", "wmiprvse"
            // 可以根据需要添加更多
        };

        // 定义一组关键系统服务，这些服务不应被禁用
        private static readonly HashSet<string> CriticalSystemServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RpcSs", "DcomLaunch", "LSM", "Winmgmt", "EventLog", "Themes",
            "AudioSrv", "FontCache", "Schedule", "Winmgmt", "PlugPlay"
            // 可以根据需要添加更多
        };

        // 定义一组关键系统文件夹，这些路径下的程序不应被阻止
        private static readonly string[] CriticalSystemPaths = {
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64")
        };

        public Main()
        {
            InitializeComponent();
            this.AutoScaleMode = AutoScaleMode.None;
        }

        private void Form1_Load(object sender, EventArgs e) { }

        private async void 尝试破解_Click(object sender, EventArgs e)
        {
            if (!await EnsureAdminAndConsentAsync())
            {
                return;
            }
            尝试破解.Enabled = false;
            string originalText = 尝试破解.Text;
            尝试破解.Text = "正在破解...";
            try
            {
                await Task.Run(KillpRepeated);
                尝试破解.Text = "破解成功！";
                await Task.Delay(800);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"破解过程中发生错误: {ex.GetBaseException().Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                尝试破解.Text = "破解失败";
            }
            finally
            {
                尝试破解.Enabled = true;
            }
        }

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
                    goto UserConsent;
                }
            }
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
        UserConsent:
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
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
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
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory,
                    FileName = Application.ExecutablePath,
                    Verb = "runas"
                };
                using (Process newProcess = Process.Start(startInfo))
                {
                    return newProcess != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void KillpRepeated()
        {
            for (int i = 0; i < KillIterations; i++)
            {
                Killp();
                if (i < KillIterations - 1) Thread.Sleep(IterationDelayMs);
            }
        }

        public static void Killp()
        {
            KillProcesses();
            DisableServices();
        }

        // --- 修改后的 KillProcesses 方法 ---
        private static void KillProcesses()
        {
            // 1. 先获取所有进程的列表和签名信息（可选优化，此处简化）
            //    为了性能，我们只在需要时检查签名。

            Span<bool> needsKilling = stackalloc bool[ProcessesToKill.Length];
            for (int i = 0; i < ProcessesToKill.Length; i++)
            {
                needsKilling[i] = true;
            }
            int remainingToKill = ProcessesToKill.Length;

            for (int iteration = 0; iteration < 2 && remainingToKill > 0; iteration++)
            {
                for (int i = ProcessesToKill.Length - 1; i >= 0; i--)
                {
                    if (!needsKilling[i]) continue;
                    string processName = ProcessesToKill[i];

                    Process[] processes = null;
                    try
                    {
                        processes = Process.GetProcessesByName(processName);
                        if (processes.Length > 0)
                        {
                            bool allKilled = true;
                            foreach (Process p in processes)
                            {
                                if (p != null && !p.HasExited)
                                {
                                    try
                                    {
                                        // --- 安全检查 1: 检查是否为关键系统进程 ---
                                        if (CriticalSystemProcesses.Contains(p.ProcessName))
                                        {
                                            // 跳过关键进程
                                            allKilled = false;
                                            continue;
                                        }

                                        // --- 安全检查 2: 检查文件路径和签名 (简化版) ---
                                        // 注意：完整的签名检查比较复杂且耗时，这里只做路径检查作为示例
                                        try
                                        {
                                            string fullPath = p.MainModule?.FileName;
                                            if (!string.IsNullOrEmpty(fullPath))
                                            {
                                                // 检查是否在系统关键目录
                                                bool isInCriticalPath = false;
                                                foreach (var criticalPath in CriticalSystemPaths)
                                                {
                                                    if (fullPath.StartsWith(criticalPath, StringComparison.OrdinalIgnoreCase))
                                                    {
                                                        isInCriticalPath = true;
                                                        break;
                                                    }
                                                }
                                                if (isInCriticalPath)
                                                {
                                                    // 跳过位于关键系统路径的进程
                                                    allKilled = false;
                                                    continue;
                                                }
                                            }
                                        }
                                        catch
                                        {
                                            // 获取主模块信息失败，可能没有权限，谨慎处理
                                            // 可以选择跳过或记录日志
                                        }

                                        // 如果通过了安全检查，则尝试终止
                                        IntPtr hProcess = OpenProcess(PROCESS_TERMINATE, false, p.Id);
                                        if (hProcess != IntPtr.Zero)
                                        {
                                            if (!TerminateProcess(hProcess, 0))
                                            {
                                                allKilled = false;
                                                p.Kill(); // Fallback
                                            }
                                            CloseHandle(hProcess);
                                        }
                                        else
                                        {
                                            allKilled = false;
                                            p.Kill(); // Fallback
                                        }
                                    }
                                    catch
                                    {
                                        // 终止失败
                                        allKilled = false;
                                    }
                                    finally
                                    {
                                        p?.Dispose();
                                    }
                                }
                                else
                                {
                                    p?.Dispose();
                                }
                            }
                            if (allKilled)
                            {
                                needsKilling[i] = false;
                                remainingToKill--;
                            }
                        }
                        else
                        {
                            needsKilling[i] = false;
                            remainingToKill--;
                        }
                    }
                    catch
                    {
                        needsKilling[i] = false;
                        remainingToKill--;
                    }
                    finally
                    {
                        if (processes != null)
                        {
                            foreach (Process p in processes)
                            {
                                p?.Dispose();
                            }
                        }
                    }
                }
                if (remainingToKill > 0 && iteration < 1)
                {
                    Thread.Sleep(IterationDelayMs / 3);
                }
            }
        }

        // --- 修改后的 DisableServices 方法 ---
        private static void DisableServices()
        {
            foreach (string serviceName in ServicesToDisable)
            {
                try
                {
                    // --- 安全检查: 检查是否为关键系统服务 ---
                    if (CriticalSystemServices.Contains(serviceName))
                    {
                        // 跳过关键服务
                        continue;
                    }

                    // 使用更安全、更明确的命令调用方式，避免 && 等连接符
                    // 停止服务
                    using (Process stopProcess = new Process())
                    {
                        stopProcess.StartInfo.FileName = "sc.exe"; // 直接调用 sc.exe
                        stopProcess.StartInfo.Arguments = $"stop \"{serviceName}\""; // 参数用引号包裹
                        stopProcess.StartInfo.UseShellExecute = false;
                        stopProcess.StartInfo.CreateNoWindow = true;
                        stopProcess.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                        stopProcess.Start();
                        stopProcess.WaitForExit(ServiceDisableTimeoutMs / 2); // 分配一半时间
                    }

                    // 禁用服务
                    using (Process configProcess = new Process())
                    {
                        configProcess.StartInfo.FileName = "sc.exe";
                        configProcess.StartInfo.Arguments = $"config \"{serviceName}\" start= disabled";
                        configProcess.StartInfo.UseShellExecute = false;
                        configProcess.StartInfo.CreateNoWindow = true;
                        configProcess.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                        configProcess.Start();
                        configProcess.WaitForExit(ServiceDisableTimeoutMs / 2); // 分配另一半时间
                    }

                    // 注意：即使服务不存在或无法停止/禁用，这些命令通常也会静默失败，
                    // 因为我们重定向了输出并且没有检查 ExitCode。
                    // 如果需要更精确的控制，可以检查 ExitCode。
                }
                catch
                {
                    // 忽略单个服务操作的异常
                }
            }
        }

        // --- 其他原有方法 (button1_Click, button2_Click 等) ---
        // ... (保持不变)

        private async void button1_Click(object sender, EventArgs e)
        {
            if (!await EnsureAdminAndConsentAsync()) return;
            try
            {
                using (Process process = new Process())
                {
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
            }
            catch (Exception ex)
            {
                MessageBox.Show("重启失败: " + ex.GetBaseException().Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void button2_Click(object sender, EventArgs e)
        {
            if (!await EnsureAdminAndConsentAsync())
                return;
            button2.Enabled = false;
            string originalText = button2.Text;
            button2.Text = "正在恢复...";
            try
            {
                using (var currentUserKey = Registry.CurrentUser.OpenSubKey(RegCurrentUserBase, writable: true))
                using (var localMachineKey = Registry.LocalMachine.OpenSubKey(RegLocalMachineBase, writable: true))
                {
                    if (currentUserKey != null)
                    {
                        foreach (var subKeyInfo in RegUserSubKeys)
                        {
                            try
                            {
                                if (subKeyInfo.isMMC)
                                {
                                    currentUserKey.DeleteSubKeyTree("MMC", false);
                                }
                                else if (!string.IsNullOrEmpty(subKeyInfo.key))
                                {
                                    currentUserKey.DeleteSubKeyTree(subKeyInfo.key, false);
                                }
                            }
                            catch
                            {
                            }
                        }
                    }
                    if (localMachineKey != null)
                    {
                        foreach (string exec in ExecutablesToUnrestrict)
                        {
                            try
                            {
                                localMachineKey.DeleteSubKeyTree($"{exec}.exe", false);
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                MessageBox.Show("恢复操作已完成！", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"操作过程中发生错误：\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                button2.Enabled = true;
                button2.Text = originalText;
            }
        }

        private async void button3_Click(object sender, EventArgs e)
        {
            if (!await EnsureAdminAndConsentAsync())
                return;
            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo.FileName = "reg.exe";
                    process.StartInfo.Arguments = "add \"HKLM\\SYSTEM\\CurrentControlSet\\Services\\USBSTOR\" /f /t reg_dword /v Start /d 3";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.Start();
                    process.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("执行失败: " + ex.Message);
            }
        }

        private async void button4_Click(object sender, EventArgs e)
        {
            if (!await EnsureAdminAndConsentAsync())
                return;
            try
            {
                using (Process p1 = new Process())
                {
                    p1.StartInfo.FileName = "reg.exe";
                    p1.StartInfo.Arguments = @"delete ""HKLM\SOFTWARE\Policies\Google\Chrome"" /f";
                    p1.StartInfo.UseShellExecute = false;
                    p1.StartInfo.CreateNoWindow = true;
                    p1.Start();
                    p1.WaitForExit();
                }
                using (Process p2 = new Process())
                {
                    p2.StartInfo.FileName = "reg.exe";
                    p2.StartInfo.Arguments = @"delete ""HKLM\SOFTWARE\Policies\Microsoft\Edge"" /f";
                    p2.StartInfo.UseShellExecute = false;
                    p2.StartInfo.CreateNoWindow = true;
                    p2.Start();
                    p2.WaitForExit();
                }
                MessageBox.Show("浏览器策略已重置完成！", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"重置过程中出现错误：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void button5_Click(object sender, EventArgs e)
        {
            if (!await EnsureAdminAndConsentAsync())
                return;
            try
            {
                DialogResult choice = MessageBox.Show(
                    "选择阻止方式：\n点击'是' - 选择当前运行的进程\n点击'否' - 手动输入程序名称",
                    "选择方式",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                string appName = "";
                if (choice == DialogResult.Yes)
                {
                    appName = SelectRunningProcess();
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

                // --- 新增安全检查: 阻止关键系统进程 ---
                if (CriticalSystemProcesses.Contains(appName))
                {
                    MessageBox.Show($"无法阻止关键系统进程 '{appName}'。", "操作被拒绝", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                    return;
                }
                // --- 新增安全检查: 阻止关键系统路径下的程序 ---
                try
                {
                    // 尝试获取程序路径（需要更复杂的逻辑，这里简化）
                    // 一个简单的方法是检查常见系统路径是否包含该名称
                    bool isInCriticalPath = false;
                    foreach (var criticalPath in CriticalSystemPaths)
                    {
                        // 这里只是一个非常基础的检查，实际应用中可能需要更精确的方法（如搜索PATH）
                        if (File.Exists(Path.Combine(criticalPath, appName + ".exe")))
                        {
                            isInCriticalPath = true;
                            break;
                        }
                    }
                    if (isInCriticalPath)
                    {
                        MessageBox.Show($"无法阻止位于关键系统路径下的程序 '{appName}'。", "操作被拒绝", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                        return;
                    }
                }
                catch
                {
                    // 忽略检查失败
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

        private string SelectRunningProcess()
        {
            try
            {
                Process[] allProcesses = Process.GetProcesses();
                List<string> processNames = new List<string>(allProcesses.Length / 4);
                HashSet<string> uniqueNames = new HashSet<string>();
                foreach (Process p in allProcesses)
                {
                    if (!string.IsNullOrEmpty(p.ProcessName) && p.MainWindowHandle != IntPtr.Zero)
                    {
                        if (uniqueNames.Add(p.ProcessName))
                        {
                            processNames.Add(p.ProcessName);
                        }
                    }
                    p?.Dispose();
                }
                if (processNames.Count == 0)
                {
                    MessageBox.Show("没有找到正在运行的进程。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return null;
                }
                processNames.Sort();
                string listToShow = string.Join("\n", processNames.Take(15));
                string selectedProcess = Microsoft.VisualBasic.Interaction.InputBox(
                    "请输入要阻止的进程名称，或从以下列表中选择：\n" + listToShow,
                    "选择进程",
                    "", -1, -1);
                return selectedProcess;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"获取进程列表失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        private async void button6_Click(object sender, EventArgs e)
        {
            if (!await EnsureAdminAndConsentAsync())
                return;
            try
            {
                var blockedPrograms = ScanBlockedPrograms();
                if (blockedPrograms.Count == 0)
                {
                    MessageBox.Show("没有找到被阻止的程序。", "扫描结果",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                string message = $"找到 {blockedPrograms.Count} 个被阻止的程序：\n";
                int displayCount = Math.Min(blockedPrograms.Count, 15);
                for (int i = 0; i < displayCount; i++)
                {
                    message += $"• {blockedPrograms[i]}\n";
                }
                if (blockedPrograms.Count > 15)
                    message += $"\n... 还有 {blockedPrograms.Count - 15} 个程序";
                message += "\n请选择操作：\n点击'是' - 解除所有阻止\n点击'否' - 选择性解除\n点击'取消' - 取消操作";
                DialogResult result = MessageBox.Show(message, "扫描完成",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                switch (result)
                {
                    case DialogResult.Yes:
                        RemoveAllBlocks(blockedPrograms);
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

        private List<string> ScanBlockedPrograms()
        {
            var blockedPrograms = new List<string>();
            try
            {
                using (var baseKey = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options", writable: false))
                {
                    if (baseKey != null)
                    {
                        string[] subKeyNames = baseKey.GetSubKeyNames();
                        foreach (string subKeyName in subKeyNames)
                        {
                            try
                            {
                                using (var subKey = baseKey.OpenSubKey(subKeyName, writable: false))
                                {
                                    if (subKey != null)
                                    {
                                        var debuggerValue = subKey.GetValue("Debugger");
                                        if (debuggerValue is string strVal && strVal.Equals("nul", StringComparison.OrdinalIgnoreCase))
                                        {
                                            string programName = subKeyName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ?
                                                subKeyName.Substring(0, subKeyName.Length - 4) : subKeyName;
                                            blockedPrograms.Add(programName);
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                continue;
                            }
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
            blockedPrograms.Sort();
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
                    catch
                    {
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
                string programList = "";
                int displayCount = Math.Min(blockedPrograms.Count, 15);
                for (int i = 0; i < displayCount; i++)
                {
                    programList += $"{i + 1}. {blockedPrograms[i]}\n";
                }
                string input = Microsoft.VisualBasic.Interaction.InputBox(
                    "请输入要解除阻止的程序序号（多个序号用逗号分隔）：\n" + programList,
                    "选择性解除",
                    "", -1, -1);
                if (string.IsNullOrWhiteSpace(input))
                    return;
                var selectedIndices = new List<int>();
                string[] parts = input.Split(',');
                foreach (string part in parts)
                {
                    if (int.TryParse(part.Trim(), out int index) && index > 0 && index <= blockedPrograms.Count)
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
                    catch
                    {
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

        private void RemoveBlockForProgram(string programName)
        {
            string fullProgramName = programName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? programName : $"{programName}.exe";
            string keyPath = $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{fullProgramName}";
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(keyPath, writable: true))
                {
                    if (key != null)
                    {
                        var debuggerValue = key.GetValue("Debugger");
                        if (debuggerValue is string strVal && strVal.Equals("nul", StringComparison.OrdinalIgnoreCase))
                        {
                            key.DeleteValue("Debugger");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"解除程序 {programName} 失败：{ex.Message}");
            }
        }

        private void button7_Click(object sender, EventArgs e)
        {
            //消息提示框
            MessageBox.Show("学生电脑机房管理工具-SCA\n" +
                "版本:1.0.7\n" +
                "如果你觉得这个工具对你有帮助，欢迎在GitHub上给我一个Star⭐，这将极大地鼓励我继续开发更多有用的工具！\n\nGitHub仓库地址:https://github.com/fengliteam/SCA",
                "感谢支持", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}