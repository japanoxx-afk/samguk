"""Feed real server test responses into the original game's x86 lobby parser."""
import struct
import sys
from pathlib import Path
import pefile
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn.x86_const import UC_X86_REG_ESP, UC_X86_REG_EIP, UC_X86_REG_EAX

pe=pefile.PE(r'C:\Users\seo\Downloads\DGGL\Games\SamKook_Win\SamKook.exe')
for name in ('room','chat'):
    u=Uc(UC_ARCH_X86,UC_MODE_32)
    u.mem_map(0x400000,0x400000);u.mem_write(0x400000,pe.get_memory_mapped_image())
    u.mem_map(0x1000000,0x100000)
    packet,stack,done,allocated=0x1000000,0x1080000,0x1090000,0x1070000
    u.mem_write(packet,(Path(sys.argv[1])/(name+'-response.bin')).read_bytes())
    u.mem_write(0x4782a8,struct.pack('<I',2))
    u.mem_write(stack,struct.pack('<II',done,packet));u.reg_write(UC_X86_REG_ESP,stack)
    u.mem_write(0x45f240,struct.pack('<I',0x1092000))
    u.mem_write(0x45f214,struct.pack('<I',0x1093000))
    u.mem_write(allocated+75,b'GUARD')
    observed=[]
    def string(addr):
        result=bytearray()
        for i in range(1024):
            c=u.mem_read(addr+i,1)[0]
            if not c:return bytes(result)
            result.append(c)
        raise AssertionError('Unterminated string')
    def hook(uc,addr,size,_):
        if addr not in (0x44c3f0,0x416320,0x44c4e0,0x44c510,0x1092000,0x1093000):return
        sp=uc.reg_read(UC_X86_REG_ESP);pop=4;value=0
        if addr==0x44c4e0:value=allocated
        if addr==0x1093000:
            dest,fmt,user,text=struct.unpack('<IIII',uc.mem_read(sp+4,16))
            assert string(fmt)==b'%s> %s'
            uc.mem_write(dest,string(user)+b'> '+string(text)+b'\0')
        if addr==0x1092000:
            hwnd,control,msg,wparam,lparam=struct.unpack('<IIIII',uc.mem_read(sp+4,20));pop=24
            if msg==0x180:observed.append(string(lparam))
        uc.reg_write(UC_X86_REG_EIP,struct.unpack('<I',uc.mem_read(sp,4))[0])
        uc.reg_write(UC_X86_REG_ESP,sp+pop);uc.reg_write(UC_X86_REG_EAX,value)
    u.hook_add(UC_HOOK_CODE,hook);u.emu_start(0x43be20,done,count=50000)
    if name=='room':
        assert observed==[b'Test room'],observed
        assert string(allocated+12)==b'testuser'
        assert string(allocated+43)==b'Map01'
        assert bytes(u.mem_read(allocated+8,4))==bytes([127,0,0,1])
        assert bytes(u.mem_read(allocated+75,5))==b'GUARD'
    else:assert observed==['testuser> 안녕하세요'.encode('cp949')],observed
    print('PASS original client '+name+' response display and return')
