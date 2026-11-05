// 大肥鱼（Aurabot）一键安装器（窗口版）
// 流程：检测 DSH 桌面版（默认位置 → 卸载记录 → 正在运行的进程 → 手动选择）→ 没装就优先使用同目录安装包，否则从官网下载，校验签名后安装
//       → 桌面版没打开过就先打开一次生成 profile → 退出 DSH，用 dsh.cmd 安装/升级 Aurabot 插件 → 重新打开 DSH。
// SnowLuma 网关受其 EULA 5.4 约束（禁止通过自动化脚本部署），本程序不下载、不安装它，只提供下载页和教程入口。
// 测试用参数：--dsh <dsh.cmd 路径>   --dsh-url <DSH 安装包地址>

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Config
{
    public const string Title = "大肥鱼一键安装";
    public const string PluginSpec = "github:accomplish07zrh-eng/dsh-aurabot-qq";
    public const string Profile = "desktop";
    public const string RepoUrl = "https://github.com/accomplish07zrh-eng/dsh-aurabot-qq";
    public const string SnowLumaUrl = "https://github.com/SnowLuma/SnowLuma/releases";
    public const string DshPageUrl = "https://www.deepseek.com/en/harness/";
    public const string DshInstallerUrl = "https://download.deepseek.com/desktop/dsh-latest-windows-x64.exe";
    public const string DshInstallerFile = "dsh-latest-windows-x64.exe";
    public const string ExpectedSigner = "Hangzhou DeepSeek Artificial Intelligence Co., Ltd.";
    public const string TutorialFile = "大肥鱼安装教程.html";
}

static class Program
{
    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main(string[] args)
    {
        try { SetProcessDPIAware(); } catch { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string dsh = null, url = null;
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--dsh") dsh = args[++i];
            else if (args[i] == "--dsh-url") url = args[++i];
        }
        Application.Run(new InstallerForm(dsh, url ?? Config.DshInstallerUrl));
    }
}
enum StepState { Pending, Running, Done, Failed, Skipped }

