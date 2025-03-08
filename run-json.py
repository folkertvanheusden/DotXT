#! /usr/bin/python3

import json
from subprocess import Popen, PIPE
import sys

debug = False

def docmd(p, str, wait=True):
    p.stdin.write((str + "\r\n").encode('ascii'))

    while wait:
        line = p.stdout.readline()
        line = line.decode('ascii', 'ignore').rstrip('\n')
        if debug:
            print(line)

        if len(line) < 5:
            continue

        idx = line.find('<SET')
        if idx != -1:
            return line[idx + 5:]

        idx = line.find('>GET')
        if idx != -1:
            return line[idx + 5:]

def flag(v, b, c):
    if v & (1 << b):
        return c
    return '-'

def flag_str(v):
    o = ''
    o += flag(v, 11, 'o')
    o += flag(v, 10, 'D')
    o += flag(v,  9, 'I')
    o += flag(v,  7, 's')
    o += flag(v,  6, 'z')
    o += flag(v,  4, 'a')
    o += flag(v,  2, 'p')
    o += flag(v,  1, '1')
    o += flag(v,  0, 'c')
    return o

def val_to_flags(v, mask):
    o = f'{v} (hex: {v:04x}, {flag_str(v)}, masked: {flag_str(v & mask)})'
    return o

process = Popen(['dotnet', 'run', '-c', 'Release', '--', '-m', 'json', '-l', sys.argv[3], '-L', 'trace', '-M', 'no-io'], stdin=PIPE, stdout=PIPE, stderr=PIPE, bufsize=0)
process.stdin.write('echo\r\n'.encode('ascii'))  # disable echo

def log(x):
    process.stdin.write(f'dolog {x}\r\n'.encode('ascii'))

test_file = sys.argv[1]

sub_cmd = test_file[-7] == '.' and test_file[-6].isdigit() and test_file[-5:] == '.json'

j = json.loads(open(test_file, 'rb').read())
jm = json.loads(open(sys.argv[2], 'rb').read())

error_nr = 0
test_nr = 0

for set in j:
    test_nr += 1
    if not 'name' in set:
        continue

    name = set['name']

    process.stdin.write(f'reset\r\n'.encode('ascii'))
    log(f'dolog START {set["name"]}\r\n')

    b = set['bytes']

    # v1 fix: flags_mask = 65535 & (~16)

    byte_offset = 0
    while b[byte_offset] in (0x26, 0x2e, 0x36, 0x3e, 0xf2, 0xf3):
        byte_offset += 1

    first_byte = f'{b[byte_offset]:02X}'

    flags_mask = None
    if sub_cmd:
        second_byte = b[byte_offset + 1]
        reg = f'{(second_byte >> 3) & 7}'

        if 'flags-mask' in jm['opcodes'][first_byte]['reg'][reg]:
            flags_mask = int(jm['opcodes'][first_byte]['reg'][reg]['flags-mask'])

    else:
        if 'flags-mask' in jm['opcodes'][first_byte]:
            flags_mask = int(jm['opcodes'][first_byte]['flags-mask'])

    initial = set['initial']

    # set registers
    regs = initial['regs']
    for reg in regs:
        docmd(process, f'set reg {reg} {regs[reg]}')

    # fill ram
    for addr, value in initial['ram']:
        docmd(process, f'set ram {addr} {value}')

    # invoke!
    docmd(process, 'S', False)  # step

    # verify
    final = set['final']

    ok = True

    # verify registers
    regs = final['regs']
    is_ = dict()
    for reg in regs:
        result = int(docmd(process, f'get reg {reg}').split()[1])
        is_[reg] = result

        compare = regs[reg]

        if reg == 'flags' and flags_mask != None:
            result &= flags_mask
            compare &= flags_mask

        if result != compare:
            if name:
                log(name)
                name = None
            log(f' *** {reg} failed ***')
            ok = False

    # verify mem
    for addr, value in final['ram']:
        result = int(docmd(process, f'get ram {addr}').split()[1])

        if result != value:
            if name:
                log(name)
                name = None
            log(f' *** {addr:06x} failed ***')
            log(f'{addr:06x} is {result} (hex: {result:04x}), should be: {value} (hex: {value:04x})')
            ok = False

    if ok:
        log(f'dolog OK {set["name"]}')
    else:
        #log(json.dumps(set))
        for reg in regs:
            if final["regs"][reg] == is_[reg]:
                continue
            if reg == 'flags':
                if flags_mask == None:
                    flags_mask = 65535
                log(f'{reg} was {val_to_flags(initial["regs"][reg], flags_mask)}, should now be {val_to_flags(final["regs"][reg], flags_mask)}, is: {val_to_flags(is_[reg], flags_mask)}')
            else:
                log(f'{reg} was {initial["regs"][reg]} (hex: {initial["regs"][reg]:04x}), should now be {final["regs"][reg]} (hex: {final["regs"][reg]:04x}), is: {is_[reg]} (hex: {is_[reg]:04x})')

        ss = is_['SS'] if 'SS' in is_ else initial['regs']['ss']
        sp = is_['SP'] if 'SP' in is_ else initial['regs']['sp']
        log(f'dolog sp:ss is at {ss * 16 + sp:06x}')
        ip = is_['ip'] if 'ip' in is_ else -1
        memint0 = int(docmd(process, f'get ram 0').split()[1]) + int(docmd(process, f'get ram 1').split()[1]) * 256
        istr = f'INT{ip:04x}' if ip == memint0 else 'NO-INT'
        log(f'dolog FAILED {set["name"]}, nr: {error_nr}/{test_nr}: {set["hash"]}, {sys.argv[1]} {istr}\r\n')

        error_nr += 1

        #process.stdin.write(f'q\r\n'.encode('ascii'))
        #sys.exit(1)

sys.exit(1 if error_nr > 0 else 0)
