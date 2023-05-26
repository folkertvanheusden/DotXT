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

finish:
''')

fh.write('\tmov ax,#$a5ee\n')
fh.write('\tmov si,ax\n')  # set si to 'finished successfully'
fh.write('\thlt\n')
fh.close()
