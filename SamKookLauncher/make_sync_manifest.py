"""Exact-build fail-stop guard and deterministic barrier fingerprint exchange."""
import sys,struct,hashlib
import pefile
from keystone import Ks,KS_ARCH_X86,KS_MODE_32
b=open(sys.argv[1],'rb').read()
assert hashlib.sha256(b).hexdigest()=='39a11e76f5328a66a4fe8dcb1318ece6362843d8192caa8c7e15f0fc08abdc62'
p=pefile.PE(data=b);ks=Ks(KS_ARCH_X86,KS_MODE_32)
BASE=0x630000;RECORD=0x63d000;FLAG=0x63d100;HASH=0x63d108;VALID=0x63d118;PATH=0x63d800
cursor=0x63c400;rows=['# Sync safety 4: sectional deterministic barrier fingerprints; not automatic resync']
def block(name,source):
 global cursor
 a=cursor;code=bytes(ks.asm(source,a)[0]);assert a+len(code)<=RECORD
 rows.extend(['# '+name+' '+hex(a),'B %X %s'%(a-BASE,code.hex())])
 cursor=(a+len(code)+15)&~15;return a
def hook(a,n,t,opcode=0xe9):
 off=p.get_offset_from_rva(a-0x400000)
 code=bytes([opcode])+struct.pack('<i',t-a-5)+b'\x90'*(n-5)
 rows.append('H %X %s %s'%(off,b[off:off+n].hex(),code.hex()))
for a,s in ((0x63d180,b'kernel32.dll\0'),(0x63d1a0,b'CreateFileW\0')):
 rows.append('B %X %s'%(a-BASE,s.hex()))

# EAX=reason, EDX=peer. Preserve registers/flags; no player state/ownership writes.
# One fixed binary record per stopped match, appended using Unicode file paths.
fail=block('stop_and_record',f'''
 pushfd
 pushad
 cmp dword ptr [{FLAG}], 0
 jne done
 mov dword ptr [{FLAG}], 1
 mov dword ptr [{RECORD}], 0x314e5953
 mov dword ptr [{RECORD+4}], 3
 mov dword ptr [{RECORD+8}], eax
 mov dword ptr [{RECORD+20}], edx
 mov eax, [0x5173d0]
 mov [{RECORD+12}], eax
 movzx eax, byte ptr [0x59ee52]
 mov [{RECORD+16}], eax
 mov eax, [0x6124e0]
 mov [{RECORD+24}], eax
 mov eax, [0x6124e8]
 mov [{RECORD+28}], eax
 movzx eax, word ptr [0x4868f0]
 mov [{RECORD+32}], eax
 movzx eax, word ptr [0x4868e8]
 mov [{RECORD+36}], eax
 mov eax, [0x611e14]
 mov [{RECORD+44}], eax
 xor ebx, ebx
 mov edi, {RECORD+64}
capture:
 imul esi, ebx, 1124
 movzx eax, byte ptr [esi+0x49b046]
 stosd
 imul esi, ebx, 1168
 movzx eax, byte ptr [esi+0x498720]
 stosd
 mov eax, [esi+0x49871c]
 stosd
 movzx eax, word ptr [esi+0x498722]
 stosd
 movzx eax, word ptr [esi+0x498724]
 stosd
 imul esi, ebx, 1124
 movsx eax, byte ptr [esi+0x49b04e]
 stosd
 inc ebx
 cmp ebx, 8
 jb capture
 mov dword ptr [0x611e14], 0x43
 mov word ptr [0x4868f0], 0
 mov word ptr [0x4868e8], 0
 push 0x63d180
 call dword ptr [0x45f124]
 test eax, eax
 je done
 push 0x63d1a0
 push eax
 call dword ptr [0x45f098]
 test eax, eax
 je done
 push 0
 push 0x80
 push 4
 push 0
 push 1
 push 4
 push {PATH}
 call eax
 cmp eax, -1
 je done
 mov ebx, eax
 push 0
 push {FLAG+4}
 push 256
 push {RECORD}
 push ebx
 call dword ptr [0x45f0ec]
 push ebx
 call dword ptr [0x45f19c]
 push ebx
 call dword ptr [0x45f0dc]
done:
 popad
 popfd
 ret
''')

