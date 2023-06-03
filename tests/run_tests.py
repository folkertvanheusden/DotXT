#! /usr/bin/python3

import glob
import multiprocessing
import os
import shutil
import sys
import time

TEMP='test'

#AS86='/usr/local/bin86/bin/as86'
AS86='/usr/bin/as86'

CUR_PATH=os.getcwd()

CC=True
CCREPORT=CUR_PATH + '/ccreport'
CCXML=CUR_PATH + '/coverage.xml'

def run(prg, par):
    rc = os.waitstatus_to_exitcode(os.system(f'{prg} {par}'))

    if rc != 0:
        print(f'"{prg} {par}" failed: {rc}')

        sys.exit(1)

def run_path(path, cmd, exprc):
    pid = os.fork()

    if pid == 0:
        os.chdir(path)

        rc = os.waitstatus_to_exitcode(os.system(cmd))

        if rc != exprc:
            print(f' *** FAILED {rc} {cmd} ***')

        sys.exit(rc)

    rc = os.waitstatus_to_exitcode(os.waitpid(pid, 0)[1])

    return rc

print('Generating tests...')
start_t = time.time()
run('python3 adc_add_sbb_sub.py', TEMP)
run('python3 adc16_add16_sbb16_sub16.py', TEMP)
run('python3 cmp.py', TEMP)
run('python3 cmp16.py', TEMP)
run('python3 misc.py', TEMP)
run('python3 mov.py', TEMP)
run('python3 or_and_xor_test.py', TEMP)
run('python3 or_and_xor_test_16.py', TEMP)
run('python3 rcl_rcr_rol_ror_sal_sar.py', TEMP)
run('python3 inc_dec.py', TEMP)
run('python3 inc_dec16.py', TEMP)
print(f'Script generation took {time.time() - start_t:.3f} seconds')

LF=True

os.chdir(TEMP)

run_path('../..', 'dotnet build -c Debug', 0)

def dotest(i):
    BASE=CUR_PATH + '/' + TEMP + '/' + os.path.splitext(i)[0]

    print(f'Working on {BASE}')

    TEST_BIN=f'{BASE}.bin'

    run(AS86, f'-0 -O -l {BASE}.list -m -b {TEST_BIN} {i}')

    COVERAGE=f'{BASE}.coverage'

    LOGFILE='/dev/null' if not LF else f'{BASE}.log'

    rc = None

    if CC:
        rc = run_path('../../', f'dotnet-coverage collect "dotnet run -c Debug -t {TEST_BIN} -l {LOGFILE}" -o {COVERAGE}', 123)

    else:
        rc = run_path('../../', f'dotnet run -c Debug -l {LOGFILE} -t {TEST_BIN}', 123)

    if rc == 123:
        if LF:
            try:
                os.unlink(LOGFILE)
            except FileNotFoundError as e:
                pass

        os.unlink(BASE + '.list')
        os.unlink(TEST_BIN)
        os.unlink(i)

def rm_r(path):
    if os.path.isdir(path) and not os.path.islink(path):
        shutil.rmtree(path)
    elif os.path.exists(path):
        os.remove(path)

print('Loading file list...')
files = glob.glob('*.asm')

print('Running batch...')
start_t = time.time()

# limit number of processes to about 75% of the number available: dotnet-coverage does
# not use a complete processing unit
with multiprocessing.Pool(processes=int(multiprocessing.cpu_count() * 3 / 4)) as pool:
    pool.map(dotest, files)
# dotest('rcl_rcr_rol_ror_sal_sar8a_19456.asm')

print(f'Batch processing took {time.time() - start_t:.3f} seconds')

if CC:
    run(f'dotnet-coverage merge -o {CCXML} -f xml', '*.coverage')

    rm_r(CCREPORT)

    os.mkdir(CCREPORT)

    run(f'reportgenerator -reports:{CCXML} -targetdir:{CCREPORT} -reporttypes:html', '')

#(cd ../../ ; dotnet clean -c Debug)

#echo All fine
#exit 0
