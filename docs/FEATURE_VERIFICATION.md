# Feature verification inventory

Date: 2026-10-24. This inventory records implementation and evidence, not a blanket
claim that every Windows application, Windows build, or connected device behaves
identically. The inventory includes the original modules and independently
restored shell, notification-placement and browser-popup features.

The application no longer needs Windhawk, AltSnap, NAudio, or System.Management
packages. It still calls Windows APIs for windows, input, audio, OCR, PDF rendering,
media codecs, registry settings, WMI, and display hardware. These are operating
system facilities; shipping a local engine does not replace Windows itself.
Clipboard cloud synchronization, when explicitly enabled, is a Windows service
feature and is not a local clipboard-history implementation.

## Evidence and reproduction

`tests/WinForge.RegressionTests/Program.cs` contains the existing application,
module, UI, file-operation, parsing, profile, and selected native API checks.
Passing UI construction, descriptor/default checks, and `IsActive` checks establish
only those properties. They do not demonstrate a visible shell effect or successful
input delivery to a different application. Some existing tests deliberately accept
an unavailable audio device, absent packaged application, or unavailable session;
those fallbacks must not be counted as successful hardware/target verification.

`FeatureVerificationChecks.cs` adds direct feature tests. The isolated command
`--feature-only --native-smoke` passed all 72 checks on this machine in the
controlled x64 build:

- Screen capture reads a real 32 × 24 GDI region and rejects overflowing sizes.
- Windows OCR recognizes `WinForge 12345` from a 4000-pixel-wide fixture whose
  alpha channel is zero, using the engine's maximum-dimension scaling.
- Windows PDF renders an actual 100 × 10000-point page with both output dimensions
  bounded before native rendering allocates the image.
- A real PCM WAV opens paused; its first Play advances playback; Stop resets
  playback; closing the preview releases the native media resource.
- Workspace mapping fits a window inside a monitor work area, including negative
  coordinates and work areas smaller than the preferred minimum. A missing window
  is rejected. The property-store buffer has the native x64 PROPVARIANT size.
- Tiling leaves its own minimized, excluded window minimized even when prevention
  of minimization is enabled.
- The shared keyboard engine submits real Unicode input to Windows and the exact
  fixture text appears in an owned WPF editor. The native INPUT buffer is 40 bytes.
- Deterministic paste fixtures verify waiting for actual key release, focus-change
  cancellation, deadline cancellation, and cancellation when either Plain Paste
  or Advanced Paste is disabled. These tests inject no input into user windows.
- The mouse fixture verifies correct native commands, exact baseline restoration,
  idempotence and failure cleanup. All native calls are intercepted, so the user's
  actual pointer settings are untouched.
- The animation fixture verifies the required native structure size in both
  input fields and the actual read/write payload, with no global animation change.
- A real owned window receives the DWM corner preference and disabling the module
  restores its original explicit preference rather than a generic default.
- Owned-window Always On Top checks cover pin/unpin, disable restoration,
  preservation of an existing topmost state, and a destroyed target.
- Taskbar matching checks exercise exact AppIDs, grouped-document ambiguity,
  conflicting profiles, measured native geometry, Start-button exclusion,
  root-only providers and native mouse-input consumption. A real owned window
  yields its grouping ID through Windows' local application resolver; live
  process/title validation rejects a changed target before a close request.

The run also includes observer-exception containment, eight notification-placement
geometry/classification checks and five actual owned-window lifecycle checks.
Core Audio COM ownership changes compile in this controlled output. An owned
fixture does not establish that Windows allows its actual toast host to be
repositioned. The separate opt-in `--notification-live` route registers a unique
temporary local application identity, sends one silent own toast and verifies
its visible host by unique accessible content before moving it. Its unpackaged
registration includes a stub ToastActivatorCLSID on the shortcut and an isolated
own protocol activation, as documented by Microsoft. It removes only its own
registrations, shortcut and notification in cleanup. Live runs accepted the
toast into its own Windows history with notifications enabled but exposed no
visible host or exact own UI Automation marker. This also occurred after a
desktop-Explorer launch with SHQueryUserNotificationState=ACCEPTS_NOTIFICATIONS
(5); BUSY (2) alone therefore does not explain the result. The user confirmed
Do Not Disturb was already off. Visible notification placement is not established.
The test never changes Do Not Disturb or global notification settings.