partial class InstallerForm
{
    bool RunSteps(out string failMessage)
    {
        failMessage = null;

        // 第 1 步：检测 DSH
        currentStep = 0;
        SetStep(0, StepState.Running, "正在查找...");
        SetStatus("正在检测 DeepSeek Harness...", null);
        string dsh = Dsh.Find(dshOverride, Log);
        if (dsh == null && Ask("没有在常见位置找到 DeepSeek Harness（DSH）。\r\n\r\n" +
                               "如果你已经装过、只是装在了别的文件夹，点「是」手动选择 DeepSeek Harness.exe。\r\n" +
                               "没装过就点「否」，接下来优先使用同目录安装包，没有才从官网下载。"))
            dsh = PickDsh();
        if (dsh != null)
        {
            SetStep(0, StepState.Done, "已安装：" + Dsh.InstallRoot(dsh));
            SetStep(1, StepState.Skipped, "已经装过 DSH，跳过");
        }
        else
        {
            SetStep(0, StepState.Done, "本机没有安装 DSH");

            // 第 2 步：优先使用安装器同目录的安装包；没有时才询问并联网下载
            currentStep = 1;
            SetStep(1, StepState.Running, "正在查找同目录的 DSH 安装包...");
            bool localPackage;
            string setup = DshPackage.Resolve(AppDomain.CurrentDomain.BaseDirectory, delegate
            {
                Log("同目录没有找到 DSH 安装包，需要从官网下载。");
                if (!Ask("本机还没有安装 DeepSeek Harness（DSH）桌面版，安装器同目录也没有 DSH 安装包。\r\n\r\n" +
                         "现在从 DeepSeek 官网下载并安装吗？（约 280 MB）\r\n" + dshUrl))
                    return null;
                return DownloadDsh();
            }, out localPackage);
            if (setup == null)
            {
                SetStep(1, StepState.Failed, "已取消。可以把 DSH 安装包放到本程序同目录后重试，或自己先安装 DSH");
                failMessage = "需要先安装 DSH 才能继续。";
                return false;
            }
            if (localPackage)
            {
                SetStatus("已找到同目录的 DSH 安装包，跳过下载", null);
                Log("使用同目录的安装包：" + setup);
            }
            VerifySignature(setup, !localPackage);
            dsh = InstallDsh(setup);
            if (dsh == null)
            {
                SetStep(1, StepState.Failed, "安装后仍没找到 DSH，请确认安装程序已完成");
                failMessage = "DSH 没有装好，可以重试，或手动运行安装包：" + setup;
                return false;
            }
            SetStep(1, StepState.Done, "已安装：" + Dsh.InstallRoot(dsh));
        }

        // 第 3 步：安装插件
        // dsh plugin --profile desktop 要求桌面版至少打开过一次（生成 profile），并且安装时 DSH 已完全退出
        currentStep = 2;
        if (!PrepareProfile(dsh, out failMessage)) return false;
        SetStep(2, StepState.Running, "正在从 GitHub 下载插件，请耐心等待...");
        SetStatus("正在安装大肥鱼插件...", null);
        Marquee(true);
        Log("执行：dsh plugin --profile " + Config.Profile + " add " + Config.PluginSpec);
        int exit = Dsh.Run(dsh, "plugin --profile " + Config.Profile + " add " + Config.PluginSpec, Log);
        Marquee(false);
        if (exit != 0)
        {
            SetStep(2, StepState.Failed, "安装失败（退出码 " + exit + "），详见下方日志");
            Log("常见原因：连不上 GitHub（开加速器后重试）；DSH 版本太旧；DSH 没有完全退出（右下角托盘里也要退出）。");
            Log("仍解决不了，请把日志发到 QQ 群 554850395。");
            failMessage = "插件安装失败，请看日志后点「重试」。";
            return false;
        }
        SetStep(2, StepState.Done, "已安装（重复运行即升级，设置和记忆不会丢）");

        // 第 4 步：重启 DSH
        currentStep = 3;
        SetStep(3, StepState.Running, null);
        SetStep(3, RestartDsh(dsh) ? StepState.Done : StepState.Skipped, null);
        return true;
    }
    // ---------- 下载与安装 DSH ----------

