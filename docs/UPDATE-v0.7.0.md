# ActionBridge 0.7.0 — simpler interfaces

Windows, Ubuntu and Android now use **Home**, **Activity** and **Settings**.
Pairing credentials, received files and transfer histories remain in their existing app-data locations.
The connection service is unchanged; no Cloudflare deployment or enrollment-key setup is needed.

## Install the update

- **Windows:** extract the Windows ZIP and run `Install.cmd`. Approve the firewall setup. The companion installs per user and replaces the previous executable while preserving app data.
- **Ubuntu amd64:** run `sudo apt install ./ActionBridge-Ubuntu-v0.7.0-amd64.deb`. Existing pairings and files remain available. ARM packages are not included.
- **Android:** the supplied APK is a debug-signed test build. It can update a prior APK signed with the same debug key. A different signature cannot update an installed app; use your existing signing key to build an update from this source if you need to preserve its saved computers and queued jobs. Play Store publishing still requires your production signing/upload configuration.

## Send from a computer

1. Open **Home** and choose **Add device**.
2. For a phone, choose **A phone · show QR code**. On Android, tap **+ Add → Scan QR code on PC**.
3. For another computer, choose **A computer · find nearby** and approve the request on the receiver. If discovery is blocked, use **enter address or code** with its local IP address.
4. For an internet connection, show the receiver's QR code, copy its private pairing code and paste it into **Add device → enter address or code** on the sender.
5. Select the destination under **Your devices**, then choose **Send files**, drag files onto the sending area, write text or send the clipboard.

Pairings stay saved while a device is offline. Home remembers your selected destination. Removing a destination never silently selects another one. Sending is disabled until you choose a device.

On a computer receiving an internet connection, the reverse-send destination appears as **Linked internet device** after a successful connection. It stays listed afterward. This is the existing QR connection slot; its label does not assert the sender's identity. A phone also approved locally can have a separately labelled nearby entry. Select the appropriate route. The existing service supports one active initiating phone or computer per remote slot.

Keep the receiving computer awake with ActionBridge running. For a phone to receive, keep ActionBridge open and select the sending computer. Files are saved, not automatically executed, in computer-to-computer transfers.

## Send from Android

1. On **Home**, choose your computer. Use **+ Add** if it is not saved yet.
2. Choose **Files**, **Text** or **Link**.
3. For files, choose **Save**, **Open** or **Print**. Save is the initial default. The explanation below the controls describes what will happen.
4. Choose files or enter text, then press the main send button.

Print settings appear only for Print: printer, paper and copies are visible first. Expand **More print options** for orientation, color, scaling, duplex, PDF page ranges and collation. Capabilities depend on the computer's installed printer.

Android's Share action and **Send to PC** text-selection action remain supported. Activity has **Received** and **Sent** filters. Received files have Open; received text has Copy. Changing pages preserves your draft. Background discovery keeps saved computers visible and avoids reconnecting merely because a list was refreshed.

## Find results and settings

**Activity** holds incoming and outgoing transfers with plain-language statuses and contextual actions. **Settings** holds startup, connection help, printing help and privacy information. Technical connection messages are outside the main sending flow.

Closing the desktop window keeps the receiver running. Windows uses the system tray; Ubuntu can be reopened from the launcher. Use Quit to stop it.

Internet connectivity still depends on the two networks. This update adds no TURN relay. "Submitted" means accepted by the print system, not verified physical output. Check an uncertain print job before sending it again.

## Verification limits

Windows is cross-published from Linux. Ubuntu was checked in a GTK/X11 virtual display with the real published host; this is not a GNOME Wayland or clean-machine installation test. Android build, lint and automated UI checks are documented in `ubuntu/VERIFICATION.md`. Real Windows/Android devices, public internet network pairs and physical printers still need a final acceptance test before public release.
