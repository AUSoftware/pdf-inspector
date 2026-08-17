using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PdfInspector.Json;

namespace PdfInspector.Interop;

/// <summary>
/// Marshals arguments into the native library and turns its JSON response
/// envelope back into a result or an exception.
/// </summary>
internal static unsafe class NativeCall
{
    /// <summary>A native entry point taking a file path.</summary>
    internal delegate IntPtr FileEntryPoint(byte* path, byte* json);

    /// <summary>A native entry point taking a byte buffer.</summary>
    internal delegate IntPtr BytesEntryPoint(byte* data, nuint length, byte* json);

    /// <summary>Calls a path-based entry point.</summary>
    internal static T FromFile<T>(
        string path,
        string? json,
        FileEntryPoint entryPoint,
        JsonTypeInfo<Envelope<T>> typeInfo)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        NativeMethods.EnsureInitialized();

        byte[] pathBytes = Utf8.ToNullTerminated(path)!;
        byte[]? jsonBytes = Utf8.ToNullTerminated(json);

        fixed (byte* pathPointer = pathBytes)
        fixed (byte* jsonPointer = jsonBytes)
        {
            return Decode(entryPoint(pathPointer, jsonPointer), typeInfo);
        }
    }

    /// <summary>Calls a buffer-based entry point.</summary>
    internal static T FromBytes<T>(
        ReadOnlySpan<byte> data,
        string? json,
        BytesEntryPoint entryPoint,
        JsonTypeInfo<Envelope<T>> typeInfo)
    {
        NativeMethods.EnsureInitialized();

        byte[]? jsonBytes = Utf8.ToNullTerminated(json);

        fixed (byte* dataPointer = data)
        fixed (byte* jsonPointer = jsonBytes)
        {
            return Decode(entryPoint(dataPointer, (nuint)data.Length, jsonPointer), typeInfo);
        }
    }

    /// <summary>
    /// Reads the response, frees the native buffer, and unwraps the envelope.
    /// </summary>
    private static T Decode<T>(IntPtr response, JsonTypeInfo<Envelope<T>> typeInfo)
    {
        if (response == IntPtr.Zero)
        {
            throw new PdfInspectorException(
                PdfErrorKind.Internal,
                "internal",
                "The native library could not allocate a response.");
        }

        string json;
        try
        {
            json = Utf8.FromPointer(response);
        }
        finally
        {
            NativeMethods.FreeString(response);
        }

        Envelope<T>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException e)
        {
            throw new PdfInspectorException(
                PdfErrorKind.Internal,
                "internal",
                "The native library returned a response this binding could not parse. "
                    + "This usually means the native library and the PdfInspector package are different versions.",
                e);
        }

        if (envelope is null)
        {
            throw new PdfInspectorException(
                PdfErrorKind.Internal,
                "internal",
                "The native library returned an empty response.");
        }

        if (!envelope.Ok)
        {
            throw ToException(envelope.Error);
        }

        if (envelope.Data is null)
        {
            throw new PdfInspectorException(
                PdfErrorKind.Internal,
                "internal",
                "The native library reported success but returned no data.");
        }

        return envelope.Data;
    }

    private static PdfInspectorException ToException(ErrorPayload? error)
    {
        if (error is null)
        {
            return new PdfInspectorException(
                PdfErrorKind.Internal,
                "internal",
                "The native library reported a failure with no detail.");
        }

        return new PdfInspectorException(ParseKind(error.Kind), error.Kind, error.Message);
    }

    /// <summary>
    /// Maps the native <c>kind</c> discriminant. Unrecognised kinds map to
    /// <see cref="PdfErrorKind.Unknown"/> so a newer native library never
    /// turns an ordinary error into a parse failure.
    /// </summary>
    internal static PdfErrorKind ParseKind(string kind) => kind switch
    {
        "invalid_argument" => PdfErrorKind.InvalidArgument,
        "invalid_options" => PdfErrorKind.InvalidOptions,
        "io" => PdfErrorKind.Io,
        "parse" => PdfErrorKind.Parse,
        "encrypted" => PdfErrorKind.Encrypted,
        "invalid_structure" => PdfErrorKind.InvalidStructure,
        "not_a_pdf" => PdfErrorKind.NotAPdf,
        "panic" => PdfErrorKind.Panic,
        "internal" => PdfErrorKind.Internal,
        _ => PdfErrorKind.Unknown,
    };
}
