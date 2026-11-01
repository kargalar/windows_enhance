# WinForge native feature roadmap

WinForge implements reversible utilities using its own engines and Windows
Win32/COM/WinRT interfaces. Feature catalogs are research references only.
The WPF host uses Microsoft's WebView2 runtime to render the React interface;
feature engines continue to use Windows/.NET APIs and their existing contracts.
## Existing coverage

| Area | Current WinForge coverage | Remaining work |
|---|---|---|
| Window management | Automatic full-screen tiling, Shift-drag zone placement, arrow-key zone navigation and Ctrl+arrow adjacent rectangular zone spanning during drag, preset zones, configurable 1–6 column × 1–4 row custom grids, per-monitor grid profiles, same-zone window cycling on `Win+PageUp`/`Win+PageDown`, direct active-window movement to adjacent zones using `Win+Ctrl+Alt+Arrow`, and live read-only cropped window thumbnails | Interactive reparent mode |
| Always on top and mouse move/resize | Own Always On Top and Window Control engines; configurable actions, edge snapping, magnets, input exclusions, reversible opacity/borders and migrated preferences | Broader Windows-build, DPI and elevated-window verification |
| OCR and screen tools | Text Extractor, Screen Ruler, Color Picker | Improve capture reliability and keyboard/accessibility flows |
| Mouse | Double-Ctrl spotlight; click rings for left, right, middle and both side buttons with an opt-in all-button scope, independent per-button colors, configurable click diameter and fade duration; configurable crosshairs; monitor jump | Add richer click highlighter modes such as per-button animation styles |
| Displays / Power Display | DDC/CI capability discovery with brightness, contrast, monitor volume, input-source and color-preset controls; Windows WMI brightness for built-in panels; linked brightness levels; async discovery, color-change confirmation, persistent per-monitor profiles, opt-in local-time day/night brightness schedules, and cursor-monitor brightness hotkeys | Add rotation/power controls, custom VCP names, and profile integration with Light Switch |
| Fluent Flyout | Compact media flyout with Windows media session title/artist and bounded album artwork, selectable sessions and persistently pinnable sessions through the taskbar widget, playback timeline/seek, shuffle/repeat, allowlist/blocklist app filtering, direct play/pause/skip commands (key fallback), independently configurable lock-key OSD and non-activating notifications, `Win+Alt+M`/`Ctrl+Alt+V` shortcuts, hardware media-key triggers, bottom-centered audio volume OSD with interactive slider and media flyout on hardware and compatible external volume changes, Core Audio endpoint volume/mute changes filtered through event context, opt-in compact taskbar-adjacent mini player with edge-aware placement, session-switching context menu and configurable master/player-app volume wheel control, opt-in 24-band WASAPI output spectrum visualizer with silence hiding and optional baseline, Up Next notification, configurable corner/monitor, black dark surfaces with white text and borderless controls, fade animation, inactivity dismissal, fullscreen suppression and high-contrast colors | Direct taskbar embedding and edge-oriented widget layout; marquee/fixed-width widget options; clickable player header that reliably activates the exact player/browser tab; richer Fluent backdrop and animation settings; verify external device behavior across audio drivers and Windows builds |
| Taskbar and shell | Own taskbar registry/API tweaks, seconds/date format, wheel volume, middle-click close, transparency/accent, tray visibility | Own version-tested shell engine for advanced XAML themes, taskbar geometry/placement, dock animations and Explorer/browser customization; these features are currently unavailable |
| Explorer and Start | Explorer tweaks, context-menu tools, Start/Search cleanup, bulk rename with persistent last-operation undo, image resize with metadata preservation/progress/cancellation, file-lock inspection with process/service details and post-termination rescan | Verify Explorer integration across supported Windows builds |
| Themes and system | Theme switch with timed light/dark schedule, corners, UI tweaks, startup | Add reversible power/menu shortcuts |

## Catalog gaps to track

### Fluent Flyout parity plan

The WinForge flyout provides `Win+Alt+M`, a separate interactive master-volume
OSD, media metadata and
bounded album artwork, selectable and filterable Windows media sessions, a playback timeline
with mouse/keyboard seek, shuffle/repeat, direct play/pause/skip commands (with
media-key fallback), and separate Caps/Num/Scroll Lock OSD notifications. Continue
in this order:

1. A compact primary-taskbar-adjacent mini player now offers track metadata,
   transport controls, contextual media/sound/player actions, and optional
   master-volume or selected-player-app volume wheel control. Placement follows
   the primary taskbar edge (top, bottom, left, or right). It hides without an active media session and
   suppresses itself in fullscreen apps. The chosen media session can be pinned
   across restarts. An opt-in 24-band WASAPI loopback spectrum visualizer now
   runs only while enabled and hides in fullscreen; its level meter is throttled
   to 30 FPS and decays cleanly during silence. Evaluate true taskbar embedding
   and orientation-aware widget layout as separate shell surfaces.
