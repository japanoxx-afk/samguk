"""Generate an exact-build patch manifest. Never ships or embeds game code/assets.

Run only on the documented original executable. Output is reviewed and embedded
as a text resource; the launcher does not heuristically scan instructions.
"""
import hashlib
import struct
import sys
from pathlib import Path
import capstone

b = Path(sys.argv[1]).read_bytes()
assert hashlib.sha256(b).hexdigest() == '39a11e76f5328a66a4fe8dcb1318ece6362843d8192caa8c7e15f0fc08abdc62'
m = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
m.detail = True
entries = []
# Offsets within the new zero-initialized data area: scratch=0, players=256.
# Player stride stays 1124. Each player has current selection + 14 groups.
# 15 * (36 IDs + count) * 2 = 1110 <= 1124; 37 would require 1140.
mapping = {0x6125d8: 0}
for address in range(0x6125f0, 0x6125fa, 2):
    mapping[address] = address-0x6125d8+48
for player in range(9):
    old = 0x49b302 + player*1124
    for group in range(15):
        for word in range(13):
            mapping[old+group*26+word*2] = 256+player*1124+group*74+(word*2 if word<12 else 72)
for old, relative in mapping.items():
    needle = struct.pack('<I', old)
    pos = 0x1000
    while True:
        pos = b.find(needle, pos, 0x5ee00)
        if pos < 0:
            break
        entries.append((pos, f'P {pos:x} {old:x} {relative:x}'))
        pos += 4

def imm(va, expected, replacement):
    i = next(m.disasm(b[va-0x400000:va-0x400000+16], va))
    assert i.operands[-1].type == capstone.CS_OP_IMM and i.operands[-1].imm == expected, (hex(va), i.op_str)
    offset = va-0x400000+i.imm_offset
    before = b[offset:offset+i.imm_size]
    after = replacement.to_bytes(i.imm_size, 'little')
    entries.append((offset, f'B {offset:x} {before.hex()} {after.hex()}'))

# Only audited selection/command loops. Portrait draw/hit-test stay at 12.
limits = '''410290 410340 4103f6 41da42 41e146 41e70e 432eba 433000
434f73 43791c 437a43 437afd 437bc9 437c7b 437e74 437f8f 438483
438547 43865b 43872b 43aafd 43abdc 43ac05 43b41a 43b42b 43b43d 43b51b
443c08 443c36 443de4 443f0a 444127 444554 444751 44482c 444870
44488b 4448be 4448d5 4448fa 44495e 4465c7 446759 44683b 446869
446872 4468b0 44699c 4469b9 4469c9 446a25 446a7c 446ae4
446b30 446b45 446b53 446bb0 447a31'''
for va in limits.split():
    imm(int(va,16),12,36)
for va in '''42bb15 4335c3 437857 43aa16 43aa3f 43aae6 443be5
443ca9 4440b0 4441ba 4442a6 44490e 444936'''.split():
    imm(int(va,16),6,18)
for va in ['438ed3','438f24','44666b']:
    imm(int(va,16),26,74)
imm(0x44666e,13,37)
# 13*g=(3*g)*4+g -> 37*g=(9*g)*4+g, preserving flags/register usage.
for va in '''41d9f0 41da63 41e156 41e4c0 43b3eb 43b466 43b4de
43b4ff 43b531 446710'''.split():
    va = int(va,16)
    i = next(m.disasm(b[va-0x400000:va-0x400000+16], va))
    assert i.mnemonic == 'lea' and i.operands[1].mem.scale == 2
    offset = va-0x400000+i.modrm_offset+1
    before = b[offset]
    assert before >> 6 == 1
    entries.append((offset, f'B {offset:x} {before:02x} {before|0x80:02x}'))

print('# Exact original SHA256: '+hashlib.sha256(b).hexdigest())
print('# P = pointer relocation to selection data area; B = checked literal change')
for _, line in sorted(entries):
    print(line)
