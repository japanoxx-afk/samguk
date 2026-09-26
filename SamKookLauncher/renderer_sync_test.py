"""Execute original viewport renderer with real incident unit records.

Missing image/animation resources must never clear simulation HP/occupancy.
The original reproduces that violation only when the unit is on-screen.
"""
import argparse, struct
from pathlib import Path
import pefile
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn.x86_const import *

BASE, SP, DONE = 0x49e0b8, 0x1010000, 0x109f000

def run(exe, record, visible, image, animation):
    u = Uc(UC_ARCH_X86, UC_MODE_32)
    u.mem_map(0x400000, 0x400000)
    u.mem_map(0x1000000, 0x100000)
    u.mem_write(0x400000, pefile.PE(exe).get_memory_mapped_image())
    def w(a, fmt, *v): u.mem_write(a, struct.pack('<'+fmt, *v))
    unit = BASE+292*struct.unpack_from('<H', record, 2)[0]
    u.mem_write(unit, record)
    # Keep native snapshot coordinates but position camera over/beyond this unit.
    px, py = struct.unpack_from('<hh', record, 14)
    w(0x59ee54, 'hh', (px-320)//32 if visible else (px+1000)//32, (py-160)//32)
    w(0x613104, 'hh', 800, 600)
    sprite = record[0]
    u.mem_write(0x47723a+12*sprite, bytes([32,32]))
    w(0x477240+12*sprite, 'I', 0x1088000 if image else 0)
    w(unit+0x48, 'I', 0x1089000 if animation else 0)
    if animation: u.mem_write(0x1089000, bytes(8))
    # No selection UI, overlays or transient effects. Normal blit is mocked.
    w(unit+0x1c, 'H', 0)
    u.mem_write(unit+0x60, bytes(24))
    w(0x611e0c, 'H', 0)
    u.mem_write(0x611e1c, bytes(1700))
    x,y=struct.unpack_from('<hh',record,0x106)
    occ=0x59ee5c+2*(y*232+x)
    w(occ,'H',struct.unpack_from('<H',record,2)[0])
    before=bytes(u.mem_read(unit,292)); before_occ=bytes(u.mem_read(occ,2))
    draws=[]
    def hook(uc,a,size,_):
        if a in (0x44b540,0x44b450,0x44b880):
            draws.append(a)
            sp=uc.reg_read(UC_X86_REG_ESP)
            ret=struct.unpack('<I',uc.mem_read(sp,4))[0]
            uc.reg_write(UC_X86_REG_ESP,sp+4);uc.reg_write(UC_X86_REG_EIP,ret)
    u.hook_add(UC_HOOK_CODE,hook)
    w(SP,'II',DONE,unit);u.reg_write(UC_X86_REG_ESP,SP)
    u.emu_start(0x41b3e0,DONE,count=100000)
    assert u.reg_read(UC_X86_REG_EIP)==DONE
    assert u.reg_read(UC_X86_REG_ESP)==SP+4
    return before,bytes(u.mem_read(unit,292)),before_occ,bytes(u.mem_read(occ,2)),draws

p=argparse.ArgumentParser()
p.add_argument('original');p.add_argument('snapshot');p.add_argument('patched',nargs='*')
p.add_argument('--unit',type=int,default=13)
args=p.parse_args()
snapshot=Path(args.snapshot).read_bytes()
record=snapshot[272+args.unit*292:272+(args.unit+1)*292]
assert record[4]==11 and struct.unpack_from('<H',record,8)[0]==55
for image,animation in ((False,True),(True,False)):
    before,after,occ,occ_after,_=run(args.original,record,True,image,animation)
    assert struct.unpack_from('<H',after,8)[0]==0 and occ_after==b'\0\0'
    assert after[0x48:0x4c]==b'\0'*4
    off=run(args.original,record,False,image,animation)
    assert off[0]==off[1] and off[2]==off[3]
print('PASS original reproduction: visible missing sprite erases Goguryeo farmer HP/occupancy; off-screen peer preserves it')
for exe in args.patched:
    for visible in (False,True):
        for image,animation in ((False,True),(True,False),(True,True)):
            before,after,occ,occ_after,draws=run(exe,record,visible,image,animation)
            assert before==after and occ==occ_after,(exe,visible,image,animation)
            if visible and image and animation:
                old=run(args.original,record,visible,image,animation)
                assert old[1]==after and old[4]==draws and draws
    print('PASS patched renderer: viewport-independent simulation, missing resources skipped, normal draw preserved:',exe)
