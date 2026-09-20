"""Run the complete native outcome dispatcher, not just individual callbacks.

Usage: script old-observer.exe new-observer12.exe new-observer36.exe
No real networking or user game process is started.
"""
import struct
import sys
import pefile
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UC_HOOK_CODE
from unicorn.x86_const import UC_X86_REG_ESP,UC_X86_REG_EIP,UC_X86_REG_EAX

STACK,STOP,DIALOG=0x1010000,0x101f000,0x4243e0
ROLE,ACTIVE=0x63b000,0x63b008

def exercise(path,patched):
    image=pefile.PE(path).get_memory_mapped_image()
    def scenario(viewer,observer,alive=(True,True),active=True,watcher=1):
        u=Uc(UC_ARCH_X86,UC_MODE_32)
        u.mem_map(0x400000,0x300000);u.mem_write(0x400000,image)
        u.mem_map(0x1000000,0x20000)
        def put(a,v):u.mem_write(a,struct.pack('<I',v))
        def get(a):return struct.unpack('<I',u.mem_read(a,4))[0]
        u.mem_write(0x49b028,bytes(1124*8))
        # B is room host slot 0; A observes a non-host slot.
        cpu=2 if watcher!=2 else 3
        for slot,state in [(0,3),(watcher,3),(cpu,2)]:
            u.mem_write(0x49b046+1124*slot,bytes([state]))
            u.mem_write(0x49b048+1124*slot,struct.pack('<H',1<<slot))
        for slot,present in zip((0,cpu),alive):
            if present:u.mem_write(0x49b0ca+1124*slot,b'\x01\x00')
        u.mem_write(ROLE+watcher,bytes([int(observer)]));u.mem_write(ACTIVE,bytes([int(active)]))
        u.mem_write(0x59ee52,bytes([viewer]));u.mem_write(0x611e1a,b'\x02')
        # Non-observer slot 1 is an ordinary participant with units unless it
        # is the viewer whose empty-army loss is being tested.
        if not observer and viewer!=watcher:u.mem_write(0x49b0ca+1124*watcher,b'\x01\x00')
        put(0x611e14,6);put(0x477fa8,0)
        removed=[]
        def hook(emu,a,size,_):
            if a==DIALOG:emu.emu_stop();return
            if a not in (0x45523e,0x443940,0x441310):return
            sp=emu.reg_read(UC_X86_REG_ESP)
            if a==0x441310:
                slot=get(sp+4);removed.append(slot)
                u.mem_write(0x49b046+1124*slot,b'\xfe')
            emu.reg_write(UC_X86_REG_EIP,get(sp));emu.reg_write(UC_X86_REG_ESP,sp+4)
            emu.reg_write(UC_X86_REG_EAX,0)
        u.hook_add(UC_HOOK_CODE,hook)
        def run(address):
            put(STACK,STOP);u.reg_write(UC_X86_REG_ESP,STACK)
            u.emu_start(address,STOP,count=100000)
            assert u.reg_read(UC_X86_REG_EIP) in (STOP,DIALOG),'dispatcher did not return'
        run(0x447b40) # native melee callback setup
        run(0x447aa0) # defeat callback -> victory callback -> empty-army fallback
        result=struct.unpack('<H',u.mem_read(0x477fa8,2))[0]
        if observer and active:assert watcher not in removed,'observer removed by global scan'
        return u,result,removed,run
    u,result,removed,run=scenario(1,True)
    if not patched:
        assert result==0x18,'old build must reproduce observer loss'
        print('PASS reproduction: old observer callbacks return no defeat, but dispatcher still opens loss dialog')
        return
    assert result==0
    for slot in range(1,8):assert scenario(slot,True,watcher=slot)[1]==0
    for _ in range(100):run(0x447aa0)
    assert u.mem_read(0x477fa8,2)==b'\0\0'
    assert u.mem_read(0x49b046+1124,1)==b'\x03'
    assert u.mem_read(0x49b048,2)==b'\x01\0' and u.mem_read(0x49b048+2248,2)==b'\x04\0'
    # Each active player still sees the actual competition, not forced allies.
    assert scenario(0,True)[1]==0
    assert scenario(2,True)[1]==0
    # Ordinary and inactive observer flags do not suppress genuine own defeat.
    assert scenario(1,False)[1]==0x18
    assert scenario(1,True,active=False)[1]==0x18
    assert scenario(0,True,alive=(False,True))[1]==0x18
    # Losing computer still goes through the global defeat scan and B wins.
    _,result,removed,_=scenario(0,True,alive=(True,False))
    assert 2 in removed and result==0x17
    print('PASS whole outcome dispatcher: observer stays, 100 checks; human/CPU loss and player victory retained',path)

exercise(sys.argv[1],False)
for p in sys.argv[2:]:exercise(p,True)
