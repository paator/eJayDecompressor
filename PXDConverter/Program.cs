using System;
using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PXDConverter
{
    public static class Program
    {

        private static string PxdHeader = "tPxD";

        private static char[] PxdHeaderAsArray = "tPxD".ToCharArray();
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
            if (!Directory.Exists(TmpPath))
            {
                Directory.CreateDirectory(TmpPath);
            }

            if (recursiveSearch)
                FindMultipleFiles(argPath);
            else
            {
                // todo check if it is header
                var lib = new PXD32Library();
                lib.InitializeDll();
                ProcessSinglePXDFile(argPath, lib);
                lib.CloseDll();
            }

            Directory.Delete(TmpPath, true);

            Console.WriteLine("Finished.");
        }

        private static void ProcessSinglePXDFile(string argPath, PXD32Library lib)
        {
            Console.WriteLine($"Converting raw data file {argPath} to wav format...");

            string tmpPath = TmpPath.ToString() + "tmp.wav";

            lib.Decompress(argPath, 0, 0, 0, 0, 0, tmpPath);

            // todo rename file from pxd
            RawToWav(argPath, tmpPath);
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

        private record PXDRecord(int id, int group, string name, int offset, int lenght, int sampleRate, int type);

        private static (int, PXDRecord) readBinaryPXDRecord(byte[] descriptorBytes, int p)
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
            p += 24; // some useless data for us
            //Console.WriteLine($"[{group}:{id}] '{name}'[{nameLenght}] offset: {offset} lenght: {lenght} sampleRateByte: {sampleRateByte}, type2: {type}");
            int sampleRate = sampleRateByte * 151200;
            return (p, new PXDRecord(id, group, name, offset, lenght, sampleRate, type));
        }

        private static void ProcessMultiWithBinaryInf(string descriptorPath, EjToolLibrary lib)
        {
            Console.WriteLine($"Work with descriptor file '{descriptorPath}'");

            string part = "";
            if (File.Exists($"{Path.GetDirectoryName(descriptorPath)}\\{Path.GetFileNameWithoutExtension(descriptorPath)[..^3]}a"))
            {
                part = "a";
            }
            else if (File.Exists($"{Path.GetDirectoryName(descriptorPath)}\\{Path.GetFileNameWithoutExtension(descriptorPath)[..^3]}"))
            {
                part = "";
            }
            else
            {
                Console.WriteLine("This discriptor is not in directory with binary MultiPXD file");
                return;
            }

            byte[] descriptorBytes = File.ReadAllBytes(descriptorPath);
            int p = 8;
            List<PXDRecord> records = new List<PXDRecord>();
            while (!isLastRecord(descriptorBytes, p))
            {
                (p, PXDRecord record) = readBinaryPXDRecord(descriptorBytes, p);
                records.Add(record);
            }
            int files = 0;
            HashSet<int> types = new HashSet<int>();
            for (int index = 0; index < records.Count; index++)
            {
                var item = records.ElementAt(index);
                types.Add(item.type);
                PXDRecord? stereo = null;
                if (index + 1 < records.Count && (records.ElementAt(index + 1).name.Length == 0))
                {
                    stereo = records.ElementAt(index + 1);
                    index++;
                }
                string filenameWithoutBadChars = string.Join("", item.name.Trim().Split(Path.GetInvalidFileNameChars()));
                string binPath = $"{Path.GetDirectoryName(descriptorPath)}\\{Path.GetFileNameWithoutExtension(descriptorPath)[..^3]}{part}";
                if (part.Length > 0 && files > 0 && (item.offset == 0 || (stereo?.offset ?? -1) == 0))
                {
                    // switch to B binary file
                    part = "b";
                }
                string dir = $"{Directory.GetCurrentDirectory()}\\converted_wav_files\\{Path.GetFileNameWithoutExtension(descriptorPath)[..^3]}\\{item.type}";
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string outputFile = $"{dir}\\{filenameWithoutBadChars}.wav";
                if (!File.Exists(outputFile))
                {
                    lib.Decompress(binPath, item.offset, item.lenght, stereo?.offset ?? 0, stereo?.lenght ?? 0, item.sampleRate * (stereo != null ? 2 : 1), outputFile);
                    if (!File.Exists(outputFile))
                    {
                        Console.WriteLine($"Warning: '{outputFile}' is not exists after decompression.");
                    }
                }
                files++;
            }
            Console.WriteLine($"Totat files extracted: {files}, types: {string.Join(",", types)}");
        }


        private static PXDRecord readTextPXDRecord(string[] descriptorLines, int p)
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
            //Console.WriteLine($"[{group}:{id}] '{name}' offset: {offset} lenght: {lenght} sampleRateByte: {sampleRateByte}, type2: {type}");
            int sampleRate = sampleRateByte * 151200;
            return new PXDRecord(id, group, name, offset, lenght, sampleRate, type);
        }

        private static void ProcessMultiWithTextInf(string descriptorPath, EjToolLibrary lib)
        {
            Console.WriteLine($"Work with descriptor file '{descriptorPath}'");


            string part = "";
            if (File.Exists($"{Path.GetDirectoryName(descriptorPath)}\\{Path.GetFileNameWithoutExtension(descriptorPath)}a"))
            {
                part = "a";
            }
            else if (File.Exists($"{Path.GetDirectoryName(descriptorPath)}\\{Path.GetFileNameWithoutExtension(descriptorPath)}"))
            {
                part = "";
            }
            else
            {
                Console.WriteLine("This discriptor is not in directory with binary MultiPXD file");
                return;
            }

            string[] descriptorLines = File.ReadAllLines(descriptorPath);
            List<PXDRecord> records = new List<PXDRecord>();
            for (int i = 14; i < descriptorLines.Length; i += 12)
            {
                records.Add(readTextPXDRecord(descriptorLines, i));
            }
            // stereo data stored not in sequence
            records.Sort((a, b) => a.id.CompareTo(b.id));
            int files = 0;
            HashSet<int> types = new HashSet<int>();
            for (int index = 0; index < records.Count; index++)
            {
                var item = records.ElementAt(index);
                types.Add(item.type);
                PXDRecord? stereo = null;
                if (index + 1 < records.Count && (records.ElementAt(index + 1).name.Length == 0 || records.ElementAt(index + 1).name == item.name[..^1] + 'R'))
                {
                    stereo = records.ElementAt(index + 1);
                    index++;
                }
                string filenameWithoutBadChars = string.Join("", item.name.Split(Path.GetInvalidFileNameChars()));
                if (stereo != null)
                {
                    // drop "L" for stereo files
                    filenameWithoutBadChars = filenameWithoutBadChars[..^1];
                }
                string binPath = $"{Path.GetDirectoryName(descriptorPath)}\\{Path.GetFileNameWithoutExtension(descriptorPath)}{part}";
                if (part.Length > 0 && files > 0 && (item.offset == 0 || (stereo?.offset ?? -1) == 0))
                {
                    // switch to B binary file
                    part = "b";
                }
                string dir = $"{Directory.GetCurrentDirectory()}\\converted_wav_files\\{Path.GetFileNameWithoutExtension(descriptorPath)}\\{item.type}";
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string outputFile = $"{dir}\\{filenameWithoutBadChars}.wav";
                if (!File.Exists(outputFile))
                {
                    lib.Decompress(binPath, item.offset, item.lenght, stereo?.offset ?? 0, stereo?.lenght ?? 0, item.sampleRate * (stereo != null ? 2 : 1), outputFile);
                    if (!File.Exists(outputFile))
                    {
                        Console.WriteLine($"Warning: '{outputFile}' is not exists after decompression.");
                    }
                }
                files++;
            }
            Console.WriteLine($"Totat files extracted: {files}, types: {string.Join(",", types)}");
        }

        private static void FindMultipleFiles(string argPath)
        {
            Console.WriteLine("Searching for all .pxd files in specified location...");

            if (string.IsNullOrEmpty(argPath))
                argPath = Directory.GetCurrentDirectory();

            string[] pxdFilesPaths = Directory.GetFiles(argPath, "*.pxd", SearchOption.AllDirectories);
            string[] pxdFilesHeaderMultiWithTextInfPaths = Directory.GetFiles(argPath, "*0.inf", SearchOption.AllDirectories);
            string[] pxdFilesHeaderMultiWithBinaryInfPaths = Directory.GetFiles(argPath, "*inf.bin", SearchOption.AllDirectories);

            if (pxdFilesPaths.Length == 0 && pxdFilesHeaderMultiWithTextInfPaths.Length == 0 && pxdFilesHeaderMultiWithBinaryInfPaths.Length == 0)
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

            if (pxdFilesHeaderMultiWithTextInfPaths.Length > 0 || pxdFilesHeaderMultiWithBinaryInfPaths.Length > 0)
            {
                var lib = new EjToolLibrary();

                if (pxdFilesHeaderMultiWithBinaryInfPaths.Length > 0)
                {
                    foreach (var path in pxdFilesHeaderMultiWithBinaryInfPaths)
                    {
                        Console.WriteLine($"Process MultiPXD header file (binary): {path}");
                        ProcessMultiWithBinaryInf(path, lib);
                    }
                }

                if (pxdFilesHeaderMultiWithTextInfPaths.Length > 0)
                {
                    foreach (var path in pxdFilesHeaderMultiWithTextInfPaths)
                    {
                        Console.WriteLine($"Process MultiPXD header file (text): {path}");
                        ProcessMultiWithTextInf(path, lib);
                    }
                }
            }
        }

        private static void RawToWav(string pxdPath, string tmpPath)
        {
            var path = Directory.GetCurrentDirectory() + "\\converted_wav_files\\";
            var name = Path.GetFileNameWithoutExtension(pxdPath);

            var wavPath = path + name + ".wav";
            if (!File.Exists(wavPath))
            {
                Console.WriteLine("Wav file path: " + wavPath);
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

                    wavStream = new FileStream(wavPath, FileMode.Create);
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
                    Console.WriteLine(e.ToString());
                    wavStream?.Close();
                    wavWriter?.Close();
                    Console.WriteLine("Couldn't convert raw data to wav. Perhaps .wav provided path is invalid?");
                    File.Delete(wavPath);
                }
                finally
                {
                    File.Delete(tmpPath);
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
