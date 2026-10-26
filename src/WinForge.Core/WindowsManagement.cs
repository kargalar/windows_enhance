using System.Collections;
using System.Runtime.InteropServices;

namespace WinForge.Core;

/// <summary>Owns a Windows WMI Automation connection; no managed WMI package is required.</summary>
public sealed class WindowsManagement : IDisposable
{
    private object? _services;

    public WindowsManagement(string namespacePath)
    {
        var locatorType = Type.GetTypeFromCLSID(new Guid("76A64158-CB41-11D1-8B02-00600806D9B6"), true)!;
        var locator = Activator.CreateInstance(locatorType)!;
        try { _services = ((dynamic)locator).ConnectServer(".", namespacePath); }
        finally { Release(locator); }
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string query, params string[] columns)
    {
        ObjectDisposedException.ThrowIf(_services is null, this);
        // Automation's .NET enumerator can clone/reset; forward-only WMI enumerators reject that.
        object results = ((dynamic)_services!).ExecQuery(query, "WQL", 0);
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        IEnumerator? enumerator = null;
        try
        {
            enumerator = ((IEnumerable)results).GetEnumerator();
            while (enumerator.MoveNext())
            {
                var item = enumerator.Current!;
                try
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var column in columns) row[column] = ReadProperty(item, column);
                    rows.Add(row);
                }
                finally { Release(item); }
            }
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
            Release(enumerator);
            Release(results);
        }
        return rows;
    }

    public uint Invoke(string instancePath, string className, string methodName, IReadOnlyDictionary<string, object> arguments)
    {
        ObjectDisposedException.ThrowIf(_services is null, this);
        object? definition = null, methods = null, method = null, template = null, input = null, output = null;
        try
        {
            definition = ((dynamic)_services!).Get(className);
            methods = ((dynamic)definition).Methods_;
            method = ((dynamic)methods).Item(methodName);
            template = ((dynamic)method).InParameters;
            input = ((dynamic)template).SpawnInstance_();
            foreach (var (name, value) in arguments)
            {
                object properties = ((dynamic)input).Properties_;
                object? property = null;
                try { property = ((dynamic)properties).Item(name); ((dynamic)property).Value = value; }
                finally { Release(property); Release(properties); }
            }
            output = ((dynamic)_services!).ExecMethod(instancePath, methodName, input);
            return Convert.ToUInt32(ReadProperty(output, "ReturnValue"), System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            Release(output); Release(input); Release(template); Release(method); Release(methods); Release(definition);
        }
    }

    private static object? ReadProperty(object instance, string name)
    {
        object properties = name.StartsWith("__", StringComparison.Ordinal)
            ? ((dynamic)instance).SystemProperties_ : ((dynamic)instance).Properties_;
        object? property = null;
        try { property = ((dynamic)properties).Item(name); return ((dynamic)property).Value; }
        finally { Release(property); Release(properties); }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    public void Dispose() => Release(Interlocked.Exchange(ref _services, null));
}
