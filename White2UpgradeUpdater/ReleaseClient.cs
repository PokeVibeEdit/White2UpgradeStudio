using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace White2UpgradeUpdater;

internal static class ReleaseClient
{
    internal const string Repository = "PokeVibeEdit/White2UpgradeStudio";
    internal const string ZipName = "White2UpgradeStudio-win-x64.zip";
    internal const string HashName = ZipName + ".sha256";
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(8) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("White2UpgradeStudio-Updater", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    internal sealed record Release(string Tag, SemVersion Version, bool PreRelease, string ZipUrl, string HashUrl);

    internal static async Task<Release?> FindUpdateAsync(string currentVersion)
    {
        var current = SemVersion.Parse(currentVersion);
        string json;
        bool useCli = false;
        var cli = await RunGhAsync("api", $"repos/{Repository}/releases?per_page=50");
        if (cli.ExitCode == 0 && !string.IsNullOrWhiteSpace(cli.Output))
        {
            json = cli.Output;
            useCli = true;
        }
        else
        {
            try { json = await Http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases?per_page=50"); }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException("GitHub releases could not be read. If the repository is private, install GitHub CLI, run 'gh auth login', and ensure your account has access. " + ex.Message);
            }
            catch (TaskCanceledException ex)
            {
                throw new InvalidOperationException("Timed out contacting GitHub. " + ex.Message);
            }
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Unexpected GitHub Releases response.");

        Release? newest = null;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            string tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!SemVersion.TryParse(tag, out var version) || version.CompareTo(current) <= 0) continue;
            string? zip = null, hash = null;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                var url = asset.GetProperty("browser_download_url").GetString();
                if (name == ZipName) zip = url;
                if (name == HashName) hash = url;
            }
            // The checksum is mandatory. Do not silently install older packages that lack one.
            if (string.IsNullOrEmpty(zip) || string.IsNullOrEmpty(hash)) continue;
            var candidate = new Release(tag, version, release.GetProperty("prerelease").GetBoolean(), zip, hash);
            if (newest is null || candidate.Version.CompareTo(newest.Version) > 0) newest = candidate;
        }
        if (newest is null) return null;
        if (useCli) Console.WriteLine("Authenticated release access: GitHub CLI.");
        return newest;
    }

    internal static async Task DownloadAsync(Release release, string directory)
    {
        Directory.CreateDirectory(directory);
        // GitHub CLI handles private release assets using the user's own existing login.
        // No access token is read, stored, echoed, or passed on the command line.
        var zipCli = await RunGhAsync("release", "download", release.Tag, "--repo", Repository,
            "--pattern", ZipName, "--dir", directory, "--clobber");
        var hashCli = zipCli.ExitCode == 0
            ? await RunGhAsync("release", "download", release.Tag, "--repo", Repository,
                "--pattern", HashName, "--dir", directory, "--clobber")
            : (ExitCode: -1, Output: "", Error: "");
        if (zipCli.ExitCode == 0 && hashCli.ExitCode == 0) return;

        // Public repositories work even without the GitHub CLI.
        await DownloadFileAsync(release.ZipUrl, Path.Combine(directory, ZipName));
        await DownloadFileAsync(release.HashUrl, Path.Combine(directory, HashName));
    }

    private static async Task DownloadFileAsync(string url, string destination)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            (uri.Host != "github.com" && uri.Host != "objects.githubusercontent.com" &&
             uri.Host != "release-assets.githubusercontent.com"))
            throw new InvalidDataException("GitHub release contained an unexpected download URL.");
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = File.Create(destination);
        await input.CopyToAsync(output);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunGhAsync(params string[] arguments)
    {
        try
        {
            using var process = new Process { StartInfo = new ProcessStartInfo("gh")
            {
                UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true,
                CreateNoWindow = true
            }};
            foreach (var arg in arguments) process.StartInfo.ArgumentList.Add(arg);
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, await output, await error);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return (-1, "", "GitHub CLI unavailable");
        }
    }
}

internal sealed class SemVersion : IComparable<SemVersion>
{
    private static readonly Regex Pattern = new(@"^v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.Compiled);
    internal int Major { get; }
    internal int Minor { get; }
    internal int Patch { get; }
    internal string[]? Pre { get; }
    private SemVersion(int major, int minor, int patch, string[]? pre) => (Major, Minor, Patch, Pre) = (major, minor, patch, pre);
    public static SemVersion Parse(string value) => TryParse(value, out var result)
        ? result! : throw new InvalidDataException($"Invalid installed version '{value}'. Expected e.g. 0.1.0-alpha.1.");
    public static bool TryParse(string? input, out SemVersion? version)
    {
        version = null;
        var m = Pattern.Match(input?.Trim() ?? "");
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out int a) || !int.TryParse(m.Groups[2].Value, out int b) || !int.TryParse(m.Groups[3].Value, out int c)) return false;
        string[]? pre = m.Groups[4].Success ? m.Groups[4].Value.Split('.') : null;
        if (pre is not null && pre.Any(s => s.Length == 0 || (s.All(char.IsDigit) && s.Length > 1 && s[0] == '0'))) return false;
        version = new SemVersion(a, b, c, pre);
        return true;
    }
    public int CompareTo(SemVersion? other)
    {
        if (other is null) return 1;
        int v = Major.CompareTo(other.Major);
        if (v != 0) return v;
        v = Minor.CompareTo(other.Minor);
        if (v != 0) return v;
        v = Patch.CompareTo(other.Patch);
        if (v != 0) return v;
        if (Pre is null) return other.Pre is null ? 0 : 1;
        if (other.Pre is null) return -1;
        for (int i = 0; i < Math.Min(Pre.Length, other.Pre.Length); i++)
        {
            bool xn = Pre[i].All(char.IsDigit), yn = other.Pre[i].All(char.IsDigit);
            if (xn && yn)
            {
                // Avoid overflow on arbitrarily long numerical prerelease identifiers.
                string x = Pre[i].TrimStart('0'), y = other.Pre[i].TrimStart('0');
                int cmp = x.Length.CompareTo(y.Length);
                if (cmp == 0) cmp = string.CompareOrdinal(x, y);
                if (cmp != 0) return cmp;
            }
            else if (xn != yn) return xn ? -1 : 1;
            else
            {
                int cmp = string.CompareOrdinal(Pre[i], other.Pre[i]);
                if (cmp != 0) return cmp;
            }
        }
        return Pre.Length.CompareTo(other.Pre.Length);
    }
}
