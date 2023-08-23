#! /bin/sh

for i in ~/t/testset/*.json
do
	./run-json.py $i ~/t/testset/8088-masks.json

	if [ $? -ne 0 ] ; then
		break
	fi
done
