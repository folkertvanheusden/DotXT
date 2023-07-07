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
    mov si,#$0001
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
    mov si,#$0002
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
    jmp test_003_go

test_003:
    dw $7766
test_003_go:
    mov si,#$0003
    cld
    mov ax,#test_003
    mov si,ax
    lodsw
    cmp ax,#$7766
    beq test_003_ok1
    hlt
test_003_ok1:
    mov ax,si
    cmp ax,#test_003_go
    beq test_003_ok2
    hlt
test_003_ok2:

test_004:
    mov si,#$0004
    mov ax,#$9911
    not ax
    cmp ax,#$66ee
    beq test_004_ok
    hlt
test_004_ok:
    jmp test_005_go

test_005:
    dw $1199
test_005_go:
    mov si,#$0005
    not [test_005]
    mov ax,[test_005]
    cmp ax,#$ee66
    beq test_005_ok
    hlt
test_005_ok:

test_006:
    mov si,#$0006
    ; make sure not to enable interrupts
    mov ax,#$fdff
    push ax
    popf
    lahf
    cmp ah,#$d7
    beq test_006_ok1
    hlt
test_006_ok1:
    cmp al,#$ff
    beq test_006_ok2
    hlt
test_006_ok2:
    mov ax,#$0000
    push ax
    popf
    lahf
    cmp ah,#$02
    beq test_006_ok3
    hlt
test_006_ok3:
    cmp al,#$00
    beq test_006_ok4
    hlt
test_006_ok4:

test_007:
    mov si,#$0007
    mov ax,#$fdff
    push ax
    popf
    mov al,#$00
    sahf
    pushf
    pop ax
    cmp al,#$d7
    beq test_007_ok1
    hlt
test_007_ok1:

test_008:
    mov si,#$0008
    ; clear flags
    xor ax,ax
    push ax
    popf
    ; enable interrupts
    sti
    pushf
    pop ax
    and ax,#$200
    cmp ax,#512
    beq test_008_ok1
    hlt
test_008_ok1:
    ; disable interrupts
    cli
    pushf
    pop bx
    and bx,#$200
    cmp bx,#0
    beq test_008_ok2
    hlt
test_008_ok2:
    jmp test_009_go

test_009:
    dw $aaaa
test_009_go:
    mov si,#$0009
    xor ax,ax
    mov es,ax
    mov di,#test_009
    mov ax,#$bbbb
    std
    scasw
    jge test_009_ok1
    hlt
test_009_ok1:
    add di,#2
    cmp di,#test_009
    beq test_009_ok2
    hlt
test_009_ok2:

test_00a:
    dw $aa
test_00a_go:
    mov si,#$000a
    xor ax,ax
    mov es,ax
    mov di,#test_00a
    mov al,#$bb
    scasb
    jge test_00a_ok1
    hlt
test_00a_ok1:
    inc di
    cmp di,#test_00a
    beq test_00a_ok2
    hlt
test_00a_ok2:
    jmp test_00b_go

test_00b:
    db $a
    db $b
    db $c
    db $d
    db $e
    db $f
    db $1
    db $2
    db $3
test_00b_go:
    mov si,#$000b
    xor ax,ax
    mov ds,ax
    mov bx,#test_00b
    mov al,#$05
    xlatb
    cmp al,#$0f
    beq test_00b_ok1
    hlt
test_00b_ok1:

finish:
''')

fh.write('\tmov ax,#$a5ee\n')
fh.write('\tmov si,ax\n')  # set si to 'finished successfully'
fh.write('\thlt\n')
fh.close()
