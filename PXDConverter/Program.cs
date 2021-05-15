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
        private static void Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("___  _ _ ___    ____ ____ __ _ _  _ ____ ____ ___ ____ ____");
            Console.WriteLine("|--' _X_ |__>   |___ [__] | \\|  \\/  |=== |--<  |  |=== |--<");
            Console.WriteLine();
            Console.ResetColor();
            
            
            if(args.Length < 1)
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
                    argPath = args[2];
                }
            }
            else
            {
                argPath = args[0];
                
                if(!argPath.Contains(".pxd", StringComparison.OrdinalIgnoreCase))
                {
                    argPath += ".pxd";
                }
            }

            Directory.CreateDirectory("converted_wav_files");
            
            //buffer used by .dll
            var tmpPath = Directory.GetCurrentDirectory() + "\\converted_wav_files\\tmp_f.tmp";

            if (recursiveSearch)
                ConvertMultipleFiles(argPath, tmpPath);
            else
                ConvertOneFile(argPath, tmpPath);

            MarshalService.CloseDll();
            
            Console.WriteLine("Finished.");
        }

        private static void ConvertOneFile(string argPath, string tmpPath)
        {
            MarshalService.ConvertWavToRawDataBuffer(argPath, tmpPath);
            
            Console.WriteLine("Converting raw data to wav format...");

            RawToWav(argPath, tmpPath);
        }

        private static void ConvertMultipleFiles(string argPath, string tmpPath)
        {
            Console.WriteLine("Searching for all .pxd files in specified location...");

            if (string.IsNullOrEmpty(argPath))
                argPath = Directory.GetCurrentDirectory();
            
            string[] filePaths = Directory.GetFiles(argPath, "*.pxd", SearchOption.AllDirectories);

            if (filePaths.Length == 0)
            {
                Console.WriteLine("Couldn't find any .pxd file!");
                return;
            }
            
            foreach (var path in filePaths)
            {
                Console.WriteLine("Found file: " + path);

                ConvertOneFile(path, tmpPath);
            }
        }

        private static void RawToWav(string pxdPath, string tmpPath)
        {
            var path = Directory.GetCurrentDirectory() + "\\converted_wav_files\\";
            var name = Path.GetFileName(pxdPath);
            
            var wavPath = path + name.Remove(name.Length - 3) + "wav";
            FileStream wavStream = null;
            BinaryWriter wavWriter = null;
            
            try
            {
                wavStream = new FileStream(wavPath, FileMode.Create);
                wavWriter = new BinaryWriter(wavStream);
                int length = (int) new FileInfo(tmpPath).Length;
                int riffSize = length + 0x24;

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
            catch
            {
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