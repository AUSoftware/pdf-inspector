using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PdfInspector.Json;

/// <summary>
/// The response envelope every native call returns.
/// </summary>
internal sealed class Envelope<T>
{
    public bool Ok { get; init; }

    public T? Data { get; init; }

    public ErrorPayload? Error { get; init; }
}

/// <summary>The <c>error</c> member of a failed envelope.</summary>
internal sealed class ErrorPayload
{
    public string Kind { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

/// <summary>Request body for the region-extraction entry points.</summary>
internal sealed class RegionsPayload
{
    public IReadOnlyList<PageRegions> PageRegions { get; init; } = Array.Empty<PageRegions>();
}

/// <summary>
/// Source-generated serialisation metadata. Using a context rather than
/// reflection keeps the binding trim- and AOT-safe.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Envelope<PdfResult>), TypeInfoPropertyName = "PdfResultEnvelope")]
[JsonSerializable(typeof(Envelope<PdfClassification>), TypeInfoPropertyName = "ClassificationEnvelope")]
[JsonSerializable(typeof(Envelope<string>), TypeInfoPropertyName = "StringEnvelope")]
[JsonSerializable(typeof(Envelope<IReadOnlyList<TextItem>>), TypeInfoPropertyName = "TextItemsEnvelope")]
[JsonSerializable(typeof(Envelope<IReadOnlyList<StructureElement>>), TypeInfoPropertyName = "StructureElementsEnvelope")]
[JsonSerializable(typeof(Envelope<PagesExtractionResult>), TypeInfoPropertyName = "PagesExtractionEnvelope")]
[JsonSerializable(typeof(Envelope<IReadOnlyList<PageRegionText>>), TypeInfoPropertyName = "PageRegionsEnvelope")]
[JsonSerializable(typeof(PdfOptions), TypeInfoPropertyName = "PdfOptionsPayload")]
[JsonSerializable(typeof(RegionsPayload), TypeInfoPropertyName = "RegionsPayload")]
internal sealed partial class PdfJsonContext : JsonSerializerContext
{
}
