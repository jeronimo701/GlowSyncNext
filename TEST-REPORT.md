# Local validation — GlowSync Next 0.3.3

2026-10-21: 110 automated checks and seven UI PASS records. Breathing tests cover enabled hue changes, disabled fixed color, whole-frame fill and UI checkbox toggling. UI test exposed a broken inherited binding inside the CardControl header; binding now explicitly targets EffectsConfig and is rebuilt on config replacement. Tests use isolated configuration. A separate home matrix was not identified as a compatible WLED/DDP receiver; dimensions, streaming protocol and physical behavior remain unvalidated. No controller settings or firmware changed. Private endpoint details are excluded from this public report.

## Historical validation — 0.3.2

Validated on 2026-10-21, .NET SDK 8.0.426, Windows x64, isolated test configuration.

- Solution Release build: zero warnings/errors.
- 106 automated checks passed; five Skydimo-reference/import checks skipped. Includes no implicit selection from YouTube URLs/titles, player names, Steam/Epic paths or SMTC; explicit Vivaldi title rules; checkbox off really disables matching; example migration, no duplicates/re-enabling/resurrection; existing capture/network/layout regressions.
- Six UI PASS records: immediate rules-mode on/off feedback; running-process enumeration and chooser creates the selected EXE/title/profile rule; 81-LED layout; cross-tab bindings and manual/game/video isolation; output/diagnostics rendering and report redaction; offscreen XAML rendering.
- Inspected profile rule grid and process chooser renders; fixed compressed column widths and dialog button contrast/height.
- No installed configuration, physical controller, firmware or GitHub state changed. No claim of live acceptance testing in the user's Vivaldi or physical LED output.
- Delivery is an unpacked launch folder, not a ZIP.

## Historical validation — 0.3.1

Validated on 2026-10-21 with .NET SDK 8.0.426, Windows x64. Installed user configuration was not modified; tests use isolated directories.

- Release solution build: zero warnings/errors.
- 105 automated checks passed; five Skydimo-reference/import checks skipped because original Skydimo files are unavailable. Real SDR Desktop Duplication passed.
- Four UI PASS records: live 81-LED layout; screen FPS/static color edit the active profile; two-way synchronization; manual/game/video isolation; full config replacement rebinds controls; recognition switches removed; output/diagnostic rendering and report redaction.
- Synthetic Vivaldi title cases cover hyphen, en dash, em dash, bare YouTube title, normal pages and YouTube Music. Browser SMTC cannot change the site-selected profile or switch to desktop on pause. These are classifier tests, not a live Vivaldi/YouTube acceptance test.
- 81-LED serial budget tests: 115200 baud ~41.64 FPS safe limit; 230400 baud allows a 60 FPS target mathematically. Receiver support for 230400 on SKA127 is unverified.
- Simulated blocking transport (8 ms requested sleep per write), 60 FPS target: observed average interval ~17.44 ms (~57.3 writes/s) on this host. This verifies that write duration is no longer added twice; it does not establish sustained 60 FPS or physical LED refresh. Host scheduling, capture, receiver behavior and actual USB hardware remain relevant.
- Output pacing now uses high-resolution scheduled start-to-start deadlines and a serial wire-time minimum. Render deadlines compensate ordinary wait drift. Existing loopback, layout, HDR math, media, migration and routing regression tests remain passing.
- Profiles/outputs/diagnostics rendered offscreen; profile image inspected. No firmware compilation/flashing, GitHub action or publication was performed for this update.

Still requires user validation: live Vivaldi/YouTube recognition (including fullscreen/custom titles), physical SKA127 output rate, USB reconnect/sleep, exclusive games, real HDR color accuracy and display-to-photon latency.

## Historical validation — 0.3.0

Validated on 2026-10-20 with .NET SDK 8.0.426, Windows x64.

- Release solution build: success, zero warnings/errors; separate GlowSync.Engine and GlowSync.Next assemblies.
- Automated checks: 92 passed. Five original checks skipped because Skydimo installation/reference files are unavailable.
- UI smoke: three PASS records covering live NumberBox layout changes to 81 LEDs, active-profile brightness binding, profile/output/diagnostic pages, redacted report and offscreen rendering.
- Live Windows SMTC API: RequestAsync succeeded and returned one session; media titles were not logged. Deterministic tests separately cover active-tab matching, pause, type, ambiguity and fallback.
- Desktop Duplication: real SDR GPU capture, mip downscaling/readback and sampler passed. Optional HDR negotiation also captured the SDR desktop as B8G8R8A8_UNorm. This does not validate an actual HDR display.
- HDR numerical tests: known sRGB transfer reference, monotonic luminance ramp, finite/negative values, FP16 RGBA to BGRA channel conversion.
- Real network loopback: fragmented DDP, equal-size/padded TPM2.NET, OPC TCP framing, simultaneous routed outputs, duplicate endpoint refusal and independent frame-rate budgeting.
- Configuration: stale 65→81 map, generated/custom layouts, empty-map regression, dynamic subscriptions, round trips and versioned v1 backup before v3 upgrade.
- Final headless test mode: 90 passed, six skipped (Skydimo references and desktop capture). GitHub-hosted execution is not yet verified.
- Arduino Uno compiled successfully with PlatformIO atmelavr 5.1.0, FastLED 3.9.15: 575/2048 bytes RAM, 6570/32256 bytes flash.
- Classic ESP32 esp32dev compiled successfully with espressif32 6.10.0 / Arduino core 2.0.17 / FastLED 3.9.15: 22496/327680 bytes RAM, 295361/1310720 bytes flash. A short Windows path resolved compiler command-line limits.
- Workflow YAML parsed; expected jobs/permissions checked. Action refs were verified against the official repositories. No GitHub workflow or release was run.

Not validated: physical Skydimo/Adalight/WLED/TPM2/OPC devices, real USB unplug/reconnect/suspend behavior, firmware flashing, power/signal integrity, exclusive-fullscreen game compatibility, HDR visual accuracy, browser/player coverage or end-to-end display-to-photon latency. Software timing and loopback delivery do not establish these results.

Original pristine checkout remains unchanged. Existing GlowSync files and the user's installed configuration were not modified by these tests. Test runs use isolated data directories.
