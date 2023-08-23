#! /usr/bin/python3

import json
from subprocess import Popen, PIPE
import sys

def docmd(p, str, wait=True):
    p.stdin.write((str + "\r\n").encode('ascii'))

    while wait:
        line = p.stdout.readline()

        line = line.decode('ascii', 'ignore').rstrip('\n')

        if len(line) < 5:
            continue

        idx = line.find('<SET')
        if idx != -1:
            return line[idx + 5:]

        idx = line.find('>GET')
        if idx != -1:
            return line[idx + 5:]

process = Popen(['dotnet', 'run', '-c', 'Debug', '--', '-d', '-P', '-l', 'logfile.dat', '-x', 'blank', '-B'], stdin=PIPE, stdout=PIPE, stderr=PIPE, bufsize=0)

process.stdin.write('echo\r\n'.encode('ascii'))  # disable echo

j = json.loads(open(sys.argv[1], 'rb').read())

for set in j:
    print(set['name'])

    process.stdin.write(f'reset\r\n'.encode('ascii'))

    process.stdin.write(f'dolog {set["name"]}\r\n'.encode('ascii'))

    initial = set['initial']

    # set registers
    regs = initial['regs']
    for reg in regs:
        docmd(process, f'set reg {reg} {regs[reg]}')

    # fill ram
    for addr, value in initial['ram']:
        docmd(process, f'set ram {addr} {value}')

    docmd(process, 's', False)  # step

    # verify
    final = set['final']

    ok = True

    # verify registers
    regs = final['regs']
    is_ = dict()
    for reg in regs:
        result = int(docmd(process, f'get reg {reg}').split()[1])

        is_[reg] = result

        if result != regs[reg]:
            print(f' *** {reg} failed ***')
            print(f': {result} (emulator) != {regs[reg]} (test set)')
            ok = False

    # verify mem
    for addr, value in final['ram']:
        result = int(docmd(process, f'get ram {addr}').split()[1])

        if result != value:
            print(f' *** {addr} failed ***')
            print(f': {result} (emulator) != {value} (test set)')
            ok = False

    if not ok:
        for reg in regs:
            print(f'{reg} was at start {initial["regs"][reg]}, should have become {final["regs"][reg]}, is: {is_[reg]}')

        break
