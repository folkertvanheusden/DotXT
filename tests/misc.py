#! /usr/bin/python3

import sys

p = sys.argv[1]

fh = open(p + '/' + 'misc.asm', 'w')

fh.write('\torg $800\n')
fh.write('\n')

fh.write('\txor ax,ax\n')
fh.write('\tmov si,ax\n')  # set si to 'still running'
fh.write('\n')
fh.write('\tmov ss,ax\n')  # set stack segment to 0
fh.write('\tmov ax,#$800\n')  # set stack pointer
fh.write('\tmov sp,ax\n')  # set stack pointer


fh.write(
'''
; NOT
test_001:
    mov si,#$0001
    xor ax,ax
    mov bx,#$1234
    not bx
    cmp bx,#$EDCB
    jz test_001a_ok
    hlt
test_001a_ok:

    cmp ax,#$0000

    jz test_001b_ok
    hlt
test_001b_ok:

; MUL
test_002:
    mov si,#$0002
    xor ax,ax
    mov al,#$7f
    mov cl,#$fe
    mul cl
    cmp ax,#$7e02
    jz test_002_ok
    hlt
test_002_ok:
; TODO: test flags

; MUL 16b
test_003:
    mov si,#$0003
    mov ax,#$1234
    mov cx,#$aa55
    mul cx
    cmp ax,#$9344
    jz test_003a_ok
    hlt
test_003a_ok:
    cmp dx,#$0c1c
    jz test_003b_ok
    hlt
test_003b_ok:
; TODO: test flags

; DIV
test_004:
    mov si,#$0004
	mov ax,#0x4321
	mov dx,#0x8001
	mov cx,#0x8fff
	div cx
    cmp ax,#$e392
    jz test_004a_ok
    hlt
test_004a_ok:
    cmp dx,#$06b3
    jz test_004b_ok
    hlt
test_004b_ok:
; NO(!) flags altered

; XCHG
test_005:
    mov si,#$0005
    pushf
	mov ax,#0x4321
	mov dx,#0x8001
    xchg ax,dx
    cmp ax,#0x8001
    jz test_005a_ok
    hlt
test_005a_ok:
    cmp dx,#0x4321
    jz test_005b_ok
    hlt
test_005b_ok:
    pushf
    pop bx
    pop ax
    cmp ax,bx
    jz test_005c_ok
    hlt
test_005c_ok:

; CALL & RET
test_006:
    mov si,#$0006
    pushf
    pop bx
    mov ax,sp
    call test_006_sub_a
    jp test_006_cont
test_006_sub_a:
    pushf
    cmp ax,sp
    jne test_006_sub_a_ok
    hlt
test_006_sub_a_ok:
    pop cx
    cmp bx,cx
    jz test_006_sub_b_ok
    hlt
test_006_sub_b_ok:
    push bx
    popf
    ret
    hlt
test_006_cont:
    cmp ax,sp
    jz test_006_b_ok
    hlt
test_006_b_ok:
    pushf
    pop cx
    cmp bx,cx
    jz test_006_c_ok
    hlt
test_006_c_ok:

test_007:
    mov si,#$0007
    mov bx,#$1234
    xor bx,bx
    cmp bx,#$0000
    jz test_007a_ok
    hlt
test_007a_ok:

; DIV (2)
test_008:
    mov si,#$0008
    jmp test_008_skip
test_008_word:
    dw 0
    nop
    nop
    nop
test_008_skip:
	mov ax,#0x4321
	mov dx,#0x8001
	mov [test_008_word],#0x8fff
	div [test_008_word]
    cmp ax,#$e392
    jz test_008a_ok
    hlt
test_008a_ok:

; MUL
test_009:
    mov si,#$0009
    jmp test_009_skip
test_009_byte:
    db 0
    nop
    nop
    nop
test_009_skip:
    xor ax,ax
    mov al,#$7f
    mov [test_009_byte],#$fe
    mul [test_009_byte]
    cmp ax,#$7e02
    jz test_009_ok
    hlt
test_009_ok:
; TODO: test flags

finish:
''')

fh.write('\tmov ax,#$a5ee\n')
fh.write('\tmov si,ax\n')  # set si to 'finished successfully'
fh.write('\thlt\n')
fh.close()
