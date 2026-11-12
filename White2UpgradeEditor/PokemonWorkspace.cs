using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace White2UpgradeEditor;

public sealed partial class MainWindow
{
    private Control BuildCombinedPokemonPage()
    {
        var tabs = new TabControl
        {
            ItemsSource = new List<TabItem>
            {
                new() { Header = "Pokémon data", Content = BuildPokemonPage() },
                new() { Header = "Evolutions", Content = BuildEvolutionPage() },
                new() { Header = "Sprites & palettes", Content = BuildSpritePage() }
            }
        };
        return tabs;
    }

    // Avalonia's ComboBox is not editable. A separate search field filters its
    // items while retaining the selected numeric species ID.
    private (TextBox Search, ComboBox Picker) CreateSearchableSpeciesPicker(int initialSpecies)
    {
        var names = Enumerable.Range(1, 721).Select(SpeciesLabel).ToList();
        var search = new TextBox { Watermark = "Search Pokémon by name or #..." };
        var picker = new ComboBox { ItemsSource = names, SelectedItem = names[Math.Clamp(initialSpecies, 1, 721) - 1], MinWidth = 280 };
        search.TextChanged += (_, _) =>
        {
            string? selected = picker.SelectedItem as string;
            var filtered = names.Where(x => x.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToList();
            picker.ItemsSource = filtered;
            if (selected != null && filtered.Contains(selected)) picker.SelectedItem = selected;
            else if (filtered.Count > 0) picker.SelectedIndex = 0;
        };
        return (search, picker);
    }

    private static int SelectedSpecies(ComboBox picker)
    {
        if (picker.SelectedItem is not string name || name.Length < 4 || name[0] != '#' ||
            !int.TryParse(name.Substring(1, 3), out int id) || id is < 1 or > 721)
            throw new InvalidDataException("Select a Pokémon from the dropdown.");
        return id;
    }

    private Control BuildMiscSpritePage()
    {
        var root = new StackPanel { Spacing = 12 };
        root.Children.Add(new TextBlock { Text = "Miscellaneous sprite resources", Classes = { "section" } });
        root.Children.Add(Muted("Explore and export non-Pokémon sprite resources from NitroFS. Raw imports are restricted to matching byte lengths; resource layouts vary between archives."));
        var paths = _workspace!.ListFiles().Where(x => x.Replace('\\','/').Contains("/a/", StringComparison.OrdinalIgnoreCase) || x.StartsWith("a/",StringComparison.OrdinalIgnoreCase)).ToList();
        var archivePath = new TextBox { Watermark = "NARC archive path, e.g. a/0/2/0", Text = "a/0/2/0", Width = 310 };
        var open = new Button { Content = "Open archive" };
        var members = new ListBox { Height = 320, MinWidth = 260 };
        var info = Muted("Open an archive to inspect its entries.");
        NarcArchive? archive = null;
        void Refresh()
        {
            try
            {
                string path = (archivePath.Text ?? "").Trim().TrimStart('/');
                archive = _workspace.GetArchive(path);
                members.ItemsSource = Enumerable.Range(0, archive.Count).Select(i => $"Member {i:D5} · {archive.GetMember(i).Length:N0} bytes").ToList();
                info.Text = $"{path}: {archive.Count:N0} entries. Select a member to export or replace raw bytes.";
            }
            catch(Exception ex) { archive = null; members.ItemsSource = null; info.Text = "Cannot open NARC: " + ex.Message; }
        }
        open.Click += (_, _) => Refresh();
        var export = new Button { Content = "Export selected resource" };
        export.Click += async (_, _) =>
        {
            try
            {
                if(archive is null || members.SelectedIndex < 0) return;
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = $"resource-{members.SelectedIndex:D5}.bin" });
                if(file is null) return;
                await using var stream = await file.OpenWriteAsync();
                await stream.WriteAsync(archive.GetMember(members.SelectedIndex));
                info.Text = "Raw resource exported.";
            }
            catch(Exception ex) { info.Text = ex.Message; }
        };
        var import = new Button { Content = "Import raw resource (same size)" };
        import.Click += async (_, _) =>
        {
            try
            {
                if(archive is null || members.SelectedIndex < 0) return;
                int index = members.SelectedIndex;
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, Title = "Import raw sprite resource" });
                if(files.Count == 0) return;
                await using var stream = await files[0].OpenReadAsync();
                using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer);
                byte[] data = buffer.ToArray();
                if(data.Length != archive.GetMember(index).Length)
                    throw new InvalidDataException("Size mismatch. Raw resource import requires the exact original length.");
                archive.SetMember(index, data);
                info.Text = $"Replaced member {index} in memory. Save a patch or ROM to retain it.";
            }
            catch(Exception ex) { info.Text = "Import failed: " + ex.Message; }
        };
        root.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { archivePath, open } });
        root.Children.Add(members);
        root.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { export, import } });
        root.Children.Add(info);
        root.Children.Add(Muted("This is a raw resource browser, not yet a decoded trainer/overworld sprite canvas. Unknown archive layouts are deliberately not interpreted as Pokémon battle sprites."));
        return new ScrollViewer { Content = root };
    }
}
