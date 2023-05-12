#! /usr/bin/python3

from flags import parity, flags_add_sub_cp16
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

def emit_test(v1, v2, carry):
    global fh
    global n_tests

    if fh == None:
        file_name = f'adc16_{n_tests}.asm'
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

    label = f'test_{v1:02x}_{v2:02x}_{carry}'

    fh.write(f'{label}:\n')

    # reset flags
    fh.write(f'\txor ax,ax\n')
    fh.write(f'\tpush ax\n')
    fh.write(f'\tpopf\n')

    (check_val, flags) = flags_add_sub_cp16(False, True if carry > 0 else False, v1, v2)

    # verify value
    fh.write(f'\tmov ax,#${v1:02x}\n')
    fh.write(f'\tmov bx,#${v2:02x}\n')
    fh.write(f'\tmov cx,#${check_val:02x}\n')
    
    if carry:
        fh.write('\tstc\n')

    else:
        fh.write('\tclc\n')

    # do test
    fh.write(f'\tadc ax,bx\n')

    # keep flags
    fh.write(f'\tpushf\n')

    fh.write(f'\tcmp ax,cx\n')
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

for carry in range(0, 2):
    emit_test(256, 256, carry)
    emit_test(255, 256, carry)
    emit_test(256, 255, carry)
    emit_test(256 + 15, 256 + 15, carry)
    emit_test(256 + 15, 256 + 16, carry)
    emit_test(256 + 16, 256 + 16, carry)
    emit_test(65535, 65535, carry)

emit_tail()
fh.close()
