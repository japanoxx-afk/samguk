"""Native room send/decode and pacing tests. Win32/time/render calls mocked."""
import sys,struct
from pathlib import Path
args=sys.argv[1:];sys.argv=sys.argv[:1]
from quality_patch_test import Game,PACKET,SP,DONE
from unicorn.x86_const import *
R={}
for line in Path(__file__).with_name('orders.manifest').read_text().splitlines():
 if line.startswith('# ') and ' 0x' in line:
  _,name,address=line.split();R[name]=int(address,16)
class Speed(Game):
 def __init__(self,path):
  super().__init__(path);self.combo=1;self.clock=0
  self.w32(0x45f240,0x109d000)
 def hook(self,u,a,size,data):
  if a in (0x109d000,0x109d010,0x442c00,0x4399f0):
   sp=u.reg_read(UC_X86_REG_ESP);extra=0;result=0
   if a==0x109d000:
    assert self.r32(sp+8)==0x423 and self.r32(sp+12)==0x147
    extra=20;result=self.combo
   if a==0x109d010:self.clock+=1;result=self.clock
   u.reg_write(UC_X86_REG_EAX,result);u.reg_write(UC_X86_REG_ESP,sp+4+extra);u.reg_write(UC_X86_REG_EIP,self.r32(sp));return
  super().hook(u,a,size,data)
 def decode(self,wire,sender=0):
  self.u.mem_write(PACKET,wire);self.u.reg_write(UC_X86_REG_EBP,PACKET)
  self.u.reg_write(UC_X86_REG_EDX,sender)
  self.u.reg_write(UC_X86_REG_EAX,struct.unpack_from('<I',wire)[0]&0xff00)
  self.call(0x42ccec,stop=0x42ccf9)
 def delays(self):return self.r16(0x476904),self.r16(0x476908)
 def pace(self,entry):
  self.clock=0;self.w32(0x59e148,0);self.u.reg_write(UC_X86_REG_EBP,0x109d010)
  self.call(entry,stop=0x443222);return self.clock

original=args[0]
old=Speed(original);old.w16(0x476904,25);old.w8(0x476926,1)
old.u.reg_write(UC_X86_REG_ESI,0);old.call(0x443198,stop=0x4431ab)
assert old.delays()==(25,25),'original normal selection was consumed unexpectedly'
old.w16(0x476908,40);old.u.reg_write(UC_X86_REG_EDX,5);old.u.reg_write(UC_X86_REG_EBP,1)
old.call(0x43a1bd,stop=0x43a1d9);assert old.r16(0x476908)==39
print('REPRODUCED original: normal selection ignored; 25ms initial delay; queue load changes 40ms to 39ms')
for path in args[1:]:
 for index,delay in [(0,30),(1,40),(2,50),(255,40)]:
  host=Speed(path);host.w8(0x476926,index)
  for command in (0x3100,):
   payload=bytearray(29);payload[4]=1;payload[5]=3;payload[6]=2;payload[7:28]=b'x'*20+b'\0'
   host.u.mem_write(PACKET+64,bytes(payload));host.w16(0x498724,0)
   host.call(R['room_speed_send'],(command,PACKET+64))
   assert host.delays()==(delay,delay)
   wire=bytes(host.u.mem_read(PACKET,39));assert wire[8]==39 and wire[16:37]==payload[7:28]
   assert wire[37]==0xa0+(index if index<=2 else 1)
   checksum=0
   for v in wire:checksum^=v
   assert checksum==0
   for local in (1,7):
    peer=Speed(path);peer.w8(0x59ee52,local);peer.w8(0x476926,0);peer.decode(wire)
    assert peer.delays()==(delay,delay) and bytes(peer.u.mem_read(PACKET,39))==wire
    for entry in (0x4431be,0x4431f6):assert peer.pace(entry)==delay
    peer.u.reg_write(UC_X86_REG_EDX,9);peer.u.reg_write(UC_X86_REG_EBP,0)
    peer.call(0x43a1bd,stop=0x43a1f9);assert peer.delays()==(delay,delay)
   peer=Speed(path);peer.w16(0x476904,40);peer.w16(0x476908,40);peer.decode(wire,1)
   assert peer.delays()==(40,40),'non-host changed speed'
   for field,value in [(8,38),(37,0),(37,0xa3)]:
    invalid=bytearray(wire);invalid[field]=value;peer.decode(bytes(invalid));assert peer.delays()==(40,40)
  lan=Speed(path);lan.combo=index;lan.u.reg_write(UC_X86_REG_ESI,123);lan.u.reg_write(UC_X86_REG_EAX,2)
  lan.call(0x42ee4c,stop=0x42ee51)
  assert lan.u.mem_read(0x476926,1)[0]==(index if index<=2 else 1) and lan.u.mem_read(0x476925,1)[0]==2
  solo=Speed(path);solo.w8(0x476926,index);solo.call(0x443198,stop=0x4431ab)
  assert solo.delays()==(delay,delay),'host without outgoing snapshot'
 print('PASS',path,'host/guest snapshot + checksum, name boundary, authority, 30/40/50ms both pacing loops, no drift, LAN combo')
