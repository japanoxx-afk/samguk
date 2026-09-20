"""Emit exact-build observer patch manifest; no game files are modified.

Requires pefile + keystone-engine. Output is embedded by ObserverPatch.cs.
All addresses are tied to the SHA256-checked original and .fnfix at 630000.
Native connection state stays 0/3; bit 40 exists only in room wire snapshots.
"""
import hashlib
import struct
import sys
import uuid
import pefile
from keystone import Ks, KS_ARCH_X86, KS_MODE_32

original = open(sys.argv[1], 'rb').read()
assert hashlib.sha256(original).hexdigest() == '39a11e76f5328a66a4fe8dcb1318ece6362843d8192caa8c7e15f0fc08abdc62'
p = pefile.PE(data=original)
image = p.get_memory_mapped_image()
BASE = 0x630000
ROLE = BASE + 45056          # eight byte roles, 0=player, 1=observer
ACTIVE = ROLE + 8           # only active during a multiplayer scenario
IAT = ROLE + 32             # private pointer to UI wrapper, NOT a loaded IAT
LABEL = ROLE + 64
PLACEHOLDER = ROLE + 96
NAMES = ROLE + 128          # 8 * 64, display labels only
PACKET = ROLE + 1024        # room dispatch copy; never mutate queued wire bytes
ks = Ks(KS_ARCH_X86, KS_MODE_32)
cursor = BASE + 20480
rows = ['# Observer protocol 2, host observer + read-only inspection; exact build only']
addresses = {}

def block(name, source):
    global cursor
    address = cursor
    encoding = bytes(ks.asm(source, address)[0])
    assert cursor + len(encoding) <= BASE + 32768, name
    rows.append('# ' + name + ' ' + hex(address))
    rows.append('B %X %s' % (address-BASE, encoding.hex()))
    addresses[name] = address
    cursor = (cursor+len(encoding)+15) & ~15
    return address

def write(va, replacement):
    offset = p.get_offset_from_rva(va-0x400000)
    expected = original[offset:offset+len(replacement)]
    assert len(expected) == len(replacement)
    rows.append('H %X %s %s' % (offset, expected.hex(), replacement.hex()))

def hook(va, length, target):
    assert length >= 5
    write(va, b'\xe9'+struct.pack('<i', target-va-5)+b'\x90'*(length-5))

def callhook(va, target):
    assert image[va-0x400000] == 0xe8
    write(va, b'\xe8'+struct.pack('<i', target-va-5))

# Test helper used by guards: EAX=slot, carry set iff active observer.
# Leaves all registers intact; flags are intentionally the boolean result.
is_observer = block('is_observer', f'''
    cmp byte ptr [{ACTIVE}], 1
    jne no
    cmp eax, 8
    jae no
    cmp byte ptr [eax+{ROLE}], 1
    jne no
    stc
    ret
no: clc
    ret
''')

# Must run on every peer, not just on the observer's rendering process.
# Preserve active player -> active player sharing exactly as configured.
vision = block('vision', f'''
    pushfd
    pushad
    cmp byte ptr [{ACTIVE}], 1
    jne done
    xor ebx, ebx
    xor ecx, ecx
mask_loop:
    cmp byte ptr [ecx+{ROLE}], 1
    jne next_mask
    imul eax, ecx, 1124
    cmp byte ptr [eax+0x49b046], 3
    jne next_mask
    bts ebx, ecx
next_mask:
    inc ecx
    cmp ecx, 8
    jb mask_loop
    xor ecx, ecx
    mov edi, 0x49b048
players:
    cmp byte ptr [ecx+{ROLE}], 1
    je observer
    cmp byte ptr [edi-2], 2
    je share
    cmp byte ptr [edi-2], 3
    jne next_player
share:
    or word ptr [edi+2], bx
    jmp next_player
observer:
    mov eax, 1
    shl eax, cl
    mov word ptr [edi], ax
    mov word ptr [edi+2], ax
    mov dword ptr [edi+12], 0
    mov dword ptr [edi+16], 0
    mov dword ptr [edi+20], 0
    mov dword ptr [edi+24], 0
next_player:
    add edi, 1124
    inc ecx
    cmp ecx, 8
    jb players
done:
    popad
    popfd
    ret
''')

