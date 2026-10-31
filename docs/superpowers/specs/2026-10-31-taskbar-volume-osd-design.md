# Taskbar volume OSD design

- **Intent:** When the taskbar mouse wheel changes master volume, show only the existing WinForge volume OSD with its interactive slider; do not show a separate notification or Windows' response to the original wheel input.
- **Behavior:** Keep the OSD visible until dismissed when opened by taskbar scrolling, so its slider remains available. Other volume OSD triggers keep their current timeout.
- **Goals:** Reuse the existing OSD and Core Audio controls; keep displayed volume and mute state synchronized; preserve existing taskbar scroll settings.
- **Non-goals:** Change hardware-key behavior, media flyout behavior, or redesign the OSD.
- **Approaches:** (1) Reuse the volume OSD persistently for taskbar scroll (recommended; smallest change and supports the existing slider). (2) Reuse it with its normal timeout (simpler, but it may disappear before the user can use the slider).
- **Implementation:** Taskbar wheel input is already consumed by WinForge's low-level mouse hook. Use WinForge's audio event context for the resulting volume update, then open the volume OSD directly; this avoids classifying the update as external and opening the external-change OSD/media flyout. Support a persistent OSD mode for this caller and leave the slider's Core Audio updates intact.
- **Constraints:** Scope is taskbar wheel input. Do not change hardware-key behavior or promise to suppress Windows OSD for volume changes initiated outside this path. Preserve project conventions and avoid new dependencies.
- **Approval status:** Awaiting user approval before implementation.
