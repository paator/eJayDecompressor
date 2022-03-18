


# eJay Decompresser
Small console application used for converting eJay music files to WAV files.
## How does it work?
Converter calls dll external methods used for unpacking PXD to raw binary data (stored in a temporary file). The new WAV file is created with appropriate values (see  [WAVE PCM format specification](http://soundfile.sapp.org/doc/WaveFormat) ), then raw binary data is copied from .tmp file.
## Usage:
Application uses .NET 6.0. It also uses pxd32d5_d4.dll (old single .pxd files) and eJ_Tool.dll (for MultiPXD packages) which needs to be located in the same folder as compiled .exe. You can find .dll file inside of your eJay installation directories.
Compile with MSBuild.exe or Visual Studio.

If you want to convert only one file, run compiled program with one argument as follows:
```
./eJayDecompresser.exe <pxd_file_location>
```

If you want to convert all .pxd files found in specified directory, run:
```
./eJayDecompresser.exe -all <folder_location>
```
If you don't specify <folder_location>, program will try to use current directory (where compiled .exe is executed) :octocat:
## Note:
This repository assumes you obtained pxd32d5_d4.dll or eJ_Tool.dll in legal way by buying the original eJay software. Don't ask for putting these files into the repository - it won't happen...