# Confine interception to references in the multiplayer room dialog/receiver.
# Native state 3 / connected name remains at index 3; index 4 is the new role.
ui = block('room_ui', f'''
    push ebp
    mov ebp, esp
    push ebx
    push esi
    push edi
    mov ebx, [ebp+12]
    sub ebx, 0x4b1
    cmp ebx, 8
    jae passthrough
    cmp dword ptr [ebp+16], 0x143
    je add_item
    cmp dword ptr [ebp+16], 0x144
    je delete_item
    cmp dword ptr [ebp+16], 0x147
    je get_selection
    cmp dword ptr [ebp+16], 0x14e
    je set_selection
passthrough:
    push dword ptr [ebp+24]
    push dword ptr [ebp+20]
    push dword ptr [ebp+16]
    push dword ptr [ebp+12]
    push dword ptr [ebp+8]
    call dword ptr [0x45f240]
    jmp done
add_item:
    push 0
    push 0
    push 0x146
    push dword ptr [ebp+12]
    push dword ptr [ebp+8]
    call dword ptr [0x45f240]
    cmp eax, 5
    jae replace_name
    push dword ptr [ebp+24]
    push 0
    push 0x143
    push dword ptr [ebp+12]
    push dword ptr [ebp+8]
    call dword ptr [0x45f240]
    cmp eax, 2
    jne done
    push {PLACEHOLDER}
    push 0
    push 0x143
    push dword ptr [ebp+12]
    push dword ptr [ebp+8]
    call dword ptr [0x45f240]
    push {LABEL}
    push 0
    push 0x143
    push dword ptr [ebp+12]
    push dword ptr [ebp+8]
    call dword ptr [0x45f240]
    mov eax, 2
    jmp done
replace_name:
    push 0
    push 3
    push 0x144
    push dword ptr [ebp+12]
    push dword ptr [ebp+8]
    call dword ptr [0x45f240]
    push dword ptr [ebp+24]
    push 3
    push 0x14a
    push dword ptr [ebp+12]
    push dword ptr [ebp+8]
    call dword ptr [0x45f240]
    jmp done
delete_item:
    cmp dword ptr [ebp+20], 3
    jne passthrough
    push 0
    push 3
    push 0x144
    push dword ptr [ebp+12]
    push dword ptr [ebp+8]
    call dword ptr [0x45f240]
    push {PLACEHOLDER}
    push 3
    push 0x14a
    push dword ptr [ebp+12]
    push dword ptr [ebp+8]
    call dword ptr [0x45f240]
    jmp done
get_selection:
    push 0
    push 0
    push 0x147
    push dword ptr [ebp+12]
    push dword ptr [ebp+8]
    call dword ptr [0x45f240]
    cmp dword ptr [ebp+4], 0x42da9a
    jne internal_get
    cmp dword ptr [0x613078], 0
    je internal_get
    cmp eax, 4
    sete byte ptr [ebx+{ROLE}]
    test ebx, ebx
    jne peer_selection
    mov eax, 3
    jmp done
peer_selection:
    cmp eax, 4
    je role_get
    cmp eax, 3
    jne done
    imul edx, ebx, 1124
    cmp dword ptr [edx+0x49b02c], 0
    jne done
    xor eax, eax
    jmp done
role_get:
    xor eax, eax
    imul edx, ebx, 1124
    cmp dword ptr [edx+0x49b02c], 0
    je done
    mov eax, 3
    jmp done
internal_get:
    cmp eax, 4
    jne done
    xor eax, eax
    jmp done
set_selection:
    cmp byte ptr [ebx+{ROLE}], 1
    jne passthrough
    mov eax, [ebp+20]
    test eax, eax
    je label_update
    cmp eax, 3
    jne passthrough
label_update:
    mov edi, ebx
    shl edi, 6
    add edi, {NAMES}
    mov esi, {LABEL}
copy_prefix:
    lodsb
    stosb
    test al, al
    jne copy_prefix
    imul eax, ebx, 1124
    cmp dword ptr [eax+0x49b02c], 0
    je label_ready
    mov byte ptr [edi-1], 0x20
    lea esi, [eax+0x49b030]
    mov ecx, 21
copy_name:
    lodsb
    stosb
    test al, al
    je label_ready
    loop copy_name
    mov byte ptr [edi], 0
label_ready:
    push 0
    push 4
    push 0x144
    push dword ptr [ebp+12]
    push dword ptr [ebp+8]
    call dword ptr [0x45f240]
    mov eax, ebx
    shl eax, 6
    add eax, {NAMES}
    push eax
    push 4
    push 0x14a
    push dword ptr [ebp+12]
    push dword ptr [ebp+8]
    call dword ptr [0x45f240]
    push 0
    push 4
    push 0x14e
    push dword ptr [ebp+12]
    push dword ptr [ebp+8]
    call dword ptr [0x45f240]
done:
    pop edi
    pop esi
    pop ebx
    pop ebp
    ret 20
''')
rows.append('B %X %s' % (IAT-BASE, struct.pack('<I', ui).hex()))
rows.append('B %X %s' % (LABEL-BASE, '관전자\0'.encode('cp949').hex()))
rows.append('B %X %s' % (PLACEHOLDER-BASE, '참가자\0'.encode('cp949').hex()))
for offset in range(0x2cc90, 0x2de60):
    if original[offset:offset+4] == struct.pack('<I', 0x45f240):
        assert original[offset-2] in (0x8b, 0xff)
        write(0x400000+offset, struct.pack('<I', IAT))

