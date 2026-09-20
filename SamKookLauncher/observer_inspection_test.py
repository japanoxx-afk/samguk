"""Host observer inspection tests on installed x86 hooks; mocked drawing only."""
import sys,struct
from pathlib import Path
import pefile
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UC_HOOK_CODE
from unicorn.x86_const import *

STACK,STOP=0x1010000,0x101f000
ROLE,ACTIVE=0x63b000,0x63b008
ROUTINES={}
for line in Path(__file__).with_name('observer.manifest').read_text().splitlines():
    if line.startswith('# ') and ' 0x' in line:
        _,n,a=line.split();ROUTINES[n]=int(a,16)

def exercise(path):
    image=pefile.PE(path).get_memory_mapped_image()
    for viewer in range(8):
      for owner in range(8):
        for active in (False,True):
          u=Uc(UC_ARCH_X86,UC_MODE_32)
          u.mem_map(0x400000,0x300000);u.mem_write(0x400000,image)
          u.mem_map(0x1000000,0x20000)
          def put(a,v):u.mem_write(a,struct.pack('<I',v))
          def get(a):return struct.unpack('<I',u.mem_read(a,4))[0]
          def text(a):
            result=bytearray()
            for i in range(200):
                c=u.mem_read(a+i,1)[0]
                if c==0:return bytes(result)
                result.append(c)
            raise AssertionError('unterminated string')
          u.mem_write(ROLE+viewer,b'\x01');u.mem_write(ACTIVE,bytes([active]))
          u.mem_write(0x59ee52,bytes([viewer]));u.mem_write(0x611e12,b'\x01\0')
          record=0x49e0b8+292
          u.mem_write(record+4,bytes([2,owner,1]))
          u.mem_write(record+8,b'\x64\0')
          u.mem_write(0x49b046+owner*1124,b'\x03')
          # Land worker and naval/second production queue; use native type names.
          for q,kind,unit,count,current,total in [(0,1,1,3,25,100),(9,9,11,1,500,100)]:
            u.mem_write(record+0x7a+q*8,struct.pack('<HBBHH',unit,kind,count,current,total))
          put(0x613bd4,0x1001000)
          draws=[]
          def hook(emu,a,size,_):
            if a not in (0x41cef0,0x453d20,0x453cc0):return
            sp=emu.reg_read(UC_X86_REG_ESP)
            if a==0x453d20:
                fmt=text(get(sp+20))
                count=4 if fmt.startswith(b'Q') else 1
                args=[get(sp+24+i*4) for i in range(count)]
                draws.append((fmt,args))
            elif a==0x453cc0:draws.append((text(get(sp+20)),[]))
            emu.reg_write(UC_X86_REG_EIP,get(sp));emu.reg_write(UC_X86_REG_ESP,sp+4)
          u.hook_add(UC_HOOK_CODE,hook)
          def run(a,end=STOP):
            put(STACK,STOP);u.reg_write(UC_X86_REG_ESP,STACK)
            u.emu_start(a,end,count=20000)
            assert u.reg_read(UC_X86_REG_EIP)==end
          eligible=active and owner!=viewer
          # Rendering-only detail gate: preserve native ownership behavior for
          # players, but reveal selected-owner detail for eligible observers.
          for zf in (False,True):
            u.reg_write(UC_X86_REG_EFLAGS,0x202 | (0x40 if zf else 0))
            end=0x418a76 if eligible or zf else 0x418d65
            run(0x418a70,end)
            assert bool(u.reg_read(UC_X86_REG_EFLAGS)&0x40)==zf
          run(ROUTINES['inspect_owner'])
          assert u.reg_read(UC_X86_REG_EAX)==(owner if eligible else 0xffffffff)
          for a in (0x41cfa3,0x41cfd9,0x41d00c,0x41d077):
            run(a,a+7)
            assert u.reg_read(UC_X86_REG_ECX)==(owner if eligible else viewer)
          players=bytes(u.mem_read(0x49b028,1124*8))
          units=bytes(u.mem_read(record,292))
          before_id=bytes(u.mem_read(0x59ee52,1))
          regs=(UC_X86_REG_EAX,UC_X86_REG_EBX,UC_X86_REG_ECX,UC_X86_REG_EDX,UC_X86_REG_ESI,UC_X86_REG_EDI,UC_X86_REG_EBP)
          for i,r in enumerate(regs):u.reg_write(r,0x1230+i)
          saved=[u.reg_read(r) for r in regs]
          run(ROUTINES['inspect_draw'])
          assert [u.reg_read(r) for r in regs]==saved
          assert u.reg_read(UC_X86_REG_ESP)==STACK+4
          assert bytes(u.mem_read(0x49b028,1124*8))==players
          assert bytes(u.mem_read(record,292))==units
          assert bytes(u.mem_read(0x59ee52,1))==before_id
          if eligible:
            assert len(draws)==3 and draws[0][1]==[owner+1]
            assert draws[1][1]==[1,0x461330+84,3,25]
            assert draws[2][1]==[10,0x461330+84*11,1,100]
            # Zero total never divides by zero. Invalid/dead/neutral selection
            # cannot leak an out-of-range owner into the native resource HUD.
            u.mem_write(record+0x80,b'\0\0');draws.clear()
            run(ROUTINES['inspect_draw']);assert draws[1][1][-1]==0
            u.mem_write(record+6,b'\0');draws.clear()
            run(ROUTINES['inspect_draw']);assert len(draws)==1
            u.mem_write(record+6,b'\1')
            u.mem_write(record+0x7a,bytes(80));draws.clear()
            run(ROUTINES['inspect_draw'])
            assert len(draws)==2 and draws[1][0]==b'Production: -'
            # An ordinary player receives no extra visibility or owner HUD.
            u.mem_write(ROLE+viewer,b'\0');draws.clear()
            run(ROUTINES['inspect_draw']);assert draws==[]
            for a in (0x41cfa3,0x41cfd9,0x41d00c,0x41d077):
                run(a,a+7);assert u.reg_read(UC_X86_REG_ECX)==viewer
            u.reg_write(UC_X86_REG_EFLAGS,0x202)
            run(0x418a70,0x418d65)
            u.mem_write(ROLE+viewer,b'\1')
            for invalid in (0,1700,65535):
                u.mem_write(0x611e12,struct.pack('<H',invalid))
                run(ROUTINES['inspect_owner']);assert u.reg_read(UC_X86_REG_EAX)==0xffffffff
            u.mem_write(0x611e12,b'\1\0')
            u.mem_write(record+5,b'\xff')
            run(ROUTINES['inspect_owner']);assert u.reg_read(UC_X86_REG_EAX)==0xffffffff
            u.mem_write(record+5,bytes([owner]));u.mem_write(record+8,b'\0\0')
            run(ROUTINES['inspect_owner']);assert u.reg_read(UC_X86_REG_EAX)==0xffffffff
          else:assert draws==[]
    assert image[0x2d8f6:0x2d8f8]==b'\x6a\x01'
    print('PASS 128 host/peer inspection cases: resources, named production/progress, no game-state writes; bounds/zero-total guards',path)

for p in sys.argv[1:]:exercise(p)
