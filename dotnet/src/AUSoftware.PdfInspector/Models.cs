using System;
using System.Collections.Generic;

namespace AUSoftware.PdfInspector;

/// <summary>
/// Machine-readable reasons a page's text layer cannot be trusted.
/// </summary>
/// <remarks>
/// These are the values that appear in <see cref="PageOcrReasons.Reasons"/>,
/// <see cref="PageMarkdown.OcrReason"/> and <see cref="RegionText.OcrReason"/>.
/// The list may grow, so treat unrecognised values as "needs OCR, cause
/// unknown" rather than an error.
/// </remarks>
public static class OcrReasons
{
    /// <summary>The text layer is garbled — broken font decoding or mojibake.</summary>
    public const string SuspectedGarbledText = "suspected_garbled_text";

    /// <summary>The page is a scanned raster with no usable text layer.</summary>
    public const string Scanned = "scanned";

    /// <summary>No extractable text and no image to OCR — blank or unreachable.</summary>
    public const string NoText = "no_text";

    /// <summary>Text is drawn as vector outlines rather than text operators.</summary>
    public const string VectorText = "vector_text";
}

/// <summary>OCR reasons for a single page.</summary>
public sealed class PageOcrReasons
{
    /// <summary>1-indexed page number.</summary>
    public int Page { get; init; }

    /// <summary>Reason identifiers; see <see cref="OcrReasons"/>.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The result of a full <see cref="Pdf.Process(string, PdfOptions?)"/> or
/// <see cref="Pdf.Detect(string, PdfOptions?)"/> call.
/// </summary>
public sealed class PdfResult
{
    /// <summary>How the document stores its content.</summary>
    public PdfType PdfType { get; init; }

    /// <summary>
    /// The extracted Markdown. <see langword="null"/> for detection-only
    /// calls and for <see cref="ProcessMode.Analyze"/>.
    /// </summary>
    public string? Markdown { get; init; }

    /// <summary>Total pages in the document.</summary>
    public int PageCount { get; init; }

    /// <summary>Wall-clock time the native library spent on this call.</summary>
    public long ProcessingTimeMs { get; init; }

    /// <summary>1-indexed pages whose text should be replaced with OCR output.</summary>
    public IReadOnlyList<int> PagesNeedingOcr { get; init; } = Array.Empty<int>();

    /// <summary>Why each page in <see cref="PagesNeedingOcr"/> needs OCR.</summary>
    public IReadOnlyList<PageOcrReasons> OcrReasonsByPage { get; init; } = Array.Empty<PageOcrReasons>();

    /// <summary>Document title from the PDF metadata, when present.</summary>
    public string? Title { get; init; }

    /// <summary>Detection confidence, 0.0–1.0.</summary>
    public double Confidence { get; init; }

    /// <summary>True when any page has tables or multi-column text.</summary>
    public bool IsComplexLayout { get; init; }

    /// <summary>1-indexed pages where tables were detected.</summary>
    public IReadOnlyList<int> PagesWithTables { get; init; } = Array.Empty<int>();

    /// <summary>1-indexed pages where a multi-column layout was detected.</summary>
    public IReadOnlyList<int> PagesWithColumns { get; init; } = Array.Empty<int>();

    /// <summary>
    /// True when broken font encodings were detected. The Markdown may be
    /// garbled even if the document classified as <see cref="PdfType.TextBased"/>;
    /// route to OCR instead.
    /// </summary>
    public bool HasEncodingIssues { get; init; }
}

/// <summary>
/// A lightweight routing decision: what kind of PDF this is and which pages
/// need OCR, without extracting any text.
/// </summary>
public sealed class PdfClassification
{
    /// <summary>How the document stores its content.</summary>
    public PdfType PdfType { get; init; }

    /// <summary>Total pages in the document.</summary>
    public int PageCount { get; init; }

    /// <summary>
    /// <b>0-indexed</b> pages that need OCR. Note the difference from
    /// <see cref="PdfResult.PagesNeedingOcr"/>, which is 1-indexed.
    /// </summary>
    public IReadOnlyList<int> PagesNeedingOcr { get; init; } = Array.Empty<int>();

    /// <summary>Detection confidence, 0.0–1.0.</summary>
    public double Confidence { get; init; }
}

/// <summary>A single positioned run of text.</summary>
public sealed class TextItem
{
    /// <summary>The text content.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>X position in PDF points.</summary>
    public double X { get; init; }

    /// <summary>Y position in PDF points, origin at the bottom-left of the page.</summary>
    public double Y { get; init; }

    /// <summary>Width of the run in PDF points.</summary>
    public double Width { get; init; }

    /// <summary>Height of the run, approximated from the font size.</summary>
    public double Height { get; init; }

