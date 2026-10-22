using System.Reflection;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForge.App;
using ForgeApplication = WinForge.App.App;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class Program
{
    private static readonly string TestDirectory = Path.Combine(Path.GetTempPath(), "WinForge.Tests", Guid.NewGuid().ToString("N"));
    private static int _passed;

    [STAThread]
    private static int Main(string[] args)
    {
        Directory.CreateDirectory(TestDirectory);
        var app = new ForgeApplication();
        app.InitializeComponent();
        try
        {
            TestSettings();
            TestLifecycle();
            AltSnapChecks.Run(TestDirectory, Check, args.Contains("--altsnap-smoke"));
            TestInterface(app);
            Console.WriteLine($"PASS: {_passed} regression checks.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            SetProperty(app, nameof(ForgeApplication.IsExiting), true);
            foreach (Window window in app.Windows.Cast<Window>().ToArray()) window.Close();
            var cleanupPath = Path.GetFullPath(TestDirectory);
            var testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WinForge.Tests")) + Path.DirectorySeparatorChar;
            if (cleanupPath.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(cleanupPath, true);
        }
    }

    private static void TestSettings()
    {
        var path = Path.Combine(TestDirectory, "settings.json");
        var store = new SettingsStore(path);
        store.Enabled.Add("EXAMPLE");
        store.For("example")["Value"] = "42";
        store.Save();
        var loaded = new SettingsStore(path);
        Check(loaded.Enabled.Contains("example"), "Enabled module IDs survive reload, ignoring case");
        Check(loaded.For("EXAMPLE")["value"] == "42", "Settings survive reload, ignoring case");
        File.WriteAllText(path, "{broken");
        Check(new SettingsStore(path).Values.Count == 0, "Corrupt JSON does not prevent startup");
        File.WriteAllText(path, "{\"Enabled\":null,\"Values\":{\"broken\":null,\"valid\":{\"key\":\"value\"}}}");
        Check(new SettingsStore(path).For("valid")["key"] == "value", "Null JSON sections do not discard valid settings");
        File.WriteAllText(path, "{\"Values\":{\"valid\":{\"key\":null}}}");
        Check(new SettingsStore(path).For("valid")["key"] == "", "Null setting values normalize to empty text");
    }

    private static ModuleHost CreateHost(string folder, string name)
    {
        var store = new SettingsStore(Path.Combine(TestDirectory, name + ".json"));
        return new ModuleHost(store, new FileLogger(Path.Combine(TestDirectory, "test.log")), new FakeHotkeys(), folder, _ => { });
    }

    private static void TestLifecycle()
    {
        var folder = Path.Combine(TestDirectory, "fixtures");
        Directory.CreateDirectory(folder);
        File.Copy(typeof(Program).Assembly.Location, Path.Combine(folder, "WinForge.Modules.Fixture.dll"));
        var host = CreateHost(folder, "lifecycle");
        host.Load();
        Check(host.Modules.Count == 1, "Module discovery loads a valid plugin");
        var module = (FixtureModule)host.Modules.Single();
        var settings = host.SettingsOf(module);
        settings.Set("invalid-bool", "invalid");
        Check(settings.GetBool("invalid-bool", true), "Invalid boolean uses its fallback");
        settings.Set("invalid-bool", "no");
        Check(!settings.GetBool("invalid-bool", true), "Legacy false boolean is supported");
        settings.Set("number", "invalid");
        Check(settings.GetInt("number", 17) == 17, "Invalid integer uses its fallback");
        host.Apply(module);
        Check(module.ApplyCount == 0, "Inactive modules do not apply settings");
        host.SetEnabled(module, true);
        Check(module.IsActive && new SettingsStore(Path.Combine(TestDirectory, "lifecycle.json")).Enabled.Contains(module.Id), "Activation persists enabled state");
        host.Apply(module);
        Check(module.ApplyCount == 1, "Active modules apply settings");
        module.FailApply = true;
        host.Apply(module);
        Check(host.ErrorOf(module) is not null, "Apply failures are reported");
        module.FailApply = false;
        host.Apply(module);
        Check(host.ErrorOf(module) is null, "Successful apply clears the error");
        module.FailDeactivate = true;
        ExpectFailure(() => host.SetEnabled(module, false));
        Check(module.IsActive && module.DeactivateCount == 1, "Failed deactivation is not repeated or falsely shown as disabled");
        module.FailDeactivate = false;
        host.SetEnabled(module, false);
        Check(!module.IsActive && !new SettingsStore(Path.Combine(TestDirectory, "lifecycle.json")).Enabled.Contains(module.Id), "Deactivation persists disabled state");
        module.FailActivate = true;
        ExpectFailure(() => host.SetEnabled(module, true));
        Check(!module.ResourceAllocated && host.ErrorOf(module) is not null, "Failed activation releases resources before IsActive is set");
        module.FailActivate = false;
        host.SetEnabled(module, true);
        host.StopAll();
        module.FailActivate = true;
        host.StartEnabled();
        Check(!module.ResourceAllocated && host.ErrorOf(module) is not null, "Startup activation failure also releases resources");
        host.Load();
        Check(host.ErrorOf(host.Modules.Single()) is null, "Reload clears stale errors");
    }

    private static void TestInterface(ForgeApplication app)
    {
        var host = CreateHost(Path.Combine(AppContext.BaseDirectory, "modules"), "ui");
        host.Load();
        Check(host.Modules.Count >= 24 && host.Modules.Any(module => module.Id == "altsnap"), "All shipped modules including AltSnap load and initialize");
        Check(host.Modules.Select(module => module.Id).Distinct().Count() == host.Modules.Count, "Module IDs are unique");
        foreach (var module in host.Modules)
        {
            Check(module.Settings.Select(setting => setting.Key).Distinct().Count() == module.Settings.Count, $"Unique settings: {module.Name}");
            foreach (var setting in module.Settings)
            {
                if (setting.Kind == SettingKind.Number)
                    Check(Convert.ToInt32(setting.Default) >= setting.Min && Convert.ToInt32(setting.Default) <= setting.Max, $"Numeric default within range: {module.Id}/{setting.Key}");
                if (setting.Kind == SettingKind.Choice)
                    Check(setting.Options.Contains(setting.Default as string), $"Choice default exists: {module.Id}/{setting.Key}");
            }
        }
        SetProperty(app, nameof(ForgeApplication.Host), host);
        var window = new MainWindow();
        window.Show();
        Pump(window);
        var list = Find<ListBox>(window, "ModuleList");
        var search = Find<TextBox>(window, "SearchBox");
        var categories = Find<ListBox>(window, "CategoryList");
        Check(list.Items.Count == host.Modules.Count && list.SelectedItem is ModuleVm, "Initial view selects a module");
        foreach (var category in categories.Items.Cast<string>().ToArray())
        {
            categories.SelectedItem = category;
            Check(list.Items.Count > 0 && list.SelectedItem is ModuleVm selected && (category == "Tümü" || selected.Category == category), $"Category updates selection: {category}");
        }
        categories.SelectedIndex = 0;
        search.Text = "   TASKBAR   ";
        Check(list.Items.Count > 0 && list.Items.Cast<ModuleVm>().All(module => module.Category == "Taskbar"), "Search matches category and trims whitespace");
        search.Text = "no-such-module-123456";
        Check(list.Items.Count == 0 && list.SelectedItem is null && Find<StackPanel>(window, "EmptyState").Visibility == Visibility.Visible, "Empty results clear stale details and show recovery");
        Click(Descendants<Button>(Find<StackPanel>(window, "EmptyState")).Single());
        Check(search.Text == "" && categories.SelectedIndex == 0 && list.Items.Count == host.Modules.Count, "Reset filters restores the module list");
        var activeOnly = Find<CheckBox>(window, "ActiveOnlyToggle");
        activeOnly.IsChecked = true;
        Click(activeOnly);
        Check(list.Items.Count == 0, "Active filter hides inactive modules");
        activeOnly.IsChecked = false;
        Click(activeOnly);
        foreach (var vm in list.Items.Cast<ModuleVm>().ToArray())
        {
            list.SelectedItem = vm;
            Pump(window);
            Check(Find<StackPanel>(window, "DetailPanel").Children.Count >= 4, $"Settings render: {vm.Name}");
        }
        list.SelectedItem = list.Items.Cast<ModuleVm>().Single(module => module.Module.Id == "altsnap");
        Pump(window);
        var altSnapDetail = Find<StackPanel>(window, "DetailPanel");
        Check(Descendants<ComboBox>(altSnapDetail).Count() >= 14, "AltSnap action lists use compact dropdowns");
        Check(Descendants<Button>(altSnapDetail).Count() == 6, "AltSnap exposes configuration, zone tray, INI, reload and help actions");
        Capture(window, "altsnap.png");
        list.SelectedItem = list.Items.Cast<ModuleVm>().First(module => module.Module.Id == "net-speed");
        Pump(window);
        var detailScroll = Find<ScrollViewer>(window, "DetailScrollViewer");
        detailScroll.ScrollToEnd();
        Pump(window);
        Check(detailScroll.VerticalOffset > 0, "Detail settings scroll to their final options");
        list.SelectedItem = list.Items[0];
        Pump(window);
        Check(detailScroll.VerticalOffset == 0, "Selecting another module resets detail scrolling");
        list.SelectedItem = list.Items.Cast<ModuleVm>().First(module => module.Module.Id == "net-speed");
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        Pump(window);
        Check(list.ActualWidth >= 360 && Find<StackPanel>(window, "DetailPanel").ActualWidth > 300, "Minimum window size keeps both columns usable");
        foreach (var control in Descendants<CheckBox>(list))
        {
            var origin = control.TranslatePoint(new Point(), list);
            Check(origin.X + control.ActualWidth <= list.ActualWidth, $"Module switch remains visible at minimum width ({origin.X}, {list.ActualWidth})");
        }
        Capture(window, "compact.png");
        window.Width = 1180;
        window.Height = 820;
        Click(Find<Button>(window, "AppearanceButton"));
        Check(Find<TextBlock>(window, "PageTitle").Text == "Görünüm", "Appearance opens from the toolbar");
        var originalTone = Appearance.ToneName;
        var originalAccent = Appearance.AccentName;
        foreach (var tone in Appearance.Tones)
        {
            SetProperty(null, nameof(Appearance.ToneName), tone.Name, typeof(Appearance));
            Invoke(window, "ApplyAppearance");
            Invoke(window, "RenderDetail");
            Pump(window);
            Check(((SolidColorBrush)app.Resources["TextBrush"]).Color != ((SolidColorBrush)app.Resources["CardBrush"]).Color, $"Theme resources update: {tone.Name}");
            if (tone.Name == "Açık") Capture(window, "light.png");
            foreach (var accent in Appearance.Accents)
            {
                SetProperty(null, nameof(Appearance.AccentName), accent.Name, typeof(Appearance));
                Appearance.Apply();
                Check(Contrast(app, "MutedBrush", "CardBrush") >= 4.5 && Contrast(app, "AccentBrush", "CardBrush") >= 4.5,
                    $"Readable text contrast: {tone.Name}/{accent.Name}");
                Check(Contrast(app, "SwitchThumbBrush", "SwitchTrackBrush") >= 3,
                    $"Visible switch contrast: {tone.Name}/{accent.Name}");
            }
        }
        SetProperty(null, nameof(Appearance.ToneName), originalTone, typeof(Appearance));
        SetProperty(null, nameof(Appearance.AccentName), originalAccent, typeof(Appearance));
        Invoke(window, "ApplyAppearance");
        search.Text = "İnternet";
        Pump(window);
        Check(Find<TextBlock>(window, "PageTitle").Text != "Görünüm" && list.SelectedItem is ModuleVm, "Search leaves appearance and selects the matching module");
        Click(Find<Button>(window, "ClearSearchButton"));
        Pump(window);
        Check(search.Text == "" && list.Items.Count == host.Modules.Count, "Clear-search button restores results");
        var selectedContainer = list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) as ListBoxItem;
        Check(selectedContainer is not null && selectedContainer.TranslatePoint(new Point(), list).Y >= 0
            && selectedContainer.TranslatePoint(new Point(), list).Y + selectedContainer.ActualHeight <= list.ActualHeight,
            "Selected module remains in view after clearing search");
        Capture(window, "main.png");
        TestAwakeExpiry(host, window);
        TestToggleUi(app);
    }

    private static void TestAwakeExpiry(ModuleHost host, Window window)
    {
        const uint continuous = 0x80000000;
        const uint systemRequired = 0x00000001;
        var module = host.Modules.Single(module => module.Id == "awake");
        host.SettingsOf(module).Set("duration", "30 dakika");
        var timerField = module.GetType().GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        try
        {
            host.SetEnabled(module, true);
            Check((SetThreadExecutionState(continuous) & systemRequired) != 0, "Awake prevents sleep on the activation thread");
            host.Apply(module);
            var timer = (DispatcherTimer)timerField.GetValue(module)!;
            timer.Interval = TimeSpan.FromMilliseconds(20);
            var elapsed = Stopwatch.StartNew();
            while (timerField.GetValue(module) is not null && elapsed.Elapsed < TimeSpan.FromSeconds(2))
            {
                Thread.Sleep(10);
                Pump(window);
            }
            Check(timerField.GetValue(module) is null, "Awake duration expires and stops its timer");
            Check((SetThreadExecutionState(continuous) & systemRequired) == 0, "Awake expiry clears the original thread's sleep prevention");
            Check(module.IsActive, "Awake module remains enabled after timed expiry");
            host.Apply(module);
            host.SetEnabled(module, false);
            Check(timerField.GetValue(module) is null, "Disabling Awake cancels its pending timer");
        }
        finally
        {
            if (module.IsActive) host.SetEnabled(module, false);
            SetThreadExecutionState(continuous);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    private static void TestToggleUi(ForgeApplication app)
    {
        var host = CreateHost(Path.Combine(TestDirectory, "fixtures"), "ui-toggle");
        host.Load();
        SetProperty(app, nameof(ForgeApplication.Host), host);
        var window = new MainWindow();
        window.Show();
        Pump(window);
        var list = Find<ListBox>(window, "ModuleList");
        var toggle = Descendants<CheckBox>(Find<StackPanel>(window, "DetailPanel")).First();
        toggle.IsChecked = true;
        Click(toggle);
        Check(host.Modules.Single().IsActive && ((ModuleVm)list.SelectedItem).IsActive, "Detail switch updates module and list");
        var detail = Find<StackPanel>(window, "DetailPanel");
        var settings = host.SettingsOf(host.Modules.Single());
        var settingToggle = Descendants<CheckBox>(detail).Last();
        settingToggle.IsChecked = true;
        Click(settingToggle);
        Check(settings.GetBool("toggle"), "Toggle setting saves its value");
        Descendants<Slider>(detail).Single().Value = 7;
        Check(settings.GetInt("number") == 7, "Numeric setting saves its value");
        var text = Descendants<TextBox>(detail).Single();
        text.Text = "  saved text  ";
        text.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        Check(settings.GetString("text") == "saved text", "Text setting trims and saves on focus loss");
        Descendants<RadioButton>(detail).Last().IsChecked = true;
        Check(settings.GetString("choice") == "Two", "Choice setting saves its value");
        var activeOnly = Find<CheckBox>(window, "ActiveOnlyToggle");
        activeOnly.IsChecked = true;
        Click(activeOnly);
        Pump(window);
        toggle = Descendants<CheckBox>(list).Single();
        toggle.IsChecked = false;
        Click(toggle);
        Check(!host.Modules.Single().IsActive && list.Items.Count == 0 && list.SelectedItem is null, "List switch updates active filter and removes stale selection");
    }

    private static T Find<T>(Window window, string name) where T : FrameworkElement => (T)window.FindName(name);

    private static double Contrast(ForgeApplication app, string foreground, string background)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color color) => 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        var first = Luminance(((SolidColorBrush)app.Resources[foreground]).Color);
        var second = Luminance(((SolidColorBrush)app.Resources[background]).Color);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }
    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static void Invoke(object target, string method) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, null);
    private static void SetProperty(object? target, string name, object value, Type? type = null) => (type ?? target!.GetType()).GetProperty(name)!.SetValue(target, value);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void Pump(Window window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static void Capture(Window window, string name)
    {
        Pump(window);
        var visual = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var folder = Path.Combine(AppContext.BaseDirectory, "artifacts");
        Directory.CreateDirectory(folder);
        using var file = File.Create(Path.Combine(folder, name));
        encoder.Save(file);
    }

    private static void ExpectFailure(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected lifecycle failure did not occur.");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        _passed++;
        Console.WriteLine("PASS: " + name);
    }
}

public sealed class FixtureModule : ITweakModule
{
    public string Id => "fixture";
    public string Name => "Test modülü";
    public string Category => "Test";
    public string Description => "Yan etkisiz arayüz ve yaşam döngüsü testi.";
    public string? Warning => null;
    public IReadOnlyList<SettingDescriptor> Settings { get; } =
    [
        new() { Key = "toggle", Label = "Test toggle", Default = false },
        new() { Key = "number", Label = "Test number", Kind = SettingKind.Number, Min = 0, Max = 10, Default = 3 },
        new() { Key = "text", Label = "Test text", Kind = SettingKind.Text, Default = "" },
        new() { Key = "choice", Label = "Test choice", Kind = SettingKind.Choice, Options = ["One", "Two"], Default = "One" }
    ];
    public bool IsActive { get; private set; }
    public bool ResourceAllocated { get; private set; }
    public bool FailActivate { get; set; }
    public bool FailDeactivate { get; set; }
    public bool FailApply { get; set; }
    public int ApplyCount { get; private set; }
    public int DeactivateCount { get; private set; }
    public void Initialize(IModuleContext context) { }
    public void Activate()
    {
        ResourceAllocated = true;
        if (FailActivate) throw new InvalidOperationException("Activation failed");
        IsActive = true;
    }
    public void Deactivate()
    {
        DeactivateCount++;
        if (FailDeactivate) throw new InvalidOperationException("Deactivation failed");
        ResourceAllocated = false;
        IsActive = false;
    }
    public void Apply()
    {
        if (FailApply) throw new InvalidOperationException("Apply failed");
        ApplyCount++;
    }
}

internal sealed class FakeHotkeys : IHotkeyService
{
    public IDisposable Register(uint modifiers, uint virtualKey, Action callback) => new Registration();
    private sealed class Registration : IDisposable { public void Dispose() { } }
}

