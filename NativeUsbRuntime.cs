using System.Runtime.InteropServices;

namespace QuestStack;

internal static class NativeUsbRuntime
{
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct LibUsbVersion
    {
        public readonly ushort Major;
        public readonly ushort Minor;
        public readonly ushort Micro;
        public readonly ushort Nano;
        public readonly nint ReleaseCandidate;
        public readonly nint Description;
    }

    [DllImport("libusb-1.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint libusb_get_version();

    public static string GetVersion()
    {
        nint versionPointer = libusb_get_version();
        if (versionPointer == nint.Zero)
            throw new InvalidOperationException("libusb_get_version returned a null pointer.");

        LibUsbVersion version = Marshal.PtrToStructure<LibUsbVersion>(versionPointer);
        string releaseCandidate = Marshal.PtrToStringAnsi(version.ReleaseCandidate) ?? string.Empty;
        return $"{version.Major}.{version.Minor}.{version.Micro}.{version.Nano}{releaseCandidate}";
    }
}
