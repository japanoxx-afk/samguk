"""Reproduce native unilateral eviction; test fail-stop and diagnostic x86.
API calls are mocked. This is NOT a real network or root-cause reproduction.
"""
import sys,struct
from pathlib import Path
import pefile
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UC_HOOK_CODE
from unicorn.x86_const import *
STOP,STACK,PACKET=0x109f000,0x1010000,0x1080000
ROLE,ACTIVE,FLAG,HASH=0x63b000,0x63b008,0x63d100,0x63d108
R={}
for line in Path(__file__).with_name('sync.manifest').read_text().splitlines():
 if line.startswith('# ') and ' 0x' in line:
  _,n,a=line.split();R[n]=int(a,16)

class Game:
 def __init__(self,path,local=0,io_fail=False):
  self.u=Uc(UC_ARCH_X86,UC_MODE_32);self.u.mem_map(0x400000,0x400000);self.u.mem_map(0x1000000,0x100000)
  self.u.mem_write(0x400000,pefile.PE(path).get_memory_mapped_image())
  self.logs=[];self.snapshots=[];self.departed=[];self.sent=[];self.io_fail=io_fail;self.packets=[];self.pump_stop=False
  for i,iat in enumerate((0x45f124,0x45f098,0x45f0ec,0x45f19c,0x45f0dc,0x45f270)):
   self.w32(iat,0x1090000+i*16)
  self.w32(0x5173e4,1);self.w32(0x611e14,2);self.w32(0x5173d0,3000)
  self.w32(0x6124e0,12345);self.w32(0x6124e8,678);self.w8(0x59ee52,local)
  self.w16(0x4868f0,3);self.w16(0x4868e8,1<<local)
  for i in range(8):
   self.w8(0x49b046+i*1124,3 if i<2 else 2 if i==2 else 0)
   self.w32(0x49b02c+i*1124,i+1 if i<2 else 0)
   self.w32(0x49871c+i*1168,0);self.w16(0x498722+i*1168,0);self.w16(0x498724+i*1168,0)
   for q in range(128):self.w32(0x4989a8+i*1168+q*4,0x1040000+i*0x4000+q*128)
  self.u.hook_add(UC_HOOK_CODE,self.hook)
 def w8(self,a,v):self.u.mem_write(a,bytes([v]))
 def w16(self,a,v):self.u.mem_write(a,struct.pack('<H',v))
 def w32(self,a,v):self.u.mem_write(a,struct.pack('<I',v))
 def get(self,a):return struct.unpack('<I',self.u.mem_read(a,4))[0]
 def ret(self,argc=0,value=0):
  sp=self.u.reg_read(UC_X86_REG_ESP);self.u.reg_write(UC_X86_REG_EAX,value)
  self.u.reg_write(UC_X86_REG_EIP,self.get(sp));self.u.reg_write(UC_X86_REG_ESP,sp+4+argc*4)
 def hook(self,u,a,size,_):
  sp=u.reg_read(UC_X86_REG_ESP)
  if a==0x1090000:self.ret(1,0x1000)
  elif a==0x1090010:self.ret(2,0x1090100)
  elif a==0x1090100:self.ret(7,0xffffffff if self.io_fail else 42)
  elif a==0x1090020:
   size=self.get(sp+12);blob=bytes(u.mem_read(self.get(sp+8),size))
   if size==256:self.logs.append(blob)
   else:self.snapshots.append(blob)
   self.ret(5,1)
  elif a in (0x1090030,0x1090040):self.ret(1,1)
  elif a==0x1090050:self.ret(0,1000)
  elif a==0x441310:self.departed.append(self.get(sp+4));self.ret()
  elif a==0x438cf0:self.sent.append(self.get(sp+4));self.ret(0,1)
  elif a in (0x44c3f0,0x45523e,0x443940,0x44c3a0):self.ret()
  elif a==0x439cb0:
   if self.packets:
    p=self.packets.pop(0);u.mem_write(PACKET,p);self.w32(0x49d7ac,PACKET);self.ret(0,len(p))
   else:self.ret()
  elif a==0x44aae0:
   if self.pump_stop:self.w32(FLAG,1);self.w32(0x611e14,0x43)
   self.ret()
  elif a in (0x442c00,0x422f90,0x422fc0,0x432de0):self.ret()
  elif a==0x442060:self.ret(0,1)
 def run(self,a,args=(),end=STOP):
  self.u.mem_write(STACK,struct.pack('<'+'I'*(1+len(args)),STOP,*args));self.u.reg_write(UC_X86_REG_ESP,STACK)
  self.u.emu_start(a,end,count=1000000)
  assert self.u.reg_read(UC_X86_REG_EIP)==end,hex(self.u.reg_read(UC_X86_REG_EIP))
  if end==STOP:assert self.u.reg_read(UC_X86_REG_ESP)==STACK+4
 def players(self):return bytes(self.u.mem_read(0x49b028,1124*8))

original=sys.argv[1]
for local in (0,1):
 g=Game(original,local);g.run(0x439770)
 assert g.departed==[1-local] and g.u.mem_read(0x49b046+(1-local)*1124,1)==b'\xfd'
 assert g.get(0x611e14)==2,'native match did not continue'
print('PASS reproduction: original A/B independently evict each other and both remain in gameplay (with CPU)')

