"""Native component tests; OS/UI calls mocked, not live multiplayer QA."""
import sys,struct
args=sys.argv[1:];sys.argv=sys.argv[:1]
from quality_patch_test import Game,PACKET,SP
from unicorn.x86_const import *
class Controls(Game):
 def __init__(self,path):
  super().__init__(path);self.key=True;self.w32(0x45f234,0x109d000)
  self.w32(0x611e14,2);self.w16(0x477fa8,0);self.w8(0x476a18,0)
  self.w16(0x613108,30);self.w16(0x61310a,20)
  self.w16(0x5173d8,200);self.w16(0x49b276,200)
  self.w32(0x49b054,100000);self.w32(0x49b058,100000)
 def hook(self,u,a,size,data):
  if a in (0x109d000,0x445910,0x445e50,0x4437e0):
   sp=u.reg_read(UC_X86_REG_ESP);extra=0
   if a==0x109d000:
    assert self.r32(sp+4)==113;extra=4;u.reg_write(UC_X86_REG_EAX,0x8000 if self.key else 0)
   u.reg_write(UC_X86_REG_EIP,self.r32(sp));u.reg_write(UC_X86_REG_ESP,sp+4+extra);return
  super().hook(u,a,size,data)
 def unit_record(self,i,kind=0,owner=0,typ=2,x=25,y=35):
  a=0x49e0b8+292*i;self.w16(a+2,i);self.w8(a+4,typ);self.w8(a+5,owner);self.w8(a+6,kind);self.w16(a+8,100)
  self.w16(a+0x106,x);self.w16(a+0x108,y);self.w16(a+0x12,2)
  return a
for path in args:
 g=Controls(path);limit=g.r32(0x432ebb)
 assert g.r32(0x42ac84)==0x42ab41,'F2 still opens load dialog'
 for i in range(1,60):g.unit_record(i)
 g.w32(0x461364+2*84,2)
 for i in (1,2,3):g.unit_record(i,typ=(1,11,23)[i-1])
 g.unit_record(4,owner=1);g.unit_record(5,kind=1);g.w16(g.unit_record(6)+8,0)
 g.call(0x433330,stop=0x4331c0)
 assert list(struct.unpack('<'+'H'*limit,g.u.mem_read(g.local,2*limit)))==list(range(7,7+limit))
 assert g.r16(g.local+2*limit)==limit
 assert g.r32(PACKET)==0x1200 and g.r16(0x498724)==1
 g.call(0x433330,stop=0x4331c0);assert g.r16(0x498724)==1,'key repeat'
 for guard in ('chat','menu','observer'):
  q=Controls(path);q.unit_record(1);q.w32(0x461364+2*84,2)
  if guard=='chat':q.w8(0x476a18,1)
  elif guard=='menu':q.w16(0x477fa8,1)
  else:q.w8(0x63b008,1);q.w8(0x63b000,1)
  q.call(0x433330,stop=0x4331c0);assert q.r16(0x498724)==0
 g=Controls(path)
 for i in range(1,4):g.unit_record(i,kind=1,typ=1,x=24+i)
 g.unit_record(4,kind=1,typ=11);g.unit_record(5,kind=1,typ=1,owner=1);g.unit_record(6,kind=1,typ=1,x=100)
 g.call(0x443ba0,(0,1))
 assert [g.r16(g.local+2*i) for i in range(3)]==[1,2,3] and g.r16(g.local+2*limit)==3
 g.w16(0x4986de,1)
 for i in range(4):g.call(0x43e5a0,(0,2))
 wires=[bytes(g.u.mem_read(PACKET+128*i,22)) for i in range(4)]
 assert [struct.unpack_from('<H',w,9)[0] for w in wires]==[1,2,3,1]
 assert all(struct.unpack_from('<I',w)[0]==0xc00 for w in wires)
 assert g.r16(0x611e12)==1,'selection not restored'
 peer=Controls(path)
 for i in range(1,4):peer.unit_record(i,kind=1,typ=1)
 for wire in wires:
  peer.u.mem_write(PACKET,wire);peer.call(0x43a210,(0,PACKET))
 assert [peer.u.mem_read(0x49e0b8+292*i+0x7d,1)[0] for i in range(1,4)]==[2,1,1]
 assert peer.r32(0x49b054)==100000-4*peer.r16(0x461360+2*84)
 assert peer.r32(0x49b058)==100000-4*peer.r16(0x461362+2*84)
 g.w8(0x49e0b8+292*2+0x7d,9);g.w16(0x49e0b8+292*3+8,0)
 g.call(0x43e5a0,(0,2));assert g.r16(PACKET+128*4+9)==1,'full/dead producer not skipped'
 # Native snapshot UI must keep host control enabled, never grant guests authority.
 for authority in (0,1):
  q=Controls(path);q.w32(0x613078,authority);q.w8(PACKET+13,0)
  q.u.reg_write(UC_X86_REG_EBP,PACKET);q.u.reg_write(UC_X86_REG_EBX,123)
  q.call(0x42cf8b,stop=0x416300);sp=q.u.reg_read(UC_X86_REG_ESP)
  assert q.r32(sp+12)==authority
 # All peers reveal only connected observer planes, including host observers.
 q=Controls(path);q.w8(0x63b008,1);q.w8(0x63b000,1);q.w8(0x63b007,1)
 q.w8(0x49b046,3);q.w8(0x49b046+7*1124,3)
 q.u.reg_write(UC_X86_REG_EDI,0x5173ea);q.u.reg_write(UC_X86_REG_EAX,0x0f0f0f0f);q.u.reg_write(UC_X86_REG_ECX,0x1a480)
 q.u.reg_write(UC_X86_REG_ESP,SP);q.u.emu_start(0x442836,0x442841,count=300000)
 assert q.u.reg_read(UC_X86_REG_EIP)==0x442841
 for slot in range(8):assert bytes(q.u.mem_read(0x5173ea+slot*53824,53824))==bytes([0 if slot in (0,7) else 15])*53824
 print('PASS',path,'F2 filters/limit/guards, building double click, production 1-2-3-1 native peer queue, host controls, observer fog isolation')
