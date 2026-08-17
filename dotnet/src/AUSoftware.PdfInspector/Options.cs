using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using AUSoftware.PdfInspector.Json;

namespace AUSoftware.PdfInspector;

/// <summary>
/// Options for a PDF operation. Every property is optional; anything left
/// unset keeps the native library's default.
/// </summary>
/// <remarks>
/// Not every entry point honours every property, and <see cref="Pages"/> is
/// indexed differently depending on the call — each method on
/// <see cref="Pdf"/> documents what it reads.
/// </remarks>
public sealed class PdfOptions
{
    /// <summary>
    /// Restrict the operation to these pages. Indexing depends on the method:
    /// 1-indexed for <see cref="Pdf.Process(string, PdfOptions?)"/>,
    /// <see cref="Pdf.ExtractTextWithPositions(string, PdfOptions?)"/> and
    /// <see cref="Pdf.ExtractStructureElements(string, PdfOptions?)"/>;
    /// 0-indexed for <see cref="Pdf.ExtractPagesMarkdown(string, PdfOptions?)"/>,
    /// which also preserves the order given here.
    /// </summary>
    public IReadOnlyList<int>? Pages { get; set; }

    /// <summary>Password for an encrypted PDF.</summary>
    public string? Password { get; set; }

    /// <summary>How far the pipeline should run. Defaults to <see cref="ProcessMode.Full"/>.</summary>
    public ProcessMode? Mode { get; set; }

    /// <summary>Detection tuning.</summary>
    public DetectionOptions? Detection { get; set; }

    /// <summary>Markdown conversion tuning.</summary>
    public MarkdownOptions? Markdown { get; set; }
}

/// <summary>Tuning for the PDF type detector.</summary>
public sealed class DetectionOptions
{
    /// <summary>
    /// Which pages the detector inspects. Defaults to
    /// <see cref="ScanStrategy.Sample(int)"/> with 8 pages.
    /// </summary>
    public ScanStrategy? Strategy { get; set; }

    /// <summary>
    /// Minimum number of text operators on a page for it to count as
    /// text-based. Defaults to 3.
    /// </summary>
    public int? MinTextOpsPerPage { get; set; }

    /// <summary>
    /// Fraction of text pages needed to classify the whole document as
    /// text-based. Defaults to 0.6.
    /// </summary>
    public double? TextPageRatioThreshold { get; set; }
}

/// <summary>
/// Which pages the detector inspects. Create one with the static members —
/// there are no other valid strategies.
/// </summary>
[JsonConverter(typeof(ScanStrategyConverter))]
public sealed class ScanStrategy
{
    private ScanStrategy(string kind, int count, IReadOnlyList<int>? pages)
    {
        Kind = kind;
        Count = count;
        PageNumbers = pages;
    }

    /// <summary>Scan pages in order and stop at the first non-text page.</summary>
    public static ScanStrategy EarlyExit { get; } = new ScanStrategy("early_exit", 0, null);

    /// <summary>Scan every page. Most accurate, slowest.</summary>
    public static ScanStrategy Full { get; } = new ScanStrategy("full", 0, null);

    /// <summary>
    /// Scan up to <paramref name="count"/> evenly distributed pages (first,
    /// last, middle). This is the default, with a count of 8.
    /// </summary>
    /// <param name="count">How many pages to sample. Must be positive.</param>
    public static ScanStrategy Sample(int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Sample count must be positive.");
        }

        return new ScanStrategy("sample", count, null);
    }

    /// <summary>Scan exactly these 1-indexed pages.</summary>
    /// <param name="pages">The pages to scan. Must not be empty.</param>
    public static ScanStrategy Pages(params int[] pages)
    {
        if (pages is null)
        {
            throw new ArgumentNullException(nameof(pages));
        }

        if (pages.Length == 0)
        {
            throw new ArgumentException("At least one page is required.", nameof(pages));
        }

        return new ScanStrategy("pages", 0, (int[])pages.Clone());
    }

    internal string Kind { get; }

    internal int Count { get; }

    internal IReadOnlyList<int>? PageNumbers { get; }
}

/// <summary>
/// Tuning for the Markdown converter. Every property is optional; anything
/// left <see langword="null"/> keeps the native library's default.
/// </summary>
public sealed class MarkdownOptions
{
    /// <summary>Source fidelity versus token efficiency. Defaults to <see cref="MarkdownProfile.Fidelity"/>.</summary>
    public MarkdownProfile? Profile { get; set; }

