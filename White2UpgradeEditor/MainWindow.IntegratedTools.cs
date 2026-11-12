using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace White2UpgradeEditor;

public sealed partial class MainWindow
{
    private TypeChartEngine.Inspection? _lastChartInspection;

    private async Task<bool> ConfirmToolActionAsync(string title, string description, string yes = "Yes, create a repaired copy")
    {
        var window = new Window
        {
            Title = title,
            Width = 560,
            Height = 265,
            MinWidth = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        var body = new StackPanel { Margin = new Thickness(22), Spacing = 17 };
        body.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 18 });
        body.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, FontSize = 13 });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 12 };
        var no = new Button { Content = "No, leave my ROM unchanged", Padding = new Thickness(12, 7) };
        var accept = new Button { Content = yes, Padding = new Thickness(12, 7) };
        accept.Classes.Add("primary");
        no.Click += (_, _) => window.Close(false);
        accept.Click += (_, _) => window.Close(true);
        buttons.Children.Add(no);
        buttons.Children.Add(accept);
        body.Children.Add(buttons);
        window.Content = body;
        return await window.ShowDialog<bool>(this);
    }

    private async Task LoadValidatedWorkspaceAsync(RomWorkspace candidate, string source, bool extracted)
    {
        TypeChartEngine.Inspection inspection = TypeChartEngine.Inspect(source, extracted);
        if (!inspection.White2UpgradeVerified)
            throw new InvalidDataException(
                "WHITE 2 UPGRADE ONLY: This editor cannot verify the selected file as White 2 Upgrade. " +
                "Ordinary Pokémon White 2, Black 2, other ROM hacks, and unverified revisions are not supported.\n" +
                inspection.Detail);
        _workspace = candidate;
        _lastChartInspection = inspection;
        _speciesNamesWorkspace = null;
        _save.IsEnabled = true;
        _exportXdelta.IsEnabled = true;
        SetStatus("Verified White 2 Upgrade workspace. " + inspection.Detail);
        ShowPage("Overview");
        if (inspection.CanRepair)
            await OfferTypeChartRepairAsync(source, extracted);
        else if (inspection.State == TypeChartEngine.Health.Unknown)
            SetStatus("White 2 Upgrade loaded, but its battle chart is unrecognized. See Type Matchups; no automatic repair was made.");
    }

    private async Task OfferTypeChartRepairAsync(string source, bool extracted)
    {
        if (_lastChartInspection?.CanRepair != true) return;
        bool approve = await ConfirmToolActionAsync(
            "White 2 Upgrade type chart needs repair",
            "A verified 17×17 chart was detected, but this White 2 Upgrade build includes Fairy. " +
            "This can produce wrong effectiveness such as Normal moves being super effective against Fairy.\n\n" +
            "Would you like to create a NEW copy with the verified 18×18 chart and corrected lookup code? " +
            "Your original ROM will never be overwritten.");
        if (!approve)
        {
            SetStatus("17×17 type chart left unchanged at your request. You can repair it from Type Matchups later.");
            return;
        }
        await RepairTypeChartAsync(source, extracted);
    }

    private async Task RepairTypeChartAsync(string source, bool extracted)
    {
        try
        {
            if (_workspace?.DirtyArchiveCount > 0)
            {
                SetStatus("Apply/save or discard outstanding Pokémon/trainer edits before repairing the battle overlay.");
                return;
            }
            string destination;
            if (extracted)
            {
                var choices = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Choose parent folder for a NEW 18x18 patch directory",
                    AllowMultiple = false
                });
                if (choices.Count == 0) return;
                destination = Path.Combine(choices[0].Path.LocalPath, "White2Upgrade-18x18-typechart-patch");
                int suffix = 2;
                while (Directory.Exists(destination))
                    destination = Path.Combine(choices[0].Path.LocalPath, $"White2Upgrade-18x18-typechart-patch-{suffix++}");
            }
            else
            {
                var choice = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save NEW White 2 Upgrade ROM with repaired 18x18 chart",
                    SuggestedFileName = "White2Upgrade-typechart-18x18-fixed.nds",
                    DefaultExtension = "nds",
                    FileTypeChoices = [new FilePickerFileType("Nintendo DS ROM") { Patterns = ["*.nds"] }]
                });
                if (choice is null) return;
                destination = choice.Path.LocalPath;
            }
            TypeChartEngine.RepairToNewCopy(source, destination, extracted);
            if (!extracted)
            {
                var checkedSource = RomWorkspace.OpenRom(destination);
                _workspace = checkedSource;
                _speciesNamesWorkspace = null;
                _lastChartInspection = TypeChartEngine.Inspect(destination);
            }
            SetStatus("Created verified 18×18 type chart repair at " + destination +
                ". Test effectiveness in-game before relying on it.");
            ShowPage("Type Matchups");
        }
        catch (Exception ex) { SetStatus("Type chart repair failed without changing the input: " + ex.Message); }
    }

    private Control BuildTypeChartPage()
    {
        if (_workspace is null) return BuildLocked("Type matchups");
        var inspection = _lastChartInspection;
        if (inspection is null) return BuildLocked("Type matchups");
        var root = new StackPanel { Spacing = 14 };
        root.Children.Add(new TextBlock { Text = "Battle type effectiveness", Classes = { "section" } });
        root.Children.Add(Muted("White 2 Upgrade only · overlay 167 · attacking type → defending type. A verified 17×17 chart is offered an opt-in 18×18 repair when the ROM opens."));
        var state = inspection.State switch
        {
            TypeChartEngine.Health.ExpandedHealthy => "18×18 chart present and verified",
            TypeChartEngine.Health.LegacyNeedsRepair => "17×17 chart detected — Fairy row and column missing",
            _ => "Unrecognized battle overlay — automatic repair disabled"
        };
        root.Children.Add(Card(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = state, FontSize = 16, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = inspection.Detail, TextWrapping = TextWrapping.Wrap },
                Muted("The inspector checks known table bytes and code patches. A byte-level match is not a substitute for testing damage and messages in a battle.")
            }
        }));
        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var retry = new Button { Content = "Recheck type chart" };
        retry.Click += (_, _) =>
        {
            try
            {
                string? source = _workspace.OpenedRomPath;
                if (source is null) source = _workspace.SourceFolderPath;
                if (source is null) return;
                _lastChartInspection = TypeChartEngine.Inspect(source, _workspace.IsExtractedFolder);
                ShowPage("Type Matchups");
            }
            catch (Exception ex) { SetStatus("Type chart check failed: " + ex.Message); }
        };
        options.Children.Add(retry);
        var exportReport = new Button { Content = "Export diagnostic report" };
        exportReport.Click += async (_, _) =>
        {
            try
            {
                var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save White 2 Upgrade type chart diagnostic report",
                    SuggestedFileName = "White2Upgrade-typechart-report.txt",
                    DefaultExtension = "txt",
                    FileTypeChoices = [new FilePickerFileType("Text report") { Patterns = ["*.txt"] }]
                });
                if (target is null) return;
                var report = new StringBuilder();
                report.AppendLine("WHITE 2 UPGRADE — TYPE CHART INSPECTOR");
                report.AppendLine(inspection.Detail);
                report.AppendLine($"Verified White 2 Upgrade: {inspection.White2UpgradeVerified}");
                report.AppendLine($"Chart dimensions: {inspection.Dimensions}×{inspection.Dimensions}");
                if (inspection.Matrix is { } entries)
                {
                    for (int i = 0; i < inspection.Dimensions; i++)
                    {
                        report.Append(TypeChartEngine.Types[i]).Append(" attacking: ");
                        report.AppendLine(string.Join(", ", Enumerable.Range(0, inspection.Dimensions)
                            .Select(j => TypeChartEngine.Types[j] + "=" +
                                TypeChartEngine.Multiplier(entries[i * inspection.Dimensions + j]))));
                    }
                }
                File.WriteAllText(target.Path.LocalPath, report.ToString());
                SetStatus("Type chart report saved to " + target.Path.LocalPath);
            }
            catch (Exception ex) { SetStatus("Could not export chart report: " + ex.Message); }
        };
        options.Children.Add(exportReport);
        if (inspection.CanRepair)
        {
            var fix = PrimaryButton("Fix to 18×18 (ask first)", async () =>
            {
                string? source = _workspace.OpenedRomPath ?? _workspace.SourceFolderPath;
                if (source is not null) await OfferTypeChartRepairAsync(source, _workspace.IsExtractedFolder);
            });
            options.Children.Add(fix);
        }
        root.Children.Add(options);
        if (inspection.Matrix is { } chart && inspection.Dimensions >= 17)
        {
            var picker = new ComboBox { ItemsSource = TypeChartEngine.Types, SelectedIndex = 0, Width = 230 };
            var rows = new StackPanel { Spacing = 4 };
            void Render()
            {
                rows.Children.Clear();
                int attacker = Math.Max(0, picker.SelectedIndex);
                for (int defender = 0; defender < TypeChartEngine.Types.Length; defender++)
                {
                    string effective = attacker < inspection.Dimensions && defender < inspection.Dimensions
                        ? TypeChartEngine.Multiplier(chart[attacker * inspection.Dimensions + defender])
                        : "Missing from 17×17 chart";
                    var item = new Grid { ColumnDefinitions = new ColumnDefinitions("190,*") };
                    item.Children.Add(new TextBlock { Text = TypeChartEngine.Types[defender], Margin = new Thickness(8, 6), VerticalAlignment = VerticalAlignment.Center });
                    var value = new TextBlock { Text = effective, Margin = new Thickness(8, 6), FontWeight = FontWeight.SemiBold };
                    Grid.SetColumn(value, 1);
                    item.Children.Add(value);
                    rows.Children.Add(item);
                }
            }
            picker.SelectionChanged += (_, _) => Render();
            Render();
            root.Children.Add(Card(new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Read-only matchup inspector", FontSize = 15, FontWeight = FontWeight.SemiBold },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12,
                        Children = { Muted("Attacking type"), picker } },
                    new TextBlock { Text = "Defending type                         Damage multiplier", FontSize = 11 },
                    rows
                }
            }));
        }
        return new ScrollViewer { Content = root };
    }

    private Control BuildStarterScreenPage()
    {
        if (_workspace is null) return BuildLocked("Starter menu sync");
        var root = new StackPanel { Spacing = 14 };
        root.Children.Add(new TextBlock { Text = "Starter selection screen", Classes = { "section" } });
        root.Children.Add(Muted("Synchronize the three starter-selection sprites, normal/dark palettes, and the bottom type-description lines after editing starters."));
        root.Children.Add(Muted("This experimental graphics companion currently requires Python 3. It runs from the main editor; no separate launcher is needed. Export a NEW .nds, then test the starter selection screen."));
        var openStarters = new Button { Content = "1. Edit starter choices" };
        openStarters.Click += (_, _) => ShowPage("Starters + Rival");
        root.Children.Add(openStarters);
        var run = PrimaryButton("2. Synchronize menu graphics and descriptions", async () =>
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Choose already-edited White 2 Upgrade ROM",
                    AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("Nintendo DS ROM") { Patterns = ["*.nds"] }]
                });
                if (files.Count == 0) return;
                string source = files[0].Path.LocalPath;
                if (!TypeChartEngine.Inspect(source).White2UpgradeVerified)
                    throw new InvalidDataException("This tool only supports verified White 2 Upgrade ROMs.");
                var output = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save NEW ROM with synchronized starter menu",
                    SuggestedFileName = "White2Upgrade-starter-menu-synced.nds",
                    FileTypeChoices = [new FilePickerFileType("Nintendo DS ROM") { Patterns = ["*.nds"] }]
                });
                if (output is null) return;
                await RunStarterSyncAsync(source, output.Path.LocalPath);
                SetStatus("Starter menu copy saved at " + output.Path.LocalPath);
            }
            catch (Exception ex) { SetStatus("Starter menu sync: " + ex.Message); }
        });
        root.Children.Add(run);
        root.Children.Add(Muted("If the source ROM has not been saved since editing starters, the menu sync cannot see those in-memory changes. Save to a new ROM first."));
        return new ScrollViewer { Content = Card(root) };
    }

    private static async Task RunStarterSyncAsync(string source, string output)
    {
        string script = Path.Combine(AppContext.BaseDirectory, "Tools", "starter_menu_sync.py");
        if (!File.Exists(script)) throw new FileNotFoundException("The bundled starter_menu_sync.py tool is missing.", script);
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The source ROM cannot be overwritten.");
        if (File.Exists(output)) throw new IOException("Output already exists. Choose another filename.");
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "py" : "python3")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows()) start.ArgumentList.Add("-3");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("--export");
        start.ArgumentList.Add(source);
        start.ArgumentList.Add(output);
        using var process = Process.Start(start)
            ?? throw new IOException("Could not start Python 3. Install Python 3 to use the starter graphics companion.");
        Task<string> stdOut = process.StandardOutput.ReadToEndAsync();
        Task<string> stdErr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string err = await stdErr;
        string info = await stdOut;
        if (process.ExitCode != 0 || !File.Exists(output))
            throw new IOException("Starter menu sync failed: " + err + " " + info);
    }
}
