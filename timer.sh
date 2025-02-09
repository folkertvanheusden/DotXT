#! /bin/sh

as86 -0 -b timer.bin -l timer.list timer.asm
rm ~/temp/ramdisk/logfile.txt ; dotnet run -- -t timer.bin -T 0 -o 0,0800 -x binary -l ~/temp/ramdisk/logfile.txt
