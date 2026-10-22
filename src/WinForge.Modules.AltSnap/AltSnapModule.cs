using System.Diagnostics;
using System.Globalization;
using WinForge.Core;

namespace WinForge.Modules.AltSnap;

public sealed class AltSnapModule : ITweakModule
{
    private readonly AltSnapRuntime _runtime;
    private readonly List<Binding> _bindings = new();
    private readonly Dictionary<string, string> _baseline = new(StringComparer.Ordinal);
    private IModuleContext? _context;

    private static readonly Dictionary<string, string> Actions = new()
    {
        ["İşlem yok"] = "Nothing",
        ["Taşı"] = "Move",
        ["Boyutlandır"] = "Resize",
        ["Pencere menüsü"] = "Menu",
        ["Küçült"] = "Minimize",
        ["Büyüt / geri yükle"] = "Maximize",
        ["Ortala"] = "Center",
        ["Her zaman üstte"] = "AlwaysOnTop",
        ["Kapat"] = "Close",
        ["Arkaya gönder"] = "Lower",
        ["Çerçevesiz"] = "Borderless",
        ["Başlık çubuğuna sar"] = "Roll",
        ["Dikey / yatay büyüt"] = "MaximizeHV",
        ["Diğer pencereleri küçült"] = "MinAllOther",
        ["Sessize al"] = "Mute",
        ["Kenara oturt"] = "SideSnap",
        ["Sonraki üst üste pencere"] = "NStacked",
        ["Önceki üst üste pencere"] = "PStacked",
        ["Pencere yığını listesi"] = "StackList",
        ["Programı sonlandır"] = "Kill",
        ["Programı duraklat"] = "Pause",
        ["Programa devam et"] = "Resume"
    };

    private static readonly Dictionary<string, string> WheelActions = new()
    {
        ["İşlem yok"] = "Nothing",
        ["Pencereler arasında geçiş"] = "AltTab",
        ["Ses düzeyi"] = "Volume",
        ["Saydamlık"] = "Transparency",
        ["Başlık çubuğuna sar"] = "Roll",
        ["Arkaya / öne gönder"] = "Lower",
        ["Büyüt / küçült"] = "Maximize",
        ["Yatay kaydır"] = "HScroll",
        ["Pencereyi yakınlaştır"] = "Zoom",
        ["Üst üste pencereler"] = "NPStacked"
    };

    public AltSnapModule() : this(new AltSnapRuntime()) { }

