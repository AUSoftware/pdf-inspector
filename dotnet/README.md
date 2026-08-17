# AUSoftware.PdfInspector

Fast PDF text extraction to structured Markdown, with scanned-vs-text
detection, table recovery, and column analysis. This is the .NET binding over
the native [pdf-inspector](https://github.com/firecrawl/pdf-inspector) library
— the parsing runs in Rust, so there is no managed PDF parser to keep up to
date and no per-page rendering cost.

```csharp
using AUSoftware.PdfInspector;

PdfResult result = Pdf.Process("invoice.pdf");

Console.WriteLine(result.PdfType);          // TextBased
Console.WriteLine(result.PageCount);        // 3
Console.WriteLine(result.Markdown);         // "# Invoice\n\n| Item | ..."
```

## Install

> **Not on nuget.org yet.** `AUSoftware.PdfInspector` is the intended package id, but
> nothing has been published under it. Until someone publishes it, build the
> package yourself (below) or take the `.nupkg` the
> [Package NuGet workflow](../.github/workflows/package-nuget.yml) attaches to
> each run.

Build a package locally and install it from a folder feed:

```bash
cd dotnet && ./build.sh --pack   # writes dotnet/artifacts/AUSoftware.PdfInspector.<version>.nupkg
dotnet nuget add source /path/to/pdf-inspector/dotnet/artifacts --name pdf-inspector-local
dotnet add package AUSoftware.PdfInspector
```

Once the package is published, the last line alone is enough.

Either way the package carries the native library for each runtime it was
built for under `runtimes/{rid}/native`, so there is no separate native
install step on .NET 6+. A locally built package only contains the runtime
you built it on; see [Building from source](#building-from-source) for
covering several.

| Target | Support |
| --- | --- |
| .NET 8 and newer | `net8.0` assembly, native library resolved automatically |
| .NET 6 / 7, .NET Core 3.1 | `netstandard2.0` assembly, native library resolved automatically |
| .NET Framework 4.6.2+ | `netstandard2.0` assembly; the bundled MSBuild targets copy the Windows native library to your output directory |

## What you can do

Every method has a file-path overload and a `ReadOnlySpan<byte>` overload for
documents that never touch disk.

| Method | Use it for |
| --- | --- |
| `Pdf.Process` | The full pipeline: detect, extract, convert to Markdown |
| `Pdf.ProcessWithOcr` | The full pipeline with built-in OCR on the pages that need it |
| `Pdf.Detect` | Type and OCR metadata without extracting text |
| `Pdf.Classify` | The cheapest routing decision — type, page count, OCR pages |
| `Pdf.ExtractText` | Plain text, no structure |
| `Pdf.ExtractTextWithPositions` | Every run of text with geometry, font, and styling |
| `Pdf.ExtractStructureElements` | Structure-tree roles from tagged PDFs |
| `Pdf.ExtractPagesMarkdown` | Markdown page by page, for hybrid OCR pipelines |
| `Pdf.ExtractTextInRegions` | Text inside bounding boxes a layout model proposed |
| `Pdf.NativeVersion` | Which native library actually loaded |

### Built-in OCR

`ProcessWithOcr` runs the normal pipeline and falls back to OCR on the pages
whose native text is missing or unusable, returning one Markdown document plus
per-page provenance:

```csharp
OcrPdfResult result = Pdf.ProcessWithOcr(path);

Console.WriteLine(result.Markdown);
Console.WriteLine($"OCR ran on pages: {string.Join(", ", result.PagesRoutedToOcr)}");

foreach (OcrPageResult page in result.Pages)
{
    OcrPageProvenance p = page.Provenance;
    Console.WriteLine($"page {page.PageNumber}: {p.Source} (confidence {p.OcrConfidence})");

    if (p.HostedRecommended)
    {
        // Local OCR read this page poorly; a hosted parser would likely do better.
    }
}
```

The default `OcrMode.Auto` only renders the pages native extraction flagged, so
a clean text PDF never loads the OCR runtime at all. `OcrMode.Off` skips OCR
entirely, and `OcrMode.Force` OCRs every selected page even where native text
looks fine.

```csharp
OcrPdfResult result = Pdf.ProcessWithOcr(path, new OcrOptions
{
    Mode = OcrMode.Force,
    Pages = new[] { 1, 2 },      // 1-indexed
    Dpi = 300,
    MinimumConfidence = 0.5,
    ModelDirectory = "/opt/pdf-inspector/models",
    Offline = true,
});
```

**OCR needs a runtime this package does not ship.** The first routed page loads
PDFium and ONNX Runtime and resolves the pinned model set, downloading it once
into a shared cache unless `Offline` is set — in which case it needs
`ModelDirectory` or an already-warm cache and fails rather than reaching the
network. Point `PDFIUM_LIB_PATH` and `ORT_DYLIB_PATH` at the shared libraries
when they are not on the platform search path; see
[docs/ocr-runtime.md](../docs/ocr-runtime.md) for the validated builds. When
any of that is missing the call throws `PdfInspectorException` with
`Kind == PdfErrorKind.Ocr`.

If you would rather send pages to your own OCR service, keep using `Classify`
and `ExtractPagesMarkdown` as below — that path loads nothing extra.

### Routing scanned documents to OCR

`Classify` is the cheap first pass — roughly 10–50 ms, no text extraction:

```csharp
PdfClassification classification = Pdf.Classify(bytes);

if (classification.PdfType == PdfType.Scanned)
{
    // Whole document needs OCR.
}
else if (classification.PagesNeedingOcr.Count > 0)
{
    // Mixed: OCR only these pages (0-indexed here).
}
```

For a page-by-page split, `ExtractPagesMarkdown` gives you the Markdown it
could read and flags the pages it could not:

```csharp
PagesExtractionResult pages = Pdf.ExtractPagesMarkdown(bytes);

foreach (PageMarkdown page in pages.Pages)
{
    if (page.NeedsOcr)
    {
        Console.WriteLine($"page {page.Page}: OCR needed ({page.OcrReason})");
    }
    else
    {
        Console.WriteLine(page.Markdown);
    }
}
```

`OcrReason` values are the constants on `OcrReasons`: `scanned`,
`no_text`, `vector_text`, `suspected_garbled_text`. Treat an unrecognised
value as "needs OCR, cause unknown" — the list can grow.

Note that `HasEncodingIssues` on `PdfResult` matters even when the type is
`TextBased`: it means the fonts decoded badly and the Markdown may be
garbled, so the document should go to OCR anyway.

### Options

Everything is optional; anything left unset keeps the native default.

```csharp
PdfResult result = Pdf.Process("report.pdf", new PdfOptions
{
    Pages = new[] { 1, 2, 3 },          // 1-indexed for Process
    Password = "secret",
    Mode = ProcessMode.Analyze,          // skip the Markdown conversion
    Detection = new DetectionOptions
    {
        Strategy = ScanStrategy.Sample(16),
    },
    Markdown = new MarkdownOptions
    {
        Profile = MarkdownProfile.Compact,
        IncludePageNumbers = true,
        StripHeadersFooters = true,
    },
});
```

**Page numbering follows the underlying library and is not uniform.** Each
method's XML documentation states which it uses:

| Where | Indexing |
| --- | --- |
| `PdfOptions.Pages` for `Process`, `Detect`, `ExtractTextWithPositions`, `ExtractStructureElements` | 1-indexed |
| `PdfOptions.Pages` for `ExtractPagesMarkdown` | 0-indexed, and the results come back in the order you asked for |
| `PdfResult.PagesNeedingOcr`, `PagesWithTables`, `PagesWithColumns` | 1-indexed |
| `PdfClassification.PagesNeedingOcr` | 0-indexed |
| `OcrOptions.Pages` and every page list on `OcrPdfResult` | 1-indexed |
| `TextItem.Page`, `StructureElement.Page` | 1-indexed |
| `PageMarkdown.Page`, `PageRegions.Page` | 0-indexed |

### Tagged PDFs

`StructureElement` joins onto `TextItem` by `(Page, Mcid)`, which is how you
attach real heading levels and table roles to extracted text instead of
guessing from font sizes:

```csharp
var roles = Pdf.ExtractStructureElements(path)
    .ToDictionary(e => (e.Page, e.Mcid), e => e.Role);

foreach (TextItem item in Pdf.ExtractTextWithPositions(path))
{
    if (item.Mcid is long mcid && roles.TryGetValue((item.Page, mcid), out string? role))
    {
        Console.WriteLine($"{role}: {item.Text}");
    }
}
```

The list is empty when the PDF is not tagged — fall back to
`Pdf.Process`, which already applies font-size heuristics.

### Errors

Failures throw `PdfInspectorException` with a `Kind`:

```csharp
try
{
    PdfResult result = Pdf.Process(path);
}
catch (PdfInspectorException e) when (e.Kind == PdfErrorKind.Encrypted)
{
    PdfResult result = Pdf.Process(path, new PdfOptions { Password = password });
}
```

`Kind` covers `NotAPdf`, `Encrypted`, `Io`, `Parse`, `InvalidStructure`,
`InvalidArgument`, `InvalidOptions`, `Ocr`, `Panic`, and `Internal`. A kind
this binding does not recognise maps to `Unknown`, with the original string on
`NativeKind`.

`Ocr` only ever comes from `ProcessWithOcr`, and it usually means the local OCR
runtime or its models could not be loaded rather than anything wrong with the
PDF — see [Built-in OCR](#built-in-ocr).

### Threading

Every method blocks while the document is parsed and every method is
thread-safe — the native library keeps no shared mutable state. There are no
`async` overloads on purpose: the work is CPU-bound, so wrap a call in
`Task.Run` when you need to keep a UI or request thread free.

## Building from source

Requires the Rust toolchain and the .NET 8 SDK.

```bash
cd dotnet
./build.sh --test          # build native + managed, run the tests
./build.sh --pack          # ... and produce a .nupkg in dotnet/artifacts
```

`build.ps1` is the PowerShell equivalent. Both stage the native library into
`dotnet/runtimes/{rid}/native`, which is what `dotnet pack` picks up. To build
a package covering several platforms, run the script once per platform (or
per `--target`/`--rid` pair when cross-compiling) before packing — the staged
directory accumulates.

The tests load the native library straight from `dotnet/native/target`, so
`cargo build --release` in `dotnet/native` is enough to run them from an IDE.
Set `PDF_INSPECTOR_NATIVE_LIBRARY` to a specific file (or a directory
containing it) to override the lookup — useful for testing a packaged build.

## Layout

```
dotnet/
  native/              – Rust crate exposing the C ABI (pdf-inspector-ffi)
  src/AUSoftware.PdfInspector/       – the managed binding
  tests/               – xUnit suite driven by the repository's fixture PDFs
  build.sh, build.ps1  – native + managed build and packaging
```

The native crate exposes a small JSON-over-C-ABI surface: each entry point
takes UTF-8 arguments and returns a JSON response envelope that the managed
side deserialises with a source-generated `System.Text.Json` context, so the
binding stays trim- and AOT-friendly.

## License

MIT, same as the rest of pdf-inspector.
