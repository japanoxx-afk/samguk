"""Verify sender-loop yield code against the exact game instructions."""
import sys,struct
import pefile
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UC_HOOK_CODE
from unicorn.x86_const import *
pe=pefile.PE(sys.argv[1])
for available in (True,False):
    u=Uc(UC_ARCH_X86,UC_MODE_32);u.mem_map(0x400000,0x400000)
    u.mem_write(0x400000,pe.get_memory_mapped_image());u.mem_map(0x1000000,0x100000)
    def put(a,n):u.mem_write(a,struct.pack('<I',n))
    put(0x45f124,0x1091000);put(0x45f098,0x1092000);put(0x4782ac,0x12345678)
    calls=[]
    def hook(uc,a,size,_):
        if a not in (0x1091000,0x1092000,0x1093000):return
        sp=uc.reg_read(UC_X86_REG_ESP);args=struct.unpack('<III',uc.mem_read(sp,12))
        calls.append(a)
        if a==0x1091000:
            assert bytes(uc.mem_read(args[1],13))==b'kernel32.dll\0';value=0x77000000;pop=8
        elif a==0x1092000:
            assert args[1]==0x77000000 and bytes(uc.mem_read(args[2],6))==b'Sleep\0'
            value=0x1093000 if available else 0;pop=12
        else:assert args[1]==0;value=0;pop=8
        uc.reg_write(UC_X86_REG_EAX,value);uc.reg_write(UC_X86_REG_ESP,sp+pop);uc.reg_write(UC_X86_REG_EIP,args[0])
    u.hook_add(UC_HOOK_CODE,hook)
    for iteration in range(2):
        u.reg_write(UC_X86_REG_ESP,0x1080000)
        regs=[UC_X86_REG_EBX,UC_X86_REG_ECX,UC_X86_REG_EDX,UC_X86_REG_ESI,UC_X86_REG_EDI,UC_X86_REG_EBP]
        for i,r in enumerate(regs):u.reg_write(r,0x11223300+i)
        u.reg_write(UC_X86_REG_EFLAGS,0x246)
        u.emu_start(0x44a1a0,0x44a1a5,count=200)
        assert u.reg_read(UC_X86_REG_EIP)==0x44a1a5
        assert u.reg_read(UC_X86_REG_ESP)==0x1080000
        assert u.reg_read(UC_X86_REG_EAX)==0x12345678
        assert u.reg_read(UC_X86_REG_EFLAGS)==0x246
        for i,r in enumerate(regs):assert u.reg_read(r)==0x11223300+i
    if available:assert calls==[0x1091000,0x1092000,0x1093000,0x1093000],calls
    else:assert 0x1093000 not in calls
    print('PASS sender yield registers/flags/stack/cache; Sleep available='+str(available))
