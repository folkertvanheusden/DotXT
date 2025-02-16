#! /bin/sh

# 8D

#for i in ~/t/testset/D3*.json
for i in /home/folkert/t/8088/v2/*.json
do
	echo $i
	#./run-json.py $i /mnt/ProcessorTests/8088/v2/8088.json
	./run-json.py $i /home/folkert/t/8088/v2/metadata.json

	if [ $? -ne 0 ] ; then
		echo "$i has errors"
	fi
done
