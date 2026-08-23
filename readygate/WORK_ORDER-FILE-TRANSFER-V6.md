# WORK_ORDER_CONFIRMED

- Work order: `WO-LANREMOTE-V6-BIDIRECTIONAL-FILE-SESSION-20260823`
- Confirmed by user: `確認執行`
- Confirmation date: 2026-08-23 (Asia/Taipei)

## Goal

Move the existing file-transfer experience into the connected session shell so either paired endpoint can initiate file transfer without first opening the remote-desktop view.

## Confirmed scope

1. After pairing, both endpoints enter a session shell with overview, remote-control, and file-transfer tabs.
2. Reuse the existing file-transfer page and behavior; do not replace it with a new Drive-style or unrelated file manager.
3. File transfer is independent from the remote-desktop tab. Either endpoint may browse the peer and initiate upload or download.
4. The initiating endpoint must request file-transfer access before connecting and the receiving endpoint must approve it during pairing. Both grants are required and expire on disconnect.
5. Upload and download have independent queues and may run simultaneously. Transfers continue when another tab is selected; toolbar state and cancellation remain available.
6. If remote video has started, switching tabs does not terminate the video stream.
7. Same-name handling offers keep-both/auto-rename as the default, overwrite, or skip. Protected-path and reparse-point rules remain enforced.
8. Preserve TLS, SHA-256 verification, partial resume, existing entry/file/batch limits, Windows 10 22H2 and Windows 11 compatibility, and pure-text-only clipboard rules.

## Implementation boundary

- Local source, tests, documentation, and a local Windows x64 test package are authorized.
- Protocol and session-shell changes required for symmetric file initiation are authorized.
- Git commit/push, release, firewall changes, relay, unattended access, remote execution, cloud storage, and folder synchronization are excluded.

## Delivery evidence

- Automated protocol/policy/loopback tests for both initiation directions, simultaneous opposite-direction transfers, authorization, cancellation, disconnect, and collision behavior.
- Debug/Release build, formatting, package integrity, and script parsing.
- Two-device Windows 10/11 testing remains a delivery gate and cannot be inferred from loopback evidence.
