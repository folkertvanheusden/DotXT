#! /usr/bin/python3

prev_file_name = None
fh = None

for al in range(0, 256):
    file_name = f'adc_{al & 0xf0:02x}.asm'

    if file_name != prev_file_name:

        prev_file_name = file_name

        if fh != None:
            # to let emulator know all was fine
            fh.write('\tmov ax,$a5ee\n')
            fh.write('\tmov si,ax\n')
            fh.write('\thlt\n')

            fh.close()

        fh = open(file_name, 'w')

        fh.write('\torg $400\n')
        fh.write('\n')

        fh.write('\txor ax,ax\n')
        fh.write('\tmov si,ax\n')
        fh.write('\n')

    for val in range(0, 256):
        for carry in range(0, 2):
            label = f'test_{al:02x}_{val:02x}_{carry}'

            fh.write(f'{label}:\n')

            # reset flags
            fh.write(f'\txor ax,ax\n')
            fh.write(f'\tpush ax\n')
            fh.write(f'\tpopf\n')

            # verify value
            fh.write(f'\tmov al,{al}\n')
            
            if carry:
                fh.write('\tstc\n')

            else:
                fh.write('\tclc\n')

            fh.write(f'\tadc al,{val}\n')

            check_val = (val + al + carry) & 0xff

            fh.write(f'\tcmp al,{check_val}\n')
            fh.write(f'\tjz ok_{label}\n')

            fh.write(f'\thlt\n')

            fh.write(f'ok_{label}:\n')

            # verify flags
            flags = 123

            fh.write(f'\tpushf\n')
            fh.write(f'\tpop ax\n')
            fh.write(f'\tcmp ax,{flags}\n')
            fh.write(f'\tjz next_{label}\n')
            fh.write(f'\thlt\n')

            # TODO: verify flags
            fh.write(f'next_{label}:\n')
            fh.write('\n')

fh.write('\tmov ax,$a5ee\n')
fh.write('\tmov si,ax\n')
fh.write('\thlt\n')
fh.close()
