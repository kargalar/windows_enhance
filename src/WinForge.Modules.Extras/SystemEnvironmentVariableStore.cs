using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace WinForge.Modules.Extras;

internal sealed class SystemEnvironmentVariableStore : IEnvironmentVariableStore
{
    private const string SystemEnvironmentKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

    public IReadOnlyList<UserEnvironmentVariable> Read()
    {
        using var key = Registry.LocalMachine.OpenSubKey(SystemEnvironmentKey, writable: false);
        if (key is null) return Array.Empty<UserEnvironmentVariable>();

        return key.GetValueNames()
            .Select(name => ReadEntry(key, name))
            .Where(entry => entry is not null)
            .Cast<UserEnvironmentVariable>()
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Save(IReadOnlyCollection<string> originalNames, IReadOnlyCollection<UserEnvironmentVariable> desired)
    {
        var entries = UserEnvironmentVariableStore.Validate(desired);
        var payload = JsonSerializer.Serialize(new SystemVariableRequest(originalNames.ToArray(), entries));
        var payloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        var script = BuildScript(payloadBase64);
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) throw new FileNotFoundException("Windows PowerShell bulunamadı.", powershell);

        using var process = Process.Start(new ProcessStartInfo(powershell)
        {
            Arguments = "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + encodedScript,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        }) ?? throw new InvalidOperationException("Yönetici işlemi başlatılamadı.");

        if (!process.WaitForExit(120_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("Sistem değişkenleri kaydı zaman aşımına uğradı.");
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Yönetici işlemi sistem değişkenlerini kaydedemedi (kod {process.ExitCode}).");
    }

    internal static string BuildScript(string payloadBase64)
    {
        if (payloadBase64.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '+' and not '/' and not '='))
            throw new FormatException("Sistem değişkeni isteği geçersiz kodlandı.");

        return $$"""
            $ErrorActionPreference = 'Stop'
            try {
                $json = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('{{payloadBase64}}'))
                $request = ConvertFrom-Json -InputObject $json
                $keyPath = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment'
                $key = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey($keyPath, $true)
                if ($null -eq $key) { throw 'Sistem ortam değişkenleri anahtarı açılamadı.' }

                $desiredNames = @{}
                foreach ($entry in @($request.Entries)) { $desiredNames[[string]$entry.Name] = $true }
                $names = @(@($request.OriginalNames) + @($request.Entries | ForEach-Object { [string]$_.Name }) | Select-Object -Unique)
                $before = @{}
                foreach ($name in $names) {
                    if ($key.GetValueNames() -contains $name) {
                        $kind = $key.GetValueKind($name).ToString()
                        if ($kind -notin @('String', 'ExpandString')) { throw "Desteklenmeyen kayıt türü: $name" }
                        $before[$name] = @{ Exists = $true; Value = $key.GetValue($name, '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames); Kind = $kind }
                    } else {
                        $before[$name] = @{ Exists = $false }
                    }
                }

                try {
                    foreach ($name in @($request.OriginalNames)) {
                        if (-not $desiredNames.ContainsKey([string]$name)) { $key.DeleteValue([string]$name, $false) }
                    }
                    foreach ($entry in @($request.Entries)) {
                        $kind = if ($entry.Expandable) { [Microsoft.Win32.RegistryValueKind]::ExpandString } else { [Microsoft.Win32.RegistryValueKind]::String }
                        $key.SetValue([string]$entry.Name, [string]$entry.Value, $kind)
                    }
                } catch {
                    foreach ($name in $names) {
                        $snapshot = $before[$name]
                        if ($snapshot.Exists) {
                            $restoreKind = [System.Enum]::Parse([Microsoft.Win32.RegistryValueKind], [string]$snapshot.Kind)
                            $key.SetValue([string]$name, [string]$snapshot.Value, $restoreKind)
                        } else {
                            $key.DeleteValue([string]$name, $false)
                        }
                    }
                    throw
                } finally {
                    $key.Dispose()
                }

                Add-Type -Namespace WinForgeNative -Name EnvironmentBroadcast -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode)] public static extern System.IntPtr SendMessageTimeout(System.IntPtr hWnd, uint Msg, System.IntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out System.IntPtr result);'
                $result = [System.IntPtr]::Zero
                [WinForgeNative.EnvironmentBroadcast]::SendMessageTimeout([System.IntPtr]65535, 26, [System.IntPtr]::Zero, 'Environment', 2, 5000, [ref]$result) | Out-Null
                exit 0
            } catch {
                [System.Console]::Error.WriteLine($_.Exception.Message)
                exit 1
            }
            """;
    }

    private static UserEnvironmentVariable? ReadEntry(RegistryKey key, string name)
    {
        try
        {
            var kind = key.GetValueKind(name);
            if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString)) return null;
            var value = key.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
            return new UserEnvironmentVariable(name, value, kind == RegistryValueKind.ExpandString);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return null;
        }
    }

    private sealed record SystemVariableRequest(string[] OriginalNames, IReadOnlyCollection<UserEnvironmentVariable> Entries);
}
