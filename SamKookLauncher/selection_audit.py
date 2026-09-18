"""Read-only exact-build disassembly helper (pefile/capstone on PYTHONPATH)."""
import argparse
from pathlib import Path
import capstone
import pefile

p = argparse.ArgumentParser()
p.add_argument('game')
p.add_argument('ranges', nargs='*', help='hex VA:VA')
args = p.parse_args()
pe = pefile.PE(args.game)
image = pe.get_memory_mapped_image()
base = pe.OPTIONAL_HEADER.ImageBase
m = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
m.detail = True
m.skipdata = True
for interval in args.ranges:
    low, high = (int(x, 16) for x in interval.split(':'))
    for i in m.disasm(image[low-base:high-base], low):
        print(f'{i.address:08x} {i.bytes.hex():24} {i.mnemonic} {i.op_str}')
if not args.ranges:
    for section in pe.sections:
        if not section.Characteristics & 0x20000000:
            continue
        for i in m.disasm(section.get_data(), base+section.VirtualAddress):
            if not i.id:
                continue
            values = [o.imm if o.type == capstone.CS_OP_IMM else o.mem.disp
                      for o in i.operands if o.type in (capstone.CS_OP_IMM, capstone.CS_OP_MEM)]
            if any(0x49b302 <= x <= 0x49b488 or 0x49d622 <= x <= 0x49d63a or
                   0x6125d8 <= x <= 0x6125f6 for x in values):
                print(f'{i.address:08x} {i.bytes.hex():24} {i.mnemonic} {i.op_str}')
