#! /usr/bin/python3

import sys

p = sys.argv[1]

fh = open(p + '/' + 'push_pop.asm', 'w')

fh.write('\torg $800\n')
fh.write('\n')

fh.write('\txor ax,ax\n')
fh.write('\tmov si,ax\n')  # set si to 'still running'
fh.write('\n')
fh.write('\tmov ss,ax\n')  # set stack segment to 0
fh.write('\tmov ax,#$800\n')  # set stack pointer
fh.write('\tmov sp,ax\n')  # set stack pointer

fh.write('\tjmp skip\n')
fh.write('space:\n')
fh.write('\tdw 0\n')
fh.write('skip:\n')

some_value = 13
nr = 1

# TODO: DS, SP, CS, #....
for reg in 'AX', 'CX', 'DX', 'BX', 'BP', 'SI', 'DI', 'ES':
    # initialize test register
    some_value ^= nr * 131
    some_value &= 0xffff
    nr += 1

    fh.write(f'\tmov ax,#${some_value:04x}\n')
    if reg != 'AX':
        fh.write(f'\tmov {reg},ax\n')

    # do
    fh.write(f'\tpush {reg}\n')
    # verify that the push did not alter the register
    fh.write(f'\tmov ax,{reg}\n')
    fh.write(f'\tcmp ax,#${some_value:04x}\n')
    l0 = f'ok___{reg}'
    fh.write(f'\tjz {l0}\n')
    fh.write(f'\thlt\n')
    fh.write(f'{l0}:\n')

    # alter test-register
    fh.write(f'\tmov ax,#${(some_value ^ 0xffff) >> 1:04x}\n')
    if reg != 'AX':
        fh.write(f'\tmov {reg},ax\n')

    # stack contains expected value?
    fh.write(f'\tcmp $7fe,#${some_value:04x}\n')
    l1 = f'ok_a_{reg}'
    fh.write(f'\tjz {l1}\n')
    fh.write(f'\thlt\n')
    fh.write(f'{l1}:\n')

    # check if pop returns expected value
    fh.write(f'\tpop {reg}\n')
    if reg != 'AX':
        fh.write(f'\tmov ax,{reg}\n')
    fh.write(f'\tcmp ax,#${some_value:04x}\n')
    l2 = f'ok_b_{reg}'
    fh.write(f'\tjz {l2}\n')
    fh.write(f'\thlt\n')
    fh.write(f'{l2}:\n')

fh.write('\tjmp skip_dw_1\n')
fh.write('store_field:\n')
fh.write('\tdw 0\n')
fh.write('skip_dw_1:\n')
fh.write('\tmov ax,#$aa33\n')
fh.write('\tpush ax\n')
fh.write('\tpop [store_field]\n')
fh.write('\tmov ax,[store_field]\n')
fh.write('\tcmp ax,#$aa33\n')
fh.write('\tbeq test_ok\n')
fh.write('\thlt\n')
fh.write('test_ok:\n')

fh.write('''
    mov bx,#$123
    mov cx,#$0000
    mov ds,bx
    push ds
    mov ds,cx
    pop ds
    mov ax,ds
    mov ds,cx
    cmp ax,#$123
    beq ds_test_1
    hlt
ds_test_1:
''')

fh.write('\tmov ax,#$a5ee\n')
fh.write('\tmov si,ax\n')  # set si to 'finished successfully'
fh.write('\thlt\n')
fh.close()
