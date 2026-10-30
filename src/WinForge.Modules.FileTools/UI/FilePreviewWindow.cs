using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using WinForge.Core;
using WinForge.FileTools.Engine;

namespace WinForge.FileTools.UI;

// React owns the preview presentation; this window manages the native preview lifecycle.
internal sealed class FilePreviewWindow : Window
{
    private readonly string[] paths;
    private readonly Toolchain tools;
    private readonly OfficeRuntime office;
    private readonly IFeatureUiAssetSurface uiSurface;
    private readonly string temporary = Path.Combine(WinForge.Core.AppData.TemporaryDirectory, "Nashi-quick-preview-" + Guid.NewGuid().ToString("N"));
    private CancellationTokenSource lifetime = new();
    private PdfRasterDocument? pdfDocument;
    private int index;
    private int pdfPage;
    private int pdfPageCount;
    private string kind = "message";
    private string message = "Preparing preview…";
    private string image = "";
    private string text = "";
    private string mediaUrl = "";
    private bool mediaFallbackAttempted;

    internal string PreviewStatus => message;
    internal string CurrentPath => paths[index];

    internal FilePreviewWindow(Window owner, IEnumerable<string> paths, Toolchain? tools = null,
        OfficeRuntime? office = null, IFeatureUiService? featureUi = null)
    {
        this.tools = tools ?? new Toolchain(
            Path.Combine(WinForge.Core.AppData.DirectoryPath, "Nashi", "Tools", "v1"),
            Environment.GetEnvironmentVariable("PATH"));
        this.office = office ?? new OfficeRuntime(
            Path.Combine(WinForge.Core.AppData.DirectoryPath, "Nashi", "Tools"));
        this.paths = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (this.paths.Length == 0) throw new ArgumentException("Choose a file to preview.", nameof(paths));

        Owner = owner;
        Title = "Preview · Nashi";
        Width = 800;
        Height = 620;
        MinWidth = 560;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        uiSurface = featureUi?.CreateSurface("nashi-file-preview") as IFeatureUiAssetSurface
            ?? throw new InvalidOperationException("Dosya önizlemesi için React varlık yüzeyi gerekli.");
        uiSurface.MessageReceived += OnUiMessage;
        Content = uiSurface.Element;
        Closed += (_, _) =>
        {
            uiSurface.MessageReceived -= OnUiMessage;
            uiSurface.Dispose();
            lifetime.Cancel();
            lifetime.Dispose();
            pdfDocument?.Dispose();
            pdfDocument = null;
            _ = CleanupAsync();
        };
        Loaded += async (_, _) => await LoadAsync();
        PushState();
    }

    internal void Navigate(int delta)
    {
        if (paths.Length < 2) return;
        index = (index + delta + paths.Length) % paths.Length;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        lifetime.Cancel();
        lifetime.Dispose();
        lifetime = new CancellationTokenSource();
        var token = lifetime.Token;
        pdfDocument?.Dispose();
        pdfDocument = null;
        pdfPage = 0;
        pdfPageCount = 0;
        mediaFallbackAttempted = false;
        image = text = mediaUrl = "";
        kind = "message";
        message = "Preparing preview…";
        PushState();

        try
        {
            var path = CurrentPath;
            if (Directory.Exists(path))
            {
                message = "Folder · " + Path.GetFileName(path) + "\nUse Open to view its contents.";
                PushState();
                return;
            }
            if (!File.Exists(path)) throw new FileNotFoundException("This file has moved or was removed.");

            var extension = FeatureCatalog.Extension(path);
            if (new[] { "docx", "doc", "rtf", "rtfd", "pages", "xlsx", "xls", "ods", "pptx", "ppt", "odp", "epub" }.Contains(extension))
            {
                if (office.Executable is null)
                {
                    message = "Set up office tools in Nashi to preview this document here, or use Open.";
                    PushState();
                    return;
                }
                Directory.CreateDirectory(temporary);
                path = (await new DocumentConversion(office, tools).ConvertAsync([path], "pdf", temporary, new(), null, token))[0];
                token.ThrowIfCancellationRequested();
                extension = "pdf";
            }

            if (FeatureCatalog.IsImage(path))
            {
                var bitmap = ImageEngine.Load(path);
                kind = "image";
                image = ImageDataUri(bitmap);
            }
            else if (extension == "pdf")
            {
                pdfDocument = await Task.Run(() => PdfRasterDocument.Open(path), token);
                pdfPageCount = pdfDocument.PageCount;
                kind = "pdf";
                await RenderPdfPageAsync(0, token);
                return;
            }
            else if (FeatureCatalog.IsVideo(path) || FeatureCatalog.IsAudio(path))
            {
                kind = FeatureCatalog.IsVideo(path) ? "video" : "audio";
                mediaUrl = uiSurface.MapLocalAsset(path).AbsoluteUri;
            }
            else if (new[] { "txt", "md", "json", "xml", "csv", "log", "yaml", "yml", "ini", "toml" }.Contains(extension))
            {
                kind = "text";
                text = await File.ReadAllTextAsync(path, token);
            }
            else
            {
                message = "Use Open to preview this document in its default app.";
            }

            token.ThrowIfCancellationRequested();
            PushState();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (token.IsCancellationRequested) return;
            kind = "message";
            message = "Preview unavailable. " + error.Message;
            PushState();
        }
    }

