#! /bin/sh

TEMP='test'

AS86=/usr/local/bin86/bin/as86

#python3 adc_add_sbb_sub.py $TEMP
#python3 adc16_add16_sbb16_sub16.py $TEMP
python3 misc.py $TEMP
#python3 mov.py $TEMP

LF=logfile.txt
#LF=/home/folkert/temp/ramdisk/logfile.txt
#LF=/dev/null

echo Logfile: $LF

cd $TEMP

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

	(cd ../../ ; rm -f $LF ; dotnet build -c Release && dotnet run -l $LF -t $TEST_BIN)

	if [ $? -ne 123 ] ; then
		echo Test $i failed
		exit 1
	fi
done

rm -f *.asm *.list *.bin

echo All fine
exit 0