reset = block('reset_room', f'''
    pushfd
    pushad
    mov dword ptr [{ROLE}], 0
    mov dword ptr [{ROLE+4}], 0
    mov byte ptr [{ACTIVE}], 0
    popad
    popfd
    mov edx, dword ptr [0x477b08]
    jmp 0x42d5ee
''')
hook(0x42d5e8, 6, reset)
# Enable only the host's own slot dropdown. Native host authority stays intact;
# room_ui maps its selections to connected-human state 3, never open/CPU/closed.
write(0x42d8f6, bytes.fromhex('6a01'))

encode = block('encode_role', f'''
    mov byte ptr [ebx+5], dl
    pushfd
    pushad
    movzx ecx, byte ptr [ebx+4]
    cmp ecx, 8
    jae done
    cmp byte ptr [ecx+{ROLE}], 1
    jne done
    cmp dl, 0
    je mark
    cmp dl, 3
    jne done
mark: or byte ptr [ebx+5], 0x40
done:
    popad
    popfd
    repne scasb
    jmp 0x42d4fd
''')
hook(0x42d4f8, 5, encode)

# Snapshot and state change authority is the original host, player slot zero.
# Race-only messages do not change roles. Reject invalid slot/status before
# original code can index arrays. Keep host migration explicitly unsupported.
decode = block('decode_role', f'''
    mov eax, [ebp]
    and eax, 0xff00
    cmp eax, 0x3100
    je copy
    cmp eax, 0x3400
    je copy
    cmp eax, 0x3500
    jne dispatch
copy:
    pushad
    mov esi, ebp
    mov edi, {PACKET}
    mov ecx, 38
    cld
    rep movsb
    mov dword ptr [esp+8], {PACKET}
    popad
    cmp eax, 0x3500
    jne snapshot
    cmp byte ptr [ebp+0xd], 8
    jae drop
    and byte ptr [ebp+0xe], 0xbf
    jmp dispatch
snapshot:
    test dx, dx
    jne drop
    push ecx
    push edx
    movzx ecx, byte ptr [ebp+0xd]
    cmp ecx, 8
    jae invalid
    movzx edx, byte ptr [ebp+0xe]
    cmp edx, 0xff
    je unused
    and edx, 0xbf
    cmp edx, 3
    ja invalid
    cmp edx, 0
    je valid
    cmp edx, 3
    je valid
normal:
    and byte ptr [ebp+0xe], 0xbf
valid:
    mov dl, byte ptr [ebp+0xe]
    shr dl, 6
    and dl, 1
    mov byte ptr [ecx+{ROLE}], dl
    and byte ptr [ebp+0xe], 0xbf
    jmp accepted
unused:
    mov byte ptr [ecx+{ROLE}], 0
accepted:
    pop edx
    pop ecx
dispatch:
    jmp 0x42ccf4
invalid:
    pop edx
    pop ecx
drop:
    jmp 0x42d2e0
''')
hook(0x42ccec, 8, decode)

start_count = block('start_count', f'''
    push ebx
    mov ebx, 8
    sub ebx, ecx
    cmp byte ptr [ebx+{ROLE}], 1
    pop ebx
    je skip
    mov edi, [eax-0x1a]
    test edi, edi
    jmp 0x42dc2a
skip:
    jmp 0x42dc38
''')
hook(0x42dc25, 5, start_count)

# Native condition is humans>=2 OR computers>=3. After excluding observers,
# that rejects one human versus one computer with a connected spectator.
# Count actual combatants together; observer-only opposition is still invalid.
start_admission = block('start_admission', '''
    push eax
    movzx eax, dx
    movzx ecx, si
    add eax, ecx
    cmp eax, 2
    pop eax
    jl reject
    jmp 0x42dc50
reject:
    jmp 0x42de24
''')
hook(0x42dc40, 6, start_admission)

start = block('start_game', f'''
    pushfd
    pushad
    xor eax, eax
    cmp dword ptr [0x5173e4], 0
    setne al
    mov byte ptr [{ACTIVE}], al
    test eax, eax
    jne done
    mov dword ptr [{ROLE}], 0
    mov dword ptr [{ROLE+4}], 0
done:
    popad
    popfd
    sub esp, 0x10
    mov al, byte ptr [0x611e1a]
    jmp 0x442068
''')
hook(0x442060, 8, start)

map_load = block('map_load', f'''
    call {vision}
    call 0x415ed0
    call {vision}
    ret
''')
callhook(0x4420bb, map_load)

spawn = block('start_location', f'''
    pushfd
    pushad
    movzx eax, word ptr [esi+4]
    call {is_observer}
    jc skip
    popad
    popfd
    cmp byte ptr [eax+0x49b046], 2
    jmp 0x415084
skip:
    popad
    popfd
    jmp 0x415176
''')
hook(0x41507d, 7, spawn)

spawn_object = block('map_object', f'''
    pushfd
    pushad
    mov esi, [esp+40]
    movzx eax, word ptr [esi]
    cmp eax, 1
    je check
    cmp eax, 2
    jne normal
check:
    movzx eax, word ptr [esi+4]
    call {is_observer}
    jc skip
normal:
    popad
    popfd
    push ecx
    push ebx
    push ebp
    push esi
    mov esi, [esp+0x14]
    jmp 0x414c08
skip:
    popad
    popfd
    ret
''')
hook(0x414c00, 8, spawn_object)

# Keep transport/no-op/chat/exit control; deny all simulation/diplomacy orders.
def guard(name, slot, command, displaced, resume):
    return block(name, f'''
        pushfd
        pushad
        {slot}
        call {is_observer}
        jnc normal
        {command}
        and eax, 0xff00
        cmp eax, 0x100
        je normal
        cmp eax, 0x300
        je normal
        cmp eax, 0x8000
        jb deny
        cmp eax, 0x8400
        ja deny
normal:
        popad
        popfd
        {displaced}
        jmp {resume}
deny:
        popad
        popfd
        mov eax, 1
        ret
    ''')
