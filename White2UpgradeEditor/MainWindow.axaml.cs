using System.Diagnostics;
using System.Reflection;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GdiBitmap = System.Drawing.Bitmap;
using GdiColor = System.Drawing.Color;

namespace White2UpgradeEditor;

public sealed partial class MainWindow : Window
{
    private RomWorkspace? _workspace;
    private int _selectedPokemonId = 1;
    private SpeciesNames? _speciesNames;
    private string SpeciesName(int id) => _speciesNames?.Get(id) ?? $"Species #{id:000}";
    private string SpeciesLabel(int id) => _speciesNames?.Label(id) ?? $"#{id:000}   Species {id:000}";
    private readonly Dictionary<string, Button> _nav = new();
    private string _page = "Overview";
    private readonly TextBlock _status;
    private readonly TextBlock _title;
    private readonly TextBlock _subtitle;
    private readonly ContentControl _host;
    private readonly Button _save;

    public MainWindow()
    {
        InitializeComponent();
        _status = this.FindControl<TextBlock>("StatusText")!;
        _title = this.FindControl<TextBlock>("PageTitle")!;
        _subtitle = this.FindControl<TextBlock>("PageSubtitle")!;
        _host = this.FindControl<ContentControl>("PageHost")!;
        _save = this.FindControl<Button>("SaveButton")!;
        this.FindControl<Button>("OpenDumpButton")!.Click += async (_, _) => await OpenDumpAsync();
        this.FindControl<Button>("OpenRomButton")!.Click += async (_, _) => await OpenRomAsync();
        _save.Click += async (_, _) => await SaveAsync();
        this.FindControl<Button>("CheckUpdatesButton")!.Click += (_, _) => OpenUpdater();
        BuildNavigation();
        ShowPage("Overview");
    }