Work stopped at the user's request with the following new notification gates
saved in source but UNRUN: persist/reopen the shortcut and verify exact AUMID and
ToastActivatorCLSID; bind the exact `shell:AppsFolder\<ownAUMID>` with an eight-second
readiness limit; optionally hold the own registration for external read-only
Explorer verification. The hold uses `WINFORGE_NOTIFICATION_REGISTRATION_HOLD_MS`
(maximum 20000) and an absolute `WINFORGE_NOTIFICATION_REGISTRATION_PROBE` artifact
path. Its artifact records only own identity/key/logical and physical shortcut
paths. An independent tray probe established that the development host's HKCU
writes can be private while desktop-Explorer writes are visible; the exact toast
AUMID/shortcut has not yet been compared across those views. Resume with the held,
desktop-context registration proof before another visible-toast attempt. No
diagnostic instrumentation or fixture registration is intentionally left active.

The separate opt-in `--audio-live` route prepares five seconds of quiet 1 kHz PCM
playback, inspects actual native loopback samples and FFT frequency, and exercises
volume/mute controls only on the test process's exact own session. It retains the
native session reference for exact floating-point baseline restoration. Master
volume and other application sessions are never written. A muted/zero endpoint,
missing own session or missing measured signal fails with a diagnostic instead of
counting capture initialization as audio evidence. All six live audio checks
passed: actual 48 kHz native capture carried the own 1 kHz component, the spectrum
band matched it, own-session volume/mute read back correctly and exact baseline
restoration left the master controls unchanged. The capture stream's own session
is excluded using the active-session baseline captured before playback starts.

Read-only display discovery through WinForge's own compiled code found two
ASUS VG24VQE external monitors on Windows 11 build 26300. DISPLAY6 reported
brightness 23/100, contrast 80/100, input 0x11 (advertised 0x0F/0x11/0x12) and
color preset 0x0B (advertised 0x01/0x04/0x05/0x06/0x08/0x0B). DISPLAY1 reported
brightness 24/100. Both raw capabilities reads succeeded and advertised the same
controls; some DISPLAY1 feature reads failed in the first discovery, so missing
snapshots are classified as unavailable readback rather than proven unsupported
hardware. Monitor volume was not exposed: firmware returned malformed token
`62FF` instead of an unambiguous VCP 0x62 advertisement. Twinkle Tray was running
and was left untouched; contention is possible but not established as the cause.
No hardware setting was written. The own WMI COM implementation successfully read
the operating-system caption/version; the internal-brightness provider returned
WBEM_E_NOT_SUPPORTED (0x8004100C), so there is no verified internal panel on this
machine. Five regression checks for stable display IDs/cursor selection, native
VCP enum storage and sparse internal brightness ranges are saved; their latest
controlled-run result must be taken from the parent verification log. No further
hardware write test was performed by this workstream.

The final controlled `--native-smoke` run passed all **802 regression checks with
process exit 0**, including the 90 feature checks, nine PDF pipeline checks within
that feature total, and 129 WindowControl checks. The Release/x64 build had zero
warnings and errors. PDF previews now run in our own native helper using an explicit
Windows WARP software device; the previous hardware PDF shutdown crash no longer
occurred. A separate final `--shell-only` run passed 11 checks with exit 0, including
recovery from a disconnected shared consumer while old leases still exist.
The desktop-launched actual tray-module run passed all 9 live checks with exit 0.
These results do not turn the unverified items below into completed features.

