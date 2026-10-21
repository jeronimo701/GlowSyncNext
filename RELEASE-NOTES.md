# GlowSync Next 0.3.3 preview

- Explicit breathing color-change checkbox, using the existing compatible BreathingRainbow setting. Checked: new hue each breath; unchecked: selected color with breathing brightness.
- Tests cover both color modes and the two-way UI checkbox binding. No output protocol or geometry change; firmware/GitHub remain paused.

## Previous 0.3.2 work

- Rules-only semi-automatic profiles. Removed built-in site/player/game-path/media classification; foreground URL and media inspection disabled.
- Disabled editable examples for Vivaldi/Chrome/Edge/Firefox YouTube and Music, Vivaldi Twitch, video/music players and a game placeholder. One-time, duplicate-safe migration preserves existing rules and deletions.
- Per-rule checkboxes control switching; rule ordering buttons expose priority. No match selects the Desktop preset.
- Add Rule opens a process chooser: running EXEs, refresh, browse for an EXE, manual input, optional title and target profile.
- Mode button toggles rules/manual mode with immediate feedback, including waiting/no-context states.
- Release output is a ready-to-run folder only; no ZIP. Firmware and GitHub remain paused.

## Previous 0.3.1 work

- Screen tuning and the static color picker now edit the active profile, with two-way synchronization and independent manual/game/video values.
- Removed four recognition/pause switches; automatic application/site classification remains. Browser pause no longer changes the profile. Browser SMTC cannot override YouTube site classification.
- Vivaldi/YouTube title fallback accepts hyphen, en/em dashes and the bare site title. A direct Auto button helps avoid testing recognition while manual mode is selected.
- Reset stale automatic profile candidates on selection changes.
- High-resolution, scheduled start-to-start output pacing includes blocking write time; serial wire spacing remains enforced. Engine pacing also compensates ordinary wait drift.
- UART bandwidth limits shown in diagnostics; 81 RGB LEDs at 115200 baud cannot receive 60 complete frames/s. 230400 requires receiver support, not just a software setting.
- Firmware and GitHub work paused at the user's request; neither was changed or published for this update.

## Previous 0.3.0 work

- Separate engine library without WPF dependencies; the Windows UI host owns foreground/media-session integration and settings windows.
- Diagnostics: separate capture/render/send rates, rolling mean/p95, queue age, replaced frames, write errors, reconnect counts and a redacted JSON report.
- Windows Media Sessions: foreground matching, playback type and optional desktop profile on pause. Browser sessions require a matching active-tab title. App/site fallback remains available.
- TPM2.NET UDP and OPC TCP, in addition to Skydimo, Adalight and WLED/DDP.
- Up to eight additional outputs, independent queues/reconnects and frame-segment routing; duplicate endpoints refused.
- Validated custom LED maps through JSON import/export; generated side layouts remain the default.
- Optional experimental FP16/scRGB HDR capture and luminance tone mapping. Real HDR hardware validation is outstanding.
- Compilable Adalight examples for Arduino Uno and classic ESP32, 81 WS2812B LEDs, 115200 baud, frame validation and two-second blackout watchdog.
- Local packaging script and GitHub Actions workflow, including manually requested draft releases.

Requires Windows 10 build 19041+ / Windows 11 x64 and .NET 8 Desktop Runtime, unless using a self-contained build. Tests cover software and loopback networks; physical controller validation and total photon latency measurement remain outstanding. Protected content may be unavailable to capture.
