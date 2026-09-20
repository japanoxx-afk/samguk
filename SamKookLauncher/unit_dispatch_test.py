"""Reproduce 2026-09-21 dump's unit-100/action-1081 dispatch and bad upgrade."""
import sys,struct
saved=sys.argv[:];sys.argv=sys.argv[:1]
# Shared emulator only; avoid importing its command-line tests.
from pathlib import Path
source=Path(__file__).with_name('sync_safety_test.py').read_text()
exec(compile(source.split('original=sys.argv[1]')[0],'sync_safety_test.py','exec'))
sys.argv=saved
UNIT=0x4a52c8 # id 100
def setup(path,kind=0):
 g=Game(path);g.w16(UNIT+2,100);g.w8(UNIT+4,24);g.w8(UNIT+5,0);g.w8(UNIT+6,kind)
 g.w16(UNIT+8,75);g.w16(UNIT+18,2);g.w32(0x49b054,10000);g.w32(0x49b058,10000)
 return g
def packet(g,unit=100,queue=0,building=3,sender=0):
 g.u.mem_write(PACKET,struct.pack('<IIBHHH',0xd00+sender,0,16,unit,queue,building)+b'\0')
 g.run(0x43a210,(sender,PACKET))

g=setup(sys.argv[1]);packet(g)
assert bytes(g.u.mem_read(UNIT+18,2))==b'\x81\x10'
g.run(0x406300,(UNIT,),end=0x4403691e)
assert g.u.reg_read(UC_X86_REG_EAX)==0x4403691e
print('PASS original reproduction: building-upgrade packet on soldier -> 1081 -> exact invalid call target from crash dump')
for path in sys.argv[2:]:
 for changes in ({},{'unit':0},{'unit':1700},{'unit':65535},{'queue':10},{'queue':65535},{'building':0},{'building':62},{'sender':1}):
  g=setup(path,0 if not changes else 1)
  before=bytes(g.u.mem_read(UNIT,292));resources=bytes(g.u.mem_read(0x49b054,8))
  packet(g,**changes)
  assert bytes(g.u.mem_read(UNIT,292))==before,changes
  assert bytes(g.u.mem_read(0x49b054,8))==resources,changes
 # Valid building upgrade must behave byte-for-byte like original.
 old=setup(sys.argv[1],1);g=setup(path,1);packet(old);packet(g)
 assert bytes(g.u.mem_read(UNIT,292))==bytes(old.u.mem_read(UNIT,292))
 assert bytes(g.u.mem_read(0x49b054,8))==bytes(old.u.mem_read(0x49b054,8))
 # All 256 possible action indices: valid table entries retain original pointer;
 # invalid entries never index the neighboring string data or invoke it.
 for command in range(256):
  g=setup(path);g.w16(UNIT+18,0x1000|command)
  if command<64:
   g.run(0x406300,(UNIT,),end=0x406340)
   expected=g.get(0x462940+command*24)
   assert g.u.reg_read(UC_X86_REG_EAX)==expected and not g.logs
  else:
   # Supply the ordinary cdecl unit argument when executing complete dispatcher.
   g.run(0x406300,(UNIT,))
   assert g.get(FLAG)==1 and g.get(0x611e14)==0x43
   assert struct.unpack_from('<I',g.logs[0],8)[0]==4
   assert struct.unpack_from('<4I',g.logs[0],48)==(100,0x1000|command,0,24)
   assert bytes(g.u.mem_read(UNIT+18,2))==struct.pack('<H',0x1000|command)
 g=setup(path);before=bytes(g.u.mem_read(UNIT,292));g.run(0x41f630,(UNIT,3,1))
 assert bytes(g.u.mem_read(UNIT,292))==before
 print('PASS upgrade target/owner/index validation, valid building equivalence, 256 dispatch indices, AI target guard',path)
