using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace WinForge.Modules.Extras;

internal sealed class EnvironmentProfilesWindow : Window
{
    private readonly EnvironmentProfileService _service;
    private readonly ObservableCollection<UserEnvironmentProfile> _profiles = new();
    private readonly ObservableCollection<UserEnvironmentVariable> _variables = new();
    private readonly ComboBox _profileBox;
    private readonly TextBox _profileName;
    private readonly TextBox _newProfileName;
    private readonly CheckBox _enabled;
    private readonly DataGrid _grid;
    private readonly TextBlock _status;
    private bool _loading;

    public EnvironmentProfilesWindow(EnvironmentProfileService service)
    {
        _service = service;
        Title = "Ortam Değişkeni Profilleri";
        Width = 900;
        Height = 600;
        MinWidth = 680;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(25, 29, 38));
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var root = new DockPanel { Margin = new Thickness(18) };
        var heading = new TextBlock
        {
            Text = "Ortam değişkeni profilleri",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var description = new TextBlock
        {
            Text = "Profiller kullanıcı değişkenlerini birlikte uygular. Aynı anda yalnızca bir profil etkin olabilir; kapatınca profil öncesi değerler geri yüklenir.",
            Foreground = new SolidColorBrush(Color.FromRgb(155, 165, 180)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        };
        DockPanel.SetDock(description, Dock.Top);
        root.Children.Add(description);

        var profilePanel = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        profilePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        profilePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        profilePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        profilePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        profilePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _profileBox = new ComboBox { ItemsSource = _profiles, DisplayMemberPath = nameof(UserEnvironmentProfile.Name), MinWidth = 150, Margin = new Thickness(0, 0, 8, 0) };
        _profileBox.SelectionChanged += OnProfileChanged;
        profilePanel.Children.Add(_profileBox);

        _profileName = new TextBox { Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 5, 8, 5), ToolTip = "Seçili profilin adı" };
        Grid.SetColumn(_profileName, 1);
        profilePanel.Children.Add(_profileName);

        _newProfileName = new TextBox { Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 5, 8, 5), ToolTip = "Yeni profil adı" };
        Grid.SetColumn(_newProfileName, 2);
        profilePanel.Children.Add(_newProfileName);
        Grid.SetColumn(profilePanel.Children[2], 2);

        profilePanel.Children.Add(CreateButton("Yeni", CreateProfile, 3));
        profilePanel.Children.Add(CreateButton("Profili sil", DeleteProfile, 4));
        DockPanel.SetDock(profilePanel, Dock.Top);
        root.Children.Add(profilePanel);

        _enabled = new CheckBox
        {
            Content = "Bu profili kullanıcı ortam değişkenlerine uygula",
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = Brushes.White
        };
        _enabled.Checked += (_, _) => SetProfileEnabled(true);
        _enabled.Unchecked += (_, _) => SetProfileEnabled(false);
        DockPanel.SetDock(_enabled, Dock.Top);
        root.Children.Add(_enabled);

