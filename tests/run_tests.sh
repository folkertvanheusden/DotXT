#! /bin/sh

python3 adc.py

for i in adc*asm
do
	base=`basename $i .asm`

	echo Working on $base

	as86 -0 -O -l $base.list -m -b $base.bin $i

	test_bin=`pwd`/$base.bin

	(cd ../ ; dotnet build -c Debug && dotnet run $test_bin)

	if [ $? -eq 1 ] ; then
		echo Test $i failed
		break
	fi

	rm -f logfile.txt
done

rm -f adc*asm* adc*list* adc*bin*
