using System.Windows;
using System.Text.Json;
using WinForge.Core;
using WinForge.FileTools.Engine;
namespace WinForge.FileTools.UI;

public sealed class ToolSetupWindow : Window
{
    private readonly Func<ToolSetupStatus> probe;
    private readonly Func<ToolGroup, IProgress<OperationProgress>, CancellationToken, Task> install;
    private readonly Action<bool> workingChanged;
    private readonly IFeatureUiSurface uiSurface;
    private CancellationTokenSource? cancellation;
    private string status = "Download the missing tools to enable all conversions, previews and web downloads. Tools already found on your computer are kept.";
    private double progress;
    private bool indeterminate = true;
    internal bool Working { get; private set; }
    private bool previouslyReady;
    internal ToolSetupStatus Status => probe();
    public ToolSetupWindow(Window owner, Toolchain media, OfficeRuntime office, Action<bool> workingChanged)
        : this(owner, () => new(media.Ready, office.Executable is not null),
            (group, report, token) => Task.Run(() => group == ToolGroup.Media ? media.InstallAsync(report, token) : office.InstallAsync(report, token),token), workingChanged,
            (owner as MainWindow)?.FeatureUi) { }
    internal ToolSetupWindow(Window owner, Func<ToolSetupStatus> probe,
        Func<ToolGroup, IProgress<OperationProgress>, CancellationToken, Task> install, Action<bool>? workingChanged = null,
        IFeatureUiService? featureUi = null)
    {
        this.probe = probe; this.install = install; this.workingChanged = workingChanged ?? (_ => { });
        Owner = owner; Title = "Set up Nashi tools"; Width = 646; Height = 690; MinWidth = 590; MinHeight = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        L10n.Bind(this,TitleProperty,"Set up Nashi tools");
        Closing += (_, e) => { if (Working) { e.Cancel = true; cancellation?.Cancel(); } };
        uiSurface = featureUi?.CreateSurface("nashi-setup")
            ?? throw new InvalidOperationException("Nashi kurulumu için React arayüzü gerekli.");
        uiSurface.MessageReceived += OnUiMessage;
        Content = uiSurface.Element;
        Closed += (_, _) => uiSurface.Dispose();
        Refresh();
    }
    private void Refresh()
    {
        var ready = Status;
        if (ready.Ready && !Working) status = "Ready. You can go back to your files.";
        else if (previouslyReady && !Working) status = "Download the missing tools to enable all conversions, previews and web downloads. Tools already found on your computer are kept.";
        previouslyReady=ready.Ready;
        PushUiState();
    }
    internal async Task StartSetupAsync(ToolGroup? selected = null)
    {
        if (Working) return;
        var pending = Status.Missing.Where(g => selected is null || g == selected).ToArray(); if (pending.Length == 0) { Refresh(); return; }
        Working = true; cancellation = new(); workingChanged(true); indeterminate = true; Refresh();
        var token = cancellation.Token;
        try
        {
            for (var i = 0; i < pending.Length; i++)
            {
                token.ThrowIfCancellationRequested(); var index = i;
                var report = new Progress<OperationProgress>(p =>
                {
                    if (!Working || token.IsCancellationRequested) return;
                    indeterminate = p.Fraction is null;
                    if (p.Fraction is { } value) progress = (index + value) / pending.Length;
                    status = p.Label;
                    PushUiState();
                });
                await install(pending[i], report, token); Refresh();
            }
            if (Status.Missing.Any(pending.Contains)) throw new InvalidDataException("The downloaded tools are incomplete. Please try setup again.");
            status = Status.Ready ? "Ready. You can go back to your files." : "One tool group is ready. Download the remaining tools to finish setup.";
        }
        catch (OperationCanceledException) { status = "Setup cancelled. Existing tools were kept."; }
        catch (Exception error) { status = error.Message; }
        finally { Working = false; cancellation.Dispose(); cancellation = null; workingChanged(false); Refresh(); }
    }

    private void OnUiMessage(string command,JsonElement payload)
    {
        switch(command)
        {
            case "setup.install":
                var group=payload.TryGetProperty("group",out var value)&&value.GetString()=="office"?ToolGroup.Office:ToolGroup.Media;
                _=StartSetupAsync(group);
                break;
            case "setup.continue":if(Status.Ready)Close();else _=StartSetupAsync();break;
            case "setup.cancel":cancellation?.Cancel();break;
            case "setup.close":if(!Working)Close();else cancellation?.Cancel();break;
            case "setup.source":MainWindow.OpenUrl("https://github.com/orkunisitmak/nashi-app/blob/main/docs/dependencies.md");break;
        }
    }

    private void PushUiState()
    {
        var ready=Status;
        uiSurface.SetState(new
        {
            mediaReady=ready.MediaReady,
            officeReady=ready.OfficeReady,
            ready=ready.Ready,
            working=Working,
            progress,
            indeterminate,
            status,
            missing=ready.Missing.Select(group=>group==ToolGroup.Media?"media":"office").ToArray()
        });
    }
}