    string DownloadDsh()
    {
        SetStep(1, StepState.Running, "正在下载...");
        SetStatus("正在从 DeepSeek 官网下载 DSH 桌面版...", null);
        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
        string dir = Path.Combine(Path.GetTempPath(), "dafeiyu-installer");
        Directory.CreateDirectory(dir);
        string target = Path.Combine(dir, Config.DshInstallerFile);
        string part = target + ".part";

        var req = (HttpWebRequest)WebRequest.Create(dshUrl);
        req.UserAgent = "DafeiyuInstaller/1.0";
        req.Timeout = 30000;
        req.ReadWriteTimeout = 60000;
        using (var resp = (HttpWebResponse)req.GetResponse())
        {
            long total = resp.ContentLength;
            if (total > 0 && File.Exists(target) && new FileInfo(target).Length == total)
            {
                req.Abort();
                Log("使用已下载的安装包：" + target);
                return target;
            }
            Log("下载：" + dshUrl + "（" + Mb(total) + "）");
            using (var src = resp.GetResponseStream())
            using (var dst = File.Create(part))
            {
                var buf = new byte[81920];
                long done = 0;
                int n, lastPct = -1;
                var watch = Stopwatch.StartNew();
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    if (cancel) throw new OperationCanceledException();
                    dst.Write(buf, 0, n);
                    done += n;
                    int pct = total > 0 ? (int)(done * 100 / total) : 0;
                    if (pct != lastPct)
                    {
                        lastPct = pct;
                        double speed = done / Math.Max(watch.Elapsed.TotalSeconds, 0.1);
                        SetProgress(pct);
                        SetStep(1, StepState.Running, "正在下载 " + Mb(done) + " / " + Mb(total) + "　" + Mb((long)speed) + "/s");
                    }
                }
                if (total > 0 && done != total) throw new IOException("下载不完整，请检查网络后重试。");
            }
        }
        if (File.Exists(target)) File.Delete(target);
        File.Move(part, target);
        Log("下载完成：" + target);
        return target;
    }
    void VerifySignature(string setup, bool downloaded)
    {
        SetStep(1, StepState.Running, "正在校验安装包的数字签名...");
        DshPackage.Verify(setup, downloaded, Log);
    }

    string InstallDsh(string setup)
    {
        SetStep(1, StepState.Running, "已打开 DSH 安装程序，请在弹出的窗口里按提示完成安装...");
        SetStatus("等待 DSH 安装程序完成...", null);
        Marquee(true);
        int exit;
        try
        {
            using (var p = Process.Start(new ProcessStartInfo(setup) { UseShellExecute = true }))
            {
                while (!p.WaitForExit(500)) if (cancel) throw new OperationCanceledException();
                exit = p.ExitCode;
            }
        }
        catch (Win32Exception ex)
        {
            Marquee(false);
            if (ex.NativeErrorCode == 1223) { Log("用户在权限提示里点了「否」。"); return null; }
            throw;
        }
        Log("DSH 安装程序退出，退出码 " + exit);

        // 有些安装程序会先退出、再由子进程继续安装，所以多等一会儿
        int waitSeconds = exit == 0 ? 90 : 5;
        string dsh = null;
        for (int i = 0; i < waitSeconds * 2 && dsh == null; i++)
        {
            if (cancel) throw new OperationCanceledException();
            dsh = Dsh.Find(dshOverride, null);
            if (dsh == null) Thread.Sleep(500);
        }
        Marquee(false);
        return dsh;
    }

    // 让用户选 DeepSeek Harness.exe，倒推 dsh.cmd 的位置
    string PickDsh()
    {
        string picked = null;
        MethodInvoker pick = delegate
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "选择 DSH 安装文件夹里的 DeepSeek Harness.exe";
                dlg.Filter = "DeepSeek Harness|DeepSeek Harness.exe;dsh.cmd|程序|*.exe;*.cmd";
                if (dlg.ShowDialog(this) == DialogResult.OK) picked = dlg.FileName;
            }
        };
        if (InvokeRequired) Invoke(pick); else pick();
        if (cancel) throw new OperationCanceledException();
        if (picked == null) { Log("没有选择文件。"); return null; }
        string dsh = Dsh.FromPicked(picked);
        Log(dsh != null ? "手动指定：" + dsh : @"选的文件旁边没有 resources\runtime\cli\bin\dsh.cmd：" + picked);
        return dsh;
    }

    // 安装插件前：profile 没生成就打开一次桌面版让它生成，然后确保 DSH 已完全退出
    bool PrepareProfile(string dsh, out string failMessage)
    {
        failMessage = null;
        string root = Dsh.InstallRoot(dsh);
        string gui;
        var running = Dsh.RunningProcesses(root, out gui);
        if (gui == null) gui = Dsh.GuessGuiExe(root);

        if (!Dsh.ProfileReady())
        {
            if (running.Count == 0)
            {
                if (gui == null)
                {
                    SetStep(2, StepState.Failed, "没找到 DSH 主程序，请自己打开一次 DSH，完全退出后再重试");
                    failMessage = "请先打开一次 DSH，再点「重试」。";
                    return false;
                }
                SetStep(2, StepState.Running, "第一次使用，正在打开 DSH 完成初始化...");
                SetStatus("正在初始化 DSH（会自动打开再关闭）...", null);
                try { Process.Start(new ProcessStartInfo(gui) { UseShellExecute = true, WorkingDirectory = root }); }
                catch (Exception ex) { Log("打开 DSH 失败：" + ex.Message); }
            }
            Marquee(true);
            for (int i = 0; i < 120 && !Dsh.ProfileReady(); i++)
            {
                if (cancel) throw new OperationCanceledException();
                Thread.Sleep(500);
            }
            Marquee(false);
            if (!Dsh.ProfileReady())
            {
                SetStep(2, StepState.Failed, "DSH 打开 60 秒后还没完成初始化，等 DSH 界面出来后再重试");
                failMessage = "等 DSH 界面出来后，再点「重试」。";
                return false;
            }
            Log("DSH 初始化完成：" + Dsh.ProfileDir());
            Thread.Sleep(2000); // 给 DSH 一点时间写完其余文件
            running = Dsh.RunningProcesses(root, out gui);
        }
        else if (running.Count > 0)
        {
            SetStep(2, StepState.Running, "DSH 正在运行，等待确认...");
            if (!Ask("安装插件前要先关闭 DSH，装完会自动重新打开。\r\n\r\n现在关闭 DSH 吗？\r\n（请先保存 DSH 里没做完的内容）"))
            {
                SetStep(2, StepState.Failed, "已取消，请完全退出 DSH 后再重试");
                failMessage = "需要先关闭 DSH。";
                return false;
            }
        }

        if (!Dsh.Quit(running, Log))
        {
            SetStep(2, StepState.Failed, "DSH 没能自动退出，请在右下角托盘里退出 DSH 后重试");
            failMessage = "请完全退出 DSH 后点「重试」。";
            return false;
        }
        return true;
    }

    bool RestartDsh(string dsh)
    {
        string root = Dsh.InstallRoot(dsh);
        string gui;
        var running = Dsh.RunningProcesses(root, out gui);
        if (gui == null) gui = Dsh.GuessGuiExe(root);

        if (running.Count > 0)
        {
            SetStep(3, StepState.Running, "DSH 正在运行，等待确认...");
            if (!Ask("插件需要重启 DSH 才会出现。\r\n\r\n现在关闭并重新打开 DSH 吗？\r\n（请先保存 DSH 里没做完的内容）"))
            {
                SetStep(3, StepState.Skipped, "已跳过，请稍后自己重启 DSH");
                return false;
            }
            Dsh.Quit(running, Log);
        }

        if (gui == null)
        {
            SetStep(3, StepState.Skipped, "没找到 DSH 主程序，请从开始菜单打开 DeepSeek Harness");
            return false;
        }
        try
        {
            Process.Start(new ProcessStartInfo(gui) { UseShellExecute = true, WorkingDirectory = root });
            Log("已启动：" + gui);
            SetStep(3, StepState.Done, "DSH 已重新打开");
            return true;
        }
        catch (Exception ex)
        {
            Log("启动 DSH 失败：" + ex.Message);
            SetStep(3, StepState.Skipped, "自动打开失败，请从开始菜单打开 DeepSeek Harness");
            return false;
        }
    }
}
// ---------- 同目录 DSH 安装包 ----------

