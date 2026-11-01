using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.RegressionTests;

/// <summary>Opt-in tray verification restricted to newly created, owned registry entries and icons.</summary>
internal static class TrayIconsLiveChecks
{
    private const string RegistryPath = @"Control Panel\NotifyIconSettings";
    private const string PromotionValue = "IsPromoted";
    private static readonly TimeSpan PlacementTimeout = TimeSpan.FromSeconds(6);

    internal static void Run(string modulePath, string directory, Action<bool, string> check)
    {
        var type = Assembly.LoadFrom(modulePath).GetType("WinForge.Modules.Taskbar.TrayIconsModule", throwOnError: true)!;
        VerifyRegistryLease(type, directory, check);
        using var fixture = new TrayIconFixture();
        var ownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        WaitUntil(() =>
        {
            ownKeys.Clear();
            using var diagnostic = JsonDocument.Parse(fixture.DescribeDiagnostic());
            foreach (var entry in diagnostic.RootElement.GetProperty("OwnRegistryCandidates").EnumerateArray())
                if (entry.GetProperty("RelatedToOwnIcon").GetBoolean()) ownKeys.Add(entry.GetProperty("Key").GetString()!);
            return ownKeys.Count == fixture.Icons.Count;
        }, "Own tray registry entries");
        foreach (var subkey in ownKeys)
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{RegistryPath}\{subkey}", writable: true)
                ?? throw new InvalidOperationException("An own tray entry disappeared.");
            key.SetValue(PromotionValue, 0, RegistryValueKind.DWord);
        }
        var others = ReadOtherPreferences(ownKeys);
        var context = new Context(directory);
        var module = Create(type, ownKeys, context);
        check(module.Settings.Count == 1, "Actual tray module scopes verification to the two own GUID icons only");
        try
        {
            module.Activate();
            check(module.IsActive && ReadPromotion(ownKeys).All(value => value == 0), "Actual tray module captures the own hidden preference baseline");
            SetAll(module, context, promoted: true);
            WaitUntil(() => DistinctBounds(fixture), "Actual tray module promotes both own icons");
            check(ReadPromotion(ownKeys).All(value => value == 1), "Actual tray module promotes two own GUID icons with distinct native rectangles");
            SetAll(module, context, promoted: false);
            WaitUntil(() => SharedBounds(fixture), "Actual tray module hides both own icons");
            check(ReadPromotion(ownKeys).All(value => value == 0), "Actual tray module returns both own icons to the shared overflow-chevron rectangle");
            SetAll(module, context, promoted: true);
            WaitUntil(() => DistinctBounds(fixture), "Repeated own promotion");
            module.Deactivate();
            WaitUntil(() => SharedBounds(fixture), "Own tray module preference restoration");
            check(!module.IsActive && ReadPromotion(ownKeys).All(value => value == 0), "Deactivation restores own original preference and actual hidden placement");
            var preferencesAfter = ReadOtherPreferences(ownKeys);
            check(others.All(pair => preferencesAfter.TryGetValue(pair.Key, out var value) && pair.Value == value),
                "Scoped tray verification preserves every other application's recorded promotion preference");
        }
        catch
        {
            Console.WriteLine("Actual tray module diagnostic: " + fixture.DescribeDiagnostic());
            throw;
        }
        finally { if (module.IsActive) module.Deactivate(); }
    }

    private static void VerifyRegistryLease(Type type, string directory, Action<bool, string> check)
    {
        var keyName = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))).ToString(CultureInfo.InvariantCulture);
        var path = $@"{RegistryPath}\{keyName}";
        using (var existing = Registry.CurrentUser.OpenSubKey(path))
            if (existing is not null) throw new InvalidOperationException("The isolated tray preference key unexpectedly exists.");
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(path))
            {
                key.SetValue("IconGuid", Guid.NewGuid().ToString("B"));
                key.SetValue("ExecutablePath", Path.Combine(directory, "WinForgeRegistryLeaseFixture.exe"));
                key.SetValue("InitialTooltip", "WinForge isolated registry lease " + keyName);
                key.SetValue(PromotionValue, 42L, RegistryValueKind.QWord);
            }
            var context = new Context(directory);
            var module = Create(type, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { keyName }, context);
            try
            {
                module.Activate();
                SetAll(module, context, promoted: true);
                module.Deactivate();
                using (var key = Registry.CurrentUser.OpenSubKey(path, writable: true)!)
                {
                    check(key.GetValueKind(PromotionValue) == RegistryValueKind.QWord && key.GetValue(PromotionValue) is 42L,
                        "Tray preference restoration preserves original registry value type and content");
                    key.DeleteValue(PromotionValue);
                }
                module.Activate();
                module.Deactivate();
                using (var key = Registry.CurrentUser.OpenSubKey(path, writable: true)!)
                    check(!key.GetValueNames().Contains(PromotionValue), "Tray preference restoration preserves an originally absent value");
                module.Activate();
                using (var key = Registry.CurrentUser.OpenSubKey(path, writable: true)!)
                {
                    key.SetValue("IconGuid", Guid.NewGuid().ToString("B"));
                    key.SetValue(PromotionValue, 7, RegistryValueKind.DWord);
                }
                SetAll(module, context, promoted: true);
                module.Deactivate();
                using (var key = Registry.CurrentUser.OpenSubKey(path)!)
                    check(key.GetValue(PromotionValue) is 7, "Tray apply and restoration refuse to modify an entry whose icon identity changed");
            }
            finally { if (module.IsActive) module.Deactivate(); }
        }
        finally { Registry.CurrentUser.DeleteSubKey(path, throwOnMissingSubKey: false); }
    }

    private static ITweakModule Create(Type type, IReadOnlySet<string> scope, IModuleContext context)
    {
        var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(IReadOnlySet<string>)], null)
            ?? throw new InvalidOperationException("The scoped tray module constructor is unavailable.");
        var module = (ITweakModule)constructor.Invoke([scope]);
        module.Initialize(context);
        return module;
    }

    private static void SetAll(ITweakModule module, Context context, bool promoted)
    {
        foreach (var setting in module.Settings) context.Settings.Set(setting.Key, promoted);
        module.Apply();
    }

    private static IReadOnlyList<int?> ReadPromotion(IEnumerable<string> ownKeys) => ownKeys.Select(subkey =>
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{RegistryPath}\{subkey}");
        return key?.GetValue(PromotionValue) as int?;
    }).ToArray();

    private static Dictionary<string, string> ReadOtherPreferences(IReadOnlySet<string> ownKeys)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var root = Registry.CurrentUser.OpenSubKey(RegistryPath);
        foreach (var name in root?.GetSubKeyNames() ?? [])
        {
            if (ownKeys.Contains(name)) continue;
            using var key = root!.OpenSubKey(name);
            if (key is null) continue;
            var present = key.GetValueNames().Contains(PromotionValue, StringComparer.OrdinalIgnoreCase);
            result[name] = present ? key.GetValueKind(PromotionValue) + ":" + JsonSerializer.Serialize(key.GetValue(PromotionValue)) : "Absent";
        }
        return result;
    }

    private static bool DistinctBounds(TrayIconFixture fixture)
    {
        var bounds = fixture.ReadBounds();
        return bounds.Count == 2 && bounds.All(icon => icon.Bounds.Right > icon.Bounds.Left) &&
            (bounds[0].Bounds.Left != bounds[1].Bounds.Left || bounds[0].Bounds.Top != bounds[1].Bounds.Top);
    }

    private static bool SharedBounds(TrayIconFixture fixture)
    {
        var bounds = fixture.ReadBounds();
        return bounds.Count == 2 && bounds[0].Bounds.Left == bounds[1].Bounds.Left && bounds[0].Bounds.Top == bounds[1].Bounds.Top;
    }

    private static void WaitUntil(Func<bool> predicate, string operation)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < PlacementTimeout)
        {
            if (predicate()) return;
            Thread.Sleep(40);
        }
        throw new TimeoutException(operation);
    }

    private sealed class Context(string directory) : IModuleContext
    {
        public IModuleSettings Settings { get; } = new Settings();
        public ILogger Log { get; } = new FileLogger(Path.Combine(directory, "tray-live.log"));
        public IHotkeyService Hotkeys => throw new NotSupportedException("Tray icon verification does not register hotkeys.");
        public void Notify(string message) { }
    }

    private sealed class Settings : IModuleSettings
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
        public bool GetBool(string key, bool fallback = false) => _values.TryGetValue(key, out var value) && value is bool result ? result : fallback;
        public int GetInt(string key, int fallback = 0) => _values.TryGetValue(key, out var value) && value is int result ? result : fallback;
        public string GetString(string key, string fallback = "") => _values.TryGetValue(key, out var value) && value is string result ? result : fallback;
        public void Set(string key, object? value) => _values[key] = value;
    }
}
