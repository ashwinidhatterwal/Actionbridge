ActionBridge 0.8.0

Windows, Android and Ubuntu companions for local and direct internet file transfers, links, clipboard text and desktop actions.

See docs/UPDATE-v0.8.0.md for the desktop redesign, live connection status, nearby-phone discovery and installation instructions.

WINDOWS: Quit the old receiver from its tray menu, extract the Windows package and run Install.cmd.
ANDROID: Install the new test APK for discovery from a computer. Keep the app visible and select the sending computer to receive files.
UBUNTU: sudo apt install ./ActionBridge-Ubuntu-v0.8.0-amd64.deb

No backend update is needed. Pairing identities and queued files are preserved by desktop upgrades. Android debug-key changes may require uninstalling the old test APK and pairing again.

GITHUB: Put this source archive's contents at the repository root, keeping android/, windows/, remote/, ubuntu/, tests/ and .github/. Commit and push to main. The Build Android, Windows and Ubuntu workflow builds the applications and runs checks. Native Windows UI checks also publish screenshots. This archive has not been pushed automatically to GitHub.

BUILD: .NET 10 SDK, JDK 17, Android SDK 36, Gradle wrapper and Go 1.26. See the workflow for commands.

VERIFICATION: ubuntu/VERIFICATION.md records this session's checks and limits. Windows GUI, real display scaling and phone Wi-Fi pairing require real-device acceptance testing before publishing.
