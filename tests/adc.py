#! /usr/bin/python3

prev_file_name = None
fh = None

def flags_add_sub_cp(is_sub: bool, carry: bool, val1: int, val2: int) -> int:
    org_value = val1

    val2 += 1 if carry else 0

    if is_sub:
        result = val1 - val2

    else:
        result = val1 + val2

    flag_h = ((val1 & 0x10) ^ (org_value & 0x10) ^ (result & 0x10)) == 0x10

    flag_c = (result & 0x100) != 0

    before_sign = val1 & 0x80
    value_sign = org_value & 0x80
    after_sign = result & 0x80
    flag_o = after_sign != before_sign and ((before_sign != value_sign and is_sub) or (before_sign == value_sign and not is_sub))

    result &= 0xff

    flag_z = result == 0
    flag_s = after_sign == 0x80

    flags = (1 if flag_c else 0) + (4 if flag_h else 0) + (256 if flag_o else 0) + (8 if flag_z else 0) + (16 if flag_s else 0)

    return (result, flags)

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
            fh.write(f'\tmov al,${al:02x}\n')
            
            if carry:
                fh.write('\tstc\n')

            else:
                fh.write('\tclc\n')

            fh.write(f'\tadc al,{val}\n')

            (check_val, flags) = flags_add_sub_cp(False, True if carry > 0 else False, al, val)

            fh.write(f'\tcmp al,{check_val}\n')
            fh.write(f'\tjz ok_{label}\n')

            fh.write(f'\thlt\n')

            fh.write(f'ok_{label}:\n')

            # verify flags
            fh.write(f'\tpushf\n')
            fh.write(f'\tpop ax\n')
            fh.write(f'\tcmp ax,${flags:04x}\n')
            fh.write(f'\tjz next_{label}\n')
            fh.write(f'\thlt\n')

            # TODO: verify flags
            fh.write(f'next_{label}:\n')
            fh.write('\n')

fh.write('\tmov ax,$a5ee\n')
fh.write('\tmov si,ax\n')
fh.write('\thlt\n')
fh.close()
