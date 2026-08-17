using System;
using System.Runtime.InteropServices;

namespace AUSoftware.PdfInspector.Interop;

/// <summary>
/// Raw P/Invoke declarations for the <c>pdf_inspector_ffi</c> shared library.
/// </summary>
/// <remarks>
/// Every entry point returns a heap pointer to a NUL-terminated UTF-8 JSON
/// response envelope that must be released with <see cref="FreeString"/>.
/// String arguments are UTF-8 byte buffers; a null pointer means "not
/// supplied" and the native side falls back to defaults.
/// </remarks>
internal static unsafe class NativeMethods
{
    /// <summary>Base name of the native library, without prefix or extension.</summary>
    internal const string LibraryName = "pdf_inspector_ffi";

    static NativeMethods()
    {
#if NET6_0_OR_GREATER
        NativeLibraryResolver.Install();
#endif
    }

    /// <summary>
    /// Forces the static constructor to run — and therefore the import
    /// resolver to be installed — before the first native call.
    /// </summary>
    internal static void EnsureInitialized()
    {
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
            typeof(NativeMethods).TypeHandle);
    }

    [DllImport(LibraryName, EntryPoint = "pdfi_version", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr Version();

    [DllImport(LibraryName, EntryPoint = "pdfi_free_string", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void FreeString(IntPtr response);

    [DllImport(LibraryName, EntryPoint = "pdfi_process_pdf_file", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ProcessPdfFile(byte* path, byte* optionsJson);

    [DllImport(LibraryName, EntryPoint = "pdfi_process_pdf_bytes", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ProcessPdfBytes(byte* data, nuint length, byte* optionsJson);

    [DllImport(LibraryName, EntryPoint = "pdfi_process_pdf_with_ocr_file", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ProcessPdfWithOcrFile(byte* path, byte* optionsJson);

    [DllImport(LibraryName, EntryPoint = "pdfi_process_pdf_with_ocr_bytes", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ProcessPdfWithOcrBytes(byte* data, nuint length, byte* optionsJson);

    [DllImport(LibraryName, EntryPoint = "pdfi_detect_pdf_file", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr DetectPdfFile(byte* path, byte* optionsJson);

    [DllImport(LibraryName, EntryPoint = "pdfi_detect_pdf_bytes", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr DetectPdfBytes(byte* data, nuint length, byte* optionsJson);

    [DllImport(LibraryName, EntryPoint = "pdfi_classify_pdf_file", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ClassifyPdfFile(byte* path);

    [DllImport(LibraryName, EntryPoint = "pdfi_classify_pdf_bytes", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ClassifyPdfBytes(byte* data, nuint length);

    [DllImport(LibraryName, EntryPoint = "pdfi_extract_text_file", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ExtractTextFile(byte* path);

    [DllImport(LibraryName, EntryPoint = "pdfi_extract_text_bytes", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ExtractTextBytes(byte* data, nuint length);

    [DllImport(LibraryName, EntryPoint = "pdfi_extract_text_with_positions_file", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ExtractTextWithPositionsFile(byte* path, byte* optionsJson);

    [DllImport(LibraryName, EntryPoint = "pdfi_extract_text_with_positions_bytes", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ExtractTextWithPositionsBytes(byte* data, nuint length, byte* optionsJson);

    [DllImport(LibraryName, EntryPoint = "pdfi_extract_structure_elements_file", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ExtractStructureElementsFile(byte* path, byte* optionsJson);

    [DllImport(LibraryName, EntryPoint = "pdfi_extract_structure_elements_bytes", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ExtractStructureElementsBytes(byte* data, nuint length, byte* optionsJson);

    [DllImport(LibraryName, EntryPoint = "pdfi_extract_pages_markdown_file", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ExtractPagesMarkdownFile(byte* path, byte* optionsJson);

    [DllImport(LibraryName, EntryPoint = "pdfi_extract_pages_markdown_bytes", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ExtractPagesMarkdownBytes(byte* data, nuint length, byte* optionsJson);

    [DllImport(LibraryName, EntryPoint = "pdfi_extract_text_in_regions_file", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ExtractTextInRegionsFile(byte* path, byte* requestJson);

    [DllImport(LibraryName, EntryPoint = "pdfi_extract_text_in_regions_bytes", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ExtractTextInRegionsBytes(byte* data, nuint length, byte* requestJson);
}