static class DshPackage
{
    public static string Resolve(string directory, Func<string> download, out bool localPackage)
    {
        string setup = FindLocal(directory);
        localPackage = setup != null;
        return localPackage ? setup : download();
    }

    public static string FindLocal(string directory)
    {
        // 使用程序所在目录，不使用工作目录，也不搜索子目录或下载文件夹。
        string preferred = Path.Combine(directory, Config.DshInstallerFile);
        if (File.Exists(preferred)) return preferred;

        var candidates = new List<string>();
        foreach (string file in Directory.GetFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(file);
            if ((name.StartsWith("dsh-", StringComparison.OrdinalIgnoreCase) &&
                 name.IndexOf("windows-x64", StringComparison.OrdinalIgnoreCase) >= 0) ||
                name.StartsWith("DeepSeek Harness Setup", StringComparison.OrdinalIgnoreCase))
                candidates.Add(file);
        }
        // 支持带版本号和浏览器添加 (1) 后缀的文件；多个候选时选择修改时间最新的。
        candidates.Sort(delegate(string a, string b)
        {
            int order = File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a));
            return order != 0 ? order : StringComparer.OrdinalIgnoreCase.Compare(a, b);
        });
        return candidates.Count > 0 ? candidates[0] : null;
    }

    public static void Verify(string setup, bool downloaded, Action<string> log)
    {
        string signer;
        int code = WinTrust.Verify(setup, out signer);
        log("签名校验结果：0x" + code.ToString("X8") + "，签名者：" + (signer ?? "无"));
        if (code != 0 || !string.Equals(signer, Config.ExpectedSigner, StringComparison.Ordinal))
        {
            string action = "本地文件已保留，请移走或替换该文件后重试：" + setup;
            if (downloaded)
            {
                try { File.Delete(setup); action = "已删除下载的文件。"; }
                catch { action = "无法删除下载的文件，请手动删除：" + setup; }
            }
            throw new Exception("安装包签名校验没通过（签名者：" + (signer ?? "无") + "）。" + action +
                                "\r\n请到 DeepSeek 官网重新下载：" + Config.DshPageUrl);
        }
        log("签名有效：" + signer);
    }
}
// ---------- DSH 定位与调用 ----------

