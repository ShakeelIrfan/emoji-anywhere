using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using EmojiAnywhere.Core;
using Microsoft.Win32;

namespace EmojiAnywhere.UI;

// The window for adding and editing snippets. One at a time; opening it again brings it to the front.
public sealed class SnippetEditor : Window
{
    private static SnippetEditor? _instance;

    public static SnippetEditor Open(SnippetLibrary library, Settings settings)
    {
        if (_instance != null)
        {
            if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
            _instance.Activate();
            return _instance;
        }
        _instance = new SnippetEditor(library, settings);
        _instance.Closed += (_, _) => _instance = null;
        _instance.Show();
        _instance.Activate();
        return _instance;
    }

    private readonly SnippetLibrary _lib;
    private readonly Settings _settings;

    // Left side
    private readonly TextBox _search = new();
    private readonly ListBox _list = new();
    private readonly TextBlock _syncText = new();
    private readonly TextBlock _leftStatus = new();

    // Right side
    private readonly Grid _formArea = new();
    private readonly StackPanel _empty = new();
    private readonly TextBox _abbr = new();
    private readonly TextBox _label = new();
    private readonly TextBox _folder = new() { MaxLength = 60 }; // a folder name; empty means no folder
    private readonly RichTextBox _body = new();
    private readonly CheckBox _popupOnly = new();
    private readonly TextBox _apps = new();
    private readonly TextBlock _error = new();
    private readonly TextBlock _preview = new();
    private readonly TextBlock _status = new();
    private readonly Button _delete;
    private readonly Button _save;

    private Snippet? _current;
    private bool _isNew;
    private bool _dirty;
    private bool _loading;
    private bool _switching;

    private SnippetEditor(SnippetLibrary library, Settings settings)
    {
        _lib = library;
        _settings = settings;

        Title = "Emoji Anywhere snippets";
        Width = 960; Height = 640; MinWidth = 720; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        Resources = BuildResources();
        ApplyTheme();
        SetResourceReference(BackgroundProperty, "Ed.Background");
        SetResourceReference(ForegroundProperty, "Ed.Text");
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            string res = asm.GetManifestResourceNames().First(n => n.EndsWith("app.ico"));
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(asm.GetManifestResourceStream(res)!);
        }
        catch { }

        _save = Primary("Save");
        _delete = Quiet("Delete");

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(BuildLeft());
        var right = BuildRight();
        Grid.SetColumn(right, 1);
        root.Children.Add(right);
        Content = root;

        Rebuild(selectId: null);
        ShowEditor(null);

