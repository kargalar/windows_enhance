using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using WinForge.Core;

namespace WinForge.App;

public partial class MainWindow : Window
{
    private sealed record ChoiceOption(string Value, string Label);

    private readonly ObservableCollection<ModuleVm> _modules = new();
    private readonly ICollectionView _view;
    private string _category = "Tümü";
    private ModuleVm? _selected;
    private bool _showingAppearance;
    private TextBlock? _detailStatus;
    private Border? _errorBox;
    private TextBlock? _errorText;
    private bool _backgroundNoticeShown;
    private TextBox? _hotkeyCaptureTarget;
    private TextBlock? _hotkeyCaptureStatus;
    private Action? _hotkeyCaptureCommit;
    private string? _capturedHotkey;

    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += OnHotkeyCaptureKeyDown;
        PreviewMouseWheel += OnHotkeyCaptureMouseWheel;

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
        if (ActiveOnlyToggle?.IsChecked == true && !vm.IsActive) return false;

        if (_category != "Tümü" &&
            !string.Equals(vm.Category, _category, StringComparison.CurrentCultureIgnoreCase))
            return false;

        var query = SearchBox?.Text?.Trim();
        if (string.IsNullOrEmpty(query)) return true;

        return vm.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
               || vm.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase)
               || vm.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedItem is not string category) return;

        _category = category;
        RefreshModules();
    }

    private void UpdateHeader()
    {
        PageTitle.Text = _category == "Tümü" ? "Tüm modüller" : _category;
        PageSubtitle.Text = _category == "Tümü"
            ? "Windows'u çalışma biçimine uyarla."
            : $"{_category} kategorisindeki modüller.";
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchHint is not null)
            SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        if (ClearSearchButton is not null)
            ClearSearchButton.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Collapsed : Visibility.Visible;

        RefreshModules();
    }

    private void RefreshModules()
    {
        if (_view is null) return;
        var previous = ModuleList.SelectedItem;
        _showingAppearance = false;
        _view.Refresh();
        ModuleList.SelectedItem = previous is not null && _view.Contains(previous)
            ? previous
            : _view.Cast<ModuleVm>().FirstOrDefault();
        _selected = ModuleList.SelectedItem as ModuleVm;
        EmptyState.Visibility = _view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        UpdateHeader();
        UpdateStatus();
        RenderDetail();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (_selected is not null) ModuleList.ScrollIntoView(_selected);
        }));
    }

    private void OnActiveFilterChanged(object sender, RoutedEventArgs e) => RefreshModules();

    private void OnClearSearch(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void OnResetFilters(object sender, RoutedEventArgs e)
    {
        ActiveOnlyToggle.IsChecked = false;
        SearchBox.Clear();
        CategoryList.SelectedIndex = 0;
        RefreshModules();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.IsKeyboardFocusWithin)
        {
            SearchBox.Clear();
            e.Handled = true;
        }
    }

    private void OnModuleSelected(object sender, SelectionChangedEventArgs e)
    {
        _selected = ModuleList.SelectedItem as ModuleVm;
        if (_selected is not null)
        {
            _showingAppearance = false;
            UpdateHeader();
            ModuleList.ScrollIntoView(_selected);
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
        toggle.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateTarget();
        RefreshModules();
    }

    private void RenderDetail()
    {
        DetailPanel.Children.Clear();
        DetailScrollViewer.ScrollToTop();
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
        AutomationProperties.SetName(toggle, module.Name);
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
            case SettingKind.Palette:
                AddPaletteSetting(module, descriptor);
                break;
            case SettingKind.Action:
                AddActionSetting(module, descriptor);
                break;
        }
    }

    private void AddActionSetting(ITweakModule module, SettingDescriptor descriptor)
    {
        var button = new Button
        {
            Style = StyleOf("GhostButton"),
            Content = descriptor.Label,
            ToolTip = descriptor.Description,
            Margin = new Thickness(0, 6, 0, 6),
            Padding = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        SetSettingAutomation(button, module, descriptor);
        button.Click += (_, _) =>
        {
            try { descriptor.Execute?.Invoke(); }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "WinForge", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            RefreshModules();
        };
        DetailPanel.Children.Add(button);
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
        SetSettingAutomation(toggle, module, descriptor);

        grid.Children.Add(labels);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        DetailPanel.Children.Add(grid);
    }

    private static void SetSettingAutomation(DependencyObject control, ITweakModule module, SettingDescriptor descriptor)
    {
        AutomationProperties.SetName(control, descriptor.Label);
        AutomationProperties.SetAutomationId(control, module.Id + "." + descriptor.Key);
        AutomationProperties.SetHelpText(control, descriptor.Section ?? "");
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
        SetSettingAutomation(slider, module, descriptor);

        var saveTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        var hasPendingChange = false;

        void CommitChange()
        {
            saveTimer.Stop();
            if (!hasPendingChange) return;

            hasPendingChange = false;
            settings.Set(descriptor.Key, (int)Math.Round(slider.Value));
            ApplyModule(module);
        }

        saveTimer.Tick += (_, _) => CommitChange();
        slider.ValueChanged += (_, _) =>
        {
            var current = (int)Math.Round(slider.Value);
            value.Text = current.ToString();
            hasPendingChange = true;
            saveTimer.Stop();
            saveTimer.Start();
        };
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => CommitChange()));
        slider.LostKeyboardFocus += (_, _) => CommitChange();
        slider.Unloaded += (_, _) => CommitChange();

        wrapper.Children.Add(slider);
        if (!string.IsNullOrWhiteSpace(descriptor.Description))
        {
            wrapper.Children.Add(new TextBlock
            {
                Text = descriptor.Description,
                Foreground = BrushOf("MutedBrush"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }
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
        SetSettingAutomation(box, module, descriptor);

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

        if (IsHotkeyTextSetting(descriptor))
        {
            var inputRow = new Grid { Margin = box.Margin };
            inputRow.ColumnDefinitions.Add(new ColumnDefinition());
            inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            box.Margin = new Thickness(0);
            inputRow.Children.Add(box);

            var captureStatus = new TextBlock
            {
                Text = "Ata'ya basıp kısayol tuşlarına basın, Enter ile kaydedin.",
                Foreground = BrushOf("MutedBrush"),
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0),
                Visibility = Visibility.Collapsed
            };
            var captureButton = new Button
            {
                Content = "Ata",
                Style = StyleOf("GhostButton"),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Kısayol tuşlarını dinle; Enter ile kaydet, Esc ile iptal et."
            };
            captureButton.Click += (_, _) => StartHotkeyCapture(box, captureStatus, Commit);
            Grid.SetColumn(captureButton, 1);
            inputRow.Children.Add(captureButton);
            wrapper.Children.Add(inputRow);
            wrapper.Children.Add(captureStatus);
        }
        else
        {
            wrapper.Children.Add(box);
        }

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

    private static bool IsHotkeyTextSetting(SettingDescriptor descriptor) =>
        descriptor.Key.Contains("hotkey", StringComparison.OrdinalIgnoreCase) ||
        descriptor.Key.Contains("shortcut", StringComparison.OrdinalIgnoreCase) ||
        descriptor.Label.Contains("kısayol", StringComparison.OrdinalIgnoreCase);

    private void StartHotkeyCapture(TextBox target, TextBlock status, Action commit)
    {
        _hotkeyCaptureTarget = target;
        _hotkeyCaptureStatus = status;
        _hotkeyCaptureCommit = commit;
        _capturedHotkey = null;
        status.Visibility = Visibility.Visible;
        status.Text = "Tuşlara basın, ardından Enter ile kaydedin. Esc iptal eder.";
    }

    private void OnHotkeyCaptureKeyDown(object sender, KeyEventArgs e)
    {
        if (_hotkeyCaptureTarget is null || _hotkeyCaptureStatus is null) return;

        if (e.Key == Key.Escape)
        {
            _hotkeyCaptureStatus.Text = "Atama iptal edildi.";
            ClearHotkeyCapture();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (_capturedHotkey is null)
            {
                _hotkeyCaptureStatus.Text = "Önce bir tuş veya kısayol kombinasyonu girin.";
            }
            else
            {
                _hotkeyCaptureTarget.Text = _capturedHotkey;
                _hotkeyCaptureCommit?.Invoke();
                _hotkeyCaptureStatus.Text = $"Kaydedildi: {_capturedHotkey}";
                ClearHotkeyCapture();
            }
            e.Handled = true;
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var parts = new List<string>();
        var modifiers = Keyboard.Modifiers;
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(key.ToString());
        _capturedHotkey = string.Join('+', parts);
        _hotkeyCaptureStatus.Text = $"Yakalandı: {_capturedHotkey} · Enter kaydet, Esc iptal et.";
        e.Handled = true;
    }

    private void OnHotkeyCaptureMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_hotkeyCaptureTarget is null || _hotkeyCaptureStatus is null) return;
        var parts = new List<string>();
        var modifiers = Keyboard.Modifiers;
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(e.Delta > 0 ? "MouseWheelUp" : "MouseWheelDown");
        _capturedHotkey = string.Join('+', parts);
        _hotkeyCaptureStatus.Text = $"Yakalandı: {_capturedHotkey} · Enter kaydet, Esc iptal et.";
        e.Handled = true;
    }

    private void ClearHotkeyCapture()
    {
        _hotkeyCaptureTarget = null;
        _hotkeyCaptureStatus = null;
        _hotkeyCaptureCommit = null;
        _capturedHotkey = null;
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
        if (descriptor.Options.Length > 6)
        {
            var options = descriptor.Options.Contains(current) ? descriptor.Options : descriptor.Options.Append(current).ToArray();
            var hasLabels = descriptor.OptionLabels.Count > 0;
            var combo = new ComboBox
            {
                ItemsSource = hasLabels
                    ? options.Select(option => new ChoiceOption(option, descriptor.OptionLabels.GetValueOrDefault(option, option))).ToArray()
                    : options,
                SelectedItem = hasLabels ? null : current,
                SelectedValuePath = hasLabels ? nameof(ChoiceOption.Value) : null,
                DisplayMemberPath = hasLabels ? nameof(ChoiceOption.Label) : null,
                SelectedValue = hasLabels ? current : null,
                Foreground = Brushes.Black,
                Background = Brushes.White,
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(8, 5, 8, 5),
                MaxDropDownHeight = 280
            };
            SetSettingAutomation(combo, module, descriptor);
            combo.SelectionChanged += (_, _) =>
            {
                var selected = combo.SelectedItem switch
                {
                    ChoiceOption item => item.Value,
                    string option => option,
                    _ => null
                };
                if (selected is null) return;
                settings.Set(descriptor.Key, selected);
                ApplyModule(module);
            };
            wrapper.Children.Add(combo);
            DetailPanel.Children.Add(wrapper);
            return;
        }
        var panel = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };

        foreach (var option in descriptor.Options)
        {
            var radio = new RadioButton
            {
                Style = StyleOf("PillOption"),
                Content = descriptor.OptionLabels.GetValueOrDefault(option, option),
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

    private void AddPaletteSetting(ITweakModule module, SettingDescriptor descriptor)
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

        var configured = settings.GetString(descriptor.Key, descriptor.Default as string ?? "");
        var current = TryParseColor(configured, out _) ? configured : descriptor.Default as string ?? "";
        var palette = new List<(Button Button, string Value)>();
        var panel = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        TextBox? hexInput = null;

        void SelectColor(string value)
        {
            current = value;
            settings.Set(descriptor.Key, value);
            if (hexInput is not null) hexInput.Text = value;
            foreach (var (button, optionValue) in palette)
            {
                var selected = string.Equals(optionValue, value, StringComparison.OrdinalIgnoreCase);
                button.BorderBrush = selected ? BrushOf("AccentBrush") : Brushes.Gray;
                button.BorderThickness = new Thickness(selected ? 3 : 1);
                button.Content = selected ? "✓" : null;
                button.Foreground = IsLightColor(optionValue) ? Brushes.Black : Brushes.White;
            }
            ApplyModule(module);
        }

        foreach (var value in descriptor.Options)
        {
            if (!TryParseColor(value, out var color)) continue;
            var label = descriptor.OptionLabels.GetValueOrDefault(value, value);
            var button = new Button
            {
                Width = 30,
                Height = 30,
                Margin = new Thickness(0, 0, 7, 7),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(color),
                BorderBrush = string.Equals(value, current, StringComparison.OrdinalIgnoreCase)
                    ? BrushOf("AccentBrush")
                    : Brushes.Gray,
                BorderThickness = new Thickness(string.Equals(value, current, StringComparison.OrdinalIgnoreCase) ? 3 : 1),
                Content = string.Equals(value, current, StringComparison.OrdinalIgnoreCase) ? "✓" : null,
                Foreground = IsLightColor(value) ? Brushes.Black : Brushes.White,
                ToolTip = label
            };
            AutomationProperties.SetName(button, label);
            AutomationProperties.SetAutomationId(button, $"{module.Id}.{descriptor.Key}.{value.TrimStart('#')}");
            var selectedValue = value;
            button.Click += (_, _) => SelectColor(selectedValue);
            palette.Add((button, value));
            panel.Children.Add(button);
        }

        wrapper.Children.Add(panel);

        var customColorRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 0)
        };
        var colorDialogButton = new Button
        {
            Style = StyleOf("GhostButton"),
            Content = "Tüm renklerden seç…",
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        colorDialogButton.Click += (_, _) =>
        {
            if (TryChooseColor(current, out var selectedColor))
                SelectColor(ToHex(selectedColor));
        };
        AutomationProperties.SetName(colorDialogButton, descriptor.Label + " renk seçici");
        customColorRow.Children.Add(colorDialogButton);

        hexInput = new TextBox
        {
            Text = current,
            Width = 92,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "HEX renk kodu (ör. #4CC2FF)"
        };
        AutomationProperties.SetName(hexInput, descriptor.Label + " HEX renk kodu");
        void CommitHexColor()
        {
            var value = hexInput.Text.Trim();
            if (!value.StartsWith('#')) value = "#" + value;
            if (!TryParseColor(value, out _))
            {
                hexInput.Text = settings.GetString(descriptor.Key, descriptor.Default as string ?? "");
                return;
            }
            value = ToHex((Color)ColorConverter.ConvertFromString(value)!);
            hexInput.Text = value;
            SelectColor(value);
        }
        hexInput.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            CommitHexColor();
            e.Handled = true;
        };
        hexInput.LostKeyboardFocus += (_, _) => CommitHexColor();
        customColorRow.Children.Add(hexInput);
        wrapper.Children.Add(customColorRow);
        DetailPanel.Children.Add(wrapper);
    }

    private bool TryChooseColor(string initialColor, out Color color)
    {
        var initial = TryParseColor(initialColor, out var parsed) ? parsed : Colors.White;
        var customColors = Marshal.AllocHGlobal(16 * sizeof(uint));
        try
        {
            for (var index = 0; index < 16; index++) Marshal.WriteInt32(customColors, index * sizeof(uint), 0);
            var dialog = new ChooseColorDialog
            {
                Size = Marshal.SizeOf<ChooseColorDialog>(),
                Owner = new WindowInteropHelper(this).Handle,
                InitialColor = ToColorRef(initial),
                CustomColors = customColors,
                Flags = ChooseColorRgbInit | ChooseColorFullOpen
            };
            if (ChooseColor(ref dialog))
            {
                color = FromColorRef(dialog.InitialColor);
                return true;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(customColors);
        }

        color = default;
        return false;
    }

    private const uint ChooseColorRgbInit = 0x00000001;
    private const uint ChooseColorFullOpen = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ChooseColorDialog
    {
        public int Size;
        public IntPtr Owner;
        public IntPtr Instance;
        public uint InitialColor;
        public IntPtr CustomColors;
        public uint Flags;
        public IntPtr CustomData;
        public IntPtr Hook;
        public IntPtr TemplateName;
    }

    [DllImport("comdlg32.dll", EntryPoint = "ChooseColorW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChooseColor(ref ChooseColorDialog dialog);

    private static uint ToColorRef(Color color) => (uint)(color.R | color.G << 8 | color.B << 16);
    private static Color FromColorRef(uint color) => Color.FromRgb((byte)color, (byte)(color >> 8), (byte)(color >> 16));
    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool TryParseColor(string value, out Color color)
    {
        try
        {
            if (ColorConverter.ConvertFromString(value) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (FormatException) { }
        catch (ArgumentException) { }

        color = default;
        return false;
    }

    private static bool IsLightColor(string value)
    {
        if (!TryParseColor(value, out var color)) return false;
        var brightness = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
        return brightness >= 150;
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
        RefreshModules();
    }

    private void UpdateStatus()
    {
        var active = _modules.Count(m => m.IsActive);
        var visible = _view?.Cast<ModuleVm>().Count() ?? _modules.Count;
        StatusBar.Text = $"{visible} / {_modules.Count} modül · {active} etkin";
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
        UpdateTitleBar();

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

        UpdateTitleBar();
    }

    private void UpdateTitleBar()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var enabled = Appearance.ToneName == "Açık" ? 0 : 1;
        DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
