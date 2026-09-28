using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;
using AUSoftware.PdfInspector.Json;

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
/// A font whose ToUnicode CMap — or, for a font without one, the embedded
/// program's cmap table — had no entry for some of the codes the document
/// shows through it, and what became of those codes.
/// <c>Codes - Interpolated - Unmapped</c> of its codes had an entry.
/// </summary>
public sealed class FontCMapGaps
{
    /// <summary>The font's <c>/BaseFont</c> name, or its resource name when it has none.</summary>
    public string Font { get; init; } = string.Empty;

    /// <summary>
    /// Codes shown through the font's CMap, repeats included: two-byte codes,
    /// or the bytes of a single-byte CMap.
    /// </summary>
    public long Codes { get; init; }

    /// <summary>
    /// Codes without an entry that were read from the mapped codes around
    /// them: a CMap mapping code 36 to <c>A</c> and code 38 to <c>C</c> says
    /// code 37 is <c>B</c>, for a run of digits or of letters of one case in
    /// alphabetical glyph order.
    /// </summary>
    public long Interpolated { get; init; }

    /// <summary>
    /// Codes without an entry that could not be read; each is a U+FFFD in
    /// the text.
    /// </summary>
    public long Unmapped { get; init; }
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

    /// <summary>
    /// The <c>/Title</c> of the document information dictionary, decoded as a
    /// PDF text string (UTF-16 or UTF-8 after a byte order mark,
    /// PDFDocEncoding otherwise). <see langword="null"/> when the entry is
    /// missing or not a string. The other document information properties
    /// follow the same decoding and missing-value rule.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>The document information dictionary's <c>/Author</c>.</summary>
    public string? Author { get; init; }

    /// <summary>The document information dictionary's <c>/Subject</c>.</summary>
    public string? Subject { get; init; }

    /// <summary>The document information dictionary's <c>/Keywords</c>.</summary>
    public string? Keywords { get; init; }

    /// <summary>
    /// The document information dictionary's <c>/Creator</c>: the
    /// application the document was authored in.
    /// </summary>
    public string? Creator { get; init; }

    /// <summary>
    /// The document information dictionary's <c>/Producer</c>: the
    /// application that wrote the PDF.
    /// </summary>
    public string? Producer { get; init; }

    /// <summary>
    /// The document information dictionary's <c>/CreationDate</c> as written,
    /// a PDF date string such as <c>D:20240115103000+01'00'</c>.
    /// </summary>
    public string? CreationDate { get; init; }

    /// <summary>The document information dictionary's <c>/ModDate</c> as written.</summary>
    public string? ModDate { get; init; }

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

    /// <summary>
    /// Fonts whose ToUnicode CMap — or, for a font without one, the embedded
    /// program's cmap table — lacked an entry for a code the document shows
    /// through them. Always empty for <see cref="Pdf.Detect(string, PdfOptions?)"/>
    /// and <see cref="ProcessMode.DetectOnly"/>, which decode no text;
    /// otherwise empty when every such code had an entry.
    /// </summary>
    public IReadOnlyList<FontCMapGaps> CmapGaps { get; init; } = Array.Empty<FontCMapGaps>();
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

    /// <summary>
    /// Rotation of the run's baseline in degrees counter-clockwise from the
    /// page's x axis, normalised to <c>[0, 360)</c>: <c>0</c> for ordinary
    /// horizontal text, <c>90</c> for text reading bottom-to-top (a rotated
    /// margin stamp), <c>270</c> for top-to-bottom, <c>180</c> for
    /// upside-down. The <see cref="X"/>, <see cref="Y"/>, <see cref="Width"/>
    /// and <see cref="Height"/> values describe the run's axis-aligned box,
    /// so a vertical run is tall and thin rather than zero-width.
    /// </summary>
    public double Rotation { get; init; }

    /// <summary>
    /// Whether the run's advance came from font metrics. It is
    /// <see langword="false"/> when the font carries no width information:
    /// <see cref="Width"/> is then an estimate of half an em per painted
    /// glyph rather than a measurement.
    /// </summary>
    public bool AdvanceKnown { get; init; }

    /// <summary>
    /// The <c>/BaseFont</c> family name (<c>"ABCDEF+CMMI10"</c>), which
    /// identifies the actual face. Before 1.16.0 this carried the resource
    /// tag now exposed as <see cref="FontTag"/>.
    /// </summary>
    public string Font { get; init; } = string.Empty;

    /// <summary>
    /// The raw font resource tag (<c>"F2"</c>, <c>"T22"</c>) the run's show
    /// operator selected. Scoped to the enclosing page or Form XObject's
    /// <c>/Resources</c>, so the same tag on another page may name a
    /// different face; within one page it distinguishes font
    /// <i>programs</i> that share a family name, which <see cref="Font"/>
    /// cannot. Empty for items that do not come from a show operator
    /// (images, links, form fields).
    /// </summary>
    public string FontTag { get; init; } = string.Empty;

    /// <summary>Font size in points.</summary>
    public double FontSize { get; init; }

    /// <summary>
    /// The font's weight class on the 100–900 scale (400 regular, 700 bold),
    /// read from the embedded font program's OS/2 table, else the
    /// <c>FontDescriptor</c>'s <c>/FontWeight</c>, else a weight word in the
    /// font name (<c>"Light"</c>, <c>"Medium"</c>, <c>"-Md"</c>,
    /// <c>"Black"</c>, <c>"W6"</c>). <see langword="null"/> when none of them
    /// says, and for items that do not come from a font (images, links, form
    /// fields). Independent of <see cref="IsBold"/> unless
    /// <see cref="PositionOptions.BoldFromWeight"/> is set: a medium face
    /// reports 500 with <see cref="IsBold"/> false.
    /// </summary>
    public int? FontWeight { get; init; }

