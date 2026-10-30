using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.FileTools.UI;

/// <summary>A native file shelf whose presentation is rendered by the shared React UI.</summary>
public sealed class FloatShelf : Window
{
    private readonly List<string> paths = [];
    private readonly HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<IReadOnlyList<string>, string, Task> action;
    private readonly Action<IReadOnlyList<string>> reuse;
    private readonly IFeatureUiService featureUi;
    private readonly IFeatureUiAssetSurface uiSurface;
    private readonly DispatcherTimer hover = new();
    private bool icons = true;
    private bool interacting;
    private bool hoverInside;
    private string? edge;
    private string? notice;

    internal bool IsExpanded { get; private set; }
    internal IReadOnlyList<string> Paths => paths;

    public FloatShelf(IEnumerable<string> initial, Func<IReadOnlyList<string>, string, Task> action,
        Action<IReadOnlyList<string>> reuse, IFeatureUiService? featureUi = null)
    {
        this.action = action;
        this.reuse = reuse;
        this.featureUi = featureUi ?? throw new InvalidOperationException("Dosya rafı için React arayüzü gerekli.");
        uiSurface = this.featureUi.CreateSurface("nashi-float-shelf") as IFeatureUiAssetSurface
            ?? throw new InvalidOperationException("Dosya rafı için React varlık yüzeyi gerekli.");

        Title = "Float · Nashi";
        Width = 112;
        Height = 136;
        MinWidth = 112;
        MinHeight = 136;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        AllowDrop = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = uiSurface.Element;

        hover.Tick += (_, _) =>
        {
            hover.Stop();
            if (interacting || System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                return;
            if (hoverInside && IsMouseOver) SetExpanded(true);
            else if (!hoverInside && !IsMouseOver) SetExpanded(false);
        };
        MouseEnter += (_, _) => ScheduleHover(true);
        MouseLeave += (_, _) => ScheduleHover(false);
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] incoming) Add(incoming);
            e.Handled = true;
        };
        PreviewKeyDown += OnKey;
        uiSurface.MessageReceived += OnUiMessage;
        Closed += (_, _) =>
        {
            hover.Stop();
            uiSurface.MessageReceived -= OnUiMessage;
            uiSurface.Dispose();
        };

        Add(initial);
        PushState();
    }

    internal void Add(IEnumerable<string> incoming)
    {
        foreach (var path in incoming.Where(path => File.Exists(path) || Directory.Exists(path)).Take(1000))
        {
            var fullPath = Path.GetFullPath(path);
            if (!paths.Contains(fullPath, StringComparer.OrdinalIgnoreCase)) paths.Add(fullPath);
        }
        PushState();
    }

    internal void SetExpanded(bool value, bool forceLayout = false)
    {
        if (IsExpanded == value && !forceLayout) return;
        hover.Stop();
        var oldWidth = ActualWidth > 0 ? ActualWidth : Width;
        var oldHeight = ActualHeight > 0 ? ActualHeight : Height;
        IsExpanded = value;
        var workArea = DesktopGeometry.WorkArea(this);
        var width = value ? (icons ? 248 : 356) : 112;
        var height = value ? Math.Min(workArea.Height - 16, 162 + Math.Max(1, Math.Min(paths.Count, 5)) * (icons ? 112 : 52)) : 136;
        var left = Math.Clamp(Left + (oldWidth - width) / 2, workArea.Left, workArea.Right - width);
        var top = Math.Clamp(Top, workArea.Top, workArea.Bottom - height);
        if (edge == "Right") left = workArea.Right - width;
        else if (edge == "Left") left = workArea.Left;
        if (edge == "Bottom") top = workArea.Bottom - height;
        else if (edge == "Top") top = workArea.Top;
        Width = width;
        Height = height;
        Left = left;
        Top = top;
        PushState();
    }

    private async void OnUiMessage(string command, JsonElement payload)
    {
        try
        {
            switch (command)
            {
                case "float.expand": SetExpanded(payload.GetProperty("value").GetBoolean()); break;
                case "float.close": Close(); break;
                case "float.select": UpdateSelection(payload); break;
                case "float.mode": icons = payload.GetProperty("icons").GetBoolean(); SetExpanded(true, true); break;
                case "float.hover": ScheduleHover(payload.GetProperty("inside").GetBoolean()); break;
                case "float.move": MoveShelf(); break;
                case "float.add": Choose(); break;
                case "float.clear": paths.Clear(); selected.Clear(); PushState(); break;
                case "float.remove": RemoveSelection(); break;
                case "float.edge": Attach(payload.GetProperty("edge").GetString()); break;
                case "float.reuse": reuse(Selection()); break;
                case "float.open": foreach (var path in Selection()) MainWindow.OpenUrl(path); break;
                case "float.preview": Preview(); break;
                case "float.reveal": foreach (var path in Selection()) MainWindow.Reveal(path); break;
                case "float.action": await action(Selection(), payload.GetProperty("id").GetString() ?? ""); break;
                case "float.copy-path": await action(Selection(), "copyPath"); break;
                case "float.drag-files": Carry(payload); break;
            }
        }
        catch (Exception error) when (ActionError(error))
        {
            notice = error.Message;
            PushState();
        }
    }

    private void UpdateSelection(JsonElement payload)
    {
        selected.Clear();
        foreach (var item in payload.GetProperty("paths").EnumerateArray())
        {
            var path = item.GetString();
            if (path is not null && paths.Contains(path, StringComparer.OrdinalIgnoreCase)) selected.Add(path);
        }
        PushState();
    }

    private void Attach(string? value)
    {
        edge = value is "Top" or "Right" or "Bottom" or "Left" ? value : null;
        DesktopGeometry.Snap(this, edge);
        PushState();
    }

    private void MoveShelf()
    {
        interacting = true;
        hover.Stop();
        try { DragMove(); edge = DesktopGeometry.Snap(this, null, true); }
        finally { interacting = false; ScheduleHover(false); PushState(); }
    }

    private void Carry(JsonElement payload)
    {
        var requested = payload.GetProperty("paths").EnumerateArray()
            .Select(item => item.GetString())
            .Where(path => path is not null && paths.Contains(path, StringComparer.OrdinalIgnoreCase))
            .Select(path => path!)
            .ToArray();
        if (requested.Length == 0) return;
        interacting = true;
        hover.Stop();
        try
        {
            var source = uiSurface.Element;
            System.Windows.DragDrop.DoDragDrop(source,
                new DataObject(DataFormats.FileDrop, requested),
                DragDropEffects.Copy | DragDropEffects.Move);
        }
        finally
        {
            interacting = false;
            PushState();
            ScheduleHover(false);
        }
    }

    private void ScheduleHover(bool inside)
    {
        hover.Stop();
        hoverInside = inside;
        hover.Interval = TimeSpan.FromMilliseconds(inside ? 200 : 650);
        hover.Start();
    }

    private IReadOnlyList<string> Selection() => selected.Count > 0
        ? paths.Where(selected.Contains).ToArray()
        : paths.Where(path => File.Exists(path) || Directory.Exists(path)).ToArray();

    private void RemoveSelection()
    {
        foreach (var path in Selection()) paths.Remove(path);
        selected.Clear();
        PushState();
    }

    private void Choose()
    {
        var picker = new OpenFileDialog { Multiselect = true };
        if (picker.ShowDialog(this) == true) Add(picker.FileNames);
    }

    private void Preview()
    {
        var selectedPaths = Selection();
        if (selectedPaths.Count > 0) new FilePreviewWindow(this, selectedPaths, featureUi: featureUi).Show();
    }

    private async void OnKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        try
        {
            var modifiers = System.Windows.Input.Keyboard.Modifiers;
            if (modifiers == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift) && e.Key == System.Windows.Input.Key.C)
            { await action(Selection(), "copyPath"); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Escape)
            { if (selected.Count > 0) selected.Clear(); else SetExpanded(false); PushState(); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Delete) { RemoveSelection(); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Space) { Preview(); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Enter) { foreach (var path in Selection()) MainWindow.OpenUrl(path); e.Handled = true; }
            else if (modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                if (e.Key == System.Windows.Input.Key.A) { selected.UnionWith(paths); SetExpanded(true); e.Handled = true; }
                else if (e.Key == System.Windows.Input.Key.C) { await action(Selection(), "copy"); e.Handled = true; }
                else if (e.Key == System.Windows.Input.Key.V && Clipboard.ContainsFileDropList()) { Add(Clipboard.GetFileDropList().Cast<string>()); e.Handled = true; }
                else if (e.Key == System.Windows.Input.Key.R) { foreach (var path in Selection()) MainWindow.Reveal(path); e.Handled = true; }
                else if (e.Key == System.Windows.Input.Key.O) { foreach (var path in Selection()) MainWindow.OpenUrl(path); e.Handled = true; }
            }
        }
        catch (Exception error) when (ActionError(error)) { notice = error.Message; PushState(); }
    }

    private void PushState()
    {
        var items = paths.Select(path => new
        {
            path,
            name = Path.GetFileName(path),
            folder = Directory.Exists(path),
            extension = Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),
            image = TryMapImage(path)
        }).ToArray();
        uiSurface.SetState(new
        {
            items,
            selected = selected.ToArray(),
            expanded = IsExpanded,
            icons,
            edge,
            notice,
            count = paths.Count
        });
    }

    private string? TryMapImage(string path)
    {
        if (!File.Exists(path) || !new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            return null;
        try { return uiSurface.MapLocalAsset(path).AbsoluteUri; }
        catch (IOException) { return null; }
    }

    private static bool ActionError(Exception error) => error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException;
}
