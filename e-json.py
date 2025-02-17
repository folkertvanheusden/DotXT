#! /usr/bin/python3

import json
import sys


js = json.load(open(sys.argv[1]))
j = js[int(sys.argv[2])]

print(f'; {j["name"]}')
prefixes = {
	0x26: 'es',
	0x2e: 'cs',
	0x36: 'ss',
	0x3e: 'ds',
	0xf2: 'repnz',
	0xf3: 'repz'
	}
if j['bytes'][0] in prefixes:
    print(f'; prefix: {prefixes[j["bytes"][0]]}')
print(';')
print('; initial ram')
for ram in j['initial']['ram']:
    print(f'\torg {ram[0]}')
    print(f'\tdb {ram[1]}')

print('\torg $1000')  # hopefully that doesn't clash
r = j['initial']['regs']
print(f'\tmov bx, #{r["bx"]}')
print(f'\tmov cx, #{r["cx"]}')
print(f'\tmov dx, #{r["dx"]}')
print('; flags')
print('\tmov ax,#$1000')
print('\tmov sp,ax')
print(f'\tmov ax, #{r["flags"]}')
print(f'\tpush ax')
print(f'\tpopf')
print('; ')
for reg in ('sp', 'ss', 'ds', 'es', 'bp', 'si', 'di'):
    print(f'\tmov ax, #{r[reg]}')
    print(f'\tmov {reg},ax')
print(f'\tmov ax, #{r["ax"]}')
print('; ')
print(f'\tjmp far {r["cs"]}:{r["ip"]}')
