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

; JMP FAR
test_003:
    mov si,#$0003
    jmpf test_003_ok_seg,test_003_ok
    hlt
    .space 70000
LOC 4
test_003_ok_seg:
test_003_ok:

; CALL FAR
test_004:
    mov si,#$0004
    callf test_004_sub_seg,test_004_sub
    jmpf test_004_sub_seg,test_004_ok
    hlt
    .space 70000
LOC 5
test_004_sub_seg:
test_004_sub:
    retf
    hlt
test_004_ok:

finish:
''')

fh.write('\tmov ax,#$a5ee\n')
fh.write('\tmov si,ax\n')  # set si to 'finished successfully'
fh.write('\thlt\n')
fh.close()
