"""Native land-unit production + native worker command equivalence in Unicorn.
Allocation, placement and sound are stubbed. Actual movement/harvest routines and
the added hook execute as x86. This is not a physical two-PC gameplay test.
Arguments: patched rice-12.exe, baseline quality-12-rally-timer.exe (then 36 pair).
"""
import sys
import struct
saved_args=sys.argv[:]
sys.argv=sys.argv[:1]
from quality_patch_test import Game, REGS, PACKET
sys.argv=saved_args
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EIP, UC_X86_REG_ESP

class Production(Game):
    def __init__(self,path,kind=1,rice=3,x=30,y=40,rally=21):
        super().__init__(path)
        self.child=0x49e0b8+292*2
        self.kind=kind
        self.w8(self.unit+4,1);self.w8(self.unit+0xca,rally)
        self.w16(self.unit+0x18,x);self.w16(self.unit+0x1a,y)
        self.w16(self.unit+0x7a,kind);self.w8(self.unit+0x7d,1)
        if x<232 and y<232:self.w8(0x5da07c+y*232+x,rice)
        self.scratch=bytes(range(16));self.u.mem_write(0x4868c8,self.scratch)
    def hook(self,u,a,size,data):
        if a in (0x446c00,0x426a00,0x446e80,0x445380):
            sp=u.reg_read(UC_X86_REG_ESP)
            if a==0x446c00:u.reg_write(UC_X86_REG_EAX,2)
            elif a==0x426a00:
                self.w16(0x47821c,10);self.w16(0x47821e,20);u.reg_write(UC_X86_REG_EAX,1)
            elif a==0x446e80:
                assert self.r32(sp+4)==2 and self.r16(sp+8)==self.kind
                self.u.mem_write(self.child,b'\0'*292)
                self.w16(self.child+2,2);self.w8(self.child+4,self.kind);self.w16(self.child+8,100)
                self.w16(self.child+0x106,10);self.w16(self.child+0x108,20)
            u.reg_write(UC_X86_REG_EIP,self.r32(sp));u.reg_write(UC_X86_REG_ESP,sp+4)
        else:super().hook(u,a,size,data)
    def produce(self):
        self.call(0x4123b0,(self.unit,0))
        assert self.u.reg_read(UC_X86_REG_EAX)==1
    def child_bytes(self):return bytes(self.u.mem_read(self.child,292))

for patched,baseline in zip(sys.argv[1::2],sys.argv[2::2]):
    for worker in (1,11,23):
        a=Production(patched,worker);a.produce()
        b=Production(baseline,worker);b.produce()
        assert b.r16(b.child+0x12)==0x2003 # reproduce old movement-only result
        assert a.r16(a.child+0x12)==0x2009 # harvest order, not movement
        assert bytes(a.u.mem_read(0x4868c8,16))==a.scratch
        # Equivalent to issuing native context-sensitive right click to this worker.
        b.call(0x438770,(2,0,30+(40<<16)))
        assert b.r16(0x4868ca)==0x2009
        b.w16(0x4868c8,0);b.w16(0x4868d6,2)
        b.u.mem_write(PACKET,struct.pack('<6H',0,0,30,40,15,20))
        b.call(0x437ff0,(PACKET,))
        assert a.child_bytes()==b.child_bytes(),worker
        # Independent peer produces the exact same worker state without local UI selection.
        peer=Production(patched,worker);peer.w8(0x59ee52,3);peer.produce()
        assert peer.child_bytes()==a.child_bytes()
    for name,kwargs in [('normal ground',dict(rice=0)),('depleted resource',dict(rice=2)),
                        ('soldier',dict(kind=2)),('no rally',dict(rally=0)),
                        ('bad x',dict(x=65535)),('bad y',dict(y=232))]:
        a=Production(patched,**kwargs);b=Production(baseline,**kwargs)
        a.produce();b.produce();assert a.child_bytes()==b.child_bytes(),name
        assert bytes(a.u.mem_read(0x4868c8,16))==a.scratch,name
    print('PASS',patched,': native production of all 3 workers matches manual rice harvesting; peer determinism; ground/soldier/no-rally/bounds guards')
