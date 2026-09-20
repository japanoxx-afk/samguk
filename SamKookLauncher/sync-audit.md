# v1.11.1 split-session investigation

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
