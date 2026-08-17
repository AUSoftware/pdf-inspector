using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PdfInspector.Tests;

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
