; Production-only replacement for CALL 4124D9 -> 426A00.
; ESI = producing building. Existing 3 cdecl arguments stay on caller stack.
; First ring only; native placement handles no rally / blocked ring / bad data.
; Local offsets: left/right/top/bottom, rallyX/Y, bestD/X/Y, maxX/Y.
pushfd
pushad
sub esp, 44
mov ebp, esp
cmp byte ptr [esi+0xca], 21
jne fallback
mov edi, dword ptr [0x477ae4]
test edi, edi
jz fallback
movzx eax, word ptr [edi+2]
cmp eax, 16
jb fallback
cmp eax, 232
ja fallback
sub eax, 4
mov [ebp+36], eax
movzx eax, word ptr [edi+4]
cmp eax, 16
jb fallback
cmp eax, 232
ja fallback
sub eax, 4
mov [ebp+40], eax
movzx eax, word ptr [esi+0x18]
cmp eax, 4
jb fallback
cmp eax, [ebp+36]
jae fallback
mov [ebp+16], eax
movzx eax, word ptr [esi+0x1a]
cmp eax, 4
jb fallback
cmp eax, [ebp+40]
jae fallback
mov [ebp+20], eax
; Shape index is the low byte of the original second argument.
movzx edi, byte ptr [ebp+88]
shl edi, 4
cmp dword ptr [0x47799c], 0
je fallback
add edi, dword ptr [0x47799c]
movsx ecx, byte ptr [edi+4]
movsx edx, byte ptr [edi+5]
movsx eax, byte ptr [edi+3]
sub edx, eax
cmp ecx, 1
jl fallback
cmp ecx, 32
jg fallback
cmp edx, 1
jl fallback
cmp edx, 32
jg fallback
movzx eax, word ptr [esi+0x106]
add ecx, eax
dec eax
mov [ebp], eax
mov [ebp+4], ecx
movzx eax, word ptr [esi+0x108]
add edx, eax
dec eax
mov [ebp+8], eax
mov [ebp+12], edx
; A rally inside the building is not a directional exit request.
mov eax, [ebp+16]
cmp eax, [ebp]
jle scan
cmp eax, [ebp+4]
jge scan
mov eax, [ebp+20]
cmp eax, [ebp+8]
jle scan
cmp eax, [ebp+12]
jge scan
jmp fallback
scan:
mov dword ptr [ebp+24], 0x7fffffff
mov edx, [ebp+8]
row:
mov ecx, [ebp]
column:
cmp ecx, 4
jl next
cmp ecx, [ebp+36]
jge next
cmp edx, 4
jl next
cmp edx, [ebp+40]
jge next
cmp ecx, [ebp]
je check
cmp ecx, [ebp+4]
je check
cmp edx, [ebp+8]
je check
cmp edx, [ebp+12]
jne next
check:
imul eax, edx, 232
add eax, ecx
cmp word ptr [eax*2+0x59ee5c], 0
jne next
cmp byte ptr [eax+0x5da07c], 16
jg next
mov eax, ecx
sub eax, [ebp+16]
imul eax, eax
mov edi, edx
sub edi, [ebp+20]
imul edi, edi
add eax, edi
cmp eax, [ebp+24]
jge next
mov [ebp+24], eax
mov [ebp+28], ecx
mov [ebp+32], edx
next:
inc ecx
cmp ecx, [ebp+4]
jle column
inc edx
cmp edx, [ebp+12]
jle row
cmp dword ptr [ebp+24], 0x7fffffff
je fallback
mov eax, [ebp+28]
mov word ptr [0x47821c], ax
mov eax, [ebp+32]
mov word ptr [0x47821e], ax
mov dword ptr [ebp+72], 1
add esp, 44
popad
popfd
ret
fallback:
add esp, 44
popad
popfd
jmp 0x426a00