    /// <summary>Promote larger text to headings. Default: true.</summary>
    public bool? DetectHeaders { get; set; }

    /// <summary>Recognise bulleted and numbered lists. Default: true.</summary>
    public bool? DetectLists { get; set; }

    /// <summary>Recognise monospaced runs as code blocks. Default: true.</summary>
    public bool? DetectCode { get; set; }

    /// <summary>
    /// Body font size to compare against for heading detection. Defaults to
    /// deriving it from the document.
    /// </summary>
    public double? BaseFontSize { get; set; }

    /// <summary>Drop standalone page numbers. Default: true.</summary>
    public bool? RemovePageNumbers { get; set; }

    /// <summary>Emit bare URLs as Markdown links. Default: true.</summary>
    public bool? FormatUrls { get; set; }

    /// <summary>Rejoin words hyphenated across line breaks. Default: true.</summary>
    public bool? FixHyphenation { get; set; }

    /// <summary>Emit <c>**bold**</c> for bold runs. Default: true.</summary>
    public bool? DetectBold { get; set; }

    /// <summary>Emit <c>*italic*</c> for italic runs. Default: true.</summary>
    public bool? DetectItalic { get; set; }

    /// <summary>Emit <c>&lt;u&gt;</c> for underlined runs.</summary>
    public bool? DetectUnderline { get; set; }

    /// <summary>Include image placeholders.</summary>
    public bool? IncludeImages { get; set; }

    /// <summary>Include extracted hyperlinks.</summary>
    public bool? IncludeLinks { get; set; }

    /// <summary>Insert <c>&lt;!-- Page N --&gt;</c> markers between pages. Default: false.</summary>
    public bool? IncludePageNumbers { get; set; }

    /// <summary>Strip headers and footers repeated across many pages.</summary>
    public bool? StripHeadersFooters { get; set; }
}

/// <summary>
/// A bounding box in PDF points with a <b>top-left</b> origin — the
/// convention layout models produce after coordinate conversion.
/// </summary>
[JsonConverter(typeof(BoundingBoxConverter))]
public readonly struct BoundingBox : IEquatable<BoundingBox>
{
    /// <summary>Creates a bounding box.</summary>
    /// <param name="x1">Left edge.</param>
    /// <param name="y1">Top edge.</param>
    /// <param name="x2">Right edge.</param>
    /// <param name="y2">Bottom edge.</param>
    public BoundingBox(double x1, double y1, double x2, double y2)
    {
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
    }

    /// <summary>Left edge.</summary>
    public double X1 { get; }

    /// <summary>Top edge.</summary>
    public double Y1 { get; }

    /// <summary>Right edge.</summary>
    public double X2 { get; }

    /// <summary>Bottom edge.</summary>
    public double Y2 { get; }

    /// <inheritdoc/>
    public bool Equals(BoundingBox other) =>
        X1.Equals(other.X1) && Y1.Equals(other.Y1) && X2.Equals(other.X2) && Y2.Equals(other.Y2);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is BoundingBox other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = X1.GetHashCode();
            hash = (hash * 397) ^ Y1.GetHashCode();
            hash = (hash * 397) ^ X2.GetHashCode();
            hash = (hash * 397) ^ Y2.GetHashCode();
            return hash;
        }
    }

    /// <summary>Value equality.</summary>
    public static bool operator ==(BoundingBox left, BoundingBox right) => left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(BoundingBox left, BoundingBox right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() =>
        string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "[{0}, {1}, {2}, {3}]",
            X1,
            Y1,
            X2,
            Y2);
}

/// <summary>The regions to extract from one page.</summary>
public sealed class PageRegions
{
    /// <summary>Creates a request for one page.</summary>
    /// <param name="page"><b>0-indexed</b> page number.</param>
    /// <param name="regions">Bounding boxes to extract, in PDF points with a top-left origin.</param>
    public PageRegions(int page, IEnumerable<BoundingBox> regions)
    {
        if (regions is null)
        {
            throw new ArgumentNullException(nameof(regions));
        }

        Page = page;
        Regions = new List<BoundingBox>(regions);
    }

    /// <summary><b>0-indexed</b> page number.</summary>
    public int Page { get; }

    /// <summary>The bounding boxes to extract, in request order.</summary>
    public IReadOnlyList<BoundingBox> Regions { get; }
}
