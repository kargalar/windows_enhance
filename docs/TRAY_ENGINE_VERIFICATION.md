# Notification-area investigation, 2026-10-24

This record separates effective Explorer behavior from saved settings. No external tray engine or runtime package was introduced.

**Paused at the user's request.** The normal desktop-launch fixture proved actual promotion, hiding and cleanup. The newly added `TrayIconsLiveChecks` test of the hardened production module has **not yet run**; both its source and the changed module passed isolated compilation with warnings treated as errors.

## Observed behavior

- Two transient notification icons are registered by `TrayIconFixture` using random per-run GUIDs and unique tooltips. They use an owned hidden window with a dedicated message thread.
- Current Windows creates `Control Panel\NotifyIconSettings` entries containing `IconGuid` and the executable path. `InitialTooltip` can be absent. Matching both the GUID and tooltip therefore failed; identity matching now accepts the exact own GUID independently of tooltip presence.
- In processes launched by the development host, writing those two entries' `IsPromoted=1` and broadcasting `TraySettings` succeeds in the caller's registry view but does **not** move registered icons out of overflow. `Shell_NotifyIconGetRect` returns the overflow-chevron rectangle for both icons. Cross-process readback subsequently established the different registry views described below.
- The fixture can remove and re-register only its own two icons after updating their saved preference. Effective placement must still be measured by the live shell checks. It does not broadcast `TaskbarCreated`, restart Explorer, or re-register another application's icons.
- The root live checks found actual notification icons under `SystemTray.NotificationAreaIcons`; the compatibility `StackListView[AutomationId=NotifyIcon]` is not the active host on this machine. Root reported 17 live shell checks passing after correcting this selector, including measured two-row geometry.

Disposal deletes the fixture's icons, restores recorded promotion-value presence/type/value, and removes only newly created registry entries matching its own GUID or exact unique tooltip. Other icon preferences are excluded.

## Classic COM service is a compatibility stub

The installed `CLSID_TrayNotify` class can be activated. QueryInterface for the Windows 8 interface succeeds, while the legacy interface is unavailable. Both managed and independent native C++ callers observed:

```text
CoCreateInstance: S_OK
RegisterCallback: S_OK; callback cookie 0; callbacks 0
After bounded COM-dispatch wait: callbacks 0
UnregisterCallback: S_OK
```

This persisted while both owned GUID icons existed and `Shell_NotifyIconGetRect` succeeded. STA/MTA selection and a callback wait did not change the result. No preference mutation was attempted through this ineffective interface.

Microsoft's matching `Taskbar.pdb` identifies the implementation as `LegacySystrayCompatStubs`. This corroborates the runtime observation. The experimental managed service was removed from application sources instead of being presented as a working production backend.

The interface ABI was cross-checked against Chromium's current primary implementation: [status_tray_state_changer_win.cc](https://chromium.googlesource.com/chromium/src/+/refs/heads/main/chrome/browser/ui/views/status_icons/status_tray_state_changer_win.cc). This is an undocumented Windows interface, not a supported Microsoft API contract.

## Modern controller metadata

The installed metadata at `C:\Windows\SystemApps\MicrosoftWindows.UndockedDevKit_cw5n1h2txyewy\windowsudk.winmd` declares:

- `ITaskbarModel5`, IID `22FE2D45-26C1-569C-B17E-42626AC39375`: `MoveNotificationAreaIcon(icon, location, index)`.
- `NotificationAreaIconLocation`: `Promoted=0`, `Overflow=1`.
- `ITaskbarModel3`: the promoted-icon collection.
- `ITaskbarModel6`: the overflow-icon collection.
- `INotificationAreaIcon`: icon tooltip and bounds.

REA metadata evidence: `ev_a2c8b4988d1e91135106236d7eb2390cd6589e18ad110f5ad96b3db8806f2de1`.
Metadata SHA-256: `9efa9802217a38bb53ae0c85036d231a90fd9109e4512d1fc6bda3f56deb2ba0`.
Module MVID: `4986da55-e636-5924-bd7d-be52df6923b9`.
Coverage for the selected seven types was complete; inspection did not execute the metadata.

The matching `Taskbar.pdb` also names `NotificationAreaIconManager2::OnIconPromotionChanged` and the modern `WindowsUdk.UI.Shell.TaskbarModel` implementation. No executable address or private object offset is used by the application.

Root's read-only XAML snapshots show `SystemTray.IconViewModel` data contexts on icon views, with no data context on the taskbar/tray frames. Matching `SystemTray.pdb` establishes the `ContentVM` and `Configuration` XAML members. The view model does not advertise `ICustomPropertyProvider` in its implementation interface list.

An exploratory `TrayModel.h` provided a bounded read-only probe using exact metadata interface IDs, collection getters, `ICustomPropertyProvider` where available, and the existing Windows component's standard `DllGetActivationFactory` export to obtain `IXamlMetadataProvider`. Root's live probe successfully read `ContentVM` as `SystemTray.ImageIconContentViewModel` and `Configuration` as `SystemTray.IconConfiguration`. Matching symbols identify additional properties, but the running XAML metadata did not expose them. No live controller reference was recovered. This exploratory header and its production snapshot wiring were removed after the supported registry path was verified.

