"""Execute real x86 input -> native packet -> native receiver, not UI automation.
Usage: py quality_patch_test.py quality-12-rally-timer.exe quality-36-rally-timer.exe
"""
import struct
import sys
import pefile
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn.x86_const import *

DONE=0x109f000
PACKET=0x1080000
SP=0x1010000
REGS=[UC_X86_REG_EAX,UC_X86_REG_EBX,UC_X86_REG_ECX,UC_X86_REG_EDX,
      UC_X86_REG_ESI,UC_X86_REG_EDI,UC_X86_REG_EBP]

class Game:
    def __init__(self,path):
        p=pefile.PE(path)
        self.u=Uc(UC_ARCH_X86,UC_MODE_32)
        self.u.mem_map(0x400000,0x400000)
        self.u.mem_write(0x400000,p.get_memory_mapped_image())
        self.u.mem_map(0x1000000,0x100000)
        self.u.hook_add(UC_HOOK_CODE,self.hook)
        self.draws=[];self.hit=0
        self.rally=0x4333da+struct.unpack('<i',self.u.mem_read(0x4333d6,4))[0]
        # Locate addresses from original instructions, also after 36-unit relocation.
        self.local=self.r32(0x432ec1)
        self.players=self.r32(0x437edb)
        self.w16(0x611e12,1);self.w8(0x47836b,1);self.w32(0x498698,1)
        self.w16(0x49869e+22,21) # native rally occupies slot 11
        self.w16(0x4986be+22,0);self.w16(0x4986de+22,0)
        self.w8(0x59ee52,0)
        self.unit=0x49e0b8+292
        self.w16(self.unit+2,1);self.w16(self.unit+8,100)
        self.w8(self.unit+5,0);self.w8(self.unit+6,1)
        self.w16(self.local,1);self.w16(self.players,1)
        self.w16(0x478360,480);self.w16(0x478362,320)
        self.w16(0x59ee54,20);self.w16(0x59ee56,30)
        self.w16(0x498722,127);self.w16(0x498724,0)
        for slot in range(128):self.w32(0x4989a8+slot*4,PACKET+slot*128)
        self.u.mem_write(0x4986be,b'\0'*32)
        self.w16(0x613104,640);self.w32(0x613bd4,0x1050000)
    def w8(self,a,v):self.u.mem_write(a,bytes([v]))
    def w16(self,a,v):self.u.mem_write(a,struct.pack('<H',v))
    def w32(self,a,v):self.u.mem_write(a,struct.pack('<I',v))
    def r16(self,a):return struct.unpack('<H',self.u.mem_read(a,2))[0]
    def r32(self,a):return struct.unpack('<I',self.u.mem_read(a,4))[0]
    def hook(self,u,a,size,_):
        if a not in (0x43e100,0x43e170,0x4347c0,0x433b50,0x453d20):return
        sp=u.reg_read(UC_X86_REG_ESP)
        if a==0x453d20:
            args=tuple(self.r32(sp+4+i*4) for i in range(8))
            assert bytes(u.mem_read(args[4],15))==b'%02u:%02u:%02u\0'
            self.draws.append(args)
            for r in REGS:u.reg_write(r,0x12345678)
            u.reg_write(UC_X86_REG_EFLAGS,0x202)
        if a==0x433b50:u.reg_write(UC_X86_REG_EAX,self.hit)
        u.reg_write(UC_X86_REG_EIP,self.r32(sp));u.reg_write(UC_X86_REG_ESP,sp+4)
    def call(self,entry,args=(),stop=DONE):
        self.u.mem_write(SP,struct.pack('<'+'I'*(1+len(args)),DONE,*args))
        self.u.reg_write(UC_X86_REG_ESP,SP)
        self.u.emu_start(entry,stop,count=100000)
        assert self.u.reg_read(UC_X86_REG_EIP)==stop,hex(self.u.reg_read(UC_X86_REG_EIP))
        if stop==DONE:assert self.u.reg_read(UC_X86_REG_ESP)==SP+4

