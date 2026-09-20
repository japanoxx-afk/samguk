"""Execute native COM cleanup and installed lifecycle hooks, without networking."""
import struct
import sys
import pefile
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn.x86_const import UC_X86_REG_ESP, UC_X86_REG_EIP, UC_X86_REG_EAX

STACK, STOP = 0x1010000, 0x101f000
INIT, UNINIT, RELEASE = 0x101e000, 0x101e100, 0x101e200
OBJ, VT = 0x1002000, 0x1002100

def exercise(path, patched):
    u=Uc(UC_ARCH_X86,UC_MODE_32)
    u.mem_map(0x400000,0x300000)
    u.mem_write(0x400000,pefile.PE(path).get_memory_mapped_image())
    u.mem_map(0x1000000,0x20000)
    def get(a):return struct.unpack('<I',u.mem_read(a,4))[0]
    def put(a,v):u.mem_write(a,struct.pack('<I',v))
    put(0x45f2d0,INIT);put(0x45f2d8,UNINIT);put(OBJ,VT);put(VT+8,RELEASE)
    state={'result':0,'uninit':0,'release':0,'double':False}
    def hook(emu,a,size,_):
        if a not in (INIT,UNINIT,RELEASE,0x44c3f0):return
        sp=emu.reg_read(UC_X86_REG_ESP);pop=4;result=0
        if a==INIT:
            assert get(sp+4)==0
            pop=8;result=state['result']
        elif a==UNINIT:state['uninit']+=1
        elif a==RELEASE:
            assert get(sp+4)==OBJ
            state['release']+=1;state['double']=state['release']>1;pop=8
            if patched:assert get(0x613084)==0,'Pointer must clear BEFORE Release'
        emu.reg_write(UC_X86_REG_EAX,result)
        emu.reg_write(UC_X86_REG_EIP,get(sp));emu.reg_write(UC_X86_REG_ESP,sp+pop)
    u.hook_add(UC_HOOK_CODE,hook)
    def cleanup():
        put(STACK,STOP);u.reg_write(UC_X86_REG_ESP,STACK)
        u.emu_start(0x449c20,STOP,count=5000)
        assert u.reg_read(UC_X86_REG_EIP)==STOP
        assert u.reg_read(UC_X86_REG_ESP)==STACK+4
    def init(result):
        state['result']=result;put(STACK,0);u.reg_write(UC_X86_REG_ESP,STACK)
        u.emu_start(0x4499fb,0x449a01,count=1000)
        assert u.reg_read(UC_X86_REG_ESP)==STACK+4
        assert u.reg_read(UC_X86_REG_EAX)==result
    # Repeated cleanup before initialization must not uninitialize other users.
    cleanup();cleanup()
    if not patched:
        assert state['uninit']==2
        put(0x613084,OBJ);cleanup();cleanup()
        assert state['double']
        print('PASS original reproduces unowned COM teardown and duplicate Release')
        return
    assert state['uninit']==0
    init(0x80010106);cleanup();assert state['uninit']==0
    init(0);init(1) # S_FALSE also owns an initialization reference.
    assert get(0x63c100)==2
    put(0x613084,OBJ);cleanup();cleanup();cleanup()
    assert state['uninit']==2 and state['release']==1 and not state['double']
    assert get(0x63c100)==0 and get(0x613084)==0
    # Rejoin with a fresh logical object, then exit repeatedly.
    state['release']=0
    for _ in range(100):
        init(0);state['release']=0;put(0x613084,OBJ)
        cleanup();cleanup()
        assert state['release']==1 and not state['double']
        assert get(0x63c100)==0
    print('PASS lifecycle: failed/successful init, S_FALSE, 100 rejoins, duplicate cleanup',path)

exercise(sys.argv[1],False)
for path in sys.argv[2:]:exercise(path,True)
