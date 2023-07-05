#! /usr/bin/python3

import sys

p = sys.argv[1]

fh = open(p + '/' + 'misc3.asm', 'w')

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
    mov si,#$0001
    jmp test_001_do

test_001_source:
    dw $1234
    dw $5678
    dw $9abc
    dw $def0

test_001_dest:
    dw 0
    dw 0
    dw 0
    dw 0

test_001_do:
    mov ax,#test_001_source
    add ax,#8
    mov si,ax

    mov ax,#test_001_dest
    add ax,#8
    mov di,ax

    std
    mov cx,#3
    cmpsw
    ble test_001_ok1
    hlt

test_001_ok1:
    mov bx,si
    mov ax,#test_001_source
    sub bx,ax
    cmp bx,#6
    beq test_001_ok2
    hlt

test_001_ok2:
    mov bx,di
    mov ax,#test_001_dest
    sub bx,ax
    cmp bx,#6
    beq test_001_ok3
    hlt

test_001_ok3:

test_002:
    mov si,#$0002
    jmp test_002_do

test_002_source:
    dw $1234
    dw $5678
    dw $9abc
    dw $def0

test_002_dest:
    dw 0
    dw 0
    dw 0
    dw 0

test_002_do:
    mov ax,#test_002_source
    mov si,ax

    mov ax,#test_002_dest
    mov di,ax

    cld
    mov cx,#4

    rep
    movsw

    cmp si,ax
    beq test_002_si_ok
    hlt

test_002_si_ok:
    mov ax,#test_002_do
    cmp di,ax
    beq test_002_di_ok
    hlt

test_002_di_ok:
    mov ax,[test_002_dest + 6]
    cmp [test_002_source + 6],ax
    beq test_002_xfer_ok
    hlt

test_002_xfer_ok:

finish:
''')

fh.write('\tmov ax,#$a5ee\n')
fh.write('\tmov si,ax\n')  # set si to 'finished successfully'
fh.write('\thlt\n')
fh.close()