for path in sys.argv[1:]:
    # Native R -> left click vs new right-click entry: identical 21-byte command.
    old=Game(path);old.call(0x410220,(21,));old.call(0x434380)
    g=Game(path);g.call(g.rally)
    packet=bytes(g.u.mem_read(PACKET,21))
    assert packet==bytes(old.u.mem_read(PACKET,21))
    assert g.r32(PACKET)==0x400
    assert g.r16(0x4783ba)==21 and g.r16(0x611e10)==0
    assert g.r16(0x498724)==1 # exactly one queued command
    assert struct.unpack_from('<HH',packet,13)==(30,40)
    # Feed bytes to a separate peer's real command decoder and building handler.
    peer=Game(path);peer.u.mem_write(PACKET,packet)
    peer.call(0x43a210,(0,PACKET))
    assert bytes(peer.u.mem_read(peer.unit+0xca,1))==b'\x15'
    assert peer.r16(peer.unit+0x14)==0 # ground, not a unit target
    assert (peer.r16(peer.unit+0x18),peer.r16(peer.unit+0x1a))==(30,40)
    # Guards must preserve the old right-click handler and all incoming state.
    cases=[('unit',lambda x:x.w8(x.unit+6,0)),('enemy',lambda x:x.w8(x.unit+5,1)),
           ('dead',lambda x:x.w16(x.unit+8,0)),('none',lambda x:x.w16(0x611e12,0)),
           ('bad id',lambda x:x.w16(0x611e12,0xffff)),('minimap',lambda x:x.w8(0x47836b,2)),
           ('button',lambda x:x.w8(0x47836b,4)),('chat',lambda x:x.w8(0x476a18,1)),
           ('menu',lambda x:x.w16(0x477fa8,1)),('target cancel',lambda x:x.w16(0x611e10,21)),
           ('disabled',lambda x:x.w16(0x4986be+22,1)),('wrong button type',lambda x:x.w16(0x4986de+22,1)),
           ('no rally',lambda x:x.w16(0x49869e+22,20)),('panel hidden',lambda x:x.w32(0x498698,0))]
    for name,change in cases:
        g=Game(path);change(g)
        for i,r in enumerate(REGS):g.u.reg_write(r,0x1000+i)
        g.u.reg_write(UC_X86_REG_EFLAGS,0x247)
        g.call(g.rally,stop=0x434870)
        assert [g.u.reg_read(r) for r in REGS]==[0x1000+i for i in range(7)],name
        assert g.u.reg_read(UC_X86_REG_EFLAGS)==0x247,name
        assert g.u.reg_read(UC_X86_REG_ESP)==SP and g.r16(0x498724)==0,name
    # Disabled first occurrence doesn't hide a later enabled native button.
    g=Game(path);g.w16(0x49869e+2,21);g.w16(0x4986be+2,1);g.call(g.rally)
    assert g.r16(0x498724)==1
    # Time boundaries, resolutions, register/stack preservation and displaced code.
    for ticks,clock in [(0,(0,0,0)),(29,(0,0,0)),(30,(0,0,1)),(1799,(0,0,59)),
                        (1800,(0,1,0)),(108000,(1,0,0)),(30*3723,(1,2,3))]:
        for width in (640,800,1024,1280):
            g=Game(path);g.w32(0x5173d0,ticks);g.w16(0x613104,width);g.w32(0x6124c0,0x1234)
            for i,r in enumerate(REGS):g.u.reg_write(r,0x1000+i)
            g.u.reg_write(UC_X86_REG_EFLAGS,0x247)
            g.call(0x442d1b,stop=0x442d20)
            assert len(g.draws)==1 and g.draws[0]==(0x1050000,width-88,20,150,g.draws[0][4],*clock)
            assert g.u.reg_read(UC_X86_REG_EAX)==0x1234
            assert [g.u.reg_read(r) for r in REGS[1:]]==[0x1000+i for i in range(1,7)]
            assert g.u.reg_read(UC_X86_REG_EFLAGS)==0x247 and g.u.reg_read(UC_X86_REG_ESP)==SP
            assert g.r32(0x5173d0)==ticks
    for address,value in [(0x613bd4,0),(0x613104,0)]:
        g=Game(path);g.w32(address,value);g.call(0x442d1b,stop=0x442d20);assert not g.draws
    print('PASS',path,': native rally packet equivalence / peer application / 14 input guards / timer boundaries and registers')
