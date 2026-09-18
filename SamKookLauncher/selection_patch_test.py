"""Execute patched x86 selection paths in Unicorn, without starting the game.

These are deterministic component tests, NOT an actual two-PC gameplay test.
"""
import argparse
import struct
import pefile
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn.x86_const import *

p=argparse.ArgumentParser();p.add_argument('exe');args=p.parse_args()
pe=pefile.PE(args.exe)
section=next(s for s in pe.sections if s.Name.rstrip(b'\0')==b'.fnfix')
patch=pe.OPTIONAL_HEADER.ImageBase+section.VirtualAddress
data=patch+32768
players=data+256
local=players+8*1124
done=0x109f000
packet=0x1080000

class Game:
    def __init__(self):
        self.u=Uc(UC_ARCH_X86,UC_MODE_32)
        self.u.mem_map(0x400000,0x400000)
        self.u.mem_write(0x400000,pe.get_memory_mapped_image())
        self.u.mem_map(0x1000000,0x100000)
        self.u.hook_add(UC_HOOK_CODE,self.hook)
        self.stubs={0x445910,0x445e50,0x43e170}
        self.fread_calls=[]
        self.fwrite_calls=[]
        for unit in range(1,101):
            base=0x49e0b8+unit*292
            self.w16(base+2,unit)
            self.u.mem_write(base+4,b'\x01\x00')
            self.w16(base+8,100)
            self.w16(base+0x12,0x2000)
    def r16(self,a):return struct.unpack('<H',self.u.mem_read(a,2))[0]
    def r32(self,a):return struct.unpack('<I',self.u.mem_read(a,4))[0]
    def w16(self,a,v):self.u.mem_write(a,struct.pack('<H',v))
    def w32(self,a,v):self.u.mem_write(a,struct.pack('<I',v))
    def ids(self,a):return list(struct.unpack('<36H',self.u.mem_read(a,72)))
    def select(self,a,ids):
        assert len(ids)<=36
        self.u.mem_write(a,struct.pack('<37H',*(ids+[0]*(36-len(ids))),sum(x!=0 for x in ids)))
    def hook(self,u,addr,size,_):
        if addr in self.stubs or addr in (0x454caa,0x455412):
            sp=u.reg_read(UC_X86_REG_ESP)
            if addr in (0x454caa,0x455412):
                call=tuple(self.r32(sp+i) for i in (4,8,12,16))
                (self.fread_calls if addr==0x454caa else self.fwrite_calls).append(call)
                u.reg_write(UC_X86_REG_EAX,9)
            u.reg_write(UC_X86_REG_EIP,self.r32(sp));u.reg_write(UC_X86_REG_ESP,sp+4)
    def call(self,entry,args=(),stop=done):
        sp=0x1010000
        self.u.mem_write(sp,struct.pack('<'+'I'*(1+len(args)),done,*args))
        self.u.reg_write(UC_X86_REG_ESP,sp)
        self.u.emu_start(entry,stop,count=300000)
        assert self.u.reg_read(UC_X86_REG_EIP)==stop,hex(self.u.reg_read(UC_X86_REG_EIP))
        if stop==done:assert self.u.reg_read(UC_X86_REG_ESP)==sp+4
    def command(self,player,cmd,payload=b''):
        self.u.mem_write(packet,struct.pack('<II',cmd,0)+bytes([10+len(payload)])+payload+b'\0')
        self.call(0x43a210,(player,packet))
    def queue(self,player):
        self.u.mem_write(0x59ee52,bytes([player]))
        self.w16(0x498722+player*1168,127)
        self.w16(0x498724+player*1168,0)
        for slot in range(128):self.w32(0x4989a8+player*1168+slot*4,packet+slot*128)

g=Game();g.select(data,list(range(1,37)));g.w16(data+74,36)
g.call(0x443f80)
assert g.ids(local)==list(range(1,37)) and g.r16(local+72)==36
assert all(g.u.mem_read(0x611e1c+x,1)==b'\x01' for x in range(1,37))
assert g.u.mem_read(data+82,174)==bytes(174)
print('PASS apply 36-unit local selection and selection flags')

# Execute the real drag append block through its stopping branch. The 36th
# addition must exit selection gathering before a 37th write can happen.
for count in (0,11,12,35):
    t=Game();t.w16(data+74,count);t.u.reg_write(UC_X86_REG_EBX,count+1)
    t.call(0x444542,stop=0x4447b8 if count==35 else 0x444589)
    assert t.r16(data+74)==count+1 and t.r16(data+count*2)==count+1
    assert t.r16(data+72)==0
t=Game();t.select(local,list(range(1,37)));t.select(data,list(range(25,61)));t.w16(data+74,36)
for unit in range(1,37):t.u.mem_write(0x611e1c+unit,b'\x01')
t.u.reg_write(UC_X86_REG_EDI,0)
t.call(0x4447c6,stop=0x44491f)
assert t.ids(data)==list(range(1,25))+list(range(37,49))
assert t.r16(data+74)==36
print('PASS drag stops at 36; Shift selection merges/toggles without exceeding the limit')

g.queue(0);g.call(0x438cf0,(0x1200,0))
wire=bytes(g.u.mem_read(packet,84))
assert wire[8]==84 and struct.unpack('<37H',wire[9:83])==tuple(range(1,37))+(36,)
assert sum(1 for x in wire)==84
checksum=0
for x in wire:checksum^=x
assert checksum==0
assert bytes(g.u.mem_read(packet+96,32))==bytes(32),'96-byte queue buffer overflow'
peer=Game();peer.u.mem_write(packet,wire);peer.call(0x43a210,(0,packet))
assert peer.ids(players)==list(range(1,37)) and peer.r16(players+72)==36
print('PASS real sender -> 84-byte packet/checksum -> independent receiver, all 36 IDs')

for player in range(8):
    g=Game()
    for unit in range(1,37):g.u.mem_write(0x49e0bd+unit*292,bytes([player]))
    base=players+player*1124
    g.select(base,list(range(1,37)))
    for group in range(14):
        g.command(player,0x1b00,struct.pack('<H',group))
        assert g.ids(base+(group+1)*74)==list(range(1,37)),(player,group)
        assert g.r16(base+(group+1)*74+72)==36
        g.command(player,0x1400)
        assert g.ids(base)==[0]*36 and g.r16(base+72)==0
        g.command(player,0x1c00,struct.pack('<H',group))
        assert g.ids(base)==list(range(1,37)) and g.r16(base+72)==36
    assert g.u.mem_read(base+1110,14)==bytes(14),'Player slot overrun'
print('PASS 8 players x 14 control groups: save / deselect / recall, 36 units')

g.queue(player);g.w16(0x5173dc,0xffff)
g.call(0x446700,(13,))
assert g.ids(local)==list(range(1,37)) and g.r16(local+72)==36
print('PASS keyboard control-group recall updates local selection with 36 units')

g=Game();g.select(players,list(range(1,37)));g.select(local,list(range(1,37)))
for group in range(14):g.select(players+(group+1)*74,list(range(1,37)))
for unit in range(1,37):g.u.mem_write(0x611e1c+unit,b'\x01')
g.u.mem_write(packet,struct.pack('<HH',0,0x2003))
g.call(0x437b20,(packet,0))
assert all(g.r16(0x49e0ca+unit*292)==0x2003 for unit in range(1,37))
assert g.r16(0x49e0ca+37*292)==0x2000
g.call(0x446a70,(36,))
assert g.r16(players+72)==35 and g.r16(local+72)==35
for group in range(14):
    assert g.r16(players+(group+1)*74+72)==35
    assert 36 not in g.ids(players+(group+1)*74)
assert g.u.mem_read(0x611e1c+36,1)==b'\0'
print('PASS command affects 36 units but not unit 37; death removes unit 36 from all groups')

g=Game()
for player in range(9):
    for group in range(15):g.select(players+player*1124+group*74,list(range(1,37)))
    g.w16(0x49b300+player*1124,0xabba);g.w32(0x49b488+player*1124,0xdeadbeef)
g.call(patch+2048,(0x49b028,0x464,9,0x1234))
assert g.fwrite_calls==[(0x49b028,0x464,9,0x1234)]
for player in range(9):
    for group in range(15):
        old=0x49b302+player*1124+group*26
        assert list(struct.unpack('<13H',g.u.mem_read(old,26)))==list(range(1,13))+[12]
g.u.mem_write(data,b'\xaa'*12288)
g.call(patch+2304,(0x49b028,0x464,9,0x1234))
assert g.fread_calls==[(0x49b028,0x464,9,0x1234)]
assert g.u.reg_read(UC_X86_REG_EAX)==9
for player in range(9):
    for group in range(15):
        new=players+player*1124+group*74
        assert g.ids(new)==list(range(1,13))+[0]*24 and g.r16(new+72)==12
    assert g.r16(0x49b300+player*1124)==0xabba
    assert g.r32(0x49b488+player*1124)==0xdeadbeef
g.call(0x447b40,stop=0x447b47)
assert g.u.mem_read(data,12288)==bytes(12288)
print('PASS legacy save/load bridge preserves file layout and surrounding player data; new-map reset')
print('PASS selection expansion component tests (real two-PC gameplay not tested)')
