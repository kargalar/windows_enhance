using System.Runtime.InteropServices;
using System.Windows;

namespace WinForge.ClipboardLine;

internal static class ClipboardAccess
{
    private const int CannotOpenClipboard = unchecked((int)0x800401D0);
    private const int MaximumAttempts = 10;
    private const int RetryMilliseconds = 50;

    internal static void SetData(IDataObject data)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { System.Windows.Clipboard.SetDataObject(data, true); return; }
            catch (ExternalException error) when (error.HResult == CannotOpenClipboard && attempt + 1 < MaximumAttempts)
            {
                Thread.Sleep(RetryMilliseconds);
            }
        }
    }
}
