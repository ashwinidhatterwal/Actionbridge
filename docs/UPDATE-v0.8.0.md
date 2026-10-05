# ActionBridge 0.8.0 — clearer desktop connections

This update addresses the clipped Windows text and missing connection/discovery feedback reported in the Windows screenshots.

## What changed

- Windows has a workspace sidebar, a persistent device list, a clearer sending area and a visible **Find nearby** button.
- Headers and instructions use content-sized rows. Add-device and text-entry dialogs use resizable layouts instead of fixed-height instruction labels.
- Devices explicitly show **Connected** or **Disconnected**, plus Nearby or Internet. Saved pairing alone never establishes live presence. Devices remain saved while disconnected; queued files wait for reconnection.
- Windows has one add-device window with **Nearby**, **QR code**, and **Enter code / IP** tabs. It discovers phones and computers together; refresh and no-results help are visible.
- Ubuntu has live device statuses and a **Find nearby phone** action, alongside existing computer discovery.
- Android answers nearby-phone discovery while its main app is open in the foreground. An invitation prompts on the phone, and the computer also asks for approval. Discovery never grants access by itself.
- Android reports successful nearby receiving polls and internet data-channel state in its connection status.

## Install

Quit the old Windows receiver from its tray menu, extract the Windows ZIP and run **Install.cmd**. This preserves the computer identity, pairings and transfer journals. Restart ActionBridge afterward.

Install the new Android test APK to enable discovery from a computer. If Android rejects the update because the old debug signing key differs, uninstalling the old test app is necessary; this clears that phone's saved identity and requires pairing again. This APK is for testing, not a Play Store release.

For Ubuntu, run `sudo apt install ./ActionBridge-Ubuntu-v0.8.0-amd64.deb` and restart the app.

## Find a phone from your computer

1. Open the updated ActionBridge app on your phone and keep it visible.
2. Put both devices on the same Wi-Fi or LAN. Guest networks may block device discovery.
3. On Windows, choose **Find nearby**, select the phone and **Connect selected**. On Ubuntu, choose **Find nearby phone** and **Invite phone**.
4. Tap **Connect** on the phone and **Allow** on the computer. Select the sending computer on the phone to receive files.

QR pairing remains available when nearby discovery is blocked. Manual IP entry is for computers, not phones. No extra networking app, domain setup, enrollment key or backend redeployment is required.

## What the status means

Nearby status uses recent authenticated requests and expires after 15 seconds without traffic. Computer sender status refreshes after successful transport requests. Internet status comes from the WebRTC connection state, not the signaling service's availability. A gray disconnected device stays in the list. An internet QR slot is labelled **Linked internet device** because this protocol does not identify the initiating device by name.

A phone can be discovered while its app is visible without being connected yet. For receiving files, leave the app open and select the sending computer. Discovery does not guarantee transport availability.

## Comparison that informed this update

[LocalSend](https://localsend.org/) puts selecting files and tapping a nearby device at the center of its flow. [KDE Connect's pairing guide](https://userbase.kde.org/Tips/Pairing_your_phone_and_PC_with_KDE_Connect) documents confirming pairing from the receiving device. ActionBridge lacked a similarly visible desktop discovery action, live status, and a clear pairing path. This update applies those workflow principles while keeping ActionBridge's printing, action handling, queues and direct internet transport.

## Verification limits

Windows was compiled and cross-published from Linux; its real Windows GUI and display scaling could not be executed here. Native Windows label-layout checks and screenshots at simulated 100%, 125%, 150% and 200% sizes are included in GitHub Actions, but have not been run in this session. They supplement a real-device display-scaling check.

The automated discovery test uses a loopback UDP phone simulator. Real Windows/Android Wi-Fi discovery, approval dialogs and internet connectivity still need device testing. This update does not add TURN or change the Cloudflare backend.
