#! /bin/sh

as86 -0 -b interrupt.bin -l interrupt.list interrupt.asm
rm ~/temp/ramdisk/logfile.txt ; dotnet run -- -t interrupt.bin -T 0 -o 0,0800 -x binary -l ~/temp/ramdisk/logfile.txt
