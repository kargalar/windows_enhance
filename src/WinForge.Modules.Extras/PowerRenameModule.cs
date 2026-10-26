using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class PowerRenameModule : ITweakModule, IAppCommandModule
{
    private readonly SettingDescriptor[] _settings;
    private readonly ExplorerFileVerb _explorerVerb = new("WinForge.PowerRename", "Toplu yeniden adlandır (WinForge)", "--power-rename %*", multiSelect: true);
    private bool _isActive;

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

    public void Initialize(IModuleContext context) { }
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
        new PowerRenameWindow(selectedFiles).ShowDialog();
        return true;
    }

    private static void Open() => new PowerRenameWindow().ShowDialog();
}

internal sealed class PowerRenameWindow : Window
{
    private readonly ObservableCollection<RenamePreview> _previews = new();
    private readonly TextBox _find = new() { MinWidth = 180 };
    private readonly TextBox _replace = new() { MinWidth = 180 };
    private readonly CheckBox _regex = new() { Content = "Düzenli ifade (regex)" };
    private readonly CheckBox _caseSensitive = new() { Content = "Büyük/küçük harfe duyarlı" };
    private readonly DataGrid _grid;
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _apply = new() { Content = "Yeniden adlandır", IsEnabled = false, MinWidth = 132 };
    private readonly Button _undo = new() { Content = "Geri al", IsEnabled = false, MinWidth = 88 };
    private readonly string? _undoStorePath;
    private List<RenameOperation> _lastOperation = new();

    public PowerRenameWindow() : this(null, null) { }

    internal PowerRenameWindow(IEnumerable<string>? initialFiles) : this(initialFiles, null) { }

    internal PowerRenameWindow(IEnumerable<string>? initialFiles, string? undoStorePath)
    {
        _undoStorePath = undoStorePath;
        Title = "Toplu Yeniden Adlandırma";
        Width = 820;
        Height = 560;
        MinWidth = 640;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;

        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            ItemsSource = _previews,
            Margin = new Thickness(0, 12, 0, 0),
            SelectionMode = DataGridSelectionMode.Extended
        };
        _grid.Columns.Add(new DataGridTextColumn { Header = "Mevcut ad", Binding = new System.Windows.Data.Binding(nameof(RenamePreview.OriginalName)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Yeni ad", Binding = new System.Windows.Data.Binding(nameof(RenamePreview.NewName)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Durum", Binding = new System.Windows.Data.Binding(nameof(RenamePreview.Status)), Width = new DataGridLength(170) });

        var choose = new Button { Content = "Dosyaları seç…", MinWidth = 110, Padding = new Thickness(10, 6, 10, 6) };
        choose.Click += (_, _) => ChooseFiles();
        _find.TextChanged += (_, _) => RefreshPreview();
        _replace.TextChanged += (_, _) => RefreshPreview();
        _regex.Checked += (_, _) => RefreshPreview();
        _regex.Unchecked += (_, _) => RefreshPreview();
        _caseSensitive.Checked += (_, _) => RefreshPreview();
        _caseSensitive.Unchecked += (_, _) => RefreshPreview();
        _apply.Click += (_, _) => ApplyRename();
        _undo.Click += (_, _) => UndoRename();

        var form = new Grid { Margin = new Thickness(18) };
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(choose);
        header.Children.Add(_regex);
        header.Children.Add(_caseSensitive);
        Grid.SetRow(header, 0);
        form.Children.Add(header);

        var findRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        findRow.Children.Add(new TextBlock { Text = "Ara", VerticalAlignment = VerticalAlignment.Center, Width = 78 });
        findRow.Children.Add(_find);
        findRow.Children.Add(new TextBlock { Text = "Değiştir", VerticalAlignment = VerticalAlignment.Center, Width = 80, Margin = new Thickness(16, 0, 0, 0) });
        findRow.Children.Add(_replace);
        Grid.SetRow(findRow, 1);
        form.Children.Add(findRow);

        Grid.SetRow(_grid, 3);
        form.Children.Add(_grid);

        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(_undo);
        actions.Children.Add(_apply);
        DockPanel.SetDock(actions, Dock.Right);
        footer.Children.Add(actions);
        footer.Children.Add(_status);
        Grid.SetRow(footer, 4);
        form.Children.Add(footer);

        Content = form;

        _lastOperation = RenameUndoStore.Load(_undoStorePath);
        _undo.IsEnabled = RenameUndoStore.CanUndo(_lastOperation);
        if (_undo.IsEnabled) _status.Text = $"Önceki işlemden {_lastOperation.Count} dosya geri alınabilir.";
        if (initialFiles is not null) AddFiles(initialFiles);
    }