    /// <summary>Font name as recorded in the PDF.</summary>
    public string Font { get; init; } = string.Empty;

    /// <summary>Font size in points.</summary>
    public double FontSize { get; init; }

    /// <summary>1-indexed page number.</summary>
    public int Page { get; init; }

    /// <summary>True when the font is bold.</summary>
    public bool IsBold { get; init; }

    /// <summary>True when the font is italic.</summary>
    public bool IsItalic { get; init; }

    /// <summary>True when a rule is drawn under the baseline of this run.</summary>
    public bool IsUnderline { get; init; }

    /// <summary>True when a rule crosses the glyphs of this run.</summary>
    public bool IsStrikeout { get; init; }

    /// <summary>What this item represents.</summary>
    public ItemType ItemType { get; init; }

    /// <summary>Target URL — set only when <see cref="ItemType"/> is <see cref="ItemType.Link"/>.</summary>
    public string? Url { get; init; }

    /// <summary>
    /// Marked Content ID from the content stream, or <see langword="null"/>
    /// when the run is not inside marked content. Join on
    /// <c>(Page, Mcid)</c> against
    /// <see cref="Pdf.ExtractStructureElements(string, PdfOptions?)"/> to
    /// attach structure-tree roles in tagged PDFs.
    /// </summary>
    public long? Mcid { get; init; }
}

/// <summary>One structure-tree element reference from a tagged PDF.</summary>
public sealed class StructureElement
{
    /// <summary>1-indexed page number, matching <see cref="TextItem.Page"/>.</summary>
    public int Page { get; init; }

    /// <summary>Marked Content ID, matching <see cref="TextItem.Mcid"/>.</summary>
    public long Mcid { get; init; }

    /// <summary>
    /// Standard structure type name — <c>"H1"</c>…<c>"H6"</c>, <c>"P"</c>,
    /// <c>"Table"</c>, <c>"TD"</c>, and so on. Custom tags are resolved
    /// through the document's <c>/RoleMap</c>; unmapped tags come through
    /// verbatim.
    /// </summary>
    public string Role { get; init; } = string.Empty;
}

/// <summary>Markdown for a single page.</summary>
public sealed class PageMarkdown
{
    /// <summary><b>0-indexed</b> page number.</summary>
    public int Page { get; init; }

    /// <summary>Markdown for this page; empty when <see cref="NeedsOcr"/> is true.</summary>
    public string Markdown { get; init; } = string.Empty;

    /// <summary>True when this page's text is unreliable and OCR should be used.</summary>
    public bool NeedsOcr { get; init; }

    /// <summary>Why OCR is needed; see <see cref="OcrReasons"/>.</summary>
    public string? OcrReason { get; init; }
}

/// <summary>
/// Per-page Markdown plus document-wide layout classification, for pipelines
/// that mix direct extraction with OCR page by page.
/// </summary>
public sealed class PagesExtractionResult
{
    /// <summary>Per-page results, in the order requested.</summary>
    public IReadOnlyList<PageMarkdown> Pages { get; init; } = Array.Empty<PageMarkdown>();

    /// <summary>1-indexed pages where tables were detected.</summary>
    public IReadOnlyList<int> PagesWithTables { get; init; } = Array.Empty<int>();

    /// <summary>1-indexed pages where a multi-column layout was detected.</summary>
    public IReadOnlyList<int> PagesWithColumns { get; init; } = Array.Empty<int>();

    /// <summary>1-indexed pages that need OCR.</summary>
    public IReadOnlyList<int> PagesNeedingOcr { get; init; } = Array.Empty<int>();

    /// <summary>Why each page needing OCR needs it.</summary>
    public IReadOnlyList<PageOcrReasons> OcrReasonsByPage { get; init; } = Array.Empty<PageOcrReasons>();

    /// <summary>True when any page has tables or multi-column text.</summary>
    public bool IsComplex { get; init; }
}

/// <summary>Which OCR model produced a page's text.</summary>
public sealed class OcrModelIdentity
{
    /// <summary>Model name, e.g. <c>pp-ocrv6-small</c>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Pinned revision of the model artifacts.</summary>
    public string Revision { get; init; } = string.Empty;
}

/// <summary>How long each OCR stage took for one page.</summary>
public sealed class OcrTimings
{
    /// <summary>Time spent rasterising the page.</summary>
    public long RenderMs { get; init; }

    /// <summary>Time spent recognising text.</summary>
    public long OcrMs { get; init; }

    /// <summary>Time spent fusing and assembling the Markdown.</summary>
    public long AssemblyMs { get; init; }
}

/// <summary>Where one page's Markdown came from, and what it cost.</summary>
public sealed class OcrPageProvenance
{
    /// <summary><b>1-indexed</b> page number.</summary>
    public int PageNumber { get; init; }

