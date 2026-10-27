using WinForge.Core;

namespace WinForge.Modules.WindowControl;

public sealed class WindowControlModule : ITweakModule
{
    private IModuleContext? _context;
    private WindowControlEngine? _engine;
    private static readonly string[] ButtonActions = ["İşlem yok", "Taşı", "Boyutlandır", "Pencere menüsü", "Windows pencere menüsü", "Küçült", "Büyüt / geri yükle", "Tam ekran / geri yükle", "Ortala", "Her zaman üstte", "Kapat", "Arkaya gönder", "Çerçevesiz", "Başlık çubuğuna sar", "Dikey / yatay büyüt", "Diğer pencereleri küçült", "Sessize al", "Uygulamayı sessize al", "Kenara oturt", "Bölgeye yerleştir", "Sonraki üst üste pencere", "Önceki üst üste pencere", "Pencere yığını listesi", "Tüm pencereler listesi", "Programı sonlandır", "Programı duraklat", "Programa devam et"];
    private static readonly string[] WheelActions = ["İşlem yok", "Pencereler arasında geçiş", "Ses düzeyi", "Saydamlık", "Başlık çubuğuna sar", "Arkaya / öne gönder", "Büyüt / küçült", "Yatay kaydır", "Pencereyi yakınlaştır", "Üst üste pencereler"];
    private static readonly string[] ActivationKeys = ["Sol Alt", "Her iki Alt", "Sağ Alt", "Sol Windows", "Her iki Windows", "Sol Ctrl"];
    private static readonly string[] AlternativeKeys = ["Kapalı", "Sol Windows", "Sol Ctrl", "Sağ Ctrl"];
    private static readonly string[] TitlebarModes = ["Kapalı", "Etkinleştirme tuşu olmadan", "Etkinleştirme tuşuyla", "Her zaman"];
    private static readonly IReadOnlyDictionary<string, string> LegacyActionLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["AltTabList"] = "Tüm pencereler listesi",
        ["Mute"] = "Sessize al"
    };
    private static readonly IReadOnlyDictionary<string, WindowAction> ActionValues = new Dictionary<string, WindowAction>
    {
        ["İşlem yok"] = WindowAction.None, ["Taşı"] = WindowAction.Move, ["Boyutlandır"] = WindowAction.Resize,
        ["Tam ekran / geri yükle"] = WindowAction.ToggleFullscreen,
        ["Pencere menüsü"] = WindowAction.ActionMenu, ["Windows pencere menüsü"] = WindowAction.SystemMenu, ["Küçült"] = WindowAction.Minimize,
        ["Büyüt / geri yükle"] = WindowAction.ToggleMaximize, ["Ortala"] = WindowAction.Center,
        ["Her zaman üstte"] = WindowAction.ToggleTopmost, ["Kapat"] = WindowAction.Close,
        ["Arkaya gönder"] = WindowAction.Lower, ["Çerçevesiz"] = WindowAction.ToggleBorder,
        ["Ses düzeyi"] = WindowAction.Volume, ["Saydamlık"] = WindowAction.Opacity,
        ["Başlık çubuğuna sar"] = WindowAction.Roll, ["Dikey / yatay büyüt"] = WindowAction.MaximizeAxis,
        ["Diğer pencereleri küçült"] = WindowAction.MinimizeOthers, ["Sessize al"] = WindowAction.MasterMute,
        ["Uygulamayı sessize al"] = WindowAction.ApplicationMute,
        ["Kenara oturt"] = WindowAction.SideSnap, ["Sonraki üst üste pencere"] = WindowAction.NextStacked,
        ["Bölgeye yerleştir"] = WindowAction.PlaceInZone,
        ["Önceki üst üste pencere"] = WindowAction.PreviousStacked, ["Pencere yığını listesi"] = WindowAction.StackList,
        ["Tüm pencereler listesi"] = WindowAction.WindowList,
        ["Programı sonlandır"] = WindowAction.Kill, ["Programı duraklat"] = WindowAction.Pause, ["Programa devam et"] = WindowAction.Resume,
        ["Pencereler arasında geçiş"] = WindowAction.SwitchWindow, ["Arkaya / öne gönder"] = WindowAction.RaiseLower,
        ["Büyüt / küçült"] = WindowAction.MaximizeMinimize, ["Yatay kaydır"] = WindowAction.HorizontalScroll,
        ["Pencereyi yakınlaştır"] = WindowAction.Zoom, ["Üst üste pencereler"] = WindowAction.CycleStacked
    };

    internal static IReadOnlyList<KeyValuePair<string, WindowAction>> ActionMenuEntries { get; } =
        new[] { WindowAction.ToggleMaximize, WindowAction.Minimize }.Concat(ButtonActions.Select(label => ActionValues[label]).Where(action =>
            action is not (WindowAction.None or WindowAction.Move or WindowAction.Resize or WindowAction.ActionMenu
                or WindowAction.ToggleMaximize or WindowAction.Minimize)))
        .Select(action => ActionValues.First(entry => entry.Value == action)).ToArray();

    public string Id => "window-control";
    public string Name => "Pencere Kontrolü";
    public string Category => "Verimlilik";
    public string Description => "WinForge'un kendi motoruyla Alt + sol tuşla pencereyi taşı, sağ tuşla boyutlandır. Ekran kenarlarına oturtma, pencere kenarlarına mıknatıslanma ve ayarlanabilir fare eylemleri.";
    public string? Warning => "Yönetici pencerelerini yönetmek için WinForge'u yönetici olarak çalıştırın. Otomatik Döşeme aynı pencereleri yeniden yerleştirebilir.";
    public bool IsActive => _engine?.IsRunning is true;
    public IReadOnlyList<SettingDescriptor> Settings { get; } = CreateSettings();

    public void Initialize(IModuleContext context)
    {
        _context = context;
        foreach (var setting in Settings.Where(setting => setting.Kind == SettingKind.Choice))
        {
            var value = context.Settings.GetString(setting.Key);
            if (LegacyActionLabels.TryGetValue(value, out var label) && setting.Options.Contains(label, StringComparer.Ordinal))
                context.Settings.Set(setting.Key, label);
        }
    }

    public void Activate()
    {
        if (IsActive) return;
        var placement = _context as IWindowPlacementContext;
        Func<IntPtr, POINT, bool>? placeInZone = placement is null ? null : new Func<IntPtr, POINT, bool>(placement.TryPlaceWindowInZone);
        Action<IntPtr>? beginZoneDrag = placement is null ? null : new Action<IntPtr>(placement.BeginWindowZoneDrag);
        Func<IntPtr, bool, bool>? endZoneDrag = placement is null ? null : new Func<IntPtr, bool, bool>(placement.EndWindowZoneDrag);
        var engine = new WindowControlEngine(ReadOptions(), exception => _context!.Log.Error("Pencere kontrolü başarısız.", exception),
            placeInZone, beginZoneDrag: beginZoneDrag, endZoneDrag: endZoneDrag);
        try { engine.Start(); _engine = engine; }
        catch { engine.Dispose(); throw; }
    }

    public void Apply() => _engine?.Update(ReadOptions());

    public void Deactivate()
    {
        var engine = _engine;
        _engine = null;
        engine?.Dispose();
    }

    internal WindowControlOptions ReadOptions()
    {
        var settings = _context!.Settings;
        string Choice(string key, string fallback, string[] choices)
        {
            var value = settings.GetString(key, fallback);
            value = LegacyActionLabels.GetValueOrDefault(value, value);
            return choices.Contains(value, StringComparer.Ordinal) ? value : fallback;
        }
        int Number(string key, int fallback, int min, int max) => Math.Clamp(settings.GetInt(key, fallback), min, max);
        var actions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, fallback) in new[] { ("LMB", "Taşı"), ("RMB", "Boyutlandır"), ("MMB", "Pencere menüsü"), ("MB4", "İşlem yok"), ("MB5", "İşlem yok") })
        {
            actions[key] = Choice(key, fallback, ButtonActions);
            actions[key + "B"] = Choice(key + "B", key == "MMB" ? "Tüm pencereler listesi" : "İşlem yok", ButtonActions);
            actions[key + "T"] = Choice(key + "T", key == "MMB" ? "Arkaya gönder" : "İşlem yok", ButtonActions);
            actions[key + "TB"] = Choice(key + "TB", "İşlem yok", ButtonActions);
        }
        actions["Scroll"] = Choice("Scroll", "İşlem yok", WheelActions);
        actions["ScrollB"] = Choice("ScrollB", "Ses düzeyi", WheelActions);
        actions["HScroll"] = Choice("HScroll", "İşlem yok", WheelActions);
        actions["HScrollB"] = Choice("HScrollB", "İşlem yok", WheelActions);
        foreach (var key in new[] { "Scroll", "HScroll" })
        {
            actions[key + "T"] = Choice(key + "T", key == "Scroll" ? "Başlık çubuğuna sar" : "İşlem yok", WheelActions);
            actions[key + "TB"] = Choice(key + "TB", "İşlem yok", WheelActions);
        }
        return new WindowControlOptions(
            Choice("Hotkeys", "Sol Alt", ActivationKeys), Choice("ModKey", "Sol Windows", AlternativeKeys),
            actions.ToDictionary(pair => pair.Key, pair => ActionValues[pair.Value], StringComparer.Ordinal),
            settings.GetBool("AutoFocus"), settings.GetBool("Aero", true),
            Choice("AutoSnap", "Pencerelerin dış kenarları", ["Kapalı", "Ekran ve görev çubuğu", "Pencerelerin dış kenarları", "İç ve dış kenarlar"]),
            Number("SnapThreshold", 20, 0, 100), Number("AeroThreshold", 8, 0, 100), Number("SnapGap", 0, -100, 100),
            Number("AeroHoffset", 50, 1, 99), Number("AeroVoffset", 50, 1, 99),
            settings.GetBool("FullScreen"), settings.GetString("Processes", "mstsc.exe"), settings.GetString("Windows"))
        {
            Hotclicks = settings.GetString("Hotclicks"),
            SmartSnap = settings.GetBool("SmartAero", true),
            StickyResize = Choice("StickyResize", "Kapalı", ["Kapalı", "Shift ile", "Her zaman", "Shift ile alternatif"]),
            ResizeCenter = Choice("ResizeCenter", "Tüm yönler", ["Sağ alt köşe", "Tüm yönler", "Taşı", "En yakın kenar"]),
            CenterFraction = Number("CenterFraction", 24, 0, 100),
            MoveOpacity = Number("MoveTrans", 255, 32, 255),
            AllowFixedResize = settings.GetBool("ResizeAll"),
            SupportMdi = settings.GetBool("MDI", true),
            TitlebarMode = Choice("TTBActions", "Kapalı", TitlebarModes),
            AlphaDelta = Number("AlphaDelta", 64, -128, 127),
            AlphaDeltaShift = Number("AlphaDeltaShift", 8, -128, 127),
            MinimumAlpha = Number("MinAlpha", 32, 1, 255),
            ZoomFraction = Number("ZoomFrac", 16, 1, 255),
            ZoomFractionShift = Number("ZoomFracShift", 64, 1, 255)
        };
    }

    private static IReadOnlyList<SettingDescriptor> CreateSettings()
    {
        var result = new List<SettingDescriptor>
        {
            Choice("Hotkeys", "Etkinleştirme tuşu", "Sol Alt", ActivationKeys, "Klavye"),
            Choice("ModKey", "Alternatif eylem tuşu", "Sol Windows", AlternativeKeys, "Klavye"),
            new() { Key = "Hotclicks", Label = "Tuşsuz fare etkinleştirme", Kind = SettingKind.Text, Default = "", Section = "Klavye", Description = "Basılı tutulacak fare tuşları: orta 04, yan 05 veya 06; boşlukla ayırın." },
            Choice("TTBActions", "Başlık çubuğu eylemleri", "Kapalı", TitlebarModes, "Başlık çubuğu")
        };
        foreach (var (key, label, fallback) in new[] { ("LMB", "Sol tuş", "Taşı"), ("RMB", "Sağ tuş", "Boyutlandır"), ("MMB", "Orta tuş", "Pencere menüsü"), ("MB4", "Yan tuş 4", "İşlem yok"), ("MB5", "Yan tuş 5", "İşlem yok") })
        {
            result.Add(Choice(key, label, fallback, ButtonActions, "Fare eylemleri"));
            result.Add(Choice(key + "B", label + " · alternatif", key == "MMB" ? "Tüm pencereler listesi" : "İşlem yok", ButtonActions, "Alternatif fare eylemleri"));
            result.Add(Choice(key + "T", label, key == "MMB" ? "Arkaya gönder" : "İşlem yok", ButtonActions, "Başlık çubuğu"));
            result.Add(Choice(key + "TB", label + " · alternatif", "İşlem yok", ButtonActions, "Başlık çubuğu · alternatif"));
        }
        foreach (var (key, label) in new[] { ("Scroll", "Dikey tekerlek"), ("HScroll", "Yatay tekerlek") })
        {
            result.Add(Choice(key, label, "İşlem yok", WheelActions, "Fare eylemleri"));
            result.Add(Choice(key + "B", label + " · alternatif", key == "Scroll" ? "Ses düzeyi" : "İşlem yok", WheelActions, "Alternatif fare eylemleri"));
            result.Add(Choice(key + "T", label, key == "Scroll" ? "Başlık çubuğuna sar" : "İşlem yok", WheelActions, "Başlık çubuğu"));
            result.Add(Choice(key + "TB", label + " · alternatif", "İşlem yok", WheelActions, "Başlık çubuğu · alternatif"));
        }
        result.AddRange([
            new() { Key = "AutoFocus", Label = "Taşınan pencereyi öne getir", Default = false, Section = "Yerleşim" },
            new() { Key = "Aero", Label = "Ekran kenarı ve köşelerine oturt", Default = true, Section = "Yerleşim" },
            new() { Key = "SmartAero", Label = "Akıllı oturtma", Default = true, Section = "Yerleşim", Description = "Ekranın karşı kenarına oturmuş pencerenin yanında kalan boşluğu kullanır." },
            Choice("StickyResize", "Bitişik pencereleri birlikte boyutlandır", "Kapalı", ["Kapalı", "Shift ile", "Her zaman", "Shift ile alternatif"], "Yerleşim"),
            Choice("ResizeCenter", "Ortadan boyutlandırma davranışı", "Tüm yönler", ["Sağ alt köşe", "Tüm yönler", "Taşı", "En yakın kenar"], "Yerleşim"),
            Number("CenterFraction", "Merkez bölgesi (%)", 24, 0, 100),
            Number("MoveTrans", "Taşırken opaklık (255: normal)", 255, 32, 255),
            Number("AlphaDelta", "Tekerlek opaklık adımı", 64, -128, 127),
            Number("AlphaDeltaShift", "Shift ile opaklık adımı", 8, -128, 127),
            Number("MinAlpha", "En düşük pencere opaklığı", 32, 1, 255),
            Number("ZoomFrac", "Yakınlaştırma böleni", 16, 1, 255),
            Number("ZoomFracShift", "Shift ile yakınlaştırma böleni", 64, 1, 255),
            new() { Key = "MDI", Label = "MDI alt pencerelerini destekle", Default = true, Section = "Yerleşim" },
            new() { Key = "ResizeAll", Label = "Sabit boyutlu pencereleri de boyutlandır", Default = false, Section = "Yerleşim" },
            Choice("AutoSnap", "Pencere kenarlarına mıknatıslanma", "Pencerelerin dış kenarları", ["Kapalı", "Ekran ve görev çubuğu", "Pencerelerin dış kenarları", "İç ve dış kenarlar"], "Yerleşim"),
            Number("SnapThreshold", "Mıknatıslanma mesafesi (px)", 20, 0, 100),
            Number("AeroThreshold", "Ekran kenarı eşiği (px)", 8, 0, 100),
            Number("SnapGap", "Pencereler arası boşluk (px)", 0, -100, 100),
            Number("AeroHoffset", "Yatay bölme oranı (%)", 50, 1, 99),
            Number("AeroVoffset", "Dikey bölme oranı (%)", 50, 1, 99),
            new() { Key = "FullScreen", Label = "Tam ekran pencerelerde çalış", Default = false, Section = "İstisnalar" },
            new() { Key = "Processes", Label = "Dışlanan uygulamalar", Kind = SettingKind.Text, Default = "mstsc.exe", Section = "İstisnalar", Description = "Virgülle ayrılmış exe adları; * ve ? jokerleri desteklenir." },
            new() { Key = "Windows", Label = "Dışlanan pencereler", Kind = SettingKind.Text, Default = "", Section = "İstisnalar", Description = "Virgülle ayrılmış başlık|sınıf kalıpları; * ve ? jokerleri desteklenir." }
        ]);
        return result;
    }

    private static SettingDescriptor Choice(string key, string label, string fallback, string[] options, string section) =>
        new() { Key = key, Label = label, Default = fallback, Kind = SettingKind.Choice, Options = options, Section = section };
    private static SettingDescriptor Number(string key, string label, int fallback, int min, int max) =>
        new() { Key = key, Label = label, Default = fallback, Kind = SettingKind.Number, Min = min, Max = max, Section = "Yerleşim" };
}

