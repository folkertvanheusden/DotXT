#! /bin/sh

python3 adc.py

for i in adc*asm
do
	base=`basename $i .asm`

	echo Working on $base

	as86 -0 -O -l $base.list -m -b $base.bin $i

	test_bin=`pwd`/$base.bin

	(cd ../ ; rm -f logfile.txt ; dotnet build -c Debug && dotnet run $test_bin)

	if [ $? -eq 1 ] ; then
		echo Test $i failed
		exit 1
	fi
done

rm -f adc*asm* adc*list* adc*bin*
