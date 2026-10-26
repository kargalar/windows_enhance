using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class HostsFileModule : ITweakModule
{
    private IModuleContext? _context;
    private HostsFileEditorWindow? _window;
    private static readonly string[] EntryPlacementOptions = { "Üst (Windows varsayılanı)", "Dosyanın sonu" };

    public string Id => "hosts-file-editor";
    public string Name => "Hosts File Editor";
    public string Category => "Sistem";
    public string Description => "Windows hosts dosyasındaki IP-ad alan eşlemelerini görüntüler, filtreler, etkinleştirir, düzenler ve yönetici izniyle kaydeder.";
    public string? Warning => "Etkin hosts girdileri DNS çözümlemesini değiştirir. Kaydetmeden önce yedek alınır ve Windows yönetici onayı istenir.";
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings { get; }

    public HostsFileModule()
    {
        Settings =
        [
            new SettingDescriptor
            {
                Key = "open-editor",
                Label = "Hosts dosyasını düzenle",
                Kind = SettingKind.Action,
                Description = "Mevcut girdileri filtreleyin, ekleyin, değiştirin veya devre dışı bırakın.",
                Execute = OpenEditor
            },
            new SettingDescriptor
            {
                Key = "entry-placement",
                Label = "Yeni girdilerin yeri",
                Kind = SettingKind.Choice,
                Options = EntryPlacementOptions,
                Default = EntryPlacementOptions[0],
                Description = "Üst seçeneği yeni girdileri Windows yorum başlığından sonra ekler; dosya sonu girdileri en alta koyar."
            }
        ];
    }

    public void Initialize(IModuleContext context) => _context = context;
    public void Activate() => IsActive = true;
    public void Deactivate()
    {
        IsActive = false;
        _window?.Close();
        if (_window is { IsVisible: false }) _window = null;
    }
    public void Apply() { }

    private void OpenEditor()
    {
        try
        {
            var placeNearHeader = _context?.Settings.GetString("entry-placement", EntryPlacementOptions[0]) == EntryPlacementOptions[0];
            _window ??= new HostsFileEditorWindow(new HostsFileStore(), placeNearHeader);
            _window.SetEntryPlacement(placeNearHeader);
            _window.Closed += (_, _) => _window = null;
            if (!_window.IsVisible) _window.Show();
            _window.Activate();
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Hosts File Editor açılamadı.", exception);
            _context?.Notify("Hosts File Editor açılamadı: " + exception.Message);
        }
    }
}

internal sealed class HostsFileEditorWindow : Window
{
    private readonly HostsFileStore _store;
    private bool _insertNearHeader;
    private readonly ObservableCollection<HostsFileRow> _rows = new();
    private readonly DataGrid _grid;
    private readonly TextBox _filter;
    private readonly TextBlock _status;
    private HostsFileSnapshot _snapshot;
    private bool _dirty;

