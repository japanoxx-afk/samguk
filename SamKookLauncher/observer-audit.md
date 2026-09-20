# Observer implementation — opt-in experimental feature in v1.10.0

Status: local v1.10.0 implementation and component tests completed; live three-PC
observer validation outstanding. The launcher exposes a default-off trial option.
At the user's request, v1.10.0 is distributed through the normal launcher update channel after the initial v1.10.0-test.1 prerelease. The feature remains experimental and default-off. This document does
not claim live multiplayer spectator correctness.

Original executable SHA256:
`39A11E76F5328A66A4FE8DCB1318ECE6362843D8192CAA8C7E15F0FC08ABDC62`.

## Confirmed native paths

- Player records: stride 1124 (0x464), eight multiplayer slots. DirectPlay ID
  at 49B02C + stride*slot; room state at 49B046; faction at 49B028.
- Room dialog initialization 42D5E8 uses strings 464928 (open), 46484C (closed),
  464844 (computer). Combo controls 0x4B1..0x4B8. Index 3 is the connected player's
  name, not a free enum value for observers. Blindly inserting an item at index 3
  collides with existing player-name handling and kicks/deletes.
- Slot change command 3400 is emitted at 42DAA7 via 42D4B0. Join 3200 processing
  42D0D2 accepts ONLY state 0 with no DirectPlay ID, then writes state 3 at 42D15A.
  An added state 4 is not joinable; joining a state-0 observer without retaining
  a separate role loses its observer designation.
- Room state snapshots use command 3100; packet offsets +0D slot, +0E state,
  +0F faction; peer updates at 42CE18..42CE68. Role persistence must cover both
  initial snapshots and subsequent slot changes, leave/rejoin and host changes.
- Lockstep membership 439DDA and 439F81 requires state exactly 3. Defeated/leaving
  states FE/FD do not provide a ready-made connected spectator mode.
- Native departure 443710..4437C8 checks player frame counters; calls 441310,
  removes the DirectPlay ID and changes state to FD. A connected observer must
  not be mislabeled as departed simply to remove it from victory checks.
- Player relation fields at +49B048 and +49B04A are distinct. The latter is read
  by visibility paths (e.g. 442A29/442A8D, 41BAA7/41BC7E); the former participates
  in AI/hostility and other logic. Do not turn every player into an ally to grant
  observer sight. Direction is now verified as source-owner row -> recipient
  bit. Full terrain/minimap and live match coverage still require tests.

## September 20 follow-up: native shared vision

The user confirmed three ordinary players can join, start, move and produce
normally, and identified the existing in-game alliance/shared-vision UI. This
is a passing baseline, not a passing observer-mode test.

The audit now executes original instructions at 4057D3 (visibility-update gate)
and 442A13 (unit-render sharing gate) for all 512 observer/owner/viewer
combinations. Adding the observer bit to each source owner's 49B04A mask admits
that observer and preserves ordinary opponent isolation; 49B048 alliance masks
remain unchanged. Setting only the observer's own row would share the
observer's sight outward, the wrong direction.

Original diplomacy-command code at 43B752 overwrites both masks from the packet,
verified for all eight slots. A one-time start-of-game mask change is therefore
insufficient: later shared-vision changes can revoke the observer's view.
The observer role must be synchronized separately and enforced when native
vision masks change. These tests are read-only emulation of the original EXE,
not an enabled runtime patch and not proof of complete fog/minimap semantics.

## Verified failure of the simple approach

`observer_audit_test.py` runs original x86 admission and lockstep branches without
modifying the EXE. A state-4 slot is neither joinable nor part of native lockstep.
State 3 is connected, but means a normal participant throughout the engine.
Thus neither adding a label nor globally changing 3 to 4 is a valid solution.

## Implemented local trial

`make_observer_manifest.py` emits exact-build original-byte guards and x86
wrappers; `ObserverPatch.cs` applies the embedded manifest only to the runtime
copy after existing patches. Code uses .fnfix+20480..32767 and private state
.fnfix+45056..47103, separate from selection, rally, timer and rice features.
Unexpected bytes or overlapping reservations abort instead of guessing.

- Native slots stay 0/3. Wire state bit 40 carries a separately stored role in
  host snapshots and slot changes. Validate host slot zero, bounds and status;
  strip the bit in a private dispatch copy, never in the shared outgoing queue.
- Preserve native connected-name index 3. Insert a placeholder and observer
  index 4; translate native/UI states through a room-only Win32 wrapper. Role
  persists on leave/rejoin; reset on opening a new room. Host cannot be observer.
- Native 430B20 returns a separate application GUID in trial mode, isolating
  DirectPlay sessions from old peers. This is protocol isolation, not encryption.
- Skip observer melee start-location creation and preplaced unit/building
  records, zero resources, skip automatic defeat and local victory/loss. Keep
  native connected status 3 and lockstep membership while the observer is present.
- OR connected observer recipient bits into active sources' native shared-vision
  masks at map load and diplomacy changes. Do not alter active-player alliances.
- Gate sender 438CF0 and receiver 43A210. Only native no-op 0100, chat 0300 and
  control 8000..8400 pass for observers; active players keep original behavior.
- Native synchronized observer departure sets FD and clears that observer's
  DPID without the normal defeat/ally/unit mutation path.

## Verification and remaining integration checks

`observer_patch_test.py` executes installed x86 hooks in both 12/36 builds,
including native visibility gates and a mocked SendDlgItemMessageA model. Tests
cover role encoding/decoding, authority/bounds, unchanged queued wire bytes,
stable UI indices and repeated snapshots, empty/occupied role toggling, initial
spawn gates, resources, command allowlist, defeat/victory and departure state,
room reset, offline isolation, minimum-player counts and diplomacy changes.
Existing quality, selection and rice native-code tests pass with observer hooks.

