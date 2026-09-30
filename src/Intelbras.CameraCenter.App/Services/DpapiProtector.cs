using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Intelbras.CameraCenter.App.Services;

public static class DpapiProtector
{
    private const int CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn,
        string? szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return "";

        var bytes = Encoding.UTF8.GetBytes(plainText);
        var input = ToBlob(bytes);
        try
        {
            if (!CryptProtectData(ref input, "Intelbras Camera Center", IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, CryptProtectUiForbidden, out var output))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                return Convert.ToBase64String(FromBlob(output));
            }
            finally
            {
                LocalFree(output.pbData);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input.pbData);
        }
    }

    public static string Unprotect(string cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
            return "";

        try
        {
            var input = ToBlob(Convert.FromBase64String(cipherText));
            try
            {
                if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        IntPtr.Zero, CryptProtectUiForbidden, out var output))
                    return "";

                try
                {
                    return Encoding.UTF8.GetString(FromBlob(output));
                }
                finally
                {
                    LocalFree(output.pbData);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(input.pbData);
            }
        }
        catch
        {
            return "";
        }
    }

    private static DataBlob ToBlob(byte[] data)
    {
        var pointer = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, pointer, data.Length);
        return new DataBlob { cbData = data.Length, pbData = pointer };
    }

    private static byte[] FromBlob(DataBlob blob)
    {
        var data = new byte[blob.cbData];
        Marshal.Copy(blob.pbData, data, 0, blob.cbData);
        return data;
    }
}
