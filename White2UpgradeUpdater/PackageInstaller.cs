using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace White2UpgradeUpdater;

internal static class PackageInstaller
{
    private const long MaxExtractedSize = 750L * 1024 * 1024;
    internal static string ReadExpectedHash(string file)
    {
        var content = File.ReadAllText(file).Trim();
        var match = Regex.Match(content, @"^([0-9a-fA-F]{64})(?:\s+\*?([^\r\n]+))?$", RegexOptions.CultureInvariant);
        if (!match.Success) throw new InvalidDataException("Invalid SHA-256 checksum file.");
        if (match.Groups[2].Success && match.Groups[2].Value.Trim() != ReleaseClient.ZipName)
            throw new InvalidDataException("Checksum does not reference the expected package name.");
        return match.Groups[1].Value.ToLowerInvariant();
    }

    internal static void VerifyHash(string file, string hash)
    {
        using var stream = File.OpenRead(file);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(hash)))
            throw new InvalidDataException("SHA-256 mismatch: the downloaded release has not been installed.");
    }

    internal static string ExtractSafe(string packageFile, string workingDirectory)
    {
        var extracted = Path.Combine(workingDirectory, "extracted");
        Directory.CreateDirectory(extracted);
        string root = Path.GetFullPath(extracted) + Path.DirectorySeparatorChar;
        long total = 0;
        int count = 0;
        using var zip = ZipFile.OpenRead(packageFile);
        foreach (var entry in zip.Entries)
        {
            if (++count > 5000) throw new InvalidDataException("Package contains too many entries.");
            var path = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(path)) throw new InvalidDataException("Unsafe absolute path inside update package.");
            string destination = Path.GetFullPath(Path.Combine(extracted, path));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase) && destination != Path.GetFullPath(extracted))
                throw new InvalidDataException("Unsafe traversal path inside update package.");
            // A symlink entry in a ZIP can be used to redirect subsequent paths outside the installation.
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Update packages may not contain symbolic links.");
            if (entry.Name.Length == 0)
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            total = checked(total + entry.Length);
            if (total > MaxExtractedSize) throw new InvalidDataException("Update exceeds safe extraction size.");
            if (destination.EndsWith(".nds", StringComparison.OrdinalIgnoreCase) ||
                destination.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) ||
                destination.EndsWith(".narc", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update package must not include game ROMs, saves, or archives.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }
        if (!File.Exists(Path.Combine(extracted, "White2UpgradeEditor.exe")) ||
            !File.Exists(Path.Combine(extracted, "White2UpgradeUpdater.exe")))
            throw new InvalidDataException("The release is missing the editor or updater executable.");
        return extracted;
    }

    private static void RejectReparsePoints(string target, string root)
    {
        var directory = Path.GetDirectoryName(target)!;
        while (directory.Length >= root.Length)
        {
            if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Installation contains a symbolic-link directory; update aborted.");
            if (string.Equals(directory, root, StringComparison.OrdinalIgnoreCase)) break;
            directory = Path.GetDirectoryName(directory)!;
        }
        if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Installation contains a linked file; update aborted.");
    }

    internal static string Install(string extracted, string installDir)
    {
        string root = Path.GetFullPath(installDir).TrimEnd(Path.DirectorySeparatorChar);
        string extractedRoot = Path.GetFullPath(extracted).TrimEnd(Path.DirectorySeparatorChar);
        var sourceFiles = Directory.GetFiles(extractedRoot, "*", SearchOption.AllDirectories);
        // Backups are deliberately outside the install folder, so ROMs and user files aren't touched.
        string backup = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "White2UpgradeStudio", "UpdaterBackups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(backup);
        var changes = new List<(string Destination, string? Backup)>();
        try
        {
            foreach (var source in sourceFiles)
            {
                string relative = Path.GetRelativePath(extractedRoot, source);
                string destination = Path.GetFullPath(Path.Combine(root, relative));
                if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unsafe destination path.");
                RejectReparsePoints(destination, root);
                string? saved = null;
                if (File.Exists(destination))
                {
                    saved = Path.Combine(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    File.Copy(destination, saved, true);
                }
                changes.Add((destination, saved));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, true);
            }
            return backup;
        }
        catch
        {
            Console.Error.WriteLine("Update failed. Restoring the files changed so far...");
            var errors = new List<Exception>();
            foreach (var (destination, saved) in Enumerable.Reverse(changes))
            {
                try
                {
                    if (saved is null) File.Delete(destination);
                    else File.Copy(saved, destination, true);
                }
                catch (Exception ex) { errors.Add(ex); }
            }
            if (errors.Count > 0) Console.Error.WriteLine($"Rollback had {errors.Count} error(s). Recovery copies are in: {backup}");
            throw;
        }
    }
}
