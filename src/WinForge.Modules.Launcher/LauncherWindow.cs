using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.Launcher;

internal sealed class LauncherWindow : Window
{
    private const int ResultLimit = 12;
    private const int ContentResultLimit = 8;
    private const int ContentFileScanLimit = 2000;
    private const int MaxContentCharacters = 128 * 1024;
    private const int RecentItemsLimit = 20;
    private static readonly TimeSpan ContentSearchDebounce = TimeSpan.FromMilliseconds(300);
    private static readonly HashSet<string> SearchableTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".log", ".csv", ".tsv", ".json", ".jsonc", ".xml", ".html", ".htm",
        ".ini", ".cfg", ".conf", ".toml", ".yaml", ".yml", ".rst", ".cs", ".cpp", ".h", ".hpp", ".js",
        ".jsx", ".ts", ".tsx", ".css", ".scss", ".sql", ".py", ".ps1", ".sh", ".bat", ".cmd", ".config"
    };
    private static readonly LauncherEntry[] BuiltInCommands =
    {
        new("Komut · Windows Ayarları", "ms-settings:", SearchTerms: "ayarlar settings control panel"),
        new("Komut · Ekran ayarları", "ms-settings:display", SearchTerms: "ekran display monitor resolution"),
        new("Komut · Ses ayarları", "ms-settings:sound", SearchTerms: "ses sound audio volume"),
        new("Komut · Wi-Fi ayarları", "ms-settings:network-wifi", SearchTerms: "wifi wi-fi kablosuz internet network"),
        new("Komut · Bluetooth ayarları", "ms-settings:bluetooth", SearchTerms: "bluetooth cihaz devices"),
        new("Komut · Yüklü uygulamalar", "ms-settings:appsfeatures", SearchTerms: "uygulamalar apps program uninstall installed"),
        new("Komut · Başlangıç uygulamaları", "ms-settings:startupapps", SearchTerms: "başlangıç startup apps açılış"),
        new("Komut · Windows Update", "ms-settings:windowsupdate", SearchTerms: "güncelleme update windows güncelle"),
        new("Komut · Pano ayarları", "ms-settings:clipboard", SearchTerms: "pano clipboard history geçmiş"),
        new("Komut · Gizlilik ayarları", "ms-settings:privacy", SearchTerms: "gizlilik privacy permissions izinler"),
        new("Komut · Fare ayarları", "ms-settings:mousetouchpad", SearchTerms: "fare mouse touchpad"),
        new("Komut · Güç ve uyku", "ms-settings:powersleep", SearchTerms: "güç power sleep uyku battery pil")
    };
    private static readonly TimeSpan IndexCacheDuration = TimeSpan.FromMinutes(2);
    private readonly Action<string> _launch;
    private readonly Action<string> _copyResult;
    private readonly IQuickActionService? _quickActions;
    private readonly ObservableCollection<LauncherEntry> _results = new();
    private readonly TextBox _searchBox;
    private readonly ListBox _resultList;
    private string[] _additionalRoots;
    private IReadOnlySet<string> _allowedExtensions;
    private bool _contentSearchEnabled;
    private bool _recentItemsEnabled;
    private CancellationTokenSource? _contentSearchCancellation;
    private List<LauncherEntry> _index = new();
    private DateTime _lastIndexRefreshUtc = DateTime.MinValue;
    private bool _indexing;
    private int _indexGeneration;

    public LauncherWindow(Action<string> launch, Action<string> copyResult,
        IEnumerable<string>? additionalRoots = null, IReadOnlySet<string>? allowedExtensions = null,
        bool contentSearchEnabled = false, bool recentItemsEnabled = false, IQuickActionService? quickActions = null)
    {
        _launch = launch;
        _copyResult = copyResult;
        _additionalRoots = additionalRoots?.ToArray() ?? Array.Empty<string>();
        _allowedExtensions = allowedExtensions ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _contentSearchEnabled = contentSearchEnabled;
        _recentItemsEnabled = recentItemsEnabled;
        _quickActions = quickActions;
        Title = "WinForge Hızlı Başlatıcı";
        Width = 680;
        Height = 480;
        MinWidth = 520;
        MinHeight = 320;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = true;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(25, 29, 38));
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        AllowsTransparency = false;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = "WinForge · Hızlı Başlatıcı",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(140, 195, 255)),
            Margin = new Thickness(2, 0, 0, 10)
        };
        root.Children.Add(heading);

        _searchBox = new TextBox
        {
            FontSize = 22,
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 12),
            Background = new SolidColorBrush(Color.FromRgb(38, 44, 56)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(74, 88, 110)),
            CaretBrush = Brushes.White
        };
        Grid.SetRow(_searchBox, 1);
        root.Children.Add(_searchBox);

        _resultList = new ListBox
        {
            ItemsSource = _results,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            Padding = new Thickness(0),
            DisplayMemberPath = nameof(LauncherEntry.Name)
        };
        Grid.SetRow(_resultList, 2);
        root.Children.Add(_resultList);

        var hint = new TextBlock
        {
            Text = "↑ ↓ seç · Enter aç / sonucu kopyala · = hesapla · Esc kapat",
            Foreground = new SolidColorBrush(Color.FromRgb(155, 165, 180)),
            FontSize = 11,
            Margin = new Thickness(2, 10, 0, 0)
        };
        Grid.SetRow(hint, 3);
        root.Children.Add(hint);
        Content = root;

        _searchBox.TextChanged += (_, _) => UpdateResults();
        _searchBox.PreviewKeyDown += OnSearchKeyDown;
        _resultList.PreviewKeyDown += OnSearchKeyDown;
        _resultList.MouseDoubleClick += (_, _) => LaunchSelected();
        Deactivated += (_, _) => Hide();
        Closed += (_, _) => CancelContentSearch();
    }

    public void Open()
    {
        _searchBox.Clear();
        UpdateResults();
        if (!IsVisible) Show();
        WindowState = WindowState.Normal;
        Activate();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            _searchBox.Focus();
            _searchBox.SelectAll();
        }));
        if (DateTime.UtcNow - _lastIndexRefreshUtc > IndexCacheDuration) _ = RefreshIndexAsync();
    }

    private async Task RefreshIndexAsync()
    {
        if (_indexing) return;
        _indexing = true;
        var generation = _indexGeneration;
        try
        {
            var roots = _additionalRoots.ToArray();
            var extensions = new HashSet<string>(_allowedExtensions, StringComparer.OrdinalIgnoreCase);
            var recentEnabled = _recentItemsEnabled;
            var entries = await Task.Run(() =>
            {
                var indexedEntries = BuildIndex(roots, allowedExtensions: extensions);
                if (recentEnabled) indexedEntries.AddRange(BuildRecentItems(RecentItemsLimit));
                return indexedEntries;
            });
            if (generation == _indexGeneration)
            {
                _index = entries;
                _lastIndexRefreshUtc = DateTime.UtcNow;
                if (IsVisible) UpdateResults();
            }
        }
        catch
        {
            // The launcher remains usable for Start Menu apps and direct paths if indexing fails.
        }
        finally
        {
            _indexing = false;
            if (IsVisible && generation != _indexGeneration) _ = RefreshIndexAsync();
        }
    }

    public void ApplyIndexOptions(IEnumerable<string> additionalRoots, IReadOnlySet<string> allowedExtensions,
        bool contentSearchEnabled, bool recentItemsEnabled)
    {
        _additionalRoots = additionalRoots.ToArray();
        _allowedExtensions = new HashSet<string>(allowedExtensions, StringComparer.OrdinalIgnoreCase);
        _contentSearchEnabled = contentSearchEnabled;
        _recentItemsEnabled = recentItemsEnabled;
        CancelContentSearch();
        _indexGeneration++;
        _lastIndexRefreshUtc = DateTime.MinValue;
        if (IsVisible) _ = RefreshIndexAsync();
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs args)
    {
        switch (args.Key)
        {
            case Key.Escape:
                Hide();
                args.Handled = true;
                break;
            case Key.Enter:
                LaunchSelected();
                args.Handled = true;
                break;
            case Key.Down when sender == _searchBox && _results.Count > 0:
                _resultList.Focus();
                _resultList.SelectedIndex = Math.Max(0, _resultList.SelectedIndex);
                args.Handled = true;
                break;
            case Key.Up when sender == _resultList && _resultList.SelectedIndex <= 0:
                _searchBox.Focus();
                args.Handled = true;
                break;
        }
    }

    private void LaunchSelected()
    {
        if (_resultList.SelectedItem is not LauncherEntry selected) return;
        Hide();
        if (selected.IsCalculation)
        {
            _copyResult(selected.Path);
            return;
        }
        if (selected.ProviderAction is { } quickAction)
        {
            quickAction.Execute();
            return;
        }
        _launch(selected.Path);
    }

    private void UpdateResults()
    {
        CancelContentSearch();
        var query = _searchBox.Text.Trim();
        if (query.StartsWith('=') && CalculatorParser.TryEvaluate(query[1..], out var result))
        {
            _results.Clear();
            _results.Add(new LauncherEntry($"Sonucu kopyala: {query[1..].Trim()} = {result}", result, true));
            _resultList.SelectedIndex = 0;
            return;
        }

        if (LauncherTargetResolver.TryResolve(query, out var directResult, out var directTarget))
        {
            _results.Clear();
            _results.Add(new LauncherEntry(directResult, directTarget));
            _resultList.SelectedIndex = 0;
            return;
        }

        var providerEntries = _quickActions?.Search(query).Select(action =>
            new LauncherEntry(action.Name, action.Id, SearchTerms: action.SearchTerms, ProviderAction: action)) ?? Enumerable.Empty<LauncherEntry>();
        var matches = _index.Concat(BuiltInCommands).Concat(providerEntries)
            .Select(entry => (Entry: entry, Score: Score(entry.SearchText, query)))
            .Where(match => match.Score >= 0)
            .OrderByDescending(match => match.Score)
            .ThenByDescending(match => match.Entry.IsRecent)
            .ThenBy(match => match.Entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(ResultLimit)
            .Select(match => match.Entry)
            .ToArray();

        _results.Clear();
        foreach (var match in matches) _results.Add(match);
        _resultList.SelectedIndex = _results.Count > 0 ? 0 : -1;
        if (_contentSearchEnabled && query.Length >= 3) ScheduleContentSearch(query);
    }

    private void ScheduleContentSearch(string query)
    {
        var cancellation = new CancellationTokenSource();
        _contentSearchCancellation = cancellation;
        var token = cancellation.Token;
        var candidates = _index.ToArray();
        _ = SearchContentsAsync(query, candidates, token);
    }

    private async Task SearchContentsAsync(string query, IReadOnlyList<LauncherEntry> candidates, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ContentSearchDebounce, cancellationToken);
            var results = await Task.Run(() => SearchFileContents(candidates, query, ContentResultLimit,
                ContentFileScanLimit, MaxContentCharacters, cancellationToken), cancellationToken);
            if (cancellationToken.IsCancellationRequested || !IsVisible ||
                !string.Equals(_searchBox.Text.Trim(), query, StringComparison.Ordinal)) return;

            foreach (var result in results)
            {
                if (_results.Any(existing => string.Equals(existing.Path, result.Path, StringComparison.OrdinalIgnoreCase))) continue;
                _results.Add(result);
                if (_results.Count >= ResultLimit + ContentResultLimit) break;
            }
        }
        catch (OperationCanceledException) { }
    }

    private void CancelContentSearch()
    {
        var cancellation = _contentSearchCancellation;
        _contentSearchCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    internal static List<LauncherEntry> SearchFileContents(IEnumerable<LauncherEntry> candidates, string query,
        int resultLimit = 8, int fileScanLimit = 2000, int maxCharacters = 128 * 1024,
        CancellationToken cancellationToken = default)
    {
        if (query.Trim().Length < 3 || resultLimit <= 0 || fileScanLimit <= 0 || maxCharacters <= 0)
            return new List<LauncherEntry>();

        var results = new List<LauncherEntry>(Math.Min(resultLimit, ContentResultLimit));
        var scanned = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (scanned >= fileScanLimit || results.Count >= resultLimit) break;
            if (!File.Exists(candidate.Path) || !SearchableTextExtensions.Contains(Path.GetExtension(candidate.Path))) continue;
            scanned++;
            try
            {
                var info = new FileInfo(candidate.Path);
                if (info.Length <= 0 || info.Length > maxCharacters * 4L) continue;
                using var stream = new FileStream(candidate.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                var buffer = new char[maxCharacters];
                var length = 0;
                while (length < buffer.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = reader.Read(buffer, length, buffer.Length - length);
                    if (read == 0) break;
                    length += read;
                }
                var content = new string(buffer, 0, length);
                if (content.IndexOf('\0') >= 0) continue;
                var matchIndex = content.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
                if (matchIndex < 0) continue;

                var start = Math.Max(0, matchIndex - 48);
                var count = Math.Min(120, content.Length - start);
                var snippet = string.Join(' ', content.Substring(start, count).Split((char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries));
                var name = Path.GetFileName(candidate.Path);
                results.Add(new LauncherEntry($"İçerik · {name}  —  …{snippet}…", candidate.Path, false, $"{name} {candidate.Path}"));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException or ArgumentException) { }
        }
        return results;
    }

    private static int Score(string name, string query)
    {
        if (query.Length == 0) return 0;
        if (name.Equals(query, StringComparison.CurrentCultureIgnoreCase)) return 1000;
        if (name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)) return 800;
        if (name.Contains(query, StringComparison.CurrentCultureIgnoreCase)) return 500;

        var queryIndex = 0;
        var gaps = 0;
        for (var nameIndex = 0; nameIndex < name.Length && queryIndex < query.Length; nameIndex++)
        {
            if (char.ToUpperInvariant(name[nameIndex]) == char.ToUpperInvariant(query[queryIndex]))
                queryIndex++;
            else if (queryIndex > 0)
                gaps++;
        }

        return queryIndex == query.Length ? 300 - gaps : -1;
    }

    internal static List<LauncherEntry> BuildIndex(IEnumerable<string>? additionalRoots = null, int maxEntries = 25000,
        IReadOnlySet<string>? allowedExtensions = null)
    {
        var startMenuRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var personalRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(profile, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };
        var roots = startMenuRoots.Concat(personalRoots).Concat(additionalRoots ?? Array.Empty<string>()).Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return IndexRoots(roots, maxEntries, startMenuRoots, allowedExtensions);
    }

    internal static List<LauncherEntry> BuildRecentItems(int maxEntries = 20, string? recentDirectory = null)
    {
        if (maxEntries <= 0) return new List<LauncherEntry>();
        recentDirectory ??= Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        if (string.IsNullOrWhiteSpace(recentDirectory) || !Directory.Exists(recentDirectory)) return new List<LauncherEntry>();

        try
        {
            return Directory.EnumerateFiles(recentDirectory)
                .Where(path => Path.GetExtension(path) is var extension &&
                    (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || extension.Equals(".url", StringComparison.OrdinalIgnoreCase)))
                .Select(path =>
                {
                    try
                    {
                        var info = new FileInfo(path);
                        var displayName = Path.GetFileNameWithoutExtension(path);
                        if (string.IsNullOrWhiteSpace(displayName) || !info.Exists) return null;
                        return new LauncherEntry($"Son kullanılan · {displayName}", path, false,
                            $"{displayName} {path}", true);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
                })
                .Where(entry => entry is not null)
                .OrderByDescending(entry => File.GetLastWriteTimeUtc(entry!.Path))
                .Take(maxEntries)
                .Select(entry => entry!)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new List<LauncherEntry>();
        }
    }

    internal static List<LauncherEntry> IndexRoots(IEnumerable<string> roots, int maxEntries = 25000,
        IEnumerable<string>? shortcutRoots = null, IReadOnlySet<string>? allowedExtensions = null)
    {
        if (maxEntries <= 0) return new List<LauncherEntry>();
        var shortcutRootSet = new HashSet<string>(shortcutRoots ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var entries = new Dictionary<string, LauncherEntry>(StringComparer.CurrentCultureIgnoreCase);

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0 && entries.Count < maxEntries)
            {
                var directory = pending.Pop();
                try
                {
                    foreach (var file in Directory.EnumerateFiles(directory))
                    {
                        var extension = System.IO.Path.GetExtension(file);
                        var name = System.IO.Path.GetFileNameWithoutExtension(file);
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        var isShortcut = extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                                         extension.Equals(".url", StringComparison.OrdinalIgnoreCase);
                        var isStartMenu = shortcutRootSet.Contains(root);
                        if (isStartMenu && !isShortcut) continue;
                        if (!isStartMenu && allowedExtensions is { Count: > 0 } &&
                            !allowedExtensions.Any(allowedExtension => file.EndsWith(allowedExtension, StringComparison.OrdinalIgnoreCase))) continue;
                        var displayName = isShortcut ? name : $"{name}  ·  {Path.GetDirectoryName(file)}";
                        entries.TryAdd(file, new LauncherEntry(displayName, file, false, $"{name} {file}"));
                        if (entries.Count >= maxEntries) break;
                    }

                    if (entries.Count < maxEntries)
                        foreach (var child in Directory.EnumerateDirectories(directory))
                        {
                            try
                            {
                                if (entries.Count >= maxEntries) break;
                                var attributes = File.GetAttributes(child);
                                if ((attributes & FileAttributes.ReparsePoint) == 0 &&
                                    (attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                                {
                                    var folderName = Path.GetFileName(child);
                                    if (!string.IsNullOrWhiteSpace(folderName))
                                        entries.TryAdd(child, new LauncherEntry($"{folderName}  ·  {child}", child, false, $"{folderName} {child}"));
                                    pending.Push(child);
                                }
                            }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
                        }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A missing or inaccessible Start Menu folder should not break the launcher.
                }
            }
        }

        return entries.Values.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    internal sealed record LauncherEntry(string Name, string Path, bool IsCalculation = false, string? SearchTerms = null,
        bool IsRecent = false, QuickAction? ProviderAction = null)
    {
        public string SearchText => SearchTerms ?? Name;
    }

    private sealed class CalculatorParser
    {
        private readonly string _expression;
        private int _position;

        private CalculatorParser(string expression) => _expression = expression;

        public static bool TryEvaluate(string expression, out string result)
        {
            result = string.Empty;
            try
            {
                var parser = new CalculatorParser(expression);
                var value = parser.ParseExpression();
                parser.SkipWhitespace();
                if (parser._position != expression.Length || !double.IsFinite(value)) return false;

                result = value.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (DivideByZeroException)
            {
                return false;
            }
        }

        private double ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (Match('+')) value += ParseTerm();
                else if (Match('-')) value -= ParseTerm();
                else return value;
            }
        }

        private double ParseTerm()
        {
            var value = ParseFactor();
            while (true)
            {
                SkipWhitespace();
                if (Match('*')) value *= ParseFactor();
                else if (Match('/'))
                {
                    var divisor = ParseFactor();
                    if (divisor == 0) throw new DivideByZeroException();
                    value /= divisor;
                }
                else if (Match('%'))
                {
                    var divisor = ParseFactor();
                    if (divisor == 0) throw new DivideByZeroException();
                    value %= divisor;
                }
                else return value;
            }
        }

        private double ParseFactor()
        {
            SkipWhitespace();
            if (Match('+')) return ParseFactor();
            if (Match('-')) return -ParseFactor();
            if (Match('('))
            {
                var value = ParseExpression();
                SkipWhitespace();
                if (!Match(')')) throw new FormatException();
                return value;
            }

            var start = _position;
            var decimalPointSeen = false;
            while (_position < _expression.Length)
            {
                var character = _expression[_position];
                if (char.IsAsciiDigit(character))
                {
                    _position++;
                    continue;
                }
                if (character == '.' && !decimalPointSeen)
                {
                    decimalPointSeen = true;
                    _position++;
                    continue;
                }
                break;
            }

            if (start == _position || !double.TryParse(
                    _expression.AsSpan(start, _position - start),
                    System.Globalization.NumberStyles.AllowDecimalPoint,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var number))
                throw new FormatException();

            return number;
        }

        private bool Match(char expected)
        {
            if (_position >= _expression.Length || _expression[_position] != expected) return false;
            _position++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_position < _expression.Length && char.IsWhiteSpace(_expression[_position])) _position++;
        }
    }
}
