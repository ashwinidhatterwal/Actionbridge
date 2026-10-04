# v0.5.0 verification

- Signed Android APK and AAB built successfully. Android JVM tests: 14 passed. Lint: 0 errors, 47 warnings.
- Windows x64 self-contained application compiled. Core/trust/HTTPS/outbox suite: 70 checks passed.
- Go transport: 7 tests passed with race detector, including real Pion DTLS/SCTP reverse RPC and bounded outgoing file bytes. Windows helper cross-compiled.
- Bundled Android transport: 2 VM scenarios passed, covering existing phone-to-PC action/resume, PC-to-phone content, lost acknowledgement without a second save, and receive retry backoff.
- Cloudflare backend code/config is unchanged from v0.4; no redeployment needed for this update.

Not performed here: physical Android/Windows menu/UI interaction, MediaStore/FileProvider acceptance on devices, printer output, or real internet NAT acceptance. v0.4 device success does not substitute for testing these new v0.5 paths.

Remote and incoming background transfer support is not added in this release. Keep the Android main page open.
