using System.Text.Json;
using System.Windows;
using WinForge.Core;

namespace WinForge.FileTools.UI;

public sealed class PdfOrganizer : Window
{
    private sealed class Page(int number, byte[] image)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public int Number { get; } = number;
        public byte[] Image { get; } = image;
        public int Rotation { get; set; }
    }

    private readonly List<Page> pages = [];
    private readonly HashSet<Guid> selectedIds = [];
    private readonly IFeatureUiSurface uiSurface;
    private bool uniformWidth;

    public bool UniformWidth => uniformWidth;
    public int[] Pages => pages.Select(page => page.Number).ToArray();
    public Dictionary<int, int> Rotations => pages.GroupBy(page => page.Number)
        .ToDictionary(group => group.Key, group => group.First().Rotation);
    public int[] PageRotations => pages.Select(page => page.Rotation).ToArray();

    private PdfOrganizer(Window owner, IFeatureUiService? featureUi)
    {
        Owner = owner;
        Title = "Organize PDF · Nashi";
        Width = 1000;
        Height = 760;
        MinWidth = 700;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        uiSurface = featureUi?.CreateSurface("nashi-pdf-organizer")
            ?? throw new InvalidOperationException("PDF düzenleyici için React arayüzü gerekli.");
        uiSurface.MessageReceived += OnUiMessage;
        Content = uiSurface.Element;
        Closed += (_, _) => uiSurface.Dispose();
    }

    public static async Task<PdfOrganizer> CreateAsync(Window owner, string file,
        IFeatureUiService? featureUi = null)
    {
        var organizer = new PdfOrganizer(owner, featureUi);
        var images = await NativePdf.RenderAsync(file, 72);
        for (var index = 0; index < images.Length; index++)
            organizer.pages.Add(new(index + 1, images[index]));
        organizer.PushState();
        return organizer;
    }

    private Page[] Chosen() => pages.Where(page => selectedIds.Contains(page.Id)).ToArray();

    private void Move(int delta)
    {
        var selected = Chosen();
        foreach (var page in delta < 0 ? selected : selected.Reverse())
        {
            var index = pages.IndexOf(page);
            var next = index + delta;
            if (next < 0 || next >= pages.Count) continue;
            pages.RemoveAt(index);
            pages.Insert(next, page);
        }
        PushState();
    }

    private void Rotate()
    {
        foreach (var page in Chosen()) page.Rotation = (page.Rotation + 90) % 360;
        PushState();
    }

    private void Duplicate()
    {
        foreach (var page in Chosen().Reverse())
        {
            var index = pages.IndexOf(page);
            pages.Insert(index + 1, new(page.Number, page.Image) { Rotation = page.Rotation });
        }
        PushState();
    }

    private void Remove()
    {
        var selected = Chosen();
        if (pages.Count - selected.Length < 1) return;
        foreach (var page in selected) pages.Remove(page);
        selectedIds.Clear();
        PushState();
    }

    private void OnUiMessage(string command, JsonElement payload)
    {
        switch (command)
        {
            case "pdf-organizer.select":
                selectedIds.Clear();
                if (payload.TryGetProperty("ids", out var ids))
                    foreach (var id in ids.EnumerateArray())
                        if (Guid.TryParse(id.GetString(), out var parsed)) selectedIds.Add(parsed);
                PushState();
                break;
            case "pdf-organizer.move": Move(payload.GetProperty("delta").GetInt32()); break;
            case "pdf-organizer.rotate": Rotate(); break;
            case "pdf-organizer.duplicate": Duplicate(); break;
            case "pdf-organizer.remove": Remove(); break;
            case "pdf-organizer.uniform":
                uniformWidth = payload.GetProperty("value").GetBoolean();
                PushState();
                break;
            case "pdf-organizer.save":
                if (pages.Count > 0) DialogResult = true;
                break;
            case "pdf-organizer.cancel": DialogResult = false; break;
        }
    }

    private void PushState() => uiSurface.SetState(new
    {
        uniformWidth,
        pages = pages.Select(page => new
        {
            id = page.Id,
            number = page.Number,
            rotation = page.Rotation,
            image = "data:image/png;base64," + Convert.ToBase64String(page.Image)
        }).ToArray(),
        selected = selectedIds.Select(id => id.ToString()).ToArray()
    });
}
