# ActionBridge Windows–Android code audit

Date: 4 October 2026 (IST). Baseline: `ActionBridge-source-v0.5.0.zip`. Ubuntu work is excluded.

Several defects remained in transfer durability, remote recovery, storage cleanup and pairing. The accompanying source contains fixes and regression tests. It is an audit checkpoint of v0.5.0, not a signed installer or a published release; installed applications and the deployed Cloudflare service have not been changed.

## Fixed findings

Priority P1 means a correctness, access-control or connection failure that should be addressed before distributing an updated build. P2 means a reliability or resource-management issue. These priorities describe impact, not how often a fault occurs.

| Priority | Trigger and original behavior | Correction and evidence |
|---|---|---|
| P1 | PC journal writes fail during upload, cancellation, completion or delivery. Memory could report an offset or outcome that was never saved, breaking retries. | Commit a snapshot before exposing the new state. UI callback exceptions cannot interrupt an outgoing acknowledgement. Dedicated write-failure tests fail on the original source and pass after the fix. |
| P1 | Saving a phone approval or revocation fails. The in-memory trust state could disagree with the durable authorization state. | Persist the proposed trust set before changing access in memory. Approval/revocation failure regressions reproduce both original defects. |
| P1 | Remote copy contains 65,536 Hindi characters. The PC echoes the full job text in a control reply: 393,591 bytes, exceeding the single-message response budget. | Return compact status metadata; keep the complete text for execution. The original fails the response-size regression and the corrected response passes. |
| P1 | A data channel closes or signaling stalls. A saved connection can remain unusable instead of recovering. A delayed ICE response from a previous PC can also alter the current connection. | Add a reconnect watchdog and guard asynchronous responses by connection generation and peer identity. Three recovery regressions fail on the original bundle and pass with the rebuilt bundle. Native ready-state visibility is explicit across the JavaScript and UI threads. |
| P1 | LAN and remote inbox handlers use different `Incoming` instances concurrently. Instance-level locks do not protect the same files. | Share the storage lock across instances. A JVM test sends 12 concurrent appends at the same offset and confirms that exactly one commits. Ownership and idempotent text completion are also tested. |
| P1 | Android's MediaStore publication fails or an earlier pending download disappears. The old code could acknowledge completion without checking publication. | Require a successful publication update; preserve the private payload on failure. Recover already-published or deleted entries during retry. Kotlin compiles; actual MediaStore behavior still requires device testing. |
| P2 | A remote phone-to-PC transfer succeeds, but its staged payload remains on the phone until later cleanup. | Centralize successful payload cleanup after the completion journal commits. JVM tests verify cleanup and preservation when the journal write fails. |
| P2 | A user cancels while an asynchronous transfer callback is still arriving. A progress or disconnect callback can overwrite cancellation. | Ignore late progress/retry/failure callbacks after cancellation; permit a confirmed PC outcome for an action that already started. JVM tests cover these transitions. |
| P2 | PC internet access is explicitly disabled, then the application restarts. Automatic setup can re-enable it. Initial setup while offline also lacks an enrollment retry. | Persist the disabled state, serialize enrollment and revocation, and retry initial enrollment while enabled. Encrypted credentials are saved atomically. Windows compilation passes; restart/offline acceptance testing is still required on Windows. |
| P2 | A newly approved local phone does not appear as a Windows send target because the UI refresh occurs before approval commits. | Notify the UI after durable approval. Core tests verify event timing and observer-failure isolation; Windows UI behavior needs device acceptance testing. |
| P2 | A very tall PDF is rendered. Bounding only raster width allows excessive allocation through height. | Bound both dimensions to 5,000 pixels while retaining normal 300-DPI rendering when possible. Geometry tests cover ordinary, tall and invalid pages. Physical printing is untested. |
| P2 | Interrupted outgoing staging leaves an orphan payload, or a destination rename fails after a temporary copy is written. | Clean orphan staging only when no matching journal exists; retain bodies associated with damaged journals. Clean failed destination copies while preserving the resumable source. Both behaviors have baseline-failing regressions. |
| P2 | Pairing input contains an absent or null phone name/token. Validation can throw instead of rejecting malformed input. | Validate missing values before accessing their length. The shared validation check passes. |

