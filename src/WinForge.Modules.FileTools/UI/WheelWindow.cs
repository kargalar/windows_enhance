using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinForge.Core;
using WinForge.FileTools.Engine;

namespace WinForge.FileTools.UI;

public sealed record ToolChoice(string Id, string Title, string Symbol, WindowPlacement? Placement = null);

/// <summary>A native window shell around the shared React radial menu.</summary>
public sealed class WheelWindow : Window
{
    private const double CanvasSize = 540;
    private readonly IReadOnlyList<ToolChoice> choices;
    private readonly Action<ToolChoice> select;
    private readonly IFeatureUiSurface uiSurface;
    private readonly bool externalDrag;
    private int selected = -1;

    internal int SelectedIndex => selected;

    internal void HoverScreen(Point point)
    {
        var local = PointFromScreen(point);
        var factor = CanvasSize / Math.Max(ActualWidth, 1);
        var delta = new Point(local.X * factor - CanvasSize / 2, local.Y * factor - CanvasSize / 2);
        var distance = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
        if (distance < 108 || distance > 250)
        {
            Highlight(-1);
            return;
        }

        var count = Math.Min(12, choices.Count);
        var angle = (Math.Atan2(delta.Y, delta.X) * 180 / Math.PI - WheelPresentation.StartAngle(count) + 360) % 360;
        var index = (int)Math.Floor((angle + 180d / count) % 360 / (360d / count));
        Highlight(index);
    }

    internal bool Navigate(Key key)
    {
        if (key == Key.Escape)
        {
            Close();
            return true;
        }
        var count = Math.Min(12, choices.Count);
        if (count == 0) return false;
        if (key is Key.Right or Key.Down) Highlight((selected + 1 + count) % count);
        else if (key is Key.Left or Key.Up) Highlight((selected - 1 + count) % count);
        else if (key is Key.Enter or Key.Space)
        {
            var choice = choices[Math.Clamp(selected, 0, count - 1)];
            Close();
            select(choice);
        }
        else return false;
        return true;
    }

    private void Highlight(int index)
    {
        if (selected == index) return;
        selected = index;
        PushState();
        if (index >= 0) Feedback.Play();
    }

    public WheelWindow(Window? owner, IReadOnlyList<ToolChoice> tools, Action<ToolChoice> select,
        bool externalDrag = false, IFeatureUiService? featureUi = null)
    {
        choices = tools.Take(12).ToArray();
        this.select = select;
        this.externalDrag = externalDrag;
        if (owner is not null) Owner = owner;
        Title = externalDrag ? "Nashi Window Management" : "Nashi tools";
        Width = WheelPresentation.Diameter * WheelPresentation.Preferences.WheelScale;
        Height = Width;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = externalDrag ? WindowStartupLocation.Manual : WindowStartupLocation.CenterOwner;
        if (externalDrag)
        {
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
        }

        uiSurface = featureUi?.CreateSurface("nashi-wheel")
            ?? throw new InvalidOperationException("Dosya tekerleği için React arayüzü gerekli.");
        Content = uiSurface.Element;
        uiSurface.MessageReceived += OnUiMessage;
        Closed += (_, _) =>
        {
            uiSurface.MessageReceived -= OnUiMessage;
            uiSurface.Dispose();
        };

        PreviewKeyDown += (_, e) => e.Handled = Navigate(e.Key);
        Loaded += (_, _) =>
        {
            WheelPresentation.Animate(this);
            if (!this.externalDrag && choices.Count > 0) PushState();
        };
        var closing = false;
        Closing += (_, _) => closing = true;
        Deactivated += (_, _) =>
        {
            if (!this.externalDrag && !closing && IsVisible) Close();
        };
        PushState();
    }

    private void OnUiMessage(string command, JsonElement payload)
    {
        switch (command)
        {
            case "wheel.highlight":
                if (payload.TryGetProperty("index", out var highlighted) && highlighted.TryGetInt32(out var index))
                    Highlight(Math.Clamp(index, -1, choices.Count - 1));
                break;
            case "wheel.select":
                if (payload.TryGetProperty("index", out var chosen) && chosen.TryGetInt32(out var choiceIndex) && choiceIndex >= 0 && choiceIndex < choices.Count)
                {
                    var choice = choices[choiceIndex];
                    Close();
                    select(choice);
                }
                break;
            case "wheel.key":
                if (payload.TryGetProperty("key", out var keyValue) && Enum.TryParse<Key>(keyValue.GetString(), true, out var key))
                    Navigate(key);
                break;
            case "wheel.close": Close(); break;
        }
    }

    private void PushState()
    {
        uiSurface.SetState(new
        {
            externalDrag,
            finish = WheelPresentation.Preferences.WheelFinish,
            selected,
            center = externalDrag ? "Arrange window" : choices.All(choice => choice.Id.StartsWith("convert:", StringComparison.Ordinal)) ? "Convert" : choices.Any(choice => choice.Id == "float") ? "File actions" : "Tools",
            subtitle = externalDrag ? "Release to place · Esc cancels" : "Arrow keys · Enter to choose · Esc to close",
            startAngle = WheelPresentation.StartAngle(choices.Count),
            choices = choices.Select(choice => new { choice.Id, choice.Title, choice.Symbol }).ToArray()
        });
    }
}

internal static class Feedback
{
    internal static bool Enabled { get; set; } = true;
    private static MediaPlayer? player;
    private static DateTime last;

    internal static void Stop()
    {
        player?.Stop();
        player?.Close();
        player = null;
    }

    public static void Play()
    {
        if (!Enabled || (DateTime.UtcNow - last).TotalMilliseconds < 70) return;
        last = DateTime.UtcNow;
        try
        {
            var directory = Path.Combine(WinForge.Core.AppData.DirectoryPath, "Nashi");
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, "WheelSelection.wav");
            if (!File.Exists(file))
            {
                using var source = Application.GetResourceStream(new Uri("pack://application:,,,/WinForge.Modules.FileTools;component/Assets/WheelSelection.wav")).Stream;
                using var target = File.Create(file);
                source.CopyTo(target);
            }
            if (player is null)
            {
                var created = new MediaPlayer { Volume = .65 };
                player = created;
                created.MediaOpened += (_, _) =>
                {
                    if (Enabled && ReferenceEquals(player, created)) created.Play();
                };
                created.Open(new Uri(file));
            }
            else
            {
                player.Position = TimeSpan.Zero;
                player.Play();
            }
        }
        catch (IOException) { }
        catch (InvalidOperationException) { }
    }
}
