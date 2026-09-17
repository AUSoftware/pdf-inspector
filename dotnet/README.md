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
| .NET 10 and newer | `net10.0` assembly, native library resolved automatically |
| .NET 6–9, .NET Core 3.1 | `netstandard2.0` assembly, native library resolved automatically |
| .NET Framework 4.6.2+ | `netstandard2.0` assembly; the bundled MSBuild targets copy the Windows native library to your output directory |

## What you can do

Every method has a file-path overload and a `ReadOnlySpan<byte>` overload for
documents that never touch disk.

| Method | Use it for |
| --- | --- |
| `Pdf.Process` | The full pipeline: detect, extract, convert to Markdown |
| `Pdf.Detect` | Type and OCR metadata without extracting text |
| `Pdf.Classify` | The cheapest routing decision — type, page count, OCR pages |
| `Pdf.ExtractText` | Plain text, no structure |
| `Pdf.ExtractTextWithPositions` | Every run of text with geometry, font, and styling |
| `Pdf.ExtractStructureElements` | Structure-tree roles from tagged PDFs |
| `Pdf.ExtractPagesMarkdown` | Markdown page by page, for hybrid OCR pipelines |
| `Pdf.ExtractTextInRegions` | Text inside bounding boxes a layout model proposed |
| `Pdf.ExtractTablesInRegions` | Markdown tables inside those same bounding boxes |
| `Pdf.NativeVersion` | Which native library actually loaded |

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

`PdfOptions.Position` tunes the positioned-text calls, and the region calls
take the same `PositionOptions` as their last argument:

```csharp
PositionOptions position = new PositionOptions
{
    Frame = PositionFrame.Display,   // line boxes up with a rendered page
    BoldFromWeight = true,           // read bold from the weight class too
};

var items = Pdf.ExtractTextWithPositions(path, new PdfOptions { Position = position });
var regions = Pdf.ExtractTextInRegions(path, boxes, position);
```

`Password` and `Position` cannot be combined on the file overload of
`ExtractTextWithPositions` — the native library reaches the frame and weight
handling only through its in-memory path, which does not decrypt, so asking
for both throws `PdfInspectorException` with `PdfErrorKind.InvalidOptions`
rather than quietly dropping one. Decrypt the document and pass its bytes.

**Page numbering follows the underlying library and is not uniform.** Each
method's XML documentation states which it uses:

| Where | Indexing |
| --- | --- |
| `PdfOptions.Pages` for `Process`, `Detect`, `ExtractTextWithPositions`, `ExtractStructureElements` | 1-indexed |
| `PdfOptions.Pages` for `ExtractPagesMarkdown` | 0-indexed, and the results come back in the order you asked for |
| `PdfResult.PagesNeedingOcr`, `PagesWithTables`, `PagesWithColumns` | 1-indexed |
| `PdfClassification.PagesNeedingOcr` | 0-indexed |
| `TextItem.Page`, `StructureElement.Page` | 1-indexed |
| `PageMarkdown.Page`, `PageRegions.Page` | 0-indexed |

### Coordinate frames

Positioned items and region rectangles are read in the **sheet** frame by
default: the visible page box as laid out in the content stream, with the
page's `/Rotate` *not* applied. That is what every release before 1.20.0 did,
and it stays the default.

`PositionFrame.Display` reads the **rendered** page instead — the visible page
box turned clockwise by the page's inheritable `/Rotate`, with the turn of a
predominantly rotated page undone first. Use it when the boxes have to line up
with a rendered page image, which is the usual case when a layout model
proposes the rectangles:

```csharp
PageRegions boxes = new PageRegions(0, modelBoxes);   // from a rendered page
var regions = Pdf.ExtractTextInRegions(
    path,
    new[] { boxes },
    new PositionOptions { Frame = PositionFrame.Display });
```

On a page with no `/Rotate` the rendered page *is* the sheet, so both frames
agree.

### Tables inside regions

`ExtractTablesInRegions` takes the same request as `ExtractTextInRegions` and
returns the same shape, but runs table detection over each region instead of
reading it as flat text. Where structure was found, `Text` is a Markdown
pipe-table and `NeedsOcr` is `false`; where none was — too few items, poor
alignment, GID fonts — `Text` is empty and `NeedsOcr` is `true`, so you can
fall back to OCR for that one region:

```csharp
foreach (RegionText region in Pdf.ExtractTablesInRegions(path, boxes)[0].Regions)
{
    if (region.NeedsOcr)
    {
        SendToOcr(region);
        continue;
    }

    Console.WriteLine(region.Text);   // | a | b |\n| --- | --- |\n| 1 | 2 |
}
```

### Run metadata

`TextItem` carries more than geometry. `Rotation` is the baseline's angle in
degrees counter-clockwise from the page's x axis (`0` upright, `90` reading
bottom-to-top, `270` top-to-bottom, `180` upside-down); `X`/`Y`/`Width`/
`Height` stay the run's axis-aligned box, so a vertical run is tall and thin
rather than zero-width. `BaselineShift` is the signed offset of a
superscript (positive) or subscript (negative) run from the body baseline it
hangs off, and is `0` for normal text:

```csharp
foreach (TextItem item in Pdf.ExtractTextWithPositions(path))
{
    string text =
        item.BaselineShift > 0 ? $"<sup>{item.Text}</sup>" :
        item.BaselineShift < 0 ? $"<sub>{item.Text}</sub>" :
        item.Text;

    Console.WriteLine($"{text} at {item.Rotation}°");
}
```

`AdvanceKnown` is `false` when the font carries no width information — `Width`
is then an estimate of half an em per glyph rather than a measurement, so
treat it as unreliable for column or table geometry.

`FontWeight` is the face's weight class on the 100–900 scale (400 regular, 700
bold), read from the embedded font program's OS/2 table, else the
`FontDescriptor`'s `/FontWeight`, else a weight word in the font name. It is
`null` when nothing states one, which is common and not an error. It does not
by itself change `IsBold`: a medium face reports `500` with `IsBold` false.
Set `PositionOptions.BoldFromWeight` to have a weight class of 600 or more
read as bold as well — adjacent runs whose weight class differs then stay
separate items, so a heavier run inside a lighter paragraph keeps its own
item.

`LegacySymbolRewrite` marks a run whose text was changed by the legacy
private-use symbol cleanup. It is decoding *provenance*, not an OCR verdict:
a rewritten value is not an authoritative Unicode alias, and `false` does not
vouch for the accuracy of the rest.

**`Font` changed meaning in 1.16.0.** It is now the `/BaseFont` family name
("ABCDEF+CMMI10"), which identifies the actual face; the raw resource tag it
used to carry ("F2", "T22") moved to `FontTag`. Group by `Font` to find one
face across a document, by `(Page, FontTag)` to separate font *programs* that
share a family name.

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
`InvalidArgument`, `InvalidOptions`, `Panic`, and `Internal`. A kind this
binding does not recognise maps to `Unknown`, with the original string on
`NativeKind`.

### Threading

Every method blocks while the document is parsed and every method is
thread-safe — the native library keeps no shared mutable state. There are no
`async` overloads on purpose: the work is CPU-bound, so wrap a call in
`Task.Run` when you need to keep a UI or request thread free.

## Building from source

Requires the Rust toolchain and the .NET 10 SDK.

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
