"""Read-only disassembly helper for the exact-build observer investigation."""
import sys
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

p = pefile.PE(sys.argv[1])
data = p.get_memory_mapped_image()
decoder = Cs(CS_ARCH_X86, CS_MODE_32)
for span in sys.argv[2:]:
    if ':' in span:
        start, end = (int(s, 16) for s in span.split(':'))
        print('\nSECTION', hex(start))
        for i in decoder.disasm(data[start-0x400000:end-0x400000], start):
            print(hex(i.address), i.bytes.hex(), i.mnemonic, i.op_str)
    else:
        target = int(span, 16)
        for offset in range(0x1000, 0x5f000-5):
            if data[offset] == 0xe8 and offset+0x400005+int.from_bytes(data[offset+1:offset+5], 'little', signed=True) == target:
                print('CALL', hex(offset+0x400000), '->', hex(target))
