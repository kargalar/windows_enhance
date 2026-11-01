namespace WinForge.Dictation;

public sealed record DictationConfiguration(string Model, string Language, bool Translate, bool Gpu);

public static class DictationAudio
{
    public const int SampleRate = 16_000;
    public const int MaximumSeconds = 300;
    public const int MaximumMessageBytes = 1024 * 1024;
}
