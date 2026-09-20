"""Read x86 minidump exception and stack code addresses, never dump account data."""
import struct
import sys
from pathlib import Path

b = Path(sys.argv[1]).read_bytes()
def unpack(fmt, off):
    return struct.unpack_from('<'+fmt, b, off)
n, directory = unpack('II', 8)
streams = {unpack('III', directory+i*12)[0]: unpack('III', directory+i*12)[1:] for i in range(n)}
modules = []
if 4 in streams:
    off = streams[4][1]
    for i in range(unpack('I', off)[0]):
        pos = off+4+108*i
        base, size = unpack('QI', pos)
        name = unpack('I', pos+20)[0]
        count = unpack('I', name)[0]
        label = b[name+4:name+4+count].decode('utf-16-le').split('\\')[-1]
        modules.append((base, base+size, label))
def symbol(addr):
    for start, end, name in modules:
        if start <= addr < end:
            return f'{name}+0x{addr-start:x}'
    return ''
off = streams[6][1]
thread = unpack('I', off)[0]
code = unpack('I', off+8)[0]
address = unpack('Q', off+24)[0]
ctx = unpack('II', off+160)[1]
eip, esp = unpack('I', ctx+184)[0], unpack('I', ctx+196)[0]
print(f'thread={thread} exception={code:08x} EIP={eip:08x} {symbol(eip)} ESP={esp:08x}')
print('exception parameters:', unpack('QQ', off+40))
for register, delta in [('EDI',156),('ESI',160),('EBX',164),('EDX',168),('ECX',172),('EAX',176),('EBP',180)]:
    print(f'{register}={unpack("I", ctx+delta)[0]:08x}')
off = streams[3][1]
for i in range(unpack('I', off)[0]):
    pos = off+4+48*i
    if unpack('I', pos)[0] != thread:
        continue
    start, size, rva = unpack('QII', pos+24)
    # DirectPlay's provider initialization can have a >12 KiB stack. A short
    # scan omitted the game's InitializeConnection caller in the join crash.
    for addr in range(esp, min(start+size, esp+65536), 4):
        val = unpack('I', rva+addr-start)[0]
        name = symbol(val)
        if name:
            print(f'stack+{addr-esp:04x}: {val:08x} {name}')