send = guard('send_guard', 'movzx eax, byte ptr [0x59ee52]',
             'mov eax, [esp+40]', 'movsx edx, byte ptr [0x59ee52]', 0x438cf7)
hook(0x438cf0, 7, send)
receive = guard('receive_guard', 'movzx eax, byte ptr [esp+40]',
                'mov eax, [esp+44]; mov eax, [eax]',
                'push ebx; push ebp; push esi; mov esi, [esp+0x14]', 0x43a217)
hook(0x43a210, 7, receive)

diplomacy = block('diplomacy', f'''
    mov word ptr [eax+0x49b04a], dx
    call {vision}
    jmp 0x43b782
''')
hook(0x43b77b, 7, diplomacy)

# The global native defeat scan must still run for active players on every PC.
defeat_scan = block('defeat_scan', f'''
    push eax
    mov eax, esi
    call {is_observer}
    pop eax
    jc skip
    mov cl, [ebx]
    cmp cl, 2
    jmp 0x4488e4
skip:
    jmp 0x44893f
''')
hook(0x4488df, 5, defeat_scan)
local_defeat = block('local_defeat', f'''
    movzx eax, byte ptr [0x59ee52]
    call {is_observer}
    jnc normal
    pop edi
    pop esi
    pop ebp
    pop ebx
    xor eax, eax
    ret
normal:
    movsx ecx, byte ptr [0x59ee52]
    jmp 0x44895a
''')
hook(0x448953, 7, local_defeat)
local_victory = block('local_victory', f'''
    push eax
    movzx eax, byte ptr [0x59ee52]
    call {is_observer}
    pop eax
    jnc normal
    xor eax, eax
    ret
normal:
    push ebx
    mov bl, byte ptr [0x59ee52]
    jmp 0x4487f7
''')
hook(0x4487f0, 7, local_victory)

# The outcome dispatcher has a SECOND defeat check after both callbacks:
# zero local buildings and units -> loss dialog (447B25). Observers deliberately
# have neither. Guard only this fallback, after the global scan has still run.
empty_army = block('empty_army_guard', f'''
    push eax
    movzx eax, byte ptr [0x59ee52]
    call {is_observer}
    pop eax
    jnc normal
    ret
normal:
    movsx ecx, byte ptr [0x59ee52]
    jmp 0x447adc
''')
hook(0x447ad5, 7, empty_army)

# No unit ownership transfer, ally-mask mutation or defeat counting on observer
# departure. FD is only set when the native synchronized departure path calls us.
depart = block('departure', f'''
    pushfd
    pushad
    movzx eax, byte ptr [esp+40]
    call {is_observer}
    jnc normal
    imul eax, eax, 1124
    mov byte ptr [eax+0x49b046], 0xfd
    mov dword ptr [eax+0x49b02c], 0
    popad
    popfd
    ret
normal:
    popad
    popfd
    push ebx
    mov bx, word ptr [esp+8]
    jmp 0x441316
''')
hook(0x441310, 6, depart)

# Read-only inspection. Never impersonate the selected owner in simulation or
# command globals. Return owner (0..7), or -1 when no eligible selection exists.
inspect_owner = block('inspect_owner', f'''
    movzx eax, byte ptr [0x59ee52]
    call {is_observer}
    jnc none
    movzx eax, word ptr [0x611e12]
    test eax, eax
    je none
    cmp eax, 1699
    ja none
    imul eax, eax, 292
    cmp word ptr [eax+0x49e0c0], 0
    jle none
    cmp byte ptr [eax+0x49e0be], 1
    ja none
    movzx eax, byte ptr [eax+0x49e0bd]
    cmp eax, 8
    jae none
    cmp byte ptr [eax+{ROLE}], 1
    je none
    push edx
    imul edx, eax, 1124
    movzx edx, byte ptr [edx+0x49b046]
    cmp edx, 2
    je valid
    cmp edx, 3
    je valid
    pop edx
    jmp none
valid:
    pop edx
    ret
none:
    mov eax, -1
    ret
''')