    internal AltSnapModule(AltSnapRuntime runtime)
    {
        _runtime = runtime;
        Choice("Hotkeys", "Etkinleştirme tuşu", "Input", "Klavye", "Sol Alt", new()
        {
            ["Sol Alt"] = "A4",
            ["Her iki Alt"] = "A4 A5",
            ["Sağ Alt"] = "A5",
            ["Sol Windows"] = "5B",
            ["Her iki Windows"] = "5B 5C",
            ["Sol Ctrl"] = "A2"
        }, "Sol Alt varsayılandır; Türkçe klavyede AltGr ile yazmayı korur. Özel iki tuşlu kombinasyonları gelişmiş Klavye sekmesinde ayarlayın.");
        Choice("ModKey", "Alternatif eylem tuşu", "Input", "Klavye", "Sol Windows", new()
        {
            ["Kapalı"] = "",
            ["Sol Windows"] = "5B",
            ["Sol Ctrl"] = "A2",
            ["Sağ Ctrl"] = "A3"
        }, "Etkinleştirme tuşuyla birlikte basıldığında alternatif fare eylemlerini kullanır.");
        Text("Hotclicks", "Tuşsuz fare etkinleştirme", "Input", "Klavye", "", "İsteğe bağlı onaltılık kodlar: orta tuş 04, yan tuşlar 05 06. Örneğin 05 ile yan tuşu basılı tutarak çalıştırın.");

        var buttons = new (string Key, string Label, string Default)[]
        {
            ("LMB", "Sol tuş", "Taşı"), ("RMB", "Sağ tuş", "Boyutlandır"),
            ("MMB", "Orta tuş", "Pencere menüsü"), ("MB4", "Yan tuş 4", "İşlem yok"),
            ("MB5", "Yan tuş 5", "İşlem yok")
        };
        foreach (var (key, label, fallback) in buttons)
        {
            Choice(key, label, "Input", "Fare eylemleri", fallback, Actions);
            Choice(key + "B", label + " · alternatif", "Input", "Alternatif fare eylemleri", "İşlem yok", Actions);
        }
        Choice("Scroll", "Dikey tekerlek", "Input", "Fare eylemleri", "İşlem yok", WheelActions);
        Choice("HScroll", "Yatay tekerlek", "Input", "Fare eylemleri", "İşlem yok", WheelActions);
        Choice("ScrollB", "Dikey tekerlek · alternatif", "Input", "Alternatif fare eylemleri", "Ses düzeyi", WheelActions);
        Choice("HScrollB", "Yatay tekerlek · alternatif", "Input", "Alternatif fare eylemleri", "İşlem yok", WheelActions);

        Toggle("AutoFocus", "Taşınan pencereyi öne getir", "General", false);
        Toggle("Aero", "Ekran kenarı ve köşelerine oturt", "General", true);
        Toggle("SmartAero", "Akıllı oturtma", "General", true);
        Choice("AutoSnap", "Pencere kenarlarına mıknatıslanma", "General", "Yerleşim", "Pencerelerin dış kenarları", new()
        {
            ["Kapalı"] = "0",
            ["Ekran ve görev çubuğu"] = "1",
            ["Pencerelerin dış kenarları"] = "2",
            ["İç ve dış kenarlar"] = "3"
        });
        Choice("StickyResize", "Bitişik pencereleri birlikte boyutlandır", "General", "Yerleşim", "Kapalı", new()
        {
            ["Kapalı"] = "0",
            ["Shift ile"] = "1",
            ["Her zaman"] = "2",
            ["Shift ile alternatif"] = "3"
        });
        Choice("ResizeCenter", "Ortadan boyutlandırma davranışı", "General", "Yerleşim", "Tüm yönler", new()
        {
            ["Sağ alt köşe"] = "0",
            ["Tüm yönler"] = "1",
            ["Taşı"] = "2",
            ["En yakın kenar"] = "3"
        });
        Number("CenterFraction", "Merkez bölgesi (%)", "General", 24, 0, 100);
        Number("AeroHoffset", "Yatay bölme oranı (%)", "General", 50, 1, 99);
        Number("AeroVoffset", "Dikey bölme oranı (%)", "General", 50, 1, 99);
        Number("SnapThreshold", "Mıknatıslanma mesafesi (px)", "Advanced", 20, 0, 100);
        Number("AeroThreshold", "Ekran kenarı eşiği (px)", "Advanced", 5, 0, 100);
        Number("SnapGap", "Pencereler arası boşluk (px)", "Advanced", 0, -100, 100);
        Number("MoveTrans", "Taşırken opaklık (255: normal)", "General", 255, 32, 255);
        Toggle("MDI", "MDI alt pencerelerini destekle", "General", true);
        Toggle("ResizeAll", "Sabit boyutlu pencereleri de boyutlandır", "Advanced", false);
        Toggle("FullScreen", "Tam ekran pencerelerde çalış", "Advanced", false);
        Text("Processes", "Dışlanan uygulamalar", "Blacklist", "İstisnalar", "", "Virgülle ayrılmış exe adları. AltSnap'in sistem ve uzak masaüstü istisnaları ilk kurulumda korunur.");
        Text("Windows", "Dışlanan pencereler", "Blacklist", "İstisnalar", "", "Virgülle ayrılmış başlık|sınıf kalıpları; * jokeri kullanılabilir. Tam eşleştirme araçları gelişmiş İstisnalar sekmesindedir.");

        var settings = _bindings.Select(binding => binding.Descriptor).ToList();
        settings.Add(new SettingDescriptor
        {
            Key = "configure",
            Label = "Tüm AltSnap ayarlarını aç",
            Kind = SettingKind.Action,
            Section = "Gelişmiş",
            Description = "Genel, Fare, Klavye, İstisnalar ve Gelişmiş sekmeleri. Modül açık olmalıdır.",
            Execute = () => _runtime.Send(AltSnapNative.OpenConfiguration)
        });
        settings.Add(new SettingDescriptor
        {
            Key = "show-tray",
            Label = "Bölge menüsü için AltSnap simgesini göster",
            Kind = SettingKind.Action,
            Section = "Gelişmiş",
            Description = "AltSnap simgesine sağ tıklayarak bölge yerleşimleri oluşturun, düzenleyin, kaydedin ve yerleşimler arasında geçin.",
            Execute = () => _runtime.Send(AltSnapNative.ShowTray)
        });
        settings.Add(new SettingDescriptor
        {
            Key = "hide-tray",
            Label = "AltSnap simgesini gizle",
            Kind = SettingKind.Action,
            Section = "Gelişmiş",
            Execute = () => _runtime.Send(AltSnapNative.HideTray)
        });
        settings.Add(new SettingDescriptor
        {
            Key = "edit-ini",
            Label = "Gelişmiş INI dosyasını düzenle",
            Kind = SettingKind.Action,
            Section = "Gelişmiş",
            Description = "Bölge yerleşimleri, başlık çubuğu eylemleri, tüm kısayollar ve parametreli eylemler.",
            Execute = OpenConfigurationFile
        });
        settings.Add(new SettingDescriptor
        {
            Key = "reload",
            Label = "Gelişmiş ayarları yeniden oku ve uygula",
            Kind = SettingKind.Action,
            Section = "Gelişmiş",
            Description = "AltSnap iletişim kutusunu veya INI düzenleyicisini kaydettikten sonra kullanın.",
            Execute = Reload
        });
        settings.Add(new SettingDescriptor
        {
            Key = "help",
            Label = "AltSnap kullanım kılavuzunu aç",
            Kind = SettingKind.Action,
            Section = "Gelişmiş",
            Execute = () => OpenFile("AltSnap.txt")
        });
        Settings = settings;
    }

