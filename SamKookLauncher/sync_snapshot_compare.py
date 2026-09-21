"""Compare two schema-4 FreeNet mismatch snapshots without player names/chat."""
import struct,sys
from pathlib import Path

COUNT,STRIDE=1700,292
CHUNK=256+16+COUNT*STRIDE
FIELDS=((4,1,'type'),(5,1,'owner'),(6,1,'kind'),(8,2,'hp'),
        (12,2,'action'),(0x18,2,'orderX'),(0x1a,2,'orderY'),
        (0x106,2,'x'),(0x108,2,'y'))

def load(path):
 data=Path(path).read_bytes()
 if len(data)<CHUNK or len(data)%CHUNK:raise ValueError('incomplete schema-4 snapshot: '+path)
 base=len(data)-CHUNK
 if struct.unpack_from('<II',data,base)!=(0x314e5953,4):raise ValueError('not schema-4: '+path)
 if struct.unpack_from('<III',data,base+256)!=(0x31504e53,COUNT,STRIDE):raise ValueError('bad snapshot header: '+path)
 return data[base:base+256],data[base+272:base+CHUNK]

def value(blob,off,size):return int.from_bytes(blob[off:off+size],'little')

if len(sys.argv)!=3:raise SystemExit('usage: sync_snapshot_compare.py A.bin B.bin')
ha,a=load(sys.argv[1]);hb,b=load(sys.argv[2])
print('frame',struct.unpack_from('<I',ha,12)[0],struct.unpack_from('<I',hb,12)[0])
print('local slots',struct.unpack_from('<I',ha,16)[0],struct.unpack_from('<I',hb,16)[0])
changes=[]
for unit in range(COUNT):
 oa=unit*STRIDE
 row=[]
 for off,size,name in FIELDS:
  av,bv=value(a,oa+off,size),value(b,oa+off,size)
  if av!=bv:row.append((name,av,bv))
 if row:changes.append((unit,row))
print('different entities',len(changes))
for unit,row in changes[:100]:
 print('unit',unit,' '.join('%s=%s/%s'%x for x in row))
if len(changes)>100:print('... truncated',len(changes)-100)
