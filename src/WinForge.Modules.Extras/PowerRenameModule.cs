using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class PowerRenameModule : ITweakModule, IAppCommandModule
{
    private readonly SettingDescriptor[] _settings;
    private readonly ExplorerFileVerb _explorerVerb = new("WinForge.PowerRename", "Toplu yeniden adlandır (WinForge)", "--power-rename %*", multiSelect: true);
    private bool _isActive;
    private IFeatureUiService? _featureUi;

    public PowerRenameModule()
    {
        _settings =
        [
            new SettingDescriptor
            {
                Key = "open",
                Label = "Toplu yeniden adlandırmayı aç",
                Kind = SettingKind.Action,
                Description = "Dosya adlarını önizleyerek topluca değiştirin; işlemi pencere kapanmadan geri alabilirsiniz.",
                Execute = Open
            }
        ];
    }

    public string Id => "power-rename";
    public string Name => "Toplu Yeniden Adlandırma";
    public string Category => "Gezgin";
    public string Description => "Dosya adlarında metin veya düzenli ifade arayıp topluca değiştirin; değişiklikleri önizleyin ve geri alın.";
    public string? Warning => "Dosyalar aynı klasörde yeniden adlandırılır. İşlem, hedef adlar çakışıyorsa uygulanmaz.";
    public bool IsActive => _isActive;
    public IReadOnlyList<SettingDescriptor> Settings => _settings;

    public void Initialize(IModuleContext context) => _featureUi = (context as IFeatureUiContext)?.FeatureUi;
    public void Activate()
    {
        _isActive = true;
        _explorerVerb.Install();
    }

    public void Deactivate()
    {
        _isActive = false;
        _explorerVerb.Uninstall();
    }

    public void Apply() { }

    public bool HandleCommand(string command, IReadOnlyList<string> arguments)
    {
        if (!string.Equals(command, "--power-rename", StringComparison.OrdinalIgnoreCase)) return false;
        var selectedFiles = arguments.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (selectedFiles.Length == 0) throw new FileNotFoundException("Toplu yeniden adlandırma için dosya seçimi gerekli.");
        Open(selectedFiles);
        return true;
    }

    private void Open() => Open(null);

    private void Open(IEnumerable<string>? files)
    {
        if (_featureUi is null) throw new InvalidOperationException("React arayüz altyapısı kullanılamıyor.");
        _ = new PowerRenameWindow(_featureUi, files);
    }
}

internal sealed class PowerRenameWindow
{
    private readonly IFeatureUiWindow _window;
    private readonly string? _undoStorePath;
    private readonly List<RenameItem> _items = [];
    private List<RenameOperation> _lastOperation;
    private string _find = "";
    private string _replace = "";
    private bool _regex;
    private bool _caseSensitive;
    private string _status = "Dosyaları ekleyerek başlayın.";

    internal PowerRenameWindow(IFeatureUiService featureUi, IEnumerable<string>? initialFiles = null, string? undoStorePath = null)
    {
        _undoStorePath = undoStorePath;
        _window = featureUi.Open("power-rename-" + Guid.NewGuid().ToString("N"), "power-rename", "Toplu Yeniden Adlandırma",
            new FeatureUiWindowOptions(900, 640, ShowInTaskbar: true, Resizable: true));
        _window.MessageReceived += OnMessage;
        _lastOperation = RenameUndoStore.Load(_undoStorePath);
        if (RenameUndoStore.CanUndo(_lastOperation)) _status = $"Önceki işlemden {_lastOperation.Count} dosya geri alınabilir.";
        if (initialFiles is not null) AddFiles(initialFiles);
        RefreshPreview();
    }

    private void OnMessage(string command, JsonElement payload)
    {
        switch (command)
        {
            case "power-rename.choose": ChooseFiles(); break;
            case "power-rename.update": UpdateOptions(payload); break;
            case "power-rename.apply": ApplyRename(); break;
            case "power-rename.undo": UndoRename(); break;
            case "power-rename.close": _window.Close(); break;
        }
    }

    private void UpdateOptions(JsonElement payload)
    {
        if (payload.TryGetProperty("find", out var find)) _find = find.GetString() ?? "";
        if (payload.TryGetProperty("replace", out var replace)) _replace = replace.GetString() ?? "";
        if (payload.TryGetProperty("regex", out var regex)) _regex = regex.ValueKind == JsonValueKind.True;
        if (payload.TryGetProperty("caseSensitive", out var sensitive)) _caseSensitive = sensitive.ValueKind == JsonValueKind.True;
        RefreshPreview();
    }

    private void ChooseFiles()
    {
        var dialog = new OpenFileDialog { Title = "Yeniden adlandırılacak dosyaları seçin", Multiselect = true,
            CheckFileExists = true, CheckPathExists = true };
        if (dialog.ShowDialog() != true) return;
        _items.Clear();
        AddFiles(dialog.FileNames);
        RefreshPreview();
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            _items.Add(new RenameItem(path));
    }

