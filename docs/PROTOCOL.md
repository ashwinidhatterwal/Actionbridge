# Local protocol v1

## Discovery

UDP 45832, IPv4 broadcast or multicast `239.255.42.99`. Request is UTF-8 `ACTIONBRIDGE_DISCOVER_V1`. Response is JSON `{version:1,id,name,port:45833,fingerprint}`. The fingerprint is lowercase SHA-256 of the DER server certificate. Discovery is unauthenticated and is never authority for changing a remembered certificate.

## HTTPS API

TCP 45833; persistent self-signed certificate protected using Windows CurrentUser DPAPI. Android pins the exact certificate and verifies validity dates. It does not rely on DNS or the certificate's IP SAN, because DHCP addresses change. No cleartext file or token transport is enabled. Phone tokens are 256-bit random values, encrypted on Android with an Android Keystore AES-GCM key; Windows stores only SHA-256 token digests. Raw tokens are never written to logs.

Unauthenticated: `GET /v1/hello`, `POST /v1/pair`.

Pair body: `{clientId,name,token}`. Client creates and stores the token before requesting approval. PC displays a nonblocking Allow/Decline dialog; expires after 55 seconds. Server request expires after 60 seconds. Only one approval is pending at once; per-address rate limit applies. Allow persists the digest. Repeating an already approved request is idempotent.

Authenticated routes require `Authorization: Bearer <64-hex-token>`:

- `GET /v1/status`: cheap identity/readiness check.
- `GET /v1/printers`: `{name,papers,supportsColor}[]`.
- `POST /v1/jobs`: immutable request `{id,name,size,sha256,action,printer?,copies,landscape,paper,color,fit,text?}`.
- `GET /v1/jobs/{id}`: journal containing request, clientId, offset, state, message and updated time. Only the creating phone can access the job.
- `PUT /v1/jobs/{id}/content?offset=N`: exact Content-Length required, at most 4 MiB. Flushes bytes before committing offset. Wrong offsets return 409 and can be resolved by fetching status.
- `POST /v1/jobs/{id}/cancel`: cancels uploading/queued work and removes its partial file; started or completed actions are retained.
- `POST /v1/jobs/{id}/finish`: verifies size/checksum, finalizes the file, queues the action. Retrying this endpoint does not repeat an executed action.

States: uploading → queued → running → completed / submitted / failed / uncertain. Text/URL jobs skip binary upload. Actions: save, open, print, copy, url. No raw command execution, remote folder path or arbitrary executable-open route exists.

An ID is a canonical UUID. Duplicate creation must have the identical request and owner. Filename is flattened and sanitized; output path includes the full UUID. Open actions use a restricted extension allowlist. URL actions support HTTP/HTTPS and reject embedded user information. Printing only accepts PDF/JPG/PNG/BMP extensions, then the Windows renderer validates the content.

## Recovery and action semantics

Every accepted chunk is flushed, then the journal is atomically replaced. Startup truncates partial files to the last journaled offset. Finalization copies to a temporary file on the destination volume, flushes it and atomically renames it. The source partial is removed only after the queued journal is durable.

The journal is written as running before the side effect starts. A running record found at restart becomes uncertain; it cannot automatically execute again. Queued records execute when the original client retries finish. Exactly-once physical printing cannot be guaranteed across spooler/driver crashes. The policy is conservative at-most-once action initiation per transfer ID, with explicit uncertainty rather than silent reprinting.

Completed journals remain durable to recognize old transfer IDs. There is a global limit of 128 unfinished jobs; completed history is retained. The UI displays the most recent 100 PC records / 20 phone records. The phone prunes terminal local jobs and payloads older than seven days.

No cloud service, telemetry endpoint or account provider is part of the protocol. Operating-system components and printer drivers may have their own network behaviour outside this app.
