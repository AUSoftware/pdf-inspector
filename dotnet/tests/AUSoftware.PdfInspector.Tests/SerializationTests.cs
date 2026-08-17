using System;
using System.Collections.Generic;
using System.Text.Json;
using AUSoftware.PdfInspector.Interop;
using AUSoftware.PdfInspector.Json;
using Xunit;

namespace AUSoftware.PdfInspector.Tests;

/// <summary>
/// Pins the wire format. The native side rejects unknown fields, so a
/// renamed property here is a runtime failure rather than a compile error —
/// these tests catch it at build time instead.
/// </summary>
public class SerializationTests
{
    private static string Serialize(PdfOptions options) =>
        JsonSerializer.Serialize(options, PdfJsonContext.Default.PdfOptionsPayload);

    private static string Serialize(OcrOptions options) =>
        JsonSerializer.Serialize(options, PdfJsonContext.Default.OcrOptionsPayload);

    [Fact]
    public void EmptyOptions_SerialiseToAnEmptyObject()
    {
        Assert.Equal("{}", Serialize(new PdfOptions()));
    }

    [Fact]
    public void SetProperties_UseSnakeCaseNames()
    {
        string json = Serialize(new PdfOptions
        {
            Pages = new[] { 1, 2 },
            Password = "hunter2",
            Mode = ProcessMode.DetectOnly,
        });

        Assert.Equal("""{"pages":[1,2],"password":"hunter2","mode":"detect_only"}""", json);
    }

    [Fact]
    public void MarkdownOptions_OnlyEmitSetProperties()
    {
        string json = Serialize(new PdfOptions
        {
            Markdown = new MarkdownOptions
            {
                Profile = MarkdownProfile.Compact,
                DetectHeaders = false,
                BaseFontSize = 10.5,
                StripHeadersFooters = true,
            },
        });

        Assert.Equal(
            """{"markdown":{"profile":"compact","detect_headers":false,"base_font_size":10.5,"strip_headers_footers":true}}""",
            json);
    }

    [Theory]
    [InlineData("early_exit")]
    [InlineData("full")]
    public void SimpleScanStrategies_SerialiseAsTaggedObjects(string kind)
    {
        ScanStrategy strategy = kind == "full" ? ScanStrategy.Full : ScanStrategy.EarlyExit;
        string json = Serialize(new PdfOptions { Detection = new DetectionOptions { Strategy = strategy } });

        Assert.Equal(
            "{\"detection\":{\"strategy\":{\"type\":\"" + kind + "\"}}}",
            json);
    }

    [Fact]
    public void SampleStrategy_CarriesItsCount()
    {
        string json = Serialize(new PdfOptions
        {
            Detection = new DetectionOptions { Strategy = ScanStrategy.Sample(4) },
        });

        Assert.Equal("""{"detection":{"strategy":{"type":"sample","count":4}}}""", json);
    }

    [Fact]
    public void PagesStrategy_CarriesItsPages()
    {
        string json = Serialize(new PdfOptions
        {
            Detection = new DetectionOptions
            {
                Strategy = ScanStrategy.Pages(2, 5),
                MinTextOpsPerPage = 7,
                TextPageRatioThreshold = 0.25,
            },
        });

        Assert.Equal(
            """{"detection":{"strategy":{"type":"pages","pages":[2,5]},"min_text_ops_per_page":7,"text_page_ratio_threshold":0.25}}""",
            json);
    }

    [Fact]
    public void Regions_SerialiseAsFlatCoordinateArrays()
    {
        RegionsPayload payload = new RegionsPayload
        {
            PageRegions = new List<PageRegions>
            {
                new PageRegions(0, new[] { new BoundingBox(1, 2, 3, 4) }),
            },
        };

        string json = JsonSerializer.Serialize(payload, PdfJsonContext.Default.RegionsPayload);

        Assert.Equal("""{"page_regions":[{"page":0,"regions":[[1,2,3,4]]}]}""", json);
    }

