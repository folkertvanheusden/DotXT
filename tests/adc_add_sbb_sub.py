#! /usr/bin/python3

from flags import parity, flags_add_sub_cp
import sys

p = sys.argv[1]

prev_file_name = None
fh = None

for al in range(0, 256):
    file_name = f'adc_add_sbb_sub_{al:02x}.asm'

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
            for instr in range(0, 4):
                label = f'test_{al:02x}_{val:02x}_{carry}_{instr}'

                fh.write(f'{label}:\n')

                # reset flags
                fh.write(f'\txor ax,ax\n')
                fh.write(f'\tpush ax\n')
                fh.write(f'\tpopf\n')

                (check_val, flags) = flags_add_sub_cp(instr >= 2, True if carry > 0 and (instr == 0 or instr == 2) else False, al, val)

                # verify value
                fh.write(f'\tmov al,#${al:02x}\n')
                fh.write(f'\tmov bl,#${val:02x}\n')
                fh.write(f'\tmov cl,#${check_val:02x}\n')
                
                if carry:
                    fh.write('\tstc\n')

                else:
                    fh.write('\tclc\n')

                # do test
                if instr == 0:
                    fh.write(f'\tadc al,bl\n')

                elif instr == 1:
                    fh.write(f'\tadd al,bl\n')

                elif instr == 2:
                    fh.write(f'\tsbb al,bl\n')

                elif instr == 3:
                    fh.write(f'\tsub al,bl\n')

                # keep flags
                fh.write(f'\tpushf\n')

                fh.write(f'\tcmp al,cl\n')
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
