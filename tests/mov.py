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
; directly value into register
test_001:
    mov si,#0001
    mov ax,#$1234
    cmp ax,#$1234
    jz test_001_ok
    hlt
test_001_ok:

test_002:
; read from address pointer by
    mov si,#0002
    mov ax,[word_read_001]
    cmp ax,#$4567
    jz test_002_ok
    hlt

test_002_ok:

; lea
test_003:
    mov si,#0003
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
    mov si,#0004
    lea bx,[word_write_001b]
    mov bp,bx
    mov word [bp - $02],#$4455
    mov ax,[word_write_001]
    cmp ax,#$4455
    jz test_004_ok
    hlt

test_004_ok:

test_005:
    mov si,#0005
    lea bx,[word_write_001]
    mov [bx],#$3366
    mov ax,#$3366
    cmp ax,[bx]
    jz test_005_ok
    hlt

test_005_ok:

test_006:
    mov si,#0006
    mov cx,ds
    mov bx,#2277
    mov ds,bx
    mov ax,ds
    mov ds,cx
    cmp ax,#2277
    jz test_006_ok
    hlt

test_006_ok:

test_007:
    mov si,#0007
    mov bx,si
    cmp bx,#0007
    jz test_007_ok
    hlt

test_007_ok:

test_008:
    mov si,#0008
    mov [word_write_001],#$1188
    lea bx,[word_write_001]
    mov di,bx
    mov ax,[di]
    cmp ax,#$1188
    jz test_008_ok
    hlt

test_008_ok:

test_009:
    mov si,#0009
    mov ah,#$1d
    mov cl,ah
    cmp cl,#$1d
    jz test_009_ok
    hlt

test_009_ok:
    jmp test_continue

word_read_001:
    dw $4567
    dw $abcd

word_read_002:
    dw $3141
    dw $5926

word_write_001:
    dw $1111
word_write_001b:

test_continue:

test_00a:
    mov si,#$000a
    lea bx,[word_write_001b]
    mov si,bx
    mov word [si - $02],#$9977
    mov ax,word [si - $02]
    cmp ax,#$9977
    jz test_00a_ok
    hlt

test_00a_ok:

test_00b:
    mov si,#$000b
    mov [word_write_001],#$8844
    mov di,[word_write_001]
    cmp di,#$8844
    jz test_00b_ok
    hlt

test_00b_ok:

finish:
''')

fh.write('\tmov ax,#$a5ee\n')
fh.write('\tmov si,ax\n')  # set si to 'finished successfully'
fh.write('\thlt\n')
fh.close()
