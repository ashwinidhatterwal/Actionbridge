# ActionBridge 0.7.0

Files, text and links between Android, Windows and Ubuntu, nearby or over the internet. All three native interfaces now use **Home**, **Activity** and **Settings**. Home focuses on choosing a device and sending. Activity holds incoming and outgoing transfers. Settings holds startup, printing, connections and help.

See [the v0.7 setup and update guide](docs/UPDATE-v0.7.0.md), [Ubuntu installation](ubuntu/README.md), and [current verification results and limitations](ubuntu/VERIFICATION.md).

## Features

- Saved device selection and pairings that remain available while devices are offline.
- Files, links and clipboard text in both directions; computer-to-computer transfers across Windows and Ubuntu.
- Android Save, Open, Print, Copy and Link actions, with conditional printer settings.
- Android Share and text-selection **Send to PC** actions.
- Durable staged file copies, resumable transfers, activity history and cancellation.
- Local pinned HTTPS and encrypted direct WebRTC through the existing signaling service.

Internet setup uses `https://actionbridge-connect.actionbridge.workers.dev`. New installations need no domain, enrollment key or extra networking app. The already deployed backend needs no update for v0.7. Pairing codes are private credentials; share them only with devices you trust.

Keep receiving computers awake and ActionBridge running. For a phone to receive, open ActionBridge and select the sending computer. Internet phone transfers need its screen open; local sending uses the existing background queue. Closing a desktop window keeps receiving until Quit.

The current remote slot accepts one active initiating phone or computer at a time. This build adds no TURN relay. Some network pairs cannot establish a direct connection. Submitted printing means accepted by the print system, not physically verified output.

## Build

The repository contains native Android, WinForms Windows and GTK Ubuntu interfaces; shared .NET transfer/action engines; a Go WebRTC helper; the deployed Cloudflare service source; tests and GitHub Actions.

- Android: Java 17, Android SDK 36, then `cd android && ./gradlew :app:assembleDebug :app:testDebugUnitTest :app:lintDebug`.
- Windows: .NET 10 and Go 1.26; the workflow publishes a self-contained x64 executable and its remote helper. Package both executables with the installer scripts.
- Ubuntu: .NET 10 and Go 1.26; `ubuntu/build.sh` publishes and packages the self-contained amd64 host and GTK desktop. See the Ubuntu README for dependencies.

Use the existing private production signing key for Android updates and Play Store publishing. The supplied test APK is debug-signed. The desktop packages are unsigned; clean-machine and real-device acceptance checks remain necessary before public release.
