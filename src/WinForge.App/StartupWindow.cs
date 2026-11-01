using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WinForge.App;

internal sealed class StartupWindow : Window
{
    public StartupWindow()
    {
        Title = "WinForge";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        SetResourceReference(ForegroundProperty, "TextBrush");

        var panel = new StackPanel { Margin = new Thickness(30, 25, 30, 28) };
        panel.Children.Add(new TextBlock { Text = "WinForge başlatılıyor", FontSize = 20 });
        panel.Children.Add(new TextBlock
        {
            Text = "Ayarlar ve uygulama verileri hazırlanıyor. İlk açılışta eski verilerin taşınması biraz zaman alabilir.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 20)
        });
        panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 4 });
        var surface = new Border { CornerRadius = new CornerRadius(32), BorderThickness = new Thickness(1), Child = panel };
        surface.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        Content = surface;
        if (SystemParameters.ClientAreaAnimation)
            surface.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)));
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs args) => App.Instance.ExitApp();

    internal void Complete()
    {
        Closed -= OnClosed;
        Close();
    }
}