    private void RefreshPreview()
    {
        var previews = _items.Select(item => (Item: item, Result: BuildTarget(item.FilePath))).ToList();
        foreach (var (item, result) in previews)
        {
            item.NewName = result.TargetName;
            item.Status = result.Error ?? (result.TargetName == item.OriginalName ? "Değişiklik yok" : "Hazır");
        }

        foreach (var group in previews.Where(entry => entry.Result.Error is null && entry.Result.TargetPath != entry.Item.FilePath)
                     .GroupBy(entry => entry.Result.TargetPath, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            foreach (var (item, _) in group) item.Status = "Çakışma: aynı hedef ad";

        var sources = previews.Select(entry => Path.GetFullPath(entry.Item.FilePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (item, result) in previews.Where(entry => entry.Result.Error is null && entry.Result.TargetPath != entry.Item.FilePath))
            if (File.Exists(result.TargetPath) && !sources.Contains(result.TargetPath)) item.Status = "Çakışma: hedef dosya var";

        var changes = _items.Count(item => item.Status == "Hazır");
        var errors = _items.Count(item => item.Status.StartsWith("Çakışma", StringComparison.Ordinal) || item.Status.StartsWith("Geçersiz", StringComparison.Ordinal));
        _status = $"{_items.Count} dosya · {changes} değişiklik · {errors} sorun";
        PushState(changes > 0 && errors == 0);
    }

    private (string TargetPath, string TargetName, string? Error) BuildTarget(string path)
    {
        var originalName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(_find)) return (path, originalName, "Arama metni girin");

        try
        {
            var comparison = _caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var newName = _regex
                ? Regex.Replace(originalName, _find, _replace, _caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250))
                : originalName.Replace(_find, _replace, comparison);
            if (newName.Length == 0 || newName is "." or ".." || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return (path, newName, "Geçersiz dosya adı");
            return (Path.Combine(Path.GetDirectoryName(path)!, newName), newName, null);
        }
        catch (ArgumentException) { return (path, originalName, "Geçersiz düzenli ifade"); }
        catch (RegexMatchTimeoutException) { return (path, originalName, "Düzenli ifade zaman aşımına uğradı"); }
    }

    private void ApplyRename()
    {
        var operations = _items.Where(item => item.Status == "Hazır")
            .Select(item => new RenameOperation(item.FilePath, Path.Combine(Path.GetDirectoryName(item.FilePath)!, item.NewName))).ToList();
        try
        {
            RenameTransaction.Apply(operations);
            _lastOperation = operations;
            foreach (var operation in operations)
            {
                var item = _items.First(entry => StringComparer.OrdinalIgnoreCase.Equals(entry.FilePath, operation.Source));
                item.FilePath = operation.Target;
            }
            _status = $"{operations.Count} dosya yeniden adlandırıldı.";
            try { RenameUndoStore.Save(_lastOperation, _undoStorePath); }
            catch (Exception exception) { _status += " Geri alma kaydı yazılamadı: " + exception.Message; }
            RefreshPreview();
        }
        catch (Exception exception)
        {
            _status = "Yeniden adlandırma başarısız: " + exception.Message;
            PushState(false);
        }
    }

    private void UndoRename()
    {
        try
        {
            RenameTransaction.Apply(_lastOperation.Select(item => new RenameOperation(item.Target, item.Source)).ToList());
            foreach (var item in _items)
            {
                var operation = _lastOperation.FirstOrDefault(entry => StringComparer.OrdinalIgnoreCase.Equals(entry.Target, item.FilePath));
                if (operation is not null) item.FilePath = operation.Source;
            }
            _lastOperation.Clear();
            _status = "Son yeniden adlandırma geri alındı.";
            try { RenameUndoStore.Clear(_undoStorePath); }
            catch (Exception exception) { _status += " Geri alma kaydı temizlenemedi: " + exception.Message; }
            RefreshPreview();
        }
        catch (Exception exception)
        {
            _status = "Geri alma başarısız: " + exception.Message;
            PushState(false);
        }
    }

    private void PushState(bool canApply) => _window.SetState(new
    {
        find = _find,
        replace = _replace,
        regex = _regex,
        caseSensitive = _caseSensitive,
        status = _status,
        canApply,
        canUndo = RenameUndoStore.CanUndo(_lastOperation),
        rows = _items.Select(item => new { item.OriginalName, item.NewName, item.Status }).ToArray()
    });

    private sealed class RenameItem(string filePath)
    {
        public string FilePath { get; set; } = filePath;
        public string OriginalName => Path.GetFileName(FilePath);
        public string NewName { get; set; } = Path.GetFileName(filePath);
        public string Status { get; set; } = "Hazır";
    }
}
internal sealed record RenameOperation(string Source, string Target);

internal static class RenameUndoStore
{
    private const int MaximumOperations = 10_000;
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinForge", "PowerRename", "undo.json");

    public static List<RenameOperation> Load(string? path = null)
    {
        try
        {
            var storePath = ResolvePath(path);
            var info = new FileInfo(storePath);
            if (!info.Exists || info.Length is <= 0 or > 2_000_000) return [];
            var operations = System.Text.Json.JsonSerializer.Deserialize<List<RenameOperation>>(File.ReadAllText(storePath));
            if (operations is null || operations.Count is 0 or > MaximumOperations ||
                operations.Any(operation => !IsValid(operation))) return [];
            return operations;
        }
        catch { return []; }
    }

    public static bool CanUndo(IReadOnlyList<RenameOperation> operations)
    {
        if (operations.Count is 0 or > MaximumOperations) return false;
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var operation in operations)
        {
            if (!IsValid(operation) || !targets.Add(Path.GetFullPath(operation.Target)) ||
                !sources.Add(Path.GetFullPath(operation.Source))) return false;
        }
        return operations.All(operation => File.Exists(operation.Target) &&
            (!File.Exists(operation.Source) || targets.Contains(Path.GetFullPath(operation.Source))));
    }

    public static void Save(IReadOnlyList<RenameOperation> operations, string? path = null)
    {
        if (operations.Count is 0 or > MaximumOperations || operations.Any(operation => !IsValid(operation)))
            throw new InvalidDataException("Geri alma kaydı geçersiz veya çok büyük.");

        var storePath = ResolvePath(path);
        var directory = Path.GetDirectoryName(storePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"undo-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, System.Text.Json.JsonSerializer.Serialize(operations, JsonOptions));
            File.Move(temporaryPath, storePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static void Clear(string? path = null)
    {
        var storePath = ResolvePath(path);
        if (File.Exists(storePath)) File.Delete(storePath);
    }

    private static string ResolvePath(string? path) => Path.GetFullPath(path ?? DefaultPath);

    private static bool IsValid(RenameOperation? operation)
    {
        if (operation is null || string.IsNullOrWhiteSpace(operation.Source) || string.IsNullOrWhiteSpace(operation.Target) ||
            !Path.IsPathFullyQualified(operation.Source) || !Path.IsPathFullyQualified(operation.Target)) return false;
        try
        {
            return !StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(operation.Source), Path.GetFullPath(operation.Target)) &&
                StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(Path.GetFullPath(operation.Source)),
                    Path.GetDirectoryName(Path.GetFullPath(operation.Target)));
        }
        catch { return false; }
    }
}

internal static class RenameTransaction
{
    public static void Apply(IReadOnlyList<RenameOperation> operations)
    {
        var changes = operations.Where(item => !StringComparer.OrdinalIgnoreCase.Equals(item.Source, item.Target)).ToList();
        if (changes.Count == 0) return;
        if (changes.Select(item => Path.GetFullPath(item.Target)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != changes.Count)
            throw new IOException("Birden fazla dosya aynı hedef ada dönüşüyor.");
        if (changes.Any(item => !File.Exists(item.Source)))
            throw new FileNotFoundException("Kaynak dosyalardan biri artık mevcut değil.");

        var sources = changes.Select(item => Path.GetFullPath(item.Source)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (changes.Any(item => File.Exists(item.Target) && !sources.Contains(Path.GetFullPath(item.Target))))
            throw new IOException("Hedef dosyalardan biri zaten mevcut.");

        var staged = new List<(RenameOperation Operation, string Temporary)>();
        try
        {
            foreach (var operation in changes)
            {
                var temporary = Path.Combine(Path.GetDirectoryName(operation.Source)!, $".winforge-{Guid.NewGuid():N}.tmp");
                File.Move(operation.Source, temporary);
                staged.Add((operation, temporary));
            }

            foreach (var (operation, temporary) in staged)
                File.Move(temporary, operation.Target);
        }
        catch (Exception failure)
        {
            var rollbackFailures = new List<Exception>();
            foreach (var (operation, temporary) in staged.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(temporary)) File.Move(temporary, operation.Source);
                    else if (File.Exists(operation.Target)) File.Move(operation.Target, operation.Source);
                }
                catch (Exception rollbackFailure)
                {
                    rollbackFailures.Add(rollbackFailure);
                }
            }

            if (rollbackFailures.Count > 0)
                throw new IOException("İşlem başarısız oldu ve otomatik geri alma tamamlanamadı. Dosyaları geçici .winforge-* adlarından kontrol edin.",
                    new AggregateException(failure, new AggregateException(rollbackFailures)));
            throw new IOException("İşlem başarısız oldu; dosyalar başlangıç adlarına geri alındı.", failure);
        }
    }
}

