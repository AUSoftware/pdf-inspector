using System;

namespace PdfInspector;

/// <summary>
/// Why a PDF operation failed. Mirrors the <c>kind</c> discriminant on the
/// native error payload.
/// </summary>
public enum PdfErrorKind
{
    /// <summary>The native library reported a kind this binding does not know.</summary>
    Unknown = 0,

    /// <summary>An argument was null or otherwise unusable.</summary>
    InvalidArgument,

    /// <summary>The options payload could not be parsed by the native library.</summary>
    InvalidOptions,

    /// <summary>The file could not be read.</summary>
    Io,

    /// <summary>The PDF could not be parsed.</summary>
    Parse,

    /// <summary>
    /// The PDF is encrypted and no usable password was supplied. Set
    /// <see cref="PdfOptions.Password"/> and retry.
    /// </summary>
    Encrypted,

    /// <summary>The PDF's internal structure is broken (xref, object streams, …).</summary>
    InvalidStructure,

    /// <summary>The input is not a PDF.</summary>
    NotAPdf,

    /// <summary>
    /// The native library panicked. The panic was caught at the interop
    /// boundary, so the process is intact, but the operation produced no
    /// result. This indicates a bug worth reporting.
    /// </summary>
    Panic,

    /// <summary>The native library could not produce a response.</summary>
    Internal,
}

/// <summary>
/// Thrown when a PDF operation fails.
/// </summary>
public sealed class PdfInspectorException : Exception
{
    /// <summary>Creates an exception with no detail. Prefer the other constructors.</summary>
    public PdfInspectorException()
        : this(PdfErrorKind.Unknown, "unknown", "The PDF operation failed.")
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">Description of the failure.</param>
    public PdfInspectorException(string message)
        : this(PdfErrorKind.Unknown, "unknown", message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">Description of the failure.</param>
    /// <param name="innerException">The underlying cause.</param>
    public PdfInspectorException(string message, Exception innerException)
        : base(message, innerException)
    {
        Kind = PdfErrorKind.Unknown;
        NativeKind = "unknown";
    }

    internal PdfInspectorException(PdfErrorKind kind, string nativeKind, string message)
        : base(message)
    {
        Kind = kind;
        NativeKind = nativeKind;
    }

    internal PdfInspectorException(PdfErrorKind kind, string nativeKind, string message, Exception innerException)
        : base(message, innerException)
    {
        Kind = kind;
        NativeKind = nativeKind;
    }

    /// <summary>The failure category.</summary>
    public PdfErrorKind Kind { get; }

    /// <summary>
    /// The raw <c>kind</c> string from the native library. Useful when
    /// <see cref="Kind"/> is <see cref="PdfErrorKind.Unknown"/> because the
    /// native library is newer than this binding.
    /// </summary>
    public string NativeKind { get; }
}
