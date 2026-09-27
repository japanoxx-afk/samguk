"""Real x86 move action and native free-tile search; not a full path simulation."""
import sys
args=sys.argv[1:];sys.argv=sys.argv[:1]
from quality_patch_test import Game
class Move(Game):
 def __init__(self,path,x,y,occupied=True,kind=1,blocked=False):
  super().__init__(path)
  self.w32(0x477ae4,0x1060000);self.w16(0x1060002,136);self.w16(0x1060004,136)
  self.u.mem_write(0x59ee5c,(b'\x02\0' if blocked else b'\0\0')*(232*232))
  self.u.mem_write(0x5da07c,b'\0'*(232*232))
  self.w8(self.unit+4,kind);self.w8(self.unit+6,0)
  self.w16(self.unit+0x12,0x2003);self.w16(self.unit+0x16,0);self.w8(self.unit+0xca,0x10)
  self.w16(self.unit+0x106,x);self.w16(self.unit+0x108,y)
  self.w16(self.unit+0x18,40);self.w16(self.unit+0x1a,40)
  if occupied:self.w16(0x59ee5c+2*(40*232+40),2)
  # Explicit movement class, same classification used by original command code.
  self.w16(0x46134c+84*kind,1 if kind in (1,11,23) else 2)
 def run(self):
  self.call(0x40ffb0,(self.unit,))
  return self.r16(self.unit+0x12),self.r16(self.unit+0x18),self.r16(self.unit+0x1a),self.u.mem_read(self.unit+0xca,1)[0]
old=args[0]
assert Move(old,20,40).run()[0]==2
assert Move(old,60,40).run()[0]==0x2003
print('REPRODUCED: occupied destination cancels move 20 tiles away from west but not east')
for path in args[1:]:
 for kind in (1,11,23):
  for x,y in [(20,40),(60,40),(40,20),(40,60),(20,20),(60,60)]:
   a=Move(path,x,y,kind=kind);result=a.run();assert result[0]==0x2003 and result[3]==0x21,result
   peer=Move(path,x,y,kind=kind);peer.w8(0x59ee52,7);peer.w16(0x59ee54,90)
   assert peer.run()==result,'peer-dependent movement'
 for x,y,kw in [(39,40,{}),(39,39,{}),(40,40,{}),(20,40,dict(occupied=False)),(20,40,dict(blocked=True)),(20,40,dict(kind=2))]:
  assert Move(path,x,y,**kw).run()==Move(old,x,y,**kw).run(),(x,y,kw)
 # Independent selected units must not stop prematurely even in large selections.
 for count in (12,36):
  for i in range(count):assert Move(path,10+i%6,20+i//6).run()[0]==0x2003
 print('PASS',path,'distant move retained; near/empty/blocked/ship unchanged; 12/36 units; peer determinism')
