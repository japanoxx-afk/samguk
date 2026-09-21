# v1.11.1 split-session investigation

## v1.11.2 crash follow-up

WER dump SamKook.FreeNet.exe.68264.dmp (2026-09-21 01:55:26) has AV execute/read
at 4403691E and return address 406347. ESI=4A52C8 is unit record 100,
type 24, owner 0, kind 0, HP 75, action 1081. Original 406336/406339 indexes
the 64-entry action table with the low byte 81, reading non-code string data
at entry 129. Sync safety latch 63D100 is zero. Thus this is not an activated
v1.11.1 fail-stop, and the fault is not a heap cleanup failure.

Native command 0D00 at 43A4E7 accepts a live target without verifying it is a
building. It writes 1081 at 43A5CB and subtracts building-upgrade costs. Giving
the original this packet for a soldier reproduces the exact dump call target.
The dump alone does not identify why the real match generated/applied that
target (stale entity ID, command/UI issue or prior divergent state remain open).

Added target kind/owner/id, queue and building-type bounds before any mutation
at 43A4E7. Native AI helper 41F630 similarly rejects non-building targets.
The original valid building path remains byte-for-byte equivalent in tests.
Unit dispatcher 406336 now checks index <64. Out-of-range values use fail-stop
reason 4, storing unit/action/kind/type in record offsets 48..60, then unwind
the dispatcher. It does not silently change the unit or erase invalid state.
The main loop handles exit; other units may finish that same simulation tick.
Compatibility profile v1.11.2 was sync-safety-2 to isolate this rule change.

## v1.12.0 deterministic barrier fingerprint

The previous guards did not detect a match that remained connected while the
two simulations silently diverged. Every native 8000 lockstep barrier now
appends a deterministic 32-bit fingerprint before the XOR trailer. It covers
the simulation RNG seed/call count, player states/resources, core unit/building
records and building production queues. UI, renderer state and pointers are
excluded. After all human barriers arrive, fingerprints are compared before
the batch is executed. A mismatch fail-stops with reason 5 and records both
hashes plus the peer slot. This is detection and evidence preservation, not
automatic state repair. The compatibility profile is sync-safety-3.

## v1.12.1 sectional fingerprint follow-up

B PC produced a reason-5 record at frame 12027 with both humans connected and
ready, proving that v1.12.0 received different fingerprints at the same native
barrier. The aggregate did not identify which field group differed and included
entity words whose exact simulation meaning was not established. Fingerprints
are now split into RNG, player state/resources, documented entity core fields,
and building production queues. Unknown entity words are excluded. A schema-3
record stores the first differing section, both slots and both hashes. The
launcher polls the per-launch report while the process remains alive, so a
fail-stop that returns to the lobby is explained immediately. Compatibility
profile sync-safety-4 prevents mixed sessions.

## v1.12.2 exact mismatch snapshot

The symmetric A/B frame-8391 records confirmed the same RNG/barrier and a core
entity mismatch, but sectional hashes cannot identify the entity. Schema 4 now
appends a fixed snapshot header and the contiguous 1,700 x 292-byte native unit
array after the 256-byte diagnostic record. Player names, account material and
chat are not included. Slot faction is packed with the native check byte in the
record. `sync_snapshot_compare.py` reports differences in documented type,
owner, kind, HP, action, order target and position fields. The compatibility
profile is sync-safety-5.

unit_dispatch_test.py reproduces original invalid target assignment and exact
call, covers bad IDs/owner/queue/type, valid building equivalence, all 256
action indices on base/observer12/observer36 images, and AI non-building guard.
APIs are stubbed. Real match and initial bad-command provenance are unverified.

User reports A and B eventually simulate different games, including in the
original executable. Available freenet.log covers lobby/auth/room traffic,
not the DirectPlay gameplay stream, so it cannot establish the first divergence.
No actual network capture or two-PC reproduction of this incident was obtained.

## Confirmed original paths

- 439DB0 waits for per-player barrier packets. After 30 seconds 423070 enables
  the missing-player drop UI; 4230B0 invokes 439770 when that control is clicked.
  This is NOT an automatic 30-second eviction. 439770 removes missing connected
  players locally, transfers/deletes via 441310, sets FD, then sends 8500.
- 4399F0 processes 8500 exclusion masks at 439B7E during packet ingestion,
  before synchronized command consumption; it also invokes 441310 and sets FD.
- 443710 compares per-player bytes at 498720 + 1168*slot, populated by 8400.
  On mismatch it also removes the other player and continues. A matching byte
  is not proof of complete state equality. The normal barrier builder 438A90
  sends 8000, not a full game-state hash. This patch adds no new checksum wire
  messages and must not be described as general desync detection.
- The game's simulation RNG at 4436C0 updates 6124E0 and increments 6124E8.
  These are recorded for comparison, not rewritten or assumed to be the cause.

sync_safety_test.py runs original 439770 twice in isolated machines, with each
local peer the only ready human and a CPU opponent. Each original removes the
other human, sends 8500, and retains gameplay state 611E14=2. This reproduces
the split mechanism, not the real network event that led to it.

## Protective change (not resynchronization)

SyncSafetyPatch applies last to the exact-build checked runtime copy. Code uses
63C400..63CFFF, record 63D000..63D0FF, latch at 63D100, API names at 63D180,
UTF-16 absolute per-launch log path at 63D800..63DFFF. These do not overlap
selection, observer, lifecycle, or timer allocations.

Hooks intercept local timeout-drop, nonempty remote exclusion of a connected
human, and original check mismatch BEFORE ownership/connection-state mutation.
They capture a 256-byte record, set normal native exit-request state 43, clear
the current wait masks, and return through the appropriate original stack
frame. A wait-loop hook unwinds an outstanding wait after the failure latch.
The ordinary main-loop exit/8100 teardown handles departure; no process kill,
unit injection, check bypass, or attempted authoritative rollback is used.
Normal voluntary departure (8100..8300) remains unchanged. A new-match wrapper
resets the latch on both native start calls (443115, 443340).

CreateFileW uses FILE_APPEND_DATA / OPEN_ALWAYS; one record per affected match.
The path is unique per launcher execution and binary records are never account
or chat payloads. Logging failure cannot prevent the exit request. On game
process exit the launcher formats the last complete record as text and warns
the user. Until the process exits, only the .bin may exist. The .bin may contain
several records if multiple matches were started without closing the process.

Profile-derived DirectPlay GUIDs isolate the 16 combinations of latency,
selection36, rice-rally, and observer settings, and isolate old launchers.
This is prevention of known incompatible settings, not a claimed explanation
for the user's original-game failure. It does not compare maps/assets and
does not authenticate peers.

## Verification and limitations

Tests execute x86 guards on baseline/observer12/observer36 configurations,
including host/peer timeout, native mismatch, complete native receive entry,
empty/stale non-human notices, wait unwind, once-only records, file API failure,
and single-player behavior. Native networking/UI/file APIs are mocked; actual
multi-PC gameplay and the original divergence trigger remain unverified.
Existing launcher runtime and native convenience/observer regressions are run
separately. Do not ship a claim that this makes all multiplayer deterministic.

Next incident: collect both sides' freenet.log and sync-logs (if generated),
map and first divergent simulation time/action. No guard record means the
incident may not have traversed these native exclusion paths; a future keyed
state checksum/command trace would require its own protocol and determinism
audit rather than hashing local selection, pointers, or rendering state.