# Four fingerprints isolate RNG, resources, entity core fields and production.
# Only fields whose simulation meaning is known are included; v1.12.0's broad
# entity words could include client-local data and are intentionally removed.
state_hash=block('state_hash',f'''
 push eax
 push ebx
 push ecx
 push edx
 push esi
 push edi
 push ebp
 mov eax, 0x811c9dc5
 xor eax, dword ptr [0x6124e0]
 rol eax, 5
 xor eax, dword ptr [0x6124e8]
 mov dword ptr [{HASH}], eax
 mov edi, 0x811c9dc5
 xor ecx, ecx
players:
 imul esi, ecx, 1124
 movzx eax, byte ptr [esi+0x49b046]
 xor edi, eax
 rol edi, 5
 xor edi, dword ptr [esi+0x49b054]
 rol edi, 5
 xor edi, dword ptr [esi+0x49b058]
 rol edi, 5
 xor edi, dword ptr [esi+0x49b05c]
 rol edi, 5
 xor edi, dword ptr [esi+0x49b060]
 rol edi, 5
 inc ecx
 cmp ecx, 8
 jb players
 mov dword ptr [{HASH+4}], edi
 mov ebp, 0x811c9dc5
 mov ebx, 0x811c9dc5
 mov ecx, 1699
 mov esi, 0x49e1dc
units:
 movzx eax, word ptr [esi+2]
 xor ebp, eax
 rol ebp, 5
 movzx eax, word ptr [esi+4]
 xor ebp, eax
 rol ebp, 5
 movzx eax, byte ptr [esi+6]
 xor ebp, eax
 rol ebp, 5
 movzx eax, word ptr [esi+8]
 xor ebp, eax
 rol ebp, 5
 movzx eax, word ptr [esi+12]
 xor ebp, eax
 rol ebp, 5
 cmp byte ptr [esi+6], 1
 jne next_unit
 push ecx
 mov edx, 10
 lea edi, [esi+0x7a]
queue:
 xor ebx, dword ptr [edi]
 rol ebx, 5
 xor ebx, dword ptr [edi+4]
 rol ebx, 5
 add edi, 8
 dec edx
 jne queue
 pop ecx
next_unit:
 add esi, 292
 dec ecx
 jne units
 mov dword ptr [{HASH+8}], ebp
 mov dword ptr [{HASH+12}], ebx
 pop ebp
 pop edi
 pop esi
 pop edx
 pop ecx
 pop ebx
 pop eax
 ret
''')

# Grow the normal barrier from 10 to 26 bytes with four sectional hashes.
barrier_hash=block('barrier_hash',f'''
 pop ebx
 xor cl, 10
 mov byte ptr [edi+8], 26
 xor cl, 26
 push ecx
 push edx
 call {state_hash}
 pop edx
 pop ecx
 push esi
 push edx
 mov esi, {HASH}
 xor eax, eax
copy_hash:
 mov dl, byte ptr [esi+eax]
 mov byte ptr [edi+eax+9], dl
 xor cl, dl
 inc eax
 cmp eax, 16
 jb copy_hash
 pop edx
 pop esi
 mov byte ptr [edi+25], cl
 jmp 0x438be5
''')
hook(0x438bdc,9,barrier_hash)

# Once every connected human has supplied a barrier, find that barrier in
# each command ring and compare fingerprints before executing the batch.
compare_hash=block('compare_hash',f'''
 pushfd
 pushad
 mov dword ptr [{VALID}], 0
 xor ecx, ecx
slot:
 imul eax, ecx, 1124
 cmp byte ptr [eax+0x49b046], 3
 jne next_slot
 imul eax, ecx, 1168
 movzx edx, word ptr [eax+0x498722]
 movzx ebp, word ptr [eax+0x498724]
scan:
 cmp dx, bp
 je bad_packet
 movzx ebx, dx
 mov esi, dword ptr [eax+ebx*4+0x4989a8]
 mov ebx, dword ptr [esi]
 and ebx, 0xff00
 cmp ebx, 0x8000
 je found
 inc dx
 and edx, 0x7f
 jmp scan
found:
 cmp byte ptr [esi+8], 26
 jne bad_packet
 cmp dword ptr [{VALID}], 0
 jne compare
 mov eax, dword ptr [esi+9]
 mov dword ptr [{HASH}], eax
 mov eax, dword ptr [esi+13]
 mov dword ptr [{HASH+4}], eax
 mov eax, dword ptr [esi+17]
 mov dword ptr [{HASH+8}], eax
 mov eax, dword ptr [esi+21]
 mov dword ptr [{HASH+12}], eax
 mov dword ptr [{RECORD+52}], ecx
 mov dword ptr [{VALID}], 1
 jmp next_slot
compare:
 xor edi, edi
compare_section:
 mov ebx, dword ptr [esi+edi*4+9]
 cmp ebx, dword ptr [{HASH}+edi*4]
 jne mismatch
 inc edi
 cmp edi, 4
 jb compare_section
next_slot:
 inc ecx
 cmp ecx, 8
 jb slot
 popad
 popfd
 ret
mismatch:
 mov eax, dword ptr [{HASH}+edi*4]
 mov dword ptr [{RECORD+40}], eax
 mov dword ptr [{RECORD+48}], ebx
 mov dword ptr [{RECORD+56}], ecx
 mov dword ptr [{RECORD+60}], edi
 mov edx, ecx
 mov eax, 5
 call {fail}
 popad
 popfd
 ret
bad_packet:
 mov edx, ecx
 mov eax, 6
 call {fail}
 popad
 popfd
 ret
''')

barrier_ready=block('barrier_ready',f'''
 call {compare_hash}
 cmp dword ptr [{FLAG}], 0
 jne stopped
 cmp word ptr [0x477fa8], di
 jmp 0x43a052
stopped:
 pop edi
 pop esi
 pop ebp
 pop ebx
 xor eax, eax
 ret
''')
hook(0x43a04b,7,barrier_ready)

