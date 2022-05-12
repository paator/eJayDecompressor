using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace eJayDecompressor;

public static class Program
{
    private static readonly string TmpPath = Path.GetTempPath() + "\\pxd_converter.tmp\\";

    private static void Main(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine("         _           ___");
        Console.WriteLine("  ___ _ | |__ _ _  _|   \\ ___ __ ___ _ __  _ __ _ _ ___ ______ ___ _ _");
        Console.WriteLine(" / -_) || / _` | || | |) / -_) _/ _ \\ '  \\| '_ \\ '_/ -_|_-<_-</ _ \\ '_|");
        Console.WriteLine(" \\___|\\__/\\__,_|\\_, |___/\\___\\__\\___/_|_|_| .__/_| \\___/__/__/\\___/_|");
        Console.WriteLine("                |__/                      |_|\n");
        Console.WriteLine("Version 1.3");
        Console.ResetColor();
        
        
        if (args.Length < 1)
        {
            Console.WriteLine("You need at least 1 argument to run this program: PXD file path.");
            Console.WriteLine("If you wish to convert multiple files at once, please use -all argument.");

            Console.WriteLine("\nUSAGE:\n");

            Console.WriteLine("./eJayDecompressor.exe <pxd_file_location>");
            Console.WriteLine("./eJayDecompressor.exe -all <folder_location>\n" +
                              "Note: If <folder_location> is not specified, the program will try to use the path where the program " +
                              "was executed.");

            Environment.Exit(-1);
        }

        var argPath = string.Empty;
        var recursiveSearch = false;

        if (args[0].Equals("-all"))
        {
            recursiveSearch = true;

            if (args.Length == 2)
            {
                argPath = args[1];
            }
        }
        else
        {
            argPath = args[0];
        }

        argPath = argPath.Trim();

        Directory.CreateDirectory("converted_wav_files");

        //buffer used by .dll
        Directory.CreateDirectory(TmpPath);

        if (recursiveSearch)
            FindFilesToDecompress(argPath);
        else
        {
            if (argPath.EndsWith(".pxd"))
            {
                var lib = new Pxd32Library();
                Pxd32Library.InitializeDll();
                ProcessSinglePxdFile(argPath, lib);
                Pxd32Library.CloseDll();
            }
            else if (argPath.EndsWith("inf.bin"))
            {
                ProcessMultiWithBinaryInf(argPath, new EjToolLibrary());
            }
            else if (argPath.EndsWith(".inf"))
            {
                ProcessMultiWithTextInf(argPath, new EjToolLibrary());
            }
            else if (argPath.EndsWith(".scl"))
            {
                SclToWav(argPath);
            }
            else
            {
                Console.WriteLine("You should pass only *.PXD, *inf.bin and .inf files");
            }
        }

        Directory.Delete(TmpPath, true);

        Console.WriteLine("Finished.");
    }

    private static void ProcessSinglePxdFile(string argPath, Pxd32Library lib)
    {
        // it can be already wav file
        if (Encoding.ASCII.GetString(ReadFileFromTo(argPath, 0, 4)) == "RIFF")
        {
            var dir = $"{Directory.GetCurrentDirectory()}\\converted_wav_files\\";
            Directory.CreateDirectory(dir);
            var filePath = $"{dir}{Path.GetFileNameWithoutExtension(argPath)}.wav";

            if (!File.Exists(filePath))
            {
                File.Copy(argPath, filePath);
            }
        }
        else
        {
            Console.WriteLine($"Converting raw data file {argPath} to wav format...");

            var tmpPath = TmpPath + "tmp.wav";

            lib.Decompress(argPath, tmpPath);

            RawToWav(argPath, tmpPath);
        }
    }

    private record PxdHeader(string name, string package, string type, int samples, bool isWave);

