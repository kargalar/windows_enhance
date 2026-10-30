using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinForge.Core;
using WinForge.FileTools.UI;

using WinForge.FileTools;
namespace WinForge.ClipboardLine;

internal enum ClipboardLibraryScope { All, Saved, Recent, Unorganized }

internal sealed partial class ClipboardShelfWindow : Window, IFeatureWindow, IManagedFeatureWindow
{
    private const int ShelfPageSize = 9;
    private const int LibraryPageSize = 60;
    private const int MaximumRecordingThumbnailCache = 120;
    private readonly ClipboardLibrary _library;
    private readonly ClipboardCapture _capture;
    private readonly IModuleContext _context;
    private readonly IFeatureUiAssetSurface _uiSurface;
    private readonly Dictionary<string,string> _assetUris = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox _search = new() { MinWidth = 220, ToolTip = "Metin, OCR, başlık, not ve etiket ara" };
    private readonly TextBlock _status = new() { Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _pinned = new() { Content = "Sabitlenenler", Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _trash = new() { Content = "Çöp", Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _selectMode = new() { Content = "Çoklu seçim", Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _type = new() { Width = 120, ItemsSource = new[] { "Tüm türler", "Görseller", "Metinler", "Bağlantılar", "Kayıtlar" }, SelectedIndex = 0 };
    private readonly ComboBox _date = new() { Width = 100, ItemsSource = new[] { "Tüm tarihler", "Bugün", "Son 7 gün", "Son 30 gün" }, SelectedIndex = 0 };
    private readonly TextBox _filterTags = new() { Width = 140, ToolTip = "Etiketler (virgülle; tümü eşleşmeli)" };
    private readonly ComboBox _sort = new() { Width = 150, ItemsSource = new[] { "İlgililik / varsayılan", "Son kopyalanan", "Başlık A–Z", "Başlık Z–A", "En eski", "En yeni", "Tür", "Etiketler" }, SelectedIndex = 0 };
    private readonly TextBox _domain = new() { Width = 130, ToolTip = "Bağlantı alan adı filtresi" };
    private readonly StackPanel _sidebar = new() { Margin = new Thickness(0, 0, 8, 0) };
    private readonly Grid _shelves = new();
    private readonly ListBox _libraryList = new() { SelectionMode = SelectionMode.Extended };
    private readonly DockPanel _libraryArea = new();
    private readonly Grid _listHeader;
    private readonly ListBox[] _rows = new ListBox[3];
    private readonly int[] _pages = new int[3];
    private readonly int[] _pageSizes = [ShelfPageSize, ShelfPageSize, ShelfPageSize];
    private readonly TextBlock[] _rowCounts = new TextBlock[3];
    private readonly StackPanel _inspector = new() { Margin = new Thickness(12, 0, 0, 0) };
    private readonly TextBox _title = Editor();
    private readonly TextBox _content = Editor(multiline: true);
    private readonly TextBox _notes = Editor(multiline: true);
    private readonly TextBox _tags = Editor();
    private Grid? _workspace;
    private readonly System.Windows.Threading.DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _libraryMode;
    private ClipboardLibraryScope _libraryScope;
    private bool _refreshing;
    private bool _draftDirty;
    private bool _settingDraft;
    private Guid? _editingId;
    private Guid[]? _batchSelection;
    private Guid? _folder;
    private string? _tag;
    private int _libraryPage;
    private int _searchFocusGeneration;
    private int _actionsOpenGeneration;
    private int _folderPickerGeneration;
    private bool _folderPickerPin;
    private int _deleteConfirmGeneration;
    private ListBox? _activeRow;
    private IntPtr _previousWindow;
    private PasteTarget? _pasteTarget;
    private bool _activatedOnce;
    private HwndSource? _activationSource;
    internal IntPtr PreviousWindowForTest => _previousWindow;
    internal PasteTarget? PasteTargetForTest => _pasteTarget;
    private Point _dragStart;
    private bool _dragStarted;
    private bool _dragInProgress;
    private bool _refreshPending;
    private bool _dragOriginItem;
    private readonly ClipboardDragSession _dragSession = new();
    private readonly Dictionary<string, Task<RecordingPreview?>> _recordingThumbnails = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _thumbnailLifetime = new();

    internal ClipboardShelfWindow(ClipboardLibrary library, ClipboardCapture capture, IModuleContext context)
    {
        _library = library; _capture = capture; _context = context;
        _listHeader = CreateLibraryHeader(_library.Preferences.ListColumnWidths);
        Title = "WinForge · ClipBoardLine"; Width = 1180; Height = 790; MinWidth = 850; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/WinForge.Modules.FileTools;component/Clipboard/ClipboardTheme.xaml", UriKind.Relative) });
        SetResourceReference(BackgroundProperty, "BgBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        toolbar.Children.Add(ActionButton("Raflar", () => SetMode(false))); toolbar.Children.Add(ActionButton("Kütüphane", () => SetMode(true)));
        toolbar.Children.Add(_search); toolbar.Children.Add(_searchMode);
        var filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(filters, Dock.Top); root.Children.Add(filters);
        _pinned.SetResourceReference(Control.ForegroundProperty, "TextBrush"); _trash.SetResourceReference(Control.ForegroundProperty, "TextBrush"); _selectMode.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        filters.Children.Add(_pinned); filters.Children.Add(_trash); filters.Children.Add(_selectMode); filters.Children.Add(_type); filters.Children.Add(_date);
        filters.Children.Add(new TextBlock { Text = "Alan adı", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 5, 0) }); filters.Children.Add(_domain); filters.Children.Add(_filterTags); filters.Children.Add(_sort);
        var commands = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(commands, Dock.Top); root.Children.Add(commands);
        var primaryActions = new HashSet<string>(StringComparer.Ordinal) { "Kopyala", "Yapıştır", "Önizle", "Sabitle", "Dosya al", "Eylemler", "Ayarlar" };
        foreach (var (name, action) in Actions().Where(item => primaryActions.Contains(item.Name))) commands.Children.Add(ActionButton(name, () => Execute(action)));
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        var layout = _library.Preferences;
        var workspace = new Grid { ClipToBounds = true }; _workspace = workspace; root.Children.Add(workspace);
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(layout.SidebarWidth), MinWidth = 160, MaxWidth = 400 });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(layout.InspectorWidth), MinWidth = 220, MaxWidth = 480 });
        var leftScroll = new ScrollViewer { Content = _sidebar, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        workspace.Children.Add(leftScroll);
        var sidebarSplitter = WorkspaceSplitter(); Grid.SetColumn(sidebarSplitter, 1); workspace.Children.Add(sidebarSplitter);
        var area = new Grid(); Grid.SetColumn(area, 2); workspace.Children.Add(area); area.Children.Add(_shelves); area.Children.Add(_libraryArea);
        var inspectorSplitter = WorkspaceSplitter(); Grid.SetColumn(inspectorSplitter, 3); workspace.Children.Add(inspectorSplitter);
        var inspectorScroll = new ScrollViewer { Content = _inspector, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(inspectorScroll, 4); workspace.Children.Add(inspectorScroll);
        sidebarSplitter.DragCompleted += (_, _) => PersistWorkspaceWidths();
        inspectorSplitter.DragCompleted += (_, _) => PersistWorkspaceWidths();
        DockPanel.SetDock(_listHeader, Dock.Top); _libraryArea.Children.Add(_listHeader); _libraryArea.Children.Add(_libraryList);
        for (var index = 0; index < 3; index++)
        {
            var rowIndex = index; _shelves.RowDefinitions.Add(new RowDefinition());
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) }; Grid.SetRow(row, index); _shelves.Children.Add(row);
            var header = new DockPanel(); DockPanel.SetDock(header, Dock.Top); row.Children.Add(header);
            _rowCounts[index] = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5) };
            header.Children.Add(_rowCounts[index]);
            var paging = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            paging.Children.Add(ActionButton("‹", () => ChangeShelfPage(rowIndex, -1))); paging.Children.Add(ActionButton("›", () => ChangeShelfPage(rowIndex, 1))); header.Children.Add(paging);
            var list = new ListBox { SelectionMode = SelectionMode.Extended, BorderThickness = new Thickness(0) }; _rows[index] = list;
            ApplyListStyle(list);
            list.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel))); list.ItemsPanel.VisualTree.SetValue(VirtualizingStackPanel.OrientationProperty, Orientation.Horizontal);
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Auto); ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            WireList(list); row.Children.Add(list);
            list.SizeChanged += (_, _) =>
            {
                if (!_libraryMode && !_refreshing && ShelfPageCapacity(list.ActualWidth, _library.Preferences.CardSize) != _pageSizes[rowIndex]) Render();
            };
        }
        ApplyListStyle(_libraryList); WireList(_libraryList); WireLibraryHeader(); BuildInspector(); InitializeSemanticSearch();
        _search.TextChanged += (_, _) => ResetAndRender(); _domain.TextChanged += (_, _) => ResetAndRender();
        _filterTags.TextChanged += (_, _) => ResetAndRender(); _sort.SelectionChanged += (_, _) => ResetAndRender();
        _type.SelectionChanged += (_, _) => ResetAndRender(); _date.SelectionChanged += (_, _) => ResetAndRender();
        _pinned.Click += (_, _) => ResetAndRender(); _trash.Click += (_, _) => { SetMode(true); ResetAndRender(); };
        _library.Changed += LibraryChanged;
        _saveTimer.Tick += (_, _) => { SaveDraft(); PushReactState(); };
        PreviewKeyDown += OnKey;
        AllowDrop = true; Drop += (_, args) => { if (args.Data.GetData(DataFormats.FileDrop) is string[] files) foreach (var file in files.Take(100)) _capture.ImportFile(file); args.Handled = true; };
        SourceInitialized += (_, _) =>
        {
            _activationSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _activationSource?.AddHook(TrackActivation);
        };
        Closing += (_, args) => { if (!SaveDraft()) args.Cancel = true; };
        Closed += (_, _) => { _activationSource?.RemoveHook(TrackActivation); _thumbnailLifetime.Cancel(); _saveTimer.Stop(); _library.Changed -= LibraryChanged; };
        SetMode(false);
        _status.Text = library.RecoveryNotice ?? "Metin ve görsel yakalamayı Ayarlar’dan açabilirsiniz. Space: önizle · Enter: kopyala · P: sabitle · S: raftan kaldır.";
        var featureUi = (context as IFeatureUiContext)?.FeatureUi
            ?? throw new InvalidOperationException("Pano rafı için React arayüzü gerekli.");
        _uiSurface = featureUi.CreateSurface("clipboard-shelf") as IFeatureUiAssetSurface
            ?? throw new InvalidOperationException("Pano rafı için React varlık yüzeyi gerekli.");
        _uiSurface.MessageReceived += OnUiMessage;
        Content = _uiSurface.Element;
        Closed += (_, _) => _uiSurface.Dispose();
        PushReactState();
    }

    private GridSplitter WorkspaceSplitter()
    {
        var splitter = new GridSplitter
        {
            Width = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch,
            ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ShowsPreview = false, Cursor = Cursors.SizeWE, ToolTip = "Sürükleyerek panel genişliğini değiştir"
        };
        splitter.SetResourceReference(Control.BackgroundProperty, "BorderBrush");
        return splitter;
    }

    private void PersistWorkspaceWidths()
    {
        if (_workspace is not { ColumnDefinitions.Count: 5 } workspace) return;
        var sidebarWidth = workspace.ColumnDefinitions[0].ActualWidth;
        var inspectorWidth = workspace.ColumnDefinitions[4].ActualWidth;
        if (!double.IsFinite(sidebarWidth) || !double.IsFinite(inspectorWidth) || sidebarWidth is < 160 or > 400 || inspectorWidth is < 220 or > 480) return;
        _library.SetPreferences(_library.Preferences with { SidebarWidth = sidebarWidth, InspectorWidth = inspectorWidth });
    }

    private IntPtr TrackActivation(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int windowActivate = 0x0006;
        if (message != windowActivate || ((long)wParam & 0xffff) == 0) return IntPtr.Zero;
        if (!_activatedOnce) { _activatedOnce = true; return IntPtr.Zero; }
        if (lParam == IntPtr.Zero || !IsWindow(lParam)) return IntPtr.Zero;
        GetWindowThreadProcessId(lParam, out var processId);
        if (processId == 0 || processId == Environment.ProcessId) return IntPtr.Zero;
        var observed = _capture.PasteTargets.CaptureCurrent();
        _previousWindow = lParam;
        _pasteTarget = observed?.Window == lParam ? observed : null;
        return IntPtr.Zero;
    }

    public void BeforeShow()
    {
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out var processId);
        if (processId != Environment.ProcessId)
        {
            _previousWindow = foreground;
            var focused = _capture.PasteTargets.CaptureFocusedAsync().GetAwaiter().GetResult();
            var observed = _capture.PasteTargets.CaptureCurrent();
            _pasteTarget = focused ?? (observed?.Window == foreground ? observed : null);
        }
        Render();
    }

    void IManagedFeatureWindow.CloseForModule()
    {
        if (OwnedWindows.Cast<Window>().Any(window => window.IsVisible))
            throw new InvalidOperationException("Açık pano önizlemesini veya düzenleyicisini kapatın, ardından modülü kapatın.");
        Close();
    }

    internal double ShelfWidthForTest(int row) => _rows[row].ActualWidth;
    internal string[] ShelfTextsForTest(int row) => _rows[row].Items.Cast<ListBoxItem>().SelectMany(item => ((StackPanel)item.Content).Children.OfType<TextBlock>()).Select(block => block.Text).ToArray();
    internal int ShelfItemCountForTest(int row) => _rows[row].Items.Count;
    internal Guid[] LibraryItemIdsForTest => _libraryList.Items.Cast<ListBoxItem>().Select(item => (Guid)item.Tag).ToArray();
    internal Grid? WorkspaceForTest => _workspace;
    internal Grid LibraryHeaderForTest => _listHeader;
    internal int LibraryColumnSplitterCountForTest => _listHeader.Children.OfType<GridSplitter>().Count();
    internal int SortIndexForTest => _sort.SelectedIndex;
    internal void PersistWorkspaceWidthsForTest() => PersistWorkspaceWidths();
    internal void PersistListColumnWidthsForTest() => PersistListColumnWidths();
    internal void FocusSearch() { SetMode(false); _search.Focus(); }
    internal void ShowLibrary() => SetMode(true);
    internal void TakeScreenshot() => Execute(Screenshot);
    internal void CopyLatestImage()
    {
        var item = _library.Items.FirstOrDefault(item => item.Kind == ClipKind.Image && item.TrashedAt is null);
        if (item is null) { _context.Notify("Kopyalanacak görsel yok."); return; }
        Execute(() => CopyItems([item], false));
    }

    private static TextBox Editor(bool multiline = false) => new() { Margin = new Thickness(0, 3, 0, 9), AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 80 : 30, MaxHeight = multiline ? 180 : 40, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    internal static Button ActionButton(string label, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(3), MinHeight = 32 };
        if (Application.Current.TryFindResource("GhostButton") is Style style) button.Style = style;
        button.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        button.SetResourceReference(Control.BackgroundProperty, "CardBrush");
        button.Click += (_, _) => action(); return button;
    }

    private static void ApplyListStyle(ListBox list)
    {
        list.SetResourceReference(Control.BackgroundProperty, "CardBrush");
        list.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        list.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
    }

    private IEnumerable<(string Name, Action Action)> Actions()
    {
        yield return ("Kopyala", () => Copy(false)); yield return ("Yapıştır", () => Copy(true));
        yield return ("Önizle", Preview); yield return ("Sabitle", () => _library.TogglePin(SelectedIds()));
        yield return ("Kaydı kırp", () => { if (Selection().FirstOrDefault() is { Kind: ClipKind.Recording } item) EditRecording(item); });
        yield return ("Klasöre ekle", () => AssignFolder()); yield return ("Adlandır ve sabitle", NameAndPin); yield return ("Dışa aktar", Export);
        yield return ("Etiket ekle", AddTags); yield return ("Tanınan metni kopyala", CopyRecognizedText); yield return ("Bağlantıyı aç", OpenLink);
        yield return ("Bağlantı bilgilerini yenile", RefreshLinkMetadata);
        yield return ("Not oluştur", NewNote); yield return ("Dosya al", Import); yield return ("Ekran al", Screenshot);
        yield return ("Raftan kaldır", () => _library.TakeDown(SelectedIds()));
        yield return ("Çöpe taşı", () => _library.Trash(SelectedIds())); yield return ("Geri yükle", () => _library.Restore(SelectedIds()));
        yield return ("Geri al", _library.Undo); yield return ("Yinele", _library.Redo);
        yield return ("Ayarlar", Settings);
    }

    private void Execute(Action action)
    {
        try { if (SaveDraft()) action(); }
        catch (Exception error) { _context.Log.Error("Pano eylemi tamamlanamadı.", error); _status.Text = error.Message; }
        PushReactState();
    }

    private void SetMode(bool library)
    {
        if (!SaveDraft()) return;
        _libraryMode = library; _shelves.Visibility = library ? Visibility.Collapsed : Visibility.Visible;
        _libraryArea.Visibility = library ? Visibility.Visible : Visibility.Collapsed;
        _filterTags.Visibility = _sort.Visibility = library ? Visibility.Visible : Visibility.Collapsed;
        _activeRow = library ? _libraryList : _rows.FirstOrDefault(row => row.Items.Count > 0);
        _sidebar.Visibility = library ? Visibility.Visible : Visibility.Collapsed;
        _inspector.Visibility = library ? Visibility.Visible : Visibility.Collapsed;
        RequestSemanticSearch();
        Render();
    }

    private void ResetAndRender() { _batchSelection = null; Array.Clear(_pages); _libraryPage = 0; RequestSemanticSearch(); Render(); }
    private void LibraryChanged(object? sender, EventArgs args) { if (!Dispatcher.CheckAccess()) Dispatcher.BeginInvoke(new Action(RefreshAfterLibraryChange)); else RefreshAfterLibraryChange(); }
    private void RefreshAfterLibraryChange()
    {
        if (_dragInProgress) { _refreshPending = true; return; }
        if (_batchSelection is not null)
        {
            var visible = Scope("").Select(item => item.Id).ToHashSet();
            _batchSelection = _batchSelection.Where(visible.Contains).ToArray();
            if (_batchSelection.Length == 0) _batchSelection = null;
        }
        RequestSemanticSearch(); Render();
    }
    private IReadOnlyList<ClipItem> Scope(string query)
    {
        var items = _library.Search(query, !_libraryMode, _trash.IsChecked == true, _folder, _tag, _pinned.IsChecked == true,
        _type.SelectedIndex switch { 1 => ClipKind.Image, 2 => ClipKind.Text, 3 => ClipKind.Link, 4 => ClipKind.Recording, _ => null },
        _date.SelectedIndex switch { 1 => DateTimeOffset.Now.Date, 2 => DateTimeOffset.Now.AddDays(-7), 3 => DateTimeOffset.Now.AddDays(-30), _ => null }, string.IsNullOrWhiteSpace(_domain.Text) ? null : _domain.Text.Trim());
        if (!_libraryMode) return items;
        var filtered = FilterTags(items, _filterTags.Text);
        return _trash.IsChecked == true || _folder is not null || _tag is not null
            ? filtered : ApplyLibraryScope(filtered, _libraryScope);
    }

    internal static IReadOnlyList<ClipItem> FilterTags(IEnumerable<ClipItem> items, string filter)
    {
        var tags = filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return items.Where(item => tags.All(tag => item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))).ToArray();
    }

    internal static IReadOnlyList<ClipItem> SortLibraryResults(IEnumerable<ClipItem> items, int order) => order switch
    {
        1 => items.OrderByDescending(item => item.LastCopiedAt ?? item.UpdatedAt).ToArray(),
        2 => items.OrderBy(item => item.DisplayTitle, StringComparer.CurrentCultureIgnoreCase).ToArray(),
        3 => items.OrderByDescending(item => item.DisplayTitle, StringComparer.CurrentCultureIgnoreCase).ToArray(),
        4 => items.OrderBy(item => item.CreatedAt).ToArray(),
        5 => items.OrderByDescending(item => item.CreatedAt).ToArray(),
        6 => items.OrderBy(item => item.Kind).ThenBy(item => item.DisplayTitle, StringComparer.CurrentCultureIgnoreCase).ToArray(),
        7 => items.OrderBy(item => string.Join(",", item.Tags), StringComparer.CurrentCultureIgnoreCase).ToArray(),
        _ => items.ToArray()
    };

    internal static IReadOnlyList<ClipItem> ApplyLibraryScope(IEnumerable<ClipItem> items, ClipboardLibraryScope scope)
    {
        static bool Saved(ClipItem item) => item.Saved || item.Pinned || item.FolderIds.Length > 0 || item.Tags.Length > 0;
        return items.Where(item => scope switch
        {
            ClipboardLibraryScope.Saved => Saved(item),
            ClipboardLibraryScope.Recent => !Saved(item),
            ClipboardLibraryScope.Unorganized => Saved(item) && item.FolderIds.Length == 0 && item.Tags.Length == 0,
            _ => true
        }).ToArray();
    }

    private void SetLibraryScope(ClipboardLibraryScope scope)
    {
        _libraryScope = scope; _folder = null; _tag = null; _trash.IsChecked = false; ResetAndRender();
    }

    private void Render()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var selected = SelectedIds().ToHashSet(); var results = Results();
            var previousIndex = Math.Max(0, _activeRow?.SelectedIndex ?? 0);
            if (_libraryMode)
            {
                _libraryPage = Math.Clamp(_libraryPage, 0, Math.Max(0, (results.Count - 1) / LibraryPageSize));
                _listHeader.Visibility = _library.Preferences.ListView ? Visibility.Visible : Visibility.Collapsed;
                Populate(_libraryList, results.Skip(_libraryPage * LibraryPageSize).Take(LibraryPageSize), selected, !_library.Preferences.ListView);
                RenderSidebar(results.Count);
            }
            else
            {
                for (var index = 0; index < 3; index++)
                {
                    var values = results.Where(item => RowFor(item.Kind) == index).ToArray();
                    var pageSize = ShelfPageCapacity(_rows[index].ActualWidth, _library.Preferences.CardSize);
                    if (pageSize != _pageSizes[index])
                    {
                        var oldOffset = (long)_pages[index] * _pageSizes[index];
                        _pages[index] = (int)Math.Min(int.MaxValue, oldOffset / pageSize);
                        _pageSizes[index] = pageSize;
                    }
                    _pages[index] = Math.Clamp(_pages[index], 0, Math.Max(0, (values.Length - 1) / pageSize));
                    _rowCounts[index].Text = $"{new[] { "Görseller ve kayıtlar", "Metinler", "Bağlantılar" }[index]} · {values.Length} · {_pages[index] + 1}. sayfa";
                    _rows[index].Visibility = _library.Visible(index == 0 ? ClipKind.Image : index == 1 ? ClipKind.Text : ClipKind.Link) ? Visibility.Visible : Visibility.Collapsed;
                    Populate(_rows[index], values.Skip(_pages[index] * pageSize).Take(pageSize), selected, true);
                }
            }
            _activeRow ??= _libraryMode ? _libraryList : _rows.FirstOrDefault(row => row.Items.Count > 0);
            if (_batchSelection?.Length != 0 && _activeRow is not null && _activeRow.SelectedItems.Count == 0 && _activeRow.Items.Count > 0) _activeRow.SelectedIndex = Math.Min(previousIndex, _activeRow.Items.Count - 1);
        }
        finally { _refreshing = false; }
        FillInspector();
        PushReactState();
    }

    private void PushReactState()
    {
        var result = Results();
        var selected = SelectedIds().Select(id => id.ToString("D")).ToArray();
        object Card(ClipItem item) => new
        {
            id = item.Id.ToString("D"), kind = item.Kind.ToString().ToLowerInvariant(), title = item.DisplayTitle,
            description = item.Kind == ClipKind.Text ? item.Content : item.Kind == ClipKind.Link ? item.Content : "",
            created = item.CreatedAt.ToLocalTime().ToString("g"), tags = item.Tags, pinned = item.Pinned,
            saved = item.Saved, preview = AssetUri(item)
        };
        var rows = Enumerable.Range(0, 3).Select(index =>
        {
            var values = result.Where(item => RowFor(item.Kind) == index).ToArray();
            var size = Math.Max(1, _pageSizes[index]);
            return new
            {
                index, title = new[] { "Görseller ve kayıtlar", "Metinler", "Bağlantılar" }[index],
                visible = _library.Visible(index == 0 ? ClipKind.Image : index == 1 ? ClipKind.Text : ClipKind.Link),
                count = values.Length, page = _pages[index], pageCount = Math.Max(1, (values.Length + size - 1) / size),
                items = values.Skip(_pages[index] * size).Take(size).Select(Card).ToArray()
            };
        }).ToArray();
        var libraryItems = result.Skip(_libraryPage * LibraryPageSize).Take(LibraryPageSize).Select(Card).ToArray();
        var selectedItem = Selection().FirstOrDefault();
        _uiSurface.SetState(new
        {
            libraryMode = _libraryMode, status = _status.Text, query = _search.Text, searchMode = _searchMode.SelectedIndex,
            pinnedOnly = _pinned.IsChecked == true, trash = _trash.IsChecked == true, selectMode = _selectMode.IsChecked == true,
            type = _type.SelectedIndex, date = _date.SelectedIndex, domain = _domain.Text, tagsFilter = _filterTags.Text,
            sort = _sort.SelectedIndex, scope = _libraryScope.ToString().ToLowerInvariant(), selected,
            title = _title.Text, content = _content.Text, notes = _notes.Text, itemTags = _tags.Text,
            inspectorEnabled = selectedItem is not null, inspectorContentReadOnly = selectedItem is null || selectedItem.Kind is ClipKind.Image or ClipKind.Recording,
            folders = OrderedFolders(_library.Folders).Select(entry => new { id = entry.Folder.Id.ToString("D"), name = entry.Folder.Name, depth = entry.Depth, selected = _folder == entry.Folder.Id }).ToArray(),
            tagList = _library.Items.SelectMany(item => item.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order().Select(tag => new { name = tag, selected = string.Equals(_tag, tag, StringComparison.OrdinalIgnoreCase) }).ToArray(),
            actions = Actions().Select((entry, index) => new { id = index, name = entry.Name }).ToArray(),
            rows, items = libraryItems, page = _libraryPage, pageCount = Math.Max(1, (result.Count + LibraryPageSize - 1) / LibraryPageSize),
            listView = _library.Preferences.ListView, cardSize = _library.Preferences.CardSize,
            showImages = _library.Preferences.ShowImages, showTexts = _library.Preferences.ShowTexts, showLinks = _library.Preferences.ShowLinks,
            count = result.Count, selectionCount = selected.Length, searchFocusGeneration = _searchFocusGeneration,
            actionsOpenGeneration = _actionsOpenGeneration, folderPickerGeneration = _folderPickerGeneration,
            folderPickerPin = _folderPickerPin, deleteConfirmGeneration = _deleteConfirmGeneration,
            permanentDeleteCount = SelectedIds().Count(id => _library.Items.Any(item => item.Id == id && item.TrashedAt is not null))
        });
    }

    private string AssetUri(ClipItem item)
    {
        if (item.Kind is not (ClipKind.Image or ClipKind.Link)) return "";
        try
        {
            var path = item.Kind == ClipKind.Image ? _library.MediaPath(item) : item.LinkThumbnail.Length > 0 ? _library.ThumbnailPath(item) : "";
            if (path.Length == 0 || !File.Exists(path)) return "";
            if (!_assetUris.TryGetValue(path, out var uri)) _assetUris[path] = uri = _uiSurface.MapLocalAsset(path).AbsoluteUri;
            return uri;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { return ""; }
    }

    private void OnUiMessage(string command, JsonElement payload)
    {
        try
        {
            switch (command)
            {
                case "clipboard.mode": SetMode(payload.GetProperty("library").GetBoolean()); break;
                case "clipboard.search": _search.Text = payload.GetProperty("value").GetString() ?? ""; break;
                case "clipboard.filter": ApplyUiFilter(payload); break;
                case "clipboard.scope": SetLibraryScope(Enum.Parse<ClipboardLibraryScope>(payload.GetProperty("value").GetString()!, true)); break;
                case "clipboard.page": ChangeUiPage(payload); break;
                case "clipboard.select": ApplyUiSelection(payload); break;
                case "clipboard.quick-copy": ApplyUiSelection(payload); Execute(() => Copy(false)); break;
                case "clipboard.preview": ApplyUiSelection(payload); Execute(Preview); break;
                case "clipboard.action": RunUiAction(payload.GetProperty("id").GetInt32()); break;
                case "clipboard.detail": UpdateUiDetail(payload); break;
                case "clipboard.preference": UpdateUiPreference(payload); break;
                case "clipboard.folder": _folder = Guid.Parse(payload.GetProperty("id").GetString()!); _tag = null; ResetAndRender(); break;
                case "clipboard.tag": _tag = payload.GetProperty("value").GetString(); _folder = null; ResetAndRender(); break;
                case "clipboard.folder.rename": RenameFolderFromReact(payload); break;
                case "clipboard.folder.remove": RemoveFolderFromReact(payload); break;
                case "clipboard.tag.rename": RenameTagFromReact(payload); break;
                case "clipboard.tag.remove": RemoveTagFromReact(payload); break;
                case "clipboard.assign-folder": AssignFolderFromReact(payload); break;
                case "clipboard.folder.create": CreateFolderFromReact(payload); break;
                case "clipboard.delete-confirm": ConfirmPermanentDelete(payload.GetProperty("confirm").GetBoolean()); break;
                case "clipboard.key": RunUiKey(payload); break;
            }
        }
        catch (Exception error) { _context.Log.Error("React pano arayüzü komutu tamamlanamadı.", error); _status.Text = error.Message; PushReactState(); }
    }

    private void RenameFolderFromReact(JsonElement payload)
    {
        if (!Guid.TryParse(payload.GetProperty("id").GetString(), out var id) ||
            _library.Folders.FirstOrDefault(folder => folder.Id == id) is not { } folder) return;
        var name = Prompt("Klasör adı", folder.Name);
        if (name is not null) _library.RenameFolder(id, name);
        ResetAndRender();
    }

    private void RemoveFolderFromReact(JsonElement payload)
    {
        if (!Guid.TryParse(payload.GetProperty("id").GetString(), out var id)) return;
        _library.RemoveFolder(id);
        if (_folder == id) _folder = null;
        ResetAndRender();
    }

    private void RenameTagFromReact(JsonElement payload)
    {
        var oldName = payload.GetProperty("name").GetString();
        if (string.IsNullOrWhiteSpace(oldName)) return;
        var newName = Prompt("Etiket adı", oldName);
        if (newName is not null) _library.RenameTag(oldName, newName);
        ResetAndRender();
    }

    private void RemoveTagFromReact(JsonElement payload)
    {
        var name = payload.GetProperty("name").GetString();
        if (string.IsNullOrWhiteSpace(name)) return;
        _library.RenameTag(name, null);
        if (string.Equals(_tag, name, StringComparison.OrdinalIgnoreCase)) _tag = null;
        ResetAndRender();
    }

    private void AssignFolderFromReact(JsonElement payload)
    {
        if (!Guid.TryParse(payload.GetProperty("id").GetString(), out var folderId) ||
            !_library.Folders.Any(folder => folder.Id == folderId)) return;
        var selected = ReadSelection(payload);
        if (selected.Length == 0) return;
        _library.AssignFolder(selected, folderId, _folderPickerPin);
        ResetAndRender();
    }

    private void CreateFolderFromReact(JsonElement payload)
    {
        var selected = ReadSelection(payload);
        if (selected.Length == 0) return;
        var name = Prompt("Klasör adı");
        if (string.IsNullOrWhiteSpace(name)) return;
        var folder = _library.CreateFolder(name, _folder);
        _library.AssignFolder(selected, folder.Id, _folderPickerPin);
        ResetAndRender();
    }

    private static Guid[] ReadSelection(JsonElement payload) =>
        payload.TryGetProperty("ids", out var ids)
            ? ids.EnumerateArray().Select(value => Guid.TryParse(value.GetString(), out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty).Distinct().ToArray()
            : [];

    private void ApplyUiFilter(JsonElement payload)
    {
        var key = payload.GetProperty("key").GetString(); var value = payload.GetProperty("value");
        switch (key)
        {
            case "searchMode": _searchMode.SelectedIndex = value.GetInt32(); break;
            case "pinned": _pinned.IsChecked = value.GetBoolean(); break;
            case "trash": _trash.IsChecked = value.GetBoolean(); if (_trash.IsChecked == true) SetMode(true); break;
            case "selectMode": _selectMode.IsChecked = value.GetBoolean(); break;
            case "type": _type.SelectedIndex = value.GetInt32(); break;
            case "date": _date.SelectedIndex = value.GetInt32(); break;
            case "domain": _domain.Text = value.GetString() ?? ""; break;
            case "tags": _filterTags.Text = value.GetString() ?? ""; break;
            case "sort": _sort.SelectedIndex = value.GetInt32(); break;
        }
        ResetAndRender();
    }

    private void ChangeUiPage(JsonElement payload)
    {
        var delta = payload.GetProperty("delta").GetInt32();
        if (_libraryMode) _libraryPage = Math.Clamp(_libraryPage + delta, 0, Math.Max(0, (Results().Count - 1) / LibraryPageSize));
        else
        {
            var row = payload.GetProperty("row").GetInt32();
            if (row is < 0 or >= 3) return;
            _pages[row] = Math.Max(0, _pages[row] + delta); _activeRow = _rows[row];
        }
        Render();
    }

    private void ApplyUiSelection(JsonElement payload)
    {
        var ids = payload.GetProperty("ids").EnumerateArray().Select(value => Guid.Parse(value.GetString()!)).ToArray();
        _batchSelection = ids;
        if (ids.Length > 0)
        {
            var item = _library.Items.FirstOrDefault(value => value.Id == ids[0]);
            _activeRow = _libraryMode ? _libraryList : item is null ? _activeRow : _rows[RowFor(item.Kind)];
        }
        Render();
    }

    private void RunUiAction(int index)
    {
        var actions = Actions().ToArray();
        if (index < 0 || index >= actions.Length) return;
        Execute(actions[index].Action); PushReactState();
    }

    private void UpdateUiDetail(JsonElement payload)
    {
        var key = payload.GetProperty("key").GetString(); var value = payload.GetProperty("value").GetString() ?? "";
        _settingDraft = true;
        try
        {
            switch (key) { case "title": _title.Text = value; break; case "content": _content.Text = value; break; case "notes": _notes.Text = value; break; case "tags": _tags.Text = value; break; }
        }
        finally { _settingDraft = false; }
        if (_editingId is not null) { _draftDirty = true; _saveTimer.Stop(); _saveTimer.Start(); }
        PushReactState();
    }

    private void UpdateUiPreference(JsonElement payload)
    {
        var key = payload.GetProperty("key").GetString(); var value = payload.GetProperty("value"); var preferences = _library.Preferences;
        preferences = key switch
        {
            "showImages" => preferences with { ShowImages = value.GetBoolean() },
            "showTexts" => preferences with { ShowTexts = value.GetBoolean() },
            "showLinks" => preferences with { ShowLinks = value.GetBoolean() },
            "listView" => preferences with { ListView = value.GetBoolean() },
            "cardSize" => preferences with { CardSize = value.GetString() ?? "medium" },
            _ => preferences
        };
        _library.SetPreferences(preferences); ResetAndRender();
    }

    private void RunUiKey(JsonElement payload)
    {
        var keyName=payload.GetProperty("key").GetString()??"";
        var key=ClipboardKeymap.ParseWebKey(keyName);
        if(key==Key.None)return;
        var modifiers=ModifierKeys.None;
        if(payload.TryGetProperty("ctrl",out var ctrl)&&ctrl.GetBoolean())modifiers|=ModifierKeys.Control;
        if(payload.TryGetProperty("shift",out var shift)&&shift.GetBoolean())modifiers|=ModifierKeys.Shift;
        if(payload.TryGetProperty("alt",out var alt)&&alt.GetBoolean())modifiers|=ModifierKeys.Alt;
        var command=ClipboardKeymap.Match(key,modifiers,_library.Preferences.LocalShortcuts);
        Action? action=command switch
        {
            "onizle"=>Preview,
            "tur-tumu" when _libraryMode=>()=>ApplyReactType(0),"tur-gorsel" when _libraryMode=>()=>ApplyReactType(1),"tur-metin" when _libraryMode=>()=>ApplyReactType(2),"tur-baglanti" when _libraryMode=>()=>ApplyReactType(3),
            "raf-gorsel" when !_libraryMode=>()=>ToggleShelfVisibility(ClipKind.Image),"raf-metin" when !_libraryMode=>()=>ToggleShelfVisibility(ClipKind.Text),"raf-baglanti" when !_libraryMode=>()=>ToggleShelfVisibility(ClipKind.Link),
            "yapistir"=>()=>Copy(true),"disa-aktar"=>Export,"etiket-ekle"=>AddTags,"baglanti-ac"=>OpenLink,"ocr-kopyala"=>CopyRecognizedText,
            "kopyala"=>()=>{Copy(false);if(!_libraryMode)ReturnFocus();},"sabitle"=>()=>_library.TogglePin(SelectedIds()),"klasore-sabitle"=>()=>AssignFolder(pin:true),"adlandir-sabitle"=>NameAndPin,
            "kaydi-kirp"=>()=>{if(Selection().FirstOrDefault() is{Kind:ClipKind.Recording} item)EditRecording(item);},"raftan-kaldir"=>()=>_library.TakeDown(SelectedIds()),
            "geri-al" or "duzenlemeyi-geri-al"=>_library.Undo,"kutuphane"=>()=>SetMode(true),"cope-tasi"=>()=>_library.Trash(SelectedIds()),"arama" or "ara"=>()=>_searchFocusGeneration++,"yinele"=>_library.Redo,
            "tumunu-sec"=>SelectAllResults,"kopyala-acik"=>()=>Copy(false),"eylemler"=>()=>_actionsOpenGeneration++,"sonraki-raf" or "asagi-raf" when !_libraryMode=>()=>SwitchRow(1),"onceki-raf" or "yukari-raf" when !_libraryMode=>()=>SwitchRow(-1),
            "onceki-sayfa"=>()=>ChangePage(-1),"sonraki-sayfa"=>()=>ChangePage(1),"ilk-oge"=>()=>SelectBoundary(false),"son-oge"=>()=>SelectBoundary(true),"sabit-filtre"=>()=>{_pinned.IsChecked=_pinned.IsChecked!=true;ResetAndRender();},_=>null
        };
        if(key==Key.Escape&&command is null){ReturnFocus();return;}
        if(action is not null){Execute(action);return;}
        if(command is null&&modifiers==ModifierKeys.None&&key is >= Key.D1 and <= Key.D9&&_activeRow is not null)
        {
            var index=key-Key.D1;if(index<_activeRow.Items.Count){_activeRow.SelectedIndex=index;_batchSelection=[(Guid)((ListBoxItem)_activeRow.Items[index]).Tag];Render();}
            return;
        }
    }

    private void ApplyReactType(int index){_type.SelectedIndex=index;ResetAndRender();}

    private static int CardWidth(string size) => size switch { "small" => 135, "large" => 240, _ => 180 };
    internal static int ShelfPageCapacity(double width, string cardSize)
    {
        if (!double.IsFinite(width) || width <= 0) return ShelfPageSize;
        return Math.Clamp((int)Math.Min(ShelfPageSize, Math.Floor(width / (CardWidth(cardSize) + 16))), 1, ShelfPageSize);
    }
    private static int RowFor(ClipKind kind) => kind is ClipKind.Image or ClipKind.Recording ? 0 : kind == ClipKind.Text ? 1 : 2;
    private void Populate(ListBox list, IEnumerable<ClipItem> values, HashSet<Guid> selected, bool cards)
    {
        list.Items.Clear(); var number = 0;
        if (ReferenceEquals(list, _libraryList)) list.ItemsPanel = cards ? new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel))) : new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        foreach (var item in values)
        {
            if (!cards)
            {
                var kind = item.Kind switch { ClipKind.Image => "Görsel", ClipKind.Text => "Metin", ClipKind.Link => "Bağlantı", ClipKind.Recording => "Kayıt", _ => "" };
                var row = LibraryColumns([(item.Pinned ? "★ " : "") + item.DisplayTitle, kind, string.Join(", ", item.Tags), item.CreatedAt.ToLocalTime().ToString("g")]);
                list.Items.Add(new ListBoxItem { Content = row, Tag = item.Id, ToolTip = item.DisplayTitle, HorizontalContentAlignment = HorizontalAlignment.Stretch, IsSelected = selected.Contains(item.Id) });
                continue;
            }
            var card = new StackPanel { Width = cards ? CardWidth(_library.Preferences.CardSize) : double.NaN, Margin = new Thickness(4) };
            if (cards && (item.Kind == ClipKind.Image || item.Kind == ClipKind.Link && item.LinkThumbnail.Length > 0))
            {
                try { card.Children.Add(new Image { Source = Thumbnail(item.Kind == ClipKind.Image ? _library.MediaPath(item) : _library.ThumbnailPath(item)), Height = 78, Stretch = Stretch.Uniform }); }
                catch (Exception error) { card.Children.Add(new TextBlock { Text = "Görsel açılamadı", ToolTip = error.Message }); }
            }
            if (cards && item.Kind == ClipKind.Recording)
            {
                var image = new Image { Height = 78, Stretch = Stretch.Uniform, ToolTip = "Kayıt önizlemesi" };
                var duration = new TextBlock { Opacity = .7, HorizontalAlignment = HorizontalAlignment.Center };
                card.Children.Add(image); card.Children.Add(duration);
                _ = LoadRecordingThumbnailAsync(item, image, duration);
            }
            card.Children.Add(new TextBlock { Text = $"{++number}. {(item.Pinned ? "★ " : "")}{item.DisplayTitle}", MaxHeight = cards ? 58 : 32, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis });
            if (item.Kind == ClipKind.Link && item.LinkAuthor.Length > 0) card.Children.Add(new TextBlock { Text = item.LinkAuthor, Opacity = .7, TextTrimming = TextTrimming.CharacterEllipsis });
            if (item.Tags.Length > 0) card.Children.Add(new TextBlock { Text = string.Join(", ", item.Tags), Opacity = .65, TextTrimming = TextTrimming.CharacterEllipsis });
            var container = new ListBoxItem { Content = card, Tag = item.Id, ToolTip = item.DisplayTitle, HorizontalContentAlignment = HorizontalAlignment.Stretch, IsSelected = selected.Contains(item.Id) };
            list.Items.Add(container);
        }
    }

    private Grid LibraryColumns(string[] values)
    {
        var grid = new Grid { Margin = new Thickness(4, 3, 4, 3) };
        foreach (var weight in _library.Preferences.ListColumnWidths) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(weight, GridUnitType.Star), MinWidth = 48 });
        for (var index = 0; index < values.Length; index++)
        {
            var text = new TextBlock { Text = values[index], Margin = new Thickness(4, 0, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(text, index); grid.Children.Add(text);
        }
        return grid;
    }

    private Grid CreateLibraryHeader(double[] widths)
    {
        var header = new Grid { Margin = new Thickness(4, 3, 4, 3), MinHeight = 30 };
        for (var index = 0; index < 4; index++)
        {
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[index], GridUnitType.Star), MinWidth = 48 });
            if (index < 3) header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7) });
        }
        var values = new[] { "Başlık", "Tür", "Etiketler", "Oluşturulma" };
        for (var index = 0; index < values.Length; index++)
        {
            var text = new TextBlock { Text = values[index], Margin = new Thickness(4, 0, 4, 0), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, index * 2); header.Children.Add(text);
            if (index == 3) continue;
            var splitter = new GridSplitter
            {
                Width = 7, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ShowsPreview = false, Cursor = Cursors.SizeWE, ToolTip = "Sütun genişliğini değiştirmek için sürükleyin"
            };
            splitter.SetResourceReference(Control.BackgroundProperty, "BorderBrush");
            Grid.SetColumn(splitter, index * 2 + 1); header.Children.Add(splitter);
            splitter.DragCompleted += (_, _) => PersistListColumnWidths();
        }
        return header;
    }

    private void PersistListColumnWidths()
    {
        var widths = new[] { 0, 2, 4, 6 }.Select(index => _listHeader.ColumnDefinitions[index].ActualWidth).ToArray();
        var total = widths.Sum();
        if (!double.IsFinite(total) || total <= 0) return;
        var normalized = widths.Select(width => Math.Clamp(width / total * 100, 5, 80)).ToArray();
        var scale = 100 / normalized.Sum();
        normalized = normalized.Select(width => width * scale).ToArray();
        if (normalized.Any(width => width is < 5 or > 80)) return;
        _library.SetPreferences(_library.Preferences with { ListColumnWidths = normalized });
        for (var index = 0; index < widths.Length; index++)
            _listHeader.ColumnDefinitions[index * 2].Width = new GridLength(normalized[index], GridUnitType.Star);
    }

    private void WireLibraryHeader()
    {
        var orders = new Dictionary<int, int> { [0] = 2, [2] = 6, [4] = 7, [6] = 4 };
        foreach (var (column, order) in orders)
        {
            if (_listHeader.Children.OfType<TextBlock>().SingleOrDefault(block => Grid.GetColumn(block) == column) is not { } header) continue;
            header.Cursor = Cursors.Hand;
            header.ToolTip = order switch { 2 => "Başlığa göre sırala", 6 => "Türe göre sırala", 7 => "Etikete göre sırala", _ => "Eklenme tarihine göre sırala" };
            header.MouseLeftButtonUp += (_, args) =>
            {
                _sort.SelectedIndex = order switch
                {
                    2 => _sort.SelectedIndex == 2 ? 3 : 2,
                    4 => _sort.SelectedIndex == 4 ? 5 : 4,
                    _ => order
                };
                args.Handled = true;
            };
        }
    }

    private async Task LoadRecordingThumbnailAsync(ClipItem item, Image image, TextBlock duration)
    {
        if (!_recordingThumbnails.TryGetValue(item.Content, out var task))
        {
            if (_recordingThumbnails.Count >= MaximumRecordingThumbnailCache) _recordingThumbnails.Clear();
            task = CreateRecordingThumbnailAsync(item);
            _recordingThumbnails[item.Content] = task;
        }
        var preview = await task;
        if (_thumbnailLifetime.IsCancellationRequested || preview is null || Dispatcher.HasShutdownStarted) return;
        await Dispatcher.InvokeAsync(() =>
        {
            if (_thumbnailLifetime.IsCancellationRequested) return;
            image.Source = preview.Image;
            duration.Text = RecordingTools.FormatDuration(preview.Info.Duration);
        });
    }

    private async Task<RecordingPreview?> CreateRecordingThumbnailAsync(ClipItem item)
    {
        try
        {
            var path = _library.MediaPath(item);
            return await Task.Run(() => RecordingTools.PreviewAsync(path, _thumbnailLifetime.Token), _thumbnailLifetime.Token);
        }
        catch (OperationCanceledException) when (_thumbnailLifetime.IsCancellationRequested) { return null; }
        catch (Exception error) { _context.Log.Error("Kayıt önizlemesi oluşturulamadı.", error); return null; }
    }

    private static System.Windows.Media.Imaging.BitmapSource Thumbnail(string path)
    {
        var bitmap = new System.Windows.Media.Imaging.BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 320; bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }

    private void WireList(ListBox list)
    {
        list.SelectionChanged += (_, _) => { if (_refreshing) return; _batchSelection = null; if (!SaveDraft()) return; _activeRow = list; FillInspector(); };
        list.MouseDoubleClick += (_, _) => Execute(Preview);
        list.PreviewMouseLeftButtonDown += (_, args) => { _dragStart = args.GetPosition(list); _dragStarted = false; _dragOriginItem = args.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(list, source) is ListBoxItem; if (Keyboard.Modifiers == ModifierKeys.None) _batchSelection = null; };
        list.MouseMove += (_, args) => { if (args.LeftButton != MouseButtonState.Pressed || _dragStarted || !_dragOriginItem) return; var position = args.GetPosition(list); if (Math.Abs(position.X - _dragStart.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(position.Y - _dragStart.Y) > SystemParameters.MinimumVerticalDragDistance) { _dragStarted = true; Execute(DragSelection); } };
        list.PreviewMouseLeftButtonUp += (_, args) =>
        {
            if (_libraryMode || _dragStarted || _selectMode.IsChecked == true || Keyboard.Modifiers != ModifierKeys.None || args.OriginalSource is not DependencyObject source) return;
            if (ItemsControl.ContainerFromElement(list, source) is ListBoxItem { Tag: Guid id } && _library.Items.FirstOrDefault(item => item.Id == id && item.TrashedAt is null) is { } item)
                Execute(() => CopyItems([item], false));
        };
    }

    private Guid[] SelectedIds() => _batchSelection ?? (_activeRow?.SelectedItems.Cast<ListBoxItem>().Select(item => (Guid)item.Tag) ?? []).ToArray();
    private ClipItem[] Selection() { var selected = SelectedIds().ToHashSet(); return _library.Items.Where(item => selected.Contains(item.Id)).OrderBy(item => Array.IndexOf(SelectedIds(), item.Id)).ToArray(); }
    private void ChangeShelfPage(int index, int delta) { _pages[index] = Math.Max(0, _pages[index] + delta); _activeRow = _rows[index]; Render(); }

    internal static IEnumerable<(ClipFolder Folder, int Depth)> OrderedFolders(IReadOnlyList<ClipFolder> folders)
    {
        var children = folders.Where(folder => folder.ParentId is not null).GroupBy(folder => folder.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
        var pending = new Stack<(ClipFolder Folder, int Depth)>(folders.Where(folder => folder.ParentId is null)
            .OrderByDescending(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase).Select(folder => (folder, 0)));
        while (pending.TryPop(out var entry))
        {
            yield return entry;
            if (children.TryGetValue(entry.Folder.Id, out var nested))
                for (var index = nested.Length - 1; index >= 0; index--) pending.Push((nested[index], entry.Depth + 1));
        }
    }

    private void RenderSidebar(int count)
    {
        _sidebar.Children.Clear();
        _sidebar.Children.Add(ActionButton($"Tümü · {count}", () => SetLibraryScope(ClipboardLibraryScope.All)));
        _sidebar.Children.Add(ActionButton("Kaydedilenler", () => SetLibraryScope(ClipboardLibraryScope.Saved)));
        _sidebar.Children.Add(ActionButton("Son kopyalananlar", () => SetLibraryScope(ClipboardLibraryScope.Recent)));
        _sidebar.Children.Add(ActionButton("Düzenlenmemiş", () => SetLibraryScope(ClipboardLibraryScope.Unorganized)));
        _sidebar.Children.Add(ActionButton("+ Klasör", () => Execute(() => { var name = Prompt("Klasör adı"); if (name is not null) _library.CreateFolder(name, _folder); })));
        foreach (var (folder, depth) in OrderedFolders(_library.Folders))
        {
            var button = ActionButton((depth == 0 ? "" : new string(' ', Math.Min(depth, 8) * 2) + "↳ ") + folder.Name, () => { _folder = folder.Id; _tag = null; ResetAndRender(); });
            button.AllowDrop = true;
            button.DragOver += (_, args) => { args.Effects = InternalDragIds(args.Data).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None; args.Handled = true; };
            button.Drop += (_, args) =>
            {
                var ids = InternalDragIds(args.Data); args.Handled = true;
                if (ids.Count > 0 && _library.Folders.Any(candidate => candidate.Id == folder.Id)) Execute(() => _library.AssignFolder(ids, folder.Id));
            };
            _sidebar.Children.Add(button);
        }
        _sidebar.Children.Add(new TextBlock { Text = "Etiketler", Margin = new Thickness(5, 16, 5, 5) });
        foreach (var tag in _library.Items.SelectMany(item => item.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order())
        {
            var button = ActionButton("# " + tag, () => { _tag = tag; _folder = null; ResetAndRender(); });
            _sidebar.Children.Add(button);
        }
        _sidebar.Children.Add(ActionButton("‹ Önceki", () => { _libraryPage = Math.Max(0, _libraryPage - 1); Render(); }));
        _sidebar.Children.Add(ActionButton("Sonraki ›", () => { _libraryPage++; Render(); }));
        _sidebar.Children.Add(new TextBlock { Text = $"{_libraryPage + 1}. sayfa", Margin = new Thickness(8) });
        _sidebar.Children.Add(ActionButton("Kalıcı sil", () => Execute(DeletePermanently)));
    }

    private void BuildInspector()
    {
        foreach (var (label, editor) in new[] { ("Başlık", _title), ("İçerik", _content), ("Notlar", _notes), ("Etiketler (virgülle)", _tags) })
        {
            _inspector.Children.Add(new TextBlock { Text = label }); _inspector.Children.Add(editor);
            editor.TextChanged += (_, _) => { if (_settingDraft || _editingId is null) return; _draftDirty = true; _saveTimer.Stop(); _saveTimer.Start(); };
        }
        _inspector.Children.Add(ActionButton("Kaydet / yeniden dene", () => SaveDraft()));
        _inspector.Children.Add(ActionButton("Kırp", () => Execute(() => EditImage("crop"))));
        _inspector.Children.Add(ActionButton("İşaretle", () => Execute(() => EditImage("annotate"))));
        _inspector.Children.Add(ActionButton("Özgün görsele dön", () => Execute(() => { if (Selection().FirstOrDefault() is { Kind: ClipKind.Image } item) _library.RestoreOriginal(item.Id); })));
        _inspector.Children.Add(ActionButton("Bağlantıyı aç", () => Execute(OpenLink)));
    }

    private void FillInspector()
    {
        var item = Selection().FirstOrDefault();
        if (item?.Id == _editingId) return;
        if (!SaveDraft()) return;
        _settingDraft = true;
        try { _editingId = item?.Id; _title.Text = item?.Title ?? ""; _content.Text = item?.Content ?? ""; _content.IsReadOnly = item is null || item.Kind is ClipKind.Image or ClipKind.Recording; _notes.Text = item?.Notes ?? ""; _tags.Text = item is null ? "" : string.Join(", ", item.Tags); _inspector.IsEnabled = item is not null; }
        finally { _settingDraft = false; }
    }

    private bool SaveDraft()
    {
        _saveTimer.Stop(); if (!_draftDirty || _editingId is not { } id) return true;
        try { _draftDirty = false; _library.SaveDetails(id, _title.Text, _content.Text, _notes.Text, _tags.Text.Split(',')); _status.Text = "Kaydedildi."; return true; }
        catch (Exception error) { _draftDirty = true; _status.Text = "Kaydedilemedi; taslak korundu. " + error.Message; return false; }
    }

    private void OnKey(object sender, KeyEventArgs args)
    {
        if (Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox || args.IsRepeat) return;
        if (args.Key == Key.Escape) { args.Handled = true; ReturnFocus(); return; }
        var command = ClipboardKeymap.Match(args, _library.Preferences.LocalShortcuts);
        Action? action = command switch
        {
            "onizle" => Preview,
            "tur-tumu" when _libraryMode => () => _type.SelectedIndex = 0,
            "tur-gorsel" when _libraryMode => () => _type.SelectedIndex = 1,
            "tur-metin" when _libraryMode => () => _type.SelectedIndex = 2,
            "tur-baglanti" when _libraryMode => () => _type.SelectedIndex = 3,
            "raf-gorsel" when !_libraryMode => () => ToggleShelfVisibility(ClipKind.Image),
            "raf-metin" when !_libraryMode => () => ToggleShelfVisibility(ClipKind.Text),
            "raf-baglanti" when !_libraryMode => () => ToggleShelfVisibility(ClipKind.Link),
            "yapistir" => () => Copy(true),
            "disa-aktar" => Export,
            "etiket-ekle" => AddTags,
            "baglanti-ac" => OpenLink,
            "ocr-kopyala" => CopyRecognizedText,
            "kopyala" => () => { Copy(false); if (!_libraryMode) ReturnFocus(); },
            "sabitle" => () => _library.TogglePin(SelectedIds()),
            "klasore-sabitle" => () => AssignFolder(pin: true),
            "adlandir-sabitle" => NameAndPin,
            "kaydi-kirp" => () => { if (Selection().FirstOrDefault() is { Kind: ClipKind.Recording } item) EditRecording(item); },
            "raftan-kaldir" => () => _library.TakeDown(SelectedIds()),
            "geri-al" or "duzenlemeyi-geri-al" => _library.Undo,
            "kutuphane" => () => SetMode(true),
            "cope-tasi" => () => _library.Trash(SelectedIds()),
            "arama" or "ara" => () => _search.Focus(),
            "yinele" => _library.Redo,
            "tumunu-sec" => SelectAllResults,
            "kopyala-acik" => () => Copy(false),
            "eylemler" => () => _actionsOpenGeneration++,
            "sonraki-raf" or "asagi-raf" when !_libraryMode => () => SwitchRow(1),
            "onceki-raf" or "yukari-raf" when !_libraryMode => () => SwitchRow(-1),
            "onceki-sayfa" => () => ChangePage(-1),
            "sonraki-sayfa" => () => ChangePage(1),
            "ilk-oge" => () => SelectBoundary(false),
            "son-oge" => () => SelectBoundary(true),
            "sabit-filtre" => () => { _pinned.IsChecked = _pinned.IsChecked != true; ResetAndRender(); },
            _ => null
        };
        if (action is null && Keyboard.Modifiers == ModifierKeys.None && args.Key is >= Key.D1 and <= Key.D9)
        {
            var index = args.Key - Key.D1; action = () => { if (_activeRow is not null && index < _activeRow.Items.Count) _activeRow.SelectedIndex = index; };
        }
        if (action is null) return; args.Handled = true; Execute(action);
    }

    internal void ToggleShelfVisibility(ClipKind kind)
    {
        var preferences = _library.Preferences;
        preferences = kind switch
        {
            ClipKind.Image or ClipKind.Recording => preferences with { ShowImages = !preferences.ShowImages },
            ClipKind.Text => preferences with { ShowTexts = !preferences.ShowTexts },
            ClipKind.Link => preferences with { ShowLinks = !preferences.ShowLinks },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        _library.SetPreferences(preferences);
        _activeRow = _rows.FirstOrDefault(row => row.Visibility == Visibility.Visible && row.Items.Count > 0);
        if (!preferences.ShowImages && !preferences.ShowTexts && !preferences.ShowLinks) ReturnFocus();
        else Render();
    }

    private void ChangePage(int delta)
    {
        if (_libraryMode) { _libraryPage = Math.Max(0, _libraryPage + delta); Render(); return; }
        var row = Array.IndexOf(_rows, _activeRow); if (row >= 0) ChangeShelfPage(row, delta);
    }

    private void SelectAllResults()
    {
        var values = Results();
        if (!_libraryMode)
        {
            var row = Array.IndexOf(_rows, _activeRow);
            if (row < 0) return;
            values = values.Where(item => RowFor(item.Kind) == row).ToArray();
        }
        _batchSelection = values.Select(item => item.Id).ToArray();
        Render();
    }

    private void SelectBoundary(bool last)
    {
        if (_libraryMode) _libraryPage = last ? int.MaxValue : 0;
        else { var row = Array.IndexOf(_rows, _activeRow); if (row < 0) return; _pages[row] = last ? int.MaxValue : 0; }
        Render(); if (_activeRow is { Items.Count: > 0 } active) active.SelectedIndex = last ? active.Items.Count - 1 : 0;
    }

    private void SwitchRow(int direction)
    {
        var index = Array.IndexOf(_rows, _activeRow); var column = Math.Max(0, _activeRow?.SelectedIndex ?? 0);
        for (var offset = 1; offset <= _rows.Length; offset++) { var next = _rows[(index + direction * offset + _rows.Length * 2) % _rows.Length]; if (next.Visibility != Visibility.Visible || next.Items.Count == 0) continue; _activeRow = next; next.SelectedIndex = Math.Min(column, next.Items.Count - 1); next.Focus(); break; }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
}
