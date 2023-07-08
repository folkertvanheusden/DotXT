#! /usr/bin/python3

from flags import parity, flags_add_sub_cp
import sys

p = sys.argv[1]

fh = None

n = 0

for al in range(0, 256):
    if (n % 512) == 0:
        if fh != None:
            # to let emulator know all was fine
            fh.write('\tmov ax,#$a5ee\n')
            fh.write('\tmov si,ax\n')
            fh.write('\thlt\n')

            fh.close()

        fh = None

    if fh == None:
        file_name = f'neg_{n}.asm'

        fh = open(p + '/' + file_name, 'w')

        fh.write('\torg $800\n')
        fh.write('\n')

        fh.write('\txor ax,ax\n')
        fh.write('\tmov si,ax\n')
        fh.write('\n')
        fh.write('\tmov ss,ax\n')  # set stack segment to 0
        fh.write('\tmov ax,#$800\n')  # set stack pointer
        fh.write('\tmov sp,ax\n')  # set stack pointer

    label = f'neg_{al:02x}'

    fh.write(f'{label}:\n')

    n += 1

    # reset flags
    fh.write(f'\txor ax,ax\n')
    fh.write(f'\tpush ax\n')
    fh.write(f'\tpopf\n')

    (check_val, flags) = flags_add_sub_cp(True, False, 0, al)

    neg_val = (-al) & 0xff

    flags &= ~1
    flags |= 1 if al != 0 else 0

    # verify value
    fh.write(f'\tmov al,#${al:02x}\n')
    fh.write('\tneg al\n')

    # keep flags
    fh.write(f'\tpushf\n')

    fh.write(f'\tcmp al,#${neg_val:02x}\n')
    fh.write(f'\tjz ok_{label}\n')

    fh.write(f'\thlt\n')

    fh.write(f'ok_{label}:\n')

    # verify flags
    fh.write(f'\tpop ax\n')
    fh.write(f'\tcmp ax,#${flags:04x}\n')
    fh.write(f'\tjz next_{label}\n')
    fh.write(f'\thlt\n')

    fh.write(f'next_{label}:\n')
    fh.write('\n')

fh.write('\tmov ax,#$a5ee\n')
fh.write('\tmov si,ax\n')
fh.write('\thlt\n')
fh.close()
