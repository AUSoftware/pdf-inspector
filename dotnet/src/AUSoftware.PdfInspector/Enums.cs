using System.Text.Json.Serialization;
using AUSoftware.PdfInspector.Json;

namespace AUSoftware.PdfInspector;

/// <summary>How a PDF's content is stored, and therefore how to read it.</summary>
[JsonConverter(typeof(PdfTypeConverter))]
public enum PdfType
{
    /// <summary>Real text operators are present; extraction is reliable.</summary>
    TextBased,

    /// <summary>Scanned pages — images with no usable text layer. Needs OCR.</summary>
    Scanned,

    /// <summary>Mostly images with minimal or no text.</summary>
    ImageBased,

    /// <summary>A mix of text pages and image-heavy pages.</summary>
    Mixed,
}

/// <summary>What a <see cref="TextItem"/> represents.</summary>
[JsonConverter(typeof(ItemTypeConverter))]
public enum ItemType
{
    /// <summary>Regular text content.</summary>
    Text,

    /// <summary>An image placeholder.</summary>
    Image,

    /// <summary>A hyperlink; see <see cref="TextItem.Url"/>.</summary>
    Link,

    /// <summary>A form field value.</summary>
    FormField,
}

/// <summary>How far the processing pipeline should run.</summary>
[JsonConverter(typeof(ProcessModeConverter))]
public enum ProcessMode
{
    /// <summary>Classification only — no text extraction. Fastest.</summary>
    DetectOnly,

    /// <summary>Detect, extract text, and compute layout complexity; skip Markdown.</summary>
    Analyze,

    /// <summary>The full pipeline, ending in Markdown. The default.</summary>
    Full,
}

/// <summary>Source-fidelity versus token-efficiency in Markdown post-processing.</summary>
[JsonConverter(typeof(MarkdownProfileConverter))]
public enum MarkdownProfile
{
    /// <summary>Preserve the source text as closely as possible. The default.</summary>
    Fidelity,

    /// <summary>Prefer compact output, e.g. collapsing long dot leaders.</summary>
    Compact,
}

/// <summary>When <see cref="Pdf.ProcessWithOcr(string, OcrOptions?)"/> may run OCR.</summary>
[JsonConverter(typeof(OcrModeConverter))]
public enum OcrMode
{
    /// <summary>
    /// Never run OCR — return the plain native extraction in the OCR result
    /// shape. Nothing outside this package is ever loaded.
    /// </summary>
    Off,

    /// <summary>
    /// OCR only the pages native extraction flagged. The default: a document
    /// with no flagged pages never touches the OCR runtime.
    /// </summary>
    Auto,

    /// <summary>
    /// OCR every selected page, even ones with good native text. Always needs
    /// the OCR runtime.
    /// </summary>
    Force,
}

/// <summary>Where a page's final Markdown came from.</summary>
[JsonConverter(typeof(PageContentSourceConverter))]
public enum PageContentSource
{
    /// <summary>
    /// A source this binding does not recognise, because the native library is
    /// newer than the package. Treat it as "content of unknown provenance".
    /// </summary>
    Unknown = 0,

    /// <summary>The PDF's own text layer.</summary>
    Native,

    /// <summary>OCR output only.</summary>
    Ocr,

    /// <summary>Native text and OCR spans were merged.</summary>
    Fused,
}
