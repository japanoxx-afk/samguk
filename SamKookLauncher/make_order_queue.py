"""Generate deterministic ground-move queue hooks; exact original build."""
import sys,struct,hashlib
import pefile
from keystone import Ks,KS_ARCH_X86,KS_MODE_32
b=open(sys.argv[1],'rb').read()
assert hashlib.sha256(b).hexdigest()=='39a11e76f5328a66a4fe8dcb1318ece6362843d8192caa8c7e15f0fc08abdc62'
p=pefile.PE(data=b);ks=Ks(KS_ARCH_X86,KS_MODE_32)
cursor=0x640000;FLAG=0x641100;Q=0x642000
rows=['# Shift ground move queue: eight pending destinations per unit']
def block(name,asm):
 global cursor
 a=cursor;v=bytes(ks.asm(asm,a)[0]);assert a+len(v)<FLAG
 rows.extend(['# '+name+' '+hex(a),'B %X %s'%(a-0x630000,v.hex())]);cursor=(a+len(v)+15)&~15;return a
def hook(a,n,target):
 off=p.get_offset_from_rva(a-0x400000)
 v=b'\xe9'+struct.pack('<i',target-a-5)+b'\x90'*(n-5)
 rows.append('H %X %s %s'%(off,b[off:off+n].hex(),v.hex()))
def callhook(a,target):
 off=p.get_offset_from_rva(a-0x400000);assert b[off]==0xe8
 rows.append('H %X %s %s'%(off,b[off:off+5].hex(),(b'\xe8'+struct.pack('<i',target-a-5)).hex()))

# Hooks only terrain/minimap RMB send sites. Local keyboard state is serialized
# in the unused terrain payload word; opcode low byte belongs to the player.
# Keep the native 0500 serializer and never mutate the caller's payload.
send=block('shift_send', '''
 pushfd
 pushad
 mov edi, [esp+44]
 cmp word ptr [edi], 0
 jne done
 push 16
 call dword ptr [0x45f234]
 test ax, 0x8000
 jz done
 sub esp, 12
 mov eax, [edi]
 mov [esp], eax
 mov eax, [edi+4]
 mov [esp+4], eax
 mov eax, [edi+8]
 mov [esp+8], eax
 mov word ptr [esp+2], 0x5153
 mov eax, esp
 push eax
 push 0x500
 call 0x438cf0
 add esp, 20
 mov [esp+28], eax
 popad
 popfd
 ret
done:
 popad
 popfd
 jmp 0x438cf0
''')
callhook(0x434ac0,send);callhook(0x4349dc,send)

# Observer guard already ran before this receive call. FLAG exists only during
# this synchronous native selected-unit dispatch, never from local input state.
receive=block('receive_move',f'''
 push ebp
 mov ebp, esp
 push ebx
 mov ebx, [ebp+8]
 push dword ptr [ebx]
 xor eax, eax
 cmp word ptr [ebx+2], 0x5153
 jne unmarked
 inc eax
 mov word ptr [ebx+2], 0
unmarked:
 mov dword ptr [{FLAG}], eax
 push dword ptr [ebp+12]
 push dword ptr [ebp+8]
 call 0x438210
 add esp, 8
 mov dword ptr [{FLAG}], 0
 pop dword ptr [ebx]
 pop ebx
 pop ebp
 ret
''')
callhook(0x43a274,receive)

apply=block('unit_order',f'''
 pushfd
 pushad
 movzx ebx, word ptr [0x4868d6]
 cmp ebx, 1
 jb normal
 cmp ebx, 1700
 jae normal
 mov edi, ebx
 shl edi, 6
 add edi, {Q}
 imul esi, ebx, 292
 add esi, 0x49e0b8
 cmp dword ptr [{FLAG}], 1
 jne clear
 cmp word ptr [0x4868ca], 0x2003
 jne clear
 cmp byte ptr [esi+6], 0
 jne clear
 movzx eax, byte ptr [esi+4]
 imul eax, eax, 84
 cmp word ptr [eax+0x46134c], 1
 jne clear
 mov ecx, [esp+40]
 cmp word ptr [ecx], 0
 jne clear
 mov ebp, [0x477ae4]
 test ebp, ebp
 jz skip
 movzx eax, word ptr [ecx+4]
 cmp eax, 4
 jb skip
 movzx edx, word ptr [ebp+2]
 sub edx, 4
 cmp eax, edx
 jae skip
 movzx eax, word ptr [ecx+6]
 cmp eax, 4
 jb skip
 movzx edx, word ptr [ebp+4]
 sub edx, 4
 cmp eax, edx
 jae skip
 cmp word ptr [esi+0x12], 0x2003
 jne clear
 mov eax, [edi]
 cmp eax, 8
 jae skip
 mov edx, [ecx+4]
 mov [edi+eax*4+8], edx
 inc eax
 mov [edi], eax
skip:
 popad
 popfd
 ret
clear:
 mov dword ptr [edi], 0
normal:
 popad
 popfd
 movsx eax, word ptr [0x4868d6]
 jmp 0x437ff7
''')
hook(0x437ff0,7,apply)

