using System.IO;
using System.Security;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class WindowsSecurityNotificationsModule : ITweakModule
{
    private const string PolicyPath = @"SOFTWARE\Policies\Microsoft\Windows Defender Security Center\Notifications";
    private const string StatePath = @"SOFTWARE\WinForge\WindowsSecurityNotifications";
    private const string PolicyValue = "DisableEnhancedNotifications";
    private const string OwnershipValue = "OwnsPolicy";

    private readonly RegistryHive _hive;
    private readonly string _policyPath;
    private readonly string _statePath;

    public WindowsSecurityNotificationsModule() : this(RegistryHive.LocalMachine, PolicyPath, StatePath) { }

    internal WindowsSecurityNotificationsModule(RegistryHive hive, string policyPath, string statePath)
    {
        _hive = hive;
        _policyPath = policyPath;
        _statePath = statePath;
    }

    public string Id => "windows-security-notifications";
    public string Name => "Windows Güvenliği Bildirimleri";
    public string Category => "Sistem";
    public string Description => "Windows Güvenliği'nin tarama özetleri gibi rutin bildirimlerini gizler. Kritik güvenlik uyarıları görünmeye devam eder.";
    public string? Warning => "Bu ayar cihazdaki tüm kullanıcıları etkiler. Değiştirmek için WinForge'u yönetici olarak çalıştırın.";
    public bool IsActive { get; private set; }
    public bool IsPersistent => true;
    public IReadOnlyList<SettingDescriptor> Settings => [];

    public void Initialize(IModuleContext context)
    {
        try
        {
            using var registry = RegistryKey.OpenBaseKey(_hive, RegistryView.Registry64);
            using var state = registry.OpenSubKey(_statePath);
            using var policy = registry.OpenSubKey(_policyPath);
            IsActive = state?.GetValue(OwnershipValue) is 1 && policy?.GetValue(PolicyValue) is 1;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
        {
            context.Log.Error(Name, exception);
        }
    }

    public void Activate()
    {
        try
        {
            using var registry = RegistryKey.OpenBaseKey(_hive, RegistryView.Registry64);
            using var currentPolicy = registry.OpenSubKey(_policyPath);
            using var currentState = registry.OpenSubKey(_statePath);
            var existingValue = currentPolicy?.GetValue(PolicyValue, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (existingValue is not null)
            {
                IsActive = existingValue is 1 && currentState?.GetValue(OwnershipValue) is 1;
                if (!IsActive) throw ExternalPolicy();
                return;
            }

            using var policy = registry.CreateSubKey(_policyPath) ?? throw new IOException(_policyPath);
            if (policy.GetValue(PolicyValue, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not null)
                throw ExternalPolicy();

            using var state = registry.CreateSubKey(_statePath) ?? throw new IOException(_statePath);
            // Record ownership before changing the policy so it can be undone after a restart.
            state.SetValue(OwnershipValue, 1, RegistryValueKind.DWord);
            try { policy.SetValue(PolicyValue, 1, RegistryValueKind.DWord); }
            catch
            {
                state.DeleteValue(OwnershipValue, throwOnMissingValue: false);
                throw;
            }
            IsActive = true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
        {
            throw AdminRequired(exception);
        }
    }

    public void Deactivate()
    {
        if (!IsActive) return;

        try
        {
            using var registry = RegistryKey.OpenBaseKey(_hive, RegistryView.Registry64);
            using var state = registry.OpenSubKey(_statePath, writable: true);
            if (state?.GetValue(OwnershipValue) is 1)
            {
                using var policy = registry.OpenSubKey(_policyPath, writable: true);
                if (policy?.GetValue(PolicyValue) is 1)
                    policy.DeleteValue(PolicyValue, throwOnMissingValue: false);
                state.DeleteValue(OwnershipValue, throwOnMissingValue: false);
            }
            IsActive = false;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
        {
            throw AdminRequired(exception);
        }
    }

    public void Apply() { }

    private static InvalidOperationException ExternalPolicy() => new(Localization.Tr(
        "Windows Güvenliği'nin rutin bildirimleri için zaten bir Windows ilkesi tanımlı.",
        "A Windows policy is already configured for routine Windows Security notifications."));

    private static InvalidOperationException AdminRequired(Exception exception) => new(Localization.Tr(
        "Bu ayarı değiştirmek için WinForge'dan çıkıp uygulamayı yönetici olarak yeniden açın.",
        "Quit WinForge and reopen it as administrator to change this setting."), exception);
}
