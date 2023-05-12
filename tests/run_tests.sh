#! /bin/sh

TEMP='test'

python3 adc.py $TEMP

#LF=logfile.txt
#LF=/home/folkert/temp/ramdisk/logfile.txt
LF=/dev/null

cd $TEMP

for i in adc*asm
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

rm -f adc*asm* adc*list* adc*bin*