# Per-unit simulation tick, not rendering. No wall clock, local player or RNG.
tick=block('advance_waypoint',f'''
 pushfd
 pushad
 mov esi, [esp+40]
 movzx ebx, word ptr [esi+2]
 cmp ebx, 1
 jb done
 cmp ebx, 1700
 jae done
 mov edi, ebx
 shl edi, 6
 add edi, {Q}
 cmp word ptr [esi+8], 0
 jle clear
 cmp byte ptr [esi+6], 0
 jne clear
 mov eax, [edi]
 test eax, eax
 jz done
 cmp eax, 8
 ja clear
 cmp word ptr [esi+0x12], 2
 jne done
 test byte ptr [esi+0xca], 0x20
 jnz done
 mov edx, [edi+8]
 dec eax
 mov [edi], eax
 xor ecx, ecx
shift:
 cmp ecx, eax
 jae ready
 mov ebp, [edi+ecx*4+12]
 mov [edi+ecx*4+8], ebp
 inc ecx
 jmp shift
ready:
 mov dword ptr [edi+eax*4+8], 0
 push dword ptr [0x4868c8]
 push dword ptr [0x4868cc]
 push dword ptr [0x4868d0]
 push dword ptr [0x4868d4]
 sub esp, 12
 mov dword ptr [esp], 0
 mov [esp+4], edx
 mov dword ptr [esp+8], 0
 mov dword ptr [0x4868c8], 0x20030000
 mov word ptr [0x4868d6], bx
 mov eax, esp
 push eax
 movsx eax, bx
 call native_apply
 add esp, 16
 pop dword ptr [0x4868d4]
 pop dword ptr [0x4868d0]
 pop dword ptr [0x4868cc]
 pop dword ptr [0x4868c8]
 jmp done
clear:
 mov dword ptr [edi], 0
done:
 popad
 popfd
 push ebx
 push esi
 mov esi, [esp+12]
 jmp 0x406306
native_apply:
 jmp 0x437ff7
''')
hook(0x406300,6,tick)

spawn=block('clear_reused_slot',f'''
 pushfd
 pushad
 movzx eax, word ptr [esp+40]
 cmp eax, 1700
 jae done
 shl eax, 6
 mov dword ptr [eax+{Q}], 0
done:
 popad
 popfd
 mov ax, word ptr [esp+8]
 jmp 0x446e85
''')
hook(0x446e80,5,spawn)

# Reset both before map initialization and after native saved-unit loading.
# Hooking the original entries also preserves observer wrappers around them.
reset=block('reset_orders',f'''
 pushfd
 pushad
 cld
 xor eax, eax
 mov edi, {Q}
 mov ecx, 27200
 rep stosd
 mov dword ptr [{FLAG}], 0
 popad
 popfd
 ret
''')
init=block('map_reset',f'''
 call {reset}
 sub esp, 8
 push 0x46292c
 jmp 0x415ed8
''')
hook(0x415ed0,8,init)
loaded=block('loaded_reset',f'''
 call {reset}
 sub esp, 8
 push ebx
 push ebp
 jmp 0x42b485
''')
hook(0x42b480,5,loaded)

# Room speed: native combo 0/1/2 was stored but never consumed by the
# multiplayer pacing loop. Snapshot payload byte 28 is outside the bounded
# 20-byte player name + NUL at [7..27]. Encode only in host snapshots.
cursor=0x640800
rate=block('room_speed_rate','''
 cmp eax, 2
 jbe valid
 mov eax, 1
valid:
 lea eax, [eax*2+6]
 lea eax, [eax+eax*4]
 mov word ptr [0x476904], ax
 mov word ptr [0x476908], ax
 ret
''')
speed_send=block('room_speed_send',f'''
 pushfd
 pushad
 cmp byte ptr [0x59ee52], 0
 jne done
 mov eax, [esp+40]
 cmp eax, 0x3100
 jne done
encode:
 movzx eax, byte ptr [0x476926]
 cmp eax, 2
 jbe valid
 mov eax, 1
valid:
 mov edi, [esp+44]
 mov dl, al
 add dl, 0xa0
 mov byte ptr [edi+28], dl
 call {rate}
done:
 popad
 popfd
 jmp 0x438cf0
''')
callhook(0x42d51b,speed_send)
speed_receive=block('room_speed_receive',f'''
 pushfd
 pushad
 test dx, dx
 jne done
 cmp eax, 0x3100
 jne done
decode:
 cmp byte ptr [ebp+8], 39
 jne done
 movzx eax, byte ptr [ebp+37]
 sub eax, 0xa0
 cmp eax, 2
 ja done
 call {rate}
done:
 popad
 popfd
 cmp eax, 0x3100
 jmp 0x42ccf9
''')
hook(0x42ccf4,5,speed_receive)
speed_start=block('room_speed_start',f'''
 pushfd
 pushad
 cmp byte ptr [0x59ee52], 0
 jne done
 movzx eax, byte ptr [0x476926]
 call {rate}
done:
 popad
 popfd
 mov ax, word ptr [0x476904]
 jmp 0x44319e
''')
hook(0x443198,6,speed_start)
# LAN create dialog never even read the speed combo. Internet dialog already
# writes 476926 at 429991. Capture LAN selection without disturbing game type.
lan=block('lan_room_speed','''
 pushfd
 pushad
 push 0
 push 0
 push 0x147
 push 0x423
 push esi
 call dword ptr [0x45f240]
 cmp eax, 2
 jbe valid
 mov eax, 1
valid:
 mov byte ptr [0x476926], al
 popad
 popfd
 mov byte ptr [0x476925], al
 jmp 0x42ee51
''')
hook(0x42ee4c,5,lan)
# Keep network polling/command buffering, but stop occupancy from overriding
# the chosen frame delay. Native queue consumption and epilogue are unchanged.
hook(0x43a1bd,6,0x43a1f9)
print('\n'.join(rows))
