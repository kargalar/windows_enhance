using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.FileTools.UI;

public sealed class ToastWindow : Window
{
    private readonly Action? cancel;
    private readonly IFeatureUiSurface uiSurface;
    private string label = "A fresh file is on its way…";
    private string message = "Working…";
    private double? fraction;
    private string? outputPath;
    private bool complete;
    private bool cancelled;

    public ToastWindow(Window owner, Action? cancel, IFeatureUiService? featureUi = null)
    {
        this.cancel = cancel;
        uiSurface = featureUi?.CreateSurface("nashi-progress")
            ?? throw new InvalidOperationException("İşlem bildirimi için React arayüzü gerekli.");
        uiSurface.MessageReceived += OnUiMessage;

        Owner = owner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.Manual;
        Width = 510;
        Height = 105;
        MinHeight = 105;
        Content = uiSurface.Element;
        Loaded += (_, _) =>
        {
            Left = Math.Max(8, owner.Left + (owner.ActualWidth - Width) / 2);
            Top = Math.Max(8, owner.Top + owner.ActualHeight - ActualHeight - 30);
        };
        Closed += (_, _) => uiSurface.Dispose();
        PushState();
    }

    public void Update(Engine.OperationProgress progress)
    {
        label = progress.Label;
        fraction = progress.Fraction is { } value ? Math.Clamp(value, 0, 1) : null;
        message = fraction is { } current ? $"{current:P0}" : "Working…";
        PushState();
    }

    public void Complete(IReadOnlyList<string> outputs)
    {
        complete = true;
        cancelled = false;
        outputPath = outputs.FirstOrDefault();
        label = outputs.Count == 0
            ? "All set. You’re ready to go."
            : $"Nicely done. {outputs.Count} {(outputs.Count == 1 ? "file is" : "files are")} ready.";
        message = outputPath is null ? "Ready." : Path.GetFileName(outputPath);
        fraction = 1;
        PushState();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (WheelPresentation.Preferences.Animations && SystemParameters.ClientAreaAnimation)
            {
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(280));
                fade.Completed += (_, _) => Close();
                BeginAnimation(OpacityProperty, fade);
            }
            else
            {
                Close();
            }
        };
        timer.Start();
    }

    private void OnUiMessage(string command, JsonElement payload)
    {
        if (command == "progress.cancel" && !complete && !cancelled)
        {
            cancelled = true;
            cancel?.Invoke();
            PushState();
        }
        else if (command == "progress.reveal" && outputPath is { } path &&
                 (File.Exists(path) || Directory.Exists(path)))
        {
            MainWindow.Reveal(path);
        }
    }

    private void PushState() => uiSurface.SetState(new
    {
        label,
        message,
        fraction,
        complete,
        cancelled,
        canReveal = complete && outputPath is { } path && (File.Exists(path) || Directory.Exists(path))
    });
}
