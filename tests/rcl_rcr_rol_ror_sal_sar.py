#! /usr/bin/python3

from flags import parity, flags_rcl, flags_rcr, flags_rol, flags_ror, flags_sal, flags_sar, flags_shr
from values_16b import b16_values
import sys

p = sys.argv[1]

fh = None

n = 0

def emit_test(width, v1, shift, carry, instr):
    global fh
    global n

    if (n % 512) == 0:
        if fh != None:
            # to let emulator know all was fine
            fh.write('\tmov ax,#$a5ee\n')
            fh.write('\tmov si,ax\n')
            fh.write('\thlt\n')

            fh.close()

        fh = None

    if fh == None:
        file_name = f'rcl_rcr_rol_ror_sal_sar{v1:x}_{n}.asm'

        fh = open(p + '/' + file_name, 'w')

        fh.write('\torg $800\n')
        fh.write('\n')

        fh.write('\txor ax,ax\n')
        fh.write('\tmov si,ax\n')
        fh.write('\n')
        fh.write('\tmov ss,ax\n')  # set stack segment to 0
        fh.write('\tmov ax,#$800\n')  # set stack pointer
        fh.write('\tmov sp,ax\n')  # set stack pointer

    n += 1

    label = f'test_{n}_{v1:x}_{carry}_{shift:02x}_{instr}' if shift != None else f'test_{n}_{v1:x}_{carry}__{instr}'

    fh.write(f'{label}:\n')
    fh.write(f'\tmov dx,#${n:04x}\n')

    target_name = 'al' if width == 8 else 'ax'

    # reset flags
    fh.write(f'\txor ax,ax\n')
    fh.write(f'\tpush ax\n')
    fh.write(f'\tpopf\n')

    # verify value
    fh.write(f'\tmov {target_name},#${v1:02x}\n')

    if shift != None:
        fh.write(f'\tmov cl,#${shift:02x}\n')
        shift_reg = 'cl'
        is_1 = False

    else:
        shift_reg = '1'
        shift = 1
        is_1 = True

    if carry:
        fh.write('\tstc\n')

    else:
        fh.write('\tclc\n')
    
    # do test
    if instr == 0:
        fh.write(f'\trcl {target_name},{shift_reg}\n')
        (check_val, flags, flags_mask) = flags_rcl(v1, shift, carry, width, is_1)

    elif instr == 1:
        fh.write(f'\trcr {target_name},{shift_reg}\n')
        (check_val, flags, flags_mask) = flags_rcr(v1, shift, carry, width, is_1)

    elif instr == 2:
        fh.write(f'\trol {target_name},{shift_reg}\n')
        (check_val, flags, flags_mask) = flags_rol(v1, shift, carry, width, is_1)

    elif instr == 3:
        fh.write(f'\tror {target_name},{shift_reg}\n')
        (check_val, flags, flags_mask) = flags_ror(v1, shift, carry, width, is_1)

    elif instr == 4:
        fh.write(f'\tsal {target_name},{shift_reg}\n')
        (check_val, flags, flags_mask) = flags_sal(v1, shift, carry, width, is_1)

    elif instr == 5:
        fh.write(f'\tsar {target_name},{shift_reg}\n')
        (check_val, flags, flags_mask) = flags_sar(v1, shift, carry, width, is_1)

    elif instr == 6:
        fh.write(f'\tshr {target_name},{shift_reg}\n')
        (check_val, flags, flags_mask) = flags_shr(v1, shift, carry, width, is_1)

    else:
        sys.exit(2)

    # keep flags
    fh.write(f'\tpushf\n')

    fh.write(f'\tcmp {target_name},#${check_val:02x}\n')
    fh.write(f'\tjz ok_{label}\n')

    fh.write(f'\thlt\n')

    fh.write(f'ok_{label}:\n')

    # verify flags
    fh.write(f'\tpop ax\n')
    if flags_mask != 0xffff:
        fh.write(f'\tand ax,#${flags_mask:04x}\n')
    fh.write(f'\tcmp ax,#${flags:04x}\n')
    fh.write(f'\tjz next_{label}\n')
    fh.write(f'\thlt\n')

    fh.write(f'next_{label}:\n')
    fh.write('\n')

# 8b
for al in range(0, 256):
    for carry in (False, True):
        for instr in range(0, 7):
            for shift in range(0, 9):
                emit_test(8, al, shift, carry, instr)

            emit_test(8, al, None, carry, instr)

# 16b
for v1 in b16_values:
    for carry in (False, True):
        for instr in range(0, 7):
            for shift in range(0, 17):
                emit_test(16, v1, shift, carry, instr)

            emit_test(16, v1, None, carry, instr)

fh.write('\tmov ax,#$a5ee\n')
fh.write('\tmov si,ax\n')
fh.write('\thlt\n')
fh.close()
