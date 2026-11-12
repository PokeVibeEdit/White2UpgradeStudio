using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace White2UpgradeEditor;

public sealed partial class MainWindow
{
    private Control BuildMapPage()
    {
        var root = new StackPanel { Spacing = 12 };
        root.Children.Add(new TextBlock { Text = "Gen V map & event workspace", FontSize = 20, FontWeight = FontWeight.Bold });
        root.Children.Add(Muted("CTRMap-inspired zone/event workflow. Edit existing signs, NPCs, warps and triggers. Includes an orbitable 3D event-position preview. Experimental NSBMD GX wireframe model preview is available; terrain painting, textures, model placement and script compilation are not implemented."));
        NarcArchive entities;
        try { entities = _workspace!.GetArchive("a/1/2/6"); }
        catch (Exception ex) { return Card(Muted("Could not open zone entities: " + ex.Message)); }
        var zoneNames = new Dictionary<int, int>();
        try
        {
            var headers = _workspace!.GetArchive("a/0/1/2").GetMember(0);
            for (int i = 0; i + 0x30 <= headers.Length; i += 0x30)
            {
                int eventFile = BitConverter.ToUInt16(headers, i + 0x16);
                if (eventFile < entities.Count && !zoneNames.ContainsKey(eventFile)) zoneNames[eventFile] = i / 0x30;
            }
        }
        catch { /* Zone IDs can still be edited by event file index. */ }
        var choices = Enumerable.Range(0, entities.Count)
            .Where(i => entities.GetMember(i).Length > 8)
            .Select(i => new ZoneChoice(i, zoneNames.TryGetValue(i, out int zone) ? $"Zone {zone:000} · events file {i:000}" : $"Events file {i:000}"))
            .ToList();
        var zone = new ComboBox { ItemsSource = choices, SelectedIndex = choices.Count > 0 ? 0 : -1, MinWidth = 260 };
        var kind = new ComboBox { ItemsSource = ZoneEvents.Kinds, SelectedIndex = 1, MinWidth = 150 };
        var viewport = new Event3DViewport();
        // Model files and event files have different indexes. Do not assume a direct mapping.
        var modelArchive = _workspace!.GetArchive("a/0/0/8");
        var modelIndex = new ComboBox
        {
            ItemsSource = Enumerable.Range(0, modelArchive.Count).Select(i => $"Model {i:0000}").ToList(),
            SelectedIndex = -1, MinWidth = 160
        };
        var modelStatus = Muted("Select a model resource to preview. Model indices are not zone IDs.");
        var loadModel = PrimaryButton("Preview selected NSBMD", () =>
        {
            try
            {
                if (modelIndex.SelectedIndex < 0) return;
                var mesh = NsbmdMapModel.Decode(modelArchive.GetMember(modelIndex.SelectedIndex));
                viewport.SetModel(mesh);
                modelStatus.Text = $"Model {modelIndex.SelectedIndex}: {mesh.Triangles.Count:N0} triangles from {mesh.DisplayLists} GX candidates. {mesh.Note}";
            }
            catch (Exception ex)
            {
                viewport.SetModel(null);
                modelStatus.Text = "Unable to preview this model: " + ex.Message;
            }
        });
        var eventsList = new ListBox { MinHeight = 180, MaxHeight = 260 };
        var fieldsPanel = new StackPanel { Spacing = 7 };
        var notice = Muted("Select an event to inspect it.");
        var recordEditors = new List<(int Offset, string Type, TextBox Input)>();
        ZoneEvents? current = null;
        var saveEvent = PrimaryButton("Apply selected event", () =>
        {
            if (zone.SelectedItem is not ZoneChoice z || current is null || eventsList.SelectedIndex < 0 || kind.SelectedIndex < 0) return;
            try
            {
                int k = kind.SelectedIndex, n = eventsList.SelectedIndex;
                var bytes = current.Get(k, n);
                foreach (var f in recordEditors) ZoneEvents.WriteField(bytes, f.Offset, f.Type, f.Input.Text ?? "0");
                current.Replace(k, n, bytes);
                entities.SetMember(z.Id, current.Data);
                viewport.SetEvents(current);
                viewport.Select(k, n);
                notice.Text = $"Saved {ZoneEvents.Kinds[k]} event {n} in memory. Use Save / Export to write your patch.";
                SetStatus($"Zone event file {z.Id} modified");
            }
            catch (Exception ex) { notice.Text = "Invalid field value: " + ex.Message; }
        });
        void ShowRecord()
        {
            fieldsPanel.Children.Clear(); recordEditors.Clear();
            if (current is null || kind.SelectedIndex < 0 || eventsList.SelectedIndex < 0) return;
            int k = kind.SelectedIndex, n = eventsList.SelectedIndex;
            var bytes = current.Get(k, n);
            bool rail = ZoneEvents.IsRail(k, bytes);
            fieldsPanel.Children.Add(Muted(rail ? "Rail-position event · position fields are rail-relative." : "Grid-position event · positions use the game's native units."));
            foreach (var f in ZoneEvents.Fields(k, rail))
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("190,*"), ColumnSpacing = 8 };
                row.Children.Add(new TextBlock { Text = f.Name, VerticalAlignment = VerticalAlignment.Center });
                var input = new TextBox { Text = ZoneEvents.ReadField(bytes, f.Offset, f.Type) };
                Grid.SetColumn(input, 1); row.Children.Add(input);
                fieldsPanel.Children.Add(row);
                recordEditors.Add((f.Offset, f.Type, input));
            }
            viewport.Select(k, n);
            notice.Text = "Changes are only staged when you click Apply selected event. Unedited binary fields and init scripts are preserved.";
        }
        void ShowKind()
        {
            fieldsPanel.Children.Clear(); recordEditors.Clear();
            if (current is null || kind.SelectedIndex < 0) return;
            int k = kind.SelectedIndex;
            eventsList.ItemsSource = Enumerable.Range(0, current.Counts[k]).Select(i => $"{ZoneEvents.Kinds[k]} #{i:000}").ToList();
            eventsList.SelectedIndex = current.Counts[k] > 0 ? 0 : -1;
            if (current.Counts[k] == 0) notice.Text = "No events of this type in this zone.";
        }
        void ShowZone()
        {
            current = null; viewport.SetEvents(null); fieldsPanel.Children.Clear(); eventsList.ItemsSource = null;
            if (zone.SelectedItem is not ZoneChoice z) return;
            try { current = new ZoneEvents(entities.GetMember(z.Id)); viewport.SetEvents(current); ShowKind(); }
            catch (Exception ex) { notice.Text = "Unsupported or malformed zone entity file: " + ex.Message; }
        }
        viewport.MarkerSelected += (k, n) => { kind.SelectedIndex = k; eventsList.SelectedIndex = n; };
        zone.SelectionChanged += (_, _) => ShowZone();
        kind.SelectionChanged += (_, _) => ShowKind();
        eventsList.SelectionChanged += (_, _) => ShowRecord();
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children =
        {
            new StackPanel { Spacing = 5, Children = { Muted("ZONE / EVENT FILE"), zone } },
            new StackPanel { Spacing = 5, Children = { Muted("EVENT TYPE"), kind } }
        }};
        root.Children.Add(Card(filters));
        root.Children.Add(Card(new StackPanel { Spacing = 8, Children =
        {
            new TextBlock { Text = "3D map model + event layout (experimental)", FontSize = 17, FontWeight = FontWeight.SemiBold },
            Muted("Drag empty space to orbit · Mouse wheel to zoom · Click a marker to edit its event. Gold = signs, blue = NPCs, purple = warps, red = triggers. Rail-position events are excluded."),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { modelIndex, loadModel } },
            modelStatus,
            viewport,
            Muted("Wireframe uses decoded GX model vertices, with independent preview centering/scaling; event coordinates are not guaranteed to align. Editing coordinates uses the properties panel below.")
        }}));
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("240,*"), ColumnSpacing = 12 };
        var left = Card(new StackPanel { Spacing = 8, Children = { Muted("EVENTS"), eventsList } });
        var right = Card(new StackPanel { Spacing = 12, Children =
        {
            new TextBlock { Text = "Event properties", FontSize = 17, FontWeight = FontWeight.SemiBold },
            fieldsPanel, saveEvent, notice
        }});
        Grid.SetColumn(right, 1); split.Children.Add(left); split.Children.Add(right); root.Children.Add(split);
        root.Children.Add(Muted("Map geometry is stored in a/0/0/8; matrices in a/0/0/9. For full 3D editing, use CTRMap CE + CTRMapV. This page safely edits existing entity records without changing record sizes."));
        ShowZone();
        return new ScrollViewer { Content = root };
    }
    private sealed record ZoneChoice(int Id, string Label)
    {
        public override string ToString() => Label;
    }
}
