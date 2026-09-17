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
    /// <see cref="PdfOptions.Pages"/> (<b>1-indexed</b>),
    /// <see cref="PdfOptions.Password"/> and
    /// <see cref="PdfOptions.Position"/> are honoured; other properties are
    /// ignored. <see cref="PdfOptions.Password"/> and
    /// <see cref="PdfOptions.Position"/> cannot be combined — the native
    /// library reaches the frame and weight handling only through its
    /// in-memory path, which does not decrypt, so asking for both throws
    /// rather than silently dropping one. Decrypt the document and use the
    /// byte overload instead.
    /// </param>
    /// <returns>The positioned text items in document order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="PdfInspectorException">
    /// The PDF could not be read, or a password and position options were
    /// requested together.
    /// </exception>
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
    /// <see cref="PdfOptions.Pages"/> (<b>1-indexed</b>) and
    /// <see cref="PdfOptions.Position"/> are honoured; other properties are
    /// ignored.
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
    /// <param name="position">
    /// The frame the region rectangles are read in, and whether bold is read
    /// from the font's weight class. Defaults to
    /// <see cref="PositionFrame.Sheet"/> with bold unchanged.
    /// </param>
    /// <returns>
    /// One entry per requested page, each holding one result per requested
    /// region in the same order.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="PdfInspectorException">The PDF could not be read.</exception>
    public static IReadOnlyList<PageRegionText> ExtractTextInRegions(
        string path,
        IEnumerable<PageRegions> pageRegions,
        PositionOptions? position = null) =>
        NativeCall.FromFile(
            path,
            SerializeRegions(pageRegions, position),
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
    /// <param name="position">
    /// See <see cref="ExtractTextInRegions(string, IEnumerable{PageRegions}, PositionOptions?)"/>.
    /// </param>
    /// <returns>
    /// See <see cref="ExtractTextInRegions(string, IEnumerable{PageRegions}, PositionOptions?)"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="pageRegions"/> is null.</exception>
    /// <exception cref="PdfInspectorException">The PDF could not be read.</exception>
    public static IReadOnlyList<PageRegionText> ExtractTextInRegions(
        ReadOnlySpan<byte> data,
        IEnumerable<PageRegions> pageRegions,
        PositionOptions? position = null) =>
        NativeCall.FromBytes(
            data,
            SerializeRegions(pageRegions, position),
            NativeMethods.ExtractTextInRegionsBytes,
            PdfJsonContext.Default.PageRegionsEnvelope);

    /// <summary>
    /// Extracts the <i>tables</i> falling inside given bounding boxes — the
    /// same hybrid-OCR path as
    /// <see cref="ExtractTextInRegions(string, IEnumerable{PageRegions}, PositionOptions?)"/>,
    /// but running table detection over each region instead of reading it as
    /// flat text.
    /// </summary>
    /// <param name="path">Path to the PDF.</param>
    /// <param name="pageRegions">
    /// The regions to read, by <b>0-indexed</b> page. Coordinates are PDF
    /// points with a top-left origin.
    /// </param>
    /// <param name="position">
    /// The frame the region rectangles are read in, and whether bold is read
    /// from the font's weight class. Defaults to
    /// <see cref="PositionFrame.Sheet"/> with bold unchanged.
    /// </param>
    /// <returns>
    /// One entry per requested page, each holding one result per requested
    /// region in the same order. Where table structure was found,
    /// <see cref="RegionText.Text"/> is a Markdown pipe-table and
    /// <see cref="RegionText.NeedsOcr"/> is false. Where none was — too few
    /// items, poor alignment, GID fonts — the text is empty and
    /// <see cref="RegionText.NeedsOcr"/> is true, so the caller can fall back
    /// to OCR for that region alone.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="PdfInspectorException">The PDF could not be read.</exception>
    public static IReadOnlyList<PageRegionText> ExtractTablesInRegions(
        string path,
        IEnumerable<PageRegions> pageRegions,
        PositionOptions? position = null) =>
        NativeCall.FromFile(
            path,
            SerializeRegions(pageRegions, position),
            NativeMethods.ExtractTablesInRegionsFile,
            PdfJsonContext.Default.PageRegionsEnvelope);

    /// <summary>
    /// Extracts the tables falling inside given bounding boxes of an
    /// in-memory document.
    /// </summary>
    /// <param name="data">The PDF bytes.</param>
    /// <param name="pageRegions">
    /// The regions to read, by <b>0-indexed</b> page. Coordinates are PDF
    /// points with a top-left origin.
    /// </param>
    /// <param name="position">
    /// See <see cref="ExtractTablesInRegions(string, IEnumerable{PageRegions}, PositionOptions?)"/>.
    /// </param>
    /// <returns>
    /// See <see cref="ExtractTablesInRegions(string, IEnumerable{PageRegions}, PositionOptions?)"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="pageRegions"/> is null.</exception>
    /// <exception cref="PdfInspectorException">The PDF could not be read.</exception>
    public static IReadOnlyList<PageRegionText> ExtractTablesInRegions(
        ReadOnlySpan<byte> data,
        IEnumerable<PageRegions> pageRegions,
        PositionOptions? position = null) =>
        NativeCall.FromBytes(
            data,
            SerializeRegions(pageRegions, position),
            NativeMethods.ExtractTablesInRegionsBytes,
            PdfJsonContext.Default.PageRegionsEnvelope);

    // -----------------------------------------------------------------
    // Payload serialisation
    // -----------------------------------------------------------------

    private static string? Serialize(PdfOptions? options) =>
        options is null ? null : JsonSerializer.Serialize(options, PdfJsonContext.Default.PdfOptionsPayload);

    private static string SerializeRegions(IEnumerable<PageRegions> pageRegions, PositionOptions? position)
    {
        if (pageRegions is null)
        {
            throw new ArgumentNullException(nameof(pageRegions));
        }

        RegionsPayload payload = new RegionsPayload
        {
            PageRegions = new List<PageRegions>(pageRegions),
            Position = position,
        };
        return JsonSerializer.Serialize(payload, PdfJsonContext.Default.RegionsPayload);
    }
}
