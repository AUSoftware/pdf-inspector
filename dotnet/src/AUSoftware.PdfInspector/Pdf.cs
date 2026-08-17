using System;
using System.Collections.Generic;
using System.Text.Json;
using AUSoftware.PdfInspector.Interop;
using AUSoftware.PdfInspector.Json;

namespace AUSoftware.PdfInspector;

/// <summary>
/// PDF inspection, classification, and Markdown extraction.
/// </summary>
/// <remarks>
/// <para>
/// Every method blocks while the native library parses the document, and
/// every method is thread-safe — the native library holds no shared mutable
/// state, so calls may run concurrently on any number of threads. Wrap a call
/// in <see cref="System.Threading.Tasks.Task.Run(Action)"/> if you need to
/// keep a UI or request thread free.
/// </para>
/// <para>
/// Overloads come in pairs: one taking a file path, one taking the document
/// bytes. They behave identically; the byte overloads exist for documents
/// that never touch disk.
/// </para>
/// <para>
/// Page numbering is not uniform, because it follows the underlying library:
/// each method's documentation states whether its pages are 0- or 1-indexed.
/// </para>
/// </remarks>
public static unsafe class Pdf
{
    /// <summary>
    /// Version of the loaded native library. Reading this forces the library
    /// to load, so it doubles as an installation check.
    /// </summary>
    /// <exception cref="DllNotFoundException">The native library could not be found.</exception>
    public static string NativeVersion
    {
        get
        {
            NativeMethods.EnsureInitialized();
            return Utf8.FromPointer(NativeMethods.Version());
        }
    }

    // -----------------------------------------------------------------
    // Full pipeline
    // -----------------------------------------------------------------

    /// <summary>
    /// Runs the full pipeline over a file: detect the document type, extract
    /// the text, and convert it to Markdown.
    /// </summary>
    /// <param name="path">Path to the PDF.</param>
    /// <param name="options">
    /// Optional settings. All are honoured; <see cref="PdfOptions.Pages"/> is
    /// <b>1-indexed</b>.
    /// </param>
    /// <returns>The Markdown plus detection and layout metadata.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="PdfInspectorException">The PDF could not be processed.</exception>
    public static PdfResult Process(string path, PdfOptions? options = null) =>
        NativeCall.FromFile(
            path,
            Serialize(options),
            NativeMethods.ProcessPdfFile,
            PdfJsonContext.Default.PdfResultEnvelope);

    /// <summary>
    /// Runs the full pipeline over an in-memory document.
    /// </summary>
    /// <param name="data">The PDF bytes.</param>
    /// <param name="options">
    /// Optional settings. All are honoured; <see cref="PdfOptions.Pages"/> is
    /// <b>1-indexed</b>.
    /// </param>
    /// <returns>The Markdown plus detection and layout metadata.</returns>
    /// <exception cref="PdfInspectorException">The PDF could not be processed.</exception>
    public static PdfResult Process(ReadOnlySpan<byte> data, PdfOptions? options = null) =>
        NativeCall.FromBytes(
            data,
            Serialize(options),
            NativeMethods.ProcessPdfBytes,
            PdfJsonContext.Default.PdfResultEnvelope);

    // -----------------------------------------------------------------
    // Detection
    // -----------------------------------------------------------------

    /// <summary>
    /// Detects the document type without extracting text.
    /// <see cref="PdfResult.Markdown"/> is always <see langword="null"/>.
    /// </summary>
    /// <param name="path">Path to the PDF.</param>
    /// <param name="options">
    /// Optional settings. <see cref="PdfOptions.Detection"/>,
    /// <see cref="PdfOptions.Password"/> and <see cref="PdfOptions.Pages"/>
    /// (<b>1-indexed</b>) are honoured; <see cref="PdfOptions.Mode"/> is not,
    /// since this call is detection-only by definition.
    /// </param>
    /// <returns>Detection metadata.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="PdfInspectorException">The PDF could not be inspected.</exception>
    public static PdfResult Detect(string path, PdfOptions? options = null) =>
        NativeCall.FromFile(
            path,
            Serialize(options),
            NativeMethods.DetectPdfFile,
            PdfJsonContext.Default.PdfResultEnvelope);

    /// <summary>
    /// Detects the document type of an in-memory document without extracting
    /// text.
    /// </summary>
    /// <param name="data">The PDF bytes.</param>
    /// <param name="options">See <see cref="Detect(string, PdfOptions?)"/>.</param>
    /// <returns>Detection metadata.</returns>
    /// <exception cref="PdfInspectorException">The PDF could not be inspected.</exception>
    public static PdfResult Detect(ReadOnlySpan<byte> data, PdfOptions? options = null) =>
        NativeCall.FromBytes(
            data,
            Serialize(options),
            NativeMethods.DetectPdfBytes,
            PdfJsonContext.Default.PdfResultEnvelope);

