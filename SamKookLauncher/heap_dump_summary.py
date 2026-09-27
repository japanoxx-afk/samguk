"""Print exception/stack code addresses only; supports full-memory dump streams."""
import struct,sys
from pathlib import Path
b=Path(sys.argv[1]).read_bytes()
u=lambda f,o:struct.unpack_from('<'+f,b,o)
n,d=u('II',8);s={u('III',d+i*12)[0]:u('III',d+i*12)[1:] for i in range(n)}
r=[]
if 9 in s:
 o=s[9][1];n,base=u('QQ',o)
 for i in range(n):
  a,z=u('QQ',o+16+16*i);r.append((a,a+z,base));base+=z
def read(a,z):
 for st,en,p in r:
  if st<=a and a+z<=en:return b[p+a-st:p+a-st+z]
 return None
mods=[];o=s[4][1]
for i in range(u('I',o)[0]):
 a,z=u('QI',o+4+i*108);p=u('I',o+24+i*108)[0];l=u('I',p)[0]
 mods.append((a,a+z,b[p+4:p+4+l].decode('utf-16le').split('\\')[-1]))
o=s[6][1];print('exception',hex(u('I',o+8)[0]),'parameters',[hex(x) for x in u('15Q',o+40)[:u('I',o+32)[0]]])
ctx=u('II',o+160)[1];sp=u('I',ctx+196)[0]
tid=u('I',o)[0];end=sp
o=s[3][1]
for i in range(u('I',o)[0]):
 pos=o+4+i*48
 if u('I',pos)[0]==tid:
  start,size,_=u('QII',pos+24);end=min(start+size,sp+4096)
for a in range(sp,end,4):
 data=read(a,4)
 if data is None:continue
 v=struct.unpack('<I',data)[0]
 for st,en,name in mods:
  if st<=v<en:print(hex(a),hex(v),name,hex(v-st))
print('destructor operand at 45241A:',(read(0x45241a,3) or b'').hex())
