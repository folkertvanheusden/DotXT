DotXT
=====

* in the roms subdirectory, unzip BIOS\_5160\_16AUG82.zip

- to run the unit-tests, install python3 and:
  - `dotnet tool install --global dotnet-coverage`
  - `dotnet tool install -g dotnet-reportgenerator-globaltool`

Tested with dotnet 8.0 on linux (apt install dotnet8).

In release mode (`dotnet run -c Release`) it is a lot faster.


This software is © Folkert van Heusden.

Released in the public domain.