        _grid = new DataGrid
        {
            ItemsSource = _variables,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            Background = new SolidColorBrush(Color.FromRgb(31, 36, 46)),
            Foreground = Brushes.White,
            RowBackground = new SolidColorBrush(Color.FromRgb(31, 36, 46)),
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(37, 43, 54)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(67, 77, 94)),
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            Margin = new Thickness(0, 0, 0, 12)
        };
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Değişken",
            Binding = new Binding(nameof(UserEnvironmentVariable.Name)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(220)
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Değer",
            Binding = new Binding(nameof(UserEnvironmentVariable.Value)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        _grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "%...% genişlet",
            Binding = new Binding(nameof(UserEnvironmentVariable.Expandable)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = new DataGridLength(130)
        });
        root.Children.Add(_grid);

        var footer = new DockPanel();
        _status = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(155, 165, 180)),
            VerticalAlignment = VerticalAlignment.Center
        };
        footer.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(CreateButton("Değişken ekle", AddVariable));
        buttons.Children.Add(CreateButton("Seçileni sil", RemoveVariable));
        buttons.Children.Add(CreateButton("Kaydet", SaveProfile));
        buttons.Children.Add(CreateButton("Kapat", Close));
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        Content = root;
        RefreshProfiles(null);
    }

    private static Button CreateButton(string label, Action action, int column = -1)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(11, 7, 11, 7),
            Margin = new Thickness(5, 0, 0, 0),
            MinWidth = 72
        };
        button.Click += (_, _) => action();
        if (column >= 0) Grid.SetColumn(button, column);
        return button;
    }

    private void RefreshProfiles(string? selectedId)
    {
        _loading = true;
        try
        {
            _profiles.Clear();
            foreach (var profile in _service.Profiles.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase))
                _profiles.Add(profile);
            _profileBox.SelectedItem = selectedId is null
                ? _profiles.FirstOrDefault(profile => profile.Id == _service.ActiveProfileId) ?? _profiles.FirstOrDefault()
                : _profiles.FirstOrDefault(profile => profile.Id == selectedId);
            LoadSelectedProfile();
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnProfileChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_loading) return;
        if (args.RemovedItems.OfType<UserEnvironmentProfile>().FirstOrDefault() is { } previous)
        {
            if (!SaveProfileData(previous.Id, _profileName.Text, _variables.ToArray(), showErrors: false))
            {
                _loading = true;
                _profileBox.SelectedItem = previous;
                _loading = false;
                return;
            }
        }
        LoadSelectedProfile();
    }

    private void LoadSelectedProfile()
    {
        _loading = true;
        try
        {
            _variables.Clear();
            if (_profileBox.SelectedItem is UserEnvironmentProfile profile)
            {
                _profileName.Text = profile.Name;
                foreach (var entry in profile.Variables) _variables.Add(new UserEnvironmentVariable(entry.Name, entry.Value, entry.Expandable));
                _enabled.IsChecked = _service.ActiveProfileId == profile.Id;
                _enabled.IsEnabled = true;
                _grid.IsEnabled = true;
                _profileName.IsEnabled = true;
                _status.Text = _service.ActiveProfileId == profile.Id ? "Etkin profil" : "Etkin değil";
            }
            else
            {
                _profileName.Clear();
                _enabled.IsChecked = false;
                _enabled.IsEnabled = false;
                _grid.IsEnabled = false;
                _profileName.IsEnabled = false;
                _status.Text = "Önce bir profil oluşturun.";
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void CreateProfile()
    {
        try
        {
            if (!SaveCurrentProfile(showErrors: true)) return;
            var id = _service.CreateProfile(_newProfileName.Text);
            _newProfileName.Clear();
            RefreshProfiles(id);
        }
        catch (Exception exception) { ShowError("Profil oluşturulamadı.", exception); }
    }

    private void DeleteProfile()
    {
        if (_profileBox.SelectedItem is not UserEnvironmentProfile profile) return;
        if (MessageBox.Show(this, $"'{profile.Name}' profili silinsin mi? Etkinse değişkenleri geri yüklenir.",
                "Ortam Değişkenleri", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            _service.DeleteProfile(profile.Id);
            RefreshProfiles(null);
        }
        catch (Exception exception) { ShowError("Profil silinemedi.", exception); }
    }

    private void SetProfileEnabled(bool enabled)
    {
        if (_loading || _profileBox.SelectedItem is not UserEnvironmentProfile profile) return;
        try
        {
            SaveCurrentProfile(showErrors: true);
            _service.SetActiveProfile(enabled ? profile.Id : null);
            LoadSelectedProfile();
        }
        catch (Exception exception)
        {
            _loading = true;
            _enabled.IsChecked = !enabled;
            _loading = false;
            ShowError("Profil uygulanamadı.", exception);
        }
    }

    private void SaveProfile() => SaveCurrentProfile(showErrors: true);

    private bool SaveCurrentProfile(bool showErrors)
    {
        if (_profileBox.SelectedItem is not UserEnvironmentProfile profile) return true;
        return SaveProfileData(profile.Id, _profileName.Text, _variables.ToArray(), showErrors);
    }

    private bool SaveProfileData(string id, string name, IReadOnlyCollection<UserEnvironmentVariable> variables, bool showErrors)
    {
        try
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);
            _service.SaveProfile(id, name, variables);
            var profile = _profiles.FirstOrDefault(item => item.Id == id);
            if (profile is not null)
            {
                profile.Name = name.Trim();
                profile.Variables = variables.Select(entry => new UserEnvironmentVariable(entry.Name, entry.Value, entry.Expandable)).ToList();
            }
            _status.Text = _service.ActiveProfileId == id ? "Etkin profil kaydedildi ve yeniden uygulandı." : "Profil kaydedildi.";
            return true;
        }
        catch (Exception exception)
        {
            if (showErrors) ShowError("Profil kaydedilemedi.", exception);
            return false;
        }
    }

    private void AddVariable()
    {
        var entry = new UserEnvironmentVariable("", "", false);
        _variables.Add(entry);
        _grid.SelectedItem = entry;
        _grid.ScrollIntoView(entry);
        _grid.BeginEdit();
    }

    private void RemoveVariable()
    {
        if (_grid.SelectedItem is UserEnvironmentVariable entry)
            _variables.Remove(entry);
    }

    private void ShowError(string message, Exception exception) =>
        MessageBox.Show(this, message + Environment.NewLine + exception.Message,
            "WinForge", MessageBoxButton.OK, MessageBoxImage.Error);
}
