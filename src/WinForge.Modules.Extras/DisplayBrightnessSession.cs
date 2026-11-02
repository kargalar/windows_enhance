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

internal sealed class DisplayBrightnessSession : IDisposable
{
    private readonly string _displayTopology = CurrentTopology();
    private static string CurrentTopology() => string.Join(";", DesktopDisplays.Enumerate()
        .OrderBy(display => display.DeviceName).Select(display => $"{display.DeviceName}:{display.Bounds.Left},{display.Bounds.Top},{display.Bounds.Right},{display.Bounds.Bottom}"));
    internal bool MatchesCurrentDisplays => _displayTopology == CurrentTopology();
    private readonly long _discoveredAt = Environment.TickCount64;
    internal bool NeedsBrightnessRediscovery => ShouldRediscoverBrightness(MatchesCurrentDisplays,
        Monitors.Count(monitor => monitor.SupportsBrightness || monitor.VcpBrightness is not null),
        DesktopDisplays.Enumerate().Count, Environment.TickCount64 - _discoveredAt);

    internal static bool ShouldRediscoverBrightness(bool topologyMatches, int supported, int connected, long ageMilliseconds) =>
        !topologyMatches || (supported < connected && ageMilliseconds >= 5000);
    private const uint MonitorDefaultToNearest = 2;
    private readonly List<PhysicalMonitor> _monitors;
    private readonly List<InternalPanelBrightness> _internalPanels;
    private bool _disposed;

    private DisplayBrightnessSession(List<PhysicalMonitor> monitors, IReadOnlyList<InternalPanelBrightness> internalPanels)
    {
        _monitors = monitors;
        _internalPanels = internalPanels.ToList();
    }

    public IReadOnlyList<DisplayBrightnessMonitor> Monitors => _monitors
        .Select(monitor => monitor.Info is null ? null : monitor.Info with
        {
            Current = monitor.Current,
            Contrast = monitor.Contrast?.Snapshot(),
            Volume = monitor.Volume?.Snapshot(),
            InputSource = monitor.InputSource?.Snapshot(),
            ColorPreset = monitor.ColorPreset?.Snapshot(),
            VcpBrightness = monitor.GenericBrightness?.Snapshot()
        })
        .Where(monitor => monitor is not null)
        .Cast<DisplayBrightnessMonitor>()
        .Concat(_internalPanels.Select(panel => new DisplayBrightnessMonitor(panel.Id, panel.Name,
            panel.Levels.Count == 0 ? 0u : panel.Levels.Min(),
            panel.Levels.Count == 0 ? 0u : panel.Levels.Max(), panel.Current, true, IsInternalPanel: true)))
        .ToArray();

