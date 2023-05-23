#! /bin/sh

TEMP=test

AS86=/usr/local/bin86/bin/as86

CC=1
CCREPORT=`pwd`/ccreport
CCXML=`pwd`/coverage.xml

#python3 adc_add_sbb_sub.py $TEMP
#python3 adc16_add16_sbb16_sub16.py $TEMP
#python3 cmp.py $TEMP
#python3 cmp16.py $TEMP
#python3 misc.py $TEMP
python3 mov.py $TEMP
#python3 or_and_xor_test.py $TEMP
#python3 or_and_xor_test_16.py $TEMP
#python3 rcl_rcr_rol_ror_sal_sar.py $TEMP

#LF=logfile.txt
LF=/home/folkert/temp/ramdisk/logfile.txt
#LF=/dev/null

echo Logfile: $LF

cd $TEMP

(cd ../../ ; dotnet build -c Release)

if [ $? -ne 0 ] ; then
	echo Build failed
	exit 3
fi

for i in *.asm
do
	BASE=`basename $i .asm`

	echo Working on $BASE

	$AS86 -0 -O -l $BASE.list -m -b $BASE.bin $i

	if [ $? -ne 0 ] ; then
		echo Test $i failed: ASM error
		exit 2
	fi

	TEST_BIN=`pwd`/$BASE.bin

	COVERAGE=`pwd`/$BASE.coverage

	(cd ../../ ;
	 rm -f $LF ;
	 if [ $CC -eq 1 ] ; then
		 dotnet-coverage collect "dotnet run -t $TEST_BIN -l $LF" -o $COVERAGE
	 else
		 dotnet run -l $LF -t $TEST_BIN
	 fi
 	)

	if [ $? -ne 123 ] ; then
		echo Test $i failed
		exit 1
	fi

	rm $BASE.list $BASE.bin $i
done

if [ $CC -eq 1 ] ; then
	dotnet-coverage merge -o $CCXML -f xml *.coverage

	rm -rf $CCREPORT
	mkdir $CCREPORT
	reportgenerator -reports:$CCXML -targetdir:$CCREPORT/ -reporttypes:html
fi

(cd ../../ ; dotnet clean -c Release)

echo All fine
exit 0
