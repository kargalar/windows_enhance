using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using WinForge.Core;

namespace WinForge.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ModuleVm> _modules = new();
    private readonly ICollectionView _view;
    private string _category = "Tümü";
    private ModuleVm? _selected;
    private bool _showingAppearance;
    private TextBlock? _detailStatus;
    private Border? _errorBox;
    private TextBlock? _errorText;
    private bool _backgroundNoticeShown;

    public MainWindow()
    {
        InitializeComponent();

        Appearance.Load();
        ApplyAppearance();

        foreach (var module in App.Instance.Host.Modules) _modules.Add(new ModuleVm(module));

        _view = CollectionViewSource.GetDefaultView(_modules);
        _view.Filter = FilterModule;
        ModuleList.ItemsSource = _view;

        var categories = new List<string> { "Tümü" };
        categories.AddRange(_modules.Select(m => m.Category).Distinct().OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase));
        CategoryList.ItemsSource = categories;
        CategoryList.SelectedIndex = 0;

        var version = typeof(MainWindow).Assembly.GetName().Version;
        VersionText.Text = version is null ? "v1.0.0" : $"v{version.Major}.{version.Minor}.{version.Build}";

        UpdateStatus();

        if (_modules.Count > 0) ModuleList.SelectedIndex = 0;
        else RenderDetail();
    }

    private Brush BrushOf(string key) => (Brush)FindResource(key);

    private Style StyleOf(string key) => (Style)FindResource(key);

    private bool FilterModule(object item)
    {
        if (item is not ModuleVm vm) return false;

        if (_category != "Tümü" &&
            !string.Equals(vm.Category, _category, StringComparison.CurrentCultureIgnoreCase))
            return false;

        var query = SearchBox?.Text?.Trim();
        if (string.IsNullOrEmpty(query)) return true;

        return vm.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
               || vm.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedItem is not string category) return;

        _category = category;
        _showingAppearance = false;
        UpdateHeader();

        _view?.Refresh();

        if (_view?.IsEmpty == true)
        {
            _selected = null;
            RenderDetail();
        }
    }

    private void UpdateHeader()
    {
        PageTitle.Text = _category == "Tümü" ? "Tüm modüller" : _category;
        PageSubtitle.Text = _category == "Tümü"
            ? "Windows'unu kendi akışına göre şekillendir."
            : $"{_category} kategorisindeki modüller.";
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchHint is not null)
            SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        _view?.Refresh();
    }

    private void OnModuleSelected(object sender, SelectionChangedEventArgs e)
    {
        _selected = ModuleList.SelectedItem as ModuleVm;
        if (_selected is not null)
        {
            _showingAppearance = false;
            UpdateHeader();
        }

        RenderDetail();
    }

    private void OnAppearanceClick(object sender, RoutedEventArgs e)
    {
        ModuleList.SelectedItem = null;
        _selected = null;
        _showingAppearance = true;
        PageTitle.Text = "Görünüm";
        PageSubtitle.Text = "Uygulamanın tonunu ve vurgu rengini kişiselleştir.";
        RenderDetail();
    }

    private void OnModuleToggle(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ModuleVm vm) return;
        if (sender is not CheckBox toggle) return;

        try
        {
            App.Instance.Host.SetEnabled(vm.Module, toggle.IsChecked == true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "WinForge", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        vm.Refresh();
        UpdateStatus();

        if (ReferenceEquals(vm, _selected)) RenderDetail();
    }

    private void RenderDetail()
    {
        DetailPanel.Children.Clear();
        _detailStatus = null;
        _errorBox = null;
        _errorText = null;

        if (_showingAppearance)
        {
            RenderAppearance();
            return;
        }

        if (_selected is null)
        {
            DetailPanel.Children.Add(new TextBlock
            {
                Text = "Detayları görmek için bir modül seç.",
                Foreground = BrushOf("MutedBrush"),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 6, 0, 0)
            });
            return;
        }

        var vm = _selected;
        var module = vm.Module;

        var header = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        header.Children.Add(new TextBlock
        {
            Text = module.Name,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushOf("TextBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        var toggle = new CheckBox
        {
            Style = StyleOf("SwitchStyle"),
            IsChecked = module.IsActive,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        toggle.Click += (_, _) => ToggleFromDetail(vm, toggle.IsChecked == true);
        Grid.SetColumn(toggle, 1);
        header.Children.Add(toggle);
        DetailPanel.Children.Add(header);

        var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        meta.Children.Add(Chip(module.Category));

        _detailStatus = new TextBlock
        {
            Text = module.IsActive ? "Etkin" : "Kapalı",
            Foreground = module.IsActive ? BrushOf("AccentBrush") : BrushOf("MutedBrush"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        meta.Children.Add(_detailStatus);
        DetailPanel.Children.Add(meta);

        DetailPanel.Children.Add(new TextBlock
        {
            Text = module.Description,
            Foreground = BrushOf("MutedBrush"),
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0)
        });

        if (!string.IsNullOrWhiteSpace(module.Warning))
        {
            DetailPanel.Children.Add(Note(module.Warning!, "WarnBrush", Color.FromArgb(24, 255, 200, 87)));
        }

        BuildErrorBox();

        if (module.Settings.Count > 0)
        {
            DetailPanel.Children.Add(new Border
            {
                Height = 1,
                Background = BrushOf("BorderBrush"),
                Margin = new Thickness(0, 16, 0, 4)
            });

            foreach (var group in module.Settings.GroupBy(s => s.Section ?? string.Empty))
            {
                if (!string.IsNullOrEmpty(group.Key))
                {
                    DetailPanel.Children.Add(new TextBlock
                    {
                        Text = group.Key.ToUpperInvariant(),
                        FontSize = 9.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = BrushOf("AccentBrush"),
                        Margin = new Thickness(0, 16, 0, 4)
                    });
                }

                foreach (var setting in group) AddSetting(module, setting);
            }
        }

        UpdateDetailState();
    }

    private void BuildErrorBox()
    {
        _errorText = new TextBlock
        {
            Foreground = BrushOf("DangerBrush"),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };

        _errorBox = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(26, 255, 107, 107)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 12, 0, 0),
            Child = _errorText,
            Visibility = Visibility.Collapsed
        };

        DetailPanel.Children.Add(_errorBox);
    }

    private void AddSetting(ITweakModule module, SettingDescriptor descriptor)
    {
        switch (descriptor.Kind)
        {
            case SettingKind.Toggle:
                AddToggleSetting(module, descriptor);
                break;
            case SettingKind.Number:
                AddNumberSetting(module, descriptor);
                break;
            case SettingKind.Text:
                AddTextSetting(module, descriptor);
                break;
            case SettingKind.Choice:
                AddChoiceSetting(module, descriptor);
                break;
        }
    }

    private void AddToggleSetting(ITweakModule module, SettingDescriptor descriptor)
    {
        var settings = settingsOf(module);

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labels = new StackPanel { Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock { Text = descriptor.Label, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(descriptor.Description))
        {
            labels.Children.Add(new TextBlock
            {
                Text = descriptor.Description,
                Foreground = BrushOf("MutedBrush"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });
        }

        var toggle = new CheckBox
        {
            Style = StyleOf("SwitchStyle"),
            IsChecked = settings.GetBool(descriptor.Key, descriptor.Default is true),
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.Click += (_, _) =>
        {
            settings.Set(descriptor.Key, toggle.IsChecked == true);
            ApplyModule(module);
        };

        grid.Children.Add(labels);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        DetailPanel.Children.Add(grid);
    }

    private void AddNumberSetting(ITweakModule module, SettingDescriptor descriptor)
    {
        var settings = settingsOf(module);

        var wrapper = new StackPanel { Margin = new Thickness(0, 6, 0, 8) };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = descriptor.Label, FontSize = 12.5 });

        var value = new TextBlock
        {
            Text = settings.GetInt(descriptor.Key, Convert.ToInt32(descriptor.Default ?? 0)).ToString(),
            Foreground = BrushOf("AccentBrush"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetColumn(value, 1);
        header.Children.Add(value);
        wrapper.Children.Add(header);

        var slider = new Slider
        {
            Style = StyleOf("DarkSlider"),
            Minimum = descriptor.Min,
            Maximum = descriptor.Max,
            Value = settings.GetInt(descriptor.Key, Convert.ToInt32(descriptor.Default ?? 0)),
            IsSnapToTickEnabled = true,
            TickFrequency = 1,
            Margin = new Thickness(0, 8, 0, 0)
        };

        slider.ValueChanged += (_, _) =>
        {
            var current = (int)Math.Round(slider.Value);
            value.Text = current.ToString();
            settings.Set(descriptor.Key, current);
            ApplyModule(module);
        };

        wrapper.Children.Add(slider);
        DetailPanel.Children.Add(wrapper);
    }

    private void AddTextSetting(ITweakModule module, SettingDescriptor descriptor)
    {
        var settings = settingsOf(module);

        var wrapper = new StackPanel { Margin = new Thickness(0, 6, 0, 8) };
        wrapper.Children.Add(new TextBlock { Text = descriptor.Label, FontSize = 12.5 });

        var box = new TextBox
        {
            Text = settings.GetString(descriptor.Key, descriptor.Default as string ?? ""),
            Margin = new Thickness(0, 8, 0, 0)
        };

        void Commit()
        {
            var text = box.Text.Trim();
            if (settings.GetString(descriptor.Key) == text) return;
            settings.Set(descriptor.Key, text);
            ApplyModule(module);
        }

        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            Commit();
            e.Handled = true;
        };

        wrapper.Children.Add(box);

        if (!string.IsNullOrWhiteSpace(descriptor.Description))
        {
            wrapper.Children.Add(new TextBlock
            {
                Text = descriptor.Description,
                Foreground = BrushOf("MutedBrush"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
        }

        DetailPanel.Children.Add(wrapper);
    }

    private void AddChoiceSetting(ITweakModule module, SettingDescriptor descriptor)
    {
        var settings = settingsOf(module);

        var wrapper = new StackPanel { Margin = new Thickness(0, 6, 0, 8) };
        wrapper.Children.Add(new TextBlock
        {
            Text = descriptor.Label,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap
        });

        if (!string.IsNullOrWhiteSpace(descriptor.Description))
        {
            wrapper.Children.Add(new TextBlock
            {
                Text = descriptor.Description,
                Foreground = BrushOf("MutedBrush"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });
        }

        var current = settings.GetString(descriptor.Key, descriptor.Default as string ?? descriptor.Options.FirstOrDefault() ?? "");
        var panel = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };

        foreach (var option in descriptor.Options)
        {
            var radio = new RadioButton
            {
                Style = StyleOf("PillOption"),
                Content = option,
                GroupName = module.Id + ":" + descriptor.Key,
                IsChecked = string.Equals(option, current, StringComparison.CurrentCultureIgnoreCase)
            };

            var captured = option;
            radio.Checked += (_, _) =>
            {
                settings.Set(descriptor.Key, captured);
                ApplyModule(module);
            };

            panel.Children.Add(radio);
        }

        wrapper.Children.Add(panel);
        DetailPanel.Children.Add(wrapper);
    }

    private static IModuleSettings settingsOf(ITweakModule module) =>
        App.Instance.Host.SettingsOf(module);

    private void ApplyModule(ITweakModule module)
    {
        App.Instance.Host.Apply(module);
        UpdateDetailState();
        UpdateStatus();
    }

    private void UpdateDetailState()
    {
        if (_selected is null) return;

        if (_detailStatus is not null)
        {
            _detailStatus.Text = _selected.Module.IsActive ? "Etkin" : "Kapalı";
            _detailStatus.Foreground = _selected.Module.IsActive ? BrushOf("AccentBrush") : BrushOf("MutedBrush");
        }

        var error = App.Instance.Host.ErrorOf(_selected.Module);
        if (_errorBox is not null && _errorText is not null)
        {
            _errorBox.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
            _errorText.Text = error ?? "";
        }
    }

    private void ToggleFromDetail(ModuleVm vm, bool enabled)
    {
        try
        {
            App.Instance.Host.SetEnabled(vm.Module, enabled);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "WinForge", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        vm.Refresh();
        UpdateStatus();
        RenderDetail();
    }

    private void UpdateStatus()
    {
        var active = _modules.Count(m => m.IsActive);
        StatusBar.Text = $"{active} etkin · {_modules.Count} modül";
        EngineStatus.Text = active > 0 ? $"{active} modül etkin" : "Hazır";
    }

    private void RenderAppearance()
    {
        DetailPanel.Children.Add(new TextBlock
        {
            Text = "Görünüm",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushOf("TextBrush")
        });

        DetailPanel.Children.Add(new TextBlock
        {
            Text = "Ton ve vurgu rengi anında uygulanır, seçimlerin hatırlanır.",
            Foreground = BrushOf("MutedBrush"),
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });

        DetailPanel.Children.Add(new Border
        {
            Height = 1,
            Background = BrushOf("BorderBrush"),
            Margin = new Thickness(0, 16, 0, 4)
        });

        SectionLabel("TON");
        var tones = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        foreach (var tone in Appearance.Tones)
        {
            var name = tone.Name;
            var radio = new RadioButton
            {
                Style = StyleOf("PillOption"),
                Content = name,
                GroupName = "appearance-tone",
                IsChecked = Appearance.ToneName == name
            };
            radio.Checked += (_, _) =>
            {
                Appearance.SetTone(name);
                ApplyAppearance();
                Dispatcher.BeginInvoke(new Action(RenderDetail));
            };
            tones.Children.Add(radio);
        }
        DetailPanel.Children.Add(tones);

        SectionLabel("VURGU RENGİ");
        var swatches = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        foreach (var accent in Appearance.Accents)
        {
            var name = accent.Name;
            var color = (Color)ColorConverter.ConvertFromString(accent.Hex)!;
            var radio = new RadioButton
            {
                Style = StyleOf("SwatchOption"),
                Background = new SolidColorBrush(color),
                GroupName = "appearance-accent",
                IsChecked = Appearance.AccentName == name,
                ToolTip = name
            };
            AutomationProperties.SetName(radio, name);
            radio.Checked += (_, _) =>
            {
                Appearance.SetAccent(name);
                ApplyAppearance();
                Dispatcher.BeginInvoke(new Action(RenderDetail));
            };
            swatches.Children.Add(radio);
        }
        DetailPanel.Children.Add(swatches);

        SectionLabel("ARAYÜZ");
        var glowRow = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        glowRow.ColumnDefinitions.Add(new ColumnDefinition());
        glowRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var glowLabels = new StackPanel { Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        glowLabels.Children.Add(new TextBlock { Text = "Arka plan parlaması", FontSize = 12.5 });
        glowLabels.Children.Add(new TextBlock
        {
            Text = "Sağ üstteki renkli ışımayı gösterir.",
            Foreground = BrushOf("MutedBrush"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        });
        glowRow.Children.Add(glowLabels);

        var glowToggle = new CheckBox
        {
            Style = StyleOf("SwitchStyle"),
            IsChecked = Appearance.Glow,
            VerticalAlignment = VerticalAlignment.Center
        };
        glowToggle.Checked += (_, _) =>
        {
            Appearance.SetGlow(true);
            ApplyAppearance();
        };
        glowToggle.Unchecked += (_, _) =>
        {
            Appearance.SetGlow(false);
            ApplyAppearance();
        };
        Grid.SetColumn(glowToggle, 1);
        glowRow.Children.Add(glowToggle);
        DetailPanel.Children.Add(glowRow);

        DetailPanel.Children.Add(new Border
        {
            Height = 1,
            Background = BrushOf("BorderBrush"),
            Margin = new Thickness(0, 14, 0, 12)
        });

        var reset = new Button
        {
            Style = StyleOf("GhostButton"),
            Content = "Varsayılana sıfırla",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        reset.Click += (_, _) =>
        {
            Appearance.Reset();
            ApplyAppearance();
            RenderDetail();
        };
        DetailPanel.Children.Add(reset);
    }

    private void SectionLabel(string text) => DetailPanel.Children.Add(new TextBlock
    {
        Text = text,
        FontSize = 9.5,
        FontWeight = FontWeights.SemiBold,
        Foreground = BrushOf("AccentBrush"),
        Margin = new Thickness(0, 16, 0, 4)
    });

    private void ApplyAppearance()
    {
        Appearance.Apply();

        if (GlowLayer is not null)
        {
            var accent = Appearance.AccentColor;
            var glow = new RadialGradientBrush();
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x24, accent.R, accent.G, accent.B), 0));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1));
            GlowLayer.Fill = glow;
            GlowLayer.Visibility = Appearance.Glow ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private Border Chip(string text)
    {
        var accent = Appearance.AccentColor;
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(24, accent.R, accent.G, accent.B)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 8, 3),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10.5,
                Foreground = BrushOf("AccentBrush")
            }
        };
    }

    private Border Note(string text, string brushKey, Color background)
    {
        return new Border
        {
            Background = new SolidColorBrush(background),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 12, 0, 0),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11.5,
                Foreground = BrushOf(brushKey),
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{App.Instance.Logger.Path}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "WinForge", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExit(object sender, RoutedEventArgs e) => App.Instance.ExitApp();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.Instance.IsExiting)
        {
            e.Cancel = true;
            Hide();

            if (!_backgroundNoticeShown)
            {
                _backgroundNoticeShown = true;
                OsdWindow.Toast("WinForge arka planda çalışıyor · Kısayollar etkin");
            }

            return;
        }

        base.OnClosing(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