Build all projects using the same `Platform=x64` and configuration. Solution
project mappings now use x64 consistently. The application exposes its exact
module target paths through `GetModuleFiles`; the test copy target uses those
returned items directly. This removes the prior stale-output/empty-directory
glob problem. Use the final integrated build output for release verification.

## Module inventory

“Existing checks” below describes coverage present in the regression suite. The
final integrated test run must be checked separately after concurrent source edits
settle. “Unverified” means a specific end-to-end result is not established by the
available tests, even when implementation exists.

| Module ID | Actual implementation | Evidence and limits |
|---|---|---|
| `taskbar` | Mouse actions, Windows preferences, asynchronous UI Automation/native hit testing, precise Windows application identity matching, guarded close requests and optional verified-empty-space mute. | Grouped-identity, geometry, stale-target, native resolver and hook-consumption fixtures passed in the 58-check run. Exact app grouping is used instead of process-name guesses; ambiguous grouped documents are not closed. Actual read-only resolution of a real external taskbar button and each registry preference's observable shell result remain unverified. |
| `shell-customization` | Own native diagnostics bridge, shared broker, taskbar theme brushes, Start-button visibility, icon/button dimensions, local taskbar clock/date formats and tray row styling. | Parent controlled verification passed 13 real-taskbar checks including apply/restore and exact measured properties; separate bridge fixtures verify apply/restore/watchdog. Top/vertical taskbar layouts, every prior third-party theme and Start-menu/Explorer styling are not established by these checks. |
| `notifications-placement` | Own Windows event observer, strict system ShellExperienceHost/CoreWindow/TR-or-EN toast-title classification, monitor/work-area/DPI placement, bounded stack positioning, exact original-bounds restoration and shell-reset error reporting. | Eight pure and five actual owned-HWND tests passed in the 72-check run. Real Windows toast movement is unverified; Action Center and unrelated processes are explicitly excluded. Other localized toast titles are unsupported until their exact identities are established. |
| `browser-fullscreen` | Own event observer and strict Chromium exit-hint popup classification with reversible hiding. | Source present; actual browser and owned-popup evidence belongs to the parent verification workstream. This is specifically the exit hint, not a general browser-popup suppressor. |
| `net-speed` | Reads adapter counters and positions a WPF speed display near/in the taskbar. | Module/UI construction is covered. Real network traffic, adapter changes, scaling and taskbar embedding across Explorer restarts remain unverified. |
| `tray-icons` | Reads per-user notification-icon registry entries, sets promotion, restores saved values and rejects changed icon identities. | Desktop-launched actual module passed 9 live checks: two owned GUID icons obtained distinct visible rectangles, returned to overflow, and restored the baseline while all other preferences were preserved. Windows may rewrite promotion when an app registers again. |
| `always-on-top` | Applies a topmost window style, draws a tracking border, registers Alt+F, restores the captured original style. | Actual owned-window pin/unpin, disable restoration, preexisting-topmost preservation and destroyed-target fixtures passed in the 58-check run. Tracking borders on multiple external applications remain unverified. |
| `text-grabber` | Captures a selected screen region, runs local Windows OCR, copies text to the clipboard. | Direct capture and 4000-pixel/zero-alpha OCR tests passed. Selection/copy/hotkey end-to-end, mixed-DPI monitor boundaries and noninstalled OCR languages remain unverified. |
| `launcher` | Bounded application/file/folder indexing, arithmetic parser, validated path/URI targets, opt-in recent items/content search, module quick actions. | Existing tests exercise index fixtures, limits, calculator, URI validation, recent items and Workspace action lookup. Actual launch of each target category remains unverified. |
| `shortcut-guide` | Searchable shortcut list and nonactivating hold-Win overlay. | Existing tests render/filter the guide and simulate key-state observation/hold/cancel/release. Physical keyboard combinations in other applications remain unverified. |
| `keyboard-manager` | Own low-level hook, key/shortcut/text/app mappings, bounded chord sequences and validated application/URI actions. | Existing parser, conflict/reserved-key validation and chord-state tests; actual Unicode input reached an owned fixture editor. Physical remapping in every application and privilege level remains unverified. Windows secure combinations cannot be remapped. |
| `auto-tiling` | Independent fullscreen window tiling module with master/grid/column layouts. | Existing geometry/navigation/profile tests; direct excluded-minimized-window test passed. Minimum-sized apps remain unverified. |
| `fancy-zones` | Independent drag-to-zone module with palette-based overlay colors, divider-proximity multi-zone selection, least-occupied-zone placement for new windows, maximized overflow when all zones are occupied, keyboard movement, monitor profiles and same-zone cycling; includes WinForge windows while excluding the preview overlay. | Geometry/navigation/profile, divider-selection, least-occupied-zone and full-grid occupancy checks. Actual drag/drop across DPI changes and full-screen games remain unverified. |
| `crop-and-lock` | Live read-only DWM thumbnail with a selected source rectangle. | Existing selection geometry and thumbnail UI checks. Interactive/reparent and static snapshot modes are not implemented. Protected/minimized source rendering depends on DWM. |
| `window-control` | Own low-level mouse engine and window actions, geometry, snapping/magnets and exclusions. | 129 checks passed in the final 802-check run, including owned native actions and actual menu selection/activation. Advanced legacy INI behavior remains incomplete as listed in `src/WinForge.Modules.WindowControl/PARITY.md`. |
| `awake` | Sets Windows thread execution-state flags and optional duration timer. | Existing live test checks activation-thread flags, expiry clearing and timer release. Long sleep/lock/resume and every power policy remain unverified. |
| `theme-switch` | User theme registry preferences, broadcast, hotkey and local-time scheduling. | Existing schedule calculations and UI checks. Visible changes across all shell/app surfaces and resume/time-zone transitions remain unverified. |
| `color-picker` | Own overlay, GDI pixel sampling and HEX/RGB/HSL clipboard formatting. | Module/settings coverage exists. Selected-pixel accuracy on HDR, mixed-DPI screens and clipboard contention remain unverified. |
| `plain-paste` | Reads clipboard text, waits for physical hotkey release and an unchanged target, replaces rich formats with text and synthesizes a paste shortcut. | Deterministic focus/release/timeout/disable cancellation tests passed. Shared native keyboard input reached a fixture editor. Full clipboard paste into every application is unverified. |
| `screen-ruler` | Own selection overlay and pixel/physical-unit calculations. | Source/module/UI coverage. Physical units depend on configured DPI and are not monitor calibration. Actual selection-to-clipboard end-to-end remains unverified. |
| `mouse-tweaks` | Reads/writes acceleration and wheel lines via SystemParametersInfo, reads timing with GetDoubleClickTime and sets it with SPI_SETDOUBLECLICKTIME. | Corrected a native command mix-up that previously reset timing on read and swapped mouse buttons on write. Injected native-call, exact restore, idempotence and failure fixtures passed. Real pointer settings are not changed by these tests. |
| `mouse-spotlight` | Own input observers, double-Ctrl spotlight, configured click rings and crosshair overlay. | Existing crosshair construction and button-decoding checks. Hardware buttons, multiple monitors and render timing remain unverified. |
| `mouse-jump` | Monitor enumeration, click-to-screen mapping and cursor repositioning. | Existing monitor enumeration, proportional mapping and lifecycle tests. Actual pointer destination on every mixed-DPI topology remains unverified. |
| `window-tweaks` | Registry values plus SystemParametersInfo animation setting. | Corrected the required ANIMATIONINFO uiParam size and error reporting; injected native read/write fixtures passed. Some preferences require sign-out; registry writes alone cannot verify visible changes. |
| `square-corners` | DWM corner preference on eligible windows, WinEvent refresh and restoration of each window's original value. | Actual owned-window DWM apply and exact restoration passed. Applications drawing their own corners may ignore DWM; effects on every app remain unverified. |
| `classic-context-menu` | Per-user shell registration override with prior-value restoration. | Source/module coverage. Whether a Windows build still honors the override is unverified until Explorer is refreshed and its actual menu inspected. |
| `context-menu` | Per-user Explorer menu commands and hiding registered shell extensions. | Existing registry/snapshot and command validation checks. Built-in Copy/Paste and application-owned menus are outside this mechanism. Actual Explorer menu invocation remains unverified. |
| `explorer-tweaks` | User Explorer preferences for extensions, hidden files, recent/frequent items, compact view, paths, checkboxes, process separation and desktop icons. | Source reviewed and settings rendered. Actual visible effects for each preference on the installed Explorer build remain unverified; some require restarting Explorer. |
| `power-rename` | Preview plus transactional rename, collision checks and durable undo record. | Existing tests perform real file swaps, reversal, conflict rejection, preview application and persistent UI undo on temporary fixtures. Directory locks, external concurrent writers and every filesystem remain unverified. |
| `image-resizer` | Local WPF codecs, proportional resize, PNG/JPEG conversion, metadata option, bounded background work/cancel. | Existing tests decode actual output, verify sizes/quality/metadata, cancellation and overwrite rejection. Corrupt-image matrix and all codec metadata variants remain unverified. |
| `file-locksmith` | Windows Restart Manager query and guarded user-process termination action. | Existing live locked-file test locates the test process and confirms scanning does not alter it. Termination is intentionally not exercised against user applications; Restart Manager is not an exhaustive kernel-handle enumerator. |
| `peek-preview` | Bounded text, image decoding, first PDF page and native Windows media playback controls. | Existing real text/image/limit tests plus direct tall-PDF and WAV playback/resource tests passed. Other audio/video codecs, encrypted PDFs, all image types and subsequent PDF pages are unverified/unsupported respectively. |
| `start-tweaks` | Writes per-user recommendation/new-app/recent-item preference values. | Source reviewed. The current Start host's actual use of each registry value remains unverified; these are not a custom Start implementation. |
| `search-tweaks` | Writes per-user Bing/highlight/suggestion preference values. | Source reviewed. These preference names vary in effect across Windows builds. Actual web-result suppression is unverified; no custom search host replaces Windows Search. |
| `clipboard-history` | Enables Windows clipboard history and configures optional Windows cloud upload preference. | Source reviewed. This is Windows Win+V history, not a WinForge history engine. Cloud/account/policy behavior and actual Win+V contents remain unverified. |
| `advanced-paste` | Local Markdown/JSON transforms, image OCR and TXT/HTML/PNG/JPEG/BMP/GIF artifacts. | Existing tests inspect real file contents and decoded images, including JPEG transparency/quality; shared native OCR fixture passed. Full clipboard-to-selected-application delivery remains unverified. |
| `environment-variables` | User variable CRUD/profiles with baseline and registry type preservation; separate elevation helper for system changes. | Existing fixture-registry/profile activation, concurrency and restoration tests. Live UAC/system-environment writes and a new child process observing each value remain unverified. |
| `hosts-file-editor` | Hosts parser/editor, lossless unmanaged lines/comments, active/disabled mappings, concurrency checks, backup and elevation helper. | Existing parse/render/change/conflict tests on fixtures. Live administrator save, backup restoration and OS DNS result are unverified; no hosts file is changed by feature smoke tests. |
| `display-brightness` | Own WMI COM for internal brightness; Windows DDC/CI/VCP for advertised external monitor controls, profiles and schedules. | Read-only actual brightness/capabilities succeeded for two connected VG24VQE monitors; DISPLAY6 also returned contrast/input/color values. Intermittent missing feature readback is not treated as unsupported hardware. Internal WMI brightness is unsupported on this machine. Actual hardware writes, scheduling effects and cursor hotkeys on hardware remain unverified. |
| `media-flyout` | Own flyout/widget and mixer using Windows media sessions/Core Audio; own WASAPI PCM decoder and FFT spectrum. | Existing session/endpoint enumeration, filters, FFT/stream-format tests and UI rendering. Direct WAV preview playback passed independently. Session transport, volume writes, Bluetooth events, endpoint failover and every player integration remain unverified. |
| `workspaces` | Saves classic/packaged application identifiers, optional process arguments and monitor geometry; launches/reuses/windows and restores bounds. | Existing capture/reuse/argument/AUMID tests; direct work-area remapping, full native PROPVARIANT buffer and invalid-handle rejection passed. App-owned document tabs/sessions are not implemented. All application cold launches and minimized-window capture remain unverified. |
| `startup` | Per-user Run registration for the current executable with background startup. | Source/module coverage. Fresh Windows sign-in and installed/moved executable-path behavior remain unverified. |

