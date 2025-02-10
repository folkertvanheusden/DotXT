#! /bin/sh

as86 -0 -b floppy.bin -l floppy.list floppy.asm
rm ~/temp/ramdisk/logfile.txt ; dotnet run -- -t floppy.bin -T 0 -o 0,0800 -x binary -l ~/temp/ramdisk/logfile.txt
