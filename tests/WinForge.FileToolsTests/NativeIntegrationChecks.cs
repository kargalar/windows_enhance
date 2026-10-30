using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using WinForge.ClipboardLine;
using WinForge.Core;
using WinForge.FileTools.Engine;
using WinForge.FileTools.UI;
using WinForge.FileTools;
using NativeClipboard = System.Windows.Clipboard;

internal static class NativeIntegrationChecks
{
    internal static void Run(Action<bool, string> check, bool testUia)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { RunOnSta(check, testUia); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Native feature integration failed.", failure);
    }

    private static void RunOnSta(Action<bool, string> check, bool testUia)
    {
        var root = Path.Combine(Path.GetTempPath(), "WinForge-NativeFeatureChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/WinForge.FileToolsTests;component/TestTheme.xaml", UriKind.Relative) });
        var previous = NativeClipboard.GetDataObject(); uint lastWritten = 0;
        try
        {
            var context = new TestContext();
            if (testUia) PasteTargetChecks.Run(check, PumpUntil, root, context);
            var library = new ClipboardLibrary(Path.Combine(root, "clipboard"));
            library.SetPreferences(library.Preferences with { CaptureText = true, IndexImages = false });
            using var capture = new ClipboardCapture(library, context);
            var token = "WinForge native test " + Guid.NewGuid().ToString("N");
            var initialData = new DataObject(); initialData.SetText(token); ClipboardAccess.SetData(initialData); lastWritten = GetClipboardSequenceNumber();
            PumpUntil(() => library.Items.Any(item => item.Content == token));
            check(library.Items.Count(item => item.Content == token) == 1, "Actual Windows clipboard update is captured once.");
            var whitespace = new DataObject(); whitespace.SetText(" \t"); ClipboardAccess.SetData(whitespace); lastWritten = GetClipboardSequenceNumber();
            PumpUntil(() => library.Items.Any(item => item.Content == " \t"));
            check(library.Items.Any(item => item.Content == " \t"), "Whitespace-only clipboard text is captured without normalization loss.");
            var privateData = new DataObject(); privateData.SetText("private marker fixture"); privateData.SetData("ExcludeClipboardContentFromMonitorProcessing", new byte[] { 1 });
            ClipboardAccess.SetData(privateData); lastWritten = GetClipboardSequenceNumber(); Pump(TimeSpan.FromMilliseconds(500));
            check(!library.Items.Any(item => item.Content == "private marker fixture"), "Actual private-marked Windows clipboard data is not captured.");
            var own = new DataObject(); own.SetText("own marker fixture"); own.SetData(ClipboardCapture.OwnCopyFormat, "1");
            ClipboardAccess.SetData(own); lastWritten = GetClipboardSequenceNumber(); Pump(TimeSpan.FromMilliseconds(500));
            check(!library.Items.Any(item => item.Content == "own marker fixture"), "The module's own copies do not create duplicate history.");

            var watched = Path.Combine(root, "watched"); Directory.CreateDirectory(watched);
            var watchSource = Path.Combine(root, "watch-source.png"); ClipboardLibraryChecks.CreateShape(watchSource, true);
            library.SetPreferences(library.Preferences with { WatchFolders = [watched] }); capture.RefreshWatchers();
            File.Copy(watchSource, Path.Combine(watched, "watch-image.png"));
            PumpUntil(() => library.Items.Any(item => item.Title == "watch-image"));
            check(library.Items.Any(item => item.Kind == ClipKind.Image && item.Title == "watch-image"), "The real folder watcher imports a completed image.");
            library.SetPreferences(library.Preferences with { WatchFolders = [] }); capture.RefreshWatchers();

            var shelf = new ClipboardShelfWindow(library, capture, context); shelf.Show(); Pump(TimeSpan.FromMilliseconds(200));
            check(shelf.IsVisible && FindText(shelf, token), "The native shelf displays a captured text record.");
            var workspace = shelf.WorkspaceForTest!;
            check(workspace.ColumnDefinitions.Count == 5 && workspace.Children.OfType<GridSplitter>().Count() == 2, "The native library exposes resizable sidebar and inspector panels.");
            workspace.ColumnDefinitions[0].Width = new GridLength(205); workspace.ColumnDefinitions[4].Width = new GridLength(320); Pump(TimeSpan.FromMilliseconds(100));
            shelf.PersistWorkspaceWidthsForTest();
            var persistedLayout = new ClipboardLibrary(library.DirectoryPath).Preferences;
            check(Math.Abs(persistedLayout.SidebarWidth - workspace.ColumnDefinitions[0].ActualWidth) < 1 && Math.Abs(persistedLayout.InspectorWidth - workspace.ColumnDefinitions[4].ActualWidth) < 1, "Resized library panel widths persist and reload.");
            var beforeRejectedPaste = GetClipboardSequenceNumber();
            FindButton(shelf, "Yapıştır").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(GetClipboardSequenceNumber() == beforeRejectedPaste, "Paste without a verified target leaves the clipboard untouched.");
            shelf.ToggleShelfVisibility(ClipKind.Image); shelf.ToggleShelfVisibility(ClipKind.Text);
            check(!library.Preferences.ShowImages && !library.Preferences.ShowTexts && shelf.IsVisible, "Individual shelves can be hidden while another remains open.");
            shelf.ToggleShelfVisibility(ClipKind.Link);
            check(!shelf.IsVisible && !library.Preferences.ShowLinks, "Hiding the last shelf closes the shelf window.");
            library.SetPreferences(library.Preferences with { ShowImages = true, ShowTexts = true, ShowLinks = true }); shelf.Show();
            library.SetPreferences(library.Preferences with { CardSize = "small" });
            for (var index = 0; index < 12; index++) library.AddText("Shelf sizing fixture " + index);
            shelf.Width = 1600; Pump(TimeSpan.FromMilliseconds(250));
            var wideCount = shelf.ShelfItemCountForTest(1); var wideWidth = shelf.ShelfWidthForTest(1); var actualWide = shelf.ActualWidth;
            shelf.Width = 850; Pump(TimeSpan.FromMilliseconds(250));
            var narrowCount = shelf.ShelfItemCountForTest(1);
            check(narrowCount >= 1 && narrowCount < wideCount && wideCount <= 9, $"Resizing the actual shelf recalculates the number of visible cards (wide={wideCount}/{wideWidth}/{actualWide}, narrow={narrowCount}/{shelf.ShelfWidthForTest(1)}/{shelf.ActualWidth}).");
            var recording = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Recordings", "synthetic-rgb-silent.mp4");
            library.AddMedia(recording);
            for (var attempt = 0; attempt < 30 && !shelf.ShelfTextsForTest(0).Contains("00:06"); attempt++) Pump(TimeSpan.FromMilliseconds(100));
            check(shelf.ShelfTextsForTest(0).Contains("00:06"), $"A real recording card displays its decoded duration (row={shelf.ShelfItemCountForTest(0)}, text={string.Join("/", shelf.ShelfTextsForTest(0))}, errors={string.Join("; ", context.Errors)}).");
            var previewItem = library.AddText("preview fixture");
            var copiedId = Guid.Empty; var dismiss = true; var pasted = false;
            var preview = new ClipboardPreviewWindow(shelf, library, [previewItem], previewItem.Id,
                (id, close, paste) => { copiedId = id; dismiss = close; pasted = paste; }, _ => { }, (_, _) => { });
            preview.Show(); Pump(TimeSpan.FromMilliseconds(150));
            var previewEditor = Children(preview).OfType<TextBox>().Single();
            check(previewEditor.IsReadOnly, "Text preview starts in browsing mode.");
            FindButton(preview, "Düzenle").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!previewEditor.IsReadOnly, "The edit action enables text editing.");
            previewEditor.Text = "preview fixture edited";
            FindButton(preview, "Kopyala").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(copiedId == previewItem.Id && !dismiss && !pasted && preview.IsVisible && library.Items.Single(item => item.Id == previewItem.Id).Content == "preview fixture edited", "Preview copy saves the edit and keeps browsing open.");
            previewEditor.Text = "preview paste fixture";
            FindButton(preview, "Yapıştır").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(pasted && dismiss && !preview.IsVisible && library.Items.Single(item => item.Id == previewItem.Id).Content == "preview paste fixture", "Preview paste saves the draft and closes the preview before dispatch.");
            var previewNext = library.AddText("preview navigation fixture");
            library.SetPreferences(library.Preferences with { LocalShortcuts = ClipboardKeymap.ParseOverrides("sonraki-raf=F6").ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) });
            var navigationPreview = new ClipboardPreviewWindow(shelf, library, [previewItem, previewNext], previewItem.Id, (_, _, _) => { }, _ => { }, (_, _) => { });
            navigationPreview.Show(); Pump(TimeSpan.FromMilliseconds(100)); navigationPreview.HandlePreviewKeyForTest(Key.F6);
            check(navigationPreview.CurrentItemIdForTest == previewNext.Id, "A remapped shortcut advances through preview results.");
            navigationPreview.Close();
            var previewLink = library.AddText("https://www.youtube.com/watch?v=example123");
            library.SetLinkMetadata(previewLink.Id, previewLink.Content, new LinkDetails("Link preview fixture", "Example author", null), null);
            var linkPreview = new ClipboardPreviewWindow(shelf, library, [previewLink], previewLink.Id, (_, _, _) => { }, _ => { }, (_, _) => { });
            linkPreview.Show(); Pump(TimeSpan.FromMilliseconds(100));
            check(FindText(linkPreview, "Link preview fixture") && FindText(linkPreview, "Example author") && Children(linkPreview).OfType<TextBox>().Any(box => box.Text == previewLink.Content), "A link preview shows its saved title, author and address in the native preview.");
            linkPreview.Close();
            var paddedThumbnail = YouTubeThumbnail(withBlackPadding: true);
            var croppedThumbnail = LinkThumbnail.ForPreview(paddedThumbnail, new Uri("https://www.youtube.com/watch?v=example123"));
            check(croppedThumbnail.PixelWidth == 480 && croppedThumbnail.PixelHeight == 270, "Verified black YouTube letterboxing crops to the 16:9 image.");
            var realFourThree = YouTubeThumbnail(withBlackPadding: false);
            check(ReferenceEquals(LinkThumbnail.ForPreview(realFourThree, new Uri("https://youtu.be/example123")), realFourThree), "A genuine 4:3 YouTube thumbnail remains uncropped.");
            check(ReferenceEquals(LinkThumbnail.ForPreview(paddedThumbnail, new Uri("https://youtube.com.example.test/watch?v=example123")), paddedThumbnail), "A lookalike YouTube hostname cannot enable thumbnail cropping.");
            library.SetPreferences(library.Preferences with { LocalShortcuts = ClipboardKeymap.ParseOverrides("").ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) });
            var captureSource = Path.Combine(root, "captured-preview.png"); ClipboardLibraryChecks.CreateShape(captureSource, false);
            var captured = library.AddMedia(captureSource, "Capture preview fixture");
            shelf.PresentCapturedImage(captured); Pump(TimeSpan.FromMilliseconds(150));
            var capturedPreview = app.Windows.OfType<ClipboardPreviewWindow>().Single();
            check(capturedPreview.IsVisible && FindText(capturedPreview, "Capture preview fixture"), "A captured image opens directly in a native preview.");
            var fit = capturedPreview.ImageScaleForTest;
            capturedPreview.ZoomForTest = 4;
            check(fit > 0 && Math.Abs(capturedPreview.ImageScaleForTest - fit * 4) < .0001, "Image preview zoom reaches 400 percent of its fitted size.");
            capturedPreview.ZoomForTest = 0;
            check(capturedPreview.ZoomForTest == 1 && Math.Abs(capturedPreview.ImageScaleForTest - fit) < .0001, "Image preview cannot zoom below its fitted size.");
            check(Math.Abs(ClipboardPreviewWindow.FitScale(500, 300, 10000, 2000) - .05) < .00001, "Large images fit even when the required scale is below ten percent.");
            capturedPreview.Close();
            var libraryButton = FindButton(shelf, "Kütüphane"); libraryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(TimeSpan.FromMilliseconds(150));
            check(FindText(shelf, token), "The same record appears in the native library.");
            FindButton(shelf, "Kaydedilenler").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(TimeSpan.FromMilliseconds(100));
            check(!FindText(shelf, token), "Saved scope excludes an unsaved clipboard record in the actual library.");
            FindButton(shelf, "Son kopyalananlar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(TimeSpan.FromMilliseconds(100));
            check(FindText(shelf, token), "Recent scope shows the unsaved clipboard record.");
            var tagged = library.Items.Single(item => item.Content == "preview paste fixture");
            library.AddTags([tagged.Id], ["alpha", "beta"]);
            FindButton(shelf, "Kaydedilenler").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var tagFilter = Children(shelf).OfType<TextBox>().Single(box => box.ToolTip is string value && value.StartsWith("Etiketler (", StringComparison.Ordinal));
            tagFilter.Text = "alpha, beta"; Pump(TimeSpan.FromMilliseconds(100));
            var filteredIds = shelf.LibraryItemIdsForTest.ToHashSet();
            check(filteredIds.Contains(tagged.Id) && !filteredIds.Contains(captured.Id), "Actual library tag filter requires all requested tags.");
            tagFilter.Text = ""; FindButton(shelf, "Son kopyalananlar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            library.SetPreferences(library.Preferences with { ListView = true }); Pump(TimeSpan.FromMilliseconds(150));
            check(FindText(shelf, "Başlık") && FindText(shelf, "Oluşturulma"), "Library list mode displays its column headings.");
            check(Children(shelf).OfType<Grid>().Any(grid => grid.ColumnDefinitions.Count == 4 && grid.Children.OfType<TextBlock>().Any(text => text.Text == "Metin")), "Library list rows show the actual item kind in a separate column.");
            var titleHeader = shelf.LibraryHeaderForTest.Children.OfType<TextBlock>().Single(block => Grid.GetColumn(block) == 0);
            titleHeader.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            check(shelf.SortIndexForTest == 2, "Clicking the library title header sorts actual results alphabetically.");
            titleHeader.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            check(shelf.SortIndexForTest == 3, "Clicking the title header again reverses the sort order.");
            check(shelf.LibraryColumnSplitterCountForTest == 3, "The actual list header exposes draggable column splitters.");
            foreach (var (index, width) in new[] { (0, 240d), (2, 100d), (4, 140d), (6, 140d) })
                shelf.LibraryHeaderForTest.ColumnDefinitions[index].Width = new GridLength(width);
            Pump(TimeSpan.FromMilliseconds(100)); shelf.PersistListColumnWidthsForTest();
            var columnWidths = library.Preferences.ListColumnWidths;
            check(Math.Abs(columnWidths.Sum() - 100) < .01 && !columnWidths.SequenceEqual([46d, 18d, 18d, 18d]), "Resized list column widths persist as normalized proportions.");
            SaveRender(shelf, Path.Combine(root, "clipboard-window.png"));
            shelf.Close();

            var fileRoot = Path.Combine(root, "files"); Directory.CreateDirectory(fileRoot);
            var source = Path.Combine(fileRoot, "input.png"); ClipboardLibraryChecks.CreateShape(source, true);
            var files = new LibraryStore(Path.Combine(root, "file-library"));
            var window = new MainWindow(library: files, checkForUpdates: false, offerToolSetup: false); window.Show();
            window.AcceptLaunch([source]); Pump(TimeSpan.FromMilliseconds(200));
            check(window.SelectedFiles.SequenceEqual([source]), "The integrated Nashi WPF panel accepts a real image.");
            var output = ImageEngine.Apply("crop", [source], fileRoot, new ImageOptions(Crop: new Int32Rect(30, 40, 100, 90)), "png").Single();
            var bitmap = ImageEngine.Load(output);
            check(bitmap.PixelWidth == 100 && bitmap.PixelHeight == 90 && File.Exists(source), "The in-process file engine exports a real crop and retains the source.");
            CheckAnnotations(window, source, root, check, testUia);
            SaveRender(window, Path.Combine(root, "nashi-window.png")); window.Close();
            var module = new NashiModule(() => new MainWindow(library: files, offerToolSetup: false));
            module.Initialize(context); module.Activate(); Pump(TimeSpan.FromMilliseconds(200));
            var hosted = app.Windows.OfType<MainWindow>().Single();
            check(module.IsActive && !hosted.IsVisible && hosted.DesktopIntegrationActive, "Nashi's desktop drag integration starts while its panel is hidden.");
            module.Settings.Single(setting => setting.Key == "open").Execute!(); Pump(TimeSpan.FromMilliseconds(100));
            check(hosted.IsVisible, "Module action shows the existing native file-tools panel.");
            hosted.Close(); Pump(TimeSpan.FromMilliseconds(100));
            check(!hosted.IsVisible && hosted.DesktopIntegrationActive, "Closing the panel keeps enabled desktop integration running.");
            var secondShelf = new ClipboardShelfWindow(library, capture, context); secondShelf.Show();
            var clipboardEditor = new ToolEditor(secondShelf, "annotate", "Clipboard editor", source, preview: ImageEngine.Load(source)); clipboardEditor.Show();
            module.Deactivate();
            check(!module.IsActive && !app.Windows.Cast<Window>().Contains(hosted), "Deactivation closes the hosted panel and releases integration.");
            check(clipboardEditor.IsVisible, "Disabling file tools does not close an editor owned by clipboard history.");
            try { ((IManagedFeatureWindow)secondShelf).CloseForModule(); check(false, "An open clipboard editor prevents module shutdown."); }
            catch (InvalidOperationException) { check(secondShelf.IsVisible && clipboardEditor.IsVisible, "An open clipboard editor prevents module shutdown without losing its work."); }
            clipboardEditor.Close(); secondShelf.Close();
            var clipboardModule = new ClipboardLineModule(featureContext =>
            {
                var featureLibrary = new ClipboardLibrary(Path.Combine(root, "hosted-clipboard"));
                return (featureLibrary, new ClipboardCapture(featureLibrary, featureContext));
            });
            clipboardModule.Initialize(context); clipboardModule.Activate();
            clipboardModule.Settings.Single(setting => setting.Key == "open").Execute!(); Pump(TimeSpan.FromMilliseconds(150));
            var hostedClipboard = app.Windows.OfType<ClipboardShelfWindow>().Single();
            check(clipboardModule.IsActive && hostedClipboard.IsVisible, "The ClipboardLine productivity module opens its native shelf from WinForge settings.");
            clipboardModule.Deactivate(); Pump(TimeSpan.FromMilliseconds(100));
            check(!clipboardModule.IsActive && !app.Windows.Cast<Window>().Contains(hostedClipboard), "Disabling ClipboardLine closes its shelf and releases clipboard monitoring.");
            check(context.Errors.Count == 0, "Native clipboard capture did not log an error.");
            if (previous is not null && lastWritten != 0 && GetClipboardSequenceNumber() == lastWritten)
            {
                check(RestoreClipboard(previous, lastWritten), "The previous clipboard is restored when no newer user copy replaced the fixture.");
                lastWritten = 0;
            }
        }
        finally
        {
            if (lastWritten != 0 && previous is not null && !RestoreClipboard(previous, lastWritten)) Console.Error.WriteLine("The original clipboard could not be restored or was changed by another application.");
            foreach (Window window in app.Windows.Cast<Window>().ToArray()) window.Close();
            app.Shutdown();
            var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            var resolved = Path.GetFullPath(root);
            if (Path.GetDirectoryName(resolved) == parent && Path.GetFileName(resolved).StartsWith("WinForge-NativeFeatureChecks-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
        }
    }

    private static bool RestoreClipboard(IDataObject original, uint expectedSequence)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (GetClipboardSequenceNumber() != expectedSequence) return true;
            try { NativeClipboard.SetDataObject(original, true); return true; }
            catch (ExternalException) { Thread.Sleep(50); }
        }
        return false;
    }

    private static void PumpUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 30 && !condition(); attempt++) Pump(TimeSpan.FromMilliseconds(100));
        if (!condition()) throw new TimeoutException("Native clipboard update did not arrive.");
    }
    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }

    private static BitmapSource YouTubeThumbnail(bool withBlackPadding)
    {
        const int width = 480, height = 360;
        var pixels = new byte[width * height * 4];
        for (var offset = 3; offset < pixels.Length; offset += 4) pixels[offset] = 255;
        var contentTop = withBlackPadding ? 45 : 0;
        var contentBottom = withBlackPadding ? 315 : height;
        for (var y = contentTop; y < contentBottom; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = (y * width + x) * 4;
            pixels[offset] = 80; pixels[offset + 1] = 120; pixels[offset + 2] = 200; pixels[offset + 3] = 255;
        }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze(); return image;
    }
    private static void RaisePreviewKey(Window window, Key key)
    {
        var source = PresentationSource.FromVisual(window) ?? throw new InvalidOperationException("Preview has no presentation source.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        window.RaiseEvent(args);
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Children(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static bool FindText(Window window, string text) => Children(window).OfType<TextBlock>().Any(block => block.Text.Contains(text, StringComparison.Ordinal));
    internal static Button FindButton(Window window, string content) => Children(window).OfType<Button>().Single(button => Equals(button.Content, content));
    private static void SaveRender(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
        if (Environment.GetEnvironmentVariable("WINFORGE_FEATURE_ARTIFACTS") is { Length: > 0 } artifacts)
        {
            Directory.CreateDirectory(artifacts);
            File.Copy(path, Path.Combine(artifacts, Path.GetFileName(path)), true);
        }
    }

    private static void CheckAnnotations(MainWindow owner, string source, string root, Action<bool, string> check, bool testInput)
    {
        check(ToolEditor.AnnotationToolForKey(System.Windows.Input.Key.V) == 11 && ToolEditor.AnnotationToolForKey(System.Windows.Input.Key.B) == 10 && ToolEditor.AnnotationToolForKey(System.Windows.Input.Key.T) == 5, "Markup shortcuts select the intended tools.");
        var constrained = ToolEditor.ConstrainAnnotationLine(new Point(0, 0), new Point(90, 30));
        check(Math.Abs(constrained.Y) < .001 && constrained.X > 90, "Shift constrains a shallow annotation line to the horizontal axis.");
        var diagonal = ToolEditor.ConstrainAnnotationLine(new Point(10, 10), new Point(60, 50));
        check(Math.Abs((diagonal.X - 10) - (diagonal.Y - 10)) < .001, "Shift constrains annotation lines to 45-degree increments.");
        var library = new ClipboardLibrary(Path.Combine(root, "annotations")); var item = library.AddMedia(source);
        var marks = AnnotationState.Serialize([
            new AnnotationState { Kind = "rectangle", X = 20, Y = 20, Width = 60, Height = 50, Stroke = "#FF0000FF", StrokeWidth = 8 },
            new AnnotationState { Kind = "polyline", X = 0, Y = 0, Stroke = "#FF008000", StrokeWidth = 5, Points = [new Point(35, 100), new Point(90, 100)] },
            new AnnotationState { Kind = "text", X = 50, Y = 130, Width = 120, Height = 40, Text = "Test", FontSize = 20 }
        ]);
        var originalPath = library.MediaPath(item); var originalBitmap = ImageEngine.Load(originalPath);
        var editor = new ToolEditor(owner, "annotate", "Test annotation", originalPath, preview: originalBitmap); editor.LoadAnnotations(marks); editor.Show(); Pump(TimeSpan.FromMilliseconds(100));
        check(editor.SelectAnnotationAt(new Point(70, 145)), "A persisted text annotation can be selected on the canvas.");
        check(editor.ApplySelectedAnnotationStyle("#FFEE0000", 4, 26, "Changed"), "Selected annotation accepts edited text and style.");
        check(AnnotationState.Parse(editor.SaveAnnotations()).Any(mark => mark.Kind == "text" && mark.Text == "Changed" && mark.FontSize == 26), "Edited annotation text and font persist in the model.");
        editor.UndoAnnotation();
        check(AnnotationState.Parse(editor.SaveAnnotations()).Any(mark => mark.Kind == "text" && mark.Text == "Test"), "Undo restores the original annotation text.");
        check(editor.SelectAnnotationAt(new Point(30, 30)), "A persisted rectangle can be selected for resizing.");
        check(editor.SelectionHasResizeHandle(new Point(80, 70)), "The selected rectangle has a visible canvas resize handle.");
        if (testInput)
        {
            TestAnnotationDrag(editor, check);
            editor.UndoAnnotation();
            check(editor.SelectAnnotationAt(new Point(30, 30)), "The rectangle can be reselected after undoing a drag.");
        }
        check(editor.ResizeSelectedAnnotation(90, 70), "Selected mark accepts a new size.");
        check(AnnotationState.Parse(editor.SaveAnnotations()).Any(mark => mark.Kind == "rectangle" && mark.Width == 90 && mark.Height == 70), "Resized dimensions survive serialization.");
        editor.UndoAnnotation();
        check(AnnotationState.Parse(editor.SaveAnnotations()).Any(mark => mark.Kind == "rectangle" && mark.Width == 60 && mark.Height == 50), "Undo restores original dimensions.");
        editor.RecordAnnotationChange();
        editor.LoadAnnotations(AnnotationState.Serialize([new AnnotationState { Kind = "rectangle", X = 1, Y = 1, Width = 8, Height = 8 }]));
        editor.UndoAnnotation();
        check(AnnotationState.Parse(editor.SaveAnnotations()).Length == 3, "Undo restores the previous annotation set.");
        editor.RedoAnnotation();
        check(AnnotationState.Parse(editor.SaveAnnotations()).Length == 4, "Redo restores the undone annotation set.");
        editor.UndoAnnotation();
        check(editor.AcceptForTest(), "Annotation editor renders loaded marks.");
        var withoutSelection = ImageEngine.PngBytes(editor.ImageOptions.Annotated!);
        check(editor.SelectAnnotationAt(new Point(30, 30)) && editor.AcceptForTest(), "The editor renders while a mark is selected.");
        check(withoutSelection.SequenceEqual(ImageEngine.PngBytes(editor.ImageOptions.Annotated!)), "The selection outline and resize handle are excluded from the saved image.");
        var persistedMarks = editor.SaveAnnotations(); var rendered = ImageEngine.Apply("annotate", [originalPath], root, editor.ImageOptions, "png").Single(); editor.Close();
        library.SaveAnnotation(item.Id, item.Content, rendered, persistedMarks);
        var reloaded = new ClipboardLibrary(library.DirectoryPath); var saved = reloaded.Items.Single(value => value.Id == item.Id);
        check(saved.AnnotationBase == item.Content && AnnotationState.Parse(saved.Annotations).Length == 3 && File.Exists(reloaded.AnnotationBasePath(saved)), "Editable vector annotations and their unmodified base survive restart.");
        var image = new FormatConvertedBitmap(ImageEngine.Load(reloaded.MediaPath(saved)), PixelFormats.Bgra32, null, 0); var pixel = new byte[4];
        image.CopyPixels(new Int32Rect(20, 40, 1, 1), pixel, 4, 0);
        check(pixel[0] > 180 && pixel[2] < 100, "Saved full-resolution image contains the blue annotation pixels.");
        var crop = new Int32Rect(10, 10, 160, 160); var cropOptions = new ImageOptions(Crop: crop);
        var cropRendered = ImageEngine.Apply("crop", [reloaded.MediaPath(saved)], root, cropOptions, "png").Single();
        var cropBase = ImageEngine.Apply("crop", [reloaded.AnnotationBasePath(saved)], root, cropOptions, "png").Single();
        reloaded.SaveCrop(saved.Id, saved.Content, cropRendered, cropBase, crop.X, crop.Y);
        var cropped = new ClipboardLibrary(library.DirectoryPath).Items.Single(value => value.Id == item.Id);
        var first = AnnotationState.Parse(cropped.Annotations)[0];
        check(first.X == 10 && first.Y == 10 && ImageEngine.Load(Path.Combine(library.DirectoryPath, "media", cropped.AnnotationBase)).PixelWidth == 160, "Cropping translates editable marks and preserves the cropped base.");
        var reopened = new ToolEditor(owner, "annotate", "Edit persisted annotation", Path.Combine(library.DirectoryPath, "media", cropped.AnnotationBase), preview: ImageEngine.Load(Path.Combine(library.DirectoryPath, "media", cropped.AnnotationBase)));
        reopened.LoadAnnotations(cropped.Annotations); check(AnnotationState.Parse(reopened.SaveAnnotations()).Length == 3, "Saved marks reopen in the native editor."); reopened.Close();
    }
    private static void TestAnnotationDrag(ToolEditor editor, Action<bool, string> check)
    {
        if ((GetAsyncKeyState(1) & 0x8000) != 0) throw new InvalidOperationException("The mouse button is already in use.");
        GetCursorPos(out var previous);
        var previousWindow = GetForegroundWindow();
        var editorWindow = new System.Windows.Interop.WindowInteropHelper(editor).Handle;
        var destinationX = int.MinValue; var destinationY = int.MinValue;
        var pressed = false;
        try
        {
            editor.SelectDrawingToolForTest(11); editor.Activate();
            for (var attempt = 0; attempt < 5 && GetForegroundWindow() != editorWindow; attempt++)
            {
                PasteTargetChecks.SetForegroundForTest(editorWindow); Pump(TimeSpan.FromMilliseconds(200));
            }
            check(GetForegroundWindow() == editorWindow, "The annotation editor is the foreground test window.");
            editor.DrawingForTest.BringIntoView(new Rect(60, 50, 60, 60)); Pump(TimeSpan.FromMilliseconds(200));
            var start = editor.DrawingForTest.PointToScreen(new Point(80, 70));
            var end = editor.DrawingForTest.PointToScreen(new Point(105, 90));
            destinationX = (int)Math.Round(end.X); destinationY = (int)Math.Round(end.Y);
            SetCursorPos((int)Math.Round(start.X), (int)Math.Round(start.Y)); Pump(TimeSpan.FromMilliseconds(100));
            var over = System.Windows.Input.Mouse.DirectlyOver;
            var active = GetForegroundWindow();
            mouse_event(2, 0, 0, 0, UIntPtr.Zero); pressed = true; Pump(TimeSpan.FromMilliseconds(100));
            SetCursorPos(destinationX, destinationY); Pump(TimeSpan.FromMilliseconds(150));
            mouse_event(4, 0, 0, 0, UIntPtr.Zero); pressed = false; Pump(TimeSpan.FromMilliseconds(150));
            var rectangle = AnnotationState.Parse(editor.SaveAnnotations()).First(mark => mark.Kind == "rectangle");
            check(rectangle.Width > 75 && rectangle.Height > 60, $"Dragging the actual resize handle changes the selected rectangle (size={rectangle.Width}x{rectangle.Height}, start={start}, end={end}, over={over}, foreground={active}, editor={new System.Windows.Interop.WindowInteropHelper(editor).Handle}).");
        }
        finally
        {
            if (pressed) mouse_event(4, 0, 0, 0, UIntPtr.Zero);
            if (GetCursorPos(out var current) && current.X == destinationX && current.Y == destinationY) SetCursorPos(previous.X, previous.Y);
            if (previousWindow != IntPtr.Zero && GetForegroundWindow() == editorWindow) PasteTargetChecks.SetForegroundForTest(previousWindow);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X; public int Y; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();

    private sealed class TestContext : IModuleContext
    {
        internal List<string> Errors { get; } = [];
        public IModuleSettings Settings { get; } = new TestSettings();
        public ILogger Log => new TestLogger(Errors);
        public IHotkeyService Hotkeys { get; } = new TestHotkeys();
        public void Notify(string message) { }
    }
    private sealed class TestHotkeys : IHotkeyService
    {
        public IDisposable Register(uint modifiers, uint virtualKey, Action callback) => new Registration();
        private sealed class Registration : IDisposable { public void Dispose() { } }
    }
    private sealed class TestSettings : IModuleSettings
    {
        public bool GetBool(string key, bool fallback = false) => fallback;
        public int GetInt(string key, int fallback = 0) => fallback;
        public string GetString(string key, string fallback = "") => fallback;
        public void Set(string key, object? value) { }
    }
    private sealed class TestLogger(List<string> errors) : ILogger
    {
        public string Path => "";
        public void Info(string message) { }
        public void Error(string message, Exception? exception = null) => errors.Add(message + " " + exception?.Message);
    }
}
