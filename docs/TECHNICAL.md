# Technical references and validation boundaries

Primary references used while implementing 0.3:

- Windows Media Sessions: https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager
- Playback type: https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmediaproperties.playbacktype
- DXGI format negotiation: https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_5/nf-dxgi1_5-idxgioutput5-duplicateoutput1
- Windows scRGB/HDR conventions: https://learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range
- DDP: https://kno.wled.ge/interfaces/ddp/
- TPM2.NET receiver and equal-payload behavior: https://github.com/Aircoookie/WLED/blob/main/wled00/udp.cpp
- WLED real-time configuration: https://kno.wled.ge/interfaces/udp-realtime/
- OPC reference implementation: https://github.com/zestyping/openpixelcontrol
- FastLED: https://github.com/FastLED/FastLED/tree/3.9.15

Tests distinguish deterministic logic, real loopback transports, live Windows API access, offscreen UI rendering and physical hardware. The first four can run without a strip; physical compatibility, HDR visual fidelity and photon latency cannot be inferred from them.

Capture timings include acquisition/readback, not display-to-light latency. Driver write return does not guarantee UART completion; UDP has no receiver acknowledgement. Reported serial wire time is bytes × 10 / baud. Outputs replace pending frames before serialization. Each output owns a worker and independent connection state.

HDR uses the Windows scRGB convention 1.0 = 80 nits, clamps negative/out-of-sRGB values, applies a luminance-based extended Reinhard operator and the sRGB transfer. It is an explicit creative mapping, not a calibrated match to a particular monitor's tone mapper. The optional FP16 path requires a driver that returns that format; diagnostics expose the actual format. SDR fallback remains available.

Version 0.3.3 uses explicit enabled process/title rules only. Foreground URL inspection and SMTC inspection are disabled; built-in site/player/game-directory classifiers were removed. The retained media observer/matcher code and their unit tests are historical utilities, not an active automatic profile-selection path. No playback control requests are made.
