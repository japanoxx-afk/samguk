"""Execute installed x86 observer hooks in Unicorn, including mocked Win32 combos.

These are component tests, not a claim of successful three-PC synchronization.
"""
import struct
import sys
import uuid
from pathlib import Path
import pefile
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn.x86_const import *

BASE=0x630000
ROLE=BASE+45056
ACTIVE=ROLE+8
STACK=0x1008000
STOP=0x101f000
API=0x101e000
ROUTINES={}
for line in Path(__file__).with_name('observer.manifest').read_text().splitlines():
    if line.startswith('# ') and ' 0x' in line:
        _,name,address=line.split()
        ROUTINES[name]=int(address,16)

def dword(n): return struct.pack('<I', n & 0xffffffff)
def word(n): return struct.pack('<H', n & 0xffff)

def emulate(path):
    pe=pefile.PE(path)
    image=pe.get_memory_mapped_image()
    def fresh(active=True):
        u=Uc(UC_ARCH_X86,UC_MODE_32)
        u.mem_map(0x400000,0x300000)
        u.mem_write(0x400000,image)
        u.mem_map(0x1000000,0x20000)
        u.reg_write(UC_X86_REG_ESP,STACK)
        u.mem_write(STACK,dword(STOP))
        u.mem_write(ACTIVE,bytes([int(active)]))
        return u
    def run(u,start,ends=(STOP,)):
        reached=[]
        def on_code(emu,address,size,_):
            if address in ends:
                reached.append(address);emu.emu_stop()
        h=u.hook_add(UC_HOOK_CODE,on_code)
        try:u.emu_start(start,0,count=100000)
        finally:u.hook_del(h)
        assert reached, ('did not reach boundary',hex(u.reg_read(UC_X86_REG_EIP)))
        return reached[0]
    def role(u,slot):u.mem_write(ROLE+slot,b'\x01')
    def regs(u):return [u.reg_read(r) for r in (UC_X86_REG_EBX,UC_X86_REG_ESI,UC_X86_REG_EDI,UC_X86_REG_EBP)]

    # Shared sight direction and resource isolation for each possible non-host.
    for observer in range(8):
        u=fresh();role(u,observer)
        for slot in range(8):
            u.mem_write(0x49b046+1124*slot,b'\x03')
            u.mem_write(0x49b048+1124*slot,word(1<<slot)+word(1<<slot))
            u.mem_write(0x49b054+1124*slot,dword(100)*4)
        before=regs(u)
        run(u,ROUTINES['vision'])
        assert regs(u)==before and u.reg_read(UC_X86_REG_ESP)==STACK+4
        for slot in range(8):
            ally,vision=struct.unpack('<HH',u.mem_read(0x49b048+1124*slot,4))
            assert ally==1<<slot
            assert vision==(1<<slot | 1<<observer)
            assert bytes(u.mem_read(0x49b054+1124*slot,16))==dword(0 if slot==observer else 100)*4
        # Original unit rendering branches consume the generated sharing mask.
        for owner in range(8):
            for viewer in range(8):
                u.reg_write(UC_X86_REG_EAX,0);u.reg_write(UC_X86_REG_ECX,viewer)
                u.mem_write(0x49e0bd,bytes([owner]))
                end=run(u,0x442a13,(0x442a3b,0x442aaf))
                assert (end==0x442a3b)==(viewer==owner or viewer==observer)
    print('PASS merged native vision: 512 combinations, no opponent/alliance leakage')

    # Outgoing + incoming command gates: no movement, production, diplomacy,
    # pause or speed commands; native control/no-op/chat still pass through.
    allowed={0x100,0x300,0x8000,0x8100,0x8200,0x8300,0x8400}
    for acting_slot in (0,2):
        for active in (False,True):
            for observer in (False,True):
                for command in list(range(0x100,0x2000,0x100))+list(range(0x8000,0x8600,0x100)):
                    deny=active and observer and command not in allowed
                    for inbound in (False,True):
                        u=fresh(active);u.mem_write(0x59ee52,bytes([acting_slot]))
                        if observer:role(u,acting_slot)
                        if inbound:
                            u.mem_write(STACK+4,dword(acting_slot)+dword(0x1009000))
                            u.mem_write(0x1009000,dword(command))
                            start,resume=0x43a210,0x43a217
                        else:
                            u.mem_write(STACK+4,dword(command))
                            start,resume=0x438cf0,0x438cf7
                        end=run(u,start,(STOP,resume))
                        assert (end==STOP)==deny,(active,observer,command,inbound)
                        if deny:assert u.reg_read(UC_X86_REG_ESP)==STACK+4
    print('PASS sender/receiver: gameplay denied, transport/chat preserved, ordinary/offline unchanged')

    # Map initial unit/building records and native melee start-location gate.
    for observer in (False,True):
        for kind in (1,2,3,4):
            u=fresh();record=0x1009000
            if observer:role(u,2)
            u.mem_write(record,word(kind)+word(1)+word(2)+bytes(32))
            u.mem_write(STACK+4,dword(record)+dword(0))
            end=run(u,0x414c00,(STOP,0x414c08))
            assert (end==STOP)==(observer and kind in (1,2))
        u=fresh();record=0x1009000
        if observer:role(u,2)
        u.mem_write(record+4,word(2));u.reg_write(UC_X86_REG_ESI,record)
        u.reg_write(UC_X86_REG_EAX,1124*2);u.mem_write(0x49b046+1124*2,b'\x03')
        assert run(u,0x41507d,(0x415084,0x415176))==(0x415176 if observer else 0x415084)
    print('PASS observer initial units/buildings/start locations suppressed')

    # Global defeat scanning must continue for players while skipping observers.
    for slot in range(8):
        u=fresh();role(u,2);u.reg_write(UC_X86_REG_ESI,slot)
        u.reg_write(UC_X86_REG_EBX,0x49b046+1124*slot)
        u.mem_write(0x49b046+1124*slot,b'\x03')
        assert run(u,0x4488df,(0x4488e4,0x44893f))==(0x44893f if slot==2 else 0x4488e4)
    for observer in (False,True):
        u=fresh();u.mem_write(0x59ee52,b'\x02')
        if observer:role(u,2)
        assert run(u,0x4487f0,(STOP,0x4487f7))==(STOP if observer else 0x4487f7)
        u=fresh();u.mem_write(0x59ee52,b'\x02')
        if observer:role(u,2)
        u.mem_write(STACK,bytes(16)+dword(STOP))
        assert run(u,0x448953,(STOP,0x44895a))==(STOP if observer else 0x44895a)
    print('PASS observer excluded from native defeat and local victory/loss')

    u=fresh();role(u,2);u.mem_write(STACK+4,dword(2))
    u.mem_write(0x49b046+1124*2,b'\x03');u.mem_write(0x49b02c+1124*2,dword(123))
    snapshot=bytes(u.mem_read(0x49b028,1124*8))
    run(u,0x441310)
    assert u.mem_read(0x49b046+1124*2,1)==b'\xfd'
    assert u.mem_read(0x49b02c+1124*2,4)==bytes(4)
    expected=bytearray(snapshot);expected[1124*2+0x1e]=0xfd;expected[1124*2+4:1124*2+8]=bytes(4)
    assert bytes(u.mem_read(0x49b028,1124*8))==expected
    print('PASS departure changes only observer connection state/DPID, not allies or active players')

    # Role snapshots are copied before decoding. The shared outgoing queue must
    # retain wire bit 40 even if the host consumes its local copy first.
    for sender in (0,1):
        for command in (0x3100,0x3400,0x3500):
            for slot in (0,2,7,8,255):
                for status in (0,3,0x40,0x43,0xff,0x44):
                    u=fresh(False);packet=bytearray(38)
                    packet[0:4]=dword(command);packet[13]=slot;packet[14]=status
                    u.mem_write(0x1009000,bytes(packet))
                    u.reg_write(UC_X86_REG_EBP,0x1009000);u.reg_write(UC_X86_REG_EDX,sender)
                    end=run(u,0x42ccec,(0x42ccf4,0x42d2e0))
                    valid=slot<8 and (command==0x3500 or (sender==0 and (status==0xff or status&0xbf<=3)))
                    assert (end==0x42ccf4)==valid,(sender,command,slot,status)
                    assert bytes(u.mem_read(0x1009000,38))==packet
                    if valid and command!=0x3500:
                        wanted=status in (0x40,0x43)
                        assert u.mem_read(ROLE+slot,1)==bytes([int(wanted)])
    print('PASS role packets: authority, bounds, unused slots, unchanged wire queue')

    for slot in range(8):
        for status in (0,1,2,3,0xff):
            u=fresh(False);role(u,slot);packet=0x1009000
            u.mem_write(packet+4,bytes([slot]));u.reg_write(UC_X86_REG_EBX,packet)
            u.reg_write(UC_X86_REG_EDX,status);u.reg_write(UC_X86_REG_EAX,0)
            u.reg_write(UC_X86_REG_EDI,0x100a000);u.mem_write(0x100a000,b'Name\0')
            u.reg_write(UC_X86_REG_ECX,0xffffffff)
            run(u,0x42d4f8,(0x42d4fd,))
            wanted=status | (0x40 if status in (0,3) else 0)
            assert u.mem_read(packet+5,1)==bytes([wanted])
            assert u.reg_read(UC_X86_REG_ECX)==0xfffffffa
    print('PASS encoder preserves native string scan and marks eligible slots including host')

    for multiplayer in (False,True):
        u=fresh(False);role(u,2);u.mem_write(0x5173e4,dword(int(multiplayer)))
        run(u,0x442060,(0x442068,))
        assert u.mem_read(ACTIVE,1)==bytes([int(multiplayer)])
        assert u.mem_read(ROLE+2,1)==bytes([int(multiplayer)])
        assert u.reg_read(UC_X86_REG_ESP)==STACK-16
    u=fresh();role(u,2)
    run(u,0x42d5e8,(0x42d5ee,))
    assert u.mem_read(ROLE,9)==bytes(9)
    print('PASS new-room reset and offline isolation')

    for slot in range(8):
        u=fresh(False);role(u,2)
        u.reg_write(UC_X86_REG_ECX,8-slot)
        u.reg_write(UC_X86_REG_EAX,0x49b046+1124*slot)
        u.mem_write(0x49b02c+1124*slot,dword(321))
        assert run(u,0x42dc25,(0x42dc2a,0x42dc38))==(0x42dc38 if slot==2 else 0x42dc2a)
    print('PASS minimum-player count excludes observers')

    # Exercise the WHOLE native counting loop and final admission decision.
    # v1.10.0 incorrectly retained native (humans>=2 OR computers>=3), which
    # rejects the requested A human + B observer + C computer configuration.
    start_cases=0
    for humans in range(9):
        for computers in range(9-humans):
            for observers in range(9-humans-computers):
                u=fresh(False)
                for slot in range(8):
                    state=1;dpid=0
                    if slot<humans:state=3;dpid=100+slot
                    elif slot<humans+observers:
                        state=3;dpid=100+slot;role(u,slot)
                    elif slot<humans+observers+computers:state=2
                    u.mem_write(0x49b046+1124*slot,bytes([state]))
                    u.mem_write(0x49b02c+1124*slot,dword(dpid))
                end=run(u,0x42dc17,(0x42dc50,0x42de24))
                assert (end==0x42dc50)==(humans+computers>=2), ('start composition',humans,computers,observers)
                start_cases+=1
    print('PASS complete native start decision:',start_cases,'compositions; two combatants required, including computer vs computer, observers excluded')

    u=fresh();role(u,2)
    for slot in range(3):u.mem_write(0x49b046+1124*slot,b'\x03')
    u.reg_write(UC_X86_REG_EAX,1124);u.reg_write(UC_X86_REG_EDX,2)
    run(u,0x43b77b,(0x43b782,))
    assert bytes(u.mem_read(0x49b04a+1124,2))==word(6)
    assert u.reg_read(UC_X86_REG_EAX)==1124 and u.reg_read(UC_X86_REG_EDX)==2
    print('PASS diplomacy changes cannot revoke observer sharing')

    # Native room UI emulation. Intercept only SendDlgItemMessageA, not game logic.
    u=fresh(False);u.mem_write(0x45f240,dword(API));u.mem_write(API,b'\xc2\x14\x00')
    u.mem_write(0x613078,dword(1))
    combos={i:[] for i in range(0x4b1,0x4b9)};selected={i:-1 for i in combos}
    def string(address):
        data=bytearray()
        for i in range(128):
            value=u.mem_read(address+i,1)[0]
            if value==0:return bytes(data)
            data.append(value)
        raise AssertionError('unterminated UI label')
    def api(emu,address,size,_):
        if address!=API:return
        esp=emu.reg_read(UC_X86_REG_ESP)
        hwnd,cid,msg,w,l=struct.unpack('<5I',emu.mem_read(esp+4,20))
        items=combos[cid];result=0
        if msg==0x143:items.append(string(l));result=len(items)-1
        elif msg==0x14a:items.insert(w,string(l));result=w
        elif msg==0x144:items.pop(w);selected[cid]=-1;result=len(items)
        elif msg==0x146:result=len(items)
        elif msg==0x147:result=selected[cid]
        elif msg==0x14e:selected[cid]=w;result=w
        else:raise AssertionError(hex(msg))
        emu.reg_write(UC_X86_REG_EAX,result & 0xffffffff)
    h=u.hook_add(UC_HOOK_CODE,api)
    def ui(cid,msg,w=0,l=0,caller=STOP):
        u.reg_write(UC_X86_REG_ESP,STACK)
        u.mem_write(STACK,dword(caller)+struct.pack('<5I',1,cid,msg,w,l))
        before=regs(u)
        run(u,ROUTINES['room_ui'],(caller,))
        assert u.reg_read(UC_X86_REG_ESP)==STACK+24
        assert regs(u)==before
        return u.reg_read(UC_X86_REG_EAX)
    for cid in combos:
        for label in (0x464928,0x46484c,0x464844):ui(cid,0x143,l=label)
        assert len(combos[cid])==5
    cid=0x4b3;selected[cid]=4
    # Host may choose observer, but never convert its connected slot to AI/open.
    u.mem_write(0x49b02c,dword(77))
    for option in (4,3,0,1,2,4):
        selected[0x4b1]=option
        assert ui(0x4b1,0x147,caller=0x42da9a)==3
        assert u.mem_read(ROLE,1)==bytes([int(option==4)])
        assert u.mem_read(0x49b02c,4)==dword(77)
    u.mem_write(ROLE,b'\0')
    assert ui(cid,0x147,caller=0x42da9a)==0
    assert u.mem_read(ROLE+2,1)==b'\x01'
    ui(cid,0x14e,w=0);assert selected[cid]==4
    # Joined observer: name replacement must not shift observer index 4.
    u.mem_write(0x49b02c+2248,dword(123))
    u.mem_write(0x49b030+2248,b'ObserverPC\0')
    for _ in range(3):
        assert ui(cid,0x147)==0
        ui(cid,0x143,l=0x49b030+2248)
        ui(cid,0x14e,w=3)
        assert len(combos[cid])==5 and selected[cid]==4
        assert combos[cid][3]==b'ObserverPC'
        assert b'ObserverPC' in combos[cid][4]
    assert ui(cid,0x147,caller=0x42da9a)==3
    selected[cid]=3
    assert ui(cid,0x147,caller=0x42da9a)==3
    assert u.mem_read(ROLE+2,1)==b'\0'
    ui(cid,0x144,w=3);assert len(combos[cid])==5
    print('PASS room UI: five stable indices, vacant/occupied observer, repeated snapshots, role reversal, leave')

    # Run the original snapshot handler through to its normal packet-complete
    # boundary, including native player/name writes and our Win32 wrapper.
    # APIs unrelated to combo behavior are deterministic no-op stubs here.
    u.mem_write(0x45f238,dword(API+16));u.mem_write(API+16,b'\xb8\x01\0\0\0\xc2\x08\0')
    u.mem_write(0x45f260,dword(API+32));u.mem_write(API+32,b'\xb8\x01\0\0\0\xc2\x08\0')
    u.mem_write(0x416300,b'\xc3');u.mem_write(0x44c3f0,b'\xc3')
    u.mem_write(0x613078,dword(0));u.mem_write(0x61307c,dword(123))
    for race_control in range(0x4bb,0x4c3):
        combos[race_control]=[b'Race0',b'Race1',b'Race2',b'Random']
        selected[race_control]=3
    for slot,status in ((slot,status) for slot in (0,2) for status in (0x43,3,0x43)):
        packet=bytearray(38);packet[:4]=dword(0x3100)
        packet[9:13]=dword(123);packet[13]=slot;packet[14]=status;packet[15]=1
        packet[16:27]=b'ObserverPC\0'
        u.mem_write(0x1009000,bytes(packet))
        u.reg_write(UC_X86_REG_ESP,STACK);u.reg_write(UC_X86_REG_EBP,0x1009000)
        u.reg_write(UC_X86_REG_EDX,0);u.reg_write(UC_X86_REG_EBX,1)
        run(u,0x42ccec,(0x42d2e0,))
        assert u.mem_read(0x49b046+slot*1124,1)==b'\x03'
        assert u.mem_read(0x49b02c+slot*1124,4)==dword(123)
        assert u.mem_read(0x49b030+slot*1124,11)==b'ObserverPC\0'
        assert u.mem_read(0x59ee52,1)==bytes([slot])
        assert selected[0x4b1+slot]==(4 if status==0x43 else 3)
        assert u.mem_read(0x1009000,38)==bytes(packet)
    u.hook_del(h)
    print('PASS full original snapshot handler: connection/name/local slot, observer-player transitions, UI and wire preservation')

    expected_guid=uuid.UUID('62046975-3128-4fd1-91b4-240b14bb2190').bytes_le
    if image[0x39770]==0xe9:
        import hashlib
        selection=struct.unpack_from('<H',image,0x44556)[0]==36
        profile='sync-safety-4;latency=True;selection36='+str(selection)+';rice=True;observer2=True'
        expected_guid=hashlib.sha256(profile.encode()).digest()[:16]
    assert image[0x5f670:0x5f680]==expected_guid
    print('PASS isolated DirectPlay app GUID; component checks:',path)

for path in sys.argv[1:]:emulate(path)
