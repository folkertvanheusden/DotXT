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
    mov ax,#$1234
    cmp ax,#$1234
    jz test_001_ok
    hlt
test_001_ok:

test_002:
    mov ax,[word_read_001]
    cmp ax,#$4567
    jz test_002_ok
    hlt

test_002_ok:

test_003:
    xor ax,ax
    lea ax,word_read_002
    mov di,ax
    mov bx,#$2
    mov ax,[bx+di]
    cmp ax,#$5926
    jz test_003_ok
    hlt

test_003_ok:

test_004:
    lea bx,[word_write_001b]
    mov bp,bx
    mov word [bp - $02],#$4455
    mov ax,[word_write_001]
    cmp ax,#$4455
    jz test_004_ok
    hlt

test_004_ok:

    jmp finish

word_read_001:
    dw $4567
    dw $abcd

word_read_002:
    dw $3141
    dw $5926

word_write_001:
    dw $1111
word_write_001b:

finish:
''')

fh.write('\tmov ax,#$a5ee\n')
fh.write('\tmov si,ax\n')  # set si to 'finished successfully'
fh.write('\thlt\n')
fh.close()
