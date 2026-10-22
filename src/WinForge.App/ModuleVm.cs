using System.ComponentModel;
using WinForge.Core;

namespace WinForge.App;

public sealed class ModuleVm : INotifyPropertyChanged
{
    public ModuleVm(ITweakModule module) => Module = module;

    public ITweakModule Module { get; }

    public string Name => Module.Name;
    public string Category => Module.Category;
    public string Description => Module.Description;
    public string ShortDescription => Module.Description;
    public bool IsActive => Module.IsActive;
    public string StatusText => IsActive ? "Etkin" : "Kapalı";

    public string Glyph => Category switch
    {
        "Taskbar" => "\uE7C4",
        "Verimlilik" => "\uE945",
        "Sistem" => "\uE713",
        "Uygulamalar" => "\uE71D",
        _ => "\uE713"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
    }
}