Five isolated native checks passed with `/W4 /WX /MT`: null context, unrelated-object rejection, exact model interfaces, empty-versus-unavailable collection distinction, and zero mutation calls. These were diagnostic checks, not proof of a usable production controller.

## Windows Settings implementation

Offline inspection of the installed `SettingsHandlers_DesktopTaskbar.dll`, with its matching Microsoft PDB, found `DesktopTaskbarSystemTrayAppListItemSetting::SetValue`. Its preference operation is a `RegSetValueExW` call for `IsPromoted`, with `REG_DWORD` and a four-byte value, followed by telemetry. This setter does not invoke a tray COM controller. The import-table slot and the UTF-16 value name were independently resolved from the installed PE file. These static findings do not establish which registry view a packaged caller writes, or prove that the running Explorer observes that write.

Microsoft documents private, per-package HKCU writes and merged reads for [MSIX flexible virtualization](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization). This differs from legacy 32-bit UAC registry virtualization. The following controlled tests establish differing views here without relying on the process's reported package-identity string.

## Cross-process and desktop-launch proof

1. A development-host child created two random owned GUID icons. Before promotion, Explorer and the child read the same `IconGuid` strings and no `IsPromoted` value.
2. After the child wrote `IsPromoted=1`, its own readback returned DWORD 1 for both keys. A temporary read-only `RegGetValueW` call inside Explorer still returned `ERROR_FILE_NOT_FOUND` for those exact values. The native `IconGuid` reads continued to succeed. Evidence: `ev_60d6d7e63ba9d516c9d5e299d4171763dccccb30b1b111b2a0ab4eb2cf6a87ac`.
3. Writing only those two owned GUID preferences inside Explorer moved both icons to distinct native rectangles. Restoring DWORD 0 returned both to the same overflow-chevron rectangle. The fixture stopped, and its two newly created physical keys were deleted only after checking exact GUID identity and that the owned HWND no longer existed. Explorer remained responsive. Evidence: `ev_bbf60b07500012a1dc851b9d3a22eb27e0dd6f8d75210f9f54bf31c299b2351f`, cleanup `ev_c115b3ccaab00521cf47b314d7d1685d34234ba2bd23e6842e6bfe9cef508800`.
4. The same unchanged fixture executable was launched from Explorer. Its ordinary .NET registry writes were immediately visible to Explorer, and its own icons obtained distinct native rectangles. Evidence: `ev_f41cd9594c3e95ee08d924d1f9290031a104125ac3fc7fc7a14d96cf6595d7dc`. Its normal disposal removed both physical keys and both icons; native readback confirmed the keys absent. Evidence: `ev_be100f6c71a52fad72adb47057eab2726af5af7191203f0b956b98d11fbaca9c`.

These results establish that the registry backend works in the normal desktop launch context. The inherited development-host view caused the earlier false failure. No private controller, COM compatibility service, or general registry proxy is needed for that path. Frida was a temporary investigation tool, not an application dependency; all mutations were restricted to newly created owned GUID entries.

`TrayIconsModule` now preserves original registry value presence/type/content, ignores stale entries whose icon identity changed, and provides an internal exact-subkey scope for live verification. The final desktop-launched `--tray-live` run passed **all 9 checks with process exit 0**. This exercised the actual module's activation, promotion, hiding and restoration against only owned icons, registry value type/absence restoration, stale-identity rejection, and preservation of every other application's recorded promotion preference. Its log is `artifacts/desktop-tests/tray-live.log`.

At stop, a final native Explorer read confirmed all four GUID keys created in the two controlled runs absent (`IconGuid` and `IsPromoted` both `ERROR_FILE_NOT_FOUND`). Evidence: `ev_648ea825cbfec01999b67552be43ac43c9ebf8e183dbac6287f0cce068e53d11`. Both fixture processes had exited, Explorer remained responsive, and the temporary Frida session was closed with all scripts unloaded. No user icon preferences were changed by these controlled runs. The separate live toast registration visibility check remains pending.

## Primary symbol identities

- [Taskbar.pdb, matching RSDS identity](https://msdl.microsoft.com/download/symbols/Taskbar.pdb/DAB69A79FEE033F0D81E023FBF172B511/Taskbar.pdb)
- [SystemTray.pdb, matching RSDS identity](https://msdl.microsoft.com/download/symbols/SystemTray.pdb/2007F3890B9D4D16AF78BAFE771227281/SystemTray.pdb)
- [SettingsHandlers_DesktopTaskbar.pdb, matching RSDS identity](https://msdl.microsoft.com/download/symbols/SettingsHandlers_DesktopTaskbar.pdb/9291D287D8E8697CC27854F489DA8FC01/SettingsHandlers_DesktopTaskbar.pdb)

Symbols and development probes are stored outside the application under temporary investigation directories. They are not runtime dependencies.
