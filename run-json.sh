#! /bin/sh

LOGFILE=/home/folkert/temp/ramdisk/logfile.txt
rm -f $LOGFILE.*

NR=1
#for i in ~/t/testset/D3*.json

for i in /home/folkert/t/8088/v2/*.json
#for i in /home/folkert/t/8088/v2/E9.json
#for i in /home/folkert/t/8088/v2/F[67].[67].json
#for i in /home/folkert/t/8088/v2_undefined/*.json
do
	echo $i "($NR)" $LOGFILE.$NR
	./run-json.py $i /home/folkert/t/8088/v2_undefined/metadata.json $LOGFILE.$NR
	if [ $? -ne 0 ] ; then
		echo "$i has errors"
	fi

	NR=$((NR+1))
done