    /// <summary>Whether this page's text is native, OCR, or a fusion of both.</summary>
    public PageContentSource Source { get; init; }

    /// <summary>
    /// The model that ran, or <see langword="null"/> when
    /// <see cref="Source"/> is <see cref="PageContentSource.Native"/>.
    /// </summary>
    public OcrModelIdentity? OcrModel { get; init; }

    /// <summary>Resolution the page was rasterised at, when it was.</summary>
    public double? RenderDpi { get; init; }

    /// <summary>Mean confidence of the accepted OCR spans, when OCR ran.</summary>
    public double? OcrConfidence { get; init; }

    /// <summary>Per-stage timings for this page.</summary>
    public OcrTimings Timings { get; init; } = new OcrTimings();

    /// <summary>Non-fatal problems encountered while processing this page.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// True when local OCR read this page poorly enough that a hosted
    /// document-parsing service would likely do better.
    /// </summary>
    public bool HostedRecommended { get; init; }
}

/// <summary>Final Markdown and provenance for one page.</summary>
public sealed class OcrPageResult
{
    /// <summary><b>1-indexed</b> page number.</summary>
    public int PageNumber { get; init; }

    /// <summary>The page's Markdown, whatever its source.</summary>
    public string Markdown { get; init; } = string.Empty;

    /// <summary>Where this page's Markdown came from.</summary>
    public OcrPageProvenance Provenance { get; init; } = new OcrPageProvenance();
}

/// <summary>
/// The result of <see cref="Pdf.ProcessWithOcr(string, OcrOptions?)"/>: native
/// extraction with OCR filled in on the pages that needed it. Every page list
/// here is <b>1-indexed</b>.
/// </summary>
public sealed class OcrPdfResult
{
    /// <summary>The whole document's Markdown, in selected-page order.</summary>
    public string Markdown { get; init; } = string.Empty;

    /// <summary>Per-page Markdown and provenance.</summary>
    public IReadOnlyList<OcrPageResult> Pages { get; init; } = Array.Empty<OcrPageResult>();

    /// <summary>Total pages in the document, whatever was selected.</summary>
    public int PageCount { get; init; }

    /// <summary>Pages native extraction flagged as wanting OCR.</summary>
    public IReadOnlyList<int> PagesRecommendedForOcr { get; init; } = Array.Empty<int>();

    /// <summary>
    /// Pages that were actually rendered and recognised. Empty when
    /// <see cref="OcrOptions.Mode"/> was <see cref="OcrMode.Off"/>, or when
    /// <see cref="OcrMode.Auto"/> found nothing worth OCR-ing.
    /// </summary>
    public IReadOnlyList<int> PagesRoutedToOcr { get; init; } = Array.Empty<int>();

    /// <summary>Pages local OCR read poorly enough to suggest a hosted service.</summary>
    public IReadOnlyList<int> PagesRecommendingHosted { get; init; } = Array.Empty<int>();

    /// <summary>Why each flagged page was flagged; see <see cref="OcrReasons"/>.</summary>
    public IReadOnlyList<PageOcrReasons> OcrReasonsByPage { get; init; } = Array.Empty<PageOcrReasons>();

    /// <summary>Selected pages where tables were detected.</summary>
    public IReadOnlyList<int> PagesWithTables { get; init; } = Array.Empty<int>();

    /// <summary>Selected pages where a multi-column layout was detected.</summary>
    public IReadOnlyList<int> PagesWithColumns { get; init; } = Array.Empty<int>();

    /// <summary>True when any selected page has tables or multi-column text.</summary>
    public bool IsComplex { get; init; }

    /// <summary>End-to-end wall-clock time for the call.</summary>
    public long ProcessingTimeMs { get; init; }

    /// <summary>Time spent rasterising pages. Zero when no page was routed.</summary>
    public long RenderTimeMs { get; init; }

    /// <summary>Time spent recognising text. Zero when no page was routed.</summary>
    public long OcrTimeMs { get; init; }
}

/// <summary>Text extracted from one requested region.</summary>
public sealed class RegionText
{
    /// <summary>The text inside the region; may be empty.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>True when the text should not be trusted and OCR should be used.</summary>
    public bool NeedsOcr { get; init; }

    /// <summary>Why OCR is needed; see <see cref="OcrReasons"/>.</summary>
    public string? OcrReason { get; init; }
}

/// <summary>Region results for one page, parallel to the requested regions.</summary>
public sealed class PageRegionText
{
    /// <summary><b>0-indexed</b> page number.</summary>
    public int Page { get; init; }

    /// <summary>One result per requested region, in the order requested.</summary>
    public IReadOnlyList<RegionText> Regions { get; init; } = Array.Empty<RegionText>();
}
