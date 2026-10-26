# Native window-control parity

The module implements its window actions in C# using Windows APIs. It does not
load, start, extract, install or communicate with AltSnap, Windhawk, or another
window-management engine. Windows and the .NET desktop runtime remain platform
requirements.

## Scope and evidence

Baseline: the former `AltSnapModule.cs` in Git HEAD and the `AltSnap.dni` /
`AltSnap.txt` files inside Git HEAD's `third_party/AltSnap/AltSnap1.68src.zip`.
The archived source and guide were read for behavior; the retired executable
was not run during this reconstruction.

The former module's 22 button-action choices and 10 wheel-action choices are
available in `WindowControlModule`. The extra button action `Bölgeye yerleştir`
calls the host's `IWindowPlacementContext` and its own zone provider.

The last verified revision of `WindowControlParityChecks` has 129 passing checks, including actual
Windows HWNDs in a child process started exclusively for the tests. The suite
verifies:

- Move, center resize, sticky adjacent resize and cancellation restoration.
- Border removal/restoration and temporary move opacity/restoration.
- Title-bar roll, directional wheel roll, vertical/horizontal maximize and
  restoration, side snap, zoom and minimize-other/restore.
- Next/previous overlapping windows, wheel window switching, raise/lower,
  native stack-list popup selection and signed horizontal wheel delivery.
- An actual Windows MDI client/child and correct screen/client coordinates.
- Muting/unmuting only the fixture process's silent PCM audio sessions.
- Suspending/resuming only the fixture process, automatic resume on teardown,
  and terminating exactly that owned process without terminating a tree.
- Zone-provider invocation and an actionable error if no provider accepts it.
- Split ratios 1–99, negative gaps, center regions, minimum sizes, smart free
  area snapping and hotclick-code validation.
- OS-interval/position double-click recognition; move double-click maximize and
  restore, Shift-double-click roll/unroll, Ctrl-double-click minimize, resize
  double-click corner snap, Shift edge extension and Ctrl-center maximize.
- Ctrl focus, whole-window monitor confinement, and Space's persistent snap
  override with injected-key and auto-repeat protection.

The 106-check run also covers caption-only titlebar bindings, right-click
maximize while moving, minimized-window popup selection, captured modifiers,
own snap-state normal-size restoration, isolated master-audio routing,
horizontal scrolling, caption-only frame removal and opacity steps.

The 118-check run also verifies the own native action catalogue, exact menu
selection and dispatch, and anchored zoom on actual normal/maximized windows.
The remaining eleven checks verify captured wheel modifiers
through the real deferred dispatcher path, Shift-lower minimization, repeated
Shift-raise max/restore, center restoring/preserving normal size, and exact
minimize-others ownership. All 129 also passed in the final combined native
regression run: 802 checks, process exit 0. The same run covers the replacement
WARP PDF preview pipeline and verifies clean shutdown after rendering.

Native list/action menus dispatch the selected command before destroying their
foreground owner. This preserves activation rights while selecting another
process's window; the real stack-list and minimized-window checks cover it.

The fixture candidate list is injected into operations such as minimize-others
so those tests do not change unrelated user windows. Test cleanup restores
foreground focus and terminates only the exclusively owned fixture process.

Example command from the repository root:

```powershell
dotnet run --project tests/WinForge.RegressionTests/WinForge.RegressionTests.csproj -c Release -- --window-only
```

The tests exercise native behavior through the engine/action APIs. They do not
prove every physical mouse/keyboard sequence, display topology, DPI setting,
third-party application, or integrity level works. The global hook lifecycle
also has its separate smoke check in `NativeInfrastructureChecks`.

## Restored controls

Published settings: original activation/alternate keys, button/wheel bindings,
hotclick activation, focus behavior, Aero edge/corner and smart snapping,
screen/window magnet modes, sticky-resize modes, center-region resize modes,
center percentage, horizontal/vertical split ratios, thresholds, positive or
negative gap, temporary move opacity, MDI detection, fixed-size resize opt-in,
fullscreen opt-in, and process/title/class exclusion patterns. Titlebar-only
normal and alternate button/wheel bindings have their own disabled/without
activation/with activation/always mode and native caption hit testing. Client
content and caption buttons retain their ordinary behavior.

