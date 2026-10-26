using System.IO;
using System.Runtime.InteropServices;

namespace WinForge.Core;

/// <summary>Reads the exact grouping identity assigned by the Windows shell, including classic applications.</summary>
internal sealed class WindowsApplicationResolver : IDisposable
{
    private static readonly Guid ResolverClassId = new("660B90C8-73A9-4B58-8CAE-355B7F55341B");
    private static readonly Guid FactoryId = new("00000001-0000-0000-C000-000000000046");
    private static readonly Guid ResolverId = new("DE25675A-72DE-44B4-9373-05170450C140");
    private static readonly Lazy<IntPtr> Library = new(() => NativeLibrary.Load(Path.Combine(Environment.SystemDirectory, "appresolver.dll")));
    private IApplicationResolver? _resolver;

    private WindowsApplicationResolver(IApplicationResolver resolver) => _resolver = resolver;

    internal static WindowsApplicationResolver? TryCreate()
    {
        IClassFactory? factory = null;
        IntPtr factoryPointer = IntPtr.Zero;
        IntPtr resolverPointer = IntPtr.Zero;
        try
        {
            var createFactory = Marshal.GetDelegateForFunctionPointer<GetClassObject>(NativeLibrary.GetExport(Library.Value, "DllGetClassObject"));
            var classId = ResolverClassId;
            var factoryId = FactoryId;
            if (createFactory(ref classId, ref factoryId, out factoryPointer) < 0 || factoryPointer == IntPtr.Zero) return null;
            factory = (IClassFactory)Marshal.GetObjectForIUnknown(factoryPointer);
            var resolverId = ResolverId;
            if (factory.CreateInstance(IntPtr.Zero, ref resolverId, out resolverPointer) < 0 || resolverPointer == IntPtr.Zero) return null;
            return new((IApplicationResolver)Marshal.GetObjectForIUnknown(resolverPointer));
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or DllNotFoundException or EntryPointNotFoundException)
        { return null; }
        finally
        {
            if (resolverPointer != IntPtr.Zero) Marshal.Release(resolverPointer);
            if (factoryPointer != IntPtr.Zero) Marshal.Release(factoryPointer);
            if (factory is not null && Marshal.IsComObject(factory)) Marshal.ReleaseComObject(factory);
        }
    }

    internal string ReadWindowId(IntPtr window)
    {
        var resolver = _resolver;
        if (resolver is null) return "";
        IntPtr text = IntPtr.Zero;
        try
        {
            if (resolver.GetAppIdForWindow(window, out text, out _, out _, out _) < 0 || text == IntPtr.Zero) return "";
            return Marshal.PtrToStringUni(text)?.Trim() ?? "";
        }
        catch (COMException) { return ""; }
        finally { if (text != IntPtr.Zero) Marshal.FreeCoTaskMem(text); }
    }

    public void Dispose()
    {
        var resolver = Interlocked.Exchange(ref _resolver, null);
        if (resolver is not null && Marshal.IsComObject(resolver)) Marshal.ReleaseComObject(resolver);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetClassObject(ref Guid classId, ref Guid interfaceId, out IntPtr factory);

    [ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IClassFactory
    {
        [PreserveSig] int CreateInstance(IntPtr outer, ref Guid interfaceId, out IntPtr instance);
        [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool locked);
    }

    // Windows 8+ interface prefix. No unneeded methods or appresolver implementation are included.
    [ComImport, Guid("DE25675A-72DE-44B4-9373-05170450C140"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationResolver
    {
        [PreserveSig] int GetAppIdForShortcut(IntPtr item, out IntPtr applicationId);
        [PreserveSig] int GetAppIdForShortcutObject(IntPtr link, IntPtr item, out IntPtr applicationId);
        [PreserveSig] int GetAppIdForWindow(IntPtr window, out IntPtr applicationId,
            [MarshalAs(UnmanagedType.Bool)] out bool pinningPrevented,
            [MarshalAs(UnmanagedType.Bool)] out bool explicitId,
            [MarshalAs(UnmanagedType.Bool)] out bool embeddedShortcut);
    }
}
