using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WinForge.Modules.Extras;

internal static class FolderIconPicker
{
    internal delegate int IconDialog(IntPtr owner, StringBuilder path, uint capacity, ref int index);

    internal static bool Show(string folder, IconDialog? dialog = null)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Seçili klasör bulunamadı.");
        if (folder.Length >= 260) throw new IOException("Windows simge penceresi için klasör yolu çok uzun.");
        var icon = new StringBuilder(1024);
        var buffer = Marshal.AllocHGlobal(icon.Capacity * sizeof(char));
        try
        {
            Marshal.WriteInt16(buffer, 0);
            var settings = new FolderCustomSettings
            {
                Size = (uint)Marshal.SizeOf<FolderCustomSettings>(), Mask = 0x10,
                IconFile = buffer, IconFileLength = (uint)icon.Capacity
            };
            var result = SHGetSetFolderCustomSettings(ref settings, folder, 1); // FCS_READ
            if (result >= 0) icon.Append(Marshal.PtrToStringUni(buffer));
            var index = settings.IconIndex;
            if (result < 0 || icon.Length == 0)
            {
                icon.Clear().Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll"));
                index = 3;
            }
            else
            {
                var source = Environment.ExpandEnvironmentVariables(icon.ToString());
                if (!Path.IsPathRooted(source)) source = Path.GetFullPath(Path.Combine(folder, source));
                icon.Clear().Append(source);
            }

            if ((dialog ?? PickIconDlg)(IntPtr.Zero, icon, (uint)icon.Capacity, ref index) != 1) return false;
            var selected = Environment.ExpandEnvironmentVariables(icon.ToString());
            if (!Path.IsPathFullyQualified(selected) || !File.Exists(selected)) throw new FileNotFoundException("Seçilen simge kaynağı bulunamadı.", selected);
            icon.Clear().Append(selected);
            if (selected.Length >= 1024) throw new IOException("Seçilen simge kaynağının yolu çok uzun.");
            var characters = (selected + '\0').ToCharArray();
            Marshal.Copy(characters, 0, buffer, characters.Length);
            settings.Mask = 0x10;
            settings.IconFileLength = 0;
            settings.IconIndex = index;
            Marshal.ThrowExceptionForHR(SHGetSetFolderCustomSettings(ref settings, folder, 2)); // FCS_FORCEWRITE
            // Explorer recognizes Desktop.ini customization on read-only/system folders.
            File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.ReadOnly);
            SHChangeNotify(0x2000, 0x0005, folder, IntPtr.Zero); // SHCNE_UPDATEITEM, SHCNF_PATHW
            return true;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FolderCustomSettings
    {
        public uint Size, Mask;
        public IntPtr ViewId, WebViewTemplate;
        public uint WebViewTemplateLength;
        public IntPtr WebViewTemplateVersion, InfoTip;
        public uint InfoTipLength;
        public IntPtr ClassId;
        public uint Flags;
        public IntPtr IconFile;
        public uint IconFileLength;
        public int IconIndex;
        public IntPtr Logo;
        public uint LogoLength;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int PickIconDlg(IntPtr owner, [In, Out] StringBuilder path, uint capacity, ref int index);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetSetFolderCustomSettings(ref FolderCustomSettings settings, string path, uint readWrite);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string item, IntPtr other);
}
