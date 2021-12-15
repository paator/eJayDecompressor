using System;
using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PXDConverter
{
    public static class Program
    {

        private static string PxdHeader = "tPxD";
        private static string TmpPath = Path.GetTempPath() + "\\pxd_converter.tmp\\";

        enum SampleType
        {
            Beats,
            Bass,
            Keys,
            Spheres,
            Guitar,
            Male,
            Female,
            Extra,
            Fx
        }

        private static void Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("___  _ _ ___    ____ ____ __ _ _  _ ____ ____ ___ ____ ____");
            Console.WriteLine("|--' _X_ |__>   |___ [__] | \\|  \\/  |=== |--<  |  |=== |--<");
            Console.WriteLine();
            Console.ResetColor();


            if (args.Length < 1)
            {
                Console.WriteLine(
                    "You at least need 1 argument to run this program: PXD file path.\nIf you wish to convert multiple files at once, please " +
                    "use -all argument.");

                Console.WriteLine();
                Console.WriteLine("USAGE: ");
                Console.WriteLine();

                Console.WriteLine("PXDConverter.exe <pxd_file_location>");
                Console.WriteLine("PXDConverter.exe -all <folder_location>\n" +
                                  "Note: If <folder_location> won't be specified, program will try to use the path where program " +
                                  "was executed.");

                Environment.Exit(-1);
            }

            var argPath = string.Empty;
            bool recursiveSearch = false;

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

            Directory.CreateDirectory("converted_wav_files");

            //buffer used by .dll
            Directory.CreateDirectory(TmpPath);

            if (recursiveSearch)
                FindFilesToDecompress(argPath);
            else
            {
                if (argPath.EndsWith(".pxd"))
                {
                    var lib = new PXD32Library();
                    lib.InitializeDll();
                    ProcessSinglePXDFile(argPath, lib);
                    lib.CloseDll();
                }
                else if (argPath.EndsWith("inf.bin"))
                {
                    var lib = new EjToolLibrary();
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

        private static void ProcessSinglePXDFile(string argPath, PXD32Library lib)
        {
            // it can be already wav file
            if (ASCIIEncoding.ASCII.GetString(ReadFileFromTo(argPath, 0, 4)) == "RIFF")
            {
                string dir = $"{Directory.GetCurrentDirectory()}\\converted_wav_files\\";
                Directory.CreateDirectory(dir);
                string filePath = $"{dir}{Path.GetFileNameWithoutExtension(argPath)}.wav";
                if (!File.Exists(filePath))
                {
                    File.Copy(argPath, filePath);
                }
            }
            else
            {
                Console.WriteLine($"Converting raw data file {argPath} to wav format...");

                string tmpPath = TmpPath.ToString() + "tmp.wav";

                lib.Decompress(argPath, 0, 0, 0, 0, 0, tmpPath);

                RawToWav(argPath, tmpPath);
            }
        }

        private record PXDHeader(string name, string package, string type, int samples, bool isWave);

        private static PXDHeader readPXDFileHeader(string filepath, byte[] bytes)
        {
            bool isWave = ASCIIEncoding.ASCII.GetString(bytes[0..4]) == "RIFF";
            // rest is useless if it's wav file, but anyway
            int p = 4;
            (p, byte headerSize) = processBytes(bytes, p, 1, b => b[0]);
            (p, byte[] headerBytes) = readBytes(bytes, p, headerSize);
            headerBytes = headerBytes.TakeWhile(c => c != 0).ToArray();
            p += 1;
            (p, int samples) = processBytes(bytes, p, 4, b => BitConverter.ToInt32(b));
            string[] s = Regex.Split(ASCIIEncoding.ASCII.GetString(headerBytes), "\x0D\x0A");
            if (s[s.Length - 1].EndsWith(".pxd"))
            {
                return new PXDHeader(s[s.Length - 1][..^4], s.ElementAtOrDefault(s.Length - 2) ?? string.Empty, string.Empty, samples, isWave);
            }
            else if (s.Length < 3)
            {
                return new PXDHeader(s[s.Length - 1], Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(filepath))!) + " DEMO", Path.GetFileName(Path.GetDirectoryName(filepath)!), samples, isWave);
            }
            else
            {
                return new PXDHeader(s[0] + (s.ElementAtOrDefault(1) ?? string.Empty), $"{s.ElementAtOrDefault(2) ?? string.Empty} {s.ElementAtOrDefault(3) ?? string.Empty}".Trim(), s.ElementAtOrDefault(4) ?? string.Empty, samples, isWave);
            }
        }

        private static (int, R) processBytes<R>(byte[] arr, int p, int size, Func<byte[], R> convert)
        {
            R res = convert(arr[p..(p + size)]);
            return (p + size, res);
        }


        private static (int, byte[]) readBytes(byte[] arr, int p, int size)
        {
            return (p + size, arr[p..(p + size)]);
        }

        private static bool isLastRecord(byte[] descriptorBytes, int p)
        {
            p += 8;
            (p, short sampleRateByte) = processBytes(descriptorBytes, p, 2, b => BitConverter.ToInt16(b));
            return sampleRateByte == 0;
        }

        // sampleRate or maybe samplesCount? not sure
        private record MultiPXDRecord(int id, int group, string name, int offset, int lenght, int sampleRateByte, int type);

        private static byte[] ReadFileFromTo(string fileName, int offset, int lenght)
        {
            byte[] fileData;
            using (FileStream fs = File.OpenRead(fileName))
            {
                fs.Position = offset;
                using (BinaryReader binaryReader = new BinaryReader(fs))
                {
                    fileData = binaryReader.ReadBytes(lenght);
                }
            }
            return fileData;
        }

        private static (int, MultiPXDRecord) readBinaryPXDRecord(byte[] descriptorBytes, int p)
        {
            (p, byte[] fileSeparator) = readBytes(descriptorBytes, p, 2);
            (p, byte id) = processBytes(descriptorBytes, p, 1, b => b[0]);
            (p, byte group) = processBytes(descriptorBytes, p, 1, b => b[0]);
            p += 2; // serarator
            (p, short nameLenght) = processBytes(descriptorBytes, p, 2, b => BitConverter.ToInt16(b));
            (p, string name) = processBytes(descriptorBytes, p, nameLenght, b => ASCIIEncoding.UTF8.GetString(b));
            (p, short sampleRateByte) = processBytes(descriptorBytes, p, 2, b => BitConverter.ToInt16(b));
            (p, short type) = processBytes(descriptorBytes, p, 2, b => BitConverter.ToInt16(b));
            (p, int offset) = processBytes(descriptorBytes, p, 4, b => BitConverter.ToInt32(b));
            (p, int lenght) = processBytes(descriptorBytes, p, 4, b => BitConverter.ToInt32(b));
            // 1 for first in stereo
            (p, short wtf1) = processBytes(descriptorBytes, p, 2, b => BitConverter.ToInt16(b));
            (p, short wtf2) = processBytes(descriptorBytes, p, 2, b => BitConverter.ToInt16(b));
            (p, int channels1) = processBytes(descriptorBytes, p, 4, b => BitConverter.ToInt32(b));
            (p, short wtf3) = processBytes(descriptorBytes, p, 2, b => BitConverter.ToInt16(b));
            (p, int wtf4) = processBytes(descriptorBytes, p, 4, b => BitConverter.ToInt32(b));
            (p, short channels2) = processBytes(descriptorBytes, p, 2, b => BitConverter.ToInt16(b));
            p += 8; // it always 00 00 00 00 00 00 FF FF
            Debug.WriteLine($"[{group}:{id}] '{name}'[{nameLenght}] offset: {offset} lenght: {lenght} sampleRateByte: {sampleRateByte}, type2: {type}");
            return (p, new MultiPXDRecord(id, group, name, offset, lenght, sampleRateByte, type));
        }

        private static void ProcessMultiWithBinaryInf(string descriptorPath, EjToolLibrary lib)
        {
            Console.WriteLine($"Work with descriptor file '{descriptorPath}'");
            string binFilePrefix = Path.GetFileNameWithoutExtension(descriptorPath)[..^3];
            string part = binPathPart(descriptorPath, binFilePrefix);
            byte[] descriptorBytes = File.ReadAllBytes(descriptorPath);
            int p = 8;
            List<(MultiPXDRecord, PXDHeader, string)> records = new List<(MultiPXDRecord, PXDHeader, string)>();
            while (!isLastRecord(descriptorBytes, p))
            {
                (p, MultiPXDRecord record) = readBinaryPXDRecord(descriptorBytes, p);
                string binPath = $"{Path.GetDirectoryName(descriptorPath)}\\{binFilePrefix}{part}";
                if (record.lenght != 0)
                {
                    PXDHeader header = readPXDFileHeader(binPath, ReadFileFromTo(binPath, record.offset, record.lenght));
                    if (part.Length > 0 && records.Count > 0 && record.offset == 0)
                    {
                        // switch to B binary file
                        part = ((char)(part[0] + 1)).ToString();
                    }
                    records.Add((record, header, part));
                }
            }
            records.Sort((a, b) => a.Item1.id.CompareTo(b.Item1.id));
            ProcessRecordsAndHeaders(records, descriptorPath, lib, binFilePrefix, true);
        }

        private static string binPathPart(string descriptorPath, string binFilePrefix)
        {
            string part = "";
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
                throw new Exception("This discriptor is not in directory with binary MultiPXD file");
            }
            return part;
        }

        private static string removeBadCharsFromFilename(string filename)
        {
            return string.Join("", filename.Trim().Split(Path.GetInvalidFileNameChars())).Trim();
        }

        private static void ProcessRecordsAndHeaders(List<(MultiPXDRecord, PXDHeader, string)> records, string descriptorPath, EjToolLibrary lib, string binFilePrefix, bool binaryHeader)
        {
            records.Sort((a, b) => (a.Item1.group, a.Item1.id).CompareTo((b.Item1.group, b.Item1.id)));
            int files = 0;
            for (int index = 0; index < records.Count; index++)
            {
                var (record, header, part) = records.ElementAt(index);
                MultiPXDRecord? stereo = null;
                int channelSize = 0;
                bool isStereo = false;

                if (index + 1 < records.Count && records.ElementAt(index + 1).Item1.name.Length == 0)
                {
                    isStereo = true;
                }
                else if (record.name.Length > 1 && index + 1 < records.Count && records.ElementAt(index + 1).Item1.name.Length > 1 &&
                     (records.ElementAt(index + 1).Item1.name[^1] == 'R' || records.ElementAt(index + 1).Item1.name[^1] == 'L') && records.ElementAt(index + 1).Item1.name[..^1] == record.name[..^1])
                {
                    channelSize = 1;
                    isStereo = true;
                }
                else if (index + 1 < records.Count && records.ElementAt(index + 1).Item1.name.Length > 3 && record.name.Length > 3 &&
                    (records.ElementAt(index + 1).Item1.name[..^3] == "(R)" || records.ElementAt(index + 1).Item1.name[..^3] == "(L)") && records.ElementAt(index + 1).Item1.name[..^3] == record.name[..^3])
                {
                    channelSize = 3;
                    isStereo = true;
                }

                if (isStereo)
                {
                    stereo = records.ElementAt(index + 1).Item1;
                    index++;
                }

                string filenameWithoutBadChars = removeBadCharsFromFilename(record.name[..^channelSize]);
                string binPath = $"{Path.GetDirectoryName(descriptorPath)}\\{binFilePrefix}{part}";
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
                string outputFile = $"{dir}\\{filenameWithoutBadChars}.wav";
                if (!File.Exists(outputFile))
                {
                    if (header.isWave) {
                        File.WriteAllBytes(outputFile, ReadFileFromTo(binPath, record.offset, record.lenght));
                    } else {
                        lib.Decompress(binPath, record.offset, record.lenght, stereo?.offset ?? 0, stereo?.lenght ?? 0, 2 * header.samples * (stereo != null ? 2 : 1), outputFile);
                    }
                    if (!File.Exists(outputFile))
                    {
                        Console.WriteLine($"Warning: '{outputFile}' is not exists after decompression");
                    }
                }
                files++;
            }
            Console.WriteLine($"Totat files extracted: {files}");
        }


        private static MultiPXDRecord readTextPXDRecord(string[] descriptorLines, int p)
        {
            int id = Int32.Parse(descriptorLines[p]);
            int group = Int32.Parse(descriptorLines[p + 1]);
            string pxdFilename = descriptorLines[p + 2][1..^1]; // remove ""
            int offset = Int32.Parse(descriptorLines[p + 3]);
            int lenght = Int32.Parse(descriptorLines[p + 4]);
            string nameLine1 = descriptorLines[p + 5][1..^1]; // remove ""
            string nameLine2 = descriptorLines[p + 6][1..^1]; // remove ""
            string name = nameLine1 + nameLine2;
            int sampleRateByte = Int32.Parse(descriptorLines[p + 7]);
            int type = Int32.Parse(descriptorLines[p + 8]);
            Debug.WriteLine($"[{group}:{id}] '{name}' offset: {offset} lenght: {lenght} sampleRateByte: {sampleRateByte}, type2: {type}");
            return new MultiPXDRecord(id, group, name, offset, lenght, sampleRateByte, type);
        }

        private static void ProcessMultiWithTextInf(string descriptorPath, EjToolLibrary lib)
        {
            Console.WriteLine($"Work with descriptor file '{descriptorPath}'");
            string binFilePrefix = Path.GetFileNameWithoutExtension(descriptorPath);
            string part = binPathPart(descriptorPath, binFilePrefix);
            string[] descriptorLines = File.ReadAllLines(descriptorPath);
            List<(MultiPXDRecord, PXDHeader, string)> records = new List<(MultiPXDRecord, PXDHeader, string)>();
            for (int i = 14; i < descriptorLines.Length; i += 12)
            {
                MultiPXDRecord record = readTextPXDRecord(descriptorLines, i);
                if (record.lenght != 0)
                {
                    string binPath = $"{Path.GetDirectoryName(descriptorPath)}\\{binFilePrefix}{part}";
                    PXDHeader header = readPXDFileHeader(binPath, ReadFileFromTo(binPath, record.offset, record.lenght));
                    if (part.Length > 0 && records.Count > 0 && record.offset == 0)
                    {
                        // switch to B binary file
                        part = ((char)(part[0] + 1)).ToString();
                    }
                    records.Add((record, header, part));
                }
            }
            ProcessRecordsAndHeaders(records, descriptorPath, lib, binFilePrefix, false);
        }

        private static void ProcessFiles(string[] pxdFilesPaths, string[] pxdFilesHeaderMultiWithTextInfPaths, string[] pxdFilesHeaderMultiWithBinaryInfPaths, string[] sclPaths)
        {
            if (pxdFilesPaths.Length == 0 && pxdFilesHeaderMultiWithTextInfPaths.Length == 0 && pxdFilesHeaderMultiWithBinaryInfPaths.Length == 0 && sclPaths.Length == 0)
            {
                Console.WriteLine("Couldn't find anything to decompress!");
                return;
            }

            if (pxdFilesPaths.Length > 0)
            {
                var pxd32lib = new PXD32Library();
                pxd32lib.InitializeDll();
                foreach (var path in pxdFilesPaths)
                {
                    Console.WriteLine($"Process PXD file: {path}");
                    ProcessSinglePXDFile(path, pxd32lib);
                }
                pxd32lib.CloseDll();
            }

            foreach (var path in sclPaths)
            {
                Console.WriteLine($"Process SCL file: {path}");
                SclToWav(path);
            }

            if (pxdFilesHeaderMultiWithTextInfPaths.Length > 0 || pxdFilesHeaderMultiWithBinaryInfPaths.Length > 0)
            {
                var lib = new EjToolLibrary();

                if (pxdFilesHeaderMultiWithBinaryInfPaths.Length > 0)
                {
                    foreach (var path in pxdFilesHeaderMultiWithBinaryInfPaths)
                    {
                        Console.WriteLine($"Process MultiPXD binary header file (text): {path}");
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
                        Console.WriteLine($"Process MultiPXD text header file (text): {path}");
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

            string[] pxdFilesPaths = Directory.GetFiles(argPath, "*.pxd", SearchOption.AllDirectories);
            string[] pxdFilesHeaderMultiWithTextInfPaths = Directory.GetFiles(argPath, "*0.inf", SearchOption.AllDirectories);
            string[] pxdFilesHeaderMultiWithBinaryInfPaths = Directory.GetFiles(argPath, "*inf.bin", SearchOption.AllDirectories);
            string[] sclFilesPaths = Directory.GetFiles(argPath, "*.scl", SearchOption.AllDirectories);

            ProcessFiles(pxdFilesPaths, pxdFilesHeaderMultiWithTextInfPaths, pxdFilesHeaderMultiWithBinaryInfPaths, sclFilesPaths);
        }

        private static void RawToWav(string pxdPath, string tmpPath)
        {
            var path = "converted_wav_files";
            var header = readPXDFileHeader(pxdPath, File.ReadAllBytes(pxdPath));
            string wavPath;
            string wavFullPath;
            if (header.name == "45" && header.package == "Hyper2")
            {
                wavPath = $"{path}\\{Path.GetFileName(Path.GetDirectoryName(pxdPath))!}";
                wavFullPath = $"{wavPath}\\{removeBadCharsFromFilename(Path.GetFileNameWithoutExtension(pxdPath))}.wav";
            }
            else if (header.name.Contains("\\"))
            {
                wavFullPath = $"{path}\\{removeBadCharsFromFilename(header.package)}\\{header.name}.wav";
                wavPath = Path.GetDirectoryName(wavFullPath)!;
            }
            else
            {
                wavPath = $"{path}\\{removeBadCharsFromFilename(header.package)}\\{removeBadCharsFromFilename(header.type)}";
                wavFullPath = $"{wavPath}\\{removeBadCharsFromFilename(header.name)}.wav";
            }

            Directory.CreateDirectory(wavPath);
            Debug.WriteLine(wavFullPath);
            if (!File.Exists(wavFullPath))
            {
                Console.WriteLine("Wav file path: " + wavFullPath);
                FileStream? wavStream = null;
                BinaryWriter? wavWriter = null;

                try
                {
                    int length = (int)new FileInfo(tmpPath).Length;
                    if (length == 0)
                    {
                        throw new Exception("raw wav file is empty, looks like file convertation was failed");
                    }
                    int riffSize = length + 0x24;

                    wavStream = new FileStream(wavFullPath, FileMode.Create);
                    wavWriter = new BinaryWriter(wavStream);
                    wavWriter.Write(Encoding.ASCII.GetBytes("RIFF"));
                    wavWriter.Write(riffSize);
                    wavWriter.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                    wavWriter.Write(16);
                    wavWriter.Write((short)1); // Encoding: PCM
                    wavWriter.Write((short)1); // Channels: MONO
                    wavWriter.Write(44100); // Sample rate: 44100
                    wavWriter.Write(88200); // Average bytes per second
                    wavWriter.Write((short)2); // Block align
                    wavWriter.Write((short)16); // Bits per sample
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
                    Console.WriteLine("Couldn't convert raw data to wav. Perhaps .wav provided path is invalid?");
                    File.Delete(wavFullPath);
                }
                finally
                {
                    File.Delete(tmpPath);
                }
            }
        }

        private static void SclToWav(string sclPath)
        {
            var path = "converted_wav_files";
            string wavPath = $"{path}\\{Path.GetDirectoryName(Path.GetDirectoryName(sclPath))}\\{Path.GetFileName(Path.GetDirectoryName(sclPath))}";
            string wavFullPath = $"{wavPath}\\{removeBadCharsFromFilename(Path.GetFileNameWithoutExtension(sclPath))}.wav";
            Debug.WriteLine(wavFullPath);
            if (!File.Exists(wavFullPath))
            {
                Console.WriteLine("Wav file path: " + wavFullPath);

                try
                {
                    byte[] sclBytes = File.ReadAllBytes(sclPath);
                    string sclAsString = ASCIIEncoding.ASCII.GetString(sclBytes);
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
        }

        private static void CopyStream(Stream input, Stream output)
        {
            byte[] buffer = new byte[0x2000];
            int len;
            while ((len = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, len);
            }
        }

        private static bool Contains(this string source, string toCheck, StringComparison comp)
        {
            return source?.IndexOf(toCheck, comp) >= 0;
        }
    }
}
