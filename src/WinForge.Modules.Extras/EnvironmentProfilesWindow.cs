using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using WinForge.Core;

namespace WinForge.Modules.Extras;

internal sealed class EnvironmentProfilesWindow : Window
{
    private readonly EnvironmentProfileService _service;
    private readonly IFeatureUiSurface _uiSurface;
    private readonly List<UserEnvironmentProfile> _profiles = [];
    private readonly ObservableCollection<UserEnvironmentVariable> _variables = [];
    private string? _selectedId;
    private string _profileName = "";
    private string _newProfileName = "";
    private string _statusText = "";
    private bool _enabled;
    private bool _enabledAllowed;
    private bool _loading;
    private bool _deleteConfirmedFromUi;
    private bool _confirmDelete;

    public EnvironmentProfilesWindow(EnvironmentProfileService service, IFeatureUiService? featureUi = null)
    {
        _service = service;
        _uiSurface = featureUi?.CreateSurface("environment-profiles")
            ?? throw new InvalidOperationException("The React environment profiles view is required.");
        _uiSurface.MessageReceived += OnUiMessage;
        Title = "Ortam Değişkeni Profilleri";
        Width = 900;
        Height = 600;
        MinWidth = 680;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Content = _uiSurface.Element;

        RefreshProfiles(null);
        Closed += (_, _) => _uiSurface.Dispose();
    }

    private UserEnvironmentProfile? SelectedProfile => _profiles.FirstOrDefault(profile => profile.Id == _selectedId);

