# A lobby server joining B's room: 2026-09-21 investigation

Status: entry-crash cause not yet established; independently reproduced
native connection-lifetime defects corrected for v1.10.2.

Two local Windows Error Reporting minidumps, game PIDs 60172 and 56224,
correspond to the reported failures at 00:14:27 and 00:14:54. These dumps
remain local; they are not included in the repository or release.

Confirmed:

- Both are access violations, not a normal room rejection.
- Both main-thread stacks contain game return address `0x449B4B`, immediately
  after the DirectPlay `InitializeConnection` vtable call at `0x449B45`.
- The calling game path returns to `0x428655`, the internet room-join setup.
- The join-target DWORD at `0x478248` is `1A F0 99 70`, i.e. B's
  `26.240.153.112`, in both dumps. It is not A's lobby-server address.
- PID 56224 faults in ntdll while a DirectPlay NAT helper/IP Helper operation
  is active; PID 60172 faults in an RPC worker. The differing fault sites do
  not establish which earlier operation caused the invalid memory state.
- `RoomList` selects the room owner's session address; it does not substitute
  the lobby server's address for a remote, non-loopback room owner.
- An unsupported E1-22 log entry alone does not establish the crash cause.

The minidumps do not contain the relevant heap allocations. Do not infer
that NAT, the observer role, alliance flags, or an IP reversal is the root
cause solely from these observations. The observer-disconnect report is a
separate case unless further evidence connects it.

Next isolation: restart both clients with observer mode disabled and repeat
A joining B. Both clients must use the same setting because the observer
protocol intentionally has a separate application GUID. This needs an actual
two-PC test; no interactive or remote-PC result has been simulated or claimed.

Follow-up: the user reports successful entry and play with observer mode off.
The local log confirms PID 61216 launched with observer mode false, while
selection 36, rice rally, right-click rally and low latency remained enabled.
Room entry progressed past E1-22 and the game later returned to the lobby.
However, after disconnect at 00:24:08 it exited with C0000005 at 00:24:11.
The corresponding WER dump faults at ntdll+0x5B320 on thread 60836 (write AV).
Consequently observer-off is a verified workaround for this entry attempt,
not proof of a complete fix or proof that every heap fault is observer-only.

`inspect_dump.py` now scans up to 64 KiB of the exception stack instead of
2 KiB, and prints x86 registers. The shorter scan missed the native game
caller beneath DirectPlay. Raw stack/account strings are not printed.

## Implemented lifetime correction

Native cleanup `0x449C7A` loads lobby pointer `0x613084` and calls Release,
but never clears that pointer. A second cleanup calls Release on the stale
object. Native `0x449C9B` also calls CoUninitialize unconditionally, even when
the game's sole CoInitialize call (`0x4499FB`) did not succeed or never ran.
Both defects reproduce by executing the original native routine with mocked
COM APIs; they are not guesses based solely on ntdll exception addresses.

`NetworkLifecyclePatch` reserves .fnfix offsets C000-C203, applied last after
observer/selection/quality patches. Hooks verify original instruction bytes.
It takes and clears the lobby pointer before Release and balances only the
game's successful COM initializations. Failure HRESULTs do not increment the
counter; S_FALSE does. The game's COM init/cleanup paths run on its UI thread.
No Windows DLL, registry, application GUID, packet format or original game
file is modified.

`network_lifecycle_test.py` reproduces original double-release/unowned COM
teardown, then checks failure/S_OK/S_FALSE, repeated cleanup and 100 logical
rejoins on the installed x86 patch for baseline and observer 12/36 variants.
This is a component test, not a two-PC network test. Actual A-joins-B and
observer-disconnect results remain unverified; no claim of complete crash
resolution is made.
