using System.Runtime.InteropServices;

namespace eJayDecompressor;

//try-catch clauses are overused cause we don't know what exceptions may occur in external pxd32d5_d4.dll methods
public class Pxd32Library
{
    [DllImport("pxd32d5_d4.dll", EntryPoint = "PInit", CharSet = CharSet.Unicode,
        CallingConvention = CallingConvention.StdCall)]
    private static extern int Initialize();

    //Integer parameters are not known, hence the names. They probably function as buffer offsets etc.
    //To use our paths with external RWavToTemp function, we need to marshal them as char*. String, StringBuilder, char[], byte[] won't work.
    [DllImport("pxd32d5_d4.dll", EntryPoint = "RWavToTemp", CharSet = CharSet.Unicode,
        CallingConvention = CallingConvention.StdCall)]
    private static extern int WavToTemp([MarshalAs(UnmanagedType.LPStr)] string pxdPath,
        [MarshalAs(UnmanagedType.LPStr)] string tmpPath, int a, int b, int c, int d, int f);

    [DllImport("pxd32d5_d4.dll", EntryPoint = "PClose", CharSet = CharSet.Unicode,
        CallingConvention = CallingConvention.StdCall)]
    private static extern int Close();

    public static void InitializeDll()
    {
        Console.WriteLine("Initializing pxd32d5_d4.dll");

        try
        {
#pragma warning disable CA1806
            Initialize();
#pragma warning restore CA1806
        }
        catch
        {
            Console.WriteLine(
                "Couldn't find or initialize pxd32d5_d4.dll. Be sure to locate it in the same location as Converter exe file.");
            Environment.Exit(-2);
        }
    }

    public static void CloseDll()
    {
        Console.WriteLine("Closing .dll file...");

        try
        {
#pragma warning disable CA1806
            Close();
#pragma warning restore CA1806
        }
        catch
        {
            Console.WriteLine("Couldn't close the pxd32d5_d4.dll!");
            Environment.Exit(-5);
        }
    }

    public void Decompress(string pxdPath, string outputPath)
    {
        try
        {
#pragma warning disable CA1806
            WavToTemp(pxdPath, outputPath, 0, 0, 0, 0, 0);
#pragma warning restore CA1806
        }
        catch
        {
            Console.WriteLine(
                "Error during creating temporary buffer file via using external eJay dll method. Are you sure that PXD file is valid?");
            Environment.Exit(-3);
        }
    }
}

public class EjToolLibrary
{
    [DllImport("eJ_Tool.dll", EntryPoint = "ADecompress", CharSet = CharSet.Unicode,
        CallingConvention = CallingConvention.StdCall)]
    private static extern int ADecompress([MarshalAs(UnmanagedType.LPStr)] string pxdPath, int leftOffset,
        int leftSize, int rightOffset, int rightLenght, int sampleRate,
        [MarshalAs(UnmanagedType.LPStr)] string tmpPath);

    public static void Decompress(string pxdPath, int leftOffset, int leftSize, int rightOffset, int rightSize,
        int sampleRate, string outputPath)
    {
        try
        {
            ADecompress(pxdPath, leftOffset, leftSize, rightOffset, rightSize, sampleRate, outputPath);
        }
        catch (Exception e)
        {
            Console.WriteLine(
                $"Error during creating temporary buffer file via using external eJay dll method. Are you sure that PXD file is valid? Detailed message: {e.Message}");
            Environment.Exit(-4);
        }
    }
}