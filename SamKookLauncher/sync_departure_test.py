"""Normal 8100/8200/8300/8400 boundaries must not be rejected as missing hashes.
Run with original.exe old-patched.exe new-patched.exe [new-patched36.exe].
"""
import sys,struct
args=sys.argv[1:];sys.argv=[sys.argv[0],args[0]]
from sync_safety_test import Game,R,FLAG
sys.argv=sys.argv[:1]
from quality_patch_test import Game as Serializer,PACKET

def prepare(path,opcode,slot=1,wrap=False):
 g=Game(path)
 for s in range(3):
  g.w8(0x49b046+s*1124,3)
  index=127 if wrap else 0
  ptr=g.get(0x4989a8+s*1168+index*4)
  op=opcode if s==slot else 0x8000
  payload=struct.pack('<II',op+s,0)+bytes([26 if op==0x8000 else 10 if op==0x8100 else 22])
  if op==0x8000:payload+=struct.pack('<4I',1,2,3,4)
  elif op!=0x8100:payload+=bytes(12)
  checksum=0
  for byte in payload:checksum^=byte
  g.u.mem_write(ptr,payload+bytes([checksum]))
  g.w16(0x498722+s*1168,index);g.w16(0x498724+s*1168,(index+1)&127)
  g.w16(0x498726+s*1168+index*2,1)
 return g

old=prepare(args[1],0x8100);old.run(R['compare_hash'])
assert old.get(FLAG)==1 and struct.unpack_from('<I',old.logs[0],8)[0]==6
print('PASS reproduced old false format-error exit on native departure boundary')
for path in args[2:]:
 for op in (0x8100,0x8200,0x8300,0x8400):
  serializer=Serializer(path);serializer.call(0x438cf0,(op,PACKET+1000))
  assert serializer.u.mem_read(PACKET+8,1)[0]==(10 if op==0x8100 else 22)
  for slot in (0,1,2):
   for wrap in (False,True):
    g=prepare(path,op,slot,wrap);g.w32(0x63d160,29);before=g.players()
    g.run(R['compare_hash'])
    assert g.get(FLAG)==0 and not g.logs and g.players()==before
    assert g.get(0x63d160)==0
    # Let the original receiver consume the boundary; it must still execute.
    ptr=g.get(0x4989a8+slot*1168+(127 if wrap else 0)*4)
    g.run(0x43a210,(slot,ptr))
    assert (slot in g.departed)==(op!=0x8400)
 # Ordinary matching barriers still pass; malformed hash packets still stop.
 g=prepare(path,0x8000);g.run(R['compare_hash']);assert not g.logs
 g=prepare(path,0x8000);ptr=g.get(0x4989a8+1168);g.w8(ptr+8,10)
 g.run(R['compare_hash']);assert g.get(FLAG)==1
 print('PASS',path,'all departure slots/opcodes/ring wrap; malformed hash detection retained')