## Former customization-engine scope

The retired Windhawk integration is removed. WinForge's own shell bridge now
attaches to the real Explorer taskbar. The parent controlled run passed 17 live
checks for Start-button hide/restore, theme brush application, 32-pixel application
icons, 60-pixel buttons, taskbar-local time/date strings, measured two-row tray
geometry, and a 52-DIP taskbar with native host/XAML height readback and restoration.
Native in-process automation-peer bounds also expose physical
coordinates and Appid metadata for precise taskbar hit testing. Separate controlled
XAML fixtures demonstrate apply/restore and heartbeat restoration.

These results establish the exercised taskbar properties. Top/vertical placement,
every former third-party theme, general Start-menu/Explorer themes and actual
visual arrangement of every tray configuration require their own implementations
and visible-result checks. The earlier height restoration failure was fixed by
preserving the subclass through verified native restoration. The user closed the retired customization programs;
the parent verified zero third-party customization modules in the live Explorer
before the controlled shell checks.

The published WindowControl action matrix has 129 passing checks, including real
menu selection and target activation. Advanced external-editor/INI behavior is
still incomplete; its exact remaining scope is recorded in
`src/WinForge.Modules.WindowControl/PARITY.md`.

## Fixes validated during this verification pass

- Shared local OCR now scales to Windows OCR's reported dimension limit, ignores
  undefined GDI alpha, and disposes the native SoftwareBitmap.
