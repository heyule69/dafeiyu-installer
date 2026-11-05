// 编译时与安装器源文件一起传入 csc，并指定 /main:LocalPackageTests。
// 参数：已下载且签名有效的 DSH 官方安装包。测试不会运行安装包或修改已安装的 DSH。
using System;
using System.IO;

static class LocalPackageTests
{
    static int checks;

    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        checks++;
        Console.WriteLine("PASS: " + name);
    }

    static void Reject(string file, bool downloaded, string message)
    {
        try { DshPackage.Verify(file, downloaded, delegate { }); }
        catch (Exception ex)
        {
            Check(ex.Message.Contains("签名校验没通过") && ex.Message.Contains(message), message);
            return;
        }
        throw new Exception("FAIL: invalid package was accepted: " + file);
    }

    static int Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "dafeiyu-package-test-" + Guid.NewGuid().ToString("N"));
        string previousDirectory = Environment.CurrentDirectory;
        Directory.CreateDirectory(root);
        try
        {
            string packages = Path.Combine(root, "安装包 中文路径");
            string elsewhere = Path.Combine(root, "工作目录");
            Directory.CreateDirectory(packages);
            Directory.CreateDirectory(elsewhere);
            Check(DshPackage.FindLocal(packages) == null, "empty directory");

            bool local;
            int downloads = 0;
            string resolved = DshPackage.Resolve(packages, delegate { downloads++; return "downloaded.exe"; }, out local);
            Check(resolved == "downloaded.exe" && !local && downloads == 1, "missing local package calls download once");
            resolved = DshPackage.Resolve(packages, delegate { return null; }, out local);
            Check(resolved == null && !local, "download cancellation is preserved");

            File.WriteAllBytes(Path.Combine(elsewhere, Config.DshInstallerFile), new byte[] { 1 });
            Environment.CurrentDirectory = elsewhere;
            Check(DshPackage.FindLocal(packages) == null, "does not search working directory");
            string nested = Path.Combine(packages, "子目录");
            Directory.CreateDirectory(nested);
            File.WriteAllBytes(Path.Combine(nested, Config.DshInstallerFile), new byte[] { 1 });
            Check(DshPackage.FindLocal(packages) == null, "does not search subdirectories");
            foreach (string name in new[] { "一键安装大肥鱼.exe", "dsh-helper.exe", "dsh-latest-windows-arm64.exe", "dsh-latest-windows-x64.exe.part" })
                File.WriteAllBytes(Path.Combine(packages, name), new byte[] { 1 });
            Check(DshPackage.FindLocal(packages) == null, "ignores unrelated executables, ARM64 and partial downloads");

            string duplicate = Path.Combine(packages, "dsh-latest-windows-x64 (1).exe");
            File.WriteAllBytes(duplicate, new byte[] { 1 });
            File.SetLastWriteTimeUtc(duplicate, new DateTime(2026, 1, 1));
            Check(DshPackage.FindLocal(packages) == duplicate, "browser duplicate filename");
            string versioned = Path.Combine(packages, "DSH-0.1.2-WINDOWS-X64.EXE");
            File.WriteAllBytes(versioned, new byte[] { 1 });
            File.SetLastWriteTimeUtc(versioned, new DateTime(2026, 2, 1));
            Check(DshPackage.FindLocal(packages) == versioned, "versioned filename and case-insensitive matching");
            string legacy = Path.Combine(packages, "DeepSeek Harness Setup 0.1.3.exe");
            File.WriteAllBytes(legacy, new byte[] { 1 });
            File.SetLastWriteTimeUtc(legacy, new DateTime(2026, 3, 1));
            Check(DshPackage.FindLocal(packages) == legacy, "most recently modified alternative wins");

            string preferred = Path.Combine(packages, Config.DshInstallerFile);
            File.WriteAllBytes(preferred, new byte[] { 1 });
            File.SetLastWriteTimeUtc(preferred, new DateTime(2025, 1, 1));
            Check(DshPackage.FindLocal(packages) == preferred, "standard filename takes priority");
            resolved = DshPackage.Resolve(packages, delegate { throw new Exception("Download must not be called"); }, out local);
            Check(resolved == preferred && local, "local package works with unavailable network");
            Reject(preferred, false, "本地文件已保留");
            Check(File.Exists(preferred), "invalid local package is not deleted");

            string badDownload = Path.Combine(root, "bad-download.exe");
            File.WriteAllBytes(badDownload, new byte[] { 1 });
            Reject(badDownload, true, "已删除下载的文件");
            Check(!File.Exists(badDownload), "invalid downloaded package is deleted");

            string foreign = Path.Combine(root, "other-publisher.exe");
            File.Copy(Environment.GetEnvironmentVariable("COMSPEC"), foreign);
            Reject(foreign, false, "本地文件已保留");
            Check(File.Exists(foreign), "other publisher is rejected and local file retained");

            Check(args.Length == 1 && File.Exists(args[0]), "official package is available");
            DshPackage.Verify(Path.GetFullPath(args[0]), false, Console.WriteLine);
            Check(true, "real downloaded DeepSeek package passes signature and publisher checks");

            string damaged = Path.Combine(root, "damaged-official.exe");
            File.Copy(args[0], damaged);
            using (var stream = new FileStream(damaged, FileMode.Open, FileAccess.ReadWrite))
            {
                stream.Position = 4096;
                int value = stream.ReadByte();
                stream.Position = 4096;
                stream.WriteByte((byte)(value ^ 1));
            }
            Reject(damaged, false, "本地文件已保留");
            Check(File.Exists(damaged), "tampered official package is rejected and retained");
            Console.WriteLine("All " + checks + " checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string absoluteRoot = Path.GetFullPath(root);
            if (absoluteRoot.StartsWith(temp, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(absoluteRoot).StartsWith("dafeiyu-package-test-", StringComparison.Ordinal))
                Directory.Delete(absoluteRoot, true);
        }
    }
}
