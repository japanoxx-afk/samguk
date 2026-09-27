"""Execute original image destructor; intercept only LocalFree to record ownership."""
import sys,struct
args=sys.argv[1:];sys.argv=sys.argv[:1]
from quality_patch_test import Game
from unicorn.x86_const import *
class Destructor(Game):
 def __init__(self,path,records):
  super().__init__(path);self.freed=[];self.records=records
  for i,(a,b) in enumerate(records):
   self.w32(0x1060000+20*i+12,a);self.w32(0x1060000+20*i+16,b)
 def hook(self,u,a,size,data):
  if a==0x44c510:
   sp=u.reg_read(UC_X86_REG_ESP);self.freed.append(self.r32(sp+4))
   u.reg_write(UC_X86_REG_EIP,self.r32(sp));u.reg_write(UC_X86_REG_ESP,sp+4)
  else:super().hook(u,a,size,data)
 def run(self):self.call(0x4523e0,(0x1060000,len(self.records)));return self.freed
fixture=[(0x1070000,0x1071000),(0x1072000,0x1073000)]
old=Destructor(args[0],fixture).run()
assert old==[0x1073000,0x1073000,0x1071000,0x1071000,0x1060000],old
print('REPRODUCED original double free + leaked second buffers')
for path in args[1:]:
 for rows in [fixture,[(0,0)],[(0,0x1071000)],[(0x1070000,0)]]:
  expected=[v for a,b in reversed(rows) for v in (b,a) if v]+[0x1060000]
  actual=Destructor(path,rows).run();assert actual==expected,(actual,expected)
 g=Destructor(path,fixture);g.call(0x4523e0,(0,2));assert not g.freed
 print('PASS',path,'each owned buffer freed once, null guards and stack preserved')