    public static DisplayBrightnessSession Discover()
    {
        var monitors = new List<PhysicalMonitor>();
        MonitorEnumCallback callback = (handle, _, _, _) =>
        {
            var displayName = GetDisplayName(handle);
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(handle, out var count) || count == 0) return true;

            var physical = new PhysicalMonitorInfo[count];
            if (!GetPhysicalMonitorsFromHMONITOR(handle, count, physical)) return true;

            for (var index = 0; index < physical.Length; index++)
            {
                var native = physical[index];
                if (native.Handle == IntPtr.Zero) continue;
                try
                {
                    var description = string.IsNullOrWhiteSpace(native.Description)
                        ? $"Fiziksel ekran {index + 1}"
                        : native.Description.Trim();
                    var brightnessSupported = TryReadBrightness(native.Handle, out var minimum, out var current, out var maximum);
                    var supportedCodes = GetCapabilities(native.Handle);
                    var genericBrightness = ReadFeature(native.Handle, 0x10, supportedCodes);
                    var contrast = ReadFeature(native.Handle, 0x12, supportedCodes);
                    var volume = ReadFeature(native.Handle, 0x62, supportedCodes);
                    var inputSource = ReadFeature(native.Handle, 0x60, supportedCodes);
                    var colorPreset = ReadFeature(native.Handle, 0x14, supportedCodes);
                    if (!brightnessSupported && genericBrightness is null && contrast is null && volume is null && inputSource is null && colorPreset is null)
                    {
                        DestroyPhysicalMonitor(native.Handle);
                        continue;
                    }

                    var monitor = new DisplayBrightnessMonitor($"{displayName}:{index}", $"{displayName} · {description}",
                        minimum, maximum, current, brightnessSupported, contrast?.Snapshot(), volume?.Snapshot(),
                        inputSource?.Snapshot(), colorPreset?.Snapshot(), genericBrightness?.Snapshot());
                    monitors.Add(new PhysicalMonitor(monitor, native.Handle, minimum, maximum, current,
                        genericBrightness, contrast, volume, inputSource, colorPreset));
                }
                catch
                {
                    DestroyPhysicalMonitor(native.Handle);
                }
            }

            return true;
        };

        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            foreach (var monitor in monitors) DestroyPhysicalMonitor(monitor.Handle);
            throw new InvalidOperationException("Ekranlar listelenemedi.");
        }

        return new DisplayBrightnessSession(monitors, InternalPanelBrightnessService.Discover());
    }

    public bool SetBrightness(string id, int percent, out int appliedPercent)
    {
        ThrowIfDisposed();
        if (InternalPanelBrightnessService.IsInternalId(id))
        {
            var index = _internalPanels.FindIndex(candidate =>
                string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                appliedPercent = 0;
                return false;
            }
            var panel = _internalPanels[index];
            if (!InternalPanelBrightnessService.SetBrightness(panel, percent, out appliedPercent)) return false;
            _internalPanels[index] = panel with { Current = InternalPanelBrightnessService.MapPercentToLevel(percent, panel.Levels) };
            return true;
        }
        var monitor = FindPhysicalMonitor(id);
        if (monitor is null)
        {
            appliedPercent = 0;
            return false;
        }

        if (monitor.Info?.SupportsBrightness != true)
        {
            if (monitor.GenericBrightness is null)
            {
                appliedPercent = 0;
                return false;
            }
            var genericValue = (uint)Math.Round(Math.Clamp(percent, 0, 100) * monitor.GenericBrightness.Maximum / 100d);
            if (!SetVCPFeature(monitor.Handle, 0x10, genericValue))
            {
                Thread.Sleep(40);
                if (!SetVCPFeature(monitor.Handle, 0x10, genericValue))
                {
                    appliedPercent = monitor.GenericBrightness.Percent;
                    return false;
                }
            }
            monitor.GenericBrightness.CurrentValue = genericValue;
            appliedPercent = monitor.GenericBrightness.Percent;
            return true;
        }

        var value = BrightnessPercent.ToMonitorValue(percent, monitor.Minimum, monitor.Maximum);
        if (!SetMonitorBrightness(monitor.Handle, value))
        {
            Thread.Sleep(40);
            if (!SetMonitorBrightness(monitor.Handle, value))
            {
                var vcpValue = (uint)Math.Round(Math.Clamp(percent, 0, 100) * (monitor.Maximum > 0 ? monitor.Maximum : 100u) / 100d);
                if (SetVCPFeature(monitor.Handle, 0x10, vcpValue))
                {
                    if (monitor.GenericBrightness is not null) monitor.GenericBrightness.CurrentValue = vcpValue;
                    monitor.Current = value;
                    appliedPercent = BrightnessPercent.FromMonitorValue(value, monitor.Minimum, monitor.Maximum);
                    return true;
                }

                appliedPercent = BrightnessPercent.FromMonitorValue(monitor.Current, monitor.Minimum, monitor.Maximum);
                return false;
            }
        }

        monitor.Current = value;
        appliedPercent = BrightnessPercent.FromMonitorValue(value, monitor.Minimum, monitor.Maximum);
        return true;
    }

    internal (int Supported, int Applied, int Failed, int? Brightness) AdjustBrightnessForDevice(string deviceName, int delta)
    {
        ThrowIfDisposed();
        return AdjustBrightness(FindMonitorsForDevice(Monitors, deviceName), delta);
    }

    internal (int Supported, int Applied, int Failed, int? Brightness) AdjustBrightnessForAllDisplays(int delta)
    {
        ThrowIfDisposed();
        return AdjustBrightness(Monitors, delta);
    }

    private (int Supported, int Applied, int Failed, int? Brightness) AdjustBrightness(
        IEnumerable<DisplayBrightnessMonitor> monitors, int delta)
    {
        return ApplyBrightnessToMonitors(monitors, null, delta);
    }

    internal (int Supported, int Applied, int Failed, int? Brightness) SetBrightnessForAllDisplays(int percent) =>
        ApplyBrightnessToMonitors(Monitors, percent, 0);

    private (int Supported, int Applied, int Failed, int? Brightness) ApplyBrightnessToMonitors(
        IEnumerable<DisplayBrightnessMonitor> monitors, int? percent, int delta)
    {
        ThrowIfDisposed();
        return ApplyBrightnessToMonitors(monitors, percent, delta, (id, target) =>
            SetBrightness(id, target, out var applied) ? applied : null);
    }

    // One shared slider must send one shared target, including monitors already at a limit.
    internal static (int Supported, int Applied, int Failed, int? Brightness) ApplyBrightnessToMonitors(
        IEnumerable<DisplayBrightnessMonitor> monitors, int? percent, int delta, Func<string, int, int?> setBrightness)
    {
        var targets = monitors.Where(monitor => monitor.SupportsBrightness || monitor.VcpBrightness is not null).ToArray();
        if (targets.Length == 0) return (0, 0, 0, null);
        var target = Math.Clamp(percent ?? (int)Math.Round(targets.Average(monitor => monitor.Percent)) + delta, 0, 100);
        var applied = 0;
        var total = 0;
        // Some drivers share a DDC/CI transport; serialize writes to avoid dropping a sibling display's command.
        foreach (var monitor in targets)
        {
            try
            {
                if (setBrightness(monitor.Id, target) is not { } actual) continue;
                applied++;
                total += actual;
            }
            catch { /* Continue applying to the other monitors and report this display as failed. */ }
        }
        return (targets.Length, applied, targets.Length - applied,
            applied == 0 ? null : (int)Math.Round(total / (double)applied));
    }

    internal static IReadOnlyList<DisplayBrightnessMonitor> FindMonitorsForDevice(
        IEnumerable<DisplayBrightnessMonitor> monitors, string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return Array.Empty<DisplayBrightnessMonitor>();
        var prefix = deviceName.Trim() + ":";
        return monitors.Where(monitor => monitor.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    internal static bool TryGetCursorDisplayDeviceName(out string deviceName)
    {
        deviceName = string.Empty;
        if (!GetCursorPos(out var cursor)) return false;
        var monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>(), DeviceName = string.Empty };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info) || string.IsNullOrWhiteSpace(info.DeviceName)) return false;
        deviceName = info.DeviceName;
        return true;
    }

    public bool SetFeature(string id, byte code, uint value, out uint appliedValue)
    {
        ThrowIfDisposed();
        var monitor = FindPhysicalMonitor(id);
        if (monitor is null)
        {
            appliedValue = 0;
            return false;
        }

        var feature = monitor.GetFeature(code);
        if (feature is null || !feature.IsValueSupported(value) || !SetVCPFeature(monitor.Handle, code, value))
        {
            if (feature is not null && feature.IsValueSupported(value))
            {
                Thread.Sleep(40);
                if (SetVCPFeature(monitor.Handle, code, value))
                {
                    feature.CurrentValue = value;
                    appliedValue = value;
                    return true;
                }
            }
            appliedValue = feature?.CurrentValue ?? 0;
            return false;
        }

        feature.CurrentValue = value;
        appliedValue = value;
        return true;
    }

    private PhysicalMonitor? FindPhysicalMonitor(string id) => _monitors.FirstOrDefault(monitor =>
        string.Equals(monitor.Info?.Id, id, StringComparison.OrdinalIgnoreCase));

    private static bool TryReadBrightness(IntPtr monitor, out uint minimum, out uint current, out uint maximum)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (GetMonitorBrightness(monitor, out minimum, out current, out maximum) && maximum > minimum)
                return true;
            if (attempt < 2) Thread.Sleep(40);
        }
        minimum = current = maximum = 0;
        return false;
    }

    private static IReadOnlyDictionary<byte, IReadOnlyList<uint>> GetCapabilities(IntPtr monitor)
    {
        if (!GetCapabilitiesStringLength(monitor, out var length) || length is < 2 or > 64 * 1024) return new Dictionary<byte, IReadOnlyList<uint>>();
        var capabilities = new StringBuilder((int)length);
        return CapabilitiesRequestAndCapabilitiesReply(monitor, capabilities, length)
            ? MonitorCapabilitiesParser.Parse(capabilities.ToString())
            : new Dictionary<byte, IReadOnlyList<uint>>();
    }

    private static MutableVcpFeature? ReadFeature(IntPtr monitor, byte code, IReadOnlyDictionary<byte, IReadOnlyList<uint>> supportedCodes)
    {
        if (!supportedCodes.TryGetValue(code, out var supportedValues))
        {
            if (code != 0x10) return null;
            supportedValues = Array.Empty<uint>();
        }
        uint current = 0, maximum = 0;
        var read = false;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (GetVCPFeatureAndVCPFeatureReply(monitor, code, out _, out current, out maximum)) { read = true; break; }
            if (attempt < 2) Thread.Sleep(80);
        }
        if (!read) return null;
        var isContinuous = MonitorCapabilitiesParser.IsContinuousFeature(supportedValues);
        if (isContinuous && maximum == 0) return null;
        if (!isContinuous && supportedValues.Count == 0) return null;
        return new MutableVcpFeature(code, current, isContinuous ? maximum : 0, supportedValues, isContinuous);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var monitor in _monitors) DestroyPhysicalMonitor(monitor.Handle);
        _monitors.Clear();
    }

    private static string GetDisplayName(IntPtr monitor)
    {
        var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
        return GetMonitorInfo(monitor, ref info) && !string.IsNullOrWhiteSpace(info.DeviceName)
            ? info.DeviceName.TrimEnd('\0')
            : "Ekran";
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DisplayBrightnessSession));
    }

    private sealed class PhysicalMonitor(DisplayBrightnessMonitor info, IntPtr handle, uint minimum, uint maximum, uint current,
        MutableVcpFeature? genericBrightness, MutableVcpFeature? contrast, MutableVcpFeature? volume,
        MutableVcpFeature? inputSource, MutableVcpFeature? colorPreset)
    {
        public DisplayBrightnessMonitor? Info { get; } = info;
        public IntPtr Handle { get; } = handle;
        public uint Minimum { get; } = minimum;
        public uint Maximum { get; } = maximum;
        public uint Current { get; set; } = current;
        public MutableVcpFeature? GenericBrightness { get; } = genericBrightness;
        public MutableVcpFeature? Contrast { get; } = contrast;
        public MutableVcpFeature? Volume { get; } = volume;
        public MutableVcpFeature? InputSource { get; } = inputSource;
        public MutableVcpFeature? ColorPreset { get; } = colorPreset;
        public MutableVcpFeature? GetFeature(byte code) => code switch
        {
            0x10 => GenericBrightness,
            0x12 => Contrast,
            0x14 => ColorPreset,
            0x60 => InputSource,
            0x62 => Volume,
            _ => null
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    private sealed class MutableVcpFeature(byte code, uint currentValue, uint maximum,
        IReadOnlyList<uint> supportedValues, bool isContinuous)
    {
        public byte Code { get; } = code;
        public uint CurrentValue { get; set; } = currentValue;
        public uint Maximum { get; } = maximum;
        public IReadOnlyList<uint> SupportedValues { get; } = supportedValues;
        public bool IsContinuous { get; } = isContinuous;
        public int Percent => Maximum == 0 ? 0 : (int)Math.Round(Math.Clamp(CurrentValue, 0, Maximum) * 100d / Maximum);
        public bool IsValueSupported(uint value) => IsContinuous ? value <= Maximum : SupportedValues.Contains(value);
        public DisplayVcpFeature Snapshot() => new(Code, CurrentValue, Maximum, SupportedValues, IsContinuous);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitorInfo
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }

    private delegate bool MonitorEnumCallback(IntPtr monitor, IntPtr deviceContext, IntPtr rect, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(IntPtr deviceContext, IntPtr clipRect, MonitorEnumCallback callback, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("Dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

    [DllImport("Dxva2.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitorInfo[] physicalMonitors);

    [DllImport("Dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorBrightness(IntPtr monitor, out uint minimum, out uint current, out uint maximum);

    [DllImport("Dxva2.dll", SetLastError = true)]
    private static extern bool SetMonitorBrightness(IntPtr monitor, uint brightness);

    [DllImport("Dxva2.dll", SetLastError = true)]
    private static extern bool GetCapabilitiesStringLength(IntPtr monitor, out uint length);

    [DllImport("Dxva2.dll", EntryPoint = "CapabilitiesRequestAndCapabilitiesReply", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr monitor, StringBuilder capabilities, uint length);

    [DllImport("Dxva2.dll", SetLastError = true)]
    private static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code, out uint codeType, out uint currentValue, out uint maximumValue);

    [DllImport("Dxva2.dll", SetLastError = true)]
    private static extern bool SetVCPFeature(IntPtr monitor, byte code, uint newValue);

    [DllImport("Dxva2.dll", EntryPoint = "DestroyPhysicalMonitor", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitor(IntPtr monitor);
}
