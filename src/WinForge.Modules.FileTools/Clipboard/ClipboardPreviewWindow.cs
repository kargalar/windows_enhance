using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.Json;
using WinForge.Core;
using WinForge.FileTools.UI;

using WinForge.FileTools;
namespace WinForge.ClipboardLine;

internal sealed class ClipboardPreviewWindow : Window
{
    private readonly ClipboardLibrary _library;
    private readonly ClipItem[] _items;
    private readonly ContentControl _preview = new();
    private readonly TextBlock _heading = new() { Margin = new Thickness(8), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Slider _zoom = new() { Minimum = 1, Maximum = 4, Value = 1, Width = 160, Margin = new Thickness(10), ToolTip = "Görsel yakınlaştırma (sığdırmanın %100–400’ü)" };
    private readonly Action<Guid, bool, bool> _copy;
    private readonly Action<ClipItem> _trim;
    private readonly Action<ClipItem, string> _editImage;
    private readonly IFeatureUiAssetSurface _uiSurface;
    private readonly Button _crop;
    private readonly Button _annotate;
    private readonly Button _editText;
    private readonly Button _openLink;
    private TextBox? _editor;
    private MediaElement? _player;
    private int _index;
    private readonly System.Windows.Threading.DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _dirty;
    private double _fitZoom = 1;
    internal double ZoomForTest { get => _zoom.Value; set => _zoom.Value = value; }
    internal double ImageScaleForTest => _preview.Content is ScrollViewer { Content: Image { LayoutTransform: ScaleTransform scale } } ? scale.ScaleX : 0;
    internal Guid CurrentItemIdForTest => _items[_index].Id;

    internal ClipboardPreviewWindow(ClipboardShelfWindow owner, ClipboardLibrary library, ClipItem[] items, Guid selected, Action<Guid, bool, bool> copy, Action<ClipItem> trim, Action<ClipItem, string> editImage, IFeatureUiService? featureUi=null)
    {
        _library = library; _items = items; _copy = copy; _trim = trim; _editImage = editImage; _index = Array.FindIndex(items, item => item.Id == selected);
        Owner = owner; Title = "Pano önizleme"; Width = 900; Height = 670; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "BgBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var root = new DockPanel { Margin = new Thickness(14) }; Content = root;
        var toolbar = new WrapPanel(); DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        toolbar.Children.Add(ClipboardShelfWindow.ActionButton("‹ Önceki", () => Navigate(-1))); toolbar.Children.Add(ClipboardShelfWindow.ActionButton("Sonraki ›", () => Navigate(1))); toolbar.Children.Add(_zoom);
        _crop = ClipboardShelfWindow.ActionButton("Kırp", () => EditImage("crop")); _annotate = ClipboardShelfWindow.ActionButton("İşaretle", () => EditImage("annotate")); toolbar.Children.Add(_crop); toolbar.Children.Add(_annotate);
        _editText = ClipboardShelfWindow.ActionButton("Düzenle", EditText); toolbar.Children.Add(_editText);
        _openLink = ClipboardShelfWindow.ActionButton("Bağlantıyı aç", OpenLink); toolbar.Children.Add(_openLink);
        toolbar.Children.Add(ClipboardShelfWindow.ActionButton("Kopyala", () => Copy(false)));
        toolbar.Children.Add(ClipboardShelfWindow.ActionButton("Yapıştır", Paste)); toolbar.Children.Add(ClipboardShelfWindow.ActionButton("Kapat / kaydet", Close));
        DockPanel.SetDock(_heading, Dock.Top); root.Children.Add(_heading); root.Children.Add(_preview);
        _zoom.ValueChanged += (_, _) => { if (_preview.Content is ScrollViewer { Content: Image image }) image.LayoutTransform = new ScaleTransform(_fitZoom * _zoom.Value, _fitZoom * _zoom.Value); };
        _save.Tick += (_, _) => Save();
        Closing += (_, args) => args.Cancel = !Save(); Closed += (_, _) => { _save.Stop(); _player?.Close(); };
        PreviewKeyDown += (_, args) =>
        {
            HandlePreviewKey(args);
        };
        _uiSurface = featureUi?.CreateSurface("clipboard-preview") as IFeatureUiAssetSurface
            ?? throw new InvalidOperationException("Pano önizlemesi için React varlık yüzeyi gerekli.");
        _uiSurface.MessageReceived += OnUiMessage;
        Content = _uiSurface.Element;
        Closed += (_, _) => _uiSurface.Dispose();
        ShowItem();
    }

    private void HandlePreviewKey(KeyEventArgs args)
    {
        if (Keyboard.FocusedElement is TextBoxBase && _editor is { IsReadOnly: false })
        {
            if (args.Key == Key.Escape) { FinishTextEdit(); args.Handled = true; }
            else if (args.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { Paste(); args.Handled = true; }
            return;
        }
        if (args.Key == Key.Escape || args.Key == Key.Space) Close();
        else if (ClipboardKeymap.Match(args, _library.Preferences.LocalShortcuts) is { } command && RunShortcut(command)) { }
        else if (args.Key == Key.Left) Navigate(-1);
        else if (args.Key == Key.Right) Navigate(1);
        else if (args.Key == Key.Up) Navigate(-1);
        else if (args.Key == Key.Down) Navigate(1);
        else return;
        args.Handled = true;
    }

    internal void HandlePreviewKeyForTest(Key key)
    {
        var source = PresentationSource.FromVisual(this) ?? throw new InvalidOperationException("Preview has no presentation source.");
        HandlePreviewKey(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    }

    private bool RunShortcut(string command)
    {
        var id = _items[_index].Id;
        switch (command)
        {
            case "kopyala": Copy(true); return true;
            case "kopyala-acik": Copy(false); return true;
            case "yapistir": Paste(); return true;
            case "baglanti-ac": OpenLink(); return true;
            case "sabitle": _library.TogglePin([id]); return true;
            case "raftan-kaldir": _library.TakeDown([id]); return true;
            case "cope-tasi": _library.Trash([id]); return true;
            case "geri-al": case "duzenlemeyi-geri-al": _library.Undo(); return true;
            case "yinele": _library.Redo(); return true;
            case "kaydi-kirp":
                if (_library.Items.FirstOrDefault(item => item.Id == id) is { Kind: ClipKind.Recording } recording)
                { _player?.Close(); _trim(recording); ShowItem(); }
                return true;
            case "ilk-oge": NavigateTo(0); return true;
            case "son-oge": NavigateTo(_items.Length - 1); return true;
            case "onceki-sayfa": Navigate(-Math.Min(9, _index)); return true;
            case "sonraki-sayfa": Navigate(Math.Min(9, _items.Length - 1 - _index)); return true;
            case "asagi-raf": case "sonraki-raf": Navigate(1); return true;
            case "yukari-raf": case "onceki-raf": Navigate(-1); return true;
            default: return false;
        }
    }

    private void Navigate(int delta) { if (!Save()) return; _player?.Close(); _index = Math.Clamp(_index + delta, 0, _items.Length - 1); ShowItem(); }
    private void NavigateTo(int index) { if (!Save()) return; _player?.Close(); _index = Math.Clamp(index, 0, _items.Length - 1); ShowItem(); }
    private void Copy(bool dismissShelf) { if (!Save()) return; _copy(_items[_index].Id, dismissShelf, false); if (!Owner.IsVisible) Close(); }
    private void Paste()
    {
        if (!Save()) return;
        var id = _items[_index].Id;
        Close();
        _copy(id, true, true);
    }
    private void EditText()
    {
        if (_editor is null) return;
        _editor.IsReadOnly = false; _editor.Focusable = true; _editor.Focus();
    }
    private void FinishTextEdit()
    {
        if (_editor is null || !Save()) return;
        _editor.IsReadOnly = true; _editor.Focusable = false; Focus();
    }
    private void OpenLink()
    {
        if (!Save() || _library.Items.FirstOrDefault(item => item.Id == _items[_index].Id) is not { Kind: ClipKind.Link } item || !ClipboardLibrary.IsStandaloneLink(item.Content)) return;
        try { Process.Start(new ProcessStartInfo(item.Content) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { _heading.Text = error.Message; }
    }
    private void ShowItem()
    {
        _editor = null; _player = null; var item = _library.Items.FirstOrDefault(item => item.Id == _items[_index].Id);
        if (item is null) { _preview.Content = new TextBlock { Text = "Bu kayıt kaldırıldı." }; PushUiState(); return; }
        _heading.Text = item.DisplayTitle; _zoom.Visibility = item.Kind == ClipKind.Image ? Visibility.Visible : Visibility.Collapsed;
        _crop.Visibility = _annotate.Visibility = item.Kind == ClipKind.Image ? Visibility.Visible : Visibility.Collapsed;
        _editText.Visibility = item.Kind is ClipKind.Text or ClipKind.Link ? Visibility.Visible : Visibility.Collapsed;
        _openLink.Visibility = item.Kind == ClipKind.Link ? Visibility.Visible : Visibility.Collapsed;
        if (item.Kind == ClipKind.Image) ShowImage(item);
        else if (item.Kind == ClipKind.Link) ShowLink(item);
        else if(item.Kind is ClipKind.Text or ClipKind.Link)
        {
            _editor = new TextBox { Text = item.Content, IsReadOnly = true, Focusable = false, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 16 };
            _editor.TextChanged += (_, _) => { _dirty = true; _save.Stop(); _save.Start(); }; _preview.Content = _editor;
        }
        else _preview.Content=new TextBlock{Text="Kayıt önizlemesi React oynatıcısında gösteriliyor."};
        PushUiState();
    }

    private void PushUiState(string? status=null)
    {
        var item=_library.Items.FirstOrDefault(value=>value.Id==_items[_index].Id);
        if(item is null){_uiSurface.SetState(new{file="Kayıt kaldırıldı",index=_index+1,count=_items.Length,kind="message",message="Bu kayıt artık bulunamıyor.",image="",mediaUrl="",text="",author="",zoom=_zoom.Value,editingText=false,canPrevious=_index>0,canNext=_index+1<_items.Length,canEditImage=false,canEditText=false,canOpenLink=false,canTrim=false});return;}
        string Map(string path)
        {
            try{return path.Length>0&&File.Exists(path)?_uiSurface.MapLocalAsset(path).AbsoluteUri:"";}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidOperationException){return "";}
        }
        var image=item.Kind==ClipKind.Image?Map(_library.MediaPath(item)):item.Kind==ClipKind.Link&&item.LinkThumbnail.Length>0?Map(_library.ThumbnailPath(item)):"";
        var media=item.Kind==ClipKind.Recording?Map(_library.MediaPath(item)):"";
        _uiSurface.SetState(new
        {
            file=item.DisplayTitle,index=_index+1,count=_items.Length,kind=item.Kind switch{ClipKind.Image=>"image",ClipKind.Link=>"link",ClipKind.Recording=>"recording",_=>"text"},
            message=status??"",image,mediaUrl=media,text=_editor?.Text??item.Content,author=item.LinkAuthor,zoom=_zoom.Value,editingText=_editor is{IsReadOnly:false},
            canPrevious=_index>0,canNext=_index+1<_items.Length,canEditImage=item.Kind==ClipKind.Image,canEditText=item.Kind is ClipKind.Text or ClipKind.Link,
            canOpenLink=item.Kind==ClipKind.Link&&ClipboardLibrary.IsStandaloneLink(item.Content),canTrim=item.Kind==ClipKind.Recording
        });
    }

    private void OnUiMessage(string command,JsonElement payload)
    {
        try
        {
            switch(command)
            {
                case "clipboard-preview.close": if(Save())Close();break;
                case "clipboard-preview.navigate": Navigate(payload.GetProperty("delta").GetInt32());break;
                case "clipboard-preview.copy": Copy(false);break;
                case "clipboard-preview.paste": Paste();break;
                case "clipboard-preview.zoom": _zoom.Value=Math.Clamp(payload.GetProperty("value").GetDouble(),_zoom.Minimum,_zoom.Maximum);PushUiState();break;
                case "clipboard-preview.text-change": if(_editor is not null){_editor.Text=payload.GetProperty("value").GetString()??"";PushUiState();}break;
                case "clipboard-preview.text-edit": if(_editor is not null){_editor.IsReadOnly=false;_editor.Focusable=true;}PushUiState();break;
                case "clipboard-preview.text-finish": if(_editor is not null&&Save()){_editor.IsReadOnly=true;_editor.Focusable=false;}PushUiState();break;
                case "clipboard-preview.edit-image": EditImage(payload.GetProperty("tool").GetString()??"crop");break;
                case "clipboard-preview.open-link": OpenLink();PushUiState();break;
                case "clipboard-preview.trim": TrimCurrent();break;
                case "clipboard-preview.key": HandleUiKey(payload);break;
            }
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException){PushUiState(error.Message);}
    }

    private void TrimCurrent()
    {
        if(_library.Items.FirstOrDefault(item=>item.Id==_items[_index].Id&&item.Kind==ClipKind.Recording) is not{} recording)return;
        _player?.Close();_trim(recording);ShowItem();
    }

    private void HandleUiKey(JsonElement payload)
    {
        var keyName=payload.GetProperty("key").GetString()??"";
        var key=ClipboardKeymap.ParseWebKey(keyName);
        if(key==Key.None)return;
        var modifiers=ModifierKeys.None;
        if(payload.TryGetProperty("ctrl",out var ctrl)&&ctrl.GetBoolean())modifiers|=ModifierKeys.Control;
        if(payload.TryGetProperty("shift",out var shift)&&shift.GetBoolean())modifiers|=ModifierKeys.Shift;
        if(payload.TryGetProperty("alt",out var alt)&&alt.GetBoolean())modifiers|=ModifierKeys.Alt;
        if(_editor is{IsReadOnly:false})
        {
            if(key==Key.Escape){FinishTextEdit();PushUiState();}
            else if(key==Key.Enter&&modifiers==ModifierKeys.Control)Paste();
            return;
        }
        if(key==Key.Escape||key==Key.Space){Close();return;}
        var command=ClipboardKeymap.Match(key,modifiers,_library.Preferences.LocalShortcuts);
        if(command is not null&&RunShortcut(command)){PushUiState();return;}
        if(key is Key.Left or Key.Up)Navigate(-1);
        else if(key is Key.Right or Key.Down)Navigate(1);
    }

    private void ShowLink(ClipItem item)
    {
        _heading.Text = item.LinkTitle.Length > 0 ? item.LinkTitle : item.DisplayTitle;
        var details = new StackPanel { Margin = new Thickness(12), MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Center };
        if (item.LinkThumbnail.Length > 0)
        {
            try
            {
                var source = ImageEngine.Load(_library.ThumbnailPath(item));
                var link = new Uri(item.Content);
                var image = new Image { Source = LinkThumbnail.ForPreview(source, link), Height = 360, MaxWidth = 620, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 12) };
                details.Children.Add(image);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or UriFormatException)
            {
                details.Children.Add(new TextBlock { Text = "Bağlantı görseli açılamadı.", ToolTip = error.Message, Margin = new Thickness(0, 0, 0, 12) });
            }
        }
        if (item.LinkAuthor.Length > 0) details.Children.Add(new TextBlock { Text = item.LinkAuthor, Opacity = .75, Margin = new Thickness(0, 0, 0, 8) });
        _editor = new TextBox { Text = item.Content, IsReadOnly = true, Focusable = false, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), FontSize = 14 };
        _editor.TextChanged += (_, _) => { _dirty = true; _save.Stop(); _save.Start(); };
        details.Children.Add(_editor);
        _preview.Content = new ScrollViewer { Content = details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void EditImage(string tool)
    {
        if (_library.Items.FirstOrDefault(item => item.Id == _items[_index].Id && item.Kind == ClipKind.Image && item.TrashedAt is null) is not { } item) return;
        _editImage(item, tool); ShowItem();
    }
    private void ShowImage(ClipItem item)
    {
        try
        {
            var image = new Image { Source = ImageEngine.Load(_library.MediaPath(item)), Stretch = Stretch.None };
            var scroll = new ScrollViewer { Content = image, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; _preview.Content = scroll;
            Point? start = null; double horizontal = 0, vertical = 0;
            _zoom.Value = 1;
            image.MouseLeftButtonDown += (_, args) => { if (args.ClickCount == 2) { _zoom.Value = 1; args.Handled = true; return; } start = args.GetPosition(scroll); horizontal = scroll.HorizontalOffset; vertical = scroll.VerticalOffset; image.CaptureMouse(); };
            image.MouseMove += (_, args) => { if (start is not { } origin || args.LeftButton != MouseButtonState.Pressed) return; var current = args.GetPosition(scroll); scroll.ScrollToHorizontalOffset(horizontal + origin.X - current.X); scroll.ScrollToVerticalOffset(vertical + origin.Y - current.Y); };
            image.MouseLeftButtonUp += (_, _) => { start = null; image.ReleaseMouseCapture(); };
            scroll.PreviewMouseWheel += (_, args) =>
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - args.Delta);
                else _zoom.Value = Math.Clamp(_zoom.Value * (args.Delta > 0 ? 1.15 : 1 / 1.15), _zoom.Minimum, _zoom.Maximum);
                args.Handled = true;
            };
            void Fit()
            {
                _fitZoom = FitScale(scroll.ActualWidth, scroll.ActualHeight, image.Source.Width, image.Source.Height);
                image.LayoutTransform = new ScaleTransform(_fitZoom * _zoom.Value, _fitZoom * _zoom.Value);
            }
            scroll.Loaded += (_, _) => Fit();
            scroll.SizeChanged += (_, _) => Fit();
        }
        catch (Exception error) { _preview.Content = new TextBlock { Text = error.Message }; }
    }
    internal static double FitScale(double viewportWidth, double viewportHeight, double imageWidth, double imageHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0 || imageWidth <= 0 || imageHeight <= 0) return 1;
        return Math.Min(1, Math.Min(viewportWidth / imageWidth, viewportHeight / imageHeight));
    }
    private bool Save()
    {
        _save.Stop(); if (!_dirty || _editor is null) return true;
        try { var item = _library.Items.Single(item => item.Id == _items[_index].Id); _library.SaveDetails(item.Id, item.Title, _editor.Text, item.Notes, item.Tags); _dirty = false; return true; }
        catch (Exception error) { _heading.Text = "Kaydedilemedi; taslak korundu: " + error.Message; return false; }
    }
}