    private void OpenUpdater()
    {
        // The separate updater never forces the editor closed: users can save their work first.
        var executable = Path.Combine(AppContext.BaseDirectory, "White2UpgradeUpdater.exe");
        if (!File.Exists(executable))
        {
            SetStatus("Updater not found. Download a full release package containing White2UpgradeUpdater.exe.");
            return;
        }
        try
        {
            var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var version = (info ?? "0.1.0-alpha.1").Split('+')[0];
            var start = new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory };
            start.ArgumentList.Add("--install-dir");
            start.ArgumentList.Add(AppContext.BaseDirectory);
            start.ArgumentList.Add("--current-version");
            start.ArgumentList.Add(version);
            start.ArgumentList.Add("--editor-pid");
            start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            Process.Start(start);
            SetStatus("Updater opened. Save and close the editor only if you choose to install an update.");
        }
        catch (Exception ex) { SetStatus("Couldn't launch updater: " + ex.Message); }
    }

    private void InitializeComponent() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

    private void BuildNavigation()
    {
        var host = this.FindControl<StackPanel>("Navigation")!;
        var entries = new (string Icon, string Name)[]
        {
            ("⌂", "Overview"), ("◈", "Pokémon"),
            ("♟", "Trainers"), ("▦", "Maps + Events"), ("✦", "Starters + Rival"), ("▧", "Misc Sprites"), ("▤", "ROM Files")
        };
        foreach (var (icon, name) in entries)
        {
            var button = new Button { Content = $"{icon}    {name}", HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Classes.Add("nav");
            button.Click += (_, _) => ShowPage(name);
            host.Children.Add(button);
            _nav[name] = button;
        }
    }

    private void ShowPage(string name)
    {
        _page = name;
        foreach (var pair in _nav) pair.Value.Background = pair.Key == name ? Brush("#1B2A42") : Brushes.Transparent;
        _title.Text = name;
        _subtitle.Text = _workspace is null ? "Open a ROM or extracted dump to enable editing" : _workspace.Description;
        _host.Content = name switch
        {
            "Overview" => BuildOverview(),
            "Pokémon" => _workspace is null ? BuildLocked("Pokémon data") : BuildCombinedPokemonPage(),
            "Trainers" => _workspace is null ? BuildLocked("Trainer data") : BuildTrainerPage(),
            "Maps + Events" => _workspace is null ? BuildLocked("Zone events") : BuildMapPage(),
            "Starters + Rival" => _workspace is null ? BuildLocked("Starter and rival data") : BuildStarterPage(),
            "Misc Sprites" => _workspace is null ? BuildLocked("Miscellaneous sprite resources") : BuildMiscSpritePage(),
            "ROM Files" => _workspace is null ? BuildLocked("NitroFS browser") : BuildFilesPage(),
            _ => BuildOverview()
        };
    }

    private Control BuildOverview()
    {
        var root = new StackPanel { Spacing = 18 };
        var hero = Card(new StackPanel { Spacing = 10, Children =
        {
            new TextBlock { Text = "A focused workspace for White 2 Upgrade", FontSize = 26, FontWeight = FontWeight.Bold, Foreground = Brush("#F4F7FC") },
            new TextBlock { Text = "Edit expanded Gen V data with archive-aware tools, clear validation and a safer save workflow.", FontSize = 13, Foreground = Brush("#98A7BC"), TextWrapping = TextWrapping.Wrap },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children =
            {
                PrimaryButton("Open extracted dump", async () => await OpenDumpAsync()),
                new Button { Content = "Open Nintendo DS ROM" }
            }}
        }});
        var actions = (StackPanel)((StackPanel)hero.Child!).Children[2];
        ((Button)actions.Children[1]).Click += async (_, _) => await OpenRomAsync();
        root.Children.Add(hero);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 14, RowSpacing = 14 };
        var cards = new (string Head, string Body)[]
        {
            ("Pokémon data", "76-byte personal records · stats, types, abilities, EV yields, items and compatibility"),
            ("Evolution methods", "Seven evolution slots per species · method, parameter and target species"),
            ("Trainer parties", "Trainer metadata, party members, levels, forms, held items and custom moves"),
            ("Starters + Hugh", "Verified starter script edits with optional automatic rival-team updates"),
            ("Battle sprites", "96×96 front/back sprites · PNG import/export and palette adjustment"),
            ("ROM files", "Browse NitroFS entries and inspect file metadata without guessing offsets")
        };
        for (int i = 0; i < cards.Length; i++)
        {
            var c = Card(new StackPanel { Spacing = 7, Children =
            {
                new TextBlock { Text = cards[i].Head, FontSize = 15, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = cards[i].Body, FontSize = 11, Foreground = Brush("#8998AE"), TextWrapping = TextWrapping.Wrap }
            }});
            Grid.SetColumn(c, i % 2); Grid.SetRow(c, i / 2); grid.Children.Add(c);
        }
        root.Children.Add(grid);
        root.Children.Add(new TextBlock { Text = _workspace is null ? "No workspace loaded · Choose an extracted dump or .nds file to begin." : _workspace.Description, Foreground = Brush("#8B9AB0"), FontSize = 11 });
        return new ScrollViewer { Content = root };
    }

    private Control BuildLocked(string feature) => Card(new StackPanel { Spacing = 8, Children =
    {
        new TextBlock { Text = $"{feature} is waiting for a workspace", FontSize = 18, FontWeight = FontWeight.SemiBold },
        new TextBlock { Text = "Open the extracted White 2 Upgrade dump or a compatible .nds ROM using the buttons in the title bar.", Foreground = Brush("#8B9AB0"), TextWrapping = TextWrapping.Wrap }
    }});

    private RomWorkspace? _speciesNamesWorkspace;
    private void EnsureSpeciesNames()
    {
        if (ReferenceEquals(_speciesNamesWorkspace, _workspace)) return;
        _speciesNamesWorkspace = _workspace;
        _speciesNames = null;
        if (_workspace is null) return;
        try { _speciesNames = SpeciesNames.Load(_workspace); }
        catch (Exception ex) { SetStatus("Species names unavailable; using numeric IDs: " + ex.Message); }
    }

    private Control BuildPokemonPage()
    {
        EnsureSpeciesNames();
        var archive = _workspace!.GetArchive("a/0/1/6");
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("270,*"), ColumnSpacing = 14 };
        var left = Card(new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), Children =
        {
            new TextBlock { Text = "SPECIES", FontSize = 10, FontWeight = FontWeight.Bold, Foreground = Brush("#7E8DA4"), Margin = new Thickness(4,0,0,8) }
        }});
        var leftGrid = (Grid)left.Child!;
        var search = new TextBox { Watermark = "Search by number or species name…", Margin = new Thickness(0,28,0,8) };
        Grid.SetRow(search, 1); leftGrid.Children.Add(search);
        var list = new ListBox(); Grid.SetRow(list, 2); leftGrid.Children.Add(list);
        var species = Enumerable.Range(1, 721).Select(SpeciesLabel).ToList();
        list.ItemsSource = species;
        search.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) list.ItemsSource = species.Where(s => s.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToList(); };

        var bindings = BuildPersonalBindings();
        var editors = new Dictionary<string, TextBox>();
        var fieldStack = new StackPanel { Spacing = 7 };
        foreach (var b in bindings)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("250,*"), ColumnSpacing = 10 };
            row.Children.Add(new TextBlock { Text = b.Name, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Foreground = Brush("#AAB7CA") });
            var input = new TextBox { Text = "0", MinWidth = 150 };
            Grid.SetColumn(input, 1); row.Children.Add(input); fieldStack.Children.Add(row); editors[b.Name] = input;
        }
        var info = Muted("Select a species to inspect its personal data.");
        int currentId = _selectedPokemonId; bool formDirty = false; bool restoringSelection = false;
        foreach (var input in editors.Values) input.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) { formDirty = true; info.Text = "Unsaved form edits · apply or discard before switching species."; } };
        var apply = PrimaryButton("Apply Pokémon changes", () =>
        {
            if (list.SelectedItem is not string selected) return;
            try
            {
                int id = int.Parse(selected.AsSpan(1, 3), CultureInfo.InvariantCulture);
                byte[] data = archive.GetMember(id).ToArray();
                foreach (var b in bindings) b.Set(data, editors[b.Name].Text ?? "0");
                archive.SetMember(id, data); currentId = id; formDirty = false; info.Text = $"Applied {SpeciesName(id)} (#{id:000}) in memory · personal record 0x4C bytes"; SetStatus($"Pokémon #{id:000} changed in memory");
            }
            catch (Exception ex) { info.Text = "Could not apply: " + ex.Message; }
        });
        list.SelectionChanged += (_, _) =>
        {
            if (restoringSelection || list.SelectedItem is not string selected) return;
            try
            {
                int id = int.Parse(selected.AsSpan(1, 3), CultureInfo.InvariantCulture);
                if (formDirty && id != currentId) { restoringSelection = true; list.SelectedItem = species.FirstOrDefault(x => x.StartsWith($"#{currentId:000}", StringComparison.Ordinal)); restoringSelection = false; info.Text = "Apply the current form edits before selecting another species."; return; }
                byte[] data = archive.GetMember(id);
                if (data.Length != 76) throw new InvalidDataException($"Record length {data.Length}; expected 76.");
                foreach (var b in bindings) editors[b.Name].Text = b.Get(data);
                _selectedPokemonId = id; currentId = id; formDirty = false; info.Text = $"{SpeciesName(id)} (#{id:000}) · Personal data record · 76 bytes";
            }
            catch (Exception ex) { info.Text = ex.Message; }
        };
        list.SelectedItem = species[_selectedPokemonId - 1];
        var rightContent = new StackPanel { Spacing = 14, Children =
        {
            new StackPanel { Spacing = 4, Children = { new TextBlock { Text = "Personal data", Classes = { "section" } }, Muted("Values are stored using the verified 76-byte White 2 Upgrade personal-record layout.") } },
            Card(new StackPanel { Spacing = 8, Children = { fieldStack } }),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { apply, info } }
        }};
        var right = new ScrollViewer { Content = rightContent };
        Grid.SetColumn(left, 0); Grid.SetColumn(right, 1); root.Children.Add(left); root.Children.Add(right);
        return root;
    }

    private sealed record PersonalBinding(string Name, Func<byte[], string> Get, Action<byte[], string> Set);
    private static List<PersonalBinding> BuildPersonalBindings()
    {
        var a = new List<PersonalBinding>();
        void B(string n, int o) => a.Add(new(n, b => b[o].ToString(CultureInfo.InvariantCulture), (b,v) => b[o] = (byte)Parse(v,255)));
        void U16(string n, int o) => a.Add(new(n, b => BitConverter.ToUInt16(b,o).ToString(CultureInfo.InvariantCulture), (b,v) => BitConverter.GetBytes((ushort)Parse(v,65535)).CopyTo(b,o)));
        void U32(string n, int o) => a.Add(new(n, b => BitConverter.ToUInt32(b,o).ToString(CultureInfo.InvariantCulture), (b,v) => BitConverter.GetBytes(Parse(v,uint.MaxValue)).CopyTo(b,o)));
        string[] stats = ["HP","Attack","Defense","Speed","Special Attack","Special Defense"];
        for (int i=0;i<6;i++) B(stats[i],i);
        B("Type 1 ID",6); B("Type 2 ID",7); B("Catch rate",8); B("Evolution stage",9);
        string[] ev = ["EV yield · HP","EV yield · Attack","EV yield · Defense","EV yield · Speed","EV yield · Sp. Attack","EV yield · Sp. Defense"];
        for(int i=0;i<6;i++){int bit=i*2; a.Add(new(ev[i],b=>((BitConverter.ToUInt16(b,10)>>bit)&3).ToString(),(b,v)=>{int n=(int)Parse(v,3);ushort x=BitConverter.ToUInt16(b,10);x=(ushort)((x&~(3<<bit))|(n<<bit));BitConverter.GetBytes(x).CopyTo(b,10);}));}
        a.Add(new("Underground flag",b=>((BitConverter.ToUInt16(b,10)>>12)&1).ToString(),(b,v)=>{int n=(int)Parse(v,1);ushort x=BitConverter.ToUInt16(b,10);x=(ushort)((x&~(1<<12))|(n<<12));BitConverter.GetBytes(x).CopyTo(b,10);}));
        U16("Held item · common",12);U16("Held item · rare",14);U16("Held item · very rare",16);
        B("Gender ratio",18);B("Egg hatch cycles",19);B("Base friendship",20);B("Growth rate ID",21);B("Egg group 1 ID",22);B("Egg group 2 ID",23);B("Ability 1 ID",24);B("Ability 2 ID",25);B("Hidden ability ID",26);B("Flee rate",27);
        U16("First form record index",28);U16("Sprite form offset",30);B("Form count",32);B("Color / symmetry flags",33);U16("Base experience",34);U16("Height (tenths of metre)",36);U16("Weight (tenths of kilogram)",38);
        for(int i=0;i<4;i++)U32($"TM/HM compatibility word {i+1}",40+i*4);for(int i=0;i<5;i++)U32($"Tutor compatibility word {i+1}",56+i*4);
        return a;
    }

    private static readonly (int Id, string Name, string Hint)[] EvolutionMethods =
    [
        (0,"None / unused", "No evolution in this slot"),
        (1,"High friendship", "Parameter normally 0"),
        (2,"High friendship · daytime", "Parameter normally 0"),
        (3,"High friendship · nighttime", "Parameter normally 0"),
        (4,"Level up", "Parameter = required level, e.g. 16"),
        (5,"Trade", "Parameter normally 0"),
        (6,"Trade holding item", "Parameter = held item ID"),
        (7,"Use evolution item", "Parameter = item ID")
    ];

    private Control BuildEvolutionPage()
    {
        EnsureSpeciesNames();
        var archive = _workspace!.GetArchive("a/0/1/9");
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("260,*"), ColumnSpacing = 14 };
        var species = Enumerable.Range(1,721).Select(SpeciesLabel).ToList();
        var search = new TextBox { Watermark = "Search Pokémon..." };
        var list = new ListBox { ItemsSource = species };
        search.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) list.ItemsSource = species.Where(x => x.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToList(); };
        var left = Card(new StackPanel { Spacing = 10, Children = { Muted("SPECIES"), search, list } });
        var rows = new StackPanel { Spacing = 12 };
        var editors = new List<(ComboBox Method, TextBox Raw, TextBox Param, ComboBox Target, TextBlock Hint)>();
        var targets = Enumerable.Range(0, 722).Select(i => i == 0 ? "#000   None" : SpeciesLabel(i)).ToList();
        for (int i=0;i<7;i++)
        {
            var method = new ComboBox { ItemsSource = EvolutionMethods.Select(m => $"{m.Id}: {m.Name}").Append("Other / custom method ID").ToList(), SelectedIndex = 0, MinWidth = 180 };
            var raw = new TextBox { Watermark = "Method ID", Width = 85, IsVisible = false };
            var param = new TextBox { Watermark = "Level / item ID", Width = 115 };
            var target = new ComboBox { ItemsSource = targets, SelectedIndex = 0, MinWidth = 190 };
            var hint = Muted("Choose how the Pokémon evolves.");
            method.SelectionChanged += (_, _) =>
            {
                raw.IsVisible = method.SelectedIndex == EvolutionMethods.Length;
                hint.Text = method.SelectedIndex >= 0 && method.SelectedIndex < EvolutionMethods.Length
                    ? EvolutionMethods[method.SelectedIndex].Hint : "Custom method: enter its numeric ID. Verify hack-specific behavior first.";
            };
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, Children = { method, raw, param, target } };
            rows.Children.Add(Card(new StackPanel { Spacing = 6, Children =
            {
                new TextBlock { Text = $"Evolution {i+1}", FontWeight = FontWeight.SemiBold }, controls, hint
            }}));
            editors.Add((method, raw, param, target, hint));
        }
        var note = Muted("Choose a species to see its evolution methods.");
        int currentId = _selectedPokemonId;
        bool loading = false, dirty = false;
        foreach (var e in editors)
        {
            e.Method.SelectionChanged += (_, _) => { if (!loading) dirty = true; };
            e.Target.SelectionChanged += (_, _) => { if (!loading) dirty = true; };
            e.Raw.PropertyChanged += (_, ev) => { if (!loading && ev.Property == TextBox.TextProperty) dirty = true; };
            e.Param.PropertyChanged += (_, ev) => { if (!loading && ev.Property == TextBox.TextProperty) dirty = true; };
        }
        void Load(int id)
        {
            loading = true;
            try
            {
                var d = archive.GetMember(id);
                for (int i=0;i<7;i++)
                {
                    int o=i*6, m=BitConverter.ToUInt16(d,o), p=BitConverter.ToUInt16(d,o+2), t=BitConverter.ToUInt16(d,o+4);
                    var e = editors[i];
                    int known = Array.FindIndex(EvolutionMethods, x => x.Id == m);
                    e.Method.SelectedIndex = known >= 0 ? known : EvolutionMethods.Length;
                    e.Raw.Text = m.ToString(); e.Param.Text = p.ToString();
                    e.Target.SelectedIndex = t <= 721 ? t : 0;
                    if (t > 721) e.Hint.Text = $"Target ID {t} is outside the supported species list; avoid applying unless intentionally changing it.";
                }
                _selectedPokemonId=id; currentId=id; dirty=false;
                note.Text=$"Editing {SpeciesName(id)} (#{id:000}). Select a method by name, then fill in the required level or item ID.";
            }
            finally { loading=false; }
        }
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not string label) return;
            int id = int.Parse(label.AsSpan(1,3));
            if (dirty && id != currentId)
            {
                note.Text = "Apply changes or use Reload before changing Pokémon.";
                loading=true; list.SelectedItem=species.FirstOrDefault(x=>x.StartsWith($"#{currentId:000}")); loading=false;
                return;
            }
            if (!loading) Load(id);
        };
        var apply = PrimaryButton("Apply evolution changes", () =>
        {
            try
            {
                var d=archive.GetMember(currentId).ToArray();
                for(int i=0;i<7;i++)
                {
                    var e=editors[i]; int o=i*6;
                    int method=e.Method.SelectedIndex == EvolutionMethods.Length ? (int)Parse(e.Raw.Text,65535) : EvolutionMethods[e.Method.SelectedIndex].Id;
                    int param=(int)Parse(e.Param.Text,65535);
                    int target=e.Target.SelectedIndex;
                    if (method != 0 && target == 0) throw new InvalidDataException($"Evolution {i+1}: select a target Pokémon.");
                    BitConverter.GetBytes((ushort)method).CopyTo(d,o);
                    BitConverter.GetBytes((ushort)param).CopyTo(d,o+2);
                    BitConverter.GetBytes((ushort)target).CopyTo(d,o+4);
                }
                archive.SetMember(currentId,d);dirty=false;
                note.Text=$"Applied {SpeciesName(currentId)} evolution changes in memory. Save / Export to write them.";
            }
            catch(Exception ex){note.Text="Cannot apply: "+ex.Message;}
        });
        var reload = new Button { Content="Discard form edits / Reload" };
        reload.Click += (_, _) => Load(currentId);
        var right = new ScrollViewer { Content = new StackPanel { Spacing = 12, Children =
        {
            new TextBlock { Text="Evolution rules", FontSize=18, FontWeight=FontWeight.SemiBold },
            Muted("Common Gen V methods are labeled. Custom IDs are preserved with the 'Other' option. Item parameters still use numeric item IDs."),
            rows, new StackPanel { Orientation=Orientation.Horizontal, Spacing=10, Children={apply,reload} }, note
        } } };
        Grid.SetColumn(right,1);root.Children.Add(left);root.Children.Add(right);
        list.SelectedItem=species[_selectedPokemonId-1];
        return root;
    }

    private sealed record TrainerListEntry(int Id, string Label)
    {
        public override string ToString() => Label;
    }

    private Control BuildTrainerPage()
    {
        EnsureSpeciesNames();
        var dataArchive=_workspace!.GetArchive("a/0/9/1");var partyArchive=_workspace.GetArchive("a/0/9/2");
        if(dataArchive.Count!=partyArchive.Count) return BuildLocked("Trainer archives (member counts do not match)");
        var root=new Grid{ColumnDefinitions=new ColumnDefinitions("280,*"),ColumnSpacing=14};
        TrainerNames? trainerNames = null;
        string nameStatus = "";
        try { trainerNames = TrainerNames.Load(_workspace, dataArchive.Count); }
        catch (Exception ex) { nameStatus = "Trainer names unavailable: " + ex.Message; }
        var entries = Enumerable.Range(0, dataArchive.Count).Select(i =>
        {
            var d = dataArchive.GetMember(i);
            int classId = d.Length > 1 ? d[1] : 0;
            string label = trainerNames is null
                ? $"Trainer #{i:000} · class {classId}"
                : trainerNames.Label(i, classId);
            return new TrainerListEntry(i, label + $"   ·   {(d.Length > 3 ? d[3] : 0)} Pokémon");
        }).ToList();
        var list = new ListBox { ItemsSource = entries };
        var search = NewBox("Search trainer name, class, or ID");
        search.Watermark = "Search trainers...";
        search.TextChanged += (_, _) =>
        {
            string query = search.Text?.Trim() ?? "";
            list.ItemsSource = entries.Where(e => e.Label.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        };
        var trainerListGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"), RowSpacing = 8 };
        trainerListGrid.Children.Add(new TextBlock { Text = "TRAINER INDEX", FontSize = 10, FontWeight = FontWeight.Bold, Foreground = Brush("#7E8DA4") });
        Grid.SetRow(search, 1); trainerListGrid.Children.Add(search);
        var namesNote = Muted(nameStatus.Length == 0 ? "Names decoded from ROM text banks 382/383" : nameStatus);
        Grid.SetRow(namesNote, 2); trainerListGrid.Children.Add(namesNote);
        Grid.SetRow(list, 3); trainerListGrid.Children.Add(list);
        var left = Card(trainerListGrid);
        var meta=new Dictionary<string,TextBox>();var metaPanel=new StackPanel{Spacing=7};
        foreach(string key in new[]{"Class ID","Battle style ID","Money multiplier","Reward item ID","AI flags (u32)","Battle item 1","Battle item 2","Battle item 3","Battle item 4"})
        {var row=new Grid{ColumnDefinitions=new ColumnDefinitions("190,*"),ColumnSpacing=8};row.Children.Add(new TextBlock{Text=key,VerticalAlignment=VerticalAlignment.Center,Foreground=Brush("#AAB7CA"),FontSize=12});var box=NewBox(key);Grid.SetColumn(box,1);row.Children.Add(box);metaPanel.Children.Add(row);meta[key]=box;}
        var heal=new CheckBox{Content="Trainer heals party"};metaPanel.Children.Add(heal);
        var partyPanel=new StackPanel{Spacing=8};var partyEditors=new List<TextBox[]>();var note=Muted("Choose a trainer to inspect metadata and party slots.");
        var apply=PrimaryButton("Apply trainer changes",()=>
        {
            if(list.SelectedItem is not TrainerListEntry selected)return;int id=selected.Id;
            try{
                byte[] d=dataArchive.GetMember(id).ToArray();byte[] p=partyArchive.GetMember(id).ToArray();if(d.Length!=20)throw new InvalidDataException("Trainer metadata record is not 20 bytes.");
                byte kind=d[0],count=d[3];int unit=8+((kind&2)!=0?2:0)+((kind&1)!=0?8:0);if(p.Length!=count*unit)throw new InvalidDataException("Party byte length does not match trainer metadata.");
                d[1]=(byte)Parse(meta["Class ID"].Text,255);d[2]=(byte)Parse(meta["Battle style ID"].Text,255);d[16]=heal.IsChecked==true?(byte)1:(byte)0;d[17]=(byte)Parse(meta["Money multiplier"].Text,255);BitConverter.GetBytes((ushort)Parse(meta["Reward item ID"].Text,65535)).CopyTo(d,18);BitConverter.GetBytes(Parse(meta["AI flags (u32)"].Text,uint.MaxValue)).CopyTo(d,12);
                for(int i=0;i<4;i++)BitConverter.GetBytes((ushort)Parse(meta[$"Battle item {i+1}"].Text,65535)).CopyTo(d,4+i*2);
                for(int i=0;i<count;i++){var e=p.AsSpan(i*unit,unit);var x=partyEditors[i];e[0]=(byte)Parse(x[3].Text,255);e[1]=(byte)((Parse(x[4].Text,15))|(Parse(x[5].Text,3)<<4));e[2]=(byte)Parse(x[2].Text,100);e[3]=0;BitConverter.GetBytes((ushort)Parse(x[0].Text,65535)).CopyTo(e.Slice(4));BitConverter.GetBytes((ushort)Parse(x[1].Text,65535)).CopyTo(e.Slice(6));int extra=8;if((kind&2)!=0){BitConverter.GetBytes((ushort)Parse(x[6].Text,65535)).CopyTo(e.Slice(extra));extra+=2;}if((kind&1)!=0)for(int m=0;m<4;m++)BitConverter.GetBytes((ushort)Parse(x[6+((kind&2)!=0?1:0)+m].Text,65535)).CopyTo(e.Slice(extra+m*2));}
                dataArchive.SetMember(id,d);partyArchive.SetMember(id,p);note.Text=$"Trainer #{id:000} applied in memory.";SetStatus($"Trainer #{id:000} changed in memory");
            }catch(Exception ex){note.Text="Could not apply: "+ex.Message;}
        });
        list.SelectionChanged+=(_,_)=>{
            if(list.SelectedItem is not TrainerListEntry selected)return;int id=selected.Id;try{byte[] d=dataArchive.GetMember(id);byte[] p=partyArchive.GetMember(id);if(d.Length!=20)throw new InvalidDataException("Invalid 20-byte trainer metadata record.");byte kind=d[0],count=d[3];int unit=8+((kind&2)!=0?2:0)+((kind&1)!=0?8:0);if(p.Length!=count*unit)throw new InvalidDataException("Trainer party length mismatch.");
                meta["Class ID"].Text=d[1].ToString();meta["Battle style ID"].Text=d[2].ToString();meta["Money multiplier"].Text=d[17].ToString();meta["Reward item ID"].Text=BitConverter.ToUInt16(d,18).ToString();meta["AI flags (u32)"].Text=BitConverter.ToUInt32(d,12).ToString();heal.IsChecked=d[16]!=0;for(int i=0;i<4;i++)meta[$"Battle item {i+1}"].Text=BitConverter.ToUInt16(d,4+i*2).ToString();
                partyPanel.Children.Clear();partyEditors.Clear();for(int i=0;i<count;i++){int o=i*unit;byte[] e=p.AsSpan(o,unit).ToArray();var row=new StackPanel{Spacing=5};row.Children.Add(new TextBlock{Text=$"Party slot {i+1}",FontWeight=FontWeight.SemiBold,Foreground=Brush("#AFC4E6")});var x=new List<TextBox>{NewBox("Species ID"),NewBox("Form"),NewBox("Level"),NewBox("Difficulty"),NewBox("Gender"),NewBox("Ability")};int extra=8;if((kind&2)!=0){x.Add(NewBox("Held item"));extra+=2;}if((kind&1)!=0)for(int m=0;m<4;m++)x.Add(NewBox("Move "+(m+1)));var grid=new Grid{ColumnDefinitions=new ColumnDefinitions(string.Join(",",Enumerable.Repeat("*",x.Count))),ColumnSpacing=6};for(int k=0;k<x.Count;k++){var wrap=new StackPanel{Spacing=3,Children = {new TextBlock{Text=k switch{0=>"Species",1=>"Form",2=>"Level",3=>"IV / Diff.",4=>"Gender",5=>"Ability",6 when (kind&2)!=0=>"Item",_=>"Move "+(k-5-((kind&2)!=0?1:0))},FontSize=10,Foreground=Brush("#7F8FA6")},x[k]}};Grid.SetColumn(wrap,k);grid.Children.Add(wrap);}x[0].Text=BitConverter.ToUInt16(e,4).ToString();x[1].Text=BitConverter.ToUInt16(e,6).ToString();x[2].Text=e[2].ToString();x[3].Text=e[0].ToString();x[4].Text=(e[1]&15).ToString();x[5].Text=(e[1]>>4).ToString();int q=6;if((kind&2)!=0){x[q++].Text=BitConverter.ToUInt16(e,8).ToString();}if((kind&1)!=0){int mo=8+((kind&2)!=0?2:0);for(int m=0;m<4;m++)x[q+m].Text=BitConverter.ToUInt16(e,mo+m*2).ToString();}partyEditors.Add(x.ToArray());row.Children.Add(grid);partyPanel.Children.Add(Card(row));}
                note.Text=$"{(trainerNames is null ? $"Trainer #{id:000}" : trainerNames.Label(id, d[1]))} · {count} party members · format flags {kind}";
            }catch(Exception ex){note.Text=ex.Message;partyPanel.Children.Clear();partyEditors.Clear();}
        };
        list.SelectedItem=entries[0];
        var right=new ScrollViewer{Content=new StackPanel{Spacing=14,Children = {new TextBlock{Text="Trainer details",Classes={"section"}},Card(metaPanel),partyPanel,new StackPanel{Orientation=Orientation.Horizontal,Spacing=12,Children = {apply,note}}}}};Grid.SetColumn(left,0);Grid.SetColumn(right,1);root.Children.Add(left);root.Children.Add(right);return root;
    }

    private Control BuildStarterPage()
    {
        EnsureSpeciesNames();
        var scripts=_workspace!.GetArchive("a/0/5/6");
        var trainerData=_workspace.GetArchive("a/0/9/1");
        var trainerParty=_workspace.GetArchive("a/0/9/2");
        int[][] offsets=[[0x58B,0x590,0x595],[0x5C0,0x5C5,0x5CA],[0x5E2,0x5E7,0x5EC]];
        var rivals=new (int Id,int Slot,int Level,string Label)[]{(161,1,5,"Opening battle"),(162,2,5,"Opening battle"),(163,0,5,"Opening battle"),(166,1,8,"Floccesy Ranch"),(167,2,8,"Floccesy Ranch"),(168,0,8,"Floccesy Ranch")};
        byte[] script=scripts.GetMember(854).ToArray();
        bool valid=offsets.All(a=>a.All(o=>o+1<script.Length)&&a.All(o=>BitConverter.ToUInt16(script,o)==BitConverter.ToUInt16(script,a[0])));
        bool rivalsValid=valid&&rivals.All(r=>r.Id<trainerData.Count&&r.Id<trainerParty.Count&&trainerData.GetMember(r.Id).Length==20&&trainerData.GetMember(r.Id)[3]==1&&trainerData.GetMember(r.Id)[1]==145&&trainerParty.GetMember(r.Id).Length>=8&&BitConverter.ToUInt16(trainerParty.GetMember(r.Id),4)==BitConverter.ToUInt16(script,offsets[r.Slot][0])&&trainerParty.GetMember(r.Id)[2]==r.Level);
        var selectors=new List<(TextBox Search,ComboBox Picker)>();
        var root=new StackPanel{Spacing=14};
        root.Children.Add(new TextBlock{Text="Player starters",Classes={"section"}});
        root.Children.Add(Muted("Search by Pokémon name or National Pokédex number. Each choice updates the verified starter script."));
        var starterRows=new StackPanel{Spacing=12};
        for(int i=0;i<3;i++)
        {
            var picker=CreateSearchableSpeciesPicker(valid?BitConverter.ToUInt16(script,offsets[i][0]):(i==0?495:i==1?498:501));
            selectors.Add(picker);
            starterRows.Children.Add(Card(new StackPanel{Spacing=7,Children={new TextBlock{Text=$"Starter {i+1}",FontWeight=FontWeight.SemiBold},picker.Search,picker.Picker}}));
        }
        root.Children.Add(starterRows);
        var auto=new CheckBox{Content="Update Hugh's verified early battles to counter the selected starters",IsChecked=rivalsValid,IsEnabled=rivalsValid};
        root.Children.Add(auto);
        var status=Muted(valid?(rivalsValid?"Starter script and Hugh's first six trainer records validated.":"Starter script validated; automatic Hugh edits disabled because trainer records differ."):"Starter script validation failed. No changes will be applied.");
        root.Children.Add(PrimaryButton("Apply player starters and early Hugh teams",()=>
        {
            try
            {
                if(!valid)throw new InvalidDataException("Starter script layout is not verified.");
                ushort[] chosen=selectors.Select(x=>(ushort)SelectedSpecies(x.Picker)).ToArray();
                if(auto.IsChecked==true&&!rivalsValid)throw new InvalidDataException("Hugh's early battle records were not verified.");
                byte[] updated=script.ToArray();
                for(int i=0;i<3;i++)foreach(int o in offsets[i])BitConverter.GetBytes(chosen[i]).CopyTo(updated,o);
                if(auto.IsChecked==true)foreach(var r in rivals)
                {
                    byte[] party=trainerParty.GetMember(r.Id).ToArray();
                    BitConverter.GetBytes(chosen[r.Slot]).CopyTo(party,4);
                    trainerParty.SetMember(r.Id,party);
                }
                scripts.SetMember(854,updated);script=updated;
                status.Text="Starter choices applied in memory. Save the ROM or export a patch to keep them.";
            }
            catch(Exception ex){status.Text="Cannot apply: "+ex.Message;}
        }));
        root.Children.Add(status);
        root.Children.Add(new TextBlock{Text="Hugh's later evolution stages",Classes={"section"}});
        root.Children.Add(Muted("Choose the second and final-stage Pokémon for each starter route. These are planning selections only: later Hugh trainer IDs have not been verified, so the editor will NOT silently modify unrelated battles."));
        var later=new StackPanel{Spacing=10};
        for(int i=0;i<3;i++)
        {
            var second=CreateSearchableSpeciesPicker(i==0?496:i==1?499:502);
            var third=CreateSearchableSpeciesPicker(i==0?497:i==1?500:503);
            later.Children.Add(Card(new StackPanel{Spacing=8,Children={new TextBlock{Text=$"Hugh route {i+1} · secondary evolution",FontWeight=FontWeight.SemiBold},second.Search,second.Picker,new TextBlock{Text="Tertiary / final evolution",FontWeight=FontWeight.SemiBold},third.Search,third.Picker}}));
        }
        root.Children.Add(later);
        root.Children.Add(Muted("Optional shiny and gender flags are omitted: the starter script and rival Pokémon data have no verified per-starter override layout. Adding speculative flags could corrupt the ROM."));
        return new ScrollViewer{Content=root};
    }

    private Control BuildSpritePage()
    {
        EnsureSpeciesNames();
        var battle=_workspace!.GetArchive("a/0/0/4");
        var species=NewBox("Species ID");species.Text=_selectedPokemonId.ToString();var side=new ComboBox{ItemsSource=new[]{"Front","Back"},SelectedIndex=0,Width=110};var female=new CheckBox{Content="Female"};var shiny=new CheckBox{Content="Shiny palette"};
        var preview=new Image{Width=384,Height=384,Stretch=Stretch.Uniform};var palette=new ListBox{MinWidth=190,MaxHeight=330};var colorBox=NewBox("#RRGGBB");var note=Muted("Choose a sprite to preview.");GdiBitmap? bitmap=null;GdiColor[]? colors=null;int spriteMember=0,paletteMember=0;
        void Load(){try{int id=(int)Parse(species.Text,721);int slot=(side.SelectedIndex==0?(female.IsChecked==true?1:0):(female.IsChecked==true?10:9));spriteMember=id*20+slot;paletteMember=id*20+(shiny.IsChecked==true?19:18);byte[] blob=battle.GetMember(spriteMember);if(blob.Length==0&&female.IsChecked==true){female.IsChecked=false;return;}colors=SpriteCodec.ReadPalette(battle.GetMember(paletteMember));bitmap?.Dispose();bitmap=SpriteCodec.DecodeSingleCellSprite(blob,colors);using var ms=new MemoryStream();bitmap.Save(ms,System.Drawing.Imaging.ImageFormat.Png);ms.Position=0;preview.Source=new Bitmap(ms);palette.ItemsSource=colors.Select((c,i)=>$"#{i:D2}  {c.R:X2}{c.G:X2}{c.B:X2}").ToList();palette.SelectedIndex=0;note.Text=$"{SpeciesName(id)} (#{id:000}) · {side.SelectedItem} · 96×96 · sprite member {spriteMember} · palette member {paletteMember}";}catch(Exception ex){preview.Source=null;note.Text="Sprite unavailable: "+ex.Message;}}
        species.LostFocus+=(_,_)=>Load();side.SelectionChanged+=(_,_)=>Load();female.PropertyChanged+=(_,e)=>{if(e.Property==CheckBox.IsCheckedProperty)Load();};shiny.PropertyChanged+=(_,e)=>{if(e.Property==CheckBox.IsCheckedProperty)Load();};palette.SelectionChanged+=(_,_)=>{if(colors!=null&&palette.SelectedIndex>=0){var c=colors[palette.SelectedIndex];colorBox.Text=$"#{c.R:X2}{c.G:X2}{c.B:X2}";}};
        var import= new Button{Content="Import PNG"};import.Click+=async(_,_)=>{try{var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title="Import 96×96 sprite PNG",AllowMultiple=false,FileTypeFilter=[new FilePickerFileType("PNG image"){Patterns=["*.png"]}]});if(files.Count==0||colors==null)return;await using var stream=await files[0].OpenReadAsync();using var source=new GdiBitmap(stream);var changed=SpriteCodec.PatchSingleCellSprite(battle.GetMember(spriteMember),source,colors);battle.SetMember(spriteMember,changed);Load();note.Text="PNG imported in memory. Save a patch or new ROM to write it.";}catch(Exception ex){note.Text=ex.Message;}};
        var export=new Button{Content="Export PNG"};export.Click+=async(_,_)=>{try{if(bitmap==null)return;var file=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions{Title="Export sprite PNG",SuggestedFileName=$"species-{species.Text}-{side.SelectedItem}.png",FileTypeChoices=[new FilePickerFileType("PNG image"){Patterns=["*.png"]}]});if(file==null)return;await using var stream=await file.OpenWriteAsync();bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Png);note.Text="Sprite exported.";}catch(Exception ex){note.Text=ex.Message;}};
        var applyColor=new Button{Content="Apply palette color"};applyColor.Click+=(_,_)=>{try{if(colors==null||palette.SelectedIndex<0) return;string hex=(colorBox.Text??"").Trim().TrimStart('#');if(hex.Length!=6)throw new FormatException("Enter a six-digit RGB hex value.");var c=GdiColor.FromArgb(Convert.ToInt32(hex,16));colors[palette.SelectedIndex]=c;battle.SetMember(paletteMember,SpriteCodec.PatchPalette(battle.GetMember(paletteMember),colors));Load();palette.SelectedIndex=Math.Min(palette.SelectedIndex,colors.Length-1);note.Text="Palette color changed in memory.";}catch(Exception ex){note.Text=ex.Message;}};
        var importPalette = new Button{Content="Import Frost palette"};
        importPalette.Click += async (_,_) =>
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title="Import Frost's Gen 5 Editor palette (16×1 PNG)", AllowMultiple=false,
                    FileTypeFilter=[new FilePickerFileType("Frost palette PNG"){Patterns=["*.png"]}]
                });
                if (files.Count==0) return;
                await using var stream=await files[0].OpenReadAsync();
                var imported=FrostPalette.Import(stream);
                var current=SpriteCodec.ReadPalette(battle.GetMember(paletteMember));
                if (current.Length!=16) throw new InvalidDataException($"This sprite has {current.Length} palette entries, not 16. No changes made.");
                battle.SetMember(paletteMember,SpriteCodec.PatchPalette(battle.GetMember(paletteMember),imported));
                Load();
                note.Text=$"Imported Frost palette into {(shiny.IsChecked==true?"SHINY":"NORMAL")} colors for {SpeciesName((int)Parse(species.Text,721))}. Save your ROM or patch to keep it.";
            }
            catch(Exception ex){note.Text="Palette import failed: "+ex.Message;}
        };
        var exportPalette = new Button{Content="Export Frost palette"};
        exportPalette.Click += async (_,_) =>
        {
            try
            {
                var current=SpriteCodec.ReadPalette(battle.GetMember(paletteMember));
                if(current.Length!=16) throw new InvalidDataException($"This sprite has {current.Length} palette entries, not 16.");
                var file=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title="Export Frost's Gen 5 Editor palette (16×1 PNG)",
                    SuggestedFileName=$"species-{species.Text}-{(shiny.IsChecked==true?"shiny":"normal")}-palette.png",
                    FileTypeChoices=[new FilePickerFileType("PNG image"){Patterns=["*.png"]}]
                });
                if(file==null)return;
                await using var stream=await file.OpenWriteAsync();
                FrostPalette.Export(stream,current);
                note.Text="Exported 16×1 PNG palette for Frost's Gen 5 Editor.";
            }
            catch(Exception ex){note.Text="Palette export failed: "+ex.Message;}
        };
        var controls=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,Children = {species,side,female,shiny,import,export}};
        var right=new StackPanel{Spacing=8,Children = {new TextBlock{Text="PALETTE",FontSize=10,FontWeight=FontWeight.Bold,Foreground=Brush("#7E8DA4")},palette,colorBox,applyColor,importPalette,exportPalette,Muted("Frost format: 16×1 PNG, one pixel per palette color (left to right). Select Shiny palette before importing to change only shiny colors. Colors are quantized to Nintendo DS 5-bit channels on save.")}};
        var body=new Grid{ColumnDefinitions=new ColumnDefinitions("*,240"),ColumnSpacing=16};var imageCard=Card(new StackPanel{Spacing=10,Children = {new TextBlock{Text="Sprite preview",Classes={"section"}},new Border{Background=Brush("#0B111B"),CornerRadius=new CornerRadius(10),Padding=new Thickness(16),Child=preview},note}});var paletteCard=Card(right);Grid.SetColumn(imageCard,0);Grid.SetColumn(paletteCard,1);body.Children.Add(imageCard);body.Children.Add(paletteCard);
        Load();return new StackPanel{Spacing=14,Children = {controls,body}};
    }

    private Control BuildFilesPage()
    {
        var root=new Grid{ColumnDefinitions=new ColumnDefinitions("350,*"),ColumnSpacing=14};var list=new ListBox();var search=NewBox("Filter NitroFS paths");var paths=_workspace!.ListFiles();list.ItemsSource=paths;search.TextChanged+=(_,_)=>list.ItemsSource=paths.Where(p=>p.Contains(search.Text??"",StringComparison.OrdinalIgnoreCase)).ToList();
        var preview=new TextBox{IsReadOnly=true,AcceptsReturn=true,FontFamily="Cascadia Mono",FontSize=11,TextWrapping=TextWrapping.NoWrap};var info=Muted("Select a file to inspect its first 512 bytes.");
        list.SelectionChanged+=(_,_)=>{if(list.SelectedItem is not string path)return;try{byte[] b=_workspace.ReadFile(path);var sb=new StringBuilder();for(int i=0;i<Math.Min(b.Length,512);i++){if(i%16==0)sb.Append($"{i:X8}  ");sb.Append(b[i].ToString("X2")).Append(' ');if(i%16==15)sb.AppendLine();}preview.Text=sb.ToString();info.Text=$"{path} · {b.Length:N0} bytes · showing first {Math.Min(512,b.Length):N0} bytes";}catch(Exception ex){info.Text=ex.Message;}};
        var fileListGrid=new Grid{RowDefinitions=new RowDefinitions("Auto,Auto,*")};fileListGrid.Children.Add(new TextBlock{Text="NITROFS FILES",FontSize=10,FontWeight=FontWeight.Bold,Foreground=Brush("#7E8DA4")});Grid.SetRow(search,1);fileListGrid.Children.Add(search);Grid.SetRow(list,2);fileListGrid.Children.Add(list);var left=Card(fileListGrid);var right=Card(new StackPanel{Spacing=10,Children = {new TextBlock{Text="Hex preview",Classes={"section"}},info,preview}});Grid.SetColumn(left,0);Grid.SetColumn(right,1);root.Children.Add(left);root.Children.Add(right);if(paths.Count>0)list.SelectedIndex=0;return root;
    }

    private async Task OpenDumpAsync()
    {
        try{var folders=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions{Title="Select extracted White 2 Upgrade dump",AllowMultiple=false});if(folders.Count==0)return;_workspace=RomWorkspace.OpenFolder(folders[0].Path.LocalPath);_save.IsEnabled=true;SetStatus(_workspace.Description+" · Save As writes only changed archives to a patch folder.");ShowPage("Overview");}
        catch(Exception ex){SetStatus("Could not open dump: "+ex.Message);}
    }
    private async Task OpenRomAsync()
    {
        try{var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title="Open Nintendo DS ROM",AllowMultiple=false,FileTypeFilter=[new FilePickerFileType("Nintendo DS ROM"){Patterns=["*.nds"]}]});if(files.Count==0)return;_workspace=RomWorkspace.OpenRom(files[0].Path.LocalPath);_save.IsEnabled=true;SetStatus(_workspace.Description+" · Direct ROM save requires same-size archive replacements.");ShowPage("Overview");}
        catch(Exception ex){SetStatus("Could not open ROM: "+ex.Message);}
    }
    private async Task SaveAsync()
    {
        if(_workspace==null)return;try{
            if(_workspace.IsExtractedFolder){var dirs=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions{Title="Choose patch output folder",AllowMultiple=false});if(dirs.Count==0)return;int changed=_workspace.DirtyArchiveCount;_workspace.SaveTo(dirs[0].Path.LocalPath);SetStatus($"Saved {changed} modified archive(s) to {dirs[0].Path.LocalPath}");}
            else{var file=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions{Title="Save edited ROM",SuggestedFileName="White2Upgrade-edited.nds",FileTypeChoices=[new FilePickerFileType("Nintendo DS ROM"){Patterns=["*.nds"]}]});if(file==null)return;_workspace.SaveTo(file.Path.LocalPath);SetStatus("Saved edited ROM. Keep your original as a backup.");}
        }catch(Exception ex){SetStatus("Save failed: "+ex.Message);}
    }

    private void SetStatus(string text){_status.Text=_workspace is null?text:$"{text} · {_workspace.DirtyArchiveCount} unsaved archive(s)";}
    private static TextBox NewBox(string watermark)=>new(){Watermark=watermark,MinWidth=80};
    private static TextBlock Muted(string text)=>new(){Text=text,Foreground=Brush("#8998AE"),FontSize=11,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};
    private static Border Card(Control child)=>new(){Classes={"panel"},Padding=new Thickness(18),Child=child};
    private static IBrush Brush(string hex)=>new SolidColorBrush(Color.Parse(hex));
    private static Button PrimaryButton(string label,Action action){var b=new Button{Content=label};b.Classes.Add("primary");b.Click+=(_,_)=>action();return b;}
    private static uint Parse(string? value,uint max){if(!uint.TryParse(value?.Trim(),NumberStyles.Integer,CultureInfo.InvariantCulture,out uint v)||v>max)throw new FormatException($"Enter a whole number from 0 to {max}.");return v;}
    private static int Parse(string? value,int max)=>checked((int)Parse(value,(uint)max));
}
