# ActionBridge for Ubuntu 0.8.0

The Ubuntu companion works with the ActionBridge Android app, including the new v0.8 discovery and connection status.
It receives files, opens supported documents and links, copies received text,
and prints through CUPS. It also sends files, text and links back to your phone.
Both local HTTPS and internet WebRTC use the same activity and action engine.

## Computer-to-computer transfers

Works in both directions: Ubuntu ↔ Windows, Ubuntu ↔ Ubuntu and Windows ↔ Windows. On **Home**, choose **Add device → A computer · find nearby**, then approve the request on the receiving computer. Alternatively use **enter address or code** with its local IP address. Select the saved device on Home and choose **Send files**.

Across the internet, copy the receiving computer's private code from **Settings → Show this computer’s QR code**, then paste it into **Add device → enter address or code** on the sender. To send back, select **Linked internet device** on the receiver after its first successful connection. Keep both apps running. This uses the existing service and direct encrypted WebRTC; no backend deployment is required. Without TURN, some network pairs cannot connect. One remote slot supports one active initiating device.

Pairings persist until removed. Files stay queued when a computer is offline and resume from committed offsets. Delivered files are saved under Downloads/ActionBridge and never automatically executed. Removing a saved destination cancels waiting sends; removing its receiving permission is a separate Home device-management action. If DHCP changes a saved local IP, find/add that computer again; the saved certificate must still match.

See [the full update guide](../docs/UPDATE-v0.8.0.md).

## Install and connect

This installer is for **64-bit Intel/AMD Ubuntu desktop (amd64/x86_64)**.
It was built and tested on Ubuntu 24.04. ARM systems need a separate build.
Ubuntu 22.04 and 26.04 desktop compatibility has not been verified.

Download the `.deb`, open a terminal in its folder, and run:

```sh
sudo apt install ./ActionBridge-Ubuntu-v0.8.0-amd64.deb
```

Apt installs the GTK/Python/printing dependencies. You do not need Node/npm,
Go, .NET, a domain, an enrollment key or an additional networking app.
The installer does not change your firewall or start a root daemon.

1. Open **ActionBridge** from Ubuntu's application launcher.
2. With internet available, click **Add device → A phone · show QR code**. Automatic enrollment uses
   `https://actionbridge-connect.actionbridge.workers.dev`.
3. On Android, choose **Add computer → Scan QR code** and scan the private QR.
   Use the copy/manual-entry option if camera access is unavailable.
4. Alternatively, use nearby discovery on the same LAN and approve the phone
   in the Ubuntu window. Nearby discovery does not require internet enrollment.
5. Send a small file in both directions, then try a link, clipboard text and a
   one-page PDF before using the companion for routine work.

Keep the pairing QR private: it grants access to file and desktop actions.
The remote slot supports one active phone or initiating computer at a time. Pairings and the computer
identity remain stored across restarts and upgrades. Home lets you manage local devices; Settings lets you revoke internet access. Remote revocation requires internet;
revoked access stays disabled until you show your QR code again.

## Desktop behavior

- **Home:** choose a device and send files, text, links or clipboard contents. You can drop up to 20 files onto the sending area. For a phone to receive, keep its Android app open and select this computer. The desktop stages private copies so edits to the original cannot alter a queued transfer.
- **Activity:** Sent and Received filters with progress, results and contextual actions.
- **Settings:** startup, printing, QR code, connection help, privacy and receiver troubleshooting.

Closing the window keeps the receiver running in your signed-in session.
Reopen the launcher to return. Menu → Quit stops it; `actionbridge --quit` also
stops a normally registered desktop instance. The computer must be awake and
signed in. There is no headless system-wide service or tray-extension dependency.

Incoming files are in **Downloads/ActionBridge** (your configured Ubuntu
Downloads folder). Transfers are limited to 2 GB per file. Outgoing text/link
is limited to 8 KB; up to 100 outgoing items may wait for delivery. Completed
staged payloads are removed after phone acknowledgment. History shows the most
recent 100 incoming and 100 outgoing items. Journals remain in app data.

