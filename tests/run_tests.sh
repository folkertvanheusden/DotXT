#! /bin/sh

TEMP='test'

#python3 adc_add_sbb_sub.py $TEMP
python3 adc16_add16_sbb16_sub16.py $TEMP

#LF=logfile.txt
LF=/home/folkert/temp/ramdisk/logfile.txt
#LF=/dev/null

echo Logfile: $LF

cd $TEMP

for i in *.asm
do
	BASE=`basename $i .asm`

	echo Working on $BASE

	as86 -0 -O -l $BASE.list -m -b $BASE.bin $i

	TEST_BIN=`pwd`/$BASE.bin

	(cd ../../ ; rm -f $LF ; dotnet build -c Release && dotnet run -l $LF -t $TEST_BIN)

	if [ $? -eq 1 ] ; then
		echo Test $i failed
		exit 1
	fi
done

rm -f *.asm *.list *.bin

echo All fine
exit 0
