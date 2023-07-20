#! /bin/sh

mkdir tests

python3 adc_add_sbb_sub.py tests
python3 adc16_add16_sbb16_sub16.py tests
python3 cbw_cwd.py tests
python3 cmp.py tests
python3 cmp16.py tests
python3 inc_dec.py tests
python3 inc_dec16.py tests
python3 jmp_call_ret.py tests
python3 jmp_call_ret_far.sh tests
python3 jmp_call_ret_far2.sh tests
python3 misc.py tests
python3 misc2.py tests
python3 misc2b.py tests
python3 misc3.py tests
python3 mov.py tests
python3 neg.py tests
python3 or_and_xor_test.py tests
python3 or_and_xor_test_16.py tests
python3 push_pop.py tests
python3 rcl_rcr_rol_ror_sal_sar.py tests
python3 strings.py tests
