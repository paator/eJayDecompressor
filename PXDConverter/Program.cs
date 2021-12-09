using System;
using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PXDConverter
{
    // thanks to https://stackoverflow.com/a/42440257/699934
    internal static class StreamReaderExtensions
    {
        public static IEnumerable<string> ReadUntil(this StreamReader reader, string delimiter)
        {
            List<char> buffer = new List<char>();
            CircularBuffer<char> delim_buffer = new CircularBuffer<char>(delimiter.Length);
            while (reader.Peek() >= 0)
            {
                char c = (char)reader.Read();
                delim_buffer.Enqueue(c);
                if (delim_buffer.ToString() == delimiter || reader.EndOfStream)
                {
                    if (buffer.Count > 0)
                    {
                        if (!reader.EndOfStream)
                        {
                            buffer.Add(c);
                            yield return new String(buffer.ToArray()).Substring(0, buffer.Count - delimiter.Length);
                        }
                        else
                        {
                            buffer.Add(c);
                            if (delim_buffer.ToString() != delimiter)
                                yield return new String(buffer.ToArray());
                            else
                                yield return new String(buffer.ToArray()).Substring(0, buffer.Count - delimiter.Length);
                        }
                        buffer.Clear();
                    }
                    continue;
                }
                buffer.Add(c);
            }
        }

        private class CircularBuffer<T> : Queue<T>
        {
            private int _capacity;

            public CircularBuffer(int capacity)
                : base(capacity)
            {
                _capacity = capacity;
            }

            new public void Enqueue(T item)
            {
                if (base.Count == _capacity)
                {
                    base.Dequeue();
                }
                base.Enqueue(item);
            }

            public override string ToString()
            {
                List<String> items = new List<string>();
                foreach (var x in this)
                {
                    items.Add(x.ToString());
                };
                return String.Join("", items);
            }
        }
    }
    public static class Program
    {

        private static string PxdHeader = "tPxD";
        
        private static char[] PxdHeaderAsArray = "tPxD".ToCharArray();
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

            MarshalService.InitializeDll();

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

                if (!argPath.Contains(".pxd", StringComparison.OrdinalIgnoreCase))
                {
                    argPath += ".pxd";
                }
            }

            Directory.CreateDirectory("converted_wav_files");

            //buffer used by .dll
            var tmpPath = Path.GetTempPath() + "\\pxd_converter.tmp";
            if (!Directory.Exists(tmpPath))
            {
                Directory.CreateDirectory(tmpPath);
            }

            if (recursiveSearch)
                ConvertMultipleFiles(argPath);
            else
                ConvertOneFile(argPath);

            MarshalService.CloseDll();

            // for debug: Directory.Delete(tmpPath, true);

            Console.WriteLine("Finished.");
        }

        private static List<string> ConvertMultiPXDFile(string argPath)
        {
            List<string> extractedPaths = new List<string>();
            using (System.IO.StreamReader sr = new System.IO.StreamReader(argPath))
            {
                int fileNumber = 0;
                // todo: read filename from the header
                // todo: make processing parallel
                foreach (var pxd in sr.ReadUntil(PxdHeader).AsParallel())
                {
                    if (pxd.Length > 0) {
                        string filePath = $"{Path.GetTempPath()}pxd_converter.tmp\\{Path.GetFileName(argPath)}-{++fileNumber}.pxd";
                        extractedPaths.Add(filePath);
                        Console.WriteLine($"Extract {fileNumber} part to file: {filePath}");
                        using (System.IO.StreamWriter sw = new System.IO.StreamWriter(filePath))
                        {
                            sw.WriteLine(PxdHeader + pxd);
                        }
                        // for debug: ConvertOneFile(filePath);
                    }
                }
            }

            return extractedPaths;
        }

        private static bool IsPXDFile(string argPath)
        {
            List<string> extractedPaths = new List<string>();
            using (System.IO.StreamReader sr = new System.IO.StreamReader(argPath))
            {
                if (!sr.EndOfStream)
                {
                    char[] buffer = new char[4];
                    sr.ReadBlock(buffer);
                    return buffer.SequenceEqual(PxdHeaderAsArray);
                }
            }

            return false;
        }

        private static void ConvertOneFile(string argPath)
        {
            Console.WriteLine($"Converting raw data file {argPath} to wav format...");

            string tmpPath = $"{Path.GetTempPath()}pxd_converter.tmp\\temp.wav";

            MarshalService.ConvertWavToRawDataBuffer(argPath, tmpPath);

            RawToWav(argPath, tmpPath);
        }

        private static void ConvertMultipleFiles(string argPath)
        {
            Console.WriteLine("Searching for all .pxd files in specified location...");

            if (string.IsNullOrEmpty(argPath))
                argPath = Directory.GetCurrentDirectory();

            List<string> filePaths = Directory.GetFiles(argPath, "*.*", SearchOption.AllDirectories).ToList().FindAll(path => IsPXDFile(path));

            if (filePaths.Count == 0)
            {
                Console.WriteLine("Couldn't find any .pxd file!");
                return;
            }

            foreach (var path in filePaths)
            {
                Console.WriteLine("Found file: " + path);
                ConvertMultiPXDFile(path);
                // ConvertOneFile was here
            }
        }

        private static void RawToWav(string pxdPath, string tmpPath)
        {
            var path = Directory.GetCurrentDirectory() + "\\converted_wav_files\\";
            var name = Path.GetFileName(pxdPath);

            var wavPath = path + name.Remove(name.Length - 3) + "wav";
            Console.WriteLine("Wav file path: " + wavPath);
            FileStream wavStream = null;
            BinaryWriter wavWriter = null;

            try
            {
                int length = (int)new FileInfo(tmpPath).Length;
                if (length == 0) {
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
                //File.Delete(wavPath);
            }
            finally
            {
                //File.Delete(tmpPath);
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