- GDI capture deselects the bitmap before GetDIBits, bounds allocation and requires
  the expected number of scanlines.
- Peek opens media paused before the first click, handles unknown durations, and
  closes its native media object. PDF rendering bounds both dimensions in advance.
- Workspace property access reserves 24 native bytes for x64 PROPVARIANT; placement
  uses current monitor work areas, handles very small work areas, and reports
  destroyed/failed window placement rather than counting it as restored.
- Tiling filters excluded processes/classes before any minimized-window restore.
- Audio session name lookup and app-specific keyboard lookup dispose their Process
  wrappers. Keyboard injection now reserves the full native x64 INPUT union; plain
  and Advanced Paste wait for actual activation-key release, preserve the intended
  target and cancel pending input when disabled or focus changes.
- Mouse preference interop uses GetDoubleClickTime and the correct setter command;
  partial capture failures do not write invented defaults. Corner customization
  preserves each window's original DWM preference instead of resetting to a default.
- Window animation read/write operations pass the required native structure size
  in both the header and uiParam, and surface failures instead of claiming success.
- Always On Top preserves the target's original topmost state, checks native
  application success before tracking/announcing a pin, and retains failed
  restorations for a retry rather than silently dropping them.
- Taskbar actions resolve Windows' exact application identity and measured button
  bounds off the native hook thread, recheck target identity before sending a close
  request, and fail closed on ambiguous documents or unavailable geometry. The
  Windows application-resolver interface is an internal operating-system ABI;
  current-machine fixture success does not establish compatibility on every build.

Native ABI references:
[PROPVARIANT](https://learn.microsoft.com/en-us/windows/win32/api/propidlbase/ns-propidlbase-propvariant),
[SystemParametersInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow),
[WPF media lifetime and preroll](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/multimedia-overview).
