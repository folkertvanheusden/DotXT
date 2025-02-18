#! /bin/sh

LOGFILE=/home/folkert/temp/ramdisk/logfile.txt
rm -f $LOGFILE

#for i in ~/t/testset/D3*.json
for i in /home/folkert/t/8088/v2/*.json
#for i in /home/folkert/t/8088/v2/D2.5.json /home/folkert/t/8088/v2/D3.5.json /home/folkert/t/8088/v2/D8.json /home/folkert/t/8088/v2/D9.json /home/folkert/t/8088/v2/DA.json /home/folkert/t/8088/v2/DC.json /home/folkert/t/8088/v2/DE.json /home/folkert/t/8088/v2/DF.json /home/folkert/t/8088/v2/E5.json /home/folkert/t/8088/v2/ED.json /home/folkert/t/8088/v2/F6.6.json /home/folkert/t/8088/v2/F6.7.json /home/folkert/t/8088/v2/F7.5.json /home/folkert/t/8088/v2/F7.6.json /home/folkert/t/8088/v2/F7.7.json /home/folkert/t/8088/v2/FF.2.json /home/folkert/t/8088/v2/FF.6.json /home/folkert/t/8088/v2/FF.7.json
#for i in /home/folkert/t/8088/v2/F7.6.json /home/folkert/t/8088/v2/F7.7.json /home/folkert/t/8088/v2/F6.6.json /home/folkert/t/8088/v2/F6.7.json
do
	echo $i
	./run-json.py $i /home/folkert/t/8088/v2/metadata.json $LOGFILE

	if [ $? -ne 0 ] ; then
		echo "$i has errors"
	fi
done
