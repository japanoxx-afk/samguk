"""Execute native placement and patched x86; no placement stubs.
Production allocator/initializer/audio are stubbed, not full multiplayer QA.
Usage: rally_exit_test.py sync-12.exe sync-36.exe
"""
import sys,struct,random
from pathlib import Path
from keystone import Ks,KS_ARCH_X86,KS_MODE_32
args=sys.argv[1:];sys.argv=sys.argv[:1]
from quality_patch_test import Game
from unicorn.x86_const import *

class Placement(Game):
    def __init__(self,path,rally=(30,10),anchor=(30,30),shape=(4,4),blocked=(),terrain=()):
        super().__init__(path)
        self.anchor=anchor;self.shape=shape;self.target=rally
        self.w32(0x477ae4,0x1060000);self.w16(0x1060002,136);self.w16(0x1060004,136)
        self.w32(0x47799c,0x1061000)
        self.u.mem_write(0x1061010,bytes([0,0,0,1,shape[0],shape[1]+1])+b'\0'*10)
        self.w8(self.unit+0xca,21);self.w16(self.unit+0x18,rally[0]);self.w16(self.unit+0x1a,rally[1])
        self.w16(self.unit+0x106,anchor[0]);self.w16(self.unit+0x108,anchor[1])
        self.u.mem_write(0x59ee5c,b'\0'*(232*232*2));self.u.mem_write(0x5da07c,b'\0'*(232*232))
        for y in range(anchor[1],anchor[1]+shape[1]):
            for x in range(anchor[0],anchor[0]+shape[0]):self.w16(0x59ee5c+2*(y*232+x),1)
        for x,y in blocked:self.w16(0x59ee5c+2*(y*232+x),42)
        for x,y in terrain:self.w8(0x5da07c+y*232+x,17)
    def place(self,entry=0x634c00):
        self.u.reg_write(UC_X86_REG_ESI,self.unit)
        self.call(entry,(self.anchor[0]+(self.anchor[1]<<16),1,4))
        return self.u.reg_read(UC_X86_REG_EAX),(self.r16(0x47821c),self.r16(0x47821e))
    def expected(self):
        x0,y0=self.anchor;w,h=self.shape;rx,ry=self.target
        candidates=[]
        for y in range(y0-1,y0+h+1):
            for x in range(x0-1,x0+w+1):
                if x not in (x0-1,x0+w) and y not in (y0-1,y0+h):continue
                if not(4<=x<132 and 4<=y<132):continue
                if self.r16(0x59ee5c+2*(y*232+x)) or self.u.mem_read(0x5da07c+y*232+x,1)[0]>16:continue
                candidates.append(((x-rx)**2+(y-ry)**2,y,x))
        if not candidates:return None
        _,y,x=min(candidates);return x,y

source='\n'.join(x.split(';')[0] for x in Path(__file__).with_name('rally_exit.asm').read_text().splitlines())
code=bytes(Ks(KS_ARCH_X86,KS_MODE_32).asm(source,0x634c00)[0])
for path in args:
    g=Placement(path);assert bytes(g.u.mem_read(0x634c00,len(code)))==code
    assert 0x4124de+struct.unpack('<i',g.u.mem_read(0x4124da,4))[0]==0x634c00
    for target in [(32,10),(32,80),(10,32),(80,32),(10,10),(80,80),(10,80),(80,10)]:
        g=Placement(path,target);assert g.place()==(1,g.expected()),target
        peer=Placement(path,target);peer.w8(0x59ee52,7);peer.w16(0x59ee54,90);peer.w16(0x59ee56,90)
        assert peer.place()==g.place(),'local camera/player affected placement'
    # Exact native fallback for disabled, invalid and inside-building rallies.
    for target,enabled in [((32,10),0),((32,32),21),((65535,10),21)]:
        a=Placement(path,target);b=Placement(path,target)
        a.w8(a.unit+0xca,enabled);b.w8(b.unit+0xca,enabled)
        assert a.place()==b.place(0x426a00)
    ring=[(x,y) for y in range(29,35) for x in range(29,35) if x in (29,34) or y in (29,34)]
    a=Placement(path,blocked=ring);b=Placement(path,blocked=ring)
    assert a.place()==b.place(0x426a00),'blocked ring fallback'
    a=Placement(path);b=Placement(path)
    for instance in (a,b):instance.u.mem_write(0x59ee5c,b'\x01\0'*(232*232))
    assert a.place()==b.place(0x426a00) and a.u.reg_read(UC_X86_REG_EAX)==0,'fully enclosed'
    a=Placement(path);assert a.place(0x426a00)[1][1]==34,'original south-first reproduction'
    rng=random.Random(127)
    for i in range(80):
        target=rng.choice([(32,10),(32,80),(10,32),(80,32)])
        blocked=rng.sample(ring,rng.randrange(len(ring)))
        g=Placement(path,target,blocked=blocked,terrain=rng.sample(ring,4))
        expected=g.expected()
        if expected is not None:assert g.place()==(1,expected)
    for anchor in [(4,4),(127,127),(4,125),(125,4)]:
        g=Placement(path,(70,70),anchor);assert g.place()==(1,g.expected()),anchor
    # Production integration executes native 4123B0, allocator/initializer/audio only stubbed.
    class Production(Placement):
        def hook(self,u,a,size,data):
            if a not in (0x446c00,0x446e80,0x445380):return super().hook(u,a,size,data)
            sp=u.reg_read(UC_X86_REG_ESP)
            if a==0x446c00:u.reg_write(UC_X86_REG_EAX,2)
            if a==0x446e80:
                self.spawn=(self.r16(sp+12),self.r16(sp+16))
                self.w8(0x49e0b8+292*2+4,2);self.w16(0x49e0b8+292*2+8,100)
            u.reg_write(UC_X86_REG_EIP,self.r32(sp));u.reg_write(UC_X86_REG_ESP,sp+4)
    for building in (1,11,23):
        g=Production(path);g.w8(g.unit+4,building);g.w16(g.unit+0x7a,2);g.w8(g.unit+0x7d,1)
        g.w16(0x46134c+2*84,1);g.w8(0x4631d4+building*60,1)
        g.call(0x4123b0,(g.unit,0));assert g.spawn==g.expected();assert g.u.reg_read(UC_X86_REG_EAX)==1
    print('PASS',path,'8 directions, blocked/terrain/bounds, native fallback, peers, production, assembled bytes')