    public string Id => "altsnap";
    public string Name => "AltSnap Pencere Kontrolü";
    public string Category => "Verimlilik";
    public string Description => "Sol Alt + sol tuşla pencerenin her yerinden taşı; sağ tuşla boyutlandır, orta tuşla pencere menüsünü aç. AltSnap 1.68 motoru; çoklu monitör, mıknatıslanma, fare eylemleri, bölgeler ve gelişmiş kısayollar dahil.";
    public string? Warning => "Otomatik Döşeme aynı pencereleri yeniden düzenleyebilir. Yönetici pencereleri için WinForge'u yönetici olarak çalıştırın. AltSnap'in ayrı başlangıç ve yükseltme seçenekleri yerine WinForge'un yaşam döngüsünü kullanın. Gelişmiş ayarlardan sonra 'yeniden oku ve uygula' düğmesine basın.";
    public bool IsActive => _runtime.IsRunning;
    public IReadOnlyList<SettingDescriptor> Settings { get; }

    public void Initialize(IModuleContext context)
    {
        _context = context;
        Synchronize(preservePending: true);
    }

    public void Activate()
    {
        if (IsActive) return;
        AltSnapRuntime.EnsureNoOtherInstance();
        _runtime.Prepare();
        WritePendingSettings();
        _runtime.Start();
        _context?.Log.Info($"AltSnap {AltSnapRuntime.Version} etkin: {_runtime.DirectoryPath}");
    }

    public void Deactivate() => _runtime.Dispose();

    public void Apply()
    {
        if (!IsActive) return;
        WritePendingSettings();
        _runtime.Send(AltSnapNative.ReloadSettings);
    }

