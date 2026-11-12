using System.Diagnostics;
using System.Reflection;

namespace White2UpgradeUpdater;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Contains("--self-test")) return SelfTest();
            string installDir = ReadOption(args, "--install-dir") ?? AppContext.BaseDirectory;
            installDir = Path.GetFullPath(installDir);
            string version = ReadOption(args, "--current-version") ?? InstalledVersion(installDir);
            int editorPid = int.TryParse(ReadOption(args, "--editor-pid"), out var pid) ? pid : 0;

            // Detach our own executable from the application directory before any replacement.
            // Windows won't overwrite an executable while it's running from that directory.
            if (!args.Contains("--relocated") &&
                string.Equals(Path.GetFileName(Environment.ProcessPath), "White2UpgradeUpdater.exe", StringComparison.OrdinalIgnoreCase) &&
                Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar).Equals(
                    installDir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                RelocateAndRestart(args);
                return 0;
            }

            Console.Title = "White 2 Upgrade Studio Updater";
            Console.WriteLine("White 2 Upgrade Studio — updater");
            Console.WriteLine("Repository: https://github.com/" + ReleaseClient.Repository);
            Console.WriteLine("Installed version: " + version);
            Console.WriteLine("Installation: " + installDir);
            Console.WriteLine();
            var release = await ReleaseClient.FindUpdateAsync(version);
            if (release is null)
            {
                Console.WriteLine("No newer installable GitHub release found.");
                Console.WriteLine("This updater uses tagged GitHub Releases, not Actions build artifacts.");
                return 0;
            }
            Console.WriteLine($"Available: {release.Tag}{(release.PreRelease ? " (preview)" : "")}");
            Console.Write("Download and install this update? [y/N] ");
            if (!Confirm()) { Console.WriteLine("Cancelled; no files were changed."); return 0; }

            string working = Path.Combine(Path.GetTempPath(), "White2UpgradeStudio-Update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(working);
            Console.WriteLine("Downloading release assets...");
            await ReleaseClient.DownloadAsync(release, working);
            string package = Path.Combine(working, ReleaseClient.ZipName);
            string expected = PackageInstaller.ReadExpectedHash(Path.Combine(working, ReleaseClient.HashName));
            PackageInstaller.VerifyHash(package, expected);
            Console.WriteLine("SHA-256 verified.");
            string extracted = PackageInstaller.ExtractSafe(package, working);
            Console.WriteLine("Package safely extracted and validated.");

            Console.WriteLine();
            Console.WriteLine("IMPORTANT: Save any unsaved ROM edits and close White 2 Upgrade Studio.");
            Console.Write("When the editor is closed, press Enter to continue (or Ctrl+C to cancel): ");
            Console.ReadLine();
            WaitForEditorExit(editorPid);
            string backup = PackageInstaller.Install(extracted, installDir);
            Console.WriteLine("Update installed successfully. Previous app files backed up at:");
            Console.WriteLine(backup);
            var editor = Path.Combine(installDir, "White2UpgradeEditor.exe");
            if (File.Exists(editor))
            {
                Process.Start(new ProcessStartInfo(editor) { WorkingDirectory = installDir, UseShellExecute = true });
                Console.WriteLine("Editor started.");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Update was not completed: " + ex.Message);
            Console.Error.WriteLine("Your ROM and save files are not part of the update package.");
            return 1;
        }
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static string InstalledVersion(string directory)
    {
        var dll = Path.Combine(directory, "White2UpgradeEditor.dll");
        if (File.Exists(dll))
        {
            string? info = FileVersionInfo.GetVersionInfo(dll).ProductVersion;
            if (!string.IsNullOrWhiteSpace(info)) return info.Split('+')[0];
        }
        return "0.1.0-alpha.1";
    }

    private static void RelocateAndRestart(string[] args)
    {
        string dir = Path.Combine(Path.GetTempPath(), "White2UpgradeUpdater-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var name in new[] { "White2UpgradeUpdater.exe", "White2UpgradeUpdater.dll",
            "White2UpgradeUpdater.deps.json", "White2UpgradeUpdater.runtimeconfig.json" })
        {
            string source = Path.Combine(AppContext.BaseDirectory, name);
            if (!File.Exists(source)) throw new FileNotFoundException("Updater installation is incomplete.", source);
            File.Copy(source, Path.Combine(dir, name), true);
        }
        var start = new ProcessStartInfo(Path.Combine(dir, "White2UpgradeUpdater.exe"))
        {
            UseShellExecute = true,
            WorkingDirectory = dir
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        // Preserve the original install directory when started by double-clicking the updater.
        if (ReadOption(args, "--install-dir") is null)
        {
            start.ArgumentList.Add("--install-dir");
            start.ArgumentList.Add(AppContext.BaseDirectory);
        }
        if (ReadOption(args, "--current-version") is null)
        {
            start.ArgumentList.Add("--current-version");
            start.ArgumentList.Add(InstalledVersion(AppContext.BaseDirectory));
        }
        start.ArgumentList.Add("--relocated");
        Process.Start(start);
    }

    private static bool Confirm()
    {
        string answer = Console.ReadLine()?.Trim() ?? "";
        return answer.Equals("y", StringComparison.OrdinalIgnoreCase) || answer.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static void WaitForEditorExit(int editorPid)
    {
        if (editorPid <= 0) return;
        try
        {
            using var process = Process.GetProcessById(editorPid);
            if (process.HasExited) return;
            Console.WriteLine("Waiting for the editor to close...");
            if (!process.WaitForExit(120000))
                throw new IOException("The editor is still running. Close it and rerun the updater.");
        }
        catch (ArgumentException) { /* Editor already exited */ }
    }

    private static int SelfTest()
    {
        void Assert(bool test, string label)
        {
            if (!test) throw new Exception("Self-test failed: " + label);
        }
        Assert(SemVersion.Parse("v0.2.0-alpha.1").CompareTo(SemVersion.Parse("0.1.9")) > 0, "major/minor/patch");
        Assert(SemVersion.Parse("0.2.0").CompareTo(SemVersion.Parse("0.2.0-rc.1")) > 0, "prerelease precedence");
        Assert(SemVersion.Parse("0.2.0-alpha.2").CompareTo(SemVersion.Parse("0.2.0-alpha.11")) < 0, "numeric prerelease");
        Assert(SemVersion.Parse("0.2.0-alpha.1").CompareTo(SemVersion.Parse("0.2.0-alpha.beta")) < 0, "pre identifiers");
        Console.WriteLine("Updater self-tests passed.");
        return 0;
    }
}
