


# PXDConverter
Small console application used for converting PXD files (eJay music files) to WAV files.
## How does it work?
Converter uses pxd32d5_d4.dll external methods for unpacking PXD to raw binary data (stored in a temporary file). The new WAV file is created with appropriate values and raw binary data copied from tmp file.
## Usage:
Application uses .NET 4.8. It also uses pxd32d5_d4.dll which needs to be located in the same folder as compiled .exe. You can find .dll file inside of your eJay installation directories.
Compile with MSBuild.exe or Visual Studio.
Run compiled program with argument as follows:
```
./PXDConverter.exe pxd_file_location
```
