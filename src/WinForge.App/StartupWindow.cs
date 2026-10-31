using System;
using System.Windows;
using System.Windows.Controls;

namespace WinForge.App;

internal sealed class StartupWindow : Window
{
    public StartupWindow()
    {
        Title = "WinForge";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "BgBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new TextBlock { Text = "WinForge başlatılıyor", FontSize = 20 });
        panel.Children.Add(new TextBlock
        {
            Text = "Ayarlar ve uygulama verileri hazırlanıyor. İlk açılışta eski verilerin taşınması biraz zaman alabilir.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 20)
        });
        panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 4 });
        Content = panel;
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs args) => App.Instance.ExitApp();

    internal void Complete()
    {
        Closed -= OnClosed;
        Close();
    }
}