    [Theory]
    [InlineData("not_a_pdf", PdfErrorKind.NotAPdf)]
    [InlineData("invalid_argument", PdfErrorKind.InvalidArgument)]
    [InlineData("invalid_options", PdfErrorKind.InvalidOptions)]
    [InlineData("io", PdfErrorKind.Io)]
    [InlineData("parse", PdfErrorKind.Parse)]
    [InlineData("encrypted", PdfErrorKind.Encrypted)]
    [InlineData("invalid_structure", PdfErrorKind.InvalidStructure)]
    [InlineData("ocr", PdfErrorKind.Ocr)]
    [InlineData("panic", PdfErrorKind.Panic)]
    [InlineData("internal", PdfErrorKind.Internal)]
    [InlineData("something_new", PdfErrorKind.Unknown)]
    public void ErrorKinds_MapOntoTheEnum(string nativeKind, PdfErrorKind expected)
    {
        Assert.Equal(expected, NativeCall.ParseKind(nativeKind));
    }

    [Fact]
    public void PdfTypeValues_RoundTripThroughTheirWireNames()
    {
        Assert.Equal(
            PdfType.ImageBased,
            JsonSerializer.Deserialize<PdfType>("\"image_based\"", JsonSerializerOptions.Default));
        Assert.Equal(
            "\"text_based\"",
            JsonSerializer.Serialize(PdfType.TextBased, JsonSerializerOptions.Default));
    }

    [Fact]
    public void UnknownPdfType_IsRejectedRatherThanSilentlyDefaulted()
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<PdfType>("\"hologram\"", JsonSerializerOptions.Default));
    }

    [Fact]
    public void BoundingBox_HasValueSemantics()
    {
        Assert.Equal(new BoundingBox(1, 2, 3, 4), new BoundingBox(1, 2, 3, 4));
        Assert.NotEqual(new BoundingBox(1, 2, 3, 4), new BoundingBox(1, 2, 3, 5));
        Assert.True(new BoundingBox(1, 2, 3, 4) == new BoundingBox(1, 2, 3, 4));
        Assert.True(new BoundingBox(1, 2, 3, 4) != new BoundingBox(0, 2, 3, 4));
    }

    [Fact]
    public void EmptyOcrOptions_SerialiseToAnEmptyObject()
    {
        // The native side layers the payload over its own `auto` preset, so an
        // empty object has to mean "every default", not "every field zeroed".
        Assert.Equal("{}", Serialize(new OcrOptions()));
    }

    [Fact]
    public void OcrOptions_UseTheNativeFieldNames()
    {
        string json = Serialize(new OcrOptions
        {
            Mode = OcrMode.Force,
            Pages = new[] { 1, 3 },
            Password = "hunter2",
            Dpi = 300,
            MinimumConfidence = 0.25,
            HostedRecommendationConfidence = 0.75,
            ModelDirectory = "/models",
            Offline = true,
        });

        Assert.Equal(
            """{"mode":"force","pages":[1,3],"password":"hunter2","dpi":300,"minimum_confidence":0.25,"hosted_recommendation_confidence":0.75,"model_directory":"/models","offline":true}""",
            json);
    }

    [Fact]
    public void OcrOptions_NestMarkdownTuningUnderTheSameKey()
    {
        string json = Serialize(new OcrOptions
        {
            Markdown = new MarkdownOptions { DetectHeaders = false },
        });

        Assert.Equal("""{"markdown":{"detect_headers":false}}""", json);
    }

    [Theory]
    [InlineData("off", OcrMode.Off)]
    [InlineData("auto", OcrMode.Auto)]
    [InlineData("force", OcrMode.Force)]
    public void OcrModeValues_RoundTripThroughTheirWireNames(string wire, OcrMode expected)
    {
        Assert.Equal(
            expected,
            JsonSerializer.Deserialize<OcrMode>($"\"{wire}\"", JsonSerializerOptions.Default));
        Assert.Equal(
            $"\"{wire}\"",
            JsonSerializer.Serialize(expected, JsonSerializerOptions.Default));
    }

    [Theory]
    [InlineData("native", PageContentSource.Native)]
    [InlineData("ocr", PageContentSource.Ocr)]
    [InlineData("fused", PageContentSource.Fused)]
    public void PageContentSourceValues_RoundTripThroughTheirWireNames(
        string wire,
        PageContentSource expected)
    {
        Assert.Equal(
            expected,
            JsonSerializer.Deserialize<PageContentSource>($"\"{wire}\"", JsonSerializerOptions.Default));
    }

    [Fact]
    public void UnknownPageContentSource_DegradesRatherThanThrowing()
    {
        // Deliberately unlike PdfType: a page whose source this binding cannot
        // name still carries Markdown the caller wants.
        Assert.Equal(
            PageContentSource.Unknown,
            JsonSerializer.Deserialize<PageContentSource>("\"telepathy\"", JsonSerializerOptions.Default));
    }
}
