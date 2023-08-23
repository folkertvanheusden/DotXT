#! /bin/sh

for i in ~/t/testset/*.json
do
	./run-json.py $i

	if [ $? -ne 0 ] ; then
		break
	fi
done