Real two-player + one-observer testing remains mandatory: room labels/start,
fog/units/minimap, opponent isolation, production/combat consistency, diplomacy
changes, normal observer departure, disconnection and actual player victory.
Mocked UI/emulation cannot prove real DirectPlay ordering or rendering behavior.
Observer network loss still uses native lockstep timeout handling; this is not
an asynchronous spectator stream. Saved scenarios, script-driven maps and host
migration are outside this trial. Observer must leave manually after the match.

## v1.10.1 start-admission regression fix

User reported host A human, B observer, C computer could not start. The observer
exclusion hook was correct, but native 42DC40 still admitted humans>=2 OR
computers>=3. The prior test checked exclusion only, not the complete decision.
New tests first reproduced the failure against the v1.10.0 runtime. Hook 42DC40
now admits humans>=1 AND humans+computers>=2 after the native counting loop;
observers remain excluded. 165 room compositions are executed through the full
native counting/admission path in both 12/36-selection builds. The host remains
a player; host-observer support is not part of this fix. Live two-PC verification
is still needed, and the feature remains default-off and experimental.

## v1.10.3 full outcome-dispatch regression fix

User confirmed A can now join B's room and observe both B and a computer, but
receives a defeat dialog shortly after starting. The preceding tests covered
the two outcome callbacks only, not their caller's subsequent fallback.

Native `447AA0` invokes defeat callback `61304C`, then victory callback
`613048`. Even when both return zero, `447AD5..447B2E` checks the local
building counts (`49B17C`) and unit counts (`49B0C8`). With both empty it sets
dialog kind `477FA8=18` and tail-calls `4243E0`. Observers intentionally have
no starting units/buildings, so the existing callback exclusions were not
sufficient. `observer_outcome_test.py` reproduces this exact loss path on the
v1.10.2 observer executable.

New hook `447AD5` returns only for an active local observer. Other local
players replay the original `movsx ecx,[59EE52]` and resume at `447ADC`.
The global defeat scan and victory callback still run before this guard;
there is no forced alliance, fake building or change to other players' results.
All seven non-host observer slots, 100 repeated dispatcher checks, ordinary
and inactive-mode loss, human loss and computer defeat leading to player
victory pass in 12/36-selection builds. These execute native dispatch/callback
code with UI/departure API stubs, not a real two-PC game. Actual updated-game
confirmation is still required. The trial's manual observer exit after the
match remains unchanged.

## v1.11.0 host observer and read-only selected-player inspection

User confirmed v1.10.3 works, then requested host observation and selected
player resources/production. Protocol 2 GUID is
`62046975-3128-4fd1-91b4-240b14bb2190`; every participant must update.

Host slot control 4B1 is enabled at 42D8F6. Only a genuine host WM_COMMAND
GETCURSEL updates its observer role. Host selections always return native
connected-human state 3: opening/closing/CPU selections cannot destroy the
host's DPID or transport role. Slot zero is now serialized/deserialized with
the observer bit; host-only snapshot authority checks remain. Start admission
requires two combatants (human or CPU), not a human combatant specifically.
The observer still participates in native lockstep. Host migration/host exit
while continuing the match is not supported.

Inspection resolves selection word 611E12 only for an active local observer.
It validates ID 1..1699, live HP, unit/building kind, owner 0..7, non-observer
owner and connected player/CPU state. Four rendering-only reads at 41CFA3,
41CFD9, 41D00C, 41D077 use that owner for the existing resource/supply HUD.
No temporary write to local player ID or resource/ownership data is made.
The owner-check branch at 418A70 reveals native building detail only for an
eligible observer; ordinary players replay the original conditional branch.

The draw hook at 442D16 calls native resource rendering first and then draws
an owner-slot label and unit production queues using native text rendering.
Queue records are ten 8-byte entries at building+7A. Native production paths
411965 (land) and 41187E (naval) establish kinds 1 and 9, unit type WORD+7A,
quantity BYTE+7D, progress WORD+7E / WORD+80. Names use native unit table
461330 + 84 * type, as at 41A3E6; type is bounded 1..44. Zero total is safe,
progress is capped at 100%. This queue overlay is not an exhaustive research
queue implementation; native building detail remains responsible for its
existing construction/upgrade display. Formats are in reserved observer state
at ROLE+768/832/896, below the private packet copy at ROLE+1024.

Verified on both 12/36-selection patched builds: 128 owner/viewer/active
combinations each; resource HUD owner indices; unit names, quantities, last
queue and progress; zero denominator; unit versus building selection; empty
queue; invalid/dead/neutral targets; ordinary-player exclusion; detail branch
and flag preservation; unchanged player/unit/local-ID memory. Existing command
guards now explicitly test host and peer observers. Full native room snapshot
handling covers slot zero and slot two with observer/player transitions. All
512 vision combinations, 165 start compositions, outcome dispatcher including
host observer, lifecycle, rally/timer, rice-rally and 36-selection tests pass.
Runtime tests verify embedded hooks and original preservation; six updater
installation/failure/rollback scenarios and quoted-path checks pass.

Drawing/Win32 APIs are mocked in emulation. Actual visual layout, native text
rendering and multi-PC gameplay still require user verification. Suggested
test: host observer + remote player + CPU; select each side's units/buildings,
queue production, compare displayed resource values against the player, try
issuing commands, and confirm the observer does not lose after three seconds.