internal enum WindowAction
{
    None, Move, Resize, ActionMenu, SystemMenu, Minimize, ToggleMaximize, ToggleFullscreen, Center, ToggleTopmost, Close, Lower, ToggleBorder, Volume, Opacity,
    Roll, MaximizeAxis, MinimizeOthers, MasterMute, ApplicationMute, SideSnap, PlaceInZone, NextStacked, PreviousStacked, StackList, WindowList, Kill, Pause, Resume,
    SwitchWindow, RaiseLower, MaximizeMinimize, HorizontalScroll, Zoom, CycleStacked
}

internal sealed record WindowControlOptions(string ActivationKey, string AlternativeKey, IReadOnlyDictionary<string, WindowAction> Actions,
    bool AutoFocus, bool EdgeSnap, string MagnetMode, int MagnetDistance, int EdgeDistance, int Gap,
    int HorizontalSplit, int VerticalSplit, bool AllowFullscreen, string ExcludedProcesses, string ExcludedWindows)
{
    internal string Hotclicks { get; init; } = "";
    internal bool SmartSnap { get; init; } = true;
    internal string StickyResize { get; init; } = "Kapalı";
    internal string ResizeCenter { get; init; } = "Tüm yönler";
    internal int CenterFraction { get; init; } = 24;
    internal int MoveOpacity { get; init; } = 255;
    internal bool AllowFixedResize { get; init; }
    internal bool SupportMdi { get; init; } = true;
    internal string TitlebarMode { get; init; } = "Kapalı";
    internal int AlphaDelta { get; init; } = 64;
    internal int AlphaDeltaShift { get; init; } = 8;
    internal int MinimumAlpha { get; init; } = 32;
    internal int ZoomFraction { get; init; } = 16;
    internal int ZoomFractionShift { get; init; } = 64;
}
