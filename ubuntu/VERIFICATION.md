# ActionBridge 0.7.0 verification — 2026-10-05

This update rebuilds the native Windows, GTK Ubuntu and Android interfaces around Home, Activity and Settings. Transfer engines, existing app-data paths, pairing identities and the deployed connection service are preserved.

Build environment: Linux container; .NET SDK 10.0.401, Go 1.26.8, Java 17, Gradle 9.6.0, Android SDK/API 36. Desktop packages include .NET/ASP.NET runtime 10.0.12. Ubuntu targets amd64; Windows targets x64. Android targets API 36 and supports API 26 upward.

| Check | Result |
| --- | --- |
| Shared Core suite | 75 passed |
| Audited disk-failure and retry regressions | 10 passed |
| Computer transport suite, pinned localhost TLS | 22 passed |
| Ubuntu actions / CUPS fixtures | 23 passed |
| Published Ubuntu host integration | 25 passed |
| Two running Ubuntu hosts, bidirectional local transfers and restart | 6 passed |
| GTK interface with the real published host | 18 passed |
| Go encrypted WebRTC / RPC tests with race detector | 8 passed; live Cloudflare test skipped |
| Phone JavaScript regression suite | 5 passed |
| Android unit tests, including 4 new activity UI tests | 24 passed |
| Android debug APK and lint | Built; 0 lint errors, 48 warnings |
| Ubuntu installer payload checks | 9 passed |
| Extracted Ubuntu installer host integration | 25 passed |
| Windows self-contained x64 publish | Succeeded |

GTK checks exercise the three pages, disabled sending before device selection, accurate empty device state, selection preservation and restoration, no silent recipient switch after removal, preparation controls, X11 clipboard verification, background receiving, launcher reopening, QR rendering and a compact desktop window. Screenshots are actual GTK renders using demonstration device labels with the real receiver process.

Android UI tests use Robolectric/API 28. They check the initial Save action and disabled Send button, Files/Text/Link controls, conditional print settings, Android Share link handling, draft preservation across navigation, and locking destination/payload controls during preparation. These tests are not an Android hardware or emulator session. Android lint still reports nonfatal warnings; this is not a zero-warning release.

Computer tests cover immutable staged bytes, approval and certificate checks, stable job IDs after lost acknowledgements, reverse transfers, saved destinations, cancellation, empty files and safe snapshots. Ubuntu process integration exchanges files in both directions and confirms pairing/identity persistence after restart. CUPS tests use a fixture command runner; no physical printer was tested.

Windows was cross-published from Linux; its GUI has not been operated on a real Windows machine here. Ubuntu's GUI was exercised in GTK/X11 under Xvfb, not GNOME Wayland. The .deb was extracted and checked without installing system packages; a clean-machine apt installation remains unverified. Neither desktop installer is code-signed. The Android APK is debug-signed and requires a matching signing key to update an existing installation.

The remote transport is unchanged. No new live production-room or independent internet/NAT transfer test was performed for this UI update. The current backend has one remote initiating-device slot per receiver. No TURN or paid relay was added. Before public distribution, test installation and a small transfer on real Windows and Android devices, Ubuntu Wayland, one physical printer and two independent internet networks.
