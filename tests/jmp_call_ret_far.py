#! /usr/bin/python3

import sys

p = sys.argv[1]

fh = open(p + '/' + 'jmp_call_ret_far_A.asm', 'w')

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
test_003:
    mov si,#$0003
    DB $EA ; opcode for jump far
    DW $0  ; offset
    DW $1000 ; segment
    hlt
''')

fh.close()

fh = open(p + '/' + 'jmp_call_ret_far_B.asm', 'w')
fh.write('\tmov ax,#$a5ee\n')
fh.write('\tmov si,ax\n')  # set si to 'finished successfully'
fh.write('\thlt\n')
fh.close()

#; CALL FAR
#test_004:
#    mov si,#$0004
#    callf test_004_sub_seg,test_004_sub
#    jmpf test_004_sub_seg,test_004_ok
#    hlt
#    .space 70000
#LOC 5
#test_004_sub_seg:
#test_004_sub:
#    retf
#    hlt
#test_004_ok:
