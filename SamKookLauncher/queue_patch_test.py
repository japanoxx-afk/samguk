"""Run with PYTHONPATH pointing to pefile + unicorn. No game execution."""
import struct
from pathlib import Path
import pefile
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE, UcError
from unicorn.x86_const import UC_X86_REG_ESP, UC_X86_REG_EIP, UC_X86_REG_EAX

ROOT = Path(__file__).resolve().parent

def exercise(path, patched):
    pe = pefile.PE(str(path))
    u = Uc(UC_ARCH_X86, UC_MODE_32)
    u.mem_map(0x400000, 0x400000)
    u.mem_write(0x400000, pe.get_memory_mapped_image())
    u.mem_map(0x1000000, 0x100000)
    queue, packet, done = 0x1090000, 0x1091000, 0x1092000
    alloc, freed, sent = [0x1080000], set(), []
    def get(a): return struct.unpack('<I', u.mem_read(a, 4))[0]
    def put(a, v): u.mem_write(a, struct.pack('<I', v))
    put(queue, 1)
    u.mem_write(packet, bytes.fromhex('e105080001020304'))
    def hook(uc, address, size, _):
        sp = uc.reg_read(UC_X86_REG_ESP)
        if address == 0x44c4e0:
            ptr = alloc[0]; alloc[0] += 0x100
            uc.mem_write(ptr, b'\0' * 0x100)
            uc.reg_write(UC_X86_REG_EAX, ptr)
        elif address == 0x44c510:
            ptr = get(sp+4)
            assert ptr not in freed, 'double free'
            freed.add(ptr)
        elif address == 0x44c36a:
            ptr, length = get(sp+8), get(sp+12)
            sent.append(bytes(uc.mem_read(ptr, length)))
            uc.reg_write(UC_X86_REG_EAX, length)
            uc.reg_write(UC_X86_REG_EIP, get(sp))
            uc.reg_write(UC_X86_REG_ESP, sp+20)
            return
        else: return
        uc.reg_write(UC_X86_REG_EIP, get(sp))
        uc.reg_write(UC_X86_REG_ESP, sp+4)
    u.hook_add(UC_HOOK_CODE, hook)
    def setup(entry, sp, args):
        u.reg_write(UC_X86_REG_ESP, sp)
        u.mem_write(sp, struct.pack('<'+'I'*(len(args)+1), done, *args))
        u.reg_write(UC_X86_REG_EIP, entry)
    setup(0x43cd80, 0x1010000, [queue, packet, 0x31])
    # Pause after publishing head/type but before allocating the packet buffer.
    u.emu_start(0x43cd80, 0x43cde2, count=10000)
    assert get(queue+0x14) != 0
    producer = u.context_save()
    setup(0x43cca0, 0x1020000, [queue, 42])
    if not patched:
        try: u.emu_start(0x43cca0, done, count=1000)
        except UcError:
            assert u.reg_read(UC_X86_REG_EIP) == 0x43ccbd
            print('PASS: original interleaving reproduces crash at 0x43ccbd')
            return
        raise AssertionError('Original unexpectedly survived')
    u.emu_start(0x43cca0, done, count=1000)
    assert not sent and u.reg_read(UC_X86_REG_EIP) != done
    consumer = u.context_save()
    u.context_restore(producer)
    u.emu_start(u.reg_read(UC_X86_REG_EIP), done, count=10000)
    u.context_restore(consumer)
    u.emu_start(u.reg_read(UC_X86_REG_EIP), done, count=10000)
    assert sent == [bytes.fromhex('e105080001020304')]
    assert get(queue+0x14) == 0 and len(freed) == 2
    print('PASS: patched interleaving waits, sends once and releases queue')

exercise(Path(r'C:\Users\seo\Downloads\DGGL\Games\SamKook_Win\SamKook.exe'), False)
exercise(ROOT / 'runtime' / 'queue-test.exe', True)