    public HostsFileEditorWindow(HostsFileStore store, bool insertNearHeader)
    {
        _store = store;
        _insertNearHeader = insertNearHeader;
        _snapshot = store.Load();
        Title = "WinForge · Hosts File Editor";
        Width = 920;
        Height = 610;
        MinWidth = 650;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush("#171C25");
        Foreground = Brush("#F1F4F9");

        var root = new DockPanel { Margin = new Thickness(18) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock { Text = "HOSTS DOSYASI", FontSize = 19, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock
        {
            Text = "Etkin girdiler Windows ad çözümlemesini değiştirir. Kaydetme öncesi yedek oluşturulur ve UAC onayı gerekir.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#BAC4D2"),
            Margin = new Thickness(0, 5, 0, 12)
        });
        var filterRow = new DockPanel { LastChildFill = true };
        var reload = Button("Yeniden yükle", (_, _) => Reload());
        DockPanel.SetDock(reload, Dock.Right);
        filterRow.Children.Add(reload);
        _filter = new TextBox { Height = 34, Padding = new Thickness(9, 5, 9, 5), ToolTip = "IP, ana bilgisayar adı veya yoruma göre filtrele" };
        _filter.TextChanged += (_, _) => CollectionViewSource.GetDefaultView(_rows).Refresh();
        filterRow.Children.Add(_filter);
        header.Children.Add(filterRow);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 12, 0, 0) };
        _status = new TextBlock { Text = "Hazır", Foreground = Brush("#BAC4D2"), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(_status, Dock.Left);
        footer.Children.Add(_status);
        var save = Button("Kaydet (yönetici)", Save, isPrimary: true);
        DockPanel.SetDock(save, Dock.Right);
        footer.Children.Add(save);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        _grid = new DataGrid
        {
            ItemsSource = _rows,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Background = Brush("#202733"),
            Foreground = Brush("#F1F4F9"),
            RowBackground = Brush("#202733"),
            AlternatingRowBackground = Brush("#252D3A"),
            BorderBrush = Brush("#414D5D"),
            CanUserSortColumns = true
        };
        _grid.Columns.Add(new DataGridTextColumn { Header = "IP adresi", Binding = new Binding(nameof(HostsFileRow.Address)), Width = new DataGridLength(180) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Ana bilgisayar adları", Binding = new Binding(nameof(HostsFileRow.HostNames)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Yorum", Binding = new Binding(nameof(HostsFileRow.Comment)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Etkin", Binding = new Binding(nameof(HostsFileRow.IsActive)), Width = new DataGridLength(70) });
        DockPanel.SetDock(_grid, Dock.Top);
        root.Children.Add(_grid);

        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(Button("Yeni girdi", AddEntry));
        actions.Children.Add(Button("Düzenle", EditEntry));
        actions.Children.Add(Button("Etkinleştir / devre dışı bırak", ToggleEntry));
        actions.Children.Add(Button("Sil", RemoveEntry));
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);
        Content = root;
        Closing += ConfirmDiscard;
        LoadRows();
    }

    private void LoadRows()
    {
        _rows.Clear();
        foreach (var entry in _snapshot.Document.Entries)
            _rows.Add(new HostsFileRow(entry.Address, entry.HostNames, entry.Comment, entry.IsActive, entry.LineIndex));
        CollectionViewSource.GetDefaultView(_rows).Filter = item => item is HostsFileRow row &&
            (string.IsNullOrWhiteSpace(_filter.Text) || row.SearchText.Contains(_filter.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        _status.Text = $"{_rows.Count} hosts girdisi · {_snapshot.Document.Entries.Count(entry => entry.IsActive)} etkin";
    }

    private void AddEntry(object? sender, RoutedEventArgs args)
    {
        if (new HostsFileEntryDialog(null).ShowDialog() != true || HostsFileEntryDialog.LastResult is not { } entry) return;
        try
        {
            _snapshot.Document.Add(entry, _insertNearHeader);
            MarkDirty();
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private void EditEntry(object? sender, RoutedEventArgs args)
    {
        if (SelectedRow() is not { } row) return;
        var current = new HostsFileEntry(row.Address, row.HostNames, row.Comment, row.IsActive, row.LineIndex);
        var dialog = new HostsFileEntryDialog(current);
        if (dialog.ShowDialog() != true || HostsFileEntryDialog.LastResult is not { } entry) return;
        try
        {
            _snapshot.Document.Update(entry with { LineIndex = row.LineIndex });
            MarkDirty();
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private void ToggleEntry(object? sender, RoutedEventArgs args)
    {
        if (SelectedRow() is not { } row) return;
        try { _snapshot.Document.Toggle(row.LineIndex); MarkDirty(); }
        catch (Exception exception) { ShowError(exception); }
    }

    private void RemoveEntry(object? sender, RoutedEventArgs args)
    {
        if (SelectedRow() is not { } row) return;
        if (MessageBox.Show(this, $"'{row.HostNames}' girdisi hosts dosyasından kaldırılsın mı?", "Girdiyi sil",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { _snapshot.Document.Remove(row.LineIndex); MarkDirty(); }
        catch (Exception exception) { ShowError(exception); }
    }

    private void Save(object? sender, RoutedEventArgs args)
    {
        try
        {
            _store.Save(_snapshot.Document, _snapshot);
            _snapshot = _store.Load();
            _dirty = false;
            LoadRows();
            _status.Text = "Kaydedildi · hosts dosyasının yedeği oluşturuldu";
        }
        catch (Exception exception)
        {
            _status.Text = "Kaydetme başarısız";
            ShowError(exception);
        }
    }

    public void SetEntryPlacement(bool insertNearHeader) => _insertNearHeader = insertNearHeader;

    private void Reload()
    {
        if (_dirty && !ConfirmDiscardChanges()) return;
        try
        {
            _snapshot = _store.Load();
            _dirty = false;
            LoadRows();
            _status.Text = "Hosts dosyası yeniden yüklendi";
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private void MarkDirty()
    {
        _dirty = true;
        LoadRows();
        _status.Text = "Kaydedilmemiş değişiklikler";
    }

    private HostsFileRow? SelectedRow() => _grid.SelectedItem as HostsFileRow;

    private void ConfirmDiscard(object? sender, CancelEventArgs args)
    {
        if (_dirty && !ConfirmDiscardChanges()) args.Cancel = true;
    }

    private bool ConfirmDiscardChanges() => MessageBox.Show(this,
        "Kaydedilmemiş hosts değişiklikleri silinsin mi?", "Kaydedilmemiş değişiklikler",
        MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private void ShowError(Exception exception) => MessageBox.Show(this, exception.Message,
        "Hosts File Editor", MessageBoxButton.OK, MessageBoxImage.Error);

    private static Button Button(string text, RoutedEventHandler click, bool isPrimary = false)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 92,
            Height = 34,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
            Background = Brush(isPrimary ? "#3478D4" : "#293342"),
            Foreground = Brush("#F1F4F9"),
            BorderBrush = Brush("#465366"),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += click;
        return button;
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color)!);
}

internal sealed class HostsFileRow(string address, string hostNames, string comment, bool isActive, int lineIndex)
{
    public string Address { get; } = address;
    public string HostNames { get; } = hostNames;
    public string Comment { get; } = comment;
    public bool IsActive { get; } = isActive;
    public int LineIndex { get; } = lineIndex;
    public string SearchText => $"{Address} {HostNames} {Comment}";
}

internal sealed class HostsFileEntryDialog : Window
{
    public static HostsFileEntry? LastResult { get; private set; }
    private readonly TextBox _address;
    private readonly TextBox _hosts;
    private readonly TextBox _comment;
    private readonly CheckBox _active;
    private readonly int _lineIndex;

    public HostsFileEntryDialog(HostsFileEntry? entry)
    {
        LastResult = null;
        _lineIndex = entry?.LineIndex ?? -1;
        Title = entry is null ? "Yeni hosts girdisi" : "Hosts girdisini düzenle";
        Width = 470;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = Application.Current?.MainWindow;
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#171C25")!);
        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F4F9")!);

        var panel = new StackPanel { Margin = new Thickness(18) };
        _address = AddField(panel, "IP adresi", entry?.Address ?? "127.0.0.1");
        _hosts = AddField(panel, "Ana bilgisayar adları", entry?.HostNames ?? "example.test");
        _comment = AddField(panel, "Yorum (isteğe bağlı)", entry?.Comment ?? "");
        _active = new CheckBox { Content = "Etkin", IsChecked = entry?.IsActive ?? true, Margin = new Thickness(0, 8, 0, 12) };
        panel.Children.Add(_active);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "İptal", Width = 84, Height = 32, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var add = new Button { Content = "Uygula", Width = 84, Height = 32, IsDefault = true };
        add.Click += SaveEntry;
        buttons.Children.Add(cancel);
        buttons.Children.Add(add);
        panel.Children.Add(buttons);
        Content = panel;
    }

    private static TextBox AddField(Panel panel, string label, string value)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 4), FontWeight = FontWeights.SemiBold });
        var field = new TextBox { Text = value, Height = 32, Padding = new Thickness(8, 4, 8, 4) };
        panel.Children.Add(field);
        return field;
    }

    private void SaveEntry(object? sender, RoutedEventArgs args)
    {
        var entry = new HostsFileEntry(_address.Text.Trim(), _hosts.Text.Trim(), _comment.Text.Trim(), _active.IsChecked == true, _lineIndex);
        try { HostsFileDocument.Validate(entry); }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Geçersiz hosts girdisi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        LastResult = entry;
        DialogResult = true;
        Close();
    }
}
