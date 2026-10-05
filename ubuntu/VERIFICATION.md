# ActionBridge 0.8.0 verification — 2026-10-05

Built in a Linux container using .NET SDK 10.0.401 / runtime 10.0.12, JDK 17, Gradle 9.6.0 and Android SDK 36. Windows targets x64; Ubuntu amd64. Android targets API 36 and supports API 26 upward.

| Check | Result |
| --- | --- |
| Shared Core suite | 75 passed |
| Disk-failure, cancellation and retry audit | 10 passed |
| Computer transport suite | 22 passed |
| Presence expiry and real UDP discovery/invitation simulator | 8 passed |
| Ubuntu actions / CUPS fixtures | 23 passed |
| Published Ubuntu host integration | 25 passed |
| Two native Ubuntu hosts: bidirectional transfer, persistence, restart | 6 passed |
| Actual GTK interface and compact window | 18 passed |
| Android unit/UI tests | 25 passed |
| Android debug APK and lint | Built; 0 lint errors, 51 warnings |
| Native Windows application and layout-test project | Compiled without warnings |
| Windows x64 and Ubuntu amd64 self-contained packages | Published |

The Go remote helper, phone JavaScript and Cloudflare backend are unchanged. Packages reuse the unchanged remote helpers from the previously built 0.7.0 packages; no new Go/WebRTC test run is claimed here.

Windows GUI execution is unavailable in this Linux environment. Native Windows layout tests and screenshots for compact/default windows at simulated 100%, 125%, 150% and 200% scale, plus the new code/IP connection dialog, are included in GitHub Actions. They compile here but have not executed on Windows in this session. Actual Windows scaling, keyboard navigation and phone invitation approval require device testing before public release.

The discovery check uses a loopback UDP phone simulator, not a physical Android handset or router. GTK checks run in a virtual X11 display with the real host; they do not verify GNOME Wayland or a clean-machine installation. Local TLS integration tests verify persistence and bidirectional files. Internet network pairs and physical printing are not retested for this UI update.

The supplied APK is a debug-signed test build. A stable release signing key and real-device acceptance are still needed for publishing. A different debug key can require removing the previous test APK and pairing again. The deployed service is not changed and no TURN relay is added.
