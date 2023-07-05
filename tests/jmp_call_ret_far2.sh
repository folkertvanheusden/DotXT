#! /bin/sh

$(dirname "$0")/jmp_call_ret_far2.py /tmp

as86 -0 -O -l jmp_call_ret_far2_A.list -m -b jmp_call_ret_far2_A.bin /tmp/jmp_call_ret_far2_A.asm
truncate -s 65536 jmp_call_ret_far2_A.bin
rm /tmp/jmp_call_ret_far2_A.asm

as86 -0 -O -l jmp_call_ret_far2_B.list -m -b jmp_call_ret_far2_B.bin /tmp/jmp_call_ret_far2_B.asm
rm /tmp/jmp_call_ret_far2_B.asm

cat jmp_call_ret_far2_A.bin jmp_call_ret_far2_B.bin > jmp_call_ret_far2.bin
rm jmp_call_ret_far2_A.bin jmp_call_ret_far2_B.bin

echo 'jmp_call_ret_far2.bin'
