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
