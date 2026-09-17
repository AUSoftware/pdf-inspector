using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace AUSoftware.PdfInspector.Tests;

/// <summary>
/// Exercises the public API against the repository's fixture PDFs.
/// </summary>
public class PdfTests
{
    private const string TextFixture = "2013-app2.pdf";
    private const string MultiPageFixture = "shannon-entropy-p1-2.pdf";
    private const string TaggedFixture = "firecrawl_docs_tagged.pdf";
    private const string UntaggedFixture = "thermo-freon12.pdf";
    private const string EncryptedFixture = "encrypted-secret123.pdf";

    // -----------------------------------------------------------------
    // Loading
    // -----------------------------------------------------------------

    [Fact]
    public void NativeVersion_IsReported()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+", Pdf.NativeVersion);
    }

    // -----------------------------------------------------------------
    // Process
    // -----------------------------------------------------------------

    [Fact]
    public void Process_File_ReturnsMarkdownAndMetadata()
    {
        PdfResult result = Pdf.Process(TestEnvironment.Fixture(TextFixture));

        Assert.Equal(PdfType.TextBased, result.PdfType);
        Assert.False(string.IsNullOrWhiteSpace(result.Markdown));
        Assert.True(result.PageCount > 0);
        Assert.InRange(result.Confidence, 0.0, 1.0);
        Assert.False(result.HasEncodingIssues);
        Assert.NotNull(result.PagesWithTables);
        Assert.NotNull(result.OcrReasonsByPage);
    }

    [Fact]
    public void Process_Bytes_MatchesProcessFile()
    {
        PdfResult fromFile = Pdf.Process(TestEnvironment.Fixture(TextFixture));
        PdfResult fromBytes = Pdf.Process(TestEnvironment.FixtureBytes(TextFixture));

        Assert.Equal(fromFile.Markdown, fromBytes.Markdown);
        Assert.Equal(fromFile.PageCount, fromBytes.PageCount);
        Assert.Equal(fromFile.PdfType, fromBytes.PdfType);
    }

    [Fact]
    public void Process_PageFilter_IsOneIndexedAndNarrowsOutput()
    {
        byte[] pdf = TestEnvironment.FixtureBytes(MultiPageFixture);

        PdfResult all = Pdf.Process(pdf);
        PdfResult first = Pdf.Process(pdf, new PdfOptions { Pages = new[] { 1 } });

        Assert.False(string.IsNullOrEmpty(first.Markdown));
        Assert.True(
            first.Markdown!.Length < all.Markdown!.Length,
            "restricting to page 1 should produce less markdown than the whole document");
    }

    [Fact]
    public void Process_MarkdownOptions_ReachTheConverter()
    {
        PdfResult result = Pdf.Process(
            TestEnvironment.FixtureBytes(MultiPageFixture),
            new PdfOptions { Markdown = new MarkdownOptions { IncludePageNumbers = true } });

        Assert.Contains("<!-- Page", result.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Process_AnalyzeMode_SkipsMarkdown()
    {
        PdfResult result = Pdf.Process(
            TestEnvironment.FixtureBytes(TextFixture),
            new PdfOptions { Mode = ProcessMode.Analyze });

        Assert.Null(result.Markdown);
        Assert.Equal(PdfType.TextBased, result.PdfType);
    }

    [Fact]
    public void Process_DetectionOptions_AreAccepted()
    {
        foreach (ScanStrategy strategy in new[]
                 {
                     ScanStrategy.Full,
                     ScanStrategy.EarlyExit,
                     ScanStrategy.Sample(2),
                     ScanStrategy.Pages(1),
                 })
        {
            PdfResult result = Pdf.Process(
                TestEnvironment.FixtureBytes(TextFixture),
                new PdfOptions
                {
                    Detection = new DetectionOptions
                    {
                        Strategy = strategy,
                        MinTextOpsPerPage = 2,
                        TextPageRatioThreshold = 0.5,
                    },
                });

            Assert.Equal(PdfType.TextBased, result.PdfType);
        }
    }

    // -----------------------------------------------------------------
    // Detect / Classify
    // -----------------------------------------------------------------

    [Fact]
    public void Detect_DoesNotExtractMarkdown()
    {
        PdfResult result = Pdf.Detect(TestEnvironment.FixtureBytes(TextFixture));

        Assert.Null(result.Markdown);
        Assert.Equal(PdfType.TextBased, result.PdfType);
        Assert.True(result.PageCount > 0);
    }

    [Fact]
    public void Detect_IgnoresACallerSuppliedMode()
    {
        PdfResult result = Pdf.Detect(
            TestEnvironment.FixtureBytes(TextFixture),
            new PdfOptions { Mode = ProcessMode.Full });

        Assert.Null(result.Markdown);
    }

    [Fact]
    public void Classify_ReportsTypeAndPageCount()
    {
        PdfClassification classification = Pdf.Classify(TestEnvironment.Fixture(TextFixture));

        Assert.Equal(PdfType.TextBased, classification.PdfType);
        Assert.True(classification.PageCount > 0);
        Assert.All(classification.PagesNeedingOcr, page => Assert.InRange(page, 0, classification.PageCount - 1));
    }

    [Fact]
    public void Classify_FileAndBytesAgree()
    {
        PdfClassification fromFile = Pdf.Classify(TestEnvironment.Fixture(TextFixture));
        PdfClassification fromBytes = Pdf.Classify(TestEnvironment.FixtureBytes(TextFixture));

        Assert.Equal(fromFile.PdfType, fromBytes.PdfType);
        Assert.Equal(fromFile.PageCount, fromBytes.PageCount);
        Assert.Equal(fromFile.PagesNeedingOcr, fromBytes.PagesNeedingOcr);
    }

    // -----------------------------------------------------------------
    // Text
    // -----------------------------------------------------------------

    [Fact]
    public void ExtractText_ReturnsPlainText()
    {
        string text = Pdf.ExtractText(TestEnvironment.Fixture(TextFixture));

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public void ExtractTextWithPositions_CarriesGeometryAndFontMetadata()
    {
        IReadOnlyList<TextItem> items =
            Pdf.ExtractTextWithPositions(TestEnvironment.FixtureBytes(TextFixture));

        Assert.NotEmpty(items);

        TextItem first = items[0];
        Assert.False(string.IsNullOrEmpty(first.Text));
        Assert.True(first.FontSize > 0);
        Assert.Equal(1, first.Page);
        Assert.Equal(ItemType.Text, first.ItemType);
        Assert.Null(first.Url);

        // Run-level metadata: `Font` is the /BaseFont family name, `FontTag`
        // the resource tag that selected it.
        Assert.False(string.IsNullOrEmpty(first.Font));
        Assert.False(string.IsNullOrEmpty(first.FontTag));
        Assert.True(first.AdvanceKnown);
        Assert.InRange(first.Rotation, 0.0, 360.0);
        Assert.All(items, item => Assert.InRange(item.Rotation, 0.0, 360.0));

        // Body text on this fixture is upright and unshifted.
        Assert.Equal(0.0, first.Rotation);
        Assert.Equal(0.0, first.BaselineShift);

        // The weight class (1.20.0). A null weight means no source stated
        // one, which is common and not an error. Whether this fixture has a
        // legacy symbol rewrite is not something to assert on — that field's
        // wire binding is pinned in SerializationTests instead.
        Assert.All(
            items,
            item => Assert.True(
                item.FontWeight is null || (item.FontWeight >= 100 && item.FontWeight <= 900),
                $"weight class is null or on the 100-900 scale: {item.FontWeight}"));
    }

    [Fact]
    public void ExtractTextWithPositions_HonoursPositionOptions()
    {
        byte[] pdf = TestEnvironment.FixtureBytes(TextFixture);
        IReadOnlyList<TextItem> sheet = Pdf.ExtractTextWithPositions(pdf);

        // This fixture declares no /Rotate, so the rendered page is the
        // sheet and both frames report identical geometry. The frame maths
        // itself is the native library's to test; this pins the plumbing.
        foreach (PositionFrame frame in new[] { PositionFrame.Sheet, PositionFrame.Display })
        {
            IReadOnlyList<TextItem> framed = Pdf.ExtractTextWithPositions(
                pdf,
                new PdfOptions { Position = new PositionOptions { Frame = frame } });

            Assert.Equal(sheet.Count, framed.Count);
            for (int i = 0; i < sheet.Count; i++)
            {
                Assert.Equal(sheet[i].Text, framed[i].Text);
                Assert.Equal(sheet[i].X, framed[i].X);
                Assert.Equal(sheet[i].Y, framed[i].Y);
                Assert.Equal(sheet[i].Rotation, framed[i].Rotation);
            }
        }

        // With BoldFromWeight on, a weight class of 600 or more reads as
        // bold. Faces below that, and items with no stated weight, are
        // unaffected.
        IReadOnlyList<TextItem> weighted = Pdf.ExtractTextWithPositions(
            pdf,
            new PdfOptions { Position = new PositionOptions { BoldFromWeight = true } });
        Assert.NotEmpty(weighted);
        Assert.All(
            weighted,
            item => Assert.True(
                item.FontWeight is null || item.FontWeight < 600 || item.IsBold,
                $"a weight class of {item.FontWeight} should read as bold"));
    }

    [Fact]
    public void ExtractTextWithPositions_RefusesAPasswordAlongsidePositionOptions()
    {
        // The native library cannot decrypt and re-frame in one pass, so the
        // combination is refused rather than silently dropping one of them.
        PdfInspectorException error = Assert.Throws<PdfInspectorException>(
            () => Pdf.ExtractTextWithPositions(
                TestEnvironment.Fixture(EncryptedFixture),
                new PdfOptions
                {
                    Password = "secret123",
                    Position = new PositionOptions { Frame = PositionFrame.Display },
                }));

        Assert.Equal(PdfErrorKind.InvalidOptions, error.Kind);
    }

    [Fact]
    public void ExtractTextWithPositions_HonoursTheOneIndexedPageFilter()
    {
        IReadOnlyList<TextItem> items = Pdf.ExtractTextWithPositions(
            TestEnvironment.FixtureBytes(MultiPageFixture),
            new PdfOptions { Pages = new[] { 2 } });

        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.Equal(2, item.Page));
    }

    // -----------------------------------------------------------------
    // Structure tree
    // -----------------------------------------------------------------

    [Fact]
    public void ExtractStructureElements_ResolvesRolesForTaggedPdfs()
    {
        IReadOnlyList<StructureElement> elements =
            Pdf.ExtractStructureElements(TestEnvironment.FixtureBytes(TaggedFixture));

        Assert.NotEmpty(elements);
        Assert.All(elements, element => Assert.False(string.IsNullOrEmpty(element.Role)));

        // Sorted by (page, mcid), which is what makes the join with
        // ExtractTextWithPositions cheap.
        Assert.Equal(
            elements.OrderBy(e => e.Page).ThenBy(e => e.Mcid).Select(e => (e.Page, e.Mcid)),
            elements.Select(e => (e.Page, e.Mcid)));
    }

    [Fact]
    public void ExtractStructureElements_IsEmptyForUntaggedPdfs()
    {
        Assert.Empty(Pdf.ExtractStructureElements(TestEnvironment.FixtureBytes(UntaggedFixture)));
    }

    [Fact]
    public void StructureElements_JoinToTextItemsByPageAndMcid()
    {
        byte[] pdf = TestEnvironment.FixtureBytes(TaggedFixture);

        IReadOnlyList<StructureElement> elements = Pdf.ExtractStructureElements(pdf);
        IReadOnlyList<TextItem> items = Pdf.ExtractTextWithPositions(pdf);

        HashSet<(int Page, long Mcid)> roles =
            elements.Select(e => (e.Page, e.Mcid)).ToHashSet();

        int matched = items.Count(item => item.Mcid.HasValue && roles.Contains((item.Page, item.Mcid.Value)));
        Assert.True(matched > 0, "tagged text items should join onto structure elements");
    }

    // -----------------------------------------------------------------
    // Per-page markdown
    // -----------------------------------------------------------------

    [Fact]
    public void ExtractPagesMarkdown_UsesZeroIndexedPagesAndPreservesOrder()
    {
        PagesExtractionResult result = Pdf.ExtractPagesMarkdown(
            TestEnvironment.FixtureBytes(MultiPageFixture),
            new PdfOptions { Pages = new[] { 1, 0 } });

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(1, result.Pages[0].Page);
        Assert.Equal(0, result.Pages[1].Page);
    }

    [Fact]
    public void ExtractPagesMarkdown_ReturnsEveryPageByDefault()
    {
        PagesExtractionResult result =
            Pdf.ExtractPagesMarkdown(TestEnvironment.Fixture(MultiPageFixture));

        Assert.NotEmpty(result.Pages);
        Assert.Equal(Enumerable.Range(0, result.Pages.Count), result.Pages.Select(p => p.Page));
        Assert.All(result.Pages, page => Assert.False(page.NeedsOcr));
    }

    // -----------------------------------------------------------------
    // Regions
    // -----------------------------------------------------------------

    [Fact]
    public void ExtractTextInRegions_ReturnsOneResultPerRequestedRegion()
    {
        PageRegions request = new PageRegions(
            0,
            new[]
            {
                new BoundingBox(0, 0, 612, 400),
                new BoundingBox(0, 400, 612, 792),
            });

        IReadOnlyList<PageRegionText> pages =
            Pdf.ExtractTextInRegions(TestEnvironment.FixtureBytes(TextFixture), new[] { request });

        PageRegionText page = Assert.Single(pages);
        Assert.Equal(0, page.Page);
        Assert.Equal(2, page.Regions.Count);
        Assert.Contains(page.Regions, region => region.Text.Length > 0);
    }

    [Fact]
    public void ExtractTextInRegions_AcceptsPositionOptions()
    {
        byte[] pdf = TestEnvironment.FixtureBytes(TextFixture);
        PageRegions request = new PageRegions(0, new[] { new BoundingBox(0, 0, 612, 400) });

        string sheet = Pdf.ExtractTextInRegions(pdf, new[] { request })[0].Regions[0].Text;

        // No /Rotate on this fixture, so a rect read in the display frame
        // selects the same text as one read in the sheet frame.
        string display = Pdf.ExtractTextInRegions(
            pdf,
            new[] { request },
            new PositionOptions { Frame = PositionFrame.Display })[0].Regions[0].Text;

        Assert.Equal(sheet, display);
    }

    [Fact]
    public void ExtractTablesInRegions_ReturnsOneResultPerRequestedRegion()
    {
        PageRegions request = new PageRegions(
            0,
            new[]
            {
                new BoundingBox(0, 0, 612, 400),
                new BoundingBox(0, 400, 612, 792),
            });

        IReadOnlyList<PageRegionText> pages =
            Pdf.ExtractTablesInRegions(TestEnvironment.FixtureBytes(TextFixture), new[] { request });

        PageRegionText page = Assert.Single(pages);
        Assert.Equal(0, page.Page);
        Assert.Equal(2, page.Regions.Count);

        // A region either yields a pipe-table or asks for OCR — never both,
        // and never neither.
        Assert.All(
            page.Regions,
            region =>
            {
                Assert.Equal(region.Text.Length == 0, region.NeedsOcr);
                if (!region.NeedsOcr)
                {
                    Assert.Contains('|', region.Text);
                }
            });
    }

    [Fact]
    public void ExtractTablesInRegions_MatchesAcrossThePathAndByteOverloads()
    {
        PageRegions request = new PageRegions(0, new[] { new BoundingBox(0, 0, 612, 792) });

        IReadOnlyList<PageRegionText> fromPath =
            Pdf.ExtractTablesInRegions(TestEnvironment.Fixture(TextFixture), new[] { request });
        IReadOnlyList<PageRegionText> fromBytes =
            Pdf.ExtractTablesInRegions(TestEnvironment.FixtureBytes(TextFixture), new[] { request });

        Assert.Equal(fromPath[0].Regions[0].Text, fromBytes[0].Regions[0].Text);
        Assert.Equal(fromPath[0].Regions[0].NeedsOcr, fromBytes[0].Regions[0].NeedsOcr);
    }

    [Fact]
    public void ExtractTablesInRegions_RejectsNullRegions()
    {
        Assert.Throws<ArgumentNullException>(
            () => Pdf.ExtractTablesInRegions(TestEnvironment.Fixture(TextFixture), null!));
    }

    // -----------------------------------------------------------------
    // Errors
    // -----------------------------------------------------------------

    [Fact]
    public void NonPdfBytes_ThrowNotAPdf()
    {
        byte[] garbage = System.Text.Encoding.UTF8.GetBytes("not a pdf at all");

        PdfInspectorException error = Assert.Throws<PdfInspectorException>(() => Pdf.Process(garbage));
        Assert.Equal(PdfErrorKind.NotAPdf, error.Kind);
        Assert.Equal("not_a_pdf", error.NativeKind);
        Assert.False(string.IsNullOrEmpty(error.Message));
    }

    [Fact]
    public void MissingFile_ThrowsIo()
    {
        PdfInspectorException error =
            Assert.Throws<PdfInspectorException>(() => Pdf.Process("/nonexistent/missing.pdf"));

        Assert.Equal(PdfErrorKind.Io, error.Kind);
    }

    [Fact]
    public void EncryptedPdf_ThrowsUntilAPasswordIsSupplied()
    {
        string path = TestEnvironment.Fixture(EncryptedFixture);

        PdfInspectorException error = Assert.Throws<PdfInspectorException>(() => Pdf.Process(path));
        Assert.Equal(PdfErrorKind.Encrypted, error.Kind);

        PdfResult result = Pdf.Process(path, new PdfOptions { Password = "secret123" });
        Assert.False(string.IsNullOrWhiteSpace(result.Markdown));
    }

    [Fact]
    public void NullPath_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Pdf.Process((string)null!));
        Assert.Throws<ArgumentNullException>(() => Pdf.Classify((string)null!));
    }

    [Fact]
    public void NullRegions_ThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => Pdf.ExtractTextInRegions(TestEnvironment.Fixture(TextFixture), null!));
    }

    [Fact]
    public void EmptyBuffer_ThrowsNotAPdf()
    {
        PdfInspectorException error =
            Assert.Throws<PdfInspectorException>(() => Pdf.Process(Array.Empty<byte>()));

        Assert.Equal(PdfErrorKind.NotAPdf, error.Kind);
    }

    [Fact]
    public void InvalidSampleCount_ThrowsBeforeReachingTheNativeLibrary()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScanStrategy.Sample(0));
        Assert.Throws<ArgumentException>(() => ScanStrategy.Pages());
    }

    // -----------------------------------------------------------------
    // Concurrency and memory
    // -----------------------------------------------------------------

    [Fact]
    public void ConcurrentCalls_ProduceIdenticalResults()
    {
        byte[] pdf = TestEnvironment.FixtureBytes(TextFixture);
        string expected = Pdf.Process(pdf).Markdown!;

        string[] results = new string[8];
        Parallel.For(0, results.Length, i => results[i] = Pdf.Process(pdf).Markdown!);

        Assert.All(results, markdown => Assert.Equal(expected, markdown));
    }

    [Fact]
    public void RepeatedCalls_DoNotLeakOrCorruptResponses()
    {
        byte[] pdf = TestEnvironment.FixtureBytes(TextFixture);
        string expected = Pdf.ExtractText(pdf);

        for (int i = 0; i < 25; i++)
        {
            Assert.Equal(expected, Pdf.ExtractText(pdf));
        }
    }
}