    /// <summary>
    /// At least one character in <see cref="Text"/> was changed by the legacy
    /// private-use symbol cleanup. This is decoding provenance, not an OCR
    /// verdict: a rewritten value must not be taken as an authoritative
    /// Unicode alias, and <see langword="false"/> does not vouch for the
    /// accuracy of the rest. Merged items keep the evidence of every run that
    /// contributed to them.
    /// </summary>
    public bool LegacySymbolRewrite { get; init; }

    /// <summary>1-indexed page number.</summary>
    public int Page { get; init; }

    /// <summary>
    /// True when the font is bold: from the font name, the
    /// <c>FontDescriptor</c>'s ForceBold flag, the embedded program's bold
    /// selection, or text filled and stroked to look heavier. With
    /// <see cref="PositionOptions.BoldFromWeight"/> also true when
    /// <see cref="FontWeight"/> is at or above
    /// <see cref="PositionOptions.BoldWeightThreshold"/>.
    /// <see cref="BoldSource"/> says which.
    /// </summary>
    public bool IsBold { get; init; }

    /// <summary>
    /// Where <see cref="IsBold"/> came from, the first in
    /// <see cref="PdfInspector.BoldSource"/> order when more than one says
    /// bold — so a verdict can be weighed against <see cref="FontWeight"/>:
    /// a face whose name says Bold over a weight class of 400 reports
    /// <see cref="PdfInspector.BoldSource.FontName"/>.
    /// <see langword="null"/> when <see cref="IsBold"/> is false, and for
    /// image, link and form-field items.
    /// </summary>
    public BoldSource? BoldSource { get; init; }

    /// <summary>
    /// Whether the font is fixed-pitch (monospaced): true when the
    /// <c>FontDescriptor</c>'s FixedPitch flag or the embedded program's
    /// <c>post</c> table says so, else measured from the font's width table —
    /// true when a dozen or more of its glyphs share one advance, false when
    /// two differ. <see langword="null"/> when neither holds, and for image,
    /// link and form-field items.
    /// </summary>
    public bool? FixedPitch { get; init; }

    /// <summary>
    /// The fill colour the run was shown with: what its glyphs are filled
    /// with in the render modes that fill (0, 2, 4, 6). DeviceRGB is read as
    /// sRGB, DeviceGray as three equal components and DeviceCMYK converted as
    /// the PDF specification converts it; ICCBased spaces are read by their
    /// component count and Indexed spaces through their palette.
    /// <see langword="null"/> for any other colour space (Separation,
    /// DeviceN, Pattern, CalRGB, Lab, …) and for image, link and form-field
    /// items. A merged item keeps its first run's colour.
    /// </summary>
    public RgbColor? FillColor { get; init; }

    /// <summary>
    /// The stroke colour the run was shown with, read like
    /// <see cref="FillColor"/>: what its glyph outlines are stroked with in
    /// the render modes that stroke (1, 2, 5, 6).
    /// </summary>
    public RgbColor? StrokeColor { get; init; }

    /// <summary>
    /// The text render mode (<c>Tr</c>) the run was shown with, 0–7: 0 fill,
    /// 1 stroke, 2 fill and stroke, 3 invisible (the mode of OCR text
    /// layers), 4–6 as 0–2 plus clip, 7 clip only. Runs in modes 3 and 7 put
    /// no glyphs on the page; which runs are extracted is unchanged by it.
    /// <see langword="null"/> for image, link and form-field items.
    /// </summary>
    public int? RenderMode { get; init; }

    /// <summary>True when the font is italic.</summary>
    public bool IsItalic { get; init; }

    /// <summary>True when a rule is drawn under the baseline of this run.</summary>
    public bool IsUnderline { get; init; }

    /// <summary>True when a rule crosses the glyphs of this run.</summary>
    public bool IsStrikeout { get; init; }

    /// <summary>
    /// Signed baseline offset, in points, of a superscript/subscript glyph
    /// run from the baseline of the body text it is attached to; <c>0</c> for
    /// normal text. Positive means raised (superscript: footnote and
    /// affiliation markers, exponents), negative means lowered (subscript).
    /// Digit-only markers beside a word are instead fused into that word as
    /// Unicode super/subscript characters and carry <c>0</c>.
    /// </summary>
    public double BaselineShift { get; init; }

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

/// <summary>An 8-bit sRGB colour.</summary>
[JsonConverter(typeof(RgbColorConverter))]
public readonly struct RgbColor : IEquatable<RgbColor>
{
    /// <summary>Creates a colour from its components.</summary>
    /// <param name="r">Red, 0–255.</param>
    /// <param name="g">Green, 0–255.</param>
    /// <param name="b">Blue, 0–255.</param>
    public RgbColor(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }

    /// <summary>Red, 0–255.</summary>
    public byte R { get; }

    /// <summary>Green, 0–255.</summary>
    public byte G { get; }

    /// <summary>Blue, 0–255.</summary>
    public byte B { get; }

    /// <inheritdoc/>
    public bool Equals(RgbColor other) => R == other.R && G == other.G && B == other.B;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is RgbColor other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => (R << 16) | (G << 8) | B;

    /// <summary>Value equality.</summary>
    public static bool operator ==(RgbColor left, RgbColor right) => left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(RgbColor left, RgbColor right) => !left.Equals(right);

    /// <summary>The colour as a CSS-style hex string, <c>#rrggbb</c>.</summary>
    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "#{0:x2}{1:x2}{2:x2}", R, G, B);
}