    private void RefreshProfiles(string? selectedId)
    {
        _profiles.Clear();
        _profiles.AddRange(_service.Profiles.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase));
        _selectedId = _profiles.FirstOrDefault(profile => profile.Id == selectedId)?.Id
            ?? _profiles.FirstOrDefault(profile => profile.Id == _service.ActiveProfileId)?.Id
            ?? _profiles.FirstOrDefault()?.Id;
        LoadSelectedProfile();
    }

    private void SelectProfile(string? id)
    {
        if (_loading || id == _selectedId) return;
        if (_selectedId is { } currentId && !SaveProfileData(currentId, _profileName, _variables.ToArray(), showErrors: false))
            return;
        _selectedId = _profiles.FirstOrDefault(profile => profile.Id == id)?.Id;
        LoadSelectedProfile();
    }

    private void LoadSelectedProfile()
    {
        _loading = true;
        try
        {
            _variables.Clear();
            if (SelectedProfile is { } profile)
            {
                _profileName = profile.Name;
                foreach (var entry in profile.Variables)
                    _variables.Add(new UserEnvironmentVariable(entry.Name, entry.Value, entry.Expandable));
                _enabled = _service.ActiveProfileId == profile.Id;
                _enabledAllowed = true;
                _statusText = _enabled ? "Etkin profil" : "Etkin değil";
            }
            else
            {
                _profileName = "";
                _enabled = false;
                _enabledAllowed = false;
                _statusText = "Önce bir profil oluşturun.";
            }
        }
        finally
        {
            _loading = false;
        }
        PushUiState();
    }

    private void OnUiMessage(string command, JsonElement payload)
    {
        switch (command)
        {
            case "profiles.select":
                SelectProfile(payload.TryGetProperty("id", out var profileId) ? profileId.GetString() : null);
                break;
            case "profiles.name":
                _profileName = ReadString(payload, "name");
                PushUiState();
                break;
            case "profiles.new-name":
                _newProfileName = ReadString(payload, "name");
                PushUiState();
                break;
            case "profiles.new":
                _newProfileName = ReadString(payload, "name");
                CreateProfile();
                break;
            case "profiles.delete": DeleteProfile(); break;
            case "profiles.delete-confirm":
                var accepted = payload.TryGetProperty("confirm", out var confirm) && confirm.ValueKind == JsonValueKind.True;
                _confirmDelete = false;
                if (accepted)
                {
                    _deleteConfirmedFromUi = true;
                    DeleteProfile();
                    _deleteConfirmedFromUi = false;
                }
                else PushUiState();
                break;
            case "profiles.toggle":
                if (payload.TryGetProperty("enabled", out var enabled) && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    SetProfileEnabled(enabled.GetBoolean());
                break;
            case "profiles.variable-update":
                if (payload.TryGetProperty("index", out var row) && row.TryGetInt32(out var rowIndex) && rowIndex >= 0 && rowIndex < _variables.Count)
                {
                    var variable = _variables[rowIndex];
                    if (payload.TryGetProperty("name", out var name)) variable.Name = name.GetString() ?? "";
                    if (payload.TryGetProperty("value", out var value)) variable.Value = value.GetString() ?? "";
                    if (payload.TryGetProperty("expandable", out var expansion) && expansion.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        variable.Expandable = expansion.GetBoolean();
                    PushUiState();
                }
                break;
            case "profiles.variable-add":
                AddVariable();
                PushUiState();
                break;
            case "profiles.variable-remove":
                if (payload.TryGetProperty("index", out var removeIndex) && removeIndex.TryGetInt32(out var index) && index >= 0 && index < _variables.Count)
                    _variables.RemoveAt(index);
                PushUiState();
                break;
            case "profiles.save": SaveProfile(); break;
            case "profiles.close": Close(); break;
        }
    }

    private static string ReadString(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

    private void PushUiState() => _uiSurface.SetState(new
    {
        profiles = _profiles.Select(profile => new { id = profile.Id, name = profile.Name }).ToArray(),
        selectedId = _selectedId ?? "",
        profileName = _profileName,
        newProfileName = _newProfileName,
        enabled = _enabled,
        enabledAllowed = _enabledAllowed,
        status = _statusText,
        confirmDelete = _confirmDelete,
        variables = _variables.Select(variable => new { variable.Name, variable.Value, variable.Expandable }).ToArray()
    });

    private void CreateProfile()
    {
        try
        {
            if (!SaveCurrentProfile(showErrors: true)) return;
            var id = _service.CreateProfile(_newProfileName);
            _newProfileName = "";
            RefreshProfiles(id);
        }
        catch (Exception exception)
        {
            ShowError("Profil oluşturulamadı.", exception);
        }
    }

    private void DeleteProfile()
    {
        if (SelectedProfile is not { } profile) return;
        if (!_deleteConfirmedFromUi)
        {
            _confirmDelete = true;
            PushUiState();
            return;
        }

        try
        {
            _service.DeleteProfile(profile.Id);
            RefreshProfiles(null);
        }
        catch (Exception exception)
        {
            ShowError("Profil silinemedi.", exception);
        }
    }

    private void SetProfileEnabled(bool enabled)
    {
        if (_loading || SelectedProfile is not { } profile) return;
        try
        {
            if (!SaveCurrentProfile(showErrors: true)) return;
            _service.SetActiveProfile(enabled ? profile.Id : null);
            LoadSelectedProfile();
        }
        catch (Exception exception)
        {
            _enabled = !enabled;
            ShowError("Profil uygulanamadı.", exception);
        }
    }

    private void SaveProfile()
    {
        SaveCurrentProfile(showErrors: true);
        PushUiState();
    }

    private bool SaveCurrentProfile(bool showErrors) => SelectedProfile is not { } profile ||
        SaveProfileData(profile.Id, _profileName, _variables.ToArray(), showErrors);

    private bool SaveProfileData(string id, string name, IReadOnlyCollection<UserEnvironmentVariable> variables, bool showErrors)
    {
        try
        {
            _service.SaveProfile(id, name, variables);
            var profile = _profiles.FirstOrDefault(item => item.Id == id);
            if (profile is not null)
            {
                profile.Name = name.Trim();
                profile.Variables = variables.Select(entry => new UserEnvironmentVariable(entry.Name, entry.Value, entry.Expandable)).ToList();
            }
            _statusText = _service.ActiveProfileId == id ? "Etkin profil kaydedildi ve yeniden uygulandı." : "Profil kaydedildi.";
            return true;
        }
        catch (Exception exception)
        {
            if (showErrors) ShowError("Profil kaydedilemedi.", exception);
            return false;
        }
    }

    private void AddVariable() => _variables.Add(new UserEnvironmentVariable("", "", false));

    private void ShowError(string message, Exception exception)
    {
        _statusText = message + " " + exception.Message;
        PushUiState();
    }
}