    private void ChooseFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Yeniden adlandırılacak dosyaları seçin",
            Multiselect = true,
            CheckFileExists = true,
            CheckPathExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        _previews.Clear();
        AddFiles(dialog.FileNames);
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(path)) _previews.Add(new RenamePreview(path));
        }
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        var items = _previews.Select(item => (Item: item, Result: BuildTarget(item.FilePath))).ToList();
        foreach (var (item, result) in items)
        {
            item.NewName = result.TargetName;
            item.Status = result.Error ?? (result.TargetName == item.OriginalName ? "Değişiklik yok" : "Hazır");
        }

        foreach (var duplicateGroup in items.Where(x => x.Result.Error is null && x.Result.TargetPath != x.Item.FilePath)
                     .GroupBy(x => x.Result.TargetPath, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            foreach (var (item, _) in duplicateGroup)
                item.Status = "Çakışma: aynı hedef ad";

        var sources = items.Select(x => Path.GetFullPath(x.Item.FilePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (item, result) in items.Where(x => x.Result.Error is null && x.Result.TargetPath != x.Item.FilePath))
            if (File.Exists(result.TargetPath) && !sources.Contains(result.TargetPath))
                item.Status = "Çakışma: hedef dosya var";

        var validChanges = _previews.Where(item => item.Status == "Hazır").ToList();
        var errors = _previews.Count(item => item.Status.StartsWith("Çakışma", StringComparison.Ordinal) || item.Status.StartsWith("Geçersiz", StringComparison.Ordinal));
        _apply.IsEnabled = validChanges.Count > 0 && errors == 0;
        _status.Text = $"{_previews.Count} dosya · {validChanges.Count} değişiklik · {errors} sorun";
    }

    private (string TargetPath, string TargetName, string? Error) BuildTarget(string path)
    {
        var originalName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(_find.Text)) return (path, originalName, "Arama metni girin");

        try
        {
            var comparison = _caseSensitive.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var newName = _regex.IsChecked == true
                ? Regex.Replace(originalName, _find.Text, _replace.Text, _caseSensitive.IsChecked == true ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250))
                : originalName.Replace(_find.Text, _replace.Text, comparison);
            if (newName.Length == 0 || newName is "." or ".." || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return (path, newName, "Geçersiz dosya adı");

            var target = Path.Combine(Path.GetDirectoryName(path)!, newName);
            return (target, newName, null);
        }
        catch (ArgumentException)
        {
            return (path, originalName, "Geçersiz düzenli ifade");
        }
        catch (RegexMatchTimeoutException)
        {
            return (path, originalName, "Düzenli ifade zaman aşımına uğradı");
        }
    }

    private void ApplyRename()
    {
        var operations = _previews.Where(item => item.Status == "Hazır")
            .Select(item => new RenameOperation(item.FilePath, Path.Combine(Path.GetDirectoryName(item.FilePath)!, item.NewName)))
            .ToList();
        try
        {
            RenameTransaction.Apply(operations);
            _lastOperation = operations;
            foreach (var operation in operations)
            {
                var item = _previews.First(x => StringComparer.OrdinalIgnoreCase.Equals(x.FilePath, operation.Source));
                item.FilePath = operation.Target;
            }
            _undo.IsEnabled = _lastOperation.Count > 0;
            _status.Text = $"{operations.Count} dosya yeniden adlandırıldı.";
            try { RenameUndoStore.Save(_lastOperation, _undoStorePath); }
            catch (Exception storeFailure)
            {
                _status.Text += " Geri alma kaydı diske yazılamadı; bu pencere açıkken geri alabilirsiniz.";
                _status.ToolTip = storeFailure.Message;
            }
            RefreshPreview();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Yeniden adlandırma başarısız", MessageBoxButton.OK, MessageBoxImage.Error);
            RefreshPreview();
        }
    }

    private void UndoRename()
    {
        try
        {
            RenameTransaction.Apply(_lastOperation.Select(item => new RenameOperation(item.Target, item.Source)).ToList());
            foreach (var item in _previews)
            {
                var operation = _lastOperation.FirstOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(x.Target, item.FilePath));
                if (operation is not null) item.FilePath = operation.Source;
            }
            _lastOperation.Clear();
            _undo.IsEnabled = false;
            _status.Text = "Son yeniden adlandırma geri alındı.";
            try { RenameUndoStore.Clear(_undoStorePath); }
            catch (Exception storeFailure)
            {
                _status.Text += " Eski geri alma kaydı diskte temizlenemedi.";
                _status.ToolTip = storeFailure.Message;
            }
            RefreshPreview();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Geri alma başarısız", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

internal sealed class RenamePreview(string path) : INotifyPropertyChanged
{
    private string _path = path;
    private string _newName = System.IO.Path.GetFileName(path);
    private string _status = "Hazır";

    public string FilePath
    {
        get => _path;
        set
        {
            if (_path == value) return;
            _path = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OriginalName));
        }
    }

    public string OriginalName => System.IO.Path.GetFileName(FilePath);
    public string NewName
    {
        get => _newName;
        set { if (_newName != value) { _newName = value; OnPropertyChanged(); } }
    }

    public string Status
    {
        get => _status;
        set { if (_status != value) { _status = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
