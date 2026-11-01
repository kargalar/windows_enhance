using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using WinForge.Core;

namespace WinForge.Modules.Extras;

internal sealed class EnvironmentVariablesWindow : Window
{
    private readonly IEnvironmentVariableStore _store;
    private readonly ObservableCollection<UserEnvironmentVariable> _entries = new();
    private readonly IFeatureUiSurface _uiSurface;
    private readonly string _descriptionText;
    private string _statusText = "";
    private string[] _originalNames = Array.Empty<string>();
    private readonly AutomaticSettingsSave _autosave;

    public EnvironmentVariablesWindow(UserEnvironmentVariableStore store, IFeatureUiService? featureUi = null)
        : this(store, "Kullanıcı Ortam Değişkenleri",
            "Yalnızca geçerli kullanıcı kapsamı düzenlenir. %DEĞİŞKEN% biçimindeki değerler genişletilebilir olarak saklanır.", featureUi)
    {
    }

    public EnvironmentVariablesWindow(IEnvironmentVariableStore store, string title, string descriptionText,
        IFeatureUiService? featureUi = null)
    {
        _store = store;
        _autosave = new(Save);
        _descriptionText = descriptionText;
        _uiSurface = featureUi?.CreateSurface("environment-variables")
            ?? throw new InvalidOperationException("The React environment variables view is required.");
        _uiSurface.MessageReceived += OnUiMessage;
        Title = title;
        Width = 820;
        Height = 540;
        MinWidth = 600;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(25, 29, 38));
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        Content = _uiSurface.Element;
        Reload();
        Closed += (_, _) => { _autosave.Dispose(); _uiSurface.Dispose(); };
    }

    private void OnUiMessage(string command, JsonElement payload)
    {
        switch (command)
        {
            case "environment.add": AddEntry(); PushUiState(); break;
            case "environment.remove":
                if (payload.TryGetProperty("index", out var index) && index.TryGetInt32(out var rowIndex) && rowIndex >= 0 && rowIndex < _entries.Count)
                    _entries.RemoveAt(rowIndex);
                PushUiState();
                break;
            case "environment.update":
                if (!payload.TryGetProperty("index", out var selected) || !selected.TryGetInt32(out var selectedIndex) || selectedIndex < 0 || selectedIndex >= _entries.Count) break;
                var row = _entries[selectedIndex];
                if (payload.TryGetProperty("name", out var name)) row.Name = name.GetString() ?? "";
                if (payload.TryGetProperty("value", out var value)) row.Value = value.GetString() ?? "";
                if (payload.TryGetProperty("expandable", out var expandable) && expandable.ValueKind is JsonValueKind.True or JsonValueKind.False) row.Expandable = expandable.GetBoolean();
                PushUiState();
                break;
            case "environment.save": Save(); break;
            case "environment.reload": Reload(); break;
            case "environment.close": Close(); break;
        }
        if (command is "environment.update" or "environment.remove") _autosave.Schedule();
    }

    private void PushUiState() => _uiSurface.SetState(new
    {
        title = Title,
        description = _descriptionText,
        status = _statusText,
        rows = _entries.Select(row => new { row.Name, row.Value, row.Expandable }).ToArray()
    });

    private void AddEntry()
    {
        var row = new UserEnvironmentVariable("", "", false);
        _entries.Add(row);
    }

    private void Reload()
    {
        try
        {
            var entries = _store.Read();
            _entries.Clear();
            foreach (var entry in entries) _entries.Add(entry);
            _originalNames = entries.Select(entry => entry.Name).ToArray();
            _statusText = $"{entries.Count} değişken yüklendi.";
        }
        catch (Exception exception)
        {
            ShowError("Ortam değişkenleri okunamadı.", exception);
        }
        PushUiState();
    }

    private void Save()
    {
        try
        {
            _store.Save(_originalNames, _entries.ToArray());
            _originalNames = _entries.Select(entry => entry.Name).ToArray();
            _statusText = "Ortam değişkenleri kaydedildi.";
        }
        catch (Exception exception)
        {
            ShowError("Değişkenler kaydedilemedi.", exception);
        }
        PushUiState();
    }

    private void ShowError(string message, Exception exception)
    {
        _statusText = message + " " + exception.Message;
        PushUiState();
    }
}
