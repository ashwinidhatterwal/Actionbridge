# Desktop v0.6.0 verification — 2026-10-05

Ubuntu/Linux amd64 build; Ubuntu 24.04 container, Python 3.12, GTK 3/Xvfb/X11. Build tools: .NET SDK 10.0.401 and Go 1.26.8. Bundled .NET/ASP.NET runtimes: 10.0.12. Windows x64 is cross-published from the same source. Android remains the preceding audited v0.5.0 source, with its API-36 GitHub workflow fix preserved; Android was not rebuilt in this task.

| Check | Result |
| --- | --- |
| Shared Core regression suite | 75 passed |
| Audited disk-failure/retry regressions | 10 passed |
| Shared computer transport suite, real pinned localhost TLS | 20 passed |
| Ubuntu action/CUPS fixture suite | 23 passed |
| Published Ubuntu host integration | 25 passed |
| Two published Ubuntu hosts, bidirectional LAN transfers and restart | 6 passed |
| Native GTK UI with published host | 10 passed |
| Go WebRTC/fragmentation/concurrency suite, race detector | 8 passed; opt-in live test skipped in ordinary run |
| Opt-in live Cloudflare signaling/WebRTC test, race detector | Passed |
| Ubuntu .deb metadata, ownership, modes and payload validation | Passed |
| Extracted .deb host integration | 25 passed |
| Windows x64 publish | Succeeded |

Computer checks cover immutable staged file bytes, receiving approval, wrong certificates and credentials, stable job IDs after lost chunk/finish acknowledgements, reverse downloads, restart-persistent destinations, empty files, cancellation, destination removal and safe public snapshots. Two real Ubuntu processes exchanged an 800-KB file in both directions over pinned HTTPS and resumed reverse delivery after restart. The shared suite runs on Linux; it is not evidence of a real Windows GUI session.

The live test enrolls a temporary room at the existing production service, connects the PC and phone-role desktop endpoints through authenticated WebSockets, requests ICE for both roles, completes signed offer/answer negotiation, opens an encrypted jobs-v2 channel, exchanges an RPC and deletes the room. Pion virtual UDP peers substitute for restricted native interface enumeration in this environment. The separate fragmented-transfer test sends 262,144 bytes and performs 16 simultaneous reverse chunk requests over real encrypted Pion channels on a virtual network, under the race detector.

A full remote transfer attempt between the two published hosts timed out in this environment. Therefore no completed desktop-to-desktop transfer across independent real internet/NAT networks is claimed. That remains a required check on real computers, especially without TURN. The live signaling test and virtual UDP transport tests passed independently.

GTK checks launch the actual published receiver, render all four tabs, verify X11 clipboard content, phone and computer destination selection, hide/reopen behavior, QR generation and screenshot capture. They do not cover GNOME Wayland or a real desktop login. The package is extracted and checked without changing system packages; its host is rerun through the local integration suite. This does not constitute a clean-machine apt installation.

CUPS tests use a fixture command runner and cover capabilities, paper, copies, orientation, color, duplex, scaling, collation, PDF ranges, argument handling and uncertain queue outcomes. No physical printer was tested. The preserved Android protocol supports the existing actions, but an Android device was not connected in this task.

Before public distribution: install on clean Ubuntu and Windows machines; test Ubuntu ↔ Windows, Ubuntu ↔ Ubuntu, your Android app locally/remotely, Wayland clipboard behavior, one physical print and separate internet networks. Windows and Ubuntu artifacts are unsigned. Only amd64/x64 installers are included. The existing backend has one active remote initiating-device slot; a phone and a desktop share it. No TURN or paid relay was added.
