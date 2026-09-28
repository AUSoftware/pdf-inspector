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

    [Fact]
    public void Regions_CarryPositionOptionsWhenSupplied()
    {
        RegionsPayload payload = new RegionsPayload
        {
            PageRegions = new List<PageRegions>(),
            Position = new PositionOptions
            {
                Frame = PositionFrame.Display,
                BoldFromWeight = true,
            },
        };

        string json = JsonSerializer.Serialize(payload, PdfJsonContext.Default.RegionsPayload);

        Assert.Equal(
            """{"page_regions":[],"position":{"frame":"display","bold_from_weight":true}}""",
            json);
    }

    [Fact]
    public void PositionOptions_OnlyEmitSetProperties()
    {
        Assert.Equal(
            """{"position":{"frame":"sheet"}}""",
            Serialize(new PdfOptions
            {
                Position = new PositionOptions { Frame = PositionFrame.Sheet },
            }));

        Assert.Equal(
            """{"position":{"bold_from_weight":false}}""",
            Serialize(new PdfOptions
            {
                Position = new PositionOptions { BoldFromWeight = false },
            }));

        // An empty PositionOptions still sends an empty object, which the
        // native side reads as "all defaults".
        Assert.Equal(
            """{"position":{}}""",
            Serialize(new PdfOptions { Position = new PositionOptions() }));
    }

    [Fact]
    public void PositionOptions_CarryTheBoldWeightThreshold()
    {
        Assert.Equal(
            """{"position":{"bold_from_weight":true,"bold_weight_threshold":700}}""",
            Serialize(new PdfOptions
            {
                Position = new PositionOptions { BoldFromWeight = true, BoldWeightThreshold = 700 },
            }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(901)]
    public void BoldWeightThreshold_OffTheWeightScale_IsRejected(int threshold)
    {
        PositionOptions options = new PositionOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.BoldWeightThreshold = threshold);
        Assert.Null(options.BoldWeightThreshold);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(900)]
    public void BoldWeightThreshold_AtTheEndsOfTheWeightScale_IsAccepted(int threshold)
    {
        PositionOptions options = new PositionOptions { BoldWeightThreshold = threshold };

        Assert.Equal(threshold, options.BoldWeightThreshold);
        options.BoldWeightThreshold = null;
        Assert.Null(options.BoldWeightThreshold);
    }

    [Theory]
    [InlineData("not_a_pdf", PdfErrorKind.NotAPdf)]
    [InlineData("invalid_argument", PdfErrorKind.InvalidArgument)]
    [InlineData("invalid_options", PdfErrorKind.InvalidOptions)]
    [InlineData("io", PdfErrorKind.Io)]
    [InlineData("parse", PdfErrorKind.Parse)]
    [InlineData("encrypted", PdfErrorKind.Encrypted)]
    [InlineData("invalid_structure", PdfErrorKind.InvalidStructure)]
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
    public void TextItemRunMetadata_DeserialisesFromItsSnakeCaseNames()
    {
        // The native side names these fields; the snake_case naming policy is
        // the only thing binding them to the properties, so pin them here.
        const string json = """
        {"ok":true,"data":[{
          "text":"x","x":1,"y":2,"width":3,"height":4,
          "rotation":90,"advance_known":false,
          "font":"ABCDEF+CMMI10","font_tag":"F2","font_size":10,
          "font_weight":500,"legacy_symbol_rewrite":true,
          "page":1,"is_bold":false,"is_italic":false,
          "is_underline":false,"is_strikeout":false,
          "baseline_shift":-2.5,"item_type":"text","mcid":null
        }]}
        """;

        Envelope<IReadOnlyList<TextItem>>? envelope =
            JsonSerializer.Deserialize(json, PdfJsonContext.Default.TextItemsEnvelope);

        TextItem item = Assert.Single(envelope!.Data!);
        Assert.Equal(90.0, item.Rotation);
        Assert.False(item.AdvanceKnown);
        Assert.Equal("ABCDEF+CMMI10", item.Font);
        Assert.Equal("F2", item.FontTag);
        Assert.Equal(-2.5, item.BaselineShift);

        // A medium face: a weight class is reported, and it does not by
        // itself make the run bold.
        Assert.Equal(500, item.FontWeight);
        Assert.False(item.IsBold);
        Assert.True(item.LegacySymbolRewrite);
    }

    [Fact]
    public void TextItemFontWeight_IsNullWhenNoSourceStatesOne()
    {
        const string json = """
        {"ok":true,"data":[{
          "text":"x","x":1,"y":2,"width":3,"height":4,
          "rotation":0,"advance_known":true,
          "font":"Helvetica","font_tag":"F1","font_size":10,
          "font_weight":null,"legacy_symbol_rewrite":false,
          "page":1,"is_bold":false,"is_italic":false,
          "is_underline":false,"is_strikeout":false,
          "baseline_shift":0,"item_type":"text","mcid":null
        }]}
        """;

        Envelope<IReadOnlyList<TextItem>>? envelope =
            JsonSerializer.Deserialize(json, PdfJsonContext.Default.TextItemsEnvelope);

        TextItem item = Assert.Single(envelope!.Data!);
        Assert.Null(item.FontWeight);
        Assert.False(item.LegacySymbolRewrite);
    }

    [Fact]
    public void TextItemPaintAndBoldProvenance_DeserialiseFromTheirSnakeCaseNames()
    {
        const string json = """
        {"ok":true,"data":[{
          "text":"x","x":1,"y":2,"width":3,"height":4,
          "rotation":0,"advance_known":true,
          "font":"Courier-Bold","font_tag":"F1","font_size":10,
          "font_weight":700,"legacy_symbol_rewrite":false,
          "page":1,"is_bold":true,"bold_source":"font_name",
          "fixed_pitch":true,"fill_color":[255,0,128],"stroke_color":[0,0,0],
          "render_mode":2,"is_italic":false,
          "is_underline":false,"is_strikeout":false,
          "baseline_shift":0,"item_type":"text","mcid":null
        },{
          "text":"y","x":1,"y":2,"width":3,"height":4,
          "rotation":0,"advance_known":true,
          "font":"Helvetica","font_tag":"F2","font_size":10,
          "font_weight":null,"legacy_symbol_rewrite":false,
          "page":1,"is_bold":false,"bold_source":null,
          "fixed_pitch":null,"fill_color":null,"stroke_color":null,
          "render_mode":null,"is_italic":false,
          "is_underline":false,"is_strikeout":false,
          "baseline_shift":0,"item_type":"text","mcid":null
        }]}
        """;

        Envelope<IReadOnlyList<TextItem>>? envelope =
            JsonSerializer.Deserialize(json, PdfJsonContext.Default.TextItemsEnvelope);
        IReadOnlyList<TextItem> items = envelope!.Data!;
        Assert.Equal(2, items.Count);

        TextItem painted = items[0];
        Assert.Equal(BoldSource.FontName, painted.BoldSource);
        Assert.True(painted.FixedPitch);
        Assert.Equal(new RgbColor(255, 0, 128), painted.FillColor);
        Assert.Equal(new RgbColor(0, 0, 0), painted.StrokeColor);
        Assert.Equal(2, painted.RenderMode);
        Assert.Equal("#ff0080", painted.FillColor!.Value.ToString());

        TextItem plain = items[1];
        Assert.Null(plain.BoldSource);
        Assert.Null(plain.FixedPitch);
        Assert.Null(plain.FillColor);
        Assert.Null(plain.StrokeColor);
        Assert.Null(plain.RenderMode);
    }

    [Theory]
    [InlineData("\"font_name\"", BoldSource.FontName)]
    [InlineData("\"font_flags\"", BoldSource.FontFlags)]
    [InlineData("\"weight_class\"", BoldSource.WeightClass)]
    [InlineData("\"painted\"", BoldSource.Painted)]
    public void BoldSourceValues_RoundTripThroughTheirWireNames(string wire, BoldSource expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<BoldSource>(wire, JsonSerializerOptions.Default));
        Assert.Equal(wire, JsonSerializer.Serialize(expected, JsonSerializerOptions.Default));
    }

    [Fact]
    public void UnknownBoldSource_IsRejectedRatherThanSilentlyDefaulted()
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<BoldSource>("\"heavy\"", JsonSerializerOptions.Default));
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("[1,2,3,4]")]
    [InlineData("[1,2,256]")]
    [InlineData("[1,2,-1]")]
    [InlineData("\"#010203\"")]
    public void MalformedRgbColor_IsRejected(string wire)
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<RgbColor>(wire, JsonSerializerOptions.Default));
    }

    [Fact]
    public void PdfResultDocumentInformationAndCmapGaps_DeserialiseFromTheirSnakeCaseNames()
    {
        const string json = """
        {"ok":true,"data":{
          "pdf_type":"text_based","markdown":"# x","page_count":1,
          "processing_time_ms":3,"pages_needing_ocr":[],"ocr_reasons_by_page":[],
          "title":"T","author":"A","subject":"S","keywords":"K",
          "creator":"Writer","producer":"pypdf",
          "creation_date":"D:20240115103000+01'00'","mod_date":null,
          "confidence":0.9,"is_complex_layout":false,
          "pages_with_tables":[],"pages_with_columns":[],
          "has_encoding_issues":true,
          "cmap_gaps":[{"font":"AAAAAA+Subset","codes":24,"interpolated":8,"unmapped":2}]
        }}
        """;

        Envelope<PdfResult>? envelope =
            JsonSerializer.Deserialize(json, PdfJsonContext.Default.PdfResultEnvelope);
        PdfResult result = envelope!.Data!;

        Assert.Equal("T", result.Title);
        Assert.Equal("A", result.Author);
        Assert.Equal("S", result.Subject);
        Assert.Equal("K", result.Keywords);
        Assert.Equal("Writer", result.Creator);
        Assert.Equal("pypdf", result.Producer);
        Assert.Equal("D:20240115103000+01'00'", result.CreationDate);
        Assert.Null(result.ModDate);

        FontCMapGaps gap = Assert.Single(result.CmapGaps);
        Assert.Equal("AAAAAA+Subset", gap.Font);
        Assert.Equal(24, gap.Codes);
        Assert.Equal(8, gap.Interpolated);
        Assert.Equal(2, gap.Unmapped);
    }

    [Fact]
    public void RgbColor_HasValueSemantics()
    {
        Assert.Equal(new RgbColor(1, 2, 3), new RgbColor(1, 2, 3));
        Assert.True(new RgbColor(1, 2, 3) == new RgbColor(1, 2, 3));
        Assert.True(new RgbColor(1, 2, 3) != new RgbColor(1, 2, 4));
        Assert.Equal(new RgbColor(1, 2, 3).GetHashCode(), new RgbColor(1, 2, 3).GetHashCode());
    }

    [Fact]
    public void PositionFrameValues_RoundTripThroughTheirWireNames()
    {
        Assert.Equal(
            PositionFrame.Display,
            JsonSerializer.Deserialize<PositionFrame>("\"display\"", JsonSerializerOptions.Default));
        Assert.Equal(
            "\"sheet\"",
            JsonSerializer.Serialize(PositionFrame.Sheet, JsonSerializerOptions.Default));
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<PositionFrame>(
                "\"rendered\"",
                JsonSerializerOptions.Default));
    }

    [Fact]
    public void BoundingBox_HasValueSemantics()
    {
        Assert.Equal(new BoundingBox(1, 2, 3, 4), new BoundingBox(1, 2, 3, 4));
        Assert.NotEqual(new BoundingBox(1, 2, 3, 4), new BoundingBox(1, 2, 3, 5));
        Assert.True(new BoundingBox(1, 2, 3, 4) == new BoundingBox(1, 2, 3, 4));
        Assert.True(new BoundingBox(1, 2, 3, 4) != new BoundingBox(0, 2, 3, 4));
    }
}
