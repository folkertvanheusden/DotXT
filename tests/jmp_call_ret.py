#! /usr/bin/python3

import sys

p = sys.argv[1]

fh = open(p + '/' + 'jmp_call_ret.asm', 'w')

fh.write('\torg $800\n')
fh.write('\n')

fh.write('\txor ax,ax\n')
fh.write('\tmov si,ax\n')  # set si to 'still running'
fh.write('\n')
fh.write('LOC 0\n')
fh.write('\tmov ss,ax\n')  # set stack segment to 0
fh.write('\tmov ax,#$800\n')  # set stack pointer
fh.write('\tmov sp,ax\n')  # set stack pointer


fh.write(
'''
; JMP NEAR
test_001:
    mov si,#$0001
    jmp test_001a_ok
    hlt
test_001a_ok:

; CALL NEAR
test_002:
    mov si,#$0002
    call test_002_sub
    jmp test_002_ok
    hlt
test_002_sub:
    ret
    hlt
test_002_ok:
    jmp test_003

test_003_data:
    dw test_003_ok
test_003:
    mov si,#$0003
    jmp [test_003_data]
    hlt
test_003_ok:
    jmp test_004

test_004_data:
    dw test_004_sub
test_004:
    mov si,#$0004
    call [test_004_data]
    jmp test_004_ok
    hlt
test_004_sub:
    ret
    hlt
test_004_ok:

test_005:
    mov si,#$0004
    mov ax,sp
    push ax
    push ax
    push ax
    call test_005_sub
    jmp test_005_cont
test_005_sub:
    ret 6
test_005_cont:
    cmp ax,sp
    beq test_005_ok
    hlt
test_005_ok:

finish:
''')

fh.write('\tmov ax,#$a5ee\n')
fh.write('\tmov si,ax\n')  # set si to 'finished successfully'
fh.write('\thlt\n')
fh.close()