2. Keyboard hardware media keys show the flyout; volume keys show both the audio
   OSD and media flyout without swallowing their original key events. While the flyout is open,
   session-list, selected-session
   metadata, playback and timeline updates are event-driven; a lightweight
   timer only keeps the timeline fresh. A scoped Core Audio endpoint callback
   shows the OSD and media flyout for software-visible volume/mute changes from
   compatible external controls;
   WinForge-originated changes carry a private event-context GUID and are
   ignored. Windows and audio drivers may not surface every Bluetooth/trackpad
   control through this API.
3. Verify headphones/Bluetooth session changes and supported brightness/audio
   OSD behavior across Windows 10/11 builds and multiple monitors.

Upstream references: [FluentFlyout features](https://fluentflyout.com/),
[FAQ and feature behavior](https://fluentflyout.com/faq/), and
[release notes](https://fluentflyout.com/changelog/).

PowerToys utilities not yet covered or only partly covered by WinForge:

- **Crop And Lock:** `Win+Ctrl+Shift+T` selects a region of the active window and displays a resizable, always-on-top live DWM thumbnail. The thumbnail follows source-window updates and is read-only; PowerToys' interactive Reparent mode and static screenshot mode remain future work. `Esc` cancels selection.
- **Keyboard Manager:** global and process-scoped key/shortcut remaps plus
  shortcut-triggered Unicode text sequences are implemented; app-specific rules
  take precedence while their process is foreground. Global shortcuts can launch
  applications with arguments/working directories or open validated URIs; chord
  shortcuts remain.
- **File Explorer add-ons:** PowerRename-style bulk rename with preview, regex,
  collision checks, two-phase rename, and a bounded, atomically written local
  undo record that restores the latest batch after the dialog or app closes is
  implemented. The undo is offered only while its destination files still exist
  and the reverse transaction is safe. Image
  Resizer supports bounded presets, custom sizing, PNG/JPEG output, and preserves
  source files. File Locksmith uses Restart Manager for app/service inspection
  and guarded application termination.
- **Mouse utilities:** spotlight, click rings, crosshairs and jump-to-monitor
  exist. Mouse Without Borders and ZoomIt are larger standalone tools
  and need separate scope evaluation.
- **Twinkle Tray / Power Display:** built-in display panel brightness through the
  Windows WMI monitor provider alongside capability-gated DDC/CI brightness, contrast,
  monitor volume, input source and color-preset controls, async discovery, linked
  levels, and persistent profiles that save and apply supported per-monitor
  settings. An opt-in module-lifetime scheduler applies configured day/night
  brightness levels at local-time boundaries, including overnight periods, even
  while the panel is closed. Optional `Ctrl+Alt+F11/F12` hotkeys adjust all
  supported physical monitors attached to the logical display beneath the cursor;
  the step is configurable from 1–20%. Internal-panel entries join the same
  brightness linking, saved profiles, and day/night schedule; unsupported panels
  are omitted when WMI returns no active brightness capability. Rotation/power,
  custom VCP labels, and Light Switch profile integration remain.

Native infrastructure now includes a WinForge-owned window-control engine
(mouse gestures, sizing limits, monitor work areas, magnets, edge layouts,
configurable actions and exclusions), a WASAPI loopback engine (format-aware
PCM/float decoding, packet accumulation, stream cleanup and endpoint switching),
and a COM WMI connection for monitor brightness and workspace process arguments.
No external process or third-party mod settings are needed for these features.
## Delivery order

### 2. Keyboard Manager — global and process-scoped remapping implemented

- Remapping editor accepts key rows (`A = B`) and shortcut rows
  (`Ctrl+C = Ctrl+Insert`) with parsing, duplicate detection, and reserved system
  shortcut checks.
- Text sequence rows bind a key or shortcut to up to 2048 Unicode characters; the hook
  temporarily releases and restores held modifiers around Unicode input.
- Persist mappings in WinForge settings and apply them through a dedicated
  low-level hook that always forwards unrelated input.
- App-specific rules override global mappings for their foreground process.
- Global shortcut actions can open URI schemes restricted to `http`, `https`,
  `mailto`, `ms-settings`, `shell` and `file`; launch applications with
  environment-expanded executable paths, arguments, working directories,
  normal/elevated/different-user launch verbs, hidden/visible windows, and
  already-running policies (new instance, focus, do nothing, close or end task).
- Multi-step shortcut actions support one to four modifier+key chords separated
  by commas (for example `Ctrl+K, Ctrl+C = uri|https://example.com`). A prefix
  chord is passed through normally; only a fully matched sequence triggers its
  action. The sequence expires after two seconds, mismatches reset and may begin
  another configured sequence, and every step is checked against reserved
  Windows shortcuts. Existing single-chord JSON settings remain compatible.
- Remaining: broader live input validation across keyboard layouts and elevated
  applications.

### 3. File Explorer productivity

- PowerRename follow-up: consider multi-step undo history after the latest
  operation's persistent undo proves reliable in everyday use.
- Image Resizer now preserves EXIF/IPTC metadata when the output codec matches,
  processes large batches off the UI thread with per-file progress, and supports
  cancellation before the next item or before an encoded temporary file is
  atomically moved into place. Cross-codec metadata conversion remains omitted
  because metadata schemas are format-specific.
- File Locksmith now reports lock owner process/service details and rescans the
  selected path after a guarded termination attempt. Image Resizer metadata,
  progress and cancellation are implemented; these items are complete.

### 4. Advanced window and mouse tools

- Extend zones with interactive reparent mode. Custom grids, keyboard
  selection, rectangular adjacent-zone spanning during drag, per-monitor grid
  profiles, same-zone window cycling, and four configurable direct keyboard
  move actions are implemented.
- Add mouse crosshairs and jump-to-monitor with independent shortcuts/options.
- Keep changes reversible and avoid injecting code into Explorer or DWM.

### 5. Own shell customization engine

- Build any future shell customization inside WinForge, with explicit Windows
  build support, reversible lifecycle and recovery after Explorer restarts.
- Advanced XAML themes, taskbar dimensions/placement, dock animations, Explorer
  folder sizes and browser mods are not currently implemented.
- Do not reintroduce external mod runtimes, downloaded executables or bundled
  third-party hook libraries.
### 6. Verification and release readiness

- Add regression coverage for each feature's persistence, cancellation, failure
  rollback, input behavior, and cleanup.
- Build the full solution and run the regression suite after each feature group.
- Maintain a Windows 10/11 compatibility matrix for features that depend on
  shell internals or OS build-specific behavior.

### Current implementation order

1. Complete runtime compatibility checks for native window control
   and Fluent Flyout audio-device behavior on Windows 10/11.
2. Finish high-use Fluent Flyout taskbar-widget behavior: edge-oriented layout,
   stable player activation, and width options.
3. Evaluate remaining PowerToys gaps by daily usefulness and platform support:
   interactive Crop And Lock reparenting, and richer PowerRename history.
4. Implement remaining shell features with a WinForge-owned engine only after
   Windows-build compatibility and reversible state restoration are verified.

## Source references

- [Microsoft PowerToys utility catalog](https://learn.microsoft.com/en-us/windows/powertoys/)
- [Microsoft Keyboard Manager remapping behavior and reserved keys](https://learn.microsoft.com/en-us/windows/powertoys/keyboard-manager)
- [Microsoft FancyZones interaction and layout behavior](https://learn.microsoft.com/en-us/windows/powertoys/fancyzones)
- [Microsoft Restart Manager functions](https://learn.microsoft.com/en-us/windows/win32/rstmgr/functions)
- [Microsoft monitor brightness APIs](https://learn.microsoft.com/en-us/windows/win32/api/_monitor/)
- [Microsoft PowerToys Power Display](https://learn.microsoft.com/en-us/windows/powertoys/power-display)
- [Microsoft WmiMonitorBrightness class](https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmimonitorbrightness)
- [Microsoft WmiSetBrightness method](https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmisetbrightness-method-in-class-wmimonitorbrightnessmethods)
- [Microsoft DDC/CI capability and VCP discovery flow](https://learn.microsoft.com/en-us/windows/win32/monitor/using-the-low-level-monitor-configuration-functions)
- [Microsoft GetVCPFeatureAndVCPFeatureReply](https://learn.microsoft.com/en-us/windows/win32/api/lowlevelmonitorconfigurationapi/nf-lowlevelmonitorconfigurationapi-getvcpfeatureandvcpfeaturereply)
- [Twinkle Tray features and monitor compatibility](https://github.com/xanderfrangos/twinkle-tray/blob/master/README.md)
- [Microsoft RM_PROCESS_INFO structure](https://learn.microsoft.com/en-us/windows/win32/api/restartmanager/ns-restartmanager-rm_process_info)
- [Microsoft RM_APP_TYPE values](https://learn.microsoft.com/en-us/windows/win32/api/restartmanager/ne-restartmanager-rm_app_type)
- [Microsoft static Explorer verbs and file arguments](https://learn.microsoft.com/en-us/windows/win32/shell/context)
- [Microsoft Explorer multi-selection verb model](https://learn.microsoft.com/en-us/windows/win32/shell/how-to-employ-the-verb-selection-model)
- [Microsoft .NET named pipe user isolation](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.pipeoptions?view=net-10.0)