## Printing and clipboard

Configure printers in Ubuntu Settings first and select a default if needed.
CUPS handles PDF/JPG/PNG/BMP. Supported settings include copies, paper,
orientation, color, scaling, duplex, collation and PDF page ranges. Available
paper/color/duplex capabilities depend on your printer driver. PDFs must have
1–500 pages; images must be at most 80 megapixels. A **submitted** result means
CUPS accepted the request; check the printer queue for physical completion.
Uncertain submissions are not retried automatically to avoid duplicate pages.

On X11, received text can be copied into the clipboard directly. On Wayland,
ActionBridge displays an explicit **Copy to clipboard** button. Real Wayland
clipboard operation must still be checked on your own desktop. Sending desktop
clipboard text is an explicit action; Android background clipboard restrictions
remain unchanged.

## Firewall and internet limitations

If Ubuntu's UFW firewall is enabled, you can allow the provided profile:

```sh
sudo ufw allow ActionBridge
```

The profile allows TCP 45833 for local HTTPS, UDP 45832 for discovery, and UDP
45840–45860 for direct WebRTC. The HTTPS API restricts peers to private/local
addresses and requires approval for actions. Do not configure router port
forwarding. On guest Wi-Fi, client isolation may prevent nearby discovery.

This package uses your existing Cloudflare signaling backend. It does not
redeploy or reconfigure that backend. Files travel directly over encrypted
WebRTC when internet connectivity permits; TURN relay is not enabled. Some
restrictive mobile networks/NATs can prevent a direct connection. Nearby
connections remain available, and saved pairing does not disappear on failure.

## Updates, troubleshooting and privacy

Quit ActionBridge before upgrading, then install the newer `.deb` with apt.
Apt removal preserves files and pairing. Do not delete app data while running.
For an intentional fresh identity: revoke remote access online, remove local
phones, quit, then move the private folder aside. Your phone must pair again.

If startup fails, check another ActionBridge instance is not running and that
TCP 45833 is free. If a file/action fails, review Activity before retrying. If a
printer is uncertain, check CUPS before resending. Use Settings → Restart
receiver after resolving the issue. An unavailable document opener does not
remove the received file. Unsupported executable/script types are saved but
never automatically opened.

Private pairing/certificate/journals are in `~/.local/share/actionbridge` or
`$XDG_DATA_HOME/actionbridge`. The folder is owner-only; the TLS private key is
mode 0600. Protect the account and use disk encryption. Do not distribute this
folder. No credentials are embedded in this release or placed in command-line
arguments. No advertising/analytics are included; see PRIVACY.txt and
THIRD-PARTY-NOTICES.txt. Remote text/actions can remain in local journals.

## Build from source

Install .NET SDK 10, Go 1.26 or newer, Python 3 and dpkg-deb. From the source:

```sh
./ubuntu/build.sh /absolute/path/to/release
```

The script publishes a self-contained Linux x64 host, builds the Go remote
helper and packages the native desktop. SDKs are required only for building.
This build pins .NET/ASP.NET Core 10.0.12 and was compiled with Go 1.26.8.
Update the runtime pin and Go toolchain for later releases. Linux tests are in
`ubuntu/tests`; shared tests are in `tests/ActionBridge.Tests`. Test reports and
an actual GTK screenshot are included in `ubuntu/test-results`.

## Validation scope

Automated verification covers CUPS option mapping and validation (fixture
printer), duplicate action protection, real local HTTPS upload/download,
UDP discovery, approved pairing/revocation, restart persistence, owner-only
storage, native GTK/X11 clipboard, and the live backend enrollment/QR/revocation.
It also exercises the existing WebRTC helper in its Go test suite.

A physical printer, Android-to-Ubuntu internet transfer on real NATs, real
Wayland desktop, and clean-machine installation remain user acceptance tests.
No claim of Play Store release or public publication is made by this build.
