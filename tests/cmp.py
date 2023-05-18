#! /usr/bin/python3

from flags import parity, flags_cmp
import sys

p = sys.argv[1]

prev_file_name = None
fh = None

for al in range(0, 256):
    file_name = f'cmp_{al:02x}.asm'

    if file_name != prev_file_name:

        prev_file_name = file_name

        if fh != None:
            # to let emulator know all was fine
            fh.write('\tmov ax,#$a5ee\n')
            fh.write('\tmov si,ax\n')
            fh.write('\thlt\n')

            fh.close()

        fh = open(p + '/' + file_name, 'w')

        fh.write('\torg $800\n')
        fh.write('\n')

        fh.write('\txor ax,ax\n')
        fh.write('\tmov si,ax\n')
        fh.write('\n')
        fh.write('\tmov ss,ax\n')  # set stack segment to 0
        fh.write('\tmov ax,#$800\n')  # set stack pointer
        fh.write('\tmov sp,ax\n')  # set stack pointer

    for val in range(0, 256):
        for carry in range(0, 2):
            label = f'test_{al:02x}_{val:02x}_{carry}'

            fh.write(f'{label}:\n')

            # reset flags
            fh.write(f'\txor ax,ax\n')
            fh.write(f'\tpush ax\n')
            fh.write(f'\tpopf\n')

            flags = flags_cmp(carry, al, val)

            # verify value
            fh.write(f'\tmov al,#${al:02x}\n')
            fh.write(f'\tmov bl,#${val:02x}\n')
            
            if carry:
                fh.write('\tstc\n')

            else:
                fh.write('\tclc\n')

            # do test
            fh.write(f'\tcmp al,bl\n')

            # keep flags
            fh.write(f'\tpushf\n')

            fh.write(f'\tcmp al,#${al:02x}\n')
            fh.write(f'\tjz ok_a_{label}\n')

            fh.write(f'\thlt\n')

            fh.write(f'ok_a_{label}:\n')

            fh.write(f'\tcmp bl,#${val:02x}\n')
            fh.write(f'\tjz ok_b_{label}\n')

            fh.write(f'\thlt\n')

            fh.write(f'ok_b_{label}:\n')

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
