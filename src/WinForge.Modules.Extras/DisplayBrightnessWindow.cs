using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.Extras;

internal sealed class DisplayBrightnessWindow
{
    private readonly Dictionary<string, int> _pendingBrightness = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Id, byte Code), uint> _pendingVcpFeatures = new();
    private readonly DispatcherTimer _applyTimer;
    private readonly IModuleSettings? _settings;
    private readonly IFeatureUiWindow _window;
    private List<DisplaySettingsProfile> _profiles;
    private DisplayBrightnessSession? _session;
    private bool _linkLevels = true;
    private bool _isClosed;
    private int _refreshGeneration;
    private string _status = "Ekran bilgileri yükleniyor…";
    private string _profileName = "Yeni profil";
    private string _selectedProfileName = "";
    private PendingColorPreset? _confirmColor;

    public DisplayBrightnessWindow(IModuleSettings? settings, IFeatureUiService featureUi)
    {
        _settings = settings;
        _profiles = DisplaySettingsProfiles.Load(settings?.GetString("profiles", "[]") ?? "[]");
        _window = featureUi.Open("display-brightness-" + Guid.NewGuid().ToString("N"), "display-brightness",
            "Power Display · Ekran Denetimi",
            new FeatureUiWindowOptions(920, 760, ShowInTaskbar: true, Resizable: true));
        _window.MessageReceived += OnMessage;
        _applyTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(180) };
        _applyTimer.Tick += (_, _) => { FlushPendingChanges(); PushState(); };
        PushState();
        Refresh();
    }

    public void Close()
    {
        if (_isClosed) return;
        _isClosed = true;
        _refreshGeneration++;
        FlushPendingChanges();
        _applyTimer.Stop();
        _session?.Dispose();
        _session = null;
        _window.Close();
    }

    public bool IsVisible => _window.IsVisible;
    public void Show() => _window.Show();

    private void OnMessage(string command, JsonElement payload)
    {
        switch (command)
        {
            case "display.close": Close(); break;
            case "display.refresh": Refresh(); break;
            case "display.link-levels": _linkLevels = TryGetBool(payload, "enabled"); PushState(); break;
            case "display.profile-name": _profileName = TryGetString(payload, "name"); PushState(); break;
            case "display.profile-select": SelectProfile(TryGetString(payload, "name")); break;
            case "display.profile-save": SaveProfile(); break;
            case "display.profile-apply": ApplyProfile(); break;
            case "display.profile-delete": DeleteProfile(); break;
            case "display.slider": UpdateSlider(payload); break;
            case "display.selector": SelectFeature(payload); break;
            case "display.color-confirm": ConfirmColorPreset(TryGetBool(payload, "confirm")); break;
        }
    }

    private void SelectProfile(string name)
    {
        _selectedProfileName = name;
        if (!string.IsNullOrEmpty(name)) _profileName = name;
        PushState();
    }

    private void UpdateSlider(JsonElement payload)
    {
        var id = TryGetString(payload, "id");
        var codeValue = TryGetInt(payload, "code");
        var percent = Math.Clamp(TryGetInt(payload, "value"), 0, 100);
        if (string.IsNullOrEmpty(id) || codeValue is < 0 or > 255) return;
        var code = (byte)codeValue;
        if (code == 0x10)
        {
            ScheduleBrightness(id, percent);
            if (_linkLevels)
            {
                foreach (var sibling in _session?.Monitors.Where(monitor => monitor.Id != id &&
                             (monitor.SupportsBrightness || monitor.VcpBrightness is not null)) ?? [])
                    ScheduleBrightness(sibling.Id, percent);
            }
        }
        else if (GetFeature(id, code) is { } feature)
            ScheduleFeature(id, code, (uint)Math.Round(percent * feature.Maximum / 100d, MidpointRounding.AwayFromZero));
        PushState();
    }

    private void SelectFeature(JsonElement payload)
    {
        var id = TryGetString(payload, "id");
        var codeValue = TryGetInt(payload, "code");
        var value = TryGetUInt(payload, "value");
        if (string.IsNullOrEmpty(id) || codeValue is < 0 or > 255 || value is null) return;
        var code = (byte)codeValue;
        if (code == 0x14)
        {
            _confirmColor = new PendingColorPreset(id, value.Value);
            PushState();
            return;
        }
        ScheduleFeature(id, code, value.Value);
        PushState();
    }

    private void ConfirmColorPreset(bool confirmed)
    {
        if (_confirmColor is { } selection && confirmed) ScheduleFeature(selection.Id, 0x14, selection.Value);
        _confirmColor = null;
        PushState();
    }

    private void SaveProfile()
    {
        FlushPendingChanges();
        var name = _profileName.Trim();
        if (name.Length is < 1 or > 48) { _status = "Profil adı 1–48 karakter arasında olmalıdır."; PushState(); return; }
        if (_session is null || _session.Monitors.Count == 0) { _status = "Profil kaydetmek için önce uyumlu bir ekran bulunmalıdır."; PushState(); return; }
        var profile = DisplaySettingsProfiles.Capture(name, _session.Monitors);
        var existing = _profiles.FindIndex(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) _profiles[existing] = profile; else _profiles.Add(profile);
        PersistProfiles();
        _profileName = name;
        _selectedProfileName = name;
        _status = $"‘{name}’ profili {_session.Monitors.Count} ekranın desteklenen denetimleriyle kaydedildi.";
        PushState();
    }

    private void ApplyProfile()
    {
        if (string.IsNullOrEmpty(_selectedProfileName) || _session is null)
        { _status = "Önce uygulanacak bir profil seçin."; PushState(); return; }
        var profile = _profiles.FirstOrDefault(item => item.Name.Equals(_selectedProfileName, StringComparison.OrdinalIgnoreCase));
        if (profile is null) return;
        FlushPendingChanges();
        var missingMonitors = 0;
        var unsupportedValues = 0;
        var requested = 0;
        foreach (var saved in profile.Monitors)
        {
            var monitor = _session.Monitors.FirstOrDefault(current => DisplaySettingsProfiles.Matches(saved, current));
            if (monitor is null) { missingMonitors++; continue; }
            if (saved.Brightness is { } brightness && (monitor.SupportsBrightness || monitor.VcpBrightness is not null))
            { ScheduleBrightness(monitor.Id, brightness); requested++; }
            else if (saved.Brightness is not null) unsupportedValues++;
            ScheduleProfileFeature(monitor, 0x12, saved.Contrast is { } contrast ? (uint)contrast : null, ref requested, ref unsupportedValues, percent: true);
            ScheduleProfileFeature(monitor, 0x62, saved.Volume is { } volume ? (uint)volume : null, ref requested, ref unsupportedValues, percent: true);
            ScheduleProfileFeature(monitor, 0x14, saved.ColorPreset, ref requested, ref unsupportedValues, percent: false);
        }
        var (applied, failed) = FlushPendingChanges();
        _status = $"‘{profile.Name}’ profili: {applied}/{requested} denetim uygulandı; {failed} hata, {missingMonitors} ekran bulunamadı, {unsupportedValues} denetim desteklenmiyor.";
        PushState();
    }

    private void ScheduleProfileFeature(DisplayBrightnessMonitor monitor, byte code, uint? savedValue,
        ref int requested, ref int unsupported, bool percent)
    {
        if (savedValue is null) return;
        var feature = code switch { 0x12 => monitor.Contrast, 0x62 => monitor.Volume, 0x14 => monitor.ColorPreset, _ => null };
        if (feature is null || (percent && !feature.IsContinuous) || (!percent && !feature.SupportedValues.Contains(savedValue.Value)))
        { unsupported++; return; }
        var value = percent
            ? (uint)Math.Round(Math.Clamp(savedValue.Value, 0, 100) * feature.Maximum / 100d, MidpointRounding.AwayFromZero)
            : savedValue.Value;
        ScheduleFeature(monitor.Id, code, value);
        requested++;
    }

    private void DeleteProfile()
    {
        if (string.IsNullOrEmpty(_selectedProfileName)) return;
        var deleted = _selectedProfileName;
        _profiles.RemoveAll(profile => profile.Name.Equals(deleted, StringComparison.OrdinalIgnoreCase));
        PersistProfiles();
        _profileName = "";
        _selectedProfileName = "";
        _status = $"‘{deleted}’ profili silindi.";
        PushState();
    }

    private void PersistProfiles() => _settings?.Set("profiles", JsonSerializer.Serialize(_profiles));

    private void Refresh() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        FlushPendingChanges();
        _session?.Dispose();
        _session = null;
        _status = "Ekranların DDC/CI yetenekleri okunuyor…";
        var generation = ++_refreshGeneration;
        PushState();
        try
        {
            var session = await Task.Run(DisplayBrightnessSession.Discover);
            if (_isClosed || generation != _refreshGeneration) { session.Dispose(); return; }
            _session = session;
            var monitors = session.Monitors;
            if (monitors.Count == 0)
                _status = "Denetlenebilir harici ekran veya Windows WMI parlaklık desteği olan dahili panel bulunamadı. Harici ekran için DDC/CI'yi açıp yenileyin.";
            else
            {
                var externalCount = monitors.Count(monitor => !monitor.IsInternalPanel);
                var internalCount = monitors.Count - externalCount;
                _status = $"{monitors.Count} ekran bulundu: {externalCount} harici DDC/CI ekranı, {internalCount} dahili panel. Yalnızca kullanılabilir denetimler gösterilir.";
            }
            PushState();
        }
        catch (Exception exception)
        {
            if (_isClosed || generation != _refreshGeneration) return;
            _status = "Ekranlar okunamadı: " + exception.Message;
            PushState();
        }
    }

    private DisplayVcpFeature? GetFeature(string monitorId, byte code) => _session?.Monitors
        .FirstOrDefault(monitor => monitor.Id == monitorId) is { } monitor
        ? code switch { 0x12 => monitor.Contrast, 0x14 => monitor.ColorPreset, 0x60 => monitor.InputSource, 0x62 => monitor.Volume, _ => null }
        : null;

    private bool ApplyBrightness(string id, int percent)
    {
        if (_session is null) return false;
        try
        {
            if (!_session.SetBrightness(id, percent, out _))
            {
                _status = InternalPanelBrightnessService.IsInternalId(id)
                    ? "Dahili panel parlaklığı değiştirilemedi. Windows WMI sağlayıcısı ve panel desteğini kontrol edin."
                    : "Bu ekranda parlaklık değiştirilemedi. DDC/CI desteğini ve monitör bağlantısını kontrol edin.";
                return false;
            }
            return true;
        }
        catch (Exception exception) { _status = "Parlaklık uygulanamadı: " + exception.Message; return false; }
    }

    private void ScheduleBrightness(string id, int percent)
    {
        _pendingBrightness[id] = percent;
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    private void ScheduleFeature(string id, byte code, uint value)
    {
        _pendingVcpFeatures[(id, code)] = value;
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    private (int Succeeded, int Failed) FlushPendingChanges()
    {
        _applyTimer.Stop();
        if (_session is null || (_pendingBrightness.Count == 0 && _pendingVcpFeatures.Count == 0)) return (0, 0);
        var succeeded = 0;
        var failed = 0;
        var brightnessRequested = _pendingBrightness.Count;
        foreach (var (id, percent) in _pendingBrightness.ToArray())
        {
            if (ApplyBrightness(id, percent)) succeeded++; else failed++;
        }
        if (brightnessRequested > 0)
            _status = $"Parlaklık: {succeeded}/{brightnessRequested} monitöre uygulandı" +
                (failed > 0 ? $" · {failed} hata. DDC/CI desteğini ve bağlantıyı kontrol edin." : ".");
        _pendingBrightness.Clear();
        foreach (var ((id, code), value) in _pendingVcpFeatures.ToArray())
        {
            try
            {
                if (!_session.SetFeature(id, code, value, out _))
                { _status = $"DDC/CI 0x{code:X2} denetimi uygulanamadı. Monitör bağlantısını ve özellik desteğini kontrol edin."; failed++; }
                else succeeded++;
            }
            catch (Exception exception) { _status = $"DDC/CI 0x{code:X2} denetimi uygulanamadı: {exception.Message}"; failed++; }
        }
        _pendingVcpFeatures.Clear();
        return (succeeded, failed);
    }

    private void PushState() => _window.SetState(new
    {
        status = _status,
        linkLevels = _linkLevels,
        profileName = _profileName,
        selectedProfileName = _selectedProfileName,
        profiles = _profiles.Select(profile => profile.Name).ToArray(),
        confirmColorPreset = _confirmColor is null ? null : new
        {
            id = _confirmColor.Id,
            value = _confirmColor.Value,
            monitorName = _session?.Monitors.FirstOrDefault(monitor => monitor.Id == _confirmColor.Id)?.Name ?? "monitör",
            label = _session?.Monitors.FirstOrDefault(monitor => monitor.Id == _confirmColor.Id)?.ColorPreset?.SupportedValues.FirstOrDefault(value => value == _confirmColor.Value).ToString("X2")
        },
        monitors = _session?.Monitors.Select(monitor => new
        {
            id = monitor.Id,
            name = monitor.Name,
            isInternalPanel = monitor.IsInternalPanel,
            controls = BuildControls(monitor)
        }).ToArray() ?? []
    });

    private object[] BuildControls(DisplayBrightnessMonitor monitor)
    {
        var controls = new List<object>();
        if (monitor.SupportsBrightness || monitor.VcpBrightness is not null)
            controls.Add(new { code = (int)0x10, label = "Parlaklık", type = "slider", value = _pendingBrightness.GetValueOrDefault(monitor.Id, monitor.Percent) });
        AddSliderControl(controls, monitor, 0x12, "Kontrast", monitor.Contrast);
        AddSliderControl(controls, monitor, 0x62, "Monitör sesi", monitor.Volume);
        AddSelectorControl(controls, monitor.Id, 0x60, "Giriş kaynağı", monitor.InputSource);
        AddSelectorControl(controls, monitor.Id, 0x14, "Renk ön ayarı", monitor.ColorPreset);
        if (!monitor.IsInternalPanel)
        {
            var codes = new[] { monitor.SupportsBrightness || monitor.VcpBrightness is not null ? (byte?)0x10 : null,
                monitor.Contrast is not null ? (byte?)0x12 : null, monitor.ColorPreset is not null ? (byte?)0x14 : null,
                monitor.InputSource is not null ? (byte?)0x60 : null, monitor.Volume is not null ? (byte?)0x62 : null }
                .Where(code => code.HasValue).Select(code => code!.Value).Distinct().Order();
            controls.Add(new { code = -1, label = "DDC/CI VCP", type = "info", value = string.Join(" · ", codes.Select(code => $"0x{code:X2}")) });
        }
        return controls.ToArray();
    }

    private void AddSliderControl(List<object> controls, DisplayBrightnessMonitor monitor, byte code, string label, DisplayVcpFeature? feature)
    {
        if (feature is not { IsContinuous: true }) return;
        var currentValue = _pendingVcpFeatures.GetValueOrDefault((monitor.Id, code), feature.Current);
        var percent = feature.Maximum == 0 ? 0 : (int)Math.Round(currentValue * 100d / feature.Maximum, MidpointRounding.AwayFromZero);
        controls.Add(new { code = (int)code, label, type = "slider", value = Math.Clamp(percent, 0, 100) });
    }

    private void AddSelectorControl(List<object> controls, string id, byte code, string label, DisplayVcpFeature? feature)
    {
        if (feature is not { IsContinuous: false, SupportedValues.Count: > 0 }) return;
        var current = _pendingVcpFeatures.GetValueOrDefault((id, code), feature.Current);
        controls.Add(new { code = (int)code, label, type = "select", value = current,
            options = feature.SupportedValues.Distinct().Order().Select(value => new { value, label = $"{label} · 0x{value:X2}" }).ToArray() });
    }

    private sealed record PendingColorPreset(string Id, uint Value);

    private static string TryGetString(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) ? value.GetString() ?? "" : "";
    private static int TryGetInt(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : 0;
    private static uint? TryGetUInt(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) && value.TryGetUInt32(out var number) ? number : null;
    private static bool TryGetBool(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
}
