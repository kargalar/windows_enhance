using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WinForge.Core;
using WinForge.FileTools.UI;
using WinForge.Modules.TextGrabber;
using NativeClipboard = System.Windows.Clipboard;

using WinForge.FileTools;
namespace WinForge.ClipboardLine;

internal sealed partial class ClipboardShelfWindow
{
    private string? Prompt(string title, string value = "")
    {
        var dialog = new ClipboardPromptWindow(this, title, value, (_context as IFeatureUiContext)?.FeatureUi);
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    private void NewNote()
    {
        var text = Prompt("Yeni not"); if (string.IsNullOrWhiteSpace(text)) return;
        var item = _library.AddText(text, note: true); SetMode(true); _activeRow = _libraryList; SelectItem(item.Id);
    }
    private void SelectItem(Guid id)
    {
        if (_activeRow is null) return;
        foreach (ListBoxItem container in _activeRow.Items) if ((Guid)container.Tag == id) { _activeRow.SelectedItem = container; _activeRow.ScrollIntoView(container); break; }
    }
    private void Import()
    {
        var dialog = new OpenFileDialog { Title = "Pano kütüphanesine al", Multiselect = true, Filter = "Desteklenen dosyalar|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tiff;*.heic;*.avif;*.mp4;*.mov;*.txt;*.md|Tüm dosyalar|*.*" };
        if (dialog.ShowDialog(this) == true) foreach (var path in dialog.FileNames) _capture.ImportFile(path);
    }
    private void AssignFolder(bool pin = false)
    {
        var selected = SelectedIds(); if (selected.Length == 0) return;
        _folderPickerPin = pin;
        _folderPickerGeneration++;
    }
    private void NameAndPin()
    {
        foreach (var item in Selection())
        {
            var title = Prompt("Adlandır ve sabitle", item.Title); if (title is null) break;
            _library.Update([item.Id], current => current with { Title = title, Pinned = true, Saved = true });
        }
    }
    private void DeletePermanently()
    {
        var ids = Selection().Where(item => item.TrashedAt is not null).Select(item => item.Id).ToArray();
        if (ids.Length > 0)
        {
            _deleteConfirmGeneration++;
            PushReactState();
            return;
        }
    }

    private void ConfirmPermanentDelete(bool confirm)
    {
        if (!confirm) return;
        var ids = Selection().Where(item => item.TrashedAt is not null).Select(item => item.Id).ToArray();
        if (ids.Length > 0) _library.DeletePermanently(ids);
        ResetAndRender();
    }
    private void AddTags()
    {
        var ids = SelectedIds(); if (ids.Length == 0) return;
        var input = Prompt("Etiket ekle (virgülle ayırın)");
        if (string.IsNullOrWhiteSpace(input)) return;
        _library.AddTags(ids, input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    private void CopyRecognizedText()
    {
        var text = string.Join(Environment.NewLine, Selection().Where(item => item.Kind == ClipKind.Image && !string.IsNullOrWhiteSpace(item.OcrText)).Select(item => item.OcrText));
        if (text.Length == 0) { _status.Text = "Seçili görsellerde tanınan metin yok."; return; }
        var data = new DataObject(); data.SetData(ClipboardCapture.OwnCopyFormat, "1"); data.SetText(text);
        ClipboardAccess.SetData(data); _status.Text = "Taranan metin panoya kopyalandı.";
    }

    private void OpenLink()
    {
        if (Selection().FirstOrDefault() is { Kind: ClipKind.Link } item && ClipboardLibrary.IsStandaloneLink(item.Content)) Process.Start(new ProcessStartInfo(item.Content) { UseShellExecute = true });
    }

    private void RefreshLinkMetadata()
    {
        var links = Selection().Where(item => item.Kind == ClipKind.Link).ToArray();
        if (links.Length == 0) { _status.Text = "Önce bir bağlantı kaydı seçin."; return; }
        if (!_library.Preferences.FetchLinkMetadata) { _status.Text = "Bağlantı bilgisi alma Ayarlar'da kapalı."; return; }
        var queued = links.Count(item => _capture.RefreshLinkMetadata(item.Id));
        _status.Text = queued == 0 ? "Bağlantı bilgisi yenilenemedi." : $"{queued} bağlantı için bilgi yenileme başlatıldı.";
    }

    private DataObject CopyData(ClipItem[] items)
    {
        var data = new DataObject(); data.SetData(ClipboardCapture.OwnCopyFormat, "1");
        var text = items.Where(item => item.Kind is ClipKind.Text or ClipKind.Link).Select(item => item.Content).ToArray();
        if (text.Length > 0) data.SetText(string.Join(Environment.NewLine, text));
        var media = items.Where(item => item.Kind is ClipKind.Image or ClipKind.Recording).Select(item => _library.MediaPath(item)).ToArray();
        if (media.Length > 0) { var files = new StringCollection(); files.AddRange(media); data.SetFileDropList(files); }
        if (items.FirstOrDefault(item => item.Kind == ClipKind.Image) is { } image) data.SetImage(ImageEngine.Load(_library.MediaPath(image)));
        return data;
    }
    private void Copy(bool paste)
    {
        CopyItems(Selection(), paste);
    }
    private void CopyItems(ClipItem[] items, bool paste)
    {
        if (items.Length == 0) return;
        if (paste) { _ = PasteAsync(items); return; }
        ClipboardAccess.SetData(CopyData(items));
        _library.MarkCopied(items.Select(item => item.Id));
        _status.Text = $"{items.Length} kayıt kopyalandı.";
        if (_library.Preferences.Sounds) System.Media.SystemSounds.Asterisk.Play();
    }
    private void ReturnFocus()
    {
        Hide(); if (_previousWindow != IntPtr.Zero && IsWindow(_previousWindow)) SetForegroundWindow(_previousWindow);
    }
    private async Task PasteAsync(ClipItem[] items)
    {
        var target = _pasteTarget;
        if (target is null || target.Window != _previousWindow || !IsWindow(target.Window))
        { _status.Text = "Önceki düzenlenebilir alan bulunamadı; yapıştırma iptal edildi."; return; }
        try
        {
            var data = CopyData(items);
            ReturnFocus(); await Task.Delay(150);
            for (var attempt = 0; attempt < 100 && ActivationKeys.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0); attempt++) await Task.Delay(20);
            if (ActivationKeys.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0) || !await _capture.PasteTargets.IsCurrentAsync(target) || GetForegroundWindow() != target.Window)
            { _context.Notify("Önceki düzenlenebilir alan odakta değil; yapıştırma iptal edildi."); return; }
            var copied = await Dispatcher.InvokeAsync(() =>
            {
                if (GetForegroundWindow() != target.Window) return false;
                ClipboardAccess.SetData(data); _library.MarkCopied(items.Select(item => item.Id)); return true;
            });
            if (!copied) { _context.Notify("Hedef pencere değişti; yapıştırma iptal edildi."); return; }
            if (!await _capture.PasteTargets.IsCurrentAsync(target)) { _context.Notify("Hedef alan değişti; yapıştırma iptal edildi."); return; }
            await Dispatcher.InvokeAsync(() =>
            {
                if (GetForegroundWindow() != target.Window) { _context.Notify("Hedef pencere değişti; yapıştırma iptal edildi."); return; }
                var inputs = new[] { KeyboardInput.Key(0x11), KeyboardInput.Key(0x56), KeyboardInput.Key(0x56, up: true), KeyboardInput.Key(0x11, up: true) };
                if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<KeyboardInput>()) != inputs.Length) _context.Notify("Windows yapıştırma komutunu engelledi; içerik panoda.");
            });
        }
        catch (Exception error) { _context.Log.Error("Pano yapıştırma tamamlanamadı.", error); _context.Notify(error.Message); }
    }

    private void DragSelection()
    {
        var items = Selection(); if (items.Length == 0) return;
        var data = CopyData(items);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            var directory = Path.Combine(_library.DirectoryPath, "drag", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            var files = ClipboardFileExport.Export(_library, items, directory); var paths = new StringCollection(); paths.AddRange(files); data.SetFileDropList(paths);
        }
        data.SetData(ClipboardDragSession.Format, _dragSession.Begin(items.Select(item => item.Id)));
        _dragInProgress = true;
        try { DragDrop.DoDragDrop(_activeRow!, data, DragDropEffects.Copy); }
        finally
        {
            _dragSession.End(); _dragInProgress = false;
            if (_refreshPending) { _refreshPending = false; RefreshAfterLibraryChange(); }
        }
    }

    private IReadOnlyList<Guid> InternalDragIds(IDataObject data)
    {
        if (!_dragInProgress || !data.GetDataPresent(ClipboardDragSession.Format, autoConvert: false)) return [];
        var available = _library.Items.Where(item => item.TrashedAt is null).Select(item => item.Id).ToHashSet();
        return _dragSession.Resolve(data.GetData(ClipboardDragSession.Format, autoConvert: false)).Where(available.Contains).ToArray();
    }
    private void Export()
    {
        var items = Selection(); if (items.Length == 0) return;
        var dialog = new OpenFolderDialog { Title = "Pano kayıtlarını dışa aktar" }; if (dialog.ShowDialog(this) != true) return;
        var files = ClipboardFileExport.Export(_library, items, dialog.FolderName); _status.Text = $"{files.Length} kayıt dışa aktarıldı.";
    }

    private void EditImage(string tool)
    {
        if (Selection().FirstOrDefault() is not { Kind: ClipKind.Image } item) return;
        EditImage(item, tool);
    }
    private void EditImage(ClipItem item, string tool)
    {
        var path = tool == "annotate" ? _library.AnnotationBasePath(item) : _library.MediaPath(item); var bitmap = ImageEngine.Load(path);
        var editor = new ToolEditor(this, tool, tool == "crop" ? "Pano görselini kırp" : "Pano görselini işaretle", path, preview: bitmap, featureUi: (_context as IFeatureUiContext)?.FeatureUi);
        if (tool == "annotate" && item.Annotations.Length > 0) editor.LoadAnnotations(item.Annotations);
        if (editor.ShowDialog() != true) return;
        var destination = Path.Combine(_library.DirectoryPath, "edits", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(destination);
        try
        {
            var output = ImageEngine.Apply(tool, [path], destination, editor.ImageOptions, "png").Single();
            if (tool == "annotate") _library.SaveAnnotation(item.Id, item.Content, output, editor.SaveAnnotations());
            else if (editor.ImageOptions.Crop is { } crop)
            {
                string? source = null;
                if (item.AnnotationBase.Length > 0)
                    source = ImageEngine.Apply("crop", [_library.AnnotationBasePath(item)], destination, editor.ImageOptions, "png").Single();
                _library.SaveCrop(item.Id, item.Content, output, source, crop.X, crop.Y);
            }
        }
        finally { if (Directory.Exists(destination)) foreach (var file in Directory.GetFiles(destination)) File.Delete(file); }
    }

    private void Screenshot()
    {
        Hide(); var overlay = new SelectionOverlay((_context as IFeatureUiContext)?.FeatureUi) { Owner = this }; overlay.ShowDialog();
        if (overlay.Result is not { } area) { Show(); return; }
        DwmFlush();
        var (pixels, width, height) = ScreenCapture.Capture((int)area.X, (int)area.Y, (int)area.Width, (int)area.Height);
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4); image.Freeze();
        var item = _capture.ImportBitmap(image, "Ekran görüntüsü"); PresentCapturedImage(item);
    }

    internal void PresentCapturedImage(ClipItem item)
    {
        Show();
        _activeRow = _rows[0]; Render(); SelectItem(item.Id);
        ShowPreview([item], item);
    }

    private void EditRecording(ClipItem item)
    {
        var jobs = Path.Combine(_library.DirectoryPath, "recording-jobs");
        Directory.CreateDirectory(jobs);
        var directory = Path.Combine(jobs, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var dialog = new RecordingTrimWindow(this, _library.MediaPath(item), directory, (_context as IFeatureUiContext)?.FeatureUi);
            if (dialog.ShowDialog() == true) _library.SaveRecordingEdit(item.Id, item.Content, Path.Combine(directory, "trimmed.mp4"));
        }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(directory)) == Path.GetFullPath(jobs) && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private void Preview()
    {
        var values = Results().ToArray(); var item = Selection().FirstOrDefault(); if (item is null) return;
        ShowPreview(values, item);
    }

    private void ShowPreview(ClipItem[] values, ClipItem item)
    {
        new ClipboardPreviewWindow(this, _library, values, item.Id, (id, dismissShelf, paste) =>
        {
            var current = _library.Items.FirstOrDefault(candidate => candidate.Id == id && candidate.TrashedAt is null);
            if (current is null) return;
            CopyItems([current], paste); if (!paste && dismissShelf && !_libraryMode) ReturnFocus();
        }, EditRecording, EditImage, (_context as IFeatureUiContext)?.FeatureUi).Show();
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct KeyboardInput
    {
        [FieldOffset(0)] private uint _type;
        [FieldOffset(8)] private ushort _key;
        [FieldOffset(12)] private uint _flags;
        internal static KeyboardInput Key(ushort key, bool up = false) => new() { _type = 1, _key = key, _flags = up ? 2u : 0u };
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, KeyboardInput[] inputs, int size);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    private static readonly int[] ActivationKeys = [0x10, 0x11, 0x12, 0x5B, 0x5C, 0x56];
}
