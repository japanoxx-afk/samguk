"""RMB -> native packet -> independent receiver -> native production.
UI hit testing, allocation/initialization/audio mocked, not live multiplayer.
"""
import sys,struct
args=sys.argv[1:];sys.argv=sys.argv[:1]
from quality_patch_test import Game,PACKET
from rice_rally_test import Production

def buildings(g,limit):
 for n in range(limit+1):
  i=10+n;a=0x49e0b8+i*292
  g.w16(a+2,i);g.w8(a+4,1);g.w8(a+5,0);g.w8(a+6,1);g.w16(a+8,100)
  g.w16(a+0x7a,2);g.w8(a+0x7d,1)
  if n<limit:g.w16(g.players+2*n,i)
 return [0x49e0b8+(10+n)*292 for n in range(limit)]

for path in args:
 g=Game(path);limit=g.r32(0x432ebb);buildings(g,limit)
 for n in range(limit):g.w16(g.local+2*n,10+n)
 g.w16(g.local+2*limit,limit);g.w16(0x611e12,10)
 g.call(g.rally);assert g.r16(0x498724)==1
 wire=bytes(g.u.mem_read(PACKET,21));assert g.r32(PACKET)==0x400
 for local_slot in (0,1):
  peer=Production(path,kind=2,rice=0);selected=buildings(peer,limit)
  peer.w8(0x59ee52,local_slot)
  peer.u.mem_write(PACKET,wire);peer.call(0x43a210,(0,PACKET))
  for a in selected:
   assert peer.u.mem_read(a+0xca,1)==b'\x15',('missing rally',hex(a))
   assert (peer.r16(a+0x18),peer.r16(a+0x1a))==(30,40)
  assert peer.u.mem_read(selected[-1]+292+0xca,1)==b'\0','unselected building changed'
  for a in selected:
   peer.unit=a;peer.produce()
   assert peer.r16(peer.child+0x12)==0x2003
   assert (peer.r16(peer.child+0x18),peer.r16(peer.child+0x1a))==(30,40)
 print('PASS',path,'group RMB one native packet, every selected building and produced unit, independent peer/local slots, selection bound')
