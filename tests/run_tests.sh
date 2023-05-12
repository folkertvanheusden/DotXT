#! /bin/sh

python3 adc.py

#LF=logfile.txt
LF=/home/folkert/temp/ramdisk/logfile.txt

for i in adc*asm
do
	BASE=`basename $i .asm`

	echo Working on $BASE

	as86 -0 -O -l $BASE.list -m -b $BASE.bin $i

	TEST_BIN=`pwd`/$BASE.bin

	(cd ../ ; rm -f $LF ; dotnet build -c Debug && dotnet run -l $LF -t $TEST_BIN)

	if [ $? -eq 1 ] ; then
		echo Test $i failed
		exit 1
	fi
done

rm -f adc*asm* adc*list* adc*bin*
