#! /usr/bin/python3

import sys

p = sys.argv[1]

fh = open(p + '/' + 'misc2.asm', 'w')

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
test_019:
    mov si,#$0019
    mov ax,#test_019_continue0
    push ax
    ret
    hlt
    org $2000
test_019_continue0:
    mov ax,#$300
    mov cs,ax
    org $3000
test_019_continue1:
    nop
    nop
    nop
    nop
    nop
    nop
    nop
    ;
    nop

finish:
''')

fh.write('\tmov ax,#$a5ee\n')
fh.write('\tmov si,ax\n')  # set si to 'finished successfully'
fh.write('\thlt\n')
fh.close()
