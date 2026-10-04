# Current v0.5 verification

See [v0.5 report](../docs/test-results-v0.5.0/VERIFICATION.md) and [device update guide](../docs/UPDATE-v0.5.0.md).

# v0.4.0 verification

- .NET core and real local HTTPS API: 53 checks. Additional remote dispatcher checks cover print options, shared history, duplicate finish, room ownership, method whitelist, copy and URL operations.
- Go receiver: six race-enabled tests. Actual Pion DTLS/SCTP channels over virtual UDP verify resumed durable offsets, printer RPC, large Hindi command fragmentation, legacy receive/resume, SHA verification and signal authentication.
- Bundled phone JS: two VM/transport tests exercise the actual minified bundle with the native bridge, print-option round trip, duplicate-action protection, interruption/resume, printer listing and 65,536-character Hindi text.
- Worker: seven unit policy tests, Wrangler dry-run, and local workerd/Miniflare integration. Automatic enrollment is idempotent, returns no device keys and obeys admission limits. Existing auth, signaling, permission boundaries and revocation remain covered.
- Android release APK/AAB: compiled with original signing backup; unit tests include saved-PC retention/merging. Android release lint checked. Exact totals and reports are in docs/test-results-v0.4.0.
- Windows x64 self-contained publish: compiled with the remote helper included.

Not performed here: production Cloudflare deployment, actual Android and Windows UI interaction, real internet NAT/relay acceptance testing, or physical printer output. The user confirmed v0.3 direct remote transfer before this change. The updated app must be tested on those devices after backend deployment. Remote transfers still require the Android main screen open.
