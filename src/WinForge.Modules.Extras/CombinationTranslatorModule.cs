using System.Text.Json;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class CombinationTranslatorModule : ShortcutActionModule
{
    private readonly KeyboardManagerModule _engine = new();
    public override string Id => "combination-translator";
    public override string Name => "Tuş Kombinasyonu Çevirici";
    protected override IReadOnlyDictionary<string, string> Actions => new Dictionary<string, string> { ["translate"] = "Kombinasyonu çevir" };
    protected override IEnumerable<ShortcutActionRule> Defaults => [new("example", "Ctrl+D → Alt+W", false, "Ctrl+D", "translate", "Alt+W")];

    internal static KeyboardManagerModule.ShortcutMapping ParseMapping(ShortcutActionRule rule) =>
        KeyboardMappingEditor.ParseShortcutMappings($"{rule.Shortcut} = {rule.Value}").Single();

    protected override void ValidateTranslation(ShortcutActionRule rule)
    {
        var mapping = ParseMapping(rule);
        if (mapping.FromKey == mapping.ToKey && mapping.FromModifiers == mapping.ToModifiers)
            throw new FormatException("Kaynak ve hedef kombinasyon farklı olmalıdır.");
    }

    protected override void Clear() => _engine.Deactivate();
    protected override void Bind()
    {
        if (!IsActive || IsCapturing) return;
        _engine.Initialize(new TranslationContext(Context, Rules.Where(r => r.Enabled).Select(ParseMapping).ToList()));
        _engine.Activate();
    }

    private sealed class TranslationContext(IModuleContext parent, List<KeyboardManagerModule.ShortcutMapping> mappings) : IModuleContext, IModuleSettings
    {
        public IModuleSettings Settings => this;
        public ILogger Log => parent.Log;
        public IHotkeyService Hotkeys => parent.Hotkeys;
        public void Notify(string message) => parent.Notify(message);
        public bool GetBool(string key, bool fallback = false) => key == "hook-enabled" || fallback;
        public int GetInt(string key, int fallback = 0) => fallback;
        public string GetString(string key, string fallback = "") => key == "shortcut-mappings" ? JsonSerializer.Serialize(mappings) : fallback;
        public void Set(string key, object? value) => throw new NotSupportedException();
    }
}
