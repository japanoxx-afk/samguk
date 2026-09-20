"""Read-only exact-build observer feasibility checks. NOT an observer patch.

Execute original admission and lockstep membership branches in Unicorn to
demonstrate why adding a fourth slot value alone is not a valid implementation.
"""
import hashlib
import sys
import pefile
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn.x86_const import (
    UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX,
    UC_X86_REG_EDX, UC_X86_REG_ESI, UC_X86_REG_ESP,
)

p=pefile.PE(sys.argv[1])
assert hashlib.sha256(p.__data__).hexdigest()=='39a11e76f5328a66a4fe8dcb1318ece6362843d8192caa8c7e15f0fc08abdc62'
image=p.get_memory_mapped_image()

def emulator():
    u=Uc(UC_ARCH_X86,UC_MODE_32)
    u.mem_map(0x400000,0x400000);u.mem_write(0x400000,image)
    u.mem_map(0x1000000,0x10000);u.reg_write(UC_X86_REG_ESP,0x1008000)
    return u

def run_until(u,start,addresses):
    reached=[]
    def stop(emu,address,size,data):
        if address in addresses:reached.append(address);emu.emu_stop()
    hook=u.hook_add(UC_HOOK_CODE,stop)
    u.emu_start(start,0x700000,count=1000);u.hook_del(hook)
    assert len(reached)==1,'Unexpected control flow'
    return reached[0]

for status in [0,1,2,3,4]:
    u=emulator()
    for player in range(8):
        u.mem_write(0x49b046+1124*player,b'\x01')
        u.mem_write(0x49b02c+1124*player,b'\0'*4)
    u.mem_write(0x49b046+1124*3,bytes([status]))
    reached=run_until(u,0x42d0e2,{0x42d114,0x42d127})
    accepted=reached==0x42d127
    assert accepted==(status==0)
    if accepted:assert u.reg_read(UC_X86_REG_EDX)==3
    print('Native admission: state',status,'accepted' if accepted else 'not joinable')

for status in [0,1,2,3,4,0xfe,0xfd]:
    u=emulator();u.reg_write(UC_X86_REG_EAX,3)
    u.mem_write(0x49b046+1124*3,bytes([status]))
    reached=run_until(u,0x439dc8,{0x439de4,0x439e61})
    included=reached==0x439de4
    assert included==(status==3)
    print('Native lockstep: state',status,'included' if included else 'excluded')
print('PASS read-only audit: a new UI state cannot safely represent a connected observer without separate role synchronization and engine changes')

# Test the ORIGINAL visibility branches, not a Python replacement of them.
# Row = source unit owner; column bit = recipient. An observer recipient bit
# must be added to every participating source row, NOT vice versa.
import struct
assert image[0x5f680:0x5f690] == struct.pack('<8H', *[1 << i for i in range(8)])
visibility_cases = 0
for observer in range(8):
    u = emulator()
    for owner in range(8):
        u.mem_write(0x49b04a + 1124 * owner,
                    struct.pack('<H', (1 << owner) | (1 << observer)))
        # Deliberately keep diplomatic relation masks independent.
        u.mem_write(0x49b048 + 1124 * owner, struct.pack('<H', 1 << owner))
    for owner in range(8):
        for viewer in range(8):
            expected = viewer == owner or viewer == observer
            # Native visibility-update gate: BL owner, local player byte viewer.
            u.reg_write(UC_X86_REG_EBX, owner)
            u.mem_write(0x59ee52, bytes([viewer]))
            reached = run_until(u, 0x4057d3, {0x405808, 0x405a32})
            assert (reached == 0x405808) == expected, (observer, owner, viewer)
            # Native unit-render sharing gate for a hidden unit.
            u.reg_write(UC_X86_REG_EAX, 0)
            u.reg_write(UC_X86_REG_ECX, viewer)
            u.mem_write(0x49e0bd, bytes([owner]))
            reached = run_until(u, 0x442a13, {0x442a3b, 0x442aaf})
            assert (reached == 0x442a3b) == expected, (observer, owner, viewer)
            visibility_cases += 1
    for owner in range(8):
        assert bytes(u.mem_read(0x49b048 + 1124 * owner, 2)) == struct.pack('<H', 1 << owner)
print('PASS native visibility direction:', visibility_cases,
      'owner/viewer/observer combinations; no opponent sharing or alliance mutation')

# Native diplomacy command overwrites the whole sharing mask. Therefore simply
# setting it once on game start cannot guarantee persistent observer sight.
for player in range(8):
    u = emulator()
    stack = 0x1008000
    packet = 0x1009000
    u.reg_write(UC_X86_REG_ESI, packet)
    u.mem_write(stack + 0x14, bytes([player]))
    u.mem_write(packet + 9, struct.pack('<HH', 0x125, 1 << player))
    u.mem_write(0x49b04a + 1124 * player, b'\xff\x00')
    run_until(u, 0x43b752, {0x43b783})
    assert bytes(u.mem_read(0x49b048 + 1124 * player, 4)) == struct.pack('<HH', 0x125, 1 << player)
print('PASS native diplomacy update: full mask replacement confirmed for all 8 slots')
