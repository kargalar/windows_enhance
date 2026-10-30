using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using WinForge.FileTools;
namespace WinForge.ClipboardLine;

public enum ClipKind { Image, Text, Link, Recording }
public sealed record ClipFolder(Guid Id, string Name, Guid? ParentId = null);
public sealed record ClipItem(Guid Id, ClipKind Kind, string Content, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public string Title { get; init; } = "";
    public string Notes { get; init; } = "";
    public string OcrText { get; init; } = "";
    public string LinkTitle { get; init; } = "";
    public string LinkAuthor { get; init; } = "";
    public string LinkThumbnail { get; init; } = "";
    public bool LinkEnriched { get; init; }
    public string OriginalFile { get; init; } = "";
    public string AnnotationBase { get; init; } = "";
    public string Annotations { get; init; } = "";
    public string[] EditHistory { get; init; } = [];
    public string[] Tags { get; init; } = [];
    public Guid[] FolderIds { get; init; } = [];
    public bool Pinned { get; init; }
    public bool Saved { get; init; }
    public bool OnShelf { get; init; } = true;
    public DateTimeOffset? LastCopiedAt { get; init; }
    public DateTimeOffset? TrashedAt { get; init; }
    public string DisplayTitle => Title.Length > 0 ? Title : Kind == ClipKind.Link && LinkTitle.Length > 0 ? LinkTitle : Kind is ClipKind.Image or ClipKind.Recording ? Path.GetFileName(Content) : Content;
}

public sealed record ClipPreferences
{
    public bool CaptureText { get; init; }
    public bool CaptureImages { get; init; }
    public bool CaptureLinks { get; init; } = true;
    public bool FetchLinkMetadata { get; init; } = true;
    public bool IndexImages { get; init; } = true;
    public bool ShowImages { get; init; } = true;
    public bool ShowTexts { get; init; } = true;
    public bool ShowLinks { get; init; } = true;
    public int ImageRetentionDays { get; init; } = 30;
    public int TextRetentionDays { get; init; } = 30;
    public int LinkRetentionDays { get; init; } = 30;
    public string[] WatchFolders { get; init; } = [];
    public bool ListView { get; init; }
    public bool Sounds { get; init; }
    public string CardSize { get; init; } = "medium";
    public double SidebarWidth { get; init; } = 180;
    public double InspectorWidth { get; init; } = 260;
    public double[] ListColumnWidths { get; init; } = [46, 18, 18, 18];
    public Dictionary<string, string> LocalShortcuts { get; init; } = new(StringComparer.Ordinal);
}

internal sealed record ClipDocument
{
    public int Version { get; init; } = 1;
    public ClipPreferences Preferences { get; init; } = new();
    public ClipItem[] Items { get; init; } = [];
    public ClipFolder[] Folders { get; init; } = [];
    public bool CleanupPending { get; init; }
}

/// <summary>Local clipboard history with atomic writes, immutable media, recovery and session undo.</summary>
public sealed partial class ClipboardLibrary
{
    public const int MaximumTextLength = 1_048_576;
    private const int MaximumItems = 20_000;
    private const long MaximumLibraryBytes = 64 * 1024 * 1024;
    private const long MaximumMediaBytes = 1024L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Func<DateTimeOffset> _clock;
    private readonly Stack<UndoEntry> _undo = new();
    private readonly Stack<UndoEntry> _redo = new();
    private ClipDocument _document;
    public string DirectoryPath { get; }
    public string? RecoveryNotice { get; private set; }
    public IReadOnlyList<ClipItem> Items => _document.Items;
    public IReadOnlyList<ClipFolder> Folders => _document.Folders;
    public ClipPreferences Preferences => _document.Preferences;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event EventHandler? Changed;