# Only the four resource/supply display reads receive the inspected owner.
detail = block('building_detail', f'''
    pushfd
    pushad
    call {inspect_owner}
    cmp eax, 0
    jl native
    popad
    popfd
    jmp 0x418a76
native:
    popad
    popfd
    jne 0x418d65
    jmp 0x418a76
''')
# This is a rendering-only owner comparison. Command eligibility is untouched.
hook(0x418a70, 6, detail)
for va in (0x41cfa3, 0x41cfd9, 0x41d00c, 0x41d077):
    hud = block('resource_owner_'+hex(va), f'''
        pushfd
        push eax
        call {inspect_owner}
        cmp eax, 0
        jl native
        mov ecx, eax
        jmp done
    native:
        movsx ecx, byte ptr [0x59ee52]
    done:
        pop eax
        popfd
        jmp {va+7}
    ''')
    hook(va, 7, hud)

INFO = ROLE+768
QUEUE = ROLE+832
IDLE = ROLE+896
for va,text in ((INFO,'OBS P%d - resources / supply'),
                (QUEUE,'Q%d: %s x%d (%d%%)'),(IDLE,'Production: -')):
    rows.append('B %X %s' % (va-BASE, (text+'\0').encode('ascii').hex()))
inspect_draw = block('inspect_draw', f'''
    call 0x41cef0
    pushfd
    pushad
    call {inspect_owner}
    cmp eax, 0
    jl done
    mov edi, dword ptr [0x613bd4]
    test edi, edi
    je done
    inc eax
    push eax
    push {INFO}
    push 150
    push 20
    push 170
    push edi
    call 0x453d20
    add esp, 24
    movzx esi, word ptr [0x611e12]
    imul esi, esi, 292
    add esi, 0x49e0b8
    cmp byte ptr [esi+6], 1
    jne done
    xor ebx, ebx
    xor ebp, ebp
next_queue:
    movzx eax, byte ptr [esi+ebx*8+0x7c]
    cmp eax, 1
    je unit_queue
    cmp eax, 9
    jne next
unit_queue:
    movzx eax, byte ptr [esi+ebx*8+0x7d]
    test eax, eax
    je next
    movzx ecx, word ptr [esi+ebx*8+0x7a]
    test ecx, ecx
    je next
    cmp ecx, 44
    ja next
    push eax
    imul ecx, ecx, 84
    add ecx, 0x461330
    movzx eax, word ptr [esi+ebx*8+0x7e]
    imul eax, eax, 100
    movzx edx, word ptr [esi+ebx*8+0x80]
    test edx, edx
    je no_progress
    push ecx
    mov ecx, edx
    xor edx, edx
    div ecx
    pop ecx
    cmp eax, 100
    jbe progress
    mov eax, 100
    jmp progress
no_progress:
    xor eax, eax
progress:
    pop edx
    push eax
    push edx
    push ecx
    lea eax, [ebx+1]
    push eax
    push {QUEUE}
    push 150
    mov eax, ebp
    shl eax, 4
    add eax, 38
    push eax
    push 170
    push edi
    call 0x453d20
    add esp, 36
    inc ebp
next:
    inc ebx
    cmp ebx, 10
    jb next_queue
    test ebp, ebp
    jne done
    push {IDLE}
    push 150
    push 38
    push 170
    push edi
    call 0x453cc0
    add esp, 20
done:
    popad
    popfd
    ret
''')
callhook(0x442d16, inspect_draw)

# Application GUID returned by native 430B20. Different app GUID prevents old
# clients from opening the extended DirectPlay session, before role packets.
write(0x45f670, uuid.UUID('62046975-3128-4fd1-91b4-240b14bb2190').bytes_le)
print('\n'.join(rows))
