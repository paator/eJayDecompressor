using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PXDConverter
{
    public static class Program
    {
        static void Main(string[] args)
        {
            if(args.Length != 1)
            {
                Console.WriteLine("You need 1 argument to run this program:\nPXD file path.\nKeep in mind that temporary raw file will be created in the same dir path.");
                Environment.Exit(-1);
            }

            var pxdPath = args[0];

            if(!pxdPath.Contains(".pxd", StringComparison.OrdinalIgnoreCase))
            {
                pxdPath += ".pxd";
            }

            var tmpPath = "tmp_f.tmp";

            Console.WriteLine("Initializing pxd32d5_d4.dll");

            //try-catch clauses cause we don't know what exceptions may occur in external pxd32d5_d4.dll methods
            try
            {
                Initialize();
            }
            catch
            {
                Console.WriteLine("Couldn't find or initialize pxd32d5_d4.dll. Be sure to locate it in the same location as Converter exe file.");
                Environment.Exit(-2);
            }

            Console.WriteLine("Creating temporary file (raw data): " + tmpPath);

            try
            {
                WavToTemp(pxdPath, tmpPath, 0, 0, 0, 0, 0);
            }
            catch
            {
                Console.WriteLine("Error during creating temporary file via using external eJay dll method. Are you sure that PXD file is valid?");
                Environment.Exit(-3);
            }

            Console.WriteLine("Converting raw data to wav format...");
            RawToWav(pxdPath, tmpPath);

            Console.WriteLine("Closing.");
            try
            {
                Close();
            }
            catch
            {
                Console.WriteLine("Couldn't close the pxd32d5_d4.dll!");
                Environment.Exit(-4);
            }

            Console.WriteLine("Done!");
        }

        [DllImport("pxd32d5_d4.dll", EntryPoint = "PInit", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int Initialize();

        //Integer parameters are not known, hence the names. They probably function as buffer offsets etc.
        //To use our paths with external RWavToTemp function, we need to marshal them as char*. String, StringBuilder, char[], byte[] won't work.
        [DllImport("pxd32d5_d4.dll", EntryPoint = "RWavToTemp", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int WavToTemp([MarshalAs(UnmanagedType.LPStr)]String pxdPath, [MarshalAs(UnmanagedType.LPStr)]String tmpPath, int a, int b, int c, int d, int f);

        [DllImport("pxd32d5_d4.dll", EntryPoint = "PClose", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int Close();

        public static void RawToWav(string pxdPath, string tmpPath)
        {
            string wavPath = pxdPath.Remove(pxdPath.Length - 3) + "wav";
            FileStream wavStream = new FileStream(wavPath, FileMode.Create);
            BinaryWriter wavWriter = new BinaryWriter(wavStream);

            int length = (int) new FileInfo(tmpPath).Length;
            int riffSize = length + 0x24;

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

            using(FileStream fs = File.OpenRead(tmpPath))
            {
                CopyStream(fs, wavStream);
            }

            wavStream.Close();
            wavWriter.Close();

            Console.WriteLine("Created new wav file: " + wavPath);
        }

        public static void CopyStream(Stream input, Stream output)
        {
            byte[] buffer = new byte[8 * 1024];
            int len;
            while ((len = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, len);
            }
        }

        public static bool Contains(this string source, string toCheck, StringComparison comp)
        {
            return source?.IndexOf(toCheck, comp) >= 0;
        }
    }
}