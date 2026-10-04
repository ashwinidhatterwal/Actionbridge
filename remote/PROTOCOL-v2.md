# Remote action transport v2

The coordination Worker still forwards authenticated offer/answer/ICE envelopes only. Phone and PC authenticate these envelopes with the private QR HMAC secret, including the SDP certificate fingerprints. Files, text, print settings, names and action results travel exclusively over the resulting DTLS/SCTP WebRTC data channel.

The `jobs-v2` ordered channel carries JSON RPC requests with type rpc, a random requestId, method and arguments. Methods: printers, create(job), status(jobId), finish(jobId), cancel(jobId). Responses contain the matching requestId and result or error. The Go helper dispatches these through newline-delimited JSON on inherited parent/child pipes. The Windows parent invokes RemoteJobs and the existing Transfers/WindowsActions implementation with client ownership remote:<room>.

An upload command selects a previously created job and receives the journal's committed offset. Binary chunks are bounded to 32 KiB. The receiver coalesces 256 KiB before asking the parent to commit bytes durably and sending an offset ACK; a short final block is also committed. Phone outstanding data is bounded to 512 KiB, with a 1 MiB data-channel buffering threshold. Interrupted uncommitted tail bytes are discarded. Retry keeps the original job UUID, content hash and action options.

Large UTF-8 RPC commands are divided into fragment messages with requestId, index, total and base64 data. Fragments carry up to 24,000 raw bytes each. The helper bounds assembled control data to 400,000 bytes and 20 fragments, requires ordered fragments, then allows only the normal RPC method whitelist. This supports the existing 65,536-character text limit without exceeding SCTP's per-message size.

The .NET action journal is authoritative. Finish verifies the complete SHA-256 and persists queued/running/terminal states. Repeated finish never replays a completed or submitted action. Restart during an action yields uncertain and requires the user to check the real application/printer before creating a new job.

QR v2 adds the Windows stable PC id and LAN TLS certificate fingerprint to the existing service/room/phone capability/HMAC secret/name. Android merges remote and discovered routes by stable id and checks this certificate pin before accepting the LAN route. Approved LAN phones can retrieve the QR capability from authenticated /v1/connection.

New PC enrollment posts locally generated, DPAPI-protected pcKey/phoneKey to /enroll; room id is derived from the PC key hash. Only hashes are retained by the Worker. Idempotent enrollment does not replace a room's keys. Rate-limit bindings and AUTO_ENROLLMENT_ENABLED gate admission. Operator key registration /rooms remains available only if separately enabled. Relay stays disabled in the supplied configuration.

The helper retains legacy files-v1 save-only reception for existing v0.3 phones during an upgrade. The v0.4 Android app has no separate remote screen and uses jobs-v2 exclusively.

## PC-to-phone extensions in v0.5

The existing authenticated jobs-v2 channel accepts outbox, download and received RPC methods. outbox returns one queued item addressed to the remote pairing slot. download reads only a staged item owned by that recipient and returns a base64 block of up to 16 KiB plus its requested offset. The phone pipelines up to 16 reads, commits received bytes locally in order, verifies SHA-256, and exports only a complete file. received is idempotent and marks delivery, removing the staged PC payload. The phone saves its completed marker before acknowledgement, so lost replies do not create another export. Failures back off for 30 seconds.

Approved LAN phones use GET /v1/outbox, GET /v1/outbox/{id}?offset=N&count=N (up to 256 KiB), and POST /v1/outbox/{id}/ack. Endpoint authentication and exact recipient ownership are enforced on every request. QR and local destinations are separate identities in the PC UI. No arbitrary file path can be read through the API.

Outgoing records include UUID id, recipient, name, kind (file/text/link), size, sha256, optional text, state, updated and live byte offset. Text is limited to 8 KiB UTF-8; links must be HTTP(S). PC-to-phone links are displayed rather than automatically launched.
