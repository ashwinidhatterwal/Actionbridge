# ActionBridge v0.5.0 — text selection and two-way transfers

Update both apps over the existing installation. Do not uninstall: existing pairings and Windows identity are retained. This release uses the same Android signing key. The existing v0.4 Cloudflare backend is unchanged; no deployment or new secret is required. TURN remains disabled unless the operator has independently enabled it.

## Update

1. Extract the Windows ZIP, keep both EXE files together and run Install.cmd. Allow its firewall request if needed. The installer stops the old companion before copying files.
2. Install the new APK on the phone as an update.
3. Open both apps. Existing PCs stay saved. Pair a new PC with Connect phone → scan QR.

## Selected text → PC clipboard

Select text in a supporting Android app, then choose **Send to PC** (possibly in ⋮). ActionBridge opens and sends the selection as a Copy action to the last selected saved PC. Press Ctrl+V on Windows after success is shown. A URL selected this way is copied rather than opened. If no saved default PC exists, choose a PC and tap Copy on PC. Unavailable PCs produce an error and never cause another PC to be chosen automatically.

Some apps replace Android's selection toolbar and omit third-party actions. Use Share → ActionBridge as a fallback, selecting Copy text to PC when you want clipboard delivery. The shortcut does not monitor the clipboard, require a keyboard replacement, or request accessibility access.

## PC → phone

Open **Send to phone** on Windows. Choose a destination:

- **Phone paired by QR · Internet or nearby** uses the existing encrypted WebRTC connection. This is the PC's existing remote pairing slot, with one active remote phone. It requires the phone to have this PC selected and the signaling service to be reachable, including when the devices are nearby.
- A named **Local pairing** uses the phone's approved local identity and HTTPS connection. This works without the remote service when both devices are on the same LAN. If you paired only by QR, use the QR destination; a local destination appears after Windows has approved the local connection.

Choose up to 20 files, drag files onto the send page, send text/link, or click Send clipboard. Files are staged as a snapshot on the PC, so editing the original does not alter the queued file. Each file can be up to 2 GB. Text and links can be up to 8 KB of UTF-8 text.

Open ActionBridge on the phone and select the sending PC. A received section shows files, text and links. On Android 10+, files are saved in Downloads/ActionBridge. Android 8–9 use ActionBridge's app-specific Received folder and an Open file button; these app-specific files are removed on uninstall. Links are never opened automatically: choose Open link or Copy. Received text has a Copy button.

The PC sent list shows queued/delivered/cancelled state and progress in bytes. Delivered means the phone acknowledged saving the item. Cancel selected removes a waiting PC staged file. Delivered metadata remains in history; the PC staged body is deleted after acknowledgement.

Interrupted file downloads resume from the phone's durable partial-file length. SHA-256 is checked before export. Repeated acknowledgement retries do not create a second exported copy. Cancelling cannot retract a file already saved on the phone.

## Current limits

Keep the Android main screen open while receiving or making remote transfers. This release does not add an Android foreground service for remote/background receiving. Files queued while the phone is closed wait on the PC. Existing LAN phone-to-PC background transfers remain supported. Keep Windows awake and the companion running.

Direct-only WebRTC still fails on some restrictive networks; this release does not enable paid TURN. Local and QR pairings are distinct destination entries. Do not treat the shared QR slot as independent per-phone accounts.

## Acceptance checks on your devices

- Update without uninstalling and confirm old pairing still works.
- Select Hindi/English text and a URL in a browser/text field; Send to PC; verify Ctrl+V. Check the toolbar ⋮ menu. Test Share fallback in an app that omits the shortcut.
- Send PDF, photo, a large file, a zero-byte file, Unicode filenames, and a link from Windows. Verify phone files and Copy/Open actions.
- Test a named local destination with internet disconnected, and the QR destination with phone and PC on different networks.
- Interrupt reception, restart both apps, and check that it resumes and saves one copy.
- Queue a file with the phone closed, then open ActionBridge. Cancel another waiting file.
- Confirm existing printing, saving, opening, copying and URL actions still work.

Automated checks cover trust boundaries, durable PC outbox, HTTPS receiving, real WebRTC outgoing RPC, phone transport acknowledgement recovery, Android metadata validation, release compilation and Android lint. Physical Windows/Android UI, actual printer output and real-network acceptance of this new version still need device testing.
