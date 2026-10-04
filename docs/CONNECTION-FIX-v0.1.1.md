# ActionBridge 0.1.1 connection fix

Update Windows only. Your existing Android APK remains compatible.

1. Extract ActionBridge-Windows-x64-v0.1.1.zip to a new folder.
2. Run Install.cmd. It replaces the installed companion and restarts it. Accept the firewall administrator prompt.
3. Confirm the PC window says "Secure connection checked · v0.1.1".
4. Share the file again on Android, then click Allow on the PC when prompted.

Existing identity, approved phones, history and received files are preserved. Do not delete identity.dat or clear app data.

The first-launch certificate was used directly from CreateSelfSigned, leaving an ephemeral private key that can fail in Windows Schannel. Version 0.1.1 always imports the encrypted saved PFX into the current user's persisted key storage before starting HTTPS. Existing certificates keep the same fingerprint. A loopback HTTPS startup check now runs before Ready; if it fails, the app stops discovery and writes %LOCALAPPDATA%/ActionBridge/startup-error.txt.

Validation: Windows x64 build succeeded; 41 companion checks passed, including the production certificate import path, private-key signing after generator disposal, certificate fingerprint preservation, and HTTPS pairing/upload/finalization. This build environment is Linux, so actual Windows Schannel and the user's phone still require verification.

If it still fails: report whether the PC shows Secure connection checked and whether an Allow prompt appears. If the PC reports Receiver unavailable, attach startup-error.txt.