    /// <summary>
    /// Classifies a file for routing: document type, page count, and which
    /// pages need OCR. The cheapest call in the API.
    /// </summary>
    /// <param name="path">Path to the PDF.</param>
    /// <returns>
    /// The classification. Its <see cref="PdfClassification.PagesNeedingOcr"/>
    /// is <b>0-indexed</b>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="PdfInspectorException">The PDF could not be classified.</exception>
    public static PdfClassification Classify(string path) =>
        NativeCall.FromFile(
            path,
            null,
            (pathPointer, _) => NativeMethods.ClassifyPdfFile(pathPointer),
            PdfJsonContext.Default.ClassificationEnvelope);

    /// <summary>
    /// Classifies an in-memory document for routing.
    /// </summary>
    /// <param name="data">The PDF bytes.</param>
    /// <returns>
    /// The classification. Its <see cref="PdfClassification.PagesNeedingOcr"/>
    /// is <b>0-indexed</b>.
    /// </returns>
    /// <exception cref="PdfInspectorException">The PDF could not be classified.</exception>
    public static PdfClassification Classify(ReadOnlySpan<byte> data) =>
        NativeCall.FromBytes(
            data,
            null,
            (dataPointer, length, _) => NativeMethods.ClassifyPdfBytes(dataPointer, length),
            PdfJsonContext.Default.ClassificationEnvelope);

    // -----------------------------------------------------------------
    // Plain text
    // -----------------------------------------------------------------

    /// <summary>
    /// Extracts plain text from a file, with no Markdown structure.
    /// </summary>
    /// <param name="path">Path to the PDF.</param>
    /// <returns>The document text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="PdfInspectorException">The text could not be extracted.</exception>
    public static string ExtractText(string path) =>
        NativeCall.FromFile(
            path,
            null,
            (pathPointer, _) => NativeMethods.ExtractTextFile(pathPointer),
            PdfJsonContext.Default.StringEnvelope);

    /// <summary>
    /// Extracts plain text from an in-memory document.
    /// </summary>
    /// <param name="data">The PDF bytes.</param>
    /// <returns>The document text.</returns>
    /// <exception cref="PdfInspectorException">The text could not be extracted.</exception>
    public static string ExtractText(ReadOnlySpan<byte> data) =>
        NativeCall.FromBytes(
            data,
            null,
            (dataPointer, length, _) => NativeMethods.ExtractTextBytes(dataPointer, length),
            PdfJsonContext.Default.StringEnvelope);

    // -----------------------------------------------------------------
    // Positioned items and structure tree
    // -----------------------------------------------------------------

    /// <summary>
    /// Extracts every run of text from a file with its position, font, and
    /// styling.
    /// </summary>
    /// <param name="path">Path to the PDF.</param>
    /// <param name="options">
    /// <see cref="PdfOptions.Pages"/> (<b>1-indexed</b>) and
    /// <see cref="PdfOptions.Password"/> are honoured; other properties are
    /// ignored.
    /// </param>
    /// <returns>The positioned text items in document order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="PdfInspectorException">The PDF could not be read.</exception>
    public static IReadOnlyList<TextItem> ExtractTextWithPositions(string path, PdfOptions? options = null) =>
        NativeCall.FromFile(
            path,
            Serialize(options),
            NativeMethods.ExtractTextWithPositionsFile,
            PdfJsonContext.Default.TextItemsEnvelope);

    /// <summary>
    /// Extracts positioned text items from an in-memory document.
    /// </summary>
    /// <param name="data">The PDF bytes.</param>
    /// <param name="options">
    /// <see cref="PdfOptions.Pages"/> (<b>1-indexed</b>) is honoured; other
    /// properties are ignored.
    /// </param>
    /// <returns>The positioned text items in document order.</returns>
    /// <exception cref="PdfInspectorException">The PDF could not be read.</exception>
    public static IReadOnlyList<TextItem> ExtractTextWithPositions(ReadOnlySpan<byte> data, PdfOptions? options = null) =>
        NativeCall.FromBytes(
            data,
            Serialize(options),
            NativeMethods.ExtractTextWithPositionsBytes,
            PdfJsonContext.Default.TextItemsEnvelope);

    /// <summary>
    /// Extracts structure-tree element references from a tagged PDF file.
    /// </summary>
    /// <param name="path">Path to the PDF.</param>
    /// <param name="options">
    /// <see cref="PdfOptions.Pages"/> (<b>1-indexed</b>) is honoured; other
    /// properties are ignored.
    /// </param>
    /// <returns>
    /// One entry per marked-content reference, sorted by page then MCID.
    /// Empty when the PDF is not tagged. Join on
    /// <c>(Page, Mcid)</c> against
    /// <see cref="ExtractTextWithPositions(string, PdfOptions?)"/> to attach
    /// roles to text.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="PdfInspectorException">The PDF could not be read.</exception>
    public static IReadOnlyList<StructureElement> ExtractStructureElements(string path, PdfOptions? options = null) =>
        NativeCall.FromFile(
            path,
            Serialize(options),
            NativeMethods.ExtractStructureElementsFile,
            PdfJsonContext.Default.StructureElementsEnvelope);

