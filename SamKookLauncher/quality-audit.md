# v1.8.0 exact-build patch notes

Original SHA256: `39A11E76F5328A66A4FE8DCB1318ECE6362843D8192CAA8C7E15F0FC08ABDC62`.
QueuePatch verifies this before either optional patch. Apply order: queue/IP,
sender yield, latency, optional selection extension, GameQualityPatch.

## Rally

- Native command table entry 21 at VA 462B30: mode 0015, callback 410220,
  hotkey 52 (R). Enabled command panel slots are 1..15; generic command type 0.
- 410220 stores the native target mode and rebuilds command buttons.
- Normal right-click dispatch call at 4333D5 originally calls 434870. Replacement
  checks mode/dialog/chat, viewport hit region 1, own live building, and enabled
  rally command. Failed checks tail-call the original with registers/flags intact.
- Accepted clicks call native 410220(21) followed by native left-click handler
  434380. This uses the original 0400 command via 438CF0. No custom network message,
  synthetic Windows input, or local-only modification of building rally fields.
- Native receiver 43A210 -> 437EA0 sets building rally state at unit+CA and
  coordinates at unit+18/+1A. Unit records are 292 bytes. Automated tests compare
  the complete original/native-R command to new commands, then execute receiver
  code in a separate emulator. UI/sound/hit testing are stubbed; this is not a
  substitute for actual gameplay or physical A/B-PC testing.

## Timer

- Draw hook VA 442D1B replays `mov eax,[6124C0]` before returning to 442D20.
- HUD function 442CD0 uses native indexed-color text routine 453D20(buffer,x,y,
  color,format,...). Clock uses x=width-88, y=20, color=150; original mission
  countdown is at y=0. Null buffers/invalid widths skip drawing.
- Native simulation counter 5173D0 advances in 442660. Original mission timer
  divides it by 30 at 4426CB. Save at 42B3DC, load at 42BA6A/42BC44 preserve the
  counter. Clock only reads it; no wall-clock synchronization messages.
- Calls preserve all registers/flags and stack; the displaced instruction still
  supplies the original EAX. Tests cover 29/30-tick, minute/hour boundaries and
  640/800/1024/1280 widths. Rendering itself requires in-game visual verification.

## Allocation

Expand last .fnfix section from 512 to 65536 bytes when selection is off; otherwise
retain its 65536 bytes. Rally code +16384 (<=1024), timer +17408 (<=1024), ASCII
format +60000. Existing selection data +32768..45055 and code +512..2559 do not
overlap. Code/format zero checks and hook expected-byte checks reject collisions.
Both features disabled returns the input unchanged. Original EXE is preserved.
