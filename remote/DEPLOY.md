# ActionBridge v0.4.0 — unified connections and actions

## Update the service once

The new Windows app automatically uses:
https://actionbridge-connect.actionbridge.workers.dev

Extract the new source/backend ZIP over your source directory (or into a fresh directory). Open Command Prompt in `remote\service` and run:

```bat
npm ci
npx wrangler login
npm test
npm run deploy
```

This updates your existing `actionbridge-connect` Worker. Keep the Worker name, Durable Object class/binding and migration unchanged. Existing paired rooms remain valid. Existing Cloudflare secrets stay in Cloudflare; no enrollment key is included in either app. This package leaves relay disabled, matching your direct-only test setup.

Open:
https://actionbridge-connect.actionbridge.workers.dev/health

Expected: `version: 2`, `automaticEnrollment: true`, `relay: false`.

Automatic enrollment is controlled separately from the old key-based pilot registration:
- `AUTO_ENROLLMENT_ENABLED: "true"`: new PCs set up automatically.
- `REGISTRATION_ENABLED: "false"`: old manual key-based registration is closed.
- `RELAY_ENABLED: "false"`: direct-only transfers; no TURN credentials are issued.

The two rate-limit bindings are created by Wrangler. They admit approximately 3 enrollment requests per IP per minute and 60 per minute across each Cloudflare location. Cloudflare rate limits are approximate and location-specific, not a billing hard cap. Unpaired installations expire after 48 hours if disconnected; a paired PC stays registered. This is basic admission control, not a complete public-service abuse defense. Monitor Worker/Durable Object usage. If later enabling paid TURN fallback, review admission and bandwidth controls before public distribution.

## Update the apps

1. Quit ActionBridge from its Windows tray menu.
2. Extract the new Windows ZIP and run Install.cmd. Keep ActionBridge.exe and ActionBridge.Remote.exe together.
3. Install the signed Android v0.4.0 APK over v0.3.0. Do not uninstall: existing trust, saved computers and history migrate.
4. Open Windows ActionBridge. Its existing remote settings are retained. On a new PC, internet setup runs automatically without a URL or setup key.
5. Android now has one main page. Select your computer, select an action, choose files or enter text, then send.

For a new phone: Windows **Connect phone** → Android **Add computer → Scan QR code**. Approve only a code from your own PC. A phone approved over LAN also receives remote pairing automatically when the service is ready.

If updating from a remote-only v0.3 pairing, scan the new Windows QR once to associate its stable PC ID with the nearby LAN entry. Local approval and a new QR also bind the PC's certificate fingerprint to its remote identity. The old entry is merged when its room matches.

## What changed

- One computer selector includes Nearby, Internet and Saved entries. Saved PCs stay present when discovery finds nothing or the internet disconnects. Only Forget removes a saved entry on the phone.
- Local discovery is preferred when available; a paired internet route is used when away. A local connection failure can fall back to the saved internet route. All five actions share the same main-page controls.
- Save, Open, Print, Copy text and Open web link use the same validated Windows action handlers locally and remotely. PDF/image print choices include printer, paper, copies, scaling, orientation, color, duplex, page range and collating.
- Remote jobs are journaled in the same durable Windows Transfers system. Activity shows connection type, action, upload progress, errors and final status. Double-click a received file to open it.
- Resuming or checking a job uses the same job ID. Retrying a completed action does not print/open/copy again. A print interrupted by a PC restart is marked uncertain and is never automatically replayed.
- Android print basics remain visible; More print options expands advanced settings. The action selector uses complete labels and shows the relevant input.
- Windows has a prominent Connect phone button, compact connection status and a shared Activity/Devices/Preferences layout.

## Test this version on your devices

- Phone on mobile data, PC on broadband: test Save, Open, Copy text and Open web link.
- Load printers remotely, print a small PDF with your selected settings, and check Windows Activity.
- Physical printer output must be checked manually. Submitted means accepted by Windows, not proof that paper printed.
- Close/reopen the phone app: the PC must remain saved. Take the PC offline and back online; reconnect without scanning again.
- Interrupt a large remote upload, then Retry the same activity item. Check exact received bytes and one action execution.
- Use the same PC on local Wi-Fi, then mobile data, and verify the same saved entry and actions.

## Current limits

Remote transfers still need this Android screen open and the PC awake with ActionBridge running. LAN transfers retain their background queue. Files are limited to 2 GB each. One active remote phone per PC is supported; the newest connection replaces the previous session. The phone can remember multiple PCs and selects one at a time. QR access is shared capability access; revoking remote access on Windows revokes the current QR for every phone holding it.

File content travels over encrypted WebRTC directly or, if you enable TURN later, through an encrypted relay. The connection Worker does not store file content. Files and durable job journals live on the receiving PC; staged payloads and history live on the phone. Remote files now use the normal Downloads\ActionBridge folder; older v0.3 remote files remain in its Remote subfolder.

The build and automated tests are verified separately from actual Android/Windows UI and physical-printer acceptance testing. Cloud deployment is an operator step and has not been performed from this workspace.