    private static PxdHeader ReadPxdFileHeader(string filepath, byte[] bytes)
    {
        var isWave = Encoding.ASCII.GetString(bytes[..4]) == "RIFF";

        // rest is useless if it's wav file, but anyway
        var position = 4;
        (position, var headerSize) = ProcessBytes(bytes, position, 1, b => b[0]);
        (position, var headerBytes) = ReadBytes(bytes, position, headerSize);
        headerBytes = headerBytes.TakeWhile(c => c != 0).ToArray();
        position += 1;
        var (_, samples) = ProcessBytes(bytes, position, 4, b => BitConverter.ToInt32(b));
        var s = Regex.Split(Encoding.ASCII.GetString(headerBytes), "\x0D\x0A");
        if (s[^1].EndsWith(".pxd"))
        {
            return new PxdHeader(s[^1][..^4], s.ElementAtOrDefault(s.Length - 2) ?? string.Empty,
                string.Empty, samples, isWave);
        }

        if (s.Length < 3)
        {
            return new PxdHeader(s[^1],
                Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(filepath))!) + " DEMO",
                Path.GetFileName(Path.GetDirectoryName(filepath)!), samples, isWave);
        }

        return new PxdHeader(s[0] + (s.ElementAtOrDefault(1) ?? string.Empty),
            $"{s.ElementAtOrDefault(2) ?? string.Empty} {s.ElementAtOrDefault(3) ?? string.Empty}".Trim(),
            s.ElementAtOrDefault(4) ?? string.Empty, samples, isWave);
    }

    private static (int, TResult) ProcessBytes<TResult>(byte[] arr, int position, int size,
        Func<byte[], TResult> convert)
    {
        var res = convert(arr[position..(position + size)]);
        return (position + size, res);
    }


    private static (int, byte[]) ReadBytes(byte[] arr, int position, int size)
    {
        return (position + size, arr[position..(position + size)]);
    }

    private static bool IsValidRecord(byte[] descriptorBytes, int position)
    {
        // this method makes an assumption that every valid record
        // is going to end with 00 00 00 00 00 00 FF FF
        // (or simply 0 for some empty records).
        // the records that don't are just leftover (or badly parsed) trash at the end of the file.
        // the assumption holds true for dance60inf.bin, but may not be valid for other files

        // as the name field is variable length,
        // we first need to find its length to know how many bytes to skip
        position += 6; // position -> nameLength
        if (position > descriptorBytes.Length) { return false; }
        (position, var nameLength) = ProcessBytes(descriptorBytes, position, 2, b => BitConverter.ToInt16(b));

        position += nameLength + 0x1C; // position -> magic
        if (position > descriptorBytes.Length) { return false; }
        (_, var magic) = ProcessBytes(descriptorBytes, position, 8, b => BitConverter.ToUInt64(b));

        return magic == 0xFFFF000000000000 || magic == 0;
    }

    // sampleRate or maybe samplesCount? not sure
    private record MultiPxdRecord(int id, int group, string name, int offset, int length, int sampleRateByte,
        int type);

    private static byte[] ReadFileFromTo(string fileName, int offset, int length)
    {
        using var fs = File.OpenRead(fileName);
        fs.Position = offset;

        using var binaryReader = new BinaryReader(fs);
        var fileData = binaryReader.ReadBytes(length);

        return fileData;
    }

    private static (int, MultiPxdRecord) ReadBinaryPxdRecord(byte[] descriptorBytes, int position)
    {
        // discards are used for stuff that have unknown purpose

        (position, var fileSeparator) = ReadBytes(descriptorBytes, position, 2);
        (position, var id) = ProcessBytes(descriptorBytes, position, 1, b => b[0]);
        (position, var group) = ProcessBytes(descriptorBytes, position, 1, b => b[0]);
        position += 2; // separator
        (position, var nameLength) = ProcessBytes(descriptorBytes, position, 2, b => BitConverter.ToInt16(b));
        (position, var name) = ProcessBytes(descriptorBytes, position, nameLength, Encoding.ASCII.GetString);
        (position, var sampleRateByte) = ProcessBytes(descriptorBytes, position, 2, b => BitConverter.ToInt16(b));
        (position, var type) = ProcessBytes(descriptorBytes, position, 2, b => BitConverter.ToInt16(b));
        (position, var offset) = ProcessBytes(descriptorBytes, position, 4, b => BitConverter.ToInt32(b));
        (position, var length) = ProcessBytes(descriptorBytes, position, 4, b => BitConverter.ToInt32(b));
        // 1 for first in stereo
        (position, _) = ProcessBytes(descriptorBytes, position, 2, b => BitConverter.ToInt16(b));
        (position, _) = ProcessBytes(descriptorBytes, position, 2, b => BitConverter.ToInt16(b));
        (position, var channels1) = ProcessBytes(descriptorBytes, position, 4, b => BitConverter.ToInt32(b));
        (position, _) = ProcessBytes(descriptorBytes, position, 2, b => BitConverter.ToInt16(b));
        (position, _) = ProcessBytes(descriptorBytes, position, 4, b => BitConverter.ToInt32(b));
        (position, var channels2) = ProcessBytes(descriptorBytes, position, 2, b => BitConverter.ToInt16(b));
        (position, var magic) = ProcessBytes(descriptorBytes, position, 8, b => BitConverter.ToUInt64(b));

        Debug.WriteLine(
            $"[{group}:{id}] '{name}'[{nameLength}] offset: {offset} length: {length} sampleRateByte: {sampleRateByte}, type2: {type}");
        return (position, new MultiPxdRecord(id, group, name, offset, length, sampleRateByte, type));
    }

    private static void ProcessMultiWithBinaryInf(string descriptorPath, EjToolLibrary lib)
    {
        Console.WriteLine($"Working with descriptor file '{descriptorPath}'");

        var binFilePrefix = Path.GetFileNameWithoutExtension(descriptorPath)[..^3];
        var part = BinPathPart(descriptorPath, binFilePrefix);
        var descriptorBytes = File.ReadAllBytes(descriptorPath);
        var position = 8;
        var records = new List<(MultiPxdRecord, PxdHeader, string)>();

        while (IsValidRecord(descriptorBytes, position))
        {
            (position, MultiPxdRecord record) = ReadBinaryPxdRecord(descriptorBytes, position);

            if (record.length != 0)
            {
                var binPath = $"{Path.GetDirectoryName(descriptorPath)}\\{binFilePrefix}{part}";

                var header =
                    ReadPxdFileHeader(binPath, ReadFileFromTo(binPath, record.offset, record.length));
                if (part.Length > 0 && records.Count > 0 && record.offset == 0)
                {
                    // switch to B binary file
                    part = ((char) (part[0] + 1)).ToString();
                }

                records.Add((record, header, part));
            }
        }

        records.Sort((a, b) => a.Item1.id.CompareTo(b.Item1.id));
        ProcessRecordsAndHeaders(records, descriptorPath, lib, binFilePrefix, true);
    }

    private static string BinPathPart(string descriptorPath, string binFilePrefix)
    {
        string part;

        if (File.Exists($"{Path.GetDirectoryName(descriptorPath)}\\{binFilePrefix}a"))
        {
            part = "a";
        }
        else if (File.Exists($"{Path.GetDirectoryName(descriptorPath)}\\{binFilePrefix}A"))
        {
            part = "A";
        }
        else if (File.Exists($"{Path.GetDirectoryName(descriptorPath)}\\{binFilePrefix}"))
        {
            part = "";
        }
        else
        {
            throw new Exception("The descriptor is not in a directory containing binary MultiPXD file");
        }

        return part;
    }

    private static string RemoveBadCharsFromFilename(string filename)
    {
        return string.Join("", filename.Trim().Split(Path.GetInvalidFileNameChars())).Trim();
    }

    private static void ProcessRecordsAndHeaders(List<(MultiPxdRecord, PxdHeader, string)> records,
        string descriptorPath, EjToolLibrary lib, string binFilePrefix, bool binaryHeader)
    {
        records
            .Sort((a, b) => (a.Item1.group, a.Item1.id)
                .CompareTo((b.Item1.group, b.Item1.id)));

        var outputFiles = new HashSet<string>();

        var files = 0;
        for (var index = 0; index < records.Count; index++)
        {
            var (record, header, part) = records.ElementAt(index);
            MultiPxdRecord? stereo = null;
            var channelSize = 0;
            var isStereo = false;

            if (index + 1 < records.Count && records.ElementAt(index + 1).Item1.name.Length == 0)
            {
                isStereo = true;
            }

            else if (record.name.Length > 1 && index + 1 < records.Count &&
                     records.ElementAt(index + 1).Item1.name.Length > 1 &&
                     (records.ElementAt(index + 1).Item1.name[^1] == 'R' ||
                      records.ElementAt(index + 1).Item1.name[^1] == 'L') &&
                     records.ElementAt(index + 1).Item1.name[..^1] == record.name[..^1])
            {
                channelSize = 1;
                isStereo = true;
            }
            else if (index + 1 < records.Count && records.ElementAt(index + 1).Item1.name.Length > 3 &&
                     record.name.Length > 3 &&
                     (records.ElementAt(index + 1).Item1.name[..^3] == "(R)" ||
                      records.ElementAt(index + 1).Item1.name[..^3] == "(L)") &&
                     records.ElementAt(index + 1).Item1.name[..^3] == record.name[..^3])
            {
                channelSize = 3;
                isStereo = true;
            }

            if (isStereo)
            {
                stereo = records.ElementAt(index + 1).Item1;
                index++;
            }

            var filenameWithoutBadChars = RemoveBadCharsFromFilename(record.name[..^channelSize]);
            var binPath = $"{Path.GetDirectoryName(descriptorPath)}\\{binFilePrefix}{part}";
            string dir;

            if (header.package.Length == 0 || header.package == "eJay Musicdirector" || binaryHeader)
            {
                // use descriptor filename
                if (header.type.Length == 0 || binaryHeader)
                {
                    dir = $"converted_wav_files\\{binFilePrefix}\\{record.type}";
                }
                else
                {
                    dir = $"converted_wav_files\\{binFilePrefix}\\{header.type}";
                }
            }
            else
            {
                // we have titles from the headers
                dir = $"converted_wav_files\\{header.package}\\{header.type}";
            }

            Directory.CreateDirectory(dir);
            var outputFile = $"{dir}\\{filenameWithoutBadChars}.wav";

            for (var i = 0; outputFiles.Contains(outputFile.ToLower()) && i < 10; i++)
            {
                outputFile = outputFile[..^4] + "_.wav";
            }

            outputFiles.Add(outputFile.ToLower());

            if (!File.Exists(outputFile))
            {
                if (header.isWave)
                {
                    File.WriteAllBytes(outputFile, ReadFileFromTo(binPath, record.offset, record.length));
                }
                else
                {
                    EjToolLibrary.Decompress(binPath, record.offset, record.length, stereo?.offset ?? 0,
                        stereo?.length ?? 0,
                        2 * header.samples * (stereo != null ? 2 : 1), outputFile);
                }

                if (!File.Exists(outputFile))
                {
                    Console.WriteLine($"Warning: '{outputFile}' does not exist after decompression");
                }
            }

            files++;
        }

        Console.WriteLine($"Total files extracted: {files}");
    }


    private static MultiPxdRecord ReadTextPxdRecord(string[] descriptorLines, int position)
    {
        var id = int.Parse(descriptorLines[position]);
        var group = int.Parse(descriptorLines[position + 1]);
        var pxdFilename = descriptorLines[position + 2][1..^1]; // remove ""
        var offset = int.Parse(descriptorLines[position + 3]);
        var length = int.Parse(descriptorLines[position + 4]);
        var nameLine1 = descriptorLines[position + 5][1..^1]; // remove ""
        var nameLine2 = descriptorLines[position + 6][1..^1]; // remove ""
        var name = nameLine1 + nameLine2;
        var sampleRateByte = int.Parse(descriptorLines[position + 7]);
        var type = int.Parse(descriptorLines[position + 8]);

        Debug.WriteLine(
            $"[{group}:{id}] '{name}' offset: {offset} length: {length} sampleRateByte: {sampleRateByte}, type2: {type}");
        return new MultiPxdRecord(id, group, name, offset, length, sampleRateByte, type);
    }

    private static void ProcessMultiWithTextInf(string descriptorPath, EjToolLibrary lib)
    {
        Console.WriteLine($"Working with descriptor file '{descriptorPath}'");
        var binFilePrefix = Path.GetFileNameWithoutExtension(descriptorPath);
        var part = BinPathPart(descriptorPath, binFilePrefix);
        var descriptorLines = File.ReadAllLines(descriptorPath);
        var records = new List<(MultiPxdRecord, PxdHeader, string)>();

        for (var i = 14; i < descriptorLines.Length; i += 12)
        {
            var record = ReadTextPxdRecord(descriptorLines, i);

            if (record.length != 0)
            {
                string binPath = $"{Path.GetDirectoryName(descriptorPath)}\\{binFilePrefix}{part}";
                PxdHeader header =
                    ReadPxdFileHeader(binPath, ReadFileFromTo(binPath, record.offset, record.length));
                if (part.Length > 0 && records.Count > 0 && record.offset == 0)
                {
                    // switch to B binary file
                    part = ((char) (part[0] + 1)).ToString();
                }

                records.Add((record, header, part));
            }
        }

        ProcessRecordsAndHeaders(records, descriptorPath, lib, binFilePrefix, false);
    }

    private static void ProcessFiles(string[] pxdFilesPaths, string[] pxdFilesHeaderMultiWithTextInfPaths,
        string[] pxdFilesHeaderMultiWithBinaryInfPaths, string[] sclPaths)
    {
        switch (pxdFilesPaths.Length)
        {
            case 0 when pxdFilesHeaderMultiWithTextInfPaths.Length == 0 &&
                        pxdFilesHeaderMultiWithBinaryInfPaths.Length == 0 && sclPaths.Length == 0:
                Console.WriteLine("Couldn't find anything to decompress!");
                return;
            case > 0:
            {
                var pxd32Lib = new Pxd32Library();
                Pxd32Library.InitializeDll();

                foreach (var path in pxdFilesPaths)
                {
                    Console.WriteLine($"Processing PXD file: {path}");
                    ProcessSinglePxdFile(path, pxd32Lib);
                }

                Pxd32Library.CloseDll();
                break;
            }
        }

        foreach (var path in sclPaths)
        {
            Console.WriteLine($"Processing SCL file: {path}");
            SclToWav(path);
        }

        if (pxdFilesHeaderMultiWithTextInfPaths.Length > 0 || pxdFilesHeaderMultiWithBinaryInfPaths.Length > 0)
        {
            var lib = new EjToolLibrary();

            if (pxdFilesHeaderMultiWithBinaryInfPaths.Length > 0)
            {
                foreach (var path in pxdFilesHeaderMultiWithBinaryInfPaths)
                {
                    Console.WriteLine($"Processing MultiPXD binary header file (text): {path}");
                    try
                    {
                        ProcessMultiWithBinaryInf(path, lib);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        Debug.WriteLine(e.ToString());
                    }
                }
            }

            if (pxdFilesHeaderMultiWithTextInfPaths.Length > 0)
            {
                foreach (var path in pxdFilesHeaderMultiWithTextInfPaths)
                {
                    Console.WriteLine($"Processing MultiPXD text header file (text): {path}");
                    try
                    {
                        ProcessMultiWithTextInf(path, lib);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        Debug.WriteLine(e.ToString());
                    }
                }
            }
        }
    }

    private static void FindFilesToDecompress(string argPath)
    {
        Console.WriteLine("Searching for all .pxd files in specified location...");

        if (string.IsNullOrEmpty(argPath))
            argPath = Directory.GetCurrentDirectory();

        var pxdFilesPaths = Directory.GetFiles(argPath, "*.pxd", SearchOption.AllDirectories);
        var pxdFilesHeaderMultiWithTextInfPaths =
            Directory.GetFiles(argPath, "*0.inf", SearchOption.AllDirectories);
        var pxdFilesHeaderMultiWithBinaryInfPaths =
            Directory.GetFiles(argPath, "*inf.bin", SearchOption.AllDirectories);
        var sclFilesPaths = Directory.GetFiles(argPath, "*.scl", SearchOption.AllDirectories);

        ProcessFiles(pxdFilesPaths, pxdFilesHeaderMultiWithTextInfPaths, pxdFilesHeaderMultiWithBinaryInfPaths,
            sclFilesPaths);
    }

    private static void RawToWav(string pxdPath, string tmpPath)
    {
        const string path = "converted_wav_files";
        var header = ReadPxdFileHeader(pxdPath, File.ReadAllBytes(pxdPath));
        string wavPath;
        string wavFullPath;
        if (header.name == "45" && header.package == "Hyper2")
        {
            wavPath = $"{path}\\{Path.GetFileName(Path.GetDirectoryName(pxdPath))!}";
            wavFullPath = $"{wavPath}\\{RemoveBadCharsFromFilename(Path.GetFileNameWithoutExtension(pxdPath))}.wav";
        }
        else if (header.name.Contains("\\"))
        {
            wavFullPath = $"{path}\\{RemoveBadCharsFromFilename(header.package)}\\{header.name}.wav";
            wavPath = Path.GetDirectoryName(wavFullPath)!;
        }
        else
        {
            wavPath =
                $"{path}\\{RemoveBadCharsFromFilename(header.package)}\\{RemoveBadCharsFromFilename(header.type)}";
            wavFullPath = $"{wavPath}\\{RemoveBadCharsFromFilename(header.name)}.wav";
        }

        Directory.CreateDirectory(wavPath);
        Debug.WriteLine(wavFullPath);

        if (File.Exists(wavFullPath)) return;
        Console.WriteLine("Wav file path: " + wavFullPath);
        FileStream? wavStream = null;
        BinaryWriter? wavWriter = null;

        try
        {
            var length = (int) new FileInfo(tmpPath).Length;
            if (length == 0)
            {
                throw new Exception("Raw wav file is empty, looks like the file conversion has failed");
            }

            var riffSize = length + 0x24;

            wavStream = new FileStream(wavFullPath, FileMode.Create);
            wavWriter = new BinaryWriter(wavStream);
            wavWriter.Write(Encoding.ASCII.GetBytes("RIFF"));
            wavWriter.Write(riffSize);
            wavWriter.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            wavWriter.Write(16);
            wavWriter.Write((short) 1); // Encoding: PCM
            wavWriter.Write((short) 1); // Channels: MONO
            wavWriter.Write(44100); // Sample rate: 44100
            wavWriter.Write(88200); // Average bytes per second
            wavWriter.Write((short) 2); // Block align
            wavWriter.Write((short) 16); // Bits per sample
            wavWriter.Write(Encoding.ASCII.GetBytes("data"));
            wavWriter.Write(length);

            using (var fs = File.OpenRead(tmpPath))
            {
                CopyStream(fs, wavStream);
            }

            wavStream.Close();
            wavWriter.Close();
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.ToString());
            wavStream?.Close();
            wavWriter?.Close();
            Console.WriteLine("Couldn't convert raw data to wav. Perhaps the provided .wav path is invalid?");
            File.Delete(wavFullPath);
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }

    private static void SclToWav(string sclPath)
    {
        var path = "converted_wav_files";
        var wavPath =
            $"{path}\\{Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(sclPath)))}\\{Path.GetFileName(Path.GetDirectoryName(sclPath))}";
        var wavFullPath =
            $"{wavPath}\\{RemoveBadCharsFromFilename(Path.GetFileNameWithoutExtension(sclPath))}.wav";
        Debug.WriteLine(wavFullPath);

        if (File.Exists(wavFullPath)) return;
        Console.WriteLine("Wav file path: " + wavFullPath);

        try
        {
            byte[] sclBytes = File.ReadAllBytes(sclPath);
            string sclAsString = Encoding.ASCII.GetString(sclBytes);
            int wavStart = sclAsString.IndexOf("RIFF");
            if (wavStart != -1)
            {
                int listIndex = sclAsString.IndexOf("LIST");
                int cueIndex = sclAsString.IndexOf("cue");
                int wavEnd = -1;
                if (listIndex != -1)
                {
                    wavEnd = listIndex;
                }
                else if (cueIndex != -1)
                {
                    wavEnd = cueIndex;
                }

                Directory.CreateDirectory(wavPath);
                File.WriteAllBytes(wavFullPath, sclBytes[wavStart..wavEnd]);
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.ToString());
            Console.WriteLine("Couldn't convert scl to wav.");
            File.Delete(wavFullPath);
        }
    }

    private static void CopyStream(Stream input, Stream output)
    {
        var buffer = new byte[0x2000];
        int len;
        while ((len = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, len);
        }
    }
}