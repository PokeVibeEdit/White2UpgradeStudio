using System.Diagnostics;
using System.Security.Cryptography;

namespace White2UpgradeEditor;

/// <summary>
/// Produces a standard VCDIFF/xdelta3 patch, never an edited ROM as the final output.
/// The xdelta3 command-line encoder is supplied by the user (or found on PATH).
/// </summary>
internal static class XdeltaPatchExporter
{
    internal static string? FindExecutable()
    {
        string local = Path.Combine(AppContext.BaseDirectory, "xdelta3.exe");
        if (File.Exists(local)) return local;
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(directory.Trim().Trim('"'), "xdelta3.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { /* Ignore malformed PATH entries. */ }
        }
        return null;
    }

    internal static async Task ExportAsync(RomWorkspace workspace, string sourceRomPath,
        string outputPatchPath, string xdeltaExecutable)
    {
        if (!File.Exists(xdeltaExecutable))
            throw new FileNotFoundException("xdelta3.exe could not be found. Select the xdelta3 command-line executable.", xdeltaExecutable);

        string source = Path.GetFullPath(sourceRomPath);
        string output = Path.GetFullPath(outputPatchPath);
        if (string.Equals(source, output, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The xdelta patch cannot overwrite your original ROM.");
        if (!File.Exists(source))
            throw new FileNotFoundException("The original .nds file was not found.", source);

        string? outputFolder = Path.GetDirectoryName(output);
        if (string.IsNullOrWhiteSpace(outputFolder) || !Directory.Exists(outputFolder))
            throw new DirectoryNotFoundException("Select an existing output directory for the .xdelta patch.");

        string temporary = Path.Combine(Path.GetTempPath(), "W2U-xdelta-" + Guid.NewGuid().ToString("N"));
        string stagedPatch = Path.Combine(outputFolder, "." + Path.GetFileName(output) + "." +
                                                   Guid.NewGuid().ToString("N") + ".partial");
        Directory.CreateDirectory(temporary);
        try
        {
            string modified = Path.Combine(temporary, "edited.nds");
            string verified = Path.Combine(temporary, "verified.nds");
            byte[] originalHash = HashFile(source);

            // This method preflights all archive lengths and does not mutate the
            // workspace or mark any in-memory edits as saved.
            workspace.WriteRomSnapshot(modified, source);

            byte[] modifiedHash = HashFile(modified);
            if (originalHash.AsSpan().SequenceEqual(modifiedHash))
                throw new InvalidOperationException("The ROM has no changes relative to the selected original; there is nothing to export.");

            // -e: VCDIFF encode; -s: reference source; target: temporary ROM image.
            await RunXdeltaAsync(xdeltaExecutable, "-e", "-s", source, modified, stagedPatch);
            if (!File.Exists(stagedPatch) || new FileInfo(stagedPatch).Length == 0)
                throw new InvalidDataException("xdelta3 did not produce a valid patch file.");

            // Decode using the exact base ROM and prove that it regenerates
            // the intended edited ROM before making the selected patch visible.
            await RunXdeltaAsync(xdeltaExecutable, "-d", "-s", source, stagedPatch, verified);
            if (!File.Exists(verified) ||
                !modifiedHash.AsSpan().SequenceEqual(HashFile(verified)))
                throw new InvalidDataException("The xdelta verification failed: the patch did not recreate the edited ROM.");

            if (!originalHash.AsSpan().SequenceEqual(HashFile(source)))
                throw new InvalidOperationException("The original ROM changed during patch export. The patch was not saved.");

            // Same-directory move keeps the output free of unverified partial patches.
            File.Move(stagedPatch, output, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(stagedPatch)) File.Delete(stagedPatch); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            try { Directory.Delete(temporary, recursive: true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static byte[] HashFile(string file)
    {
        using var input = File.OpenRead(file);
        return SHA256.HashData(input);
    }

    private static async Task RunXdeltaAsync(string executable, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string arg in arguments) info.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Could not start xdelta3.exe.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("Could not run xdelta3.exe. Select a valid Windows xdelta3 executable. " + ex.Message, ex);
        }

        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("xdelta3 did not finish within 30 minutes.");
        }

        string message = (await stderr).Trim();
        if (process.ExitCode != 0)
        {
            var fallback = (await stdout).Trim();
            throw new InvalidOperationException(
                $"xdelta3 failed (exit code {process.ExitCode}): " +
                (message.Length > 0 ? message : fallback.Length > 0 ? fallback : "No error details returned."));
        }
    }
}
