#! /usr/bin/python3

import sys

p = sys.argv[1]

fh = open(p + '/' + 'mov.asm', 'w')

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
test_001:
    xor ax,ax
    mov bx,#$1234
    not bx
    cmp bx,#$EDCB
    jz test_001_ok
    hlt
test_001_ok:

    cmp ax,#$0000

    jz test_001b_ok
    hlt
test_001b_ok:

finish:
''')

fh.write('\tmov ax,#$a5ee\n')
fh.write('\tmov si,ax\n')  # set si to 'finished successfully'
fh.write('\thlt\n')
fh.close()