for path in sys.argv[2:]:
 for local in (0,1):
  for failure in (False,True):
   g=Game(path,local,failure);before=g.players();g.run(0x439770)
   assert g.get(0x611e14)==0x43 and g.get(FLAG)==1
   assert g.players()==before and not g.departed and not g.sent
   assert len(g.logs)==(0 if failure else 1)
   if not failure:
    r=g.logs[0];assert struct.unpack_from('<4I',r)==(0x314e5953,4,1,3000)
    assert struct.unpack_from('<I',r,16)[0]==local
    assert struct.unpack_from('<4I',r,24)==(12345,678,3,1<<local)
    assert [len(x) for x in g.snapshots]==[16,496400]
    assert struct.unpack_from('<4I',g.snapshots[0])==(0x31504e53,1700,292,0x49e0b8)
   g.run(0x439770);assert len(g.logs)==(0 if failure else 1)
   # Original check path must not evict players after an earlier failure.
   g.run(0x443710);assert g.players()==before
  g=Game(path,local);before=g.players()
  g.w8(0x498720,1);g.w8(0x498720+1168,2);g.run(0x443710)
  assert g.get(0x611e14)==0x43 and g.players()==before and not g.departed
  assert struct.unpack_from('<I',g.logs[0],8)[0]==2
  # Matching native check values must neither stop nor log anything.
  g=Game(path,local);before=g.players();g.run(0x443710)
  assert g.get(FLAG)==0 and not g.logs and g.players()==before
  # Execute the original receiver from entry, not just our hook.
  g=Game(path,local);before=g.players()
  payload=bytearray(8);payload[1-local]=1
  g.packets=[struct.pack('<II',0x8500+(1-local),0)+b'\x12'+payload+b'\0']
  g.run(0x4399f0)
  assert g.get(FLAG)==1 and not g.departed
  after=bytearray(g.players());after[(1-local)*1124+0x26]=before[(1-local)*1124+0x26]
  assert bytes(after)==before # native packet ingestion increments pending count
  assert struct.unpack_from('<I',g.logs[0],8)[0]==3
  # Empty or CPU-only eviction notice is not a connected-player removal.
  for target in (None,2):
   g=Game(path,local);payload=bytearray(8)
   if target is not None:payload[target]=1
   g.packets=[struct.pack('<II',0x8500+(1-local),0)+b'\x12'+payload+b'\0']
   g.run(0x4399f0);assert g.get(FLAG)==0 and not g.logs
  # A recovered wait must not be aborted by a stale drop-button click.
  g=Game(path,local);g.w16(0x4868e8,3);g.run(0x439770)
  assert not g.logs and not g.departed and g.get(FLAG)==0
  g.w32(FLAG,1);g.run(R['new_match']);assert g.get(FLAG)==0
 # A failure raised during the native waiting loop must actually unwind it.
 g=Game(path);g.pump_stop=True;g.run(0x439db0)
 assert g.get(0x611e14)==0x43 and g.get(FLAG)==1
 # Single-player timeout helper replays the native path unchanged.
 g=Game(path);g.w32(0x5173e4,0);g.run(0x439770)
 assert g.get(FLAG)==0 and g.departed==[1]
 # No mismatch: all connected players' checks match, disconnected stale checks ignored.
 g=Game(path);g.w8(0x498720+2*1168,255);g.run(0x443710);assert not g.logs
 # State fingerprint is deterministic, changes with simulation state, and is
 # appended to the native barrier with a valid XOR trailer.
 g=Game(path);g.run(R['state_hash']);h=bytes(g.u.mem_read(HASH,16))
 g2=Game(path);g2.run(R['state_hash']);assert bytes(g2.u.mem_read(HASH,16))==h
 g2.w32(0x49b054,g2.get(0x49b054)+1);g2.run(R['state_hash'])
 h2=bytes(g2.u.mem_read(HASH,16));assert h2!=h and h2[:4]==h[:4] and h2[8:]==h[8:]
 g=Game(path);g.w32(0x5173e4,0);g.run(0x438a90)
 packet=g.get(0x4989a8);wire=bytes(g.u.mem_read(packet,26))
 assert wire[8]==26 and any(wire[9:25])
 checksum=0
 for value in wire:checksum^=value
 assert checksum==0
 # Equal fingerprints continue. One-frame construction/harvest transitions are
 # tolerated; only three consecutive differing barriers fail-stop.
 for mismatch in (False,True):
  g=Game(path)
  for slot in (0,1):
   ptr=g.get(0x4989a8+slot*1168)
   hashes=[0x11111111,0x22222222,0x33333333,0x44444444]
   if mismatch and slot==1:hashes[2]+=1
   payload=struct.pack('<II',0x8000+slot,0)+bytes([26])+struct.pack('<4I',*hashes)
   trailer=0
   for value in payload:trailer^=value
   g.u.mem_write(ptr,payload+bytes([trailer]));g.w16(0x498724+slot*1168,1)
  g.run(R['compare_hash'])
  if mismatch:
   assert g.get(FLAG)==0 and not g.logs
   # A matching barrier clears the grace counter.
   peer=g.get(0x4989a8+1168);g.u.mem_write(peer+17,struct.pack('<I',0x33333333))
   g.run(R['compare_hash']);assert g.get(FLAG)==0 and not g.logs
   g.u.mem_write(peer+17,struct.pack('<I',0x33333334))
   g.run(R['compare_hash']);g.run(R['compare_hash']);assert g.get(FLAG)==0 and not g.logs
   g.run(R['compare_hash'])
   assert g.get(FLAG)==1 and struct.unpack_from('<I',g.logs[0],8)[0]==5
   assert struct.unpack_from('<I',g.logs[0],40)[0]==0x33333333
   assert struct.unpack_from('<I',g.logs[0],48)[0]==0x33333334
   assert struct.unpack_from('<3I',g.logs[0],52)==(0,1,2)
  else:assert g.get(FLAG)==0 and not g.logs
 print('PASS protected host/peer paths: timeout, native mismatch, remote eviction, once-only diagnostics, logging failure, wait unwind, ordinary/offline paths',path)
