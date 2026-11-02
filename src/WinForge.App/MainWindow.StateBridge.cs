using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using WinForge.Core;

namespace WinForge.App;

public partial class MainWindow
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly string _bridgeSession = Guid.NewGuid().ToString("N");
    private readonly HashSet<string> _changedModules = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _inspectedModules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonNode> _moduleSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(IModulePanel Panel, Action Handler)> _panelSubscriptions = new();
    private long _stateVersion;
    private bool _commandActive;
    private bool _refreshQueued;
    private bool _pageReady;
    private bool _bridgeClosed;
    private bool _fullRefreshNeeded;
    private bool _metadataChanged;
    private bool _appearanceChanged;
    private bool _deferPanels;

    private void SubscribeToModulePanels()
    {
        foreach (var module in App.Instance.Host.Modules)
        {
            if (module is not IModulePanel panel) continue;
            Action handler = () => Dispatcher.BeginInvoke(new Action(() => MarkModuleChanged(module.Id)));
            panel.PanelStateChanged += handler;
            _panelSubscriptions.Add((panel, handler));
        }
    }

    private void BeginPageNavigation()
    {
        _pageReady = false;
        _deferPanels = false;
        _inspectedModules.Clear();
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        string? requestId = null;
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var root = document.RootElement;
            if (root.TryGetProperty("requestId", out var id))
            {
                requestId = id.GetString();
                if (requestId?.Length > 80) throw new InvalidOperationException("Geçersiz arayüz istek kimliği.");
            }
            var command = root.GetProperty("command").GetString() ?? "";
            var payload = root.TryGetProperty("payload", out var value) ? value : default;
            await _commandGate.WaitAsync();
            try
            {
                if (_bridgeClosed || App.Instance.IsExiting) return;
                _commandActive = true;
                using var timing = PerformanceTrace.Measure("bridge.command", command);
                var snapshot = false;
                try
                {
                    switch (command)
                    {
                        case "ui.ready":
                            _deferPanels = payload.ValueKind == JsonValueKind.Object &&
                                payload.TryGetProperty("deferredPanels", out var deferred) && deferred.ValueKind == JsonValueKind.True;
                            _pageReady = true; snapshot = true; break;
                        case "ui.resync": _pageReady = true; snapshot = true; break;
                        case "module.inspect":
                            var inspected = FindModule(payload.GetProperty("moduleId").GetString());
                            _inspectedModules.Add(inspected.Id);
                            MarkModuleChanged(inspected.Id);
                            break;
                        case "module.toggle": await ToggleModule(payload); break;
                        case "setting.update": await UpdateSetting(payload); break;
                        case "setting.execute": ExecuteSetting(payload); break;
                        case "module.command": ExecutePanelCommand(payload); break;
                        case "shortcut.capture-begin": BeginShortcutCapture(); break;
                        case "shortcut.capture-end": EndShortcutCapture(); break;
                        case "shortcut.record-begin": BeginShortcutRecording(); break;
                        case "appearance.update": UpdateAppearance(payload); break;
                        case "system.openLogs": await OpenLogs(); break;
                        case "system.openData": OpenData(); break;
                        case "app.exit": ExitApp(); return;
                        default: throw new InvalidOperationException("Bilinmeyen arayüz komutu.");
                    }
                    await App.Instance.Store.FlushAsync();
                    if (snapshot || _fullRefreshNeeded) SendSnapshot(requestId);
                    else SendPatch(requestId);
                }
                catch (Exception error)
                {
                    App.Instance.Logger.Error("Arayüz komutu işlenemedi.", error);
                    SendSnapshot(requestId, error.Message);
                }
            }
            finally
            {
                _commandActive = false;
                _commandGate.Release();
                QueueRefresh();
            }
        }
        catch (Exception error)
        {
            App.Instance.Logger.Error("Arayüz iletisi okunamadı.", error);
            PostMessage(new { type = "protocol.error", message = error.Message });
        }
    }

    private object BuildModuleState(ITweakModule module)
    {
        using var timing = PerformanceTrace.Measure("bridge.buildModule", module.Id);
        object? panel = null;
        var panelAvailable = module is IModulePanel;
        var panelLoaded = false;
        string? panelError = null;
        var error = App.Instance.Host.ErrorOf(module);
        try
        {
            if (module is IModulePanel modulePanel && (!_deferPanels || _inspectedModules.Contains(module.Id)))
            {
                using var panelTiming = PerformanceTrace.Measure("bridge.readPanel", module.Id);
                panel = modulePanel.GetPanelState();
                panelLoaded = true;
            }
        }
        catch (Exception exception)
        {
            panelError = exception.Message;
            error ??= exception.Message;
            App.Instance.Logger.Error($"Modül paneli okunamadı: {module.Id}", exception);
        }
        return new
        {
            id = module.Id, name = module.Name, category = module.Category,
            description = module.Description, warning = module.Warning, isActive = module.IsActive,
            error, panel, panelAvailable, panelLoaded, panelError,
            settings = module.Settings.Select(setting => new
            {
                key = setting.Key, label = setting.Label, description = setting.Description,
                section = setting.Section, kind = setting.Kind.ToString().ToLowerInvariant(),
                defaultValue = setting.Default, value = ReadSetting(module, setting), min = setting.Min,
                max = setting.Max, options = setting.Options, optionLabels = setting.OptionLabels
            }).ToArray()
        };
    }

    private static object BuildAppearance() => new
    {
        tone = Appearance.ToneName, accent = Appearance.AccentName, glow = Appearance.Glow,
        tones = Appearance.Tones.Select(tone => tone.Name).ToArray(), accents = Appearance.Accents
    };

    private object BuildState()
    {
        using var timing = PerformanceTrace.Measure("bridge.buildState");
        var modules = App.Instance.Host.Modules;
        _moduleSnapshots.Clear();
        return new
        {
            modules = modules.Select(module =>
            {
                var snapshot = JsonSerializer.SerializeToNode(BuildModuleState(module), _jsonOptions)!;
                _moduleSnapshots[module.Id] = snapshot;
                return snapshot;
            }).ToArray(),
            categories = modules.Select(module => WinForge.Core.Localization.Get(module.Category, Appearance.Language)).Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            activeCount = modules.Count(module => module.IsActive), totalCount = modules.Count,
            dataDirectory = AppData.DirectoryPath, persistence = App.Instance.Store.Persistence,
            version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            appearance = BuildAppearance()
        };
    }

    private object BuildPatch()
    {
        var modules = App.Instance.Host.Modules;
        var replacements = new List<JsonNode>();
        var deltas = new List<object>();
        foreach (var module in modules.Where(module => _changedModules.Contains(module.Id)))
        {
            var snapshot = JsonSerializer.SerializeToNode(BuildModuleState(module), _jsonOptions)!;
            if (_moduleSnapshots.TryGetValue(module.Id, out var previous) && JsonStateDelta.Between(previous, snapshot) is { } changes)
            {
                if (changes.Count > 0) deltas.Add(new { id = module.Id, changes });
            }
            else replacements.Add(snapshot);
            _moduleSnapshots[module.Id] = snapshot;
        }
        var patch = new Dictionary<string, object?>
        {
            ["modules"] = replacements, ["moduleChanges"] = deltas,
            ["activeCount"] = modules.Count(module => module.IsActive), ["totalCount"] = modules.Count,
            ["persistence"] = App.Instance.Store.Persistence
        };
        if (_appearanceChanged) patch["appearance"] = BuildAppearance();
        return patch;
    }

    private bool CanSendState => !_bridgeClosed && _pageReady && !App.Instance.IsExiting && UiWebView.CoreWebView2 is not null;

    private void SendSnapshot(string? requestId = null, string? error = null)
    {
        if (!CanSendState) { _fullRefreshNeeded = true; return; }
        var payload = BuildState();
        PostMessage(new { type = error is null ? "state" : "error", protocolVersion = 1, sessionId = _bridgeSession,
            version = ++_stateVersion, requestId, message = error, payload });
        ClearChanges();
    }

    private void SendPatch(string? requestId = null)
    {
        if (!CanSendState) return;
        if (_fullRefreshNeeded) { SendSnapshot(requestId); return; }
        var payload = BuildPatch();
        var previous = _stateVersion;
        PostMessage(new { type = "state.patch", protocolVersion = 1, sessionId = _bridgeSession,
            version = ++_stateVersion, baseVersion = previous, requestId, payload });
        ClearChanges();
    }

    private void ClearChanges()
    {
        _changedModules.Clear(); _metadataChanged = _appearanceChanged = _fullRefreshNeeded = false;
    }

    private void MarkModuleChanged(string id)
    {
        if (_bridgeClosed) return;
        _changedModules.Add(id);
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (_refreshQueued || _commandActive || !CanSendState ||
            (!_metadataChanged && !_appearanceChanged && !_fullRefreshNeeded && _changedModules.Count == 0)) return;
        _refreshQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _refreshQueued = false;
            if (!_commandActive && CanSendState &&
                (_metadataChanged || _appearanceChanged || _fullRefreshNeeded || _changedModules.Count > 0)) SendPatch();
        }), DispatcherPriority.Background);
    }

    internal void RefreshPersistence() { _metadataChanged = true; QueueRefresh(); }
    internal void RefreshState() { _fullRefreshNeeded = true; QueueRefresh(); }

    private void PostMessage(object message)
    {
        if (_bridgeClosed || App.Instance.IsExiting || UiWebView.CoreWebView2 is null) return;
        var json = JsonSerializer.Serialize(message, _jsonOptions);
        PerformanceTrace.Record("bridge.send", 0, bytes: Encoding.UTF8.GetByteCount(json));
        UiWebView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void DisposeStateBridge()
    {
        _bridgeClosed = true;
        foreach (var (panel, handler) in _panelSubscriptions) panel.PanelStateChanged -= handler;
        _panelSubscriptions.Clear(); _changedModules.Clear();
        _moduleSnapshots.Clear();
        _inspectedModules.Clear();
    }
}
