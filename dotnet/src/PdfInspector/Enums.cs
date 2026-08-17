using System.Text.Json.Serialization;
using PdfInspector.Json;

namespace PdfInspector;

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