timeout=block('timeout_drop',f'''
 cmp dword ptr [0x5173e4], 0
 je native
 pushfd
 pushad
 xor ecx, ecx
 movzx edx, word ptr [0x4868e8]
scan:
 imul eax, ecx, 1124
 cmp byte ptr [eax+0x49b046], 3
 jne next
 bt edx, ecx
 jnc missing
next:
 inc ecx
 cmp ecx, 8
 jb scan
 popad
 popfd
 jmp native
missing:
 mov eax, 1
 mov edx, -1
 call {fail}
 popad
 popfd
 ret
native:
 sub esp, 12
 push ebx
 push ebp
 jmp 0x439775
''')
hook(0x439770,5,timeout)

check=block('check_entry',f'''
 cmp dword ptr [{FLAG}], 0
 jne done
 xor dx, dx
 push ebp
 push esi
 jmp 0x443715
done: ret
''')
hook(0x443710,5,check)
mismatch=block('mismatch_drop',f'''
 mov eax, 2
 movzx edx, word ptr [0x6124dc]
 call {fail}
 pop esi
 pop ebp
 ret
''')
hook(0x443765,6,mismatch)

# The original 8500 receive path evicts peers before lockstep consumption.
# Only intercept a nonempty eviction of a connected human, not empty notices.
remote=block('remote_drop',f'''
 pushfd
 pushad
 cmp dword ptr [0x5173e4], 0
 je native
 xor ecx, ecx
 mov esi, [0x49d7ac]
scan:
 cmp byte ptr [esi+ecx+9], 0
 je next
 imul eax, ecx, 1124
 cmp byte ptr [eax+0x49b046], 3
 jne next
 mov edx, ecx
 mov eax, 3
 call {fail}
 popad
 popfd
 pop edi
 pop esi
 pop ebx
 mov eax, 1
 ret
next:
 inc ecx
 cmp ecx, 8
 jb scan
native:
 popad
 popfd
 mov word ptr [0x4868fc], 0
 jmp 0x439b87
''')
hook(0x439b7e,9,remote)

# Ensure an outstanding wait unwinds rather than remaining inside the old
# ready-mask loop after the fail-stop. Normal waits still execute original code.
wait=block('wait_exit',f'''
 cmp dword ptr [{FLAG}], 0
 je native
 pop edi
 pop esi
 pop ebp
 pop ebx
 xor eax, eax
 ret
native:
 call 0x4399f0
 jmp 0x439ea9
''')
hook(0x439ea4,5,wait)
reset=block('new_match',f'''
 mov dword ptr [{FLAG}], 0
 call 0x442060
 ret
''')
hook(0x443115,5,reset,0xe8);hook(0x443340,5,reset,0xe8)

# 0D00 is building reconstruction/upgrade. The original accepts a live soldier
# as its target and writes building-only 1081 into its unit command field.
upgrade=block('upgrade_target', '''
 pushfd
 pushad
 cmp byte ptr [esi+8], 16
 jb reject
 movzx eax, word ptr [esi+9]
 test eax, eax
 je reject
 cmp eax, 1699
 ja reject
 imul eax, eax, 292
 cmp byte ptr [eax+0x49e0be], 1
 jne reject
 mov edx, [esp+56]
 cmp edx, 8
 jae reject
 cmp byte ptr [eax+0x49e0bd], dl
 jne reject
 cmp word ptr [esi+11], 10
 jae reject
 movzx edx, word ptr [esi+13]
 test edx, edx
 je reject
 cmp edx, 61
 ja reject
 popad
 popfd
 movsx eax, word ptr [esi+9]
 lea ecx, [eax+eax*8]
 jmp 0x43a4ee
reject:
 popad
 popfd
 jmp 0x43b82f
''')
hook(0x43a4e7,7,upgrade)

ai_upgrade=block('ai_upgrade_target','''
 mov eax, [esp+4]
 cmp byte ptr [eax+6], 1
 jne reject
 mov eax, [esp+12]
 push ebx
 jmp 0x41f635
reject:
 xor eax, eax
 ret
''')
hook(0x41f630,5,ai_upgrade)

# Verified function table is 64 entries, followed by text, not more functions.
# Do not turn a corrupt command into an idle unit: stop and preserve evidence.
dispatch=block('unit_dispatch',f'''
 cmp eax, 64
 jae invalid
 lea eax, [eax+eax*2]
 mov eax, [eax*8+0x462940]
 jmp 0x406340
invalid:
 pushfd
 pushad
 cmp dword ptr [{FLAG}], 0
 jne stopped
 movzx eax, word ptr [esi+2]
 mov [{RECORD+48}], eax
 movzx eax, word ptr [esi+18]
 mov [{RECORD+52}], eax
 movzx eax, byte ptr [esi+6]
 mov [{RECORD+56}], eax
 movzx eax, byte ptr [esi+4]
 mov [{RECORD+60}], eax
 mov eax, 4
 mov edx, -1
 call {fail}
stopped:
 popad
 popfd
 pop esi
 pop ebx
 ret
''')
hook(0x406336,10,dispatch)
print('\n'.join(rows))
