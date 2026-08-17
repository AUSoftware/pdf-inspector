using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AUSoftware.PdfInspector.Json;

/// <summary>
/// Base for the enum converters. The native library speaks snake_case, and
/// the mappings are written out longhand so a rename on either side is a
/// compile error rather than a silent wire-format change.
/// </summary>
internal abstract class SnakeCaseEnumConverter<T> : JsonConverter<T>
    where T : struct, Enum
{
    /// <summary>Maps a wire value onto an enum member.</summary>
    protected abstract bool TryParse(string value, out T result);

    /// <summary>Maps an enum member onto its wire value.</summary>
    protected abstract string Format(T value);

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"Expected a string for {typeof(T).Name}, found {reader.TokenType}.");
        }

        string? value = reader.GetString();
        if (value is not null && TryParse(value, out T result))
        {
            return result;
        }

        throw new JsonException($"Unrecognised {typeof(T).Name} value '{value}'.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        writer.WriteStringValue(Format(value));
    }
}

internal sealed class PdfTypeConverter : SnakeCaseEnumConverter<PdfType>
{
    protected override bool TryParse(string value, out PdfType result)
    {
        switch (value)
        {
            case "text_based":
                result = PdfType.TextBased;
                return true;
            case "scanned":
                result = PdfType.Scanned;
                return true;
            case "image_based":
                result = PdfType.ImageBased;
                return true;
            case "mixed":
                result = PdfType.Mixed;
                return true;
            default:
                result = default;
                return false;
        }
    }

    protected override string Format(PdfType value) => value switch
    {
        PdfType.TextBased => "text_based",
        PdfType.Scanned => "scanned",
        PdfType.ImageBased => "image_based",
        PdfType.Mixed => "mixed",
        _ => throw new JsonException($"Unrecognised PdfType value '{value}'."),
    };
}

internal sealed class ItemTypeConverter : SnakeCaseEnumConverter<ItemType>
{
    protected override bool TryParse(string value, out ItemType result)
    {
        switch (value)
        {
            case "text":
                result = ItemType.Text;
                return true;
            case "image":
                result = ItemType.Image;
                return true;
            case "link":
                result = ItemType.Link;
                return true;
            case "form_field":
                result = ItemType.FormField;
                return true;
            default:
                result = default;
                return false;
        }
    }

    protected override string Format(ItemType value) => value switch
    {
        ItemType.Text => "text",
        ItemType.Image => "image",
        ItemType.Link => "link",
        ItemType.FormField => "form_field",
        _ => throw new JsonException($"Unrecognised ItemType value '{value}'."),
    };
}

internal sealed class ProcessModeConverter : SnakeCaseEnumConverter<ProcessMode>
{
    protected override bool TryParse(string value, out ProcessMode result)
    {
        switch (value)
        {
            case "detect_only":
                result = ProcessMode.DetectOnly;
                return true;
            case "analyze":
                result = ProcessMode.Analyze;
                return true;
            case "full":
                result = ProcessMode.Full;
                return true;
            default:
                result = default;
                return false;
        }
    }

    protected override string Format(ProcessMode value) => value switch
    {
        ProcessMode.DetectOnly => "detect_only",
        ProcessMode.Analyze => "analyze",
        ProcessMode.Full => "full",
        _ => throw new JsonException($"Unrecognised ProcessMode value '{value}'."),
    };
}

internal sealed class MarkdownProfileConverter : SnakeCaseEnumConverter<MarkdownProfile>
{
    protected override bool TryParse(string value, out MarkdownProfile result)
    {
        switch (value)
        {
            case "fidelity":
                result = MarkdownProfile.Fidelity;
                return true;
            case "compact":
                result = MarkdownProfile.Compact;
                return true;
            default:
                result = default;
                return false;
        }
    }

    protected override string Format(MarkdownProfile value) => value switch
    {
        MarkdownProfile.Fidelity => "fidelity",
        MarkdownProfile.Compact => "compact",
        _ => throw new JsonException($"Unrecognised MarkdownProfile value '{value}'."),
    };
}

/// <summary>
/// Writes a <see cref="ScanStrategy"/> as the tagged object the native
/// library expects: <c>{"type":"sample","count":8}</c>.
/// </summary>
internal sealed class ScanStrategyConverter : JsonConverter<ScanStrategy>
{
    public override ScanStrategy Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("ScanStrategy is only ever sent to the native library.");

    public override void Write(Utf8JsonWriter writer, ScanStrategy value, JsonSerializerOptions options)
    {
        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        writer.WriteStartObject();
        writer.WriteString("type", value.Kind);

        if (value.Kind == "sample")
        {
            writer.WriteNumber("count", value.Count);
        }
        else if (value.PageNumbers is not null)
        {
            writer.WritePropertyName("pages");
            writer.WriteStartArray();
            foreach (int page in value.PageNumbers)
            {
                writer.WriteNumberValue(page);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }
}

/// <summary>
/// Writes a <see cref="BoundingBox"/> as the flat <c>[x1, y1, x2, y2]</c>
/// array the native library expects.
/// </summary>
internal sealed class BoundingBoxConverter : JsonConverter<BoundingBox>
{
    public override BoundingBox Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("BoundingBox is only ever sent to the native library.");

    public override void Write(Utf8JsonWriter writer, BoundingBox value, JsonSerializerOptions options)
    {
        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        writer.WriteStartArray();
        writer.WriteNumberValue(value.X1);
        writer.WriteNumberValue(value.Y1);
        writer.WriteNumberValue(value.X2);
        writer.WriteNumberValue(value.Y2);
        writer.WriteEndArray();
    }
}