    public ClipboardLibrary(string? directory = null, Func<DateTimeOffset>? clock = null)
    {
        DirectoryPath = Path.GetFullPath(directory ?? Path.Combine(WinForge.Core.AppData.DirectoryPath, "ClipboardLine"));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        Directory.CreateDirectory(DirectoryPath);
        if ((File.GetAttributes(DirectoryPath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Pano kütüphanesi bağlantı içeren bir klasörde tutulamaz.");
        ProtectDirectory(DirectoryPath);
        Directory.CreateDirectory(Path.Combine(DirectoryPath, "media"));
        if ((File.GetAttributes(Path.Combine(DirectoryPath, "media")) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Pano medya klasörü bağlantı olamaz.");
        _document = Load();
        if (_document.CleanupPending) FinishMediaCleanup();
        Expire();
    }

    private static void ProtectDirectory(string directory)
    {
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(true, false);
        var current = WindowsIdentity.GetCurrent().User ?? throw new IOException("Windows kullanıcı kimliği okunamadı.");
        foreach (var sid in new[] { current, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(directory).SetAccessControl(acl);
    }

    private ClipDocument Load()
    {
        var file = Path.Combine(DirectoryPath, "library.json");
        if (!File.Exists(file)) return new();
        try { return ReadDocument(file); }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException)
        {
            var preserved = file + ".corrupt-" + Guid.NewGuid().ToString("N");
            File.Copy(file, preserved);
            var backup = file + ".backup";
            if (!File.Exists(backup)) throw new InvalidDataException("Pano kütüphanesi bozuk. Özgün dosya korundu: " + preserved, error);
            var recovered = ReadDocument(backup);
            var repair = file + ".repair-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(backup, repair);
                File.Replace(repair, file, null);
            }
            finally { if (File.Exists(repair)) File.Delete(repair); }
            RecoveryNotice = "Pano kütüphanesi yedekten kurtarıldı; bozuk dosya korundu.";
            return recovered;
        }
    }

    private static ClipDocument ReadDocument(string file)
    {
        if (new FileInfo(file).Length > MaximumLibraryBytes) throw new InvalidDataException("Pano kütüphanesi boyut sınırını aşıyor.");
        var document = JsonSerializer.Deserialize<ClipDocument>(File.ReadAllText(file), JsonOptions) ?? throw new InvalidDataException("Pano kütüphanesi boş.");
        Validate(document);
        return document;
    }

    private static void Validate(ClipDocument document)
    {
        if (document.Version != 1 || document.Items is null || document.Folders is null || document.Preferences is null || document.Items.Length > MaximumItems || document.Items.Any(item => item is null) || document.Folders.Any(folder => folder is null))
            throw new InvalidDataException("Pano kütüphanesi biçimi geçersiz.");
        ValidatePreferences(document.Preferences);
        if (document.Items.Select(item => item.Id).Distinct().Count() != document.Items.Length || document.Folders.Select(folder => folder.Id).Distinct().Count() != document.Folders.Length)
            throw new InvalidDataException("Kütüphanede yinelenen kimlik var.");
        var folderIds = document.Folders.Select(folder => folder.Id).ToHashSet();
        foreach (var folder in document.Folders)
        {
            Text(folder.Name, 200);
            var seen = new HashSet<Guid> { folder.Id };
            var parent = folder.ParentId;
            while (parent is { } id)
            {
                if (!seen.Add(id)) throw new InvalidDataException("Klasör döngüsü geçersiz.");
                parent = document.Folders.FirstOrDefault(candidate => candidate.Id == id)?.ParentId;
                if (!folderIds.Contains(id)) throw new InvalidDataException("Üst klasör bulunamadı.");
            }
        }
        foreach (var item in document.Items)
        {
            if (item.Id == Guid.Empty || !Enum.IsDefined(item.Kind) || item.Tags is null || item.FolderIds is null || item.EditHistory is null) throw new InvalidDataException("Pano kaydı geçersiz.");
            TextContent(item.Content); Text(item.Title, 500, true); Text(item.Notes, MaximumTextLength, true); Text(item.OcrText, MaximumTextLength, true);
            Text(item.LinkTitle, 500, true); Text(item.LinkAuthor, 500, true); Text(item.LinkThumbnail, 100, true);
            Text(item.AnnotationBase, 100, true); Text(item.Annotations, 30_000_000, true);
            if (item.AnnotationBase.Length > 0 && (item.Kind != ClipKind.Image || !MediaName().IsMatch(item.AnnotationBase))) throw new InvalidDataException("İşaretleme kaynağı geçersiz.");
            if (item.Annotations.Length > 0) { if (item.AnnotationBase.Length == 0) throw new InvalidDataException("İşaretleme kaynağı eksik."); _ = AnnotationState.Parse(item.Annotations); }
            if (item.LinkThumbnail.Length > 0 && !MediaName().IsMatch(item.LinkThumbnail)) throw new InvalidDataException("Bağlantı görseli geçersiz.");
            if (item.Tags.Length > 100 || item.FolderIds.Any(id => !folderIds.Contains(id))) throw new InvalidDataException("Pano düzenleme bilgisi geçersiz.");
            foreach (var tag in item.Tags) Text(tag, 100);
            if (item.Kind is ClipKind.Image or ClipKind.Recording)
            {
                if (!MediaName().IsMatch(item.Content) || !MediaName().IsMatch(item.OriginalFile) || item.EditHistory.Length > 30 || item.EditHistory.Any(name => !MediaName().IsMatch(name)))
                    throw new InvalidDataException("Pano medya yolu geçersiz.");
            }
            if (item.Kind == ClipKind.Link && !IsStandaloneLink(item.Content)) throw new InvalidDataException("Pano bağlantısı geçersiz.");
        }
    }

    private static void ValidatePreferences(ClipPreferences value)
    {
        if (new[] { value.ImageRetentionDays, value.TextRetentionDays, value.LinkRetentionDays }.Any(days => days is < 1 or > 3650) || value.WatchFolders is null || value.WatchFolders.Length > 3 || value.WatchFolders.Any(path => !Path.IsPathFullyQualified(path)) || value.CardSize is not ("small" or "medium" or "large") || !double.IsFinite(value.SidebarWidth) || value.SidebarWidth is < 160 or > 400 || !double.IsFinite(value.InspectorWidth) || value.InspectorWidth is < 220 or > 480 || value.ListColumnWidths is null || value.ListColumnWidths.Length != 4 || value.ListColumnWidths.Any(width => !double.IsFinite(width) || width is < 5 or > 80) || Math.Abs(value.ListColumnWidths.Sum() - 100) > .01)
            throw new ArgumentException("Pano tercihleri geçersiz.");
        ClipboardKeymap.Validate(value.LocalShortcuts);
    }

    private static string Text(string? value, int limit, bool empty = false)
    {
        if (value is null || value.Length > limit || (!empty && string.IsNullOrWhiteSpace(value)) || value.Contains('\0')) throw new ArgumentException("Metin boş veya boyut sınırını aşıyor.");
        return value;
    }

    private static string TextContent(string? value)
    {
        var text = Text(value, MaximumTextLength, empty: true);
        if (text.Length == 0) throw new ArgumentException("Pano metni boş olamaz.");
        return text;
    }

    private void Commit(ClipDocument document, bool undo = true)
    {
        Validate(document);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        if (bytes.LongLength > MaximumLibraryBytes) throw new InvalidDataException("Pano kütüphanesi dolu; gereksiz kayıtları silin.");
        var file = Path.Combine(DirectoryPath, "library.json");
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
            if (File.Exists(file)) File.Replace(temporary, file, file + ".backup");
            else File.Move(temporary, file);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        if (undo)
        {
            if (_undo.Count >= 30) _undo.Clear();
            _undo.Push(new UndoEntry(_document, document)); _redo.Clear();
        }
        _document = document;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static bool IsStandaloneLink(string value) => !value.Any(char.IsWhiteSpace) && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo);
    private static string Identity(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        try { normalized = normalized.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { /* Preserve literal invalid UTF-16 rather than losing a clipboard item. */ }
        var trimmed = normalized.Trim('\n');
        return trimmed.Length == 0 ? normalized : trimmed;
    }

    public ClipItem AddText(string value, bool note = false)
    {
        TextContent(value);
        var kind = IsStandaloneLink(value) ? ClipKind.Link : ClipKind.Text;
        var existing = note ? null : Items.FirstOrDefault(item => item.Kind == kind && item.TrashedAt is null && Identity(item.Content) == Identity(value));
        var now = _clock();
        var item = existing is null ? new ClipItem(Guid.NewGuid(), kind, value, now, now) { Saved = note } : existing with { Content = value, UpdatedAt = now, OnShelf = true };
        Commit(_document with { Items = new[] { item }.Concat(Items.Where(candidate => candidate.Id != item.Id)).ToArray() }, undo: false);
        return item;
    }

    public ClipItem AddMedia(string file, string? title = null)
    {
        var extension = Path.GetExtension(file).ToLowerInvariant();
        if (extension is not (".png" or ".mp4" or ".mov")) throw new ArgumentException("PNG görseli veya MP4/MOV kaydı seçin.");
        if (extension == ".png") ValidatePng(file);
        var name = StoreMedia(file, extension);
        var existing = Items.FirstOrDefault(item => item.OriginalFile == name && item.TrashedAt is null);
        var now = _clock();
        var displayTitle = Text(title ?? Path.GetFileNameWithoutExtension(file), 500, empty: true);
        var item = existing is null ? new ClipItem(Guid.NewGuid(), extension == ".png" ? ClipKind.Image : ClipKind.Recording, name, now, now) { OriginalFile = name, Title = displayTitle } : existing with { UpdatedAt = now, OnShelf = true };
        Commit(_document with { Items = new[] { item }.Concat(Items.Where(candidate => candidate.Id != item.Id)).ToArray() }, undo: false);
        return item;
    }

    private string StoreMedia(string file, string extension)
    {
        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is <= 0 or > MaximumMediaBytes) throw new InvalidDataException("Pano medyası boyut sınırını aşıyor.");
        var hash = Convert.ToHexStringLower(SHA256.HashData(input));
        var name = hash + extension; var destination = MediaPath(name);
        if (!File.Exists(destination))
        {
            input.Position = 0;
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output); output.Flush(true);
        }
        return name;
    }

    private static void ValidatePng(string file)
    {
        using var input = File.OpenRead(file); Span<byte> signature = stackalloc byte[8];
        if (input.Read(signature) != signature.Length || !signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new InvalidDataException("Geçerli bir PNG görseli seçin.");
        _ = FileTools.UI.ImageEngine.Load(file);
    }

    public string MediaPath(ClipItem item, bool original = false) => MediaPath(original ? item.OriginalFile : item.Content);
    private string MediaPath(string name)
    {
        if (!MediaName().IsMatch(name)) throw new ArgumentException("Geçersiz medya yolu.");
        var file = Path.Combine(DirectoryPath, "media", name);
        if (File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Medya dosyası bağlantı olamaz.");
        return file;
    }

    public void Update(IEnumerable<Guid> ids, Func<ClipItem, ClipItem> change, bool undo = true, bool touch = true)
    {
        var selected = ids.ToHashSet();
        if (selected.Count == 0) return;
        ClipItem Apply(ClipItem item) { var updated = change(item); return touch ? updated with { UpdatedAt = _clock() } : updated; }
        Commit(_document with { Items = Items.Select(item => selected.Contains(item.Id) ? Apply(item) : item).ToArray() }, undo);
    }

    public void SaveDetails(Guid id, string title, string content, string notes, string[] tags)
    {
        Text(title, 500, true); Text(notes, MaximumTextLength, true);
        var names = tags.Select(tag => tag.Trim()).Where(tag => tag.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Update([id], item =>
        {
            var edited = item with { Title = title, Content = item.Kind is ClipKind.Text or ClipKind.Link ? TextContent(content) : item.Content, Notes = notes, Tags = names, Saved = true };
            return item.Kind == ClipKind.Link && edited.Content != item.Content ? edited with { LinkTitle = "", LinkAuthor = "", LinkThumbnail = "", LinkEnriched = false } : edited;
        });
    }

    internal void SetLinkMetadata(Guid id, string expectedUrl, LinkDetails details, string? thumbnailFile)
    {
        var item = Items.SingleOrDefault(item => item.Id == id && item.Kind == ClipKind.Link && item.Content == expectedUrl && item.TrashedAt is null);
        if (item is null) return;
        var thumbnail = "";
        if (thumbnailFile is not null) { ValidatePng(thumbnailFile); thumbnail = StoreMedia(thumbnailFile, ".png"); }
        Update([id], current => current with { LinkTitle = details.Title, LinkAuthor = details.Author, LinkThumbnail = thumbnail, LinkEnriched = true }, undo: false, touch: false);
    }

    internal bool MarkLinkMetadataPending(Guid id)
    {
        if (Items.SingleOrDefault(item => item.Id == id && item.Kind == ClipKind.Link && item.TrashedAt is null) is not { } item) return false;
        Update([id], current => current with { LinkEnriched = false }, undo: false, touch: false);
        return true;
    }

    internal string ThumbnailPath(ClipItem item) => MediaPath(item.LinkThumbnail);

    public void SetPreferences(ClipPreferences preferences) => Commit(_document with { Preferences = preferences }, false);
    public void TogglePin(IEnumerable<Guid> ids) => Update(ids, item => item with { Pinned = !item.Pinned, Saved = true });
    public void MarkCopied(IEnumerable<Guid> ids) => Update(ids, item => item with { LastCopiedAt = _clock() }, undo: false);
    public void AssignFolder(IEnumerable<Guid> ids, Guid folderId, bool pin = false)
    {
        if (!Folders.Any(folder => folder.Id == folderId)) throw new ArgumentException("Klasör bulunamadı.");
        Update(ids, item => item with { FolderIds = item.FolderIds.Append(folderId).Distinct().ToArray(), Saved = true, Pinned = pin || item.Pinned });
    }

    public ClipFolder CreateFolder(string name, Guid? parent = null)
    {
        Text(name, 200);
        if (parent is { } id && !Folders.Any(folder => folder.Id == id)) throw new ArgumentException("Üst klasör bulunamadı.");
        var folder = new ClipFolder(Guid.NewGuid(), name.Trim(), parent);
        Commit(_document with { Folders = Folders.Append(folder).ToArray() }); return folder;
    }

    public void RenameFolder(Guid id, string name)
    {
        Text(name, 200); Commit(_document with { Folders = Folders.Select(folder => folder.Id == id ? folder with { Name = name.Trim() } : folder).ToArray() });
    }

    public void RemoveFolder(Guid id)
    {
        var removed = new HashSet<Guid> { id };
        while (true) { var before = removed.Count; foreach (var folder in Folders.Where(folder => folder.ParentId is { } parent && removed.Contains(parent))) removed.Add(folder.Id); if (before == removed.Count) break; }
        Commit(_document with { Folders = Folders.Where(folder => !removed.Contains(folder.Id)).ToArray(), Items = Items.Select(item => item with { FolderIds = item.FolderIds.Where(folder => !removed.Contains(folder)).ToArray() }).ToArray() });
    }

    public void AddTags(IEnumerable<Guid> ids, IEnumerable<string> tags)
    {
        var added = tags.Select(tag => Text(tag.Trim(), 100)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (added.Length == 0) return;
        Update(ids, item => item with { Tags = item.Tags.Concat(added).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), Saved = true });
    }

    public void RenameTag(string oldName, string? newName)
    {
        if (newName is not null) Text(newName, 100);
        Update(Items.Where(item => item.Tags.Contains(oldName, StringComparer.OrdinalIgnoreCase)).Select(item => item.Id), item => item with { Tags = item.Tags.Where(tag => !tag.Equals(oldName, StringComparison.OrdinalIgnoreCase)).Concat(newName is null ? [] : new[] { newName }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() });
    }

    public void TakeDown(IEnumerable<Guid> ids) => Update(ids, item => item.Saved ? item with { OnShelf = false } : item with { OnShelf = false, TrashedAt = _clock() });
    public void Trash(IEnumerable<Guid> ids) => Update(ids, item => item with { OnShelf = false, TrashedAt = _clock() });
    public void Restore(IEnumerable<Guid> ids) => Update(ids, item => item with { OnShelf = true, TrashedAt = null });
    public void Expire()
    {
        var now = _clock();
        var expired = Items.Where(item => item.TrashedAt is null && !item.Saved && (now - item.UpdatedAt).TotalDays >= (item.Kind is ClipKind.Image or ClipKind.Recording ? Preferences.ImageRetentionDays : item.Kind == ClipKind.Link ? Preferences.LinkRetentionDays : Preferences.TextRetentionDays)).Select(item => item.Id).ToArray();
        if (expired.Length > 0) Update(expired, item => item with { OnShelf = false, TrashedAt = now }, false);
    }

    public void DeletePermanently(IEnumerable<Guid> ids)
    {
        var selected = ids.ToHashSet();
        if (Items.Any(item => selected.Contains(item.Id) && item.TrashedAt is null)) throw new InvalidOperationException("Önce kayıtları çöpe taşıyın.");
        Commit(_document with { Items = Items.Where(item => !selected.Contains(item.Id)).ToArray(), CleanupPending = true }, false);
        _undo.Clear(); _redo.Clear();
        FinishMediaCleanup();
    }

    private void FinishMediaCleanup()
    {
        // Replace the rollback copy too before removing any unreferenced private media.
        Commit(_document, false);
        var referenced = Items.SelectMany(item => item.EditHistory.Concat(new[] { item.Content, item.OriginalFile, item.LinkThumbnail, item.AnnotationBase })).ToHashSet(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(DirectoryPath, "media")))
            if (MediaName().IsMatch(Path.GetFileName(file)) && !referenced.Contains(Path.GetFileName(file))) File.Delete(file);
        Commit(_document with { CleanupPending = false }, false);
    }

    public void SaveImageEdit(Guid id, string pngFile)
    {
        var item = Items.Single(item => item.Id == id && item.Kind == ClipKind.Image);
        ValidatePng(pngFile);
        var name = StoreMedia(pngFile, ".png");
        Update([id], value => value with { Content = name, AnnotationBase = "", Annotations = "", EditHistory = value.EditHistory.Append(item.Content).TakeLast(30).ToArray(), OcrText = "", Saved = true });
    }

    internal void SaveAnnotation(Guid id, string expectedContent, string renderedFile, string annotations)
    {
        var item = CurrentImage(id, expectedContent); _ = AnnotationState.Parse(annotations); ValidatePng(renderedFile);
        var name = StoreMedia(renderedFile, ".png"); var baseName = item.AnnotationBase.Length > 0 ? item.AnnotationBase : item.Content;
        Update([id], current => current with { Content = name, AnnotationBase = baseName, Annotations = annotations, EditHistory = current.EditHistory.Append(item.Content).TakeLast(30).ToArray(), OcrText = "", Saved = true });
    }

    internal void SaveCrop(Guid id, string expectedContent, string renderedFile, string? baseFile, double x, double y)
    {
        var item = CurrentImage(id, expectedContent); ValidatePng(renderedFile);
        var name = StoreMedia(renderedFile, ".png"); var baseName = ""; var marks = "";
        if (item.AnnotationBase.Length > 0)
        {
            if (baseFile is null) throw new InvalidDataException("İşaretleme kaynağı kırpılamadı.");
            ValidatePng(baseFile); baseName = StoreMedia(baseFile, ".png"); marks = AnnotationState.Translate(item.Annotations, x, y);
        }
        Update([id], current => current with { Content = name, AnnotationBase = baseName, Annotations = marks, EditHistory = current.EditHistory.Append(item.Content).TakeLast(30).ToArray(), OcrText = "", Saved = true });
    }

    private ClipItem CurrentImage(Guid id, string expectedContent)
    {
        var item = Items.Single(item => item.Id == id && item.Kind == ClipKind.Image && item.TrashedAt is null);
        if (item.Content != expectedContent) throw new InvalidOperationException("Görsel işlem sırasında değişti; düzenleme uygulanmadı.");
        return item;
    }

    internal string AnnotationBasePath(ClipItem item) => MediaPath(item.AnnotationBase.Length > 0 ? item.AnnotationBase : item.Content);

    public void SaveRecordingEdit(Guid id, string expectedContent, string file)
    {
        var item = Items.Single(item => item.Id == id && item.Kind == ClipKind.Recording && item.TrashedAt is null);
        if (item.Content != expectedContent) throw new InvalidOperationException("Kayıt işlem sırasında değişti; kırpma uygulanmadı.");
        var name = StoreMedia(file, ".mp4");
        Update([id], value => value with { Content = name, EditHistory = value.EditHistory.Append(item.Content).TakeLast(30).ToArray(), Saved = true });
    }

    public void RestoreOriginal(Guid id) => Update([id], item => item with { Content = item.OriginalFile, AnnotationBase = "", Annotations = "", OcrText = "", EditHistory = item.EditHistory.Append(item.Content).TakeLast(30).ToArray() });
    public void Undo()
    {
        if (!_undo.TryPeek(out var previous)) return;
        Commit(Replay(previous.After, previous.Before), false); _undo.Pop(); _redo.Push(previous);
    }
    public void Redo()
    {
        if (!_redo.TryPeek(out var next)) return;
        Commit(Replay(next.Before, next.After), false); _redo.Pop(); _undo.Push(next);
    }

    private ClipDocument Replay(ClipDocument expected, ClipDocument target)
    {
        var expectedItems = expected.Items.ToDictionary(item => item.Id);
        var targetItems = target.Items.ToDictionary(item => item.Id);
        var result = Items.ToDictionary(item => item.Id);
        foreach (var id in expectedItems.Keys.Union(targetItems.Keys))
        {
            expectedItems.TryGetValue(id, out var before); targetItems.TryGetValue(id, out var after); result.TryGetValue(id, out var current);
            if (Equals(before, after) || !Equals(current, before)) continue;
            if (after is null) result.Remove(id); else result[id] = after;
        }
        var folders = ReferenceEquals(_document.Folders, expected.Folders) ? target.Folders : _document.Folders;
        var validFolders = folders.Select(folder => folder.Id).ToHashSet();
        return _document with { Folders = folders, Items = result.Values.Select(item => item.FolderIds.All(validFolders.Contains) ? item : item with { FolderIds = item.FolderIds.Where(validFolders.Contains).ToArray() }).OrderByDescending(item => item.UpdatedAt).ToArray() };
    }

    private sealed record UndoEntry(ClipDocument Before, ClipDocument After);

    public IReadOnlyList<ClipItem> Search(string query, bool shelves = false, bool trash = false, Guid? folder = null, string? tag = null, bool pinned = false, ClipKind? kind = null, DateTimeOffset? after = null, string? domain = null)
    {
        var terms = Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Items.Where(item => (item.TrashedAt is not null) == trash && (!shelves || item.OnShelf && Visible(item.Kind)) && (!pinned || item.Pinned) && (kind is null || item.Kind == kind) && (folder is null || item.FolderIds.Contains(folder.Value)) && (tag is null || item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) && (after is null || item.CreatedAt >= after) && (domain is null || item.Kind == ClipKind.Link && new Uri(item.Content).Host.Contains(domain, StringComparison.OrdinalIgnoreCase)) && terms.All(term => Fold(string.Join(' ', item.Title, item.Content, item.LinkTitle, item.LinkAuthor, item.Notes, item.OcrText, string.Join(' ', item.Tags))).Contains(term, StringComparison.Ordinal))).OrderByDescending(item => item.UpdatedAt).ToArray();
    }

    public bool Visible(ClipKind kind) => kind is ClipKind.Image or ClipKind.Recording ? Preferences.ShowImages : kind == ClipKind.Link ? Preferences.ShowLinks : Preferences.ShowTexts;
    private static string Fold(string value) => string.Concat(value.Normalize(NormalizationForm.FormD).Where(character => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.NonSpacingMark)).ToLowerInvariant().Replace('ı', 'i');
    [GeneratedRegex("\\A[a-f0-9]{64}\\.(png|mp4|mov)\\z", RegexOptions.CultureInvariant)]
    private static partial Regex MediaName();
}
