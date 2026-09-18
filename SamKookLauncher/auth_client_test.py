"""Verify server status interpretation against the original x86 client."""
import struct
import pefile
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn.x86_const import UC_X86_REG_ESP, UC_X86_REG_EIP, UC_X86_REG_EAX

pe = pefile.PE(r'C:\Users\seo\Downloads\DGGL\Games\SamKook_Win\SamKook.exe')
for command, expected in [(0x2a, 'create'), (0x36, 'proof'), (0x29, 'lobby')]:
    u = Uc(UC_ARCH_X86, UC_MODE_32)
    u.mem_map(0x400000, 0x400000)
    u.mem_write(0x400000, pe.get_memory_mapped_image())
    u.mem_map(0x1000000, 0x100000)
    packet, output, stack, done = 0x1000000, 0x1001000, 0x1080000, 0x1090000
    u.mem_write(0x4782a8, struct.pack('<I', 1))
    u.mem_write(0x4782b8, struct.pack('<I', output))
    u.mem_write(packet, struct.pack('<BBHI', 0xe1, command, 8, 1))
    u.mem_write(stack, struct.pack('<II', done, packet))
    u.reg_write(UC_X86_REG_ESP, stack)
    reached = []
    def hook(uc, address, size, _):
        if address in (0x43ba00, 0x43ba60):
            reached.append('create' if address == 0x43ba00 else 'proof')
            uc.emu_stop()
        elif address in (0x44c3f0, 0x43cd80):
            sp = uc.reg_read(UC_X86_REG_ESP)
            if address == 0x43cd80:
                assert bytes(uc.mem_read(output, 4)) == bytes.fromhex('e10b0800')
                reached.append('lobby')
            uc.reg_write(UC_X86_REG_EAX, 1)
            uc.reg_write(UC_X86_REG_EIP, struct.unpack('<I', uc.mem_read(sp, 4))[0])
            uc.reg_write(UC_X86_REG_ESP, sp+4)
        elif address == 0x43cafc:
            # GUI text input is outside this test; reaching this branch proves
            # E1/36 status 1 initiates the password-proof routine, not login.
            reached.append('proof'); uc.emu_stop()
    u.hook_add(UC_HOOK_CODE, hook)
    u.emu_start(0x43be20, done, count=10000)
    assert expected in reached, (command, reached)
    if expected == 'lobby':
        assert struct.unpack('<I', u.mem_read(0x4782a8, 4))[0] == 2
    print(f'PASS original client E1-{command:02X} status 1 -> {expected}')

# Exercise the actual channel event parser, stubbing only GUI calls.
u = Uc(UC_ARCH_X86, UC_MODE_32)
u.mem_map(0x400000, 0x400000)
u.mem_write(0x400000, pe.get_memory_mapped_image())
u.mem_map(0x1000000, 0x100000)
packet, stack, done = 0x1000000, 0x1080000, 0x1090000
u.mem_write(0x4782a8, struct.pack('<I', 2))
u.mem_write(packet, struct.pack('<BBHI', 0xe1, 0x0f, 37, 7) + bytes(20) + b'\0FreeNet\0')
u.mem_write(stack, struct.pack('<II', done, packet))
u.reg_write(UC_X86_REG_ESP, stack)
set_text = 0x1091000
u.mem_write(0x45f1b8, struct.pack('<I', set_text))
seen = []
def channel_hook(uc, address, size, _):
    if address in (0x416320, 0x44c3f0, set_text):
        sp = uc.reg_read(UC_X86_REG_ESP)
        if address == set_text:
            ptr = struct.unpack('<I', uc.mem_read(sp + 12, 4))[0]
            assert bytes(uc.mem_read(ptr, 8)) == b'FreeNet\0'
            seen.append('FreeNet')
        uc.reg_write(UC_X86_REG_EIP, struct.unpack('<I', uc.mem_read(sp, 4))[0])
        uc.reg_write(UC_X86_REG_ESP, sp + (16 if address == set_text else 4))
        uc.reg_write(UC_X86_REG_EAX, 1)
u.hook_add(UC_HOOK_CODE, channel_hook)
u.emu_start(0x43be20, done, count=10000)
assert seen == ['FreeNet'], seen
print('PASS original client channel event 7 displays FreeNet and returns')

# A successful room response must set state 4 and close the create dialog.
u.mem_write(0x4782a8, struct.pack('<I', 2))
u.mem_write(packet, struct.pack('<BBHI', 0xe1, 8, 8, 1))
u.mem_write(stack, struct.pack('<II', done, packet))
u.reg_write(UC_X86_REG_ESP, stack)
get_text, send_message = 0x1092000, 0x1093000
u.mem_write(0x45f21c, struct.pack('<I', get_text))
u.mem_write(0x45f250, struct.pack('<I', send_message))
closed = []
def room_hook(uc, address, size, _):
    if address not in (get_text, send_message):
        return
    sp = uc.reg_read(UC_X86_REG_ESP)
    args = struct.unpack('<IIII', uc.mem_read(sp+4, 16))
    if address == get_text:
        assert args[1] == 0x41a and args[3] == 32
        uc.mem_write(args[2], b'Test room\0')
    else:
        assert args[1] == 0x10  # WM_CLOSE
        closed.append(True)
    uc.reg_write(UC_X86_REG_EIP, struct.unpack('<I', uc.mem_read(sp, 4))[0])
    uc.reg_write(UC_X86_REG_ESP, sp+20)
    uc.reg_write(UC_X86_REG_EAX, 1)
u.hook_add(UC_HOOK_CODE, room_hook)
u.emu_start(0x43be20, done, count=10000)
assert closed == [True]
assert struct.unpack('<I', u.mem_read(0x4782a8, 4))[0] == 4
assert bytes(u.mem_read(0x478266, 10)) == b'Test room\0'
print('PASS original client E1-08 status 1 -> host state 4, name copied, dialog closed')