static class Dsh
{
    public static string Find(string overridePath, Action<string> log)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(overridePath)) candidates.Add(overridePath);
        string tail = Path.Combine("DeepSeek Harness", @"resources\runtime\cli\bin\dsh.cmd");
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        candidates.Add(Path.Combine(Path.Combine(local, "Programs"), tail));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), tail));
        foreach (var c in candidates)
        {
            if (log != null) log("检查：" + c);
            if (File.Exists(c)) return c;
        }
        // 装在别的文件夹：查卸载记录里的安装位置，再查正在运行的 DSH
        foreach (string root in RegistryRoots())
        {
            string c = DshCmd(root);
            if (log != null) log("检查（卸载记录）：" + c);
            if (File.Exists(c)) return c;
        }
        foreach (var p in Process.GetProcessesByName("DeepSeek Harness"))
        {
            string path;
            try { path = p.MainModule.FileName; } catch { continue; }
            string c = DshCmd(Path.GetDirectoryName(path));
            if (log != null) log("检查（正在运行）：" + c);
            if (File.Exists(c)) return c;
        }
        return null;
    }

    static string DshCmd(string root)
    {
        return Path.Combine(root, @"resources\runtime\cli\bin\dsh.cmd");
    }

    // DSH 安装程序写在卸载记录里的 InstallLocation（当前用户和所有用户两种安装方式都查）
    static List<string> RegistryRoots()
    {
        var roots = new List<string>();
        var paths = new[] { @"Software\Microsoft\Windows\CurrentVersion\Uninstall", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" };
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            foreach (var path in paths)
            {
                try
                {
                    using (var key = hive.OpenSubKey(path))
                    {
                        if (key == null) continue;
                        foreach (string name in key.GetSubKeyNames())
                            using (var sub = key.OpenSubKey(name))
                            {
                                if (sub == null) continue;
                                var display = sub.GetValue("DisplayName") as string;
                                var location = sub.GetValue("InstallLocation") as string;
                                if (display != null && display.StartsWith("DeepSeek Harness", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(location))
                                    roots.Add(location.Trim('"'));
                            }
                    }
                }
                catch { }
            }
        return roots;
    }

    // 手动选的可能是 DeepSeek Harness.exe，也可能直接是 dsh.cmd
    public static string FromPicked(string file)
    {
        if (file.EndsWith("dsh.cmd", StringComparison.OrdinalIgnoreCase)) return File.Exists(file) ? file : null;
        string c = DshCmd(Path.GetDirectoryName(file));
        return File.Exists(c) ? c : null;
    }

    // 桌面版第一次打开时生成 <DSH_HOME 或 ~/.dsh>\profiles\desktop\package.json
    public static string ProfileDir()
    {
        string home = Environment.GetEnvironmentVariable("DSH_HOME");
        if (string.IsNullOrEmpty(home) || home.Trim().Length == 0)
            home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
        return Path.Combine(Path.Combine(home, "profiles"), Config.Profile);
    }

    public static bool ProfileReady()
    {
        return File.Exists(Path.Combine(ProfileDir(), "package.json"));
    }

    // 先请窗口正常关闭，关不掉再结束进程；返回是否已全部退出
    public static bool Quit(List<Process> running, Action<string> log)
    {
        if (running.Count == 0) return true;
        foreach (var p in running) { try { p.CloseMainWindow(); } catch { } }
        Thread.Sleep(3000);
        foreach (var p in running) { try { if (!p.HasExited) p.Kill(); } catch { } }
        for (int i = 0; i < 20; i++)
        {
            bool alive = false;
            foreach (var p in running) { try { if (!p.HasExited) alive = true; } catch { } }
            if (!alive) { if (log != null) log("DSH 已退出。"); return true; }
            Thread.Sleep(500);
        }
        if (log != null) log("DSH 仍在运行。");
        return false;
    }

    // dsh.cmd 位于 <安装目录>\resources\runtime\cli\bin\
    public static string InstallRoot(string dsh)
    {
        var dir = new System.IO.FileInfo(dsh).Directory;
        for (int i = 0; i < 4 && dir != null; i++) dir = dir.Parent;
        return dir == null ? null : dir.FullName;
    }

    public static int Run(string dsh, string arguments, Action<string> log)
    {
        var psi = new ProcessStartInfo("cmd.exe", "/d /s /c \"\"" + dsh + "\" " + arguments + "\"");
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;
        psi.CreateNoWindow = true;
        psi.WorkingDirectory = Path.GetDirectoryName(dsh);
        using (var p = new Process())
        {
            p.StartInfo = psi;
            p.OutputDataReceived += (s, e) => { if (e.Data != null) log("    " + e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) log("    " + e.Data); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            return p.ExitCode;
        }
    }

    public static List<Process> RunningProcesses(string root, out string gui)
    {
        gui = null;
        var list = new List<Process>();
        if (root == null) return list;
        foreach (var p in Process.GetProcesses())
        {
            string path;
            try { path = p.MainModule.FileName; } catch { continue; }
            if (path == null || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(p);
            if (gui == null && p.MainWindowHandle != IntPtr.Zero) gui = path;
        }
        return list;
    }

    public static string GuessGuiExe(string root)
    {
        if (root == null || !Directory.Exists(root)) return null;
        string preferred = Path.Combine(root, "DeepSeek Harness.exe");
        if (File.Exists(preferred)) return preferred;
        foreach (var f in Directory.GetFiles(root, "*.exe"))
        {
            string name = Path.GetFileName(f).ToLowerInvariant();
            if (name.Contains("uninstall") || name.Contains("update") || name.Contains("crash")) continue;
            return f;
        }
        return null;
    }
}
// ---------- Authenticode 签名校验 ----------

static class WinTrust
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WinTrustFileInfo
    {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref WinTrustData data);

    static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    // 返回 0 表示签名有效且证书链可信；signer 为签名证书的名称
    public static int Verify(string path, out string signer)
    {
        signer = null;
        try { signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)).GetNameInfo(X509NameType.SimpleName, false); }
        catch { }

        var file = new WinTrustFileInfo { cbStruct = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo)), pcwszFilePath = path };
        IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
        try
        {
            Marshal.StructureToPtr(file, pFile, false);
            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf(typeof(WinTrustData)),
                dwUIChoice = 2,          // WTD_UI_NONE
                fdwRevocationChecks = 0, // WTD_REVOKE_NONE
                dwUnionChoice = 1,       // WTD_CHOICE_FILE
                pFile = pFile,
                dwProvFlags = 0x10       // WTD_REVOCATION_CHECK_NONE
            };
            return WinVerifyTrust(new IntPtr(-1), GenericVerifyV2, ref data);
        }
        finally
        {
            Marshal.DestroyStructure(pFile, typeof(WinTrustFileInfo));
            Marshal.FreeHGlobal(pFile);
        }
    }
}
