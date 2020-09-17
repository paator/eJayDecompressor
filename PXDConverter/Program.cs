using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PXDConverter
{
    public class Program
    {
        static void Main(string[] args)
        {
            if(args.Length != 1)
            {
                Console.WriteLine("You need 1 argument to run this program:\nPXD file path.");
                Environment.Exit(-1);
            }

            var pxdPath = args[0];
            var tmpPath = "tmp_f.tmp";

            Console.WriteLine("Initializing pxd32d5_d4.dll");
            Initialize();

            Console.WriteLine("Creating TMP-File (RAW): " + tmpPath);
            WavToTemp(pxdPath, tmpPath, 0, 0, 0, 0, 0);

            Console.WriteLine("Converting TMP-File (RAW) to (WAV)...");
            RawToWav(tmpPath);

            Console.WriteLine("Closing.");
            Close();
        }

        [DllImport("pxd32d5_d4.dll", EntryPoint = "PInit", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int Initialize();

        [DllImport("pxd32d5_d4.dll", EntryPoint = "RWavToTemp", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int WavToTemp([MarshalAs(UnmanagedType.LPStr)]String pxdPath, [MarshalAs(UnmanagedType.LPStr)]String tmpPath, int a, int b, int c, int d, int f);

        [DllImport("pxd32d5_d4.dll", EntryPoint = "PClose", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int Close();

        public static void RawToWav(string tmpPath)
        {
            FileStream f = new FileStream("output.wav", FileMode.Create);
            BinaryWriter wr = new BinaryWriter(f);

            int length = (int) new FileInfo(tmpPath).Length;
            int sampleCount = length / 2;
            int riffSize = sampleCount * 2 + 0x24;
            int dataSize = sampleCount * 2;

            wr.Write(Encoding.ASCII.GetBytes("RIFF"));
            wr.Write(riffSize);
            wr.Write(Encoding.ASCII.GetBytes("WAVE"));
            wr.Write(Encoding.ASCII.GetBytes("fmt "));
            wr.Write(16);
            wr.Write((short)1); // Encoding: PCM
            wr.Write((short)1); // Channels: MONO
            wr.Write(44100); // Sample rate: 44100
            wr.Write(44100 * 2); // Average bytes per second
            wr.Write((short)2); // block align
            wr.Write((short)16); // bits per sample
            wr.Write(Encoding.ASCII.GetBytes("data"));
            wr.Write(dataSize);

            using(FileStream fs = File.OpenRead(tmpPath))
            {
                CopyStream(fs, f);
            }

            f.Close();
            wr.Close();
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
    }
}