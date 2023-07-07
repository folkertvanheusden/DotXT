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

; access of ds and ax, bx
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

; si & bx
test_007:
    mov si,#0007
    mov bx,si
    cmp bx,#0007
    jz test_007_ok
    hlt

test_007_ok:

; access label
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

; 8 bit
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

; reference + offset
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

test_00c:
    mov si,#$000c
    mov bx,#-2
    lea di,[word_write_001b]
    mov [bx+di],#8384
    mov ax,[bx+di]
    cmp ax,#8384
    jz test_00c_ok
    hlt

test_00c_ok:

test_00d:
    mov si,#$000d
    mov ax,#$8285
    mov es,ax
    mov [word_write_001],es
    mov ax,#$8a86
    mov es,ax
    mov es,[word_write_001]
    mov ax,es
    cmp ax,#$8285
    jz test_00d_ok
    hlt

test_00d_ok:

test_00e:
    mov si,#$000e
    lea di,[word_write_001]
    mov dx,#$02
    mov ax,#$4312
    mov es,ax
    mov [bx+di+0x02],es
    mov [word_write_001],#$9977
    mov es,[bx+di+0x02]
    mov ax,es
    cmp ax,#$9977
    jz test_00e_ok
    hlt

test_00e_ok:

test_00f:
; acces of 8b and check in 16b
    mov si,#$000f
    mov cx,#$1234
    mov cl,#$88
    cmp cx,#$1288
    jz test_00f_a_ok
    hlt
test_00f_a_ok:
    mov cx,#$1234
    mov ch,#$44
    cmp cx,#$4434
    jz test_00f_b_ok
    hlt
test_00f_b_ok:

test_010:
; check for .x -> .x
    mov si,#$0010
    mov ax,#$9988
    mov cx,#$1234
    mov cx,ax
    cmp cx,#$9988
    jz test_010_ok
    hlt
test_010_ok:

test_011:
    mov si,#$0011
    mov cx,#$2211
    mov [word_write_001],cx
    cmp [word_write_001],#$2211
    jz test_011_ok
    hlt
test_011_ok:

test_012:
    mov si,#$0012
    mov cl,#$93
    mov [word_write_001],cl
    cmp [word_write_001],#$2293
    jz test_012_ok
    hlt
test_012_ok:

test_013:
    mov si,#$0013
    mov [word_write_001],#$4312
    cmp [word_write_001],#$4312
    jz test_013_ok
    hlt
test_013_ok:

test_014:
    mov si,#$0014
    mov byte [word_write_001],#$43
    cmp byte [word_write_001],#$43
    jz test_014_ok
    hlt
test_014_ok:
    jmp test_015_go

test_015:
    dw 0
    dw 0
    dw 0
    dw $a5e5
    dw 0
 
test_015_go:
    mov si,#$0015
    mov ax,#test_015
    mov di,ax
    mov ax,#$0006
    mov bp,ax
    mov ax,[bp + di]
    cmp ax,#$a5e5
    beq test_015_ok
    hlt
test_015_ok:
    jmp test_016_go
 
test_016:
    dw $1332
test_016_go:
    mov si,#$0016
    mov ax,#$ff00
    mov al,[test_016]
    cmp ax,#$ff32
    beq test_016_ok
    hlt
test_016_ok:
 
test_017:
    dw $1332
test_017_go:
    mov si,#$0017
    mov al,#$ff
    mov [test_017],al
    mov ax,#$13ff
    cmp ax,[test_017]
    beq test_017_ok
    hlt
test_017_ok:
 
test_018:
    mov si,#$0018
    mov ax,#$ff12
    cmp ax,#$ff12
    beq test_018_ok1
    hlt
test_018_ok1:
    xor ax,ax
    mov bx,ax
    mov bl,#$12
    cmp bx,#$12
    beq test_018_ok2
    hlt
test_018_ok2:
    jmp test_019_go

test_019:
    dw $0
    dw $2
    dw $4
    dw $6
    dw $8
    dw $a
    dw $c
    dw $e
    dw $10
    dw $12
    dw $14
    dw $16
    dw $18
    dw $1a
 
test_019_go:
    mov si,#$0019
    mov ax,#test_019
    mov bp,ax
    mov ax,[bp + si]
    cmp ax,#$1a00
    beq test_019_ok
    hlt
test_019_ok:
    jmp test_01a_go

test_01a:
    dw $0
test_01a_go:
    mov si,#$001a
    ; increment after stos
    cld
    xor ax,ax
    mov es,ax
    mov di,#test_01a
    mov ax,#$8382
    stosw
    ; next statement is for debugging
    mov bx,[test_01a]
    cmp [test_01a],#$8382
    beq test_01a_ok1
    hlt
test_01a_ok1:
    sub di,#2
    cmp di,#test_01a
    beq test_01a_ok2
    hlt
test_01a_ok2:

finish:
''')

fh.write('\tmov ax,#$a5ee\n')
fh.write('\tmov si,ax\n')  # set si to 'finished successfully'
fh.write('\thlt\n')
fh.close()
