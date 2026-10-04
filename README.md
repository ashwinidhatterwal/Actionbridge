# ActionBridge v0.5.0 — Windows/Android audit checkpoint

This checkpoint includes reliability fixes and regression tests from the [Windows/Android code audit](docs/AUDIT-WINDOWS-ANDROID.md). App version numbers remain unchanged. See that report for validation and remaining issues before building a release.

New in v0.5: Android text selection → Send to PC, and Windows Send to phone with files, links and clipboard text. See [update guide](docs/UPDATE-v0.5.0.md). The existing v0.4 backend does not need redeployment.

One Android main page for remembered Windows computers, local discovery and encrypted remote connections. Save files, open supported files, print PDF/images with printer settings, copy text and open web links. Windows uses one durable job journal and activity view for both routes.

The Windows build automatically uses https://actionbridge-connect.actionbridge.workers.dev. New installations need no service URL or enrollment key. The operator must deploy the updated backend once; see [remote/DEPLOY.md](remote/DEPLOY.md). Existing paired PCs retain their credentials. Nearby approved phones receive remote pairing automatically; QR pairing also works for phones away from the LAN.

Windows packages must keep ActionBridge.exe and ActionBridge.Remote.exe together. Install.cmd configures startup and scoped firewall rules. Android APK updates use the original private release signing key; do not publish its backup or cloud credentials.

The Android main page owns all controls, staged files and history. A bundled invisible WebView supplies WebRTC. The Go helper transports requests over encrypted data channels and inherited private pipes to the existing .NET Transfers and WindowsActions implementations. Cloudflare handles enrollment and connection signaling, with optional TURN forwarding. File bodies never pass through the signaling Worker.

Remote transfers require the main Android screen open and the PC awake. LAN transfers keep the background worker queue. One active remote phone per PC, multiple remembered PCs on the phone, 2 GB per file. Submitted printing means accepted by Windows, not confirmed physical output. Real-device and printer acceptance testing is required for the updated build.

Source includes Android, Windows, the remote service and helper, CI workflows, tests and deployment notes. See remote/VERIFICATION.md for the current automated checks and limitations.
