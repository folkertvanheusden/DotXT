#! /bin/sh

# 8D

#for i in ~/t/testset/D3*.json
for i in /mnt/ProcessorTests/8088/v1/D*json
do
	./run-json.py $i /mnt/ProcessorTests/8088/v1/8088.json

	if [ $? -ne 0 ] ; then
		break
	fi
done
