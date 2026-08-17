using System;
using System.Text;

namespace PdfInspector.Interop;

/// <summary>
/// UTF-8 marshalling helpers shared by every P/Invoke call.
/// </summary>
/// <remarks>
/// Hand-rolled rather than using <see cref="System.Runtime.InteropServices.Marshal"/>
/// so the same code path works on netstandard2.0, where the UTF-8 helpers are
/// unavailable.
/// </remarks>
internal static unsafe class Utf8
{
    /// <summary>
    /// Encodes <paramref name="value"/> as a NUL-terminated UTF-8 buffer, or
    /// returns <see langword="null"/> when <paramref name="value"/> is null so
    /// the caller can pass a null pointer through to the native side.
    /// </summary>
    internal static byte[]? ToNullTerminated(string? value)
    {
        if (value is null)
        {
            return null;
        }

        int byteCount = Encoding.UTF8.GetByteCount(value);
        byte[] buffer = new byte[byteCount + 1];
        Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, 0);
        buffer[byteCount] = 0;
        return buffer;
    }

    /// <summary>
    /// Copies a NUL-terminated UTF-8 string out of native memory.
    /// </summary>
    internal static string FromPointer(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
        {
            return string.Empty;
        }

        byte* start = (byte*)pointer;
        int length = 0;
        while (start[length] != 0)
        {
            length++;
        }

        return length == 0 ? string.Empty : Encoding.UTF8.GetString(start, length);
    }
}