The changed code is in the Android queue/inbox/remote engine, bundled phone transport, Windows connection/UI, and shared Core transfer/trust/printing code. The backend and Go receiver source are unchanged. The existing backend does not need redeployment for these patches.

## Verification

| Check | Result |
|---|---|
| Existing and extended Core/HTTPS checks | 75 passed |
| Dedicated disk-failure, trust, cleanup and clipboard regressions | Original: 10 failures; corrected: 10 passed |
| Windows application cross-compilation | Passed; zero warnings and errors |
| Bundled phone JavaScript tests | 5 passed, including 3 baseline-failing recovery tests |
| Go receiver race-enabled suite | 7 passed |
| Worker policy unit tests | 7 passed |
| Android Kotlin compilation and JVM unit tests | Compilation passed; 20 tests passed, including 6 storage regressions |
| Android lint | Passed: zero errors, 46 warnings |

The source includes `tests/ActionBridge.Audit`, the new Android storage tests, the new JavaScript recovery tests, and this audit's logs under `docs/test-results-audit`. The patch also includes the rebuilt Android JavaScript asset, so the packaged phone transport agrees with the source.

To repeat the main checks with the relevant SDKs installed:

```sh
dotnet run --project tests/ActionBridge.Tests -m:1
dotnet run --project tests/ActionBridge.Audit -m:1
dotnet build windows/ActionBridge.Windows -m:1
cd android
./gradlew :app:compileDebugKotlin :app:testDebugUnitTest :app:lintDebug
```

Run `npm ci`, `npm test` and `npm run build` in `remote/phone`; run `go test -race ./...` in `remote/receiver`. Run `npm test` in `remote/service` for the policy tests. No Cloudflare deployment was performed. The Miniflare/workerd runtime integration suite was not rerun in this audit.

## Remaining issues and limits

1. **Corrupt incoming journals can prevent PC startup — P2, source-confirmed.** `Transfers` loads every journal without isolating malformed JSON or inconsistent file lengths. A damaged journal can abort construction. Recovery should quarantine it, preserve its ID and flag its outcome as unknown. Simply deleting it risks repeating a previously submitted print job; this audit does not add that recovery policy.
2. **PC history has no retention limit — P2, source-confirmed.** The UI caps displayed rows, but PC transfer/outbox journals and in-memory job collections retain historical items. Clipboard text can remain in those local journals. Add a retention policy with compact duplicate-protection records before prolonged use; deleting all old journals would weaken idempotency.
3. **Device acceptance remains necessary.** Windows was compiled on Linux; its GUI, DPAPI storage, tray lifecycle and physical printing were not exercised. Android tests use a JVM filesystem fixture, not a device content provider. Check Android 26/28, 29+ and 36 storage paths, two-way large transfers, restart/resume, PC replacement, and intentional remote disable/re-enable on the actual devices.
4. **Existing product limits remain.** Remote transfers still require the Android main screen open. Direct-only internet transfers cannot be guaranteed across every NAT/firewall without a relay. Printing reports submission to the Windows queue, not physical paper output. Android compilation emits existing API-deprecation warnings. Lint reports 46 warnings: 33 localization issues, 5 launcher-shape issues, and 8 other review prompts. These include the lack of explicit Android 12+ data-extraction rules and the deliberately enabled JavaScript/custom certificate trust code. The LAN API checks the exact saved certificate fingerprint and validity; the WebView uses bundled assets, blocks navigation and cancels SSL errors. These safeguards were inspected, not penetration-tested. Review the backup policy and complete icon/localization work before publishing; SDK-upgrade prompts do not by themselves establish a Play Store rejection.

This is a source and regression audit, not a complete penetration test, dependency-vulnerability certification or production load test. Use the corrected source for the next build and complete device acceptance before general distribution. Release signing and version-number updates are still required for publishing an update.
