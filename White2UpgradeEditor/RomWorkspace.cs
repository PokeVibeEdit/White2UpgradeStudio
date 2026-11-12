namespace White2UpgradeEditor;

public sealed class RomWorkspace
{
    private readonly string? _folderRoot;
    private readonly bool _folderHasDataPrefix;
    private readonly NdsRom? _rom;
    private readonly Dictionary<string, NarcArchive> _archives = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RomFile> _romFiles = new(StringComparer.OrdinalIgnoreCase);

    public bool IsExtractedFolder => _folderRoot is not null;
    public string? SourceFolderPath => _folderRoot;
    public string Description { get; }

    private RomWorkspace(string root, bool hasDataPrefix)
    {
        _folderRoot = root;
        _folderHasDataPrefix = hasDataPrefix;
        Description = "Extracted NitroFS dump: " + root;
    }

    private RomWorkspace(NdsRom rom)
    {
        _rom = rom;
        Description = "Nintendo DS ROM: " + rom.Title;
    }

    public static RomWorkspace OpenFolder(string root)
    {
        string dataPath = Path.Combine(root, "data", "a", "0", "1", "6");
        string directPath = Path.Combine(root, "a", "0", "1", "6");
        if (File.Exists(dataPath)) return new RomWorkspace(root, true);
        if (File.Exists(directPath)) return new RomWorkspace(root, false);
        throw new DirectoryNotFoundException(
            "The selected folder does not contain data/a/0/1/6 or a/0/1/6. Select the root of the extracted ROM dump.");
    }

    public static RomWorkspace OpenRom(string path)
    {
        var rom = new NdsRom();
        rom.Open(path);
        return new RomWorkspace(rom);
    }

