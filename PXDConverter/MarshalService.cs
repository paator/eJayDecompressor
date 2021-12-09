using System;
using System.Runtime.InteropServices;

namespace PXDConverter
{
    //try-catch clauses are overused cause we don't know what exceptions may occur in external eJ_Tool.dll methods
    public static class MarshalService
    {

        //Integer parameters are not known, hence the names. They probably function as buffer offsets etc.
        //To use our paths with external ADecompress function, we need to marshal them as char*. String, StringBuilder, char[], byte[] won't work.
        [DllImport("eJ_Tool.dll", EntryPoint = "ADecompress", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private static extern int ADecompress([MarshalAs(UnmanagedType.LPStr)]string pxdPath, int offset, int b, int c, int d, int e, [MarshalAs(UnmanagedType.LPStr)]string tmpPath, int f, int g);


        public static void ConvertWavToRawDataBuffer(string wavPath, string bufferTmpPath)
        {
            try
            {
                ADecompress(wavPath, 0, 0x0001E9AD, 0, 0, 0x00049D40, bufferTmpPath, 1, 0x0AAD0000);
            }
            catch
            {
                Console.WriteLine("Error during creating temporary buffer file via using external eJay dll method. Are you sure that PXD file is valid?");
                Environment.Exit(-4);
            }
        }
    }
}
