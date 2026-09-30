"""Native UI occupancy corruption -> native rice-target divergence reproduction.
This demonstrates a real bug, not proof of the first cause of a supplied match.
Usage: original.exe patched-12.exe patched-36.exe
"""
import struct,sys
import pefile
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32
from unicorn.x86_const import UC_X86_REG_ESP,UC_X86_REG_EIP

STOP,SP,MAP=0x109f000,0x1010000,0x1080000
def game(path,occupant,preview):
 u=Uc(UC_ARCH_X86,UC_MODE_32);u.mem_map(0x400000,0x400000);u.mem_map(0x1000000,0x100000)
 u.mem_write(0x400000,pefile.PE(path).get_memory_mapped_image())
 def w16(a,v):u.mem_write(a,struct.pack('<H',v))
 def run(a,end,args=()):
  u.mem_write(SP,struct.pack('<'+'I'*(1+len(args)),STOP,*args));u.reg_write(UC_X86_REG_ESP,SP)
  u.emu_start(a,end,count=100000)
  assert u.reg_read(UC_X86_REG_EIP)==end
  if end==STOP:assert u.reg_read(UC_X86_REG_ESP)==SP+4
 cell=0x59ee5c+2*(18*232+65)
 w16(0x611e12,97);worker=0x49e0b8+97*292
 w16(worker+2,97);w16(worker+0x106,65);w16(worker+0x108,18)
 w16(cell,occupant);w16(0x477f46,1)
 before=bytes(u.mem_read(0x59ee5c,232*232*2))
 if preview:run(0x4343c1,0x4343c6)
 after=bytes(u.mem_read(0x59ee5c,232*232*2))
 # Another worker is walking to rice at (65,18). Only one PC clicked build.
 u.mem_write(0x477ae4,struct.pack('<I',MAP));w16(MAP+2,100);w16(MAP+4,100)
 for x in (65,66):u.mem_write(0x5da07c+18*232+x,b'\3')
 harvester=0x49e0b8+131*292
 w16(harvester+0x106,63);w16(harvester+0x108,18)
 w16(harvester+0x3a,65);w16(harvester+0x3c,18)
 run(0x422260,STOP,(harvester,))
 return before,after,bytes(u.mem_read(harvester+0x3a,4)),bytes(u.mem_read(harvester+0x10a,4))

for occupant in (0,224,97):
 plain=game(sys.argv[1],occupant,False);clicked=game(sys.argv[1],occupant,True)
 if occupant!=97:assert clicked[0]!=clicked[1], 'original must overwrite cell'
 if occupant==0:assert plain[2:]!=clicked[2:], 'native harvest target must diverge'
for path in sys.argv[2:]:
 for occupant in (0,224,97):
  plain=game(path,occupant,False);clicked=game(path,occupant,True)
  assert clicked[0]==clicked[1], 'local build UI changed occupancy'
  assert plain[2:]==clicked[2:], 'rice target diverged between peers'
 print('PASS',path,'native build-click occupancy preserved; native rice-target peer equivalence')
print('PASS original reproduction: local build click can overwrite empty/other-unit tile and change rice targeting')