    internal void WritePendingSettings()
    {
        var configuration = _runtime.ReadConfiguration();
        foreach (var binding in _bindings)
        {
            var descriptor = binding.Descriptor;
            var value = ReadSetting(descriptor);
            if (_baseline.GetValueOrDefault(descriptor.Key) == value) continue;
            configuration.Set(binding.Section, binding.NativeKey, binding.Encode(value));
        }
        // Keep the child process under WinForge's job and privilege level.
        configuration.Set("Advanced", "AlwaysElevate", "0");
        configuration.Save(_runtime.ConfigurationPath);
        Synchronize();
    }

    private void Synchronize(bool preservePending = false)
    {
        var configuration = _runtime.ReadConfiguration();
        foreach (var binding in _bindings)
        {
            var descriptor = binding.Descriptor;
            var native = configuration.Get(binding.Section, binding.NativeKey, binding.Encode(Convert.ToString(descriptor.Default, CultureInfo.InvariantCulture) ?? ""));
            var value = binding.Decode(native);
            _baseline[descriptor.Key] = value;
            var pending = _context!.Settings.GetString(descriptor.Key, "__missing__");
            var previousNative = _context.Settings.GetString("__native-" + descriptor.Key, "__missing__");
            if ((!preservePending || pending == "__missing__" || pending == previousNative)
                && pending != value)
                _context!.Settings.Set(descriptor.Key, value);
            if (previousNative != value) _context.Settings.Set("__native-" + descriptor.Key, value);
        }
    }

    private string ReadSetting(SettingDescriptor descriptor) => descriptor.Kind switch
    {
        SettingKind.Toggle => _context!.Settings.GetBool(descriptor.Key, descriptor.Default is true).ToString(),
        SettingKind.Number => Math.Clamp(_context!.Settings.GetInt(descriptor.Key, Convert.ToInt32(descriptor.Default)), descriptor.Min, descriptor.Max).ToString(CultureInfo.InvariantCulture),
        _ => _context!.Settings.GetString(descriptor.Key, descriptor.Default as string ?? "")
    };

    private void Reload()
    {
        if (!File.Exists(_runtime.ConfigurationPath)) throw new InvalidOperationException("Önce AltSnap modülünü etkinleştirin.");
        Synchronize();
        if (IsActive) _runtime.Send(AltSnapNative.ReloadSettings);
    }

    private void OpenConfigurationFile() => OpenFile("AltSnap.ini");

    private void OpenFile(string name)
    {
        var path = Path.Combine(_runtime.DirectoryPath, name);
        if (!File.Exists(path)) throw new InvalidOperationException("Önce AltSnap modülünü etkinleştirin.");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void Toggle(string key, string label, string section, bool fallback) => _bindings.Add(new Binding(
        new SettingDescriptor { Key = key, Label = label, Default = fallback, Section = "Yerleşim" }, section, key,
        value => bool.Parse(value) ? "1" : "0", value => (value != "0").ToString()));

    private void Number(string key, string label, string section, int fallback, int min, int max) => _bindings.Add(new Binding(
        new SettingDescriptor { Key = key, Label = label, Kind = SettingKind.Number, Default = fallback, Min = min, Max = max, Section = "Yerleşim" }, section, key,
        value => value, value => value));

    private void Text(string key, string label, string section, string group, string fallback, string description) => _bindings.Add(new Binding(
        new SettingDescriptor { Key = key, Label = label, Kind = SettingKind.Text, Default = fallback, Description = description, Section = group }, section, key,
        value => value, value => value));

    private void Choice(string key, string label, string section, string group, string fallback, Dictionary<string, string> values, string? description = null) => _bindings.Add(new Binding(
        new SettingDescriptor { Key = key, Label = label, Kind = SettingKind.Choice, Default = fallback, Options = values.Keys.ToArray(), Description = description, Section = group }, section, key,
        value => values.GetValueOrDefault(value, value),
        value => values.FirstOrDefault(pair => pair.Value.Equals(value, StringComparison.OrdinalIgnoreCase)).Key ?? value));

    private sealed record Binding(SettingDescriptor Descriptor, string Section, string NativeKey, Func<string, string> Encode, Func<string, string> Decode);
}