        _lib.Changed += OnLibraryChanged;
        Closing += OnClosing;
        Closed += (_, _) => _lib.Changed -= OnLibraryChanged;
        PreviewKeyDown += OnKeys;
        Loaded += (_, _) => { if (_lib.Snippets.Count > 0 && _list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is Snippet) is ListBoxItem first) first.IsSelected = true; _search.Focus(); };
    }

    // ---------- Layout ----------

    private UIElement BuildLeft()
    {
        var panel = new DockPanel { Margin = new Thickness(16, 16, 8, 12) };

        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = "Snippets", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 2) });
        top.Children.Add(Muted($"Type {_settings.SnippetTrigger} and a short code anywhere to insert the text.", new Thickness(2, 0, 0, 12)));
        _search.TextChanged += (_, _) => Rebuild(_current?.Id);
        top.Children.Add(Field(_search, "Search snippets", "Search snippets"));
        var buttons = new UniformGrid { Rows = 1, Margin = new Thickness(0, 8, 0, 8) };
        var add = Primary("New snippet");
        add.Margin = new Thickness(0, 0, 4, 0);
        add.Click += (_, _) => NewSnippet();
        var addFolder = Quiet("New folder");
        addFolder.Margin = new Thickness(4, 0, 0, 0);
        addFolder.Click += (_, _) => NewFolder();
        buttons.Children.Add(add);
        buttons.Children.Add(addFolder);
        top.Children.Add(buttons);
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);

        var bottom = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var row = new UniformGrid { Rows = 1 };
        var import = Quiet("Import");
        import.ToolTip = "Add snippets from a file: Emoji Anywhere, espanso or Beeftext";
        import.Click += (_, _) => Import();
        var export = Quiet("Export");
        export.ToolTip = "Save all snippets to a file";
        export.Click += (_, _) => Export();
        var sync = Quiet("Sync");
        sync.ToolTip = "Keep snippets the same on all your PCs through OneDrive, Dropbox or Google Drive";
        sync.Click += (_, _) => ChooseSync();
        row.Children.Add(import); row.Children.Add(export); row.Children.Add(sync);
        bottom.Children.Add(row);
        _syncText.TextWrapping = TextWrapping.Wrap;
        _syncText.Margin = new Thickness(2, 6, 0, 0);
        _syncText.FontSize = 12;
        _syncText.SetResourceReference(TextBlock.ForegroundProperty, "Ed.Muted");
        bottom.Children.Add(_syncText);
        _leftStatus.TextWrapping = TextWrapping.Wrap;
        _leftStatus.Margin = new Thickness(2, 4, 0, 0);
        _leftStatus.FontSize = 12;
        bottom.Children.Add(_leftStatus);
        DockPanel.SetDock(bottom, Dock.Bottom);
        panel.Children.Add(bottom);

        _list.BorderThickness = new Thickness(0);
        _list.Background = Brushes.Transparent;
        _list.SetResourceReference(ForegroundProperty, "Ed.Text");
        _list.Resources[SystemColors.HighlightBrushKey] = Resources["Ed.Selected"];
        _list.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = Resources["Ed.Selected"];
        _list.Resources[SystemColors.HighlightTextBrushKey] = Resources["Ed.Text"];
        _list.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = Resources["Ed.Text"];
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.SelectionChanged += OnSelect;
        System.Windows.Automation.AutomationProperties.SetName(_list, "Snippets");
        panel.Children.Add(_list);
        UpdateSyncText();
        return panel;
    }

    private UIElement BuildRight()
    {
        var card = new Border { Margin = new Thickness(8, 16, 16, 16), Padding = new Thickness(20), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "Ed.Card");
        card.SetResourceReference(Border.BorderBrushProperty, "Ed.Border");

        // Empty state
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.MaxWidth = 380;
        _empty.Children.Add(new TextBlock { Text = "No snippet selected", FontSize = 17, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        _empty.Children.Add(Muted($"Pick one on the left, or make a new one. For example {_settings.SnippetTrigger}addr for your address, or {_settings.SnippetTrigger}sig for an email sign off.", new Thickness(0, 6, 0, 14), center: true));
        var emptyNew = Primary("New snippet");
        emptyNew.HorizontalAlignment = HorizontalAlignment.Center;
        emptyNew.Click += (_, _) => NewSnippet();
        _empty.Children.Add(emptyNew);

        // Form
        var form = new DockPanel();
        var head = new StackPanel();

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        var abbrBox = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        abbrBox.Children.Add(Label("Short code"));
        var abbrRow = new DockPanel();
        var trig = new TextBlock { Text = _settings.SnippetTrigger.ToString(), FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 6, 0) };
        trig.SetResourceReference(TextBlock.ForegroundProperty, "Ed.Accent");
        DockPanel.SetDock(trig, Dock.Left);
        abbrRow.Children.Add(trig);
        _abbr.MaxLength = SnippetMatcher.MaxAbbreviation;
        abbrRow.Children.Add(Field(_abbr, "addr", "Short code"));
        abbrBox.Children.Add(abbrRow);
        grid.Children.Add(abbrBox);
        var labelBox = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        labelBox.Children.Add(Label("Name"));
        _label.MaxLength = 120;
        labelBox.Children.Add(Field(_label, "Home address", "Name"));
        Grid.SetColumn(labelBox, 1);
        grid.Children.Add(labelBox);
        var folderBox = new StackPanel();
        folderBox.Children.Add(Label("Folder"));
        var folderRow = new DockPanel();
        var pick = Quiet("\uE70D");
        pick.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        pick.Padding = new Thickness(8, 6, 8, 6);
        pick.Margin = new Thickness(4, 0, 0, 0);
        pick.ToolTip = "Pick a folder";
        System.Windows.Automation.AutomationProperties.SetName(pick, "Pick a folder");
        pick.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = pick };
            var none = new MenuItem { Header = "No folder" };
            none.Click += (_, _) => _folder.Text = "";
            menu.Items.Add(none);
            foreach (var f in _lib.Folders)
            {
                var mi = new MenuItem { Header = f.Name };
                mi.Click += (_, _) => _folder.Text = f.Name;
                menu.Items.Add(mi);
            }
            menu.IsOpen = true;
        };
        DockPanel.SetDock(pick, Dock.Right);
        folderRow.Children.Add(pick);
        _folder.ToolTip = "Type a new folder name, or pick one with the arrow";
        folderRow.Children.Add(Field(_folder, "No folder", "Folder"));
        folderBox.Children.Add(folderRow);
        Grid.SetColumn(folderBox, 2);
        grid.Children.Add(folderBox);
        head.Children.Add(grid);

        head.Children.Add(Label("Text to insert"));
        head.Children.Add(BuildToolbar());
        DockPanel.SetDock(head, Dock.Top);
        form.Children.Add(head);

        var foot = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _preview.TextWrapping = TextWrapping.Wrap;
        _preview.FontSize = 12;
        _preview.MaxHeight = 60;
        _preview.TextTrimming = TextTrimming.CharacterEllipsis;
        _preview.SetResourceReference(TextBlock.ForegroundProperty, "Ed.Muted");
        foot.Children.Add(_preview);
        var opts = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        opts.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        opts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _popupOnly.Content = "Only from the search popup";
        _popupOnly.ToolTip = "It never expands on its own; pick it from the list that shows while you type";
        _popupOnly.VerticalAlignment = VerticalAlignment.Center;
        _popupOnly.SetResourceReference(ForegroundProperty, "Ed.Text");
        _popupOnly.Checked += (_, _) => MarkDirty();
        _popupOnly.Unchecked += (_, _) => MarkDirty();
        opts.Children.Add(_popupOnly);
        var appsRow = new DockPanel { Margin = new Thickness(16, 0, 0, 0) };
        var appsLabel = Muted("Only in apps", new Thickness(0, 0, 8, 0));
        appsLabel.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(appsLabel, Dock.Left);
        appsRow.Children.Add(appsLabel);
        _apps.ToolTip = "App names separated by commas, like outlook, chrome. Leave empty for every app.";
        appsRow.Children.Add(Field(_apps, "every app", "Only in these apps"));
        Grid.SetColumn(appsRow, 1);
        opts.Children.Add(appsRow);
        foot.Children.Add(opts);
        _error.Foreground = new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38));
        _error.TextWrapping = TextWrapping.Wrap;
        _error.Margin = new Thickness(0, 8, 0, 0);
        _error.Visibility = Visibility.Collapsed;
        foot.Children.Add(_error);
        var actions = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        _save.Click += (_, _) => TrySave();
        _save.ToolTip = "Save (Ctrl+S)";
        _delete.Click += (_, _) => DeleteCurrent();
        DockPanel.SetDock(_save, Dock.Right);
        DockPanel.SetDock(_delete, Dock.Right);
        _delete.Margin = new Thickness(0, 0, 8, 0);
        actions.Children.Add(_save);
        actions.Children.Add(_delete);
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.SetResourceReference(TextBlock.ForegroundProperty, "Ed.Muted");
        actions.Children.Add(_status);
        foot.Children.Add(actions);
        DockPanel.SetDock(foot, Dock.Bottom);
        form.Children.Add(foot);

        _body.AcceptsReturn = true;
        _body.AcceptsTab = true;
        _body.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _body.Padding = new Thickness(6);
        _body.MinHeight = 140;
        _body.SetResourceReference(Control.BackgroundProperty, "Ed.Input");
        _body.SetResourceReference(Control.ForegroundProperty, "Ed.Text");
        _body.SetResourceReference(Control.BorderBrushProperty, "Ed.Border");
        _body.SetResourceReference(TextBoxBase.CaretBrushProperty, "Ed.Text");
        _body.FontSize = 14;
        SpellCheck.SetIsEnabled(_body, true);
        var para = new Style(typeof(Paragraph));
        para.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0)));
        _body.Resources.Add(typeof(Paragraph), para);
        System.Windows.Automation.AutomationProperties.SetName(_body, "Text to insert");
        _body.TextChanged += (_, _) => { MarkDirty(); UpdatePreview(); };
        form.Children.Add(_body);

        foreach (var tb in new[] { _abbr, _label, _apps }) tb.TextChanged += (_, _) => { MarkDirty(); UpdatePreview(); };
        _folder.TextChanged += (_, _) => MarkDirty();

        _formArea.Children.Add(_empty);
        _formArea.Children.Add(form);
        card.Child = _formArea;
        return card;
    }

    private UIElement BuildToolbar()
    {
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        void Tool(string glyph, string name, Action act, bool icon = true)
        {
            var b = Quiet(glyph);
            b.Padding = new Thickness(9, 4, 9, 4);
            b.Margin = new Thickness(0, 0, 4, 0);
            if (icon) b.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
            b.ToolTip = name;
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            b.Click += (_, _) => { act(); _body.Focus(); };
            bar.Children.Add(b);
        }
        Tool("", "Bold (Ctrl+B)", () => EditingCommands.ToggleBold.Execute(null, _body));
        Tool("", "Italic (Ctrl+I)", () => EditingCommands.ToggleItalic.Execute(null, _body));
        Tool("", "Underline (Ctrl+U)", () => EditingCommands.ToggleUnderline.Execute(null, _body));
        Tool("", "Link", AddLink);
        Tool("", "Bulleted list", () => EditingCommands.ToggleBullets.Execute(null, _body));
        Tool("", "Plain text (remove formatting)", ClearFormatting);

        var insert = Quiet("Insert");
        insert.Padding = new Thickness(10, 4, 10, 4);
        insert.ToolTip = "Insert something that fills in when the snippet is used";
        var menu = new ContextMenu();
        void Item(string text, string hint, Func<string?> token)
        {
            var mi = new MenuItem { Header = text, ToolTip = hint };
            mi.Click += (_, _) =>
            {
                string? t = token();
                if (t == null) return;
                _body.Selection.Text = t;
                _body.Selection.Select(_body.Selection.End, _body.Selection.End);
                _body.Focus();
            };
            menu.Items.Add(mi);
        }
        Item("Today's date", "Like 18 November 2026", () => "{date:d MMMM yyyy}");
        Item("Time", "The time when you use the snippet", () => "{time}");
        Item("Clipboard", "Whatever you last copied", () => "{clipboard}");
        Item("Cursor position", "Where the cursor ends up after inserting", () => "{cursor}");
        menu.Items.Add(new Separator());
        Item("Text to fill in...", "Asks you for it each time", () =>
        {
            var r = SnippetForm.Ask("Text to fill in", new() { new SnippetField("What to ask for", null) }, this, new Dictionary<string, string> { ["What to ask for"] = "Name" });
            string n = Clean(r?["What to ask for"]);
            return n.Length == 0 ? null : "{input:" + n + "}";
        });
        Item("Choice...", "Pick from a short list each time", () =>
        {
            var r = SnippetForm.Ask("Choice", new() { new SnippetField("Name", null), new SnippetField("Options, separated by commas", null) }, this,
                new Dictionary<string, string> { ["Name"] = "Size", ["Options, separated by commas"] = "Small, Medium, Large" });
            if (r == null) return null;
            string n = Clean(r["Name"]);
            string opts = string.Join(",", (r["Options, separated by commas"] ?? "").Split(',').Select(Clean).Where(o => o.Length > 0));
            return n.Length == 0 || opts.Length == 0 ? null : "{choice:" + n + "|" + opts + "}";
        });
        Item("Another snippet...", "Puts another snippet's text here", () =>
        {
            var r = SnippetForm.Ask("Another snippet", new() { new SnippetField("Its short code", null) }, this);
            string n = Clean(r?["Its short code"]).TrimStart(_settings.SnippetTrigger);
            return n.Length == 0 ? null : "{snippet:" + n + "}";
        });
        insert.ContextMenu = menu;
        insert.Click += (_, _) => { menu.PlacementTarget = insert; menu.IsOpen = true; };
        bar.Children.Add(insert);
        return bar;
    }

    // Field names can't hold braces, pipes or colons, which mean something in the template
    private static string Clean(string? s) => new string((s ?? "").Where(c => c is not ('{' or '}' or '|' or ':' or ',')).ToArray()).Trim();

    // ---------- List ----------

    private void Rebuild(string? selectId)
    {
        _switching = true;
        _list.Items.Clear();
        string q = _search.Text.Trim();
        var all = _lib.Snippets;
        var shown = q.Length == 0 ? all.ToList()
            : all.Where(s => s.Abbreviation.Contains(q, StringComparison.OrdinalIgnoreCase) || s.Label.Contains(q, StringComparison.OrdinalIgnoreCase)
                          || s.Body.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        var folders = _lib.Folders;

        foreach (var f in folders)
        {
            var inFolder = shown.Where(s => s.FolderId == f.Id).OrderBy(s => s.Abbreviation, StringComparer.OrdinalIgnoreCase).ToList();
            if (q.Length > 0 && inFolder.Count == 0) continue;
            _list.Items.Add(FolderHeader(f, inFolder.Count));
            foreach (var s in inFolder) _list.Items.Add(SnippetItem(s, !f.Enabled));
        }
        var loose = shown.Where(s => s.FolderId == null || folders.All(f => f.Id != s.FolderId)).OrderBy(s => s.Abbreviation, StringComparer.OrdinalIgnoreCase).ToList();
        if (loose.Count > 0 && folders.Count > 0)
            _list.Items.Add(new ListBoxItem { Content = Muted("No folder", new Thickness(4, 10, 0, 2)), Focusable = false, IsHitTestVisible = false });
        foreach (var s in loose) _list.Items.Add(SnippetItem(s, false));

        if (_list.Items.Count == 0)
            _list.Items.Add(new ListBoxItem
            {
                Content = Muted(q.Length > 0 ? "No snippets match. Try another word." : "No snippets yet. Click New snippet to add one.", new Thickness(4, 8, 0, 0)),
                Focusable = false, IsHitTestVisible = false,
            });

        if (selectId != null && _list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is Snippet s && s.Id == selectId) is ListBoxItem sel)
        {
            sel.IsSelected = true;
            _list.ScrollIntoView(sel);
        }
        _switching = false;
    }

    private ListBoxItem SnippetItem(Snippet s, bool off)
    {
        var name = new TextBlock { Text = s.Label.Length > 0 ? s.Label : " ", TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Ed.Muted");
        var stack = new StackPanel { Margin = new Thickness(4, 3, 4, 3), Opacity = off ? 0.5 : 1 };
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(new TextBlock { Text = _settings.SnippetTrigger + s.Abbreviation, FontWeight = FontWeights.SemiBold });
        if (s.PopupOnly) top.Children.Add(Muted("  popup only", new Thickness(0)));
        if (s.IsRich) top.Children.Add(Muted("  formatted", new Thickness(0)));
        stack.Children.Add(top);
        stack.Children.Add(name);
        var item = new ListBoxItem { Content = stack, Tag = s, Padding = new Thickness(6, 2, 6, 2) };
        System.Windows.Automation.AutomationProperties.SetName(item, _settings.SnippetTrigger + s.Abbreviation + " " + s.Label);
        return item;
    }

    private ListBoxItem FolderHeader(SnippetFolder f, int count)
    {
        var row = new DockPanel { Margin = new Thickness(0, 10, 0, 2) };
        var toggle = new CheckBox { IsChecked = f.Enabled, ToolTip = "Turn this folder's snippets on or off", VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(toggle, "Folder " + f.Name + " on or off");
        toggle.Click += (_, _) => _lib.UpdateFolder(f.Id, enabled: toggle.IsChecked == true);
        DockPanel.SetDock(toggle, Dock.Right);
        row.Children.Add(toggle);
        var title = new TextBlock { Text = $"{f.Name}  ({count})", FontWeight = FontWeights.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Ed.Muted");
        row.Children.Add(title);
        var item = new ListBoxItem { Content = row, Tag = f, Focusable = false, Padding = new Thickness(4, 0, 6, 0) };
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = "Rename folder" };
        rename.Click += (_, _) =>
        {
            var r = SnippetForm.Ask("Rename folder", new() { new SnippetField("Folder name", null) }, this, new Dictionary<string, string> { ["Folder name"] = f.Name });
            string n = (r?["Folder name"] ?? "").Trim();
            if (n.Length > 0 && n.Length <= 60) _lib.UpdateFolder(f.Id, name: n);
        };
        var del = new MenuItem { Header = "Delete folder (keeps its snippets)" };
        del.Click += (_, _) => _lib.DeleteFolder(f.Id);
        menu.Items.Add(rename); menu.Items.Add(del);
        item.ContextMenu = menu;
        item.ToolTip = "Right click to rename or delete";
        return item;
    }

    private void OnSelect(object? sender, SelectionChangedEventArgs e)
    {
        if (_switching) return;
        if (_list.SelectedItem is not ListBoxItem { Tag: Snippet picked }) return;
        if (_current != null && picked.Id == _current.Id) return;
        if (_dirty && !TrySave(quiet: true))
        {
            // Keep the unsaved snippet open so its problem can be fixed
            _switching = true;
            _list.SelectedItem = _list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is Snippet s && s.Id == _current?.Id);
            _switching = false;
            return;
        }
        ShowEditor(picked);
    }

    // ---------- Editing ----------

    private void ShowEditor(Snippet? s)
    {
        _loading = true;
        _current = s?.Clone();
        _isNew = false;
        var form = (UIElement)_formArea.Children[1];
        _empty.Visibility = s == null ? Visibility.Visible : Visibility.Collapsed;
        form.Visibility = s == null ? Visibility.Collapsed : Visibility.Visible;
        if (s != null)
        {
            _abbr.Text = s.Abbreviation;
            _label.Text = s.Label;
            _folder.Text = _lib.Folders.FirstOrDefault(f => f.Id == s.FolderId)?.Name ?? "";
            _popupOnly.IsChecked = s.PopupOnly;
            _apps.Text = s.Apps == null ? "" : string.Join(", ", s.Apps);
            if (s.IsRich && s.Rtf != null)
            {
                try { RichTextConvert.LoadRtf(_body.Document, s.Rtf); }
                catch { RichTextConvert.LoadPlain(_body.Document, s.Body); }
            }
            else RichTextConvert.LoadPlain(_body.Document, s.Body);
        }
        _error.Visibility = Visibility.Collapsed;
        _status.Text = "";
        _delete.Visibility = s == null ? Visibility.Collapsed : Visibility.Visible;
        _dirty = false;
        _loading = false;
        UpdatePreview();
    }

    private void NewSnippet()
    {
        if (_dirty && !TrySave(quiet: true)) return;
        _list.SelectedItem = null;
        string? folderId = _current?.FolderId;
        ShowEditor(new Snippet { FolderId = folderId });
        _isNew = true;
        _delete.Visibility = Visibility.Collapsed;
        _abbr.Focus();
    }

    private void NewFolder()
    {
        var r = SnippetForm.Ask("New folder", new() { new SnippetField("Folder name", null) }, this, new Dictionary<string, string> { ["Folder name"] = "Work" });
        string n = (r?["Folder name"] ?? "").Trim();
        if (n.Length == 0) return;
        if (n.Length > 60) n = n[..60];
        var f = _lib.AddFolder(n);
        Rebuild(_current?.Id);
        if (_current != null) _folder.Text = f.Name;
    }

    private void MarkDirty()
    {
        if (_loading || _current == null) return;
        _dirty = true;
        _status.Text = "Unsaved changes";
    }

    private bool TrySave(bool quiet = false)
    {
        if (_current == null) return true;
        string abbr = _abbr.Text.Trim().TrimStart(_settings.SnippetTrigger);
        string plain = RichTextConvert.Plain(_body.Document);

        string? problem = null;
        if (abbr.Length == 0) problem = "Add a short code, like addr.";
        else if (!SnippetMatcher.IsValidAbbreviation(abbr)) problem = "The short code can use letters, numbers, dashes, dots and underscores, up to 32 characters, with no spaces.";
        else if (_lib.Snippets.Any(x => x.Id != _current.Id && x.Abbreviation.Equals(abbr, StringComparison.OrdinalIgnoreCase)))
            problem = $"Another snippet already uses {_settings.SnippetTrigger}{abbr}. Pick a different short code.";
        else if (plain.Trim().Length == 0) problem = "Add the text this snippet inserts.";
        else if (plain.Length > SnippetMatcher.MaxBody) problem = "That text is too long for a snippet (100,000 characters at most).";

        if (problem != null)
        {
            _error.Text = problem;
            _error.Visibility = Visibility.Visible;
            if (!quiet) (abbr.Length == 0 || problem.Contains("short code") ? (UIElement)_abbr : _body).Focus();
            return false;
        }
        _error.Visibility = Visibility.Collapsed;

        var s = _current;
        s.Abbreviation = abbr;
        s.Label = _label.Text.Trim();
        s.PopupOnly = _popupOnly.IsChecked == true;
        var apps = _apps.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? a[..^4] : a).Where(a => a.Length is > 0 and <= 100).Distinct(StringComparer.OrdinalIgnoreCase).Take(50).ToList();
        s.Apps = apps.Count > 0 ? apps : null;
        string folderName = (_folder.Text ?? "").Trim();
        s.FolderId = folderName.Length == 0 || folderName == "No folder" ? null : _lib.AddFolder(folderName.Length > 60 ? folderName[..60] : folderName).Id;

        s.Body = plain;
        if (RichTextConvert.HasFormatting(_body.Document))
        {
            s.Format = "rich";
            s.Rtf = RichTextConvert.Rtf(_body.Document);
            s.Html = RichTextConvert.Html(_body.Document);
        }
        else { s.Format = "plain"; s.Rtf = null; s.Html = null; }

        _lib.Upsert(s);
        _dirty = false;
        _isNew = false;
        _status.Text = "Saved";
        _delete.Visibility = Visibility.Visible;
        Rebuild(s.Id);
        return true;
    }

    private void DeleteCurrent()
    {
        if (_current == null) return;
        if (_isNew) { ShowEditor(null); return; }
        var answer = MessageBox.Show(this, $"Delete {_settings.SnippetTrigger}{_current.Abbreviation}? This can't be undone.", "Delete snippet",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        _lib.Delete(_current.Id);
        _dirty = false;
        ShowEditor(null);
        Rebuild(null);
    }

    private void UpdatePreview()
    {
        if (_current == null) { _preview.Text = ""; return; }
        string abbr = _abbr.Text.Trim().TrimStart(_settings.SnippetTrigger);
        string body = RichTextConvert.Plain(_body.Document);
        var values = SnippetText.Fields(body).ToDictionary(f => f.Name, f => "[" + f.Name + "]", StringComparer.OrdinalIgnoreCase);
        string shown;
        try { shown = SnippetText.Render(body, values, () => "[clipboard]", DateTime.Now, _lib.Matcher.FindAny).Text; }
        catch { shown = body; }
        shown = shown.ReplaceLineEndings(" ⏎ ");
        if (shown.Length > 220) shown = shown[..219] + "…";
        _preview.Text = abbr.Length == 0 ? "Pick a short code, then type it anywhere to insert this."
            : $"Typing {_settings.SnippetTrigger}{abbr} inserts: {shown}";
    }

    private void AddLink()
    {
        var sel = _body.Selection;
        var r = SnippetForm.Ask("Add link", new() { new SnippetField("Link address", null) }, this, new Dictionary<string, string> { ["Link address"] = "https://" });
        string url = (r?["Link address"] ?? "").Trim();
        if (url.Length == 0 || url == "https://") return;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            url = (url.Contains('@') && !url.Contains('/') ? "mailto:" : "https://") + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) { _status.Text = "That link address doesn't look right."; return; }
        try
        {
            if (sel.IsEmpty)
            {
                var link = new Hyperlink(new Run(url), sel.Start) { NavigateUri = uri };
                _body.CaretPosition = link.ElementEnd;
            }
            else new Hyperlink(sel.Start, sel.End) { NavigateUri = uri };
        }
        catch { _status.Text = "A link can't go there. Select some plain text first."; }
    }

    private void ClearFormatting()
    {
        string plain = RichTextConvert.Plain(_body.Document);
        RichTextConvert.LoadPlain(_body.Document, plain);
    }

    // ---------- Import, export, sync ----------

    private void Import()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import snippets",
            Filter = "Snippet files (*.json, *.yml, *.yaml)|*.json;*.yml;*.yaml|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dlg.FileName).Length > SnippetImport.MaxFileBytes) { _leftStatus.Text = "That file is too big to import."; return; }
            var found = SnippetImport.Read(File.ReadAllText(dlg.FileName));
            if (found.Count == 0) { _leftStatus.Text = "No snippets found in that file."; return; }
            int added = _lib.Import(found, "Imported");
            _leftStatus.Text = added == 0 ? "Those snippets are already here." : $"Added {added} snippet{(added == 1 ? "" : "s")} to the Imported folder.";
            Rebuild(_current?.Id);
        }
        catch (Exception ex) { _leftStatus.Text = "Could not read that file: " + ex.Message; }
    }

    private void Export()
    {
        var dlg = new SaveFileDialog { Title = "Export snippets", FileName = "emoji-anywhere-snippets.json", Filter = "JSON file (*.json)|*.json" };
        if (dlg.ShowDialog(this) != true) return;
        try { File.WriteAllText(dlg.FileName, _lib.ExportJson()); _leftStatus.Text = "Saved all snippets to " + Path.GetFileName(dlg.FileName) + "."; }
        catch (Exception ex) { _leftStatus.Text = "Could not save the file: " + ex.Message; }
    }

    private void ChooseSync()
    {
        if (!string.IsNullOrWhiteSpace(_settings.SnippetSyncFolder))
        {
            var stop = MessageBox.Show(this, "Stop syncing snippets with " + _settings.SnippetSyncFolder + "?\n\nThis PC keeps its own copy of every snippet.",
                "Sync", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (stop != MessageBoxResult.Yes) return;
            _settings.SnippetSyncFolder = null;
            _settings.Save();
            _lib.SyncFolderChanged();
            UpdateSyncText();
            return;
        }
        string? oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        var dlg = new OpenFolderDialog
        {
            Title = "Pick a folder that syncs between your PCs (OneDrive, Dropbox or Google Drive)",
            InitialDirectory = oneDrive != null && Directory.Exists(oneDrive) ? oneDrive : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        if (dlg.ShowDialog(this) != true) return;
        _settings.SnippetSyncFolder = dlg.FolderName;
        _settings.Save();
        _lib.SyncFolderChanged();
        UpdateSyncText();
        Rebuild(_current?.Id);
    }

    private void UpdateSyncText()
    {
        string? folder = _settings.SnippetSyncFolder;
        _syncText.Text = string.IsNullOrWhiteSpace(folder)
            ? "Sync is off. Snippets are on this PC only."
            : "Syncing through " + folder + ". Pick the same folder on your other PCs.";
        if (_lib.SyncProblem is string p) _syncText.Text += "\n" + p;
    }

    private void OnLibraryChanged() => Dispatcher.BeginInvoke(() =>
    {
        UpdateSyncText();
        if (!_dirty) Rebuild(_current?.Id);
    });

    // ---------- Window ----------

    private void OnKeys(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (ctrl && e.Key == Key.S) { TrySave(); e.Handled = true; }
        else if (ctrl && e.Key == Key.N) { NewSnippet(); e.Handled = true; }
        else if (ctrl && e.Key == Key.F) { _search.Focus(); _search.SelectAll(); e.Handled = true; }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_dirty || TrySave(quiet: true)) return;
        var answer = MessageBox.Show(this, "This snippet has a problem and wasn't saved:\n\n" + _error.Text + "\n\nClose anyway and lose the changes?",
            "Unsaved snippet", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) e.Cancel = true;
    }

    // ---------- Look ----------

    private static ResourceDictionary BuildResources()
    {
        const string xaml = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style x:Key="Ed.Quiet" TargetType="Button">
            <Setter Property="Cursor" Value="Hand" />
            <Setter Property="Foreground" Value="{DynamicResource Ed.Text}" />
            <Setter Property="Padding" Value="12,6" />
            <Setter Property="FocusVisualStyle" Value="{x:Null}" />
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Button">
                  <Border x:Name="Bg" CornerRadius="6" Padding="{TemplateBinding Padding}" Background="{DynamicResource Ed.Hover}"
                          BorderThickness="1" BorderBrush="{DynamicResource Ed.Border}">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Bg" Property="Background" Value="{DynamicResource Ed.HoverStrong}" /></Trigger>
                    <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Bg" Property="BorderBrush" Value="{DynamicResource Ed.Text}" /></Trigger>
                    <Trigger Property="IsPressed" Value="True"><Setter TargetName="Bg" Property="RenderTransform"><Setter.Value><TranslateTransform Y="1" /></Setter.Value></Setter></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
          <Style x:Key="Ed.Primary" TargetType="Button" BasedOn="{StaticResource Ed.Quiet}">
            <Setter Property="Foreground" Value="White" />
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Button">
                  <Border x:Name="Bg" CornerRadius="6" Padding="{TemplateBinding Padding}" Background="{DynamicResource Ed.Accent}"
                          BorderThickness="1" BorderBrush="{DynamicResource Ed.Accent}">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Bg" Property="Opacity" Value="0.9" /></Trigger>
                    <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Bg" Property="BorderBrush" Value="{DynamicResource Ed.Text}" /></Trigger>
                    <Trigger Property="IsPressed" Value="True"><Setter TargetName="Bg" Property="RenderTransform"><Setter.Value><TranslateTransform Y="1" /></Setter.Value></Setter></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
          <Style TargetType="ListBoxItem">
            <Setter Property="FocusVisualStyle" Value="{x:Null}" />
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="ListBoxItem">
                  <Border x:Name="Bg" CornerRadius="6" Padding="{TemplateBinding Padding}" Margin="0,1" Background="Transparent"
                          BorderThickness="1" BorderBrush="Transparent">
                    <ContentPresenter />
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Bg" Property="Background" Value="{DynamicResource Ed.Hover}" /></Trigger>
                    <Trigger Property="IsSelected" Value="True"><Setter TargetName="Bg" Property="Background" Value="{DynamicResource Ed.Selected}" /></Trigger>
                    <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Bg" Property="BorderBrush" Value="{DynamicResource Ed.Text}" /></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
          <Style TargetType="TextBox">
            <Setter Property="Padding" Value="6,5" />
            <Setter Property="Background" Value="{DynamicResource Ed.Input}" />
            <Setter Property="Foreground" Value="{DynamicResource Ed.Text}" />
            <Setter Property="BorderBrush" Value="{DynamicResource Ed.Border}" />
            <Setter Property="CaretBrush" Value="{DynamicResource Ed.Text}" />
          </Style>
        </ResourceDictionary>
        """;
        return (ResourceDictionary)XamlReader.Parse(xaml);
    }

    private void ApplyTheme()
    {
        bool light = true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = (key?.GetValue("AppsUseLightTheme") as int? ?? 1) != 0;
        }
        catch { }
        SolidColorBrush B(byte a, byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromArgb(a, r, g, b)); x.Freeze(); return x; }
        Resources["Ed.Background"] = light ? B(255, 0xF3, 0xF4, 0xF7) : B(255, 0x1C, 0x1C, 0x1E);
        Resources["Ed.Card"] = light ? B(255, 0xFF, 0xFF, 0xFF) : B(255, 0x27, 0x27, 0x2A);
        Resources["Ed.Input"] = light ? B(255, 0xFF, 0xFF, 0xFF) : B(255, 0x1F, 0x1F, 0x22);
        Resources["Ed.Text"] = light ? B(255, 0x1B, 0x1D, 0x22) : B(255, 0xF2, 0xF2, 0xF4);
        Resources["Ed.Muted"] = light ? B(255, 0x64, 0x6A, 0x76) : B(255, 0xA3, 0xA8, 0xB2);
        Resources["Ed.Border"] = light ? B(0x26, 0, 0, 0) : B(0x33, 0xFF, 0xFF, 0xFF);
        Resources["Ed.Hover"] = light ? B(0x0A, 0, 0, 0) : B(0x14, 0xFF, 0xFF, 0xFF);
        Resources["Ed.HoverStrong"] = light ? B(0x16, 0, 0, 0) : B(0x24, 0xFF, 0xFF, 0xFF);
        Resources["Ed.Selected"] = light ? B(0x1F, 0xD6, 0x33, 0x6C) : B(0x40, 0xD6, 0x33, 0x6C);
        Resources["Ed.Accent"] = B(255, 0xD6, 0x33, 0x6C);
    }

    private Button Primary(string text) => new() { Content = text, Style = (Style)Resources["Ed.Primary"] };
    private Button Quiet(string text) => new() { Content = text, Style = (Style)Resources["Ed.Quiet"] };

    private TextBlock Label(string text)
    {
        var t = new TextBlock { Text = text, Margin = new Thickness(2, 0, 0, 4), FontSize = 12 };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Ed.Muted");
        return t;
    }

    private TextBlock Muted(string text, Thickness margin, bool center = false)
    {
        var t = new TextBlock { Text = text, Margin = margin, TextWrapping = TextWrapping.Wrap, FontSize = 12, TextAlignment = center ? TextAlignment.Center : TextAlignment.Left };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Ed.Muted");
        return t;
    }

    // A text box with a grey hint shown while it is empty
    private UIElement Field(TextBox box, string hint, string name)
    {
        System.Windows.Automation.AutomationProperties.SetName(box, name);
        var g = new Grid();
        var h = new TextBlock { Text = hint, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Opacity = 0.7 };
        h.SetResourceReference(TextBlock.ForegroundProperty, "Ed.Muted");
        box.TextChanged += (_, _) => h.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        g.Children.Add(box);
        g.Children.Add(h);
        return g;
    }
}