    public NarcArchive GetArchive(string relativePath)
    {
        string key = Normalize(relativePath);
        if (_archives.TryGetValue(key, out var existing)) return existing;

        byte[] bytes;
        if (_folderRoot is not null)
        {
            string rel = (_folderHasDataPrefix ? "data/" : "") + key;
            string full = Path.Combine(_folderRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            bytes = File.ReadAllBytes(full);
        }
        else
        {
            RomFile file = _rom!.FindFile(key);
            _romFiles[key] = file;
            bytes = _rom.ReadFile(file);
        }

        var archive = NarcArchive.Read(bytes);
        _archives[key] = archive;
        return archive;
    }

    public void SaveTo(string target)
    {
        var dirty = _archives.Where(x => x.Value.IsDirty).ToList();
        if (dirty.Count == 0)
            throw new InvalidOperationException("There are no modified archives to save.");

        // Build every changed archive before writing anything. In direct-ROM mode this
        // also validates every size constraint before mutating the in-memory ROM image.
        var built = dirty.ToDictionary(x => x.Key, x => x.Value.Build(), StringComparer.OrdinalIgnoreCase);

        if (_folderRoot is not null)
        {
            string source = Path.GetFullPath(_folderRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string destination = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a separate patch output folder; the source dump will not be overwritten.");

            Directory.CreateDirectory(target);
            foreach (var pair in dirty)
            {
                string rel = (_folderHasDataPrefix ? "data/" : "") + pair.Key;
                string full = Path.Combine(target, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllBytes(full, built[pair.Key]);
            }
            File.WriteAllText(Path.Combine(target, "PATCH-README.txt"),
                "White 2 Upgrade Studio patch output\r\n\r\n" +
                "This folder contains only modified NitroFS archives. Reinsert each file into the matching path in your own ROM " +
                "with a compatible NDS ROM filesystem tool. Keep a backup of your original ROM.\r\n");
            foreach (var pair in dirty) pair.Value.MarkSaved();
            return;
        }

        string outputPath = Path.GetFullPath(target);
        string inputPath = Path.GetFullPath(_rom!.FilePath);
        if (string.Equals(outputPath, inputPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a different output filename; the source ROM will not be overwritten.");

        // Preflight all FAT entry lengths before replacing any bytes.
        foreach (var pair in dirty)
        {
            RomFile file = _romFiles.TryGetValue(pair.Key, out var found) ? found : _rom!.FindFile(pair.Key);
            if (built[pair.Key].Length != file.Length)
                throw new InvalidOperationException($"Archive '{pair.Key}' rebuilds to {built[pair.Key].Length:N0} bytes, but its ROM entry is {file.Length:N0} bytes. No ROM data was changed. Save a patch from extracted-dump mode instead.");
        }
        foreach (var pair in dirty)
        {
            RomFile file = _romFiles.TryGetValue(pair.Key, out var found) ? found : _rom!.FindFile(pair.Key);
            _rom!.ReplaceSameSize(file, built[pair.Key]);
        }
        _rom!.SaveAs(target);
        foreach (var pair in dirty) pair.Value.MarkSaved();
    }

    /// <summary>
    /// The original source ROM path when a .nds workspace is open. Extracted dumps
    /// require the user to select their own matching base ROM for xdelta export.
    /// </summary>
    public string? OpenedRomPath => _rom?.FilePath;

    /// <summary>
    /// Write a temporary complete .nds image for xdelta encoding without modifying
    /// the live workspace or clearing its unsaved-change flags.
    /// </summary>
    public void WriteRomSnapshot(string target, string sourceRomPath)
    {
        string inputPath = Path.GetFullPath(sourceRomPath);
        string outputPath = Path.GetFullPath(target);
        if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The original ROM cannot be used as the temporary output.");

        NdsRom baseRom;
        if (_rom is not null)
        {
            if (!string.Equals(inputPath, Path.GetFullPath(_rom.FilePath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The selected source ROM must be the .nds originally opened in the editor.");
            if (!_rom.OriginalFileMatchesDisk())
                throw new InvalidOperationException("The source .nds file was changed or replaced since it was opened. Reopen the ROM before exporting an xdelta patch.");
            baseRom = _rom;
        }
        else
        {
            if (DirtyArchiveCount == 0)
                throw new InvalidOperationException("No edited archives are available to patch. Apply an edit first.");
            baseRom = new NdsRom();
            baseRom.Open(inputPath);
        }

        // Build and validate everything before creating a target file. No in-memory
        // archive is changed and no MarkSaved() call happens during patch export.
        var replacements = new List<(RomFile File, byte[] Bytes)>();
        foreach (var pair in _archives.Where(x => x.Value.IsDirty))
        {
            RomFile file = baseRom.FindFile(pair.Key);
            byte[] updated = pair.Value.Build();
            if (updated.Length != file.Length)
                throw new InvalidOperationException(
                    $"Archive '{pair.Key}' changes size ({file.Length:N0} to {updated.Length:N0} bytes). " +
                    "This editor cannot reinsert size-changing NARC archives into an NDS ROM yet. No xdelta was generated.");

            if (_folderRoot is not null)
            {
                string relative = (_folderHasDataPrefix ? "data/" : "") + pair.Key;
                string sourceFile = Path.Combine(_folderRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                // A base .nds must match the original extracted file being edited;
                // otherwise a different game revision could silently be patched.
                if (!File.ReadAllBytes(sourceFile).AsSpan().SequenceEqual(baseRom.ReadFile(file)))
                    throw new InvalidOperationException(
                        $"The selected base ROM does not match extracted archive '{pair.Key}'. " +
                        "Select the same ROM revision from which your dump was extracted.");
            }
            replacements.Add((file, updated));
        }

        try
        {
            baseRom.SaveAs(outputPath);
            using var stream = new FileStream(outputPath, FileMode.Open, FileAccess.Write, FileShare.None);
            foreach (var replacement in replacements)
            {
                stream.Position = replacement.File.Start;
                stream.Write(replacement.Bytes);
            }
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            try { File.Delete(outputPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    public IReadOnlyList<string> ListFiles()
    {
        if (_folderRoot is not null)
        {
            return Directory.EnumerateFiles(_folderRoot, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(_folderRoot, p).Replace('\\', '/'))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }
        return _rom!.Files.Select(f => f.Path).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public byte[] ReadFile(string path)
    {
        if (_folderRoot is not null)
        {
            string clean = path.Replace('/', Path.DirectorySeparatorChar);
            return File.ReadAllBytes(Path.Combine(_folderRoot, clean));
        }
        var file = _rom!.FindFile(path);
        return _rom.ReadFile(file);
    }

    public int ArchiveCount => _archives.Count;
    public int DirtyArchiveCount => _archives.Count(x => x.Value.IsDirty);

    private static string Normalize(string path) => path.TrimStart('/').Replace('\\', '/').Replace("data/", "", StringComparison.OrdinalIgnoreCase);
}
