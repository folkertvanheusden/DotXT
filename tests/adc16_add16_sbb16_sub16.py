#! /usr/bin/python3

from flags import parity, flags_add_sub_cp16
from values_16b import get_pairs_16b
import sys

p = sys.argv[1]

fh = None
n_tests = 0

def emit_tail():
    global fh

    # to let emulator know all was fine
    fh.write('\tmov ax,#$a5ee\n')
    fh.write('\tmov si,ax\n')
    fh.write('\thlt\n')

def emit_test(instr, v1, v2, carry, use_val, target):
    global fh
    global n_tests

    if fh == None:
        file_name = f'adc16_add16_{n_tests}.asm'
        fh = open(p + '/' + file_name, 'w')

        fh.write('\torg $800\n')
        fh.write('\n')

        fh.write('\txor ax,ax\n')
        fh.write('\tmov si,ax\n')
        fh.write('\n')
        fh.write('\tmov ss,ax\n')  # set stack segment to 0
        fh.write('\tmov ax,#$800\n')  # set stack pointer
        fh.write('\tmov sp,ax\n')  # set stack pointer

    # test itself

    label = f't_{instr}_{v1:04x}_{v2:04x}_{carry}_{use_val}_{target}'

    fh.write(f'{label}:\n')

    # reset flags
    fh.write(f'\txor ax,ax\n')
    fh.write(f'\tpush ax\n')
    fh.write(f'\tpopf\n')

    (check_val, flags) = flags_add_sub_cp16(instr >= 2, True if carry and (instr == 0 or instr == 2) else False, v1, v2)

    target_use_name = 'ax' if target else 'dx'

    # verify value
    fh.write(f'\tmov {target_use_name},#${v1:04x}\n')
    if not use_val:
        fh.write(f'\tmov bx,#${v2:04x}\n')
    fh.write(f'\tmov cx,#${check_val:04x}\n')
    
    if carry:
        fh.write('\tstc\n')

    else:
        fh.write('\tclc\n')

    from_use_name = 'bx' if not use_val else f'#${v2:04x}'

    # do test
    if instr == 0:
        fh.write(f'\tadc {target_use_name},{from_use_name}\n')

    elif instr == 1:
        fh.write(f'\tadd {target_use_name},{from_use_name}\n')

    elif instr == 2:
        fh.write(f'\tsbb {target_use_name},{from_use_name}\n')

    elif instr == 3:
        fh.write(f'\tsub {target_use_name},{from_use_name}\n')

    # keep flags
    fh.write(f'\tpushf\n')

    fh.write(f'\tcmp {target_use_name},cx\n')
    fh.write(f'\tjz ok_{label}\n')

    fh.write(f'\thlt\n')

    fh.write(f'ok_{label}:\n')

    # verify flags
    fh.write(f'\tpop ax\n')
    fh.write(f'\tcmp ax,#${flags:04x}\n')
    fh.write(f'\tjz next_{label}\n')
    fh.write(f'\thlt\n')

    # TODO: verify flags
    fh.write(f'next_{label}:\n')
    fh.write('\n')

    n_tests += 1

    if (n_tests % 512) == 0:
        emit_tail()

        fh.close()
        fh = None

for instr in range(0, 4):
    for target in (False, True):
        for carry in (False, True):
            for use_value in (False, True):
                for pair in get_pairs_16b():
                    emit_test(instr, pair[0], pair[1], carry, use_value, target)

emit_tail()
fh.close()
