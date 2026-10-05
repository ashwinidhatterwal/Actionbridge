# Desktop v0.6.0: Ubuntu fixes and two-way computer transfers

Update both desktop apps. The existing Android v0.5.0 app stays compatible. No Cloudflare deployment, enrollment key or new networking app is needed.

## Install

**Ubuntu amd64:** close the old app using Quit, then run:

```sh
sudo apt install ./ActionBridge-Ubuntu-v0.6.0-amd64.deb
```

Open ActionBridge from the launcher. Apt installs the system GTK and printing dependencies; the package includes .NET and the remote helper. Existing user data is retained. Other architectures require separate builds.

**Windows x64:** extract the entire Windows ZIP, then run Install-Windows.cmd. Keep ActionBridge.exe and ActionBridge.Remote.exe together. The installer stops your old instance, updates the binaries and preserves your pairings and history. Approve its Windows Firewall prompt for local and direct remote connections.

## Pair two computers on the same network

1. Keep ActionBridge running on both.
2. On computer A, open Send → Add computer and enter computer B’s local IP address. Ubuntu offers a Nearby/Internet selector. Windows accepts an IP or code in the same field. Find nearby (Windows Send tab / Ubuntu Devices tab) is an alternative.
3. Approve the request on B. A keeps B as a saved destination.
4. On A, select B and send files. On B, select the approved A device to send files back. You do not need to repeat pairing in the opposite direction.
5. Check Activity on the receiver and outgoing history on the sender. “Delivered” means the file was saved and acknowledged by the destination.

Ubuntu ↔ Windows, Ubuntu ↔ Ubuntu and Windows ↔ Windows use the same protocol. Phone destinations remain in the same Send page.

## Pair across the internet

1. On B, open Connect device, then copy its private pairing code. This existing QR/code also supports a desktop initiator.
2. On A, select Add computer and paste the abremote: code. Ubuntu requires choosing Internet pairing code.
3. Select B in A’s Send page. To send back, select the QR-paired device/phone destination on B. The destination is labeled Device paired by QR.
4. Keep both applications running and awake. The saved pairing reconnects without asking you for the code again.

Cloudflare routes authenticated connection signals. File bytes travel through the encrypted WebRTC connection. This update does not enable TURN or add a relay subscription. Direct-only internet transfer cannot connect every combination of routers/firewalls; files stay queued if a route is unavailable.

The current backend has one active initiating device per remote room. A phone and a sending computer share that slot: connecting one replaces the other. Use local pairing when you want simultaneous devices on the same LAN. Do not paste the code into public messages; it grants the existing file/action permissions.

## Reliability and controls

- Incoming files are checksum verified and saved under Downloads/ActionBridge. Desktop file sends use Save; they never automatically run or open a received file.
- Interrupted uploads resume from committed offsets. A lost final acknowledgement retries the same job ID rather than saving a second copy.
- Outgoing file bodies are private staged snapshots, retained until acknowledgement/cancellation and cleaned after durable confirmation.
- Saved destinations and trust changes are committed before the UI reports success. Windows protects destination credentials with the current user’s DPAPI; Ubuntu uses its private user data directory.
- Audited upload checkpoints, cancellation, approval/revocation, remote dispatch, clipboard response size and printing geometry fixes are merged into Ubuntu’s shared engine. Ubuntu retains CUPS print behavior and its existing UI/IPC protections.
- Cancel an outgoing job from Send. Cancellation cannot retract a file already saved at the destination.
- Removing a saved computer stops sends and cancels its queued files. To deny that device incoming access, remove its local approved-device entry on the receiver, or revoke the remote QR. Remote revocation requires internet access.
- Local saved addresses are numeric private IPs. If DHCP changes the address, find/add that computer again. A changed certificate requires explicitly removing and approving the new identity.

## Source and GitHub builds

The source ZIP contains Windows, Ubuntu, Android, the existing service and new tests. Copy its contents into the repository root, including .github/workflows. Build and signed-release workflows now produce an Ubuntu .deb alongside Windows artifacts. Android SDK setup still explicitly installs API 36 and build tools 36.0.0.

Ubuntu local build: install .NET SDK 10, Go 1.26 and dpkg-deb, then run `sh ubuntu/build.sh`. Installed users need none of these build tools. Verification commands and limits are recorded in ubuntu/VERIFICATION.md.

The Windows app and Ubuntu package are unsigned test builds. Test Ubuntu ↔ Windows on your own machines before distributing publicly; a real Windows GUI, physical printing, Wayland and separate public-NAT networks were not exercised in this build environment.