    private async Task RenderPdfPageAsync(int delta, CancellationToken token)
    {
        var document = pdfDocument;
        if (document is null || document.PageCount == 0) return;
        pdfPage = Math.Clamp(pdfPage + delta, 0, document.PageCount - 1);
        try
        {
            var requestedPage = pdfPage;
            var png = await Task.Run(() => document.RenderPage(requestedPage, 144, token), token);
            if (token.IsCancellationRequested || !ReferenceEquals(pdfDocument, document) || pdfPage != requestedPage) return;
            image = "data:image/png;base64," + Convert.ToBase64String(png);
            kind = "pdf";
            message = "";
            PushState();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
            {
                kind = "message";
                message = "Preview unavailable. " + error.Message;
                PushState();
            }
        }
    }

    private async void OnUiMessage(string command, JsonElement payload)
    {
        try
        {
            switch (command)
            {
                case "preview.navigate": Navigate(payload.GetProperty("delta").GetInt32()); break;
                case "preview.open":
                    if (File.Exists(CurrentPath) || Directory.Exists(CurrentPath)) MainWindow.OpenUrl(CurrentPath);
                    break;
                case "preview.close": Close(); break;
                case "preview.pdf-page": await RenderPdfPageAsync(payload.GetProperty("delta").GetInt32(), lifetime.Token); break;
                case "preview.media-error": await RetryMediaAsync(); break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            kind = "message";
            message = "Preview unavailable. " + error.Message;
            PushState();
        }
    }

    private async Task RetryMediaAsync()
    {
        if (lifetime.IsCancellationRequested || mediaFallbackAttempted) return;
        mediaFallbackAttempted = true;
        if (!tools.Ready)
        {
            kind = "message";
            message = "Set up media tools for this codec, or use Open to preview in its default app.";
            PushState();
            return;
        }

        try
        {
            Directory.CreateDirectory(temporary);
            var source = CurrentPath;
            var audio = FeatureCatalog.IsAudio(source);
            var proxy = Path.Combine(temporary, Guid.NewGuid().ToString("N") + (audio ? ".wav" : ".mp4"));
            var arguments = new List<string> { "-v", "error", "-nostdin", "-i", source };
            if (audio) arguments.AddRange(["-vn", "-c:a", "pcm_s16le"]);
            else arguments.AddRange(["-vf", "scale=w='min(1280,iw)':h=-2", "-c:v", "libx264", "-preset", "veryfast", "-crf", "25", "-pix_fmt", "yuv420p", "-c:a", "aac", "-movflags", "+faststart"]);
            arguments.Add(proxy);
            await ProcessRunner.RunAsync(tools.Executable("ffmpeg"), arguments, lifetime.Token);
            if (lifetime.IsCancellationRequested) return;
            mediaUrl = uiSurface.MapLocalAsset(proxy).AbsoluteUri;
            kind = audio ? "audio" : "video";
            message = "";
            PushState();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            kind = "message";
            message = "Set up media tools for this codec, or use Open to preview in its default app. " + error.Message;
            PushState();
        }
    }

    private static string ImageDataUri(BitmapSource bitmap)
    {
        const int maximumDimension = 2400;
        var scale = Math.Min(1d, maximumDimension / (double)Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
        BitmapSource preview = bitmap;
        if (scale < 1)
        {
            var resized = new System.Windows.Media.Imaging.TransformedBitmap(bitmap,
                new System.Windows.Media.ScaleTransform(scale, scale));
            resized.Freeze();
            preview = resized;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(preview));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }

    private void PushState() => uiSurface.SetState(new
    {
        file = Path.GetFileName(CurrentPath),
        index = index + 1,
        count = paths.Length,
        kind,
        message,
        image,
        text,
        mediaUrl,
        pdfPage = pdfPage + 1,
        pdfPageCount,
        canPrevious = index > 0,
        canNext = index < paths.Length - 1
    });

    private async Task CleanupAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
                return;
            }
            catch (IOException) { await Task.Delay(250); }
            catch (UnauthorizedAccessException) { return; }
        }
    }
}
