using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WinForge.Modules.Extras;

internal sealed class HostsFileStore
{
    private const int SaveTimeoutMilliseconds = 120_000;
    private readonly string _path;

    public HostsFileStore() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
        "drivers", "etc", "hosts")) { }

    internal HostsFileStore(string path) => _path = path;

    public HostsFileSnapshot Load()
    {
        var bytes = File.ReadAllBytes(_path);
        var hasUtf8Bom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        var payload = hasUtf8Bom ? bytes.AsSpan(Encoding.UTF8.Preamble.Length).ToArray() : bytes;
        Encoding encoding;
        string text;
        try
        {
            encoding = new UTF8Encoding(false, true);
            text = encoding.GetString(payload);
        }
        catch (DecoderFallbackException)
        {
            encoding = Encoding.Latin1;
            text = encoding.GetString(payload);
        }

        return new HostsFileSnapshot(HostsFileDocument.Parse(text), Convert.ToHexString(SHA256.HashData(bytes)), encoding, hasUtf8Bom);
    }

    public void Save(HostsFileDocument document, HostsFileSnapshot snapshot)
    {
        var content = snapshot.Encoding.GetBytes(document.Render());
        var bytes = snapshot.HasUtf8Bom ? Encoding.UTF8.Preamble.ToArray().Concat(content).ToArray() : content;
        var request = JsonSerializer.Serialize(new HostsFileSaveRequest(snapshot.Sha256, Convert.ToBase64String(bytes)));
        var payloadPath = Path.Combine(WinForge.Core.AppData.TemporaryDirectory, "WinForge-hosts-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(payloadPath, request, new UTF8Encoding(false));
            RunElevatedSave(payloadPath);
        }
        finally
        {
            try { File.Delete(payloadPath); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static string BuildElevatedScript(string payloadPathBase64)
    {
        if (payloadPathBase64.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '+' and not '/' and not '='))
            throw new FormatException("Hosts file isteği geçersiz kodlandı.");

        return $$"""
            $ErrorActionPreference = 'Stop'
            try {
                $payloadPath = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('{{payloadPathBase64}}'))
                $request = Get-Content -LiteralPath $payloadPath -Raw | ConvertFrom-Json
                $hostsPath = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
                $currentBytes = [System.IO.File]::ReadAllBytes($hostsPath)
                $sha = [System.Security.Cryptography.SHA256]::Create()
                try { $currentHash = [System.BitConverter]::ToString($sha.ComputeHash($currentBytes)).Replace('-', '') }
                finally { $sha.Dispose() }
                if (-not [string]::Equals($currentHash, [string]$request.ExpectedSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
                    throw 'Hosts dosyası bu pencerede açıldıktan sonra değişti. Yeniden yükleyip tekrar deneyin.'
                }

                $backupPath = Join-Path ([System.IO.Path]::GetDirectoryName($hostsPath)) ('hosts_WinForgeBackup_' + [System.DateTime]::Now.ToString('yyyyMMddHHmmssfff'))
                [System.IO.File]::Copy($hostsPath, $backupPath, $false)
                $temporaryPath = $hostsPath + '.winforge-' + [System.Guid]::NewGuid().ToString('N') + '.tmp'
                try {
                    $newBytes = [System.Convert]::FromBase64String([string]$request.ContentBase64)
                    [System.IO.File]::WriteAllBytes($temporaryPath, $newBytes)
                    [System.IO.File]::Replace($temporaryPath, $hostsPath, $null)
                } finally {
                    if ([System.IO.File]::Exists($temporaryPath)) { [System.IO.File]::Delete($temporaryPath) }
                }

                try {
                    $backups = @(Get-ChildItem -LiteralPath ([System.IO.Path]::GetDirectoryName($hostsPath)) -Filter 'hosts_WinForgeBackup_*' -File | Sort-Object LastWriteTime -Descending)
                    $cutoff = [System.DateTime]::Now.AddDays(-15)
                    for ($index = 5; $index -lt $backups.Count; $index++) {
                        if ($backups[$index].LastWriteTime -lt $cutoff) { Remove-Item -LiteralPath $backups[$index].FullName -Force }
                    }
                } catch { }
                exit 0
            } catch {
                [System.Console]::Error.WriteLine($_.Exception.Message)
                exit 1
            }
            """;
    }

    private static void RunElevatedSave(string payloadPath)
    {
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) throw new FileNotFoundException("Windows PowerShell bulunamadı.", powershell);
        var script = BuildElevatedScript(Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadPath)));
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var process = Process.Start(new ProcessStartInfo(powershell)
        {
            Arguments = "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + encodedScript,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        }) ?? throw new InvalidOperationException("Yönetici işlemi başlatılamadı.");

        if (!process.WaitForExit(SaveTimeoutMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("Hosts dosyası kaydı zaman aşımına uğradı.");
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Hosts dosyası kaydedilemedi (yönetici işlemi kodu {process.ExitCode}).");
    }

    private sealed record HostsFileSaveRequest(string ExpectedSha256, string ContentBase64);
}

internal sealed record HostsFileSnapshot(HostsFileDocument Document, string Sha256, Encoding Encoding, bool HasUtf8Bom);
