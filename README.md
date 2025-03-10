DotXT
=====

An IBM PC emulator written in C# with dotnet 9.0. It runs on Linux and probably on other C#/.NET platforms as well.

Please no "pull requests" (via github or other way), but tips/hints/suggestions are welcome.


compiling the emulator
======================

* On Linux
  `dotnet build -c Release`
  After a while you'll find a programfile called "`bin/Release/net9.0/dotxt`".

* On Windows
  `dotnet build -c Release`
  After a while you'll find a programfile called "`bin\Release\net9.0\dotxt.exe`".

If you like, you can copy that dotxt(.exe) file into the demo directory.


running it
==========

`dotxt -R roms/GLABIOS.ROM,f000:e000 -X harddisks/demo.vhd -p cga,vnc,5900 -R roms/ide_xt.rom,d000:0000 -R roms/GLaTICK_0.8.5_AT.ROM,d000:2000 -S -O rtc`

Note that you need to use a VNC-viewer to connect to the emulator. E.g. "`vncviewer localhost:0`" (for example https://www.tightvnc.com/ should work).

The example above runs DotXT with:
* GLABIOS installed (`-R roms/GLABIOS.ROM,f000:e000`)
* an XT-IDE harddisk (`-X harddisks/demo.vhd -R roms/ide_xt.rom,d000:0000`)
* a CGA-adapter exported via VNC at port 5900 (`-p cga,vnc,5900`)
* an RTC (`-O rtc -R roms/GLaTICK_0.8.5_AT.ROM,d000:2000`)
* and throttles the speed to emulate a 4.77 MHz PC (`-S`).


If you would like MIDI-support, add "`-O midi`" to the command line.
Adlib support uses "`-O alib`". Listen to it via RTSP (e.g. on linux: `gst-launch-1.0 rtspsrc location=rtsp://localhost:5540 \! decodebin \! audioconvert \! audioresample \! autoaudiosink`).
Run `dotxt -h` to get a list fo commandline parameters.
In the console ("`==>`" on your screen), you can enter e.g. "help" to get a list of commands. For example for changing the floppy drive image for an other.

==> you need to enter "go" (+ enter) to start the emulation!


demo
====

See https://vanheusden.com/emulation/ibmpc/demo.zip contains all the bios-files etc.

And https://www.youtube.com/watch?v=9jfngoD6r70 and https://www.youtube.com/watch?v=9jfngoD6r70



This software is © Folkert van Heusden.

Released in the public domain.
