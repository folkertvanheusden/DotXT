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

finish:
''')

fh.write('\tmov ax,#$a5ee\n')
fh.write('\tmov si,ax\n')  # set si to 'finished successfully'
fh.write('\thlt\n')
fh.close()
