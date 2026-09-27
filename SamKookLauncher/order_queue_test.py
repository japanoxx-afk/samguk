"""Execute native send/receive/order code and queue tick. Animation is not simulated."""
import sys,struct
from pathlib import Path
args=sys.argv[1:];sys.argv=sys.argv[:1]
from quality_patch_test import Game,PACKET,SP,DONE
from unicorn.x86_const import *
Q=0x642000
R={}
for line in Path(__file__).with_name('orders.manifest').read_text().splitlines():
 if line.startswith('# ') and ' 0x' in line:
  _,name,address=line.split();R[name]=int(address,16)
class Orders(Game):
 def __init__(self,path):
  super().__init__(path);self.shift=True
  self.w32(0x477ae4,0x1060000);self.w16(0x1060002,136);self.w16(0x1060004,136)
  self.w8(self.unit+6,0);self.w8(self.unit+4,2);self.w16(0x46134c+2*84,1)
  self.w16(self.unit+0x12,2);self.w8(self.unit+0xca,0x10)
  self.w32(0x45f234,0x109e000)
 def hook(self,u,a,size,data):
  if a==0x109e000:
   sp=u.reg_read(UC_X86_REG_ESP);assert self.r32(sp+4)==16
   u.reg_write(UC_X86_REG_EAX,0x8000 if self.shift else 0)
   u.reg_write(UC_X86_REG_EIP,self.r32(sp));u.reg_write(UC_X86_REG_ESP,sp+8)
  else:super().hook(u,a,size,data)
 def receive(self,x,y,queued=True,command=0x500):
  opcode=command
  marker=0x5153 if queued and command==0x500 else 2 if command==0x400 else 0
  payload=struct.pack('<6H',0,marker,x,y,x//2,y//2)
  self.u.mem_write(PACKET,struct.pack('<I',opcode)+b'\0'*5+payload)
  self.call(0x43a210,(0,PACKET))
 def tick(self):
  self.call(0x406300,(self.unit,),stop=0x406306)
 def target(self):return self.r16(self.unit+0x18),self.r16(self.unit+0x1a)
 def count(self):return self.r32(Q+64)
for path in args:
 g=Orders(path)
 g.receive(30,40);assert g.target()==(30,40) and g.count()==0,'idle first move'
 g.receive(50,60);g.receive(70,80);assert g.target()==(30,40) and g.count()==2
 g.tick();assert g.count()==2,'advanced during active move'
 g.w16(g.unit+0x12,2);g.w8(g.unit+0xca,0x10);g.tick()
 assert g.target()==(50,60) and g.count()==1,'FIFO first'
 g.w16(g.unit+0x12,2);g.w8(g.unit+0xca,0x10);g.tick()
 assert g.target()==(70,80) and g.count()==0,'FIFO second'
 for i in range(10):g.receive(40+i,50)
 assert g.count()==8,'bounded queue'
 g.receive(90,90,False);assert g.count()==0 and g.target()==(90,90),'normal click cancellation'
 g.receive(50,50);g.receive(0,0,False,0x400);assert g.count()==0 and g.r16(g.unit+0x12)==2,'stop cancellation'
 g.receive(30,30);g.receive(50,50);g.w16(g.unit+8,0);g.tick();assert g.count()==0,'death cleanup'
 # Keyboard state is captured only when sending, and not consulted by peer.
 sender=Orders(path);sender.u.mem_write(PACKET+64,struct.pack('<6H',0,0,30,40,15,20))
 sender.call(R['shift_send'],(0x500,PACKET+64))
 wire=bytes(sender.u.mem_read(PACKET,21));assert struct.unpack_from('<I',wire)[0]==0x500
 assert struct.unpack_from('<H',wire,11)[0]==0x5153 and sender.r16(PACKET+66)==0
 peer=Orders(path);peer.shift=False;peer.w8(0x59ee52,7);peer.u.mem_write(PACKET,wire);peer.call(0x43a210,(0,PACKET))
 assert peer.target()==(30,40)
 peer.receive(50,60);assert peer.count()==1
 # No shift preserves original wire opcode.
 sender=Orders(path);sender.shift=False;sender.u.mem_write(PACKET+64,struct.pack('<6H',0,0,30,40,15,20))
 sender.call(R['shift_send'],(0x500,PACKET+64));assert sender.r32(PACKET)==0x500 and sender.r16(PACKET+11)==0
 # The native opcode low byte MUST remain the source player, including odd slots.
 for slot in (0,1,7):
  for shifted in (False,True):
   sender=Orders(path);sender.shift=shifted;sender.w8(0x59ee52,slot)
   sender.w16(0x498722+1168*slot,127);sender.w16(0x498724+1168*slot,0)
   sender.w32(0x4989a8+1168*slot,PACKET)
   sender.u.mem_write(PACKET+64,struct.pack('<6H',0,0,30,40,15,20))
   sender.call(R['shift_send'],(0x500,PACKET+64))
   assert sender.r32(PACKET)==0x500+slot
   assert sender.r16(PACKET+11)==(0x5153 if shifted else 0)
   peer=Orders(path);peer.w8(peer.unit+5,slot)
   peer.w16(peer.players,0);peer.w16(peer.players+1124*slot,1)
   peer.w16(peer.unit+0x12,0x2003);peer.w16(peer.unit+0x18,90);peer.w16(peer.unit+0x1a,90)
   wire=bytes(sender.u.mem_read(PACKET,21));peer.u.mem_write(PACKET,wire)
   peer.call(0x43a210,(slot,PACKET))
   assert peer.count()==(1 if shifted else 0) and peer.target()==((90,90) if shifted else (30,40))
   assert bytes(peer.u.mem_read(PACKET,21))==wire,'receiver modified retained packet'
 # Native linked selections: queues remain independent for every selected unit.
 n=36 if '36' in path else 12
 g=Orders(path)
 for i in range(1,n+1):
  unit=0x49e0b8+292*i
  g.u.mem_write(unit,bytes(g.u.mem_read(g.unit,292)))
  g.w16(unit+2,i);g.w16(g.players+i*2,i+1 if i<n else 0)
 g.receive(30,40);g.receive(50,60);g.receive(70,80)
 for i in range(1,n+1):
  assert g.r32(Q+64*i)==2 and g.r32(Q+64*i+8)==(60<<16|50)
 g.unit=0x49e0b8+292*n;g.w16(g.unit+0x12,2);g.tick()
 assert g.target()==(50,60) and g.r32(Q+64*n)==1 and g.r32(Q+64)==2
 # Invalid map coordinates cannot enter the queue or cancel valid reservations.
 g=Orders(path);g.receive(30,40);g.receive(50,60)
 for x,y in [(3,30),(132,30),(30,132),(65535,30)]:g.receive(x,y)
 assert g.count()==1
 # Both native initialization paths clear all records and preserve flags.
 for entry,stop in [(0x415ed0,0x415ed8),(0x42b480,0x42b485)]:
  g=Orders(path);g.u.mem_write(Q,b'\x7f'*(1700*64));g.w32(0x641100,1)
  g.call(entry,stop=stop)
  assert bytes(g.u.mem_read(Q,1700*64))==bytes(1700*64) and g.r32(0x641100)==0
 # Recycled ID does not inherit the preceding unit's reservations.
 g=Orders(path);g.w32(Q+64*2,8);g.call(0x446e80,(2,0),stop=0x446e85)
 assert g.r32(Q+64*2)==0
 print('PASS',path,'native wire/peer, FIFO, overflow, cancellation, death, group',n,'bounds, new/load reset, reused ID')