Source implementations preserve original action modifiers: MaximizeHV is
vertical by default and horizontal with Shift; SideSnap uses the clicked window
region and Shift extends its selected edges. Shift maximize minimizes, Shift
border removal preserves the resize frame, and Ctrl lower toggles topmost.
Opacity steps are configurable (64 normally, eight with Shift, minimum32 by
default); the engine removes its added layered style on returning to255.
Shift volume uses five ordinary steps. AltTabList is monitor-local by default
and covers all monitors with Shift; Shift minimize-others is monitor-local.
Master mute and per-application mute are distinct actions. Native axis/side
snapping remembers the normal size and restores it when a subsequent drag
actually starts.

Center restores the remembered/native normal size by default; Shift keeps the
current size. Shift lower minimizes, repeated Shift raise-wheel toggles
maximize/restore, and minimize-others restores its saved set only when repeated
on the same selected owner. Its Shift monitor filter uses that selected owner.

The default middle button opens an own native menu of these actions. The
Windows system menu is a distinct configurable action. Zoom anchors the clicked
region while resizing opposite edges; Ctrl enables centered axes and Shift
uses the finer configured divisor (16 normally,64 with Shift). Maximized
windows restore before this resize. Native tracking size limits still apply.

Some semantics are intentionally explicit: per-app mute requires an active
audio session on the default render device; resume releases suspensions owned
by this engine; smart snapping uses the space beside an opposite-edge
full-height window. Application-enforced geometry and Windows permissions still
apply. An administrator target requires an appropriately elevated host.

## Not yet reconstructed from the external advanced editor

Restoring the module's dropdown actions is **not** complete AltSnap 1.68 parity.
The former `configure`, `edit-ini`, tray and help actions exposed additional
capabilities. They were not merely cosmetic entry points. The following
underlying behavior still needs native configuration and implementation:

- Remaining modifier variants and exact
  legacy behavior when an action's modifier conflicts with an activation key.
  Native confinement restricts the window; it does not globally clip the user's
  physical pointer.
- Separate wheel-up overrides, configurable during-move/resize bindings (`*M`, `*R`),
  release actions (`MoveUp`, `ResizeUp`), and no-click activation (`GrabWithAlt`).
- Arbitrary virtual-key activation combinations, custom shift/kill/scroll keys,
  extra buttons, long-click activation, keyboard-only actions, parameterized
  action strings, and command-line control of those actions.
- Advanced action families absent from the former module's dropdowns: pixel
  stepping, directional focus, monitor-edge extension/movement, layout cycling,
  physical-monitor centering, shortcut actions, and other guide/INI actions.
- Separate scroll/inactive-scroll/MDI/pause/snap/titlebar/resize/size-message/
  hit-test/bottommost exclusion and inclusion lists. The native module currently
  has the published general process and title/class exclusion lists.
- Exact legacy zone semantics: multiple named layouts, per-layout grids,
  layout switching/preview, drag-zone combination/selection modes, external
  FancyZones import and all zone-preview styling. Native zone placement is
  delegated to WinForge's own Tiling provider; that provider's capabilities
  should be assessed separately.
- Inactive-window scrolling, outline-only drag, cursor variants, topmost pin
  indicator, upper-border/caption-button rules, drag-out thresholds,
  auto-remaximize, snap-speed policy,
  synthetic size/move/accessibility notifications and performance-timer knobs.
- Complete migration of advanced INI values and every legacy edge case.

Region-anchored zoom currently honors the step policy, selected edges, and size
limits; its optional legacy post-zoom magnet-to-neighbor pass remains missing.

The retired editor, tray icon, INI editor, reload protocol and AltSnap updater
are not recreated. Their useful underlying capabilities require WinForge-owned
controls before they can be called supported. Do not present this document's
unsupported items as completed or infer a blanket guarantee from passing tests.
