namespace WinForge.Core;

internal static class CoreAudioDeviceFactory
{
    private static readonly Guid EnumeratorClassId = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    // Activate as object: COM may return an existing RCW created by another
    // audio service, which cannot be cast to a second managed coclass type.
    internal static object CreateEnumerator() => Activator.CreateInstance(
        Type.GetTypeFromCLSID(EnumeratorClassId, throwOnError: true)!)
        ?? throw new InvalidOperationException("Windows ses aygıtları açılamadı.");
}