    /// <summary>
    /// Extracts structure-tree element references from an in-memory tagged
    /// PDF.
    /// </summary>
    /// <param name="data">The PDF bytes.</param>
    /// <param name="options">
    /// <see cref="PdfOptions.Pages"/> (<b>1-indexed</b>) is honoured; other
    /// properties are ignored.
    /// </param>
    /// <returns>See <see cref="ExtractStructureElements(string, PdfOptions?)"/>.</returns>
    /// <exception cref="PdfInspectorException">The PDF could not be read.</exception>
    public static IReadOnlyList<StructureElement> ExtractStructureElements(ReadOnlySpan<byte> data, PdfOptions? options = null) =>
        NativeCall.FromBytes(
            data,
            Serialize(options),
            NativeMethods.ExtractStructureElementsBytes,
            PdfJsonContext.Default.StructureElementsEnvelope);

    // -----------------------------------------------------------------
    // Per-page Markdown
    // -----------------------------------------------------------------

    /// <summary>
    /// Extracts Markdown page by page, with document-wide layout
    /// classification — for pipelines that send some pages to OCR and read
    /// the rest directly.
    /// </summary>
    /// <param name="path">Path to the PDF.</param>
    /// <param name="options">
    /// <see cref="PdfOptions.Pages"/> is honoured and is <b>0-indexed</b>
    /// here; the results come back in the order requested. Other properties
    /// are ignored.
    /// </param>
    /// <returns>Per-page Markdown and layout metadata.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="PdfInspectorException">The PDF could not be read.</exception>
    public static PagesExtractionResult ExtractPagesMarkdown(string path, PdfOptions? options = null) =>
        NativeCall.FromFile(
            path,
            Serialize(options),
            NativeMethods.ExtractPagesMarkdownFile,
            PdfJsonContext.Default.PagesExtractionEnvelope);

    /// <summary>
    /// Extracts Markdown page by page from an in-memory document.
    /// </summary>
    /// <param name="data">The PDF bytes.</param>
    /// <param name="options">
    /// <see cref="PdfOptions.Pages"/> is honoured and is <b>0-indexed</b>
    /// here. Other properties are ignored.
    /// </param>
    /// <returns>Per-page Markdown and layout metadata.</returns>
    /// <exception cref="PdfInspectorException">The PDF could not be read.</exception>
    public static PagesExtractionResult ExtractPagesMarkdown(ReadOnlySpan<byte> data, PdfOptions? options = null) =>
        NativeCall.FromBytes(
            data,
            Serialize(options),
            NativeMethods.ExtractPagesMarkdownBytes,
            PdfJsonContext.Default.PagesExtractionEnvelope);

    // -----------------------------------------------------------------
    // Region extraction
    // -----------------------------------------------------------------

    /// <summary>
    /// Extracts the text falling inside given bounding boxes — the hybrid-OCR
    /// path, where a layout model proposes regions on a rendered page and
    /// this reads the real text out of them.
    /// </summary>
    /// <param name="path">Path to the PDF.</param>
    /// <param name="pageRegions">
    /// The regions to read, by <b>0-indexed</b> page. Coordinates are PDF
    /// points with a top-left origin.
    /// </param>
    /// <returns>
    /// One entry per requested page, each holding one result per requested
    /// region in the same order.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="PdfInspectorException">The PDF could not be read.</exception>
    public static IReadOnlyList<PageRegionText> ExtractTextInRegions(string path, IEnumerable<PageRegions> pageRegions) =>
        NativeCall.FromFile(
            path,
            SerializeRegions(pageRegions),
            NativeMethods.ExtractTextInRegionsFile,
            PdfJsonContext.Default.PageRegionsEnvelope);

    /// <summary>
    /// Extracts the text falling inside given bounding boxes of an in-memory
    /// document.
    /// </summary>
    /// <param name="data">The PDF bytes.</param>
    /// <param name="pageRegions">
    /// The regions to read, by <b>0-indexed</b> page. Coordinates are PDF
    /// points with a top-left origin.
    /// </param>
    /// <returns>See <see cref="ExtractTextInRegions(string, IEnumerable{PageRegions})"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pageRegions"/> is null.</exception>
    /// <exception cref="PdfInspectorException">The PDF could not be read.</exception>
    public static IReadOnlyList<PageRegionText> ExtractTextInRegions(ReadOnlySpan<byte> data, IEnumerable<PageRegions> pageRegions) =>
        NativeCall.FromBytes(
            data,
            SerializeRegions(pageRegions),
            NativeMethods.ExtractTextInRegionsBytes,
            PdfJsonContext.Default.PageRegionsEnvelope);

    // -----------------------------------------------------------------
    // Payload serialisation
    // -----------------------------------------------------------------

    private static string? Serialize(PdfOptions? options) =>
        options is null ? null : JsonSerializer.Serialize(options, PdfJsonContext.Default.PdfOptionsPayload);

    private static string SerializeRegions(IEnumerable<PageRegions> pageRegions)
    {
        if (pageRegions is null)
        {
            throw new ArgumentNullException(nameof(pageRegions));
        }

        RegionsPayload payload = new RegionsPayload
        {
            PageRegions = new List<PageRegions>(pageRegions),
        };
        return JsonSerializer.Serialize(payload, PdfJsonContext.Default.RegionsPayload);
    }
}
