using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace WinForge.Modules.Extras;

internal sealed class EnvironmentVariablesWindow : Window
{
    private readonly IEnvironmentVariableStore _store;
    private readonly ObservableCollection<UserEnvironmentVariable> _entries = new();
    private readonly DataGrid _grid;
    private string[] _originalNames = Array.Empty<string>();

    public EnvironmentVariablesWindow(UserEnvironmentVariableStore store)
        : this(store, "Kullanıcı Ortam Değişkenleri",
            "Yalnızca geçerli kullanıcı kapsamı düzenlenir. %DEĞİŞKEN% biçimindeki değerler genişletilebilir olarak saklanır.")
    {
    }

    public EnvironmentVariablesWindow(IEnvironmentVariableStore store, string title, string descriptionText)
    {
        _store = store;
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

        var root = new DockPanel { Margin = new Thickness(18) };
        var heading = new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var description = new TextBlock
        {
            Text = descriptionText,
            Foreground = new SolidColorBrush(Color.FromRgb(155, 165, 180)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        };
        DockPanel.SetDock(description, Dock.Top);
        root.Children.Add(description);

        _grid = new DataGrid
        {
            ItemsSource = _entries,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = false,
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

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(CreateButton("Değişken ekle", AddEntry));
        buttons.Children.Add(CreateButton("Seçileni sil", RemoveSelected));
        buttons.Children.Add(CreateButton("Yenile", Reload));
        buttons.Children.Add(CreateButton("Kaydet", Save));
        buttons.Children.Add(CreateButton("Kapat", Close));
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        Content = root;
        Reload();
    }

    private static Button CreateButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(5, 0, 0, 0),
            MinWidth = 74
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void AddEntry()
    {
        var row = new UserEnvironmentVariable("", "", false);
        _entries.Add(row);
        _grid.SelectedItem = row;
        _grid.ScrollIntoView(row);
        _grid.BeginEdit();
    }

    private void RemoveSelected()
    {
        if (_grid.SelectedItem is UserEnvironmentVariable selected)
            _entries.Remove(selected);
    }

    private void Reload()
    {
        try
        {
            var entries = _store.Read();
            _entries.Clear();
            foreach (var entry in entries) _entries.Add(entry);
            _originalNames = entries.Select(entry => entry.Name).ToArray();
        }
        catch (Exception exception)
        {
            ShowError("Ortam değişkenleri okunamadı.", exception);
        }
    }

    private void Save()
    {
        try
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);
            _store.Save(_originalNames, _entries.ToArray());
            Reload();
        }
        catch (Exception exception)
        {
            ShowError("Değişkenler kaydedilemedi.", exception);
        }
    }

    private void ShowError(string message, Exception exception) =>
        MessageBox.Show(this, message + Environment.NewLine + exception.Message,
            "WinForge", MessageBoxButton.OK, MessageBoxImage.Error);
}
