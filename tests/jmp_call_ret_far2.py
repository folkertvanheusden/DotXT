#! /usr/bin/python3

import sys

p = sys.argv[1]

fh = open(p + '/' + 'jmp_call_ret_far2_A.asm', 'w')

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
; JMP FAR
test_004:
    mov si,#$0004
    DB $9A ; opcode for call far
    DW $0  ; offset
    DW $1000 ; segment
    mov ax,#$a5ee
    mov si,ax
    hlt
''')

fh.close()

fh = open(p + '/' + 'jmp_call_ret_far2_B.asm', 'w')
fh.write('\tretf\n')
fh.write('\thlt\n')
fh.close()
