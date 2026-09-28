//! Serialisable mirrors of the `pdf-inspector` result types.
//!
//! The core crate deliberately has no serde dependency, so every value that
//! crosses the ABI is projected onto a DTO here. Field names are snake_case
//! and match the Python/Node bindings so the JSON shape is familiar; the .NET
//! binding maps them onto PascalCase properties.

use pdf_inspector::detector::PdfType;
use pdf_inspector::types::ItemType;
use pdf_inspector::{
    FontCMapGaps, PageMarkdown, PageOcrReasons, PageRegionResult, PagesExtractionResult,
    PdfClassification, PdfProcessResult, RegionText, StructureElement, TextItem,
};
use serde::Serialize;

/// Stable wire name for a detected PDF type.
pub fn pdf_type_name(t: PdfType) -> &'static str {
    match t {
        PdfType::TextBased => "text_based",
        PdfType::Scanned => "scanned",
        PdfType::ImageBased => "image_based",
        PdfType::Mixed => "mixed",
    }
}

/// OCR reasons for a single 1-indexed page.
#[derive(Debug, Serialize)]
pub struct PageOcrReasonsDto {
    pub page: u32,
    pub reasons: Vec<String>,
}

impl From<&PageOcrReasons> for PageOcrReasonsDto {
    fn from(r: &PageOcrReasons) -> Self {
        Self {
            page: r.page,
            reasons: r.reasons.clone(),
        }
    }
}

fn ocr_reasons(reasons: &[PageOcrReasons]) -> Vec<PageOcrReasonsDto> {
    reasons.iter().map(PageOcrReasonsDto::from).collect()
}

/// A font whose ToUnicode CMap — or, for a font without one, the embedded
/// program's cmap table — had no entry for some of the codes the document
/// shows through it. `codes - interpolated - unmapped` of its codes had one.
#[derive(Debug, Serialize)]
pub struct FontCMapGapsDto {
    /// The font's `/BaseFont` name, or its resource name without one.
    pub font: String,
    /// Codes shown through the font's CMap, repeats included.
    pub codes: u32,
    /// Codes without an entry that were read from the mapped codes around
    /// them.
    pub interpolated: u32,
    /// Codes without an entry that could not be read; each is a U+FFFD in
    /// the text.
    pub unmapped: u32,
}

impl From<FontCMapGaps> for FontCMapGapsDto {
    fn from(g: FontCMapGaps) -> Self {
        Self {
            font: g.font,
            codes: g.codes,
            interpolated: g.interpolated,
            unmapped: g.unmapped,
        }
    }
}

/// Full processing result (detection + markdown + layout metadata).
#[derive(Debug, Serialize)]
pub struct PdfResultDto {
    pub pdf_type: &'static str,
    pub markdown: Option<String>,
    pub page_count: u32,
    pub processing_time_ms: u64,
    /// 1-indexed page numbers that need OCR.
    pub pages_needing_ocr: Vec<u32>,
    pub ocr_reasons_by_page: Vec<PageOcrReasonsDto>,
    /// The document information dictionary's `/Title`, decoded as a PDF text
    /// string; `null` when missing or not a string. The entries below follow
    /// the same rule, the two dates as written (`D:20240115103000+01'00'`).
    pub title: Option<String>,
    pub author: Option<String>,
    pub subject: Option<String>,
    pub keywords: Option<String>,
    pub creator: Option<String>,
    pub producer: Option<String>,
    pub creation_date: Option<String>,
    pub mod_date: Option<String>,
    pub confidence: f32,
    pub is_complex_layout: bool,
    /// 1-indexed pages where tables were detected.
    pub pages_with_tables: Vec<u32>,
    /// 1-indexed pages where multi-column layout was detected.
    pub pages_with_columns: Vec<u32>,
    pub has_encoding_issues: bool,
    /// Fonts whose CMap lacked an entry for a code the document shows
    /// through it. Always empty in detect-only mode, which decodes no text.
    pub cmap_gaps: Vec<FontCMapGapsDto>,
}

impl From<PdfProcessResult> for PdfResultDto {
    fn from(r: PdfProcessResult) -> Self {
        Self {
            pdf_type: pdf_type_name(r.pdf_type),
            markdown: r.markdown,
            page_count: r.page_count,
            processing_time_ms: r.processing_time_ms,
            pages_needing_ocr: r.pages_needing_ocr,
            ocr_reasons_by_page: ocr_reasons(&r.ocr_reasons_by_page),
            title: r.title,
            author: r.author,
            subject: r.subject,
            keywords: r.keywords,
            creator: r.creator,
            producer: r.producer,
            creation_date: r.creation_date,
            mod_date: r.mod_date,
            confidence: r.confidence,
            is_complex_layout: r.layout.is_complex,
            pages_with_tables: r.layout.pages_with_tables,
            pages_with_columns: r.layout.pages_with_columns,
            has_encoding_issues: r.has_encoding_issues,
            cmap_gaps: r.cmap_gaps.into_iter().map(FontCMapGapsDto::from).collect(),
        }
    }
}

/// Lightweight classification result.
#[derive(Debug, Serialize)]
pub struct ClassificationDto {
    pub pdf_type: &'static str,
    pub page_count: u32,
    /// 0-indexed page numbers that need OCR (matches the Python binding).
    pub pages_needing_ocr: Vec<u32>,
    pub confidence: f32,
}

impl From<PdfClassification> for ClassificationDto {
    fn from(c: PdfClassification) -> Self {
        Self {
            pdf_type: pdf_type_name(c.pdf_type),
            page_count: c.page_count,
            pages_needing_ocr: c.pages_needing_ocr,
            confidence: c.confidence,
        }
    }
}

/// A positioned text item.
#[derive(Debug, Serialize)]
pub struct TextItemDto {
    pub text: String,
    pub x: f32,
    pub y: f32,
    pub width: f32,
    pub height: f32,
    /// Rotation of the run's baseline in degrees counter-clockwise from the
    /// page's x axis, in `[0, 360)`: `0` for ordinary horizontal text, `90`
    /// for text reading bottom-to-top (a rotated margin stamp), `270` for
    /// top-to-bottom, `180` for upside-down. `x`/`y`/`width`/`height` is the
    /// run's axis-aligned box, so a vertical run is tall and thin
    /// (height > width) rather than zero-width.
    pub rotation: f32,
    /// Whether the run's advance came from font metrics. `false` when the
    /// font carries no width information (or an ActualText span's advance
    /// could not be recovered): the box's extent along the baseline is then
    /// an estimate of half an em per painted glyph, not a measurement.
    pub advance_known: bool,
    /// `/BaseFont` family name ("ABCDEF+CMMI10"), identifying the face.
    pub font: String,
    /// The raw font resource tag ("F2", "T22") the show operator selected —
    /// what `font` carried before 1.16.0. Scoped to the enclosing page or
    /// Form XObject's `/Resources`, so the same tag on another page may name
    /// a different face; within one page it separates font *programs* that
    /// share a family name. Empty for items with no show operator (images,
    /// links, form fields).
    pub font_tag: String,
    pub font_size: f32,
    /// The font's weight class on the 100..=900 scale (400 regular, 700
    /// bold), read from the embedded font program's OS/2 table, else the
    /// FontDescriptor's `/FontWeight`, else a weight word in the font name.
    /// `null` when none of them says, and for items that do not come from a
    /// font (images, links, form fields). Independent of `is_bold`, which is
    /// unchanged unless `bold_from_weight` is set: a medium face reports
    /// `500` with `is_bold: false`.
    pub font_weight: Option<u16>,
    /// At least one source character was changed by the legacy private-use
    /// symbol cleanup. Decoding provenance, not an OCR verdict: the
    /// rewritten value must not be taken as an authoritative Unicode alias,
    /// and a `false` here does not vouch for the rest.
    pub legacy_symbol_rewrite: bool,
    /// 1-indexed page number.
    pub page: u32,
    pub is_bold: bool,
    /// Where `is_bold` came from — `"font_name"`, `"font_flags"`,
    /// `"weight_class"` (only with `bold_from_weight`) or `"painted"`, the
    /// first of them in that order when more than one says bold. `null` when
    /// `is_bold` is false, and for image, link and form-field items.
    pub bold_source: Option<&'static str>,
    /// Whether the font is fixed-pitch: `true` when the FontDescriptor's
    /// FixedPitch flag or the embedded program's `post` table says so, else
    /// measured from the width table (`true` when a dozen or more glyphs
    /// share one advance, `false` when two differ). `null` when neither
    /// holds, and for image, link and form-field items.
    pub fixed_pitch: Option<bool>,
    /// The fill colour the run was shown with, as 8-bit sRGB
    /// `[red, green, blue]`. `null` for colour spaces that are not read
    /// (Separation, DeviceN, Pattern, CalRGB, Lab, …) and for image, link
    /// and form-field items.
    pub fill_color: Option<[u8; 3]>,
    /// The stroke colour the run was shown with, read like `fill_color`.
    pub stroke_color: Option<[u8; 3]>,
    /// The text render mode (`Tr`), 0..=7: 0 fill, 1 stroke, 2 fill and
    /// stroke, 3 invisible, 4..=6 as 0..=2 plus clip, 7 clip only. `null`
    /// for image, link and form-field items.
    pub render_mode: Option<u8>,
    pub is_italic: bool,
    pub is_underline: bool,
    pub is_strikeout: bool,
    /// Signed baseline offset, in points, of a super/subscript glyph run from
    /// the body baseline it is attached to; `0` for normal text. Positive =
    /// raised (superscript: footnote markers, exponents), negative = lowered
    /// (subscript). Digit-only markers beside a word are already fused into
    /// it as Unicode super/subscript characters ("word2") and carry `0`.
    pub baseline_shift: f32,
    pub item_type: &'static str,
    /// Present only for `item_type == "link"`.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub url: Option<String>,
    /// Marked Content ID, `null` when the item is not inside marked content.
    pub mcid: Option<i64>,
}

impl From<TextItem> for TextItemDto {
    fn from(item: TextItem) -> Self {
        let (item_type, url) = match item.item_type {
            ItemType::Text => ("text", None),
            ItemType::Image => ("image", None),
            ItemType::Link(url) => ("link", Some(url)),
            ItemType::FormField => ("form_field", None),
        };
        Self {
            text: item.text,
            x: item.x,
            y: item.y,
            width: item.width,
            height: item.height,
            rotation: item.rotation,
            advance_known: item.advance_known,
            font: item.font,
            font_tag: item.font_tag,
            font_size: item.font_size,
            font_weight: item.font_weight,
            legacy_symbol_rewrite: item.legacy_symbol_rewrite,
            page: item.page,
            is_bold: item.is_bold,
            bold_source: item.bold_source.map(|source| source.as_str()),
            fixed_pitch: item.fixed_pitch,
            fill_color: item.fill_color,
            stroke_color: item.stroke_color,
            render_mode: item.render_mode,
            is_italic: item.is_italic,
            is_underline: item.is_underline,
            is_strikeout: item.is_strikeout,
            baseline_shift: item.baseline_shift,
            item_type,
            url,
            mcid: item.mcid,
        }
    }
}

/// A structure-tree element reference from a tagged PDF.
#[derive(Debug, Serialize)]
pub struct StructureElementDto {
    /// 1-indexed page number (matches `TextItemDto::page`).
    pub page: u32,
    pub mcid: i64,
    pub role: String,
}

impl From<StructureElement> for StructureElementDto {
    fn from(e: StructureElement) -> Self {
        Self {
            page: e.page,
            mcid: e.mcid,
            role: e.role,
        }
    }
}

/// Markdown for a single page.
#[derive(Debug, Serialize)]
pub struct PageMarkdownDto {
    /// 0-indexed page number.
    pub page: u32,
    pub markdown: String,
    pub needs_ocr: bool,
    pub ocr_reason: Option<String>,
}

impl From<PageMarkdown> for PageMarkdownDto {
    fn from(p: PageMarkdown) -> Self {
        Self {
            page: p.page,
            markdown: p.markdown,
            needs_ocr: p.needs_ocr,
            ocr_reason: p.ocr_reason,
        }
    }
}

/// Per-page markdown plus document-wide layout classification.
#[derive(Debug, Serialize)]
pub struct PagesExtractionDto {
    pub pages: Vec<PageMarkdownDto>,
    /// 1-indexed pages where tables were detected.
    pub pages_with_tables: Vec<u32>,
    /// 1-indexed pages where multi-column layout was detected.
    pub pages_with_columns: Vec<u32>,
    /// 1-indexed pages that need OCR.
    pub pages_needing_ocr: Vec<u32>,
    pub ocr_reasons_by_page: Vec<PageOcrReasonsDto>,
    pub is_complex: bool,
}

impl From<PagesExtractionResult> for PagesExtractionDto {
    fn from(r: PagesExtractionResult) -> Self {
        Self {
            pages: r.pages.into_iter().map(PageMarkdownDto::from).collect(),
            pages_with_tables: r.pages_with_tables,
            pages_with_columns: r.pages_with_columns,
            pages_needing_ocr: r.pages_needing_ocr,
            ocr_reasons_by_page: ocr_reasons(&r.ocr_reasons_by_page),
            is_complex: r.is_complex,
        }
    }
}

/// Text extracted from one region.
#[derive(Debug, Serialize)]
pub struct RegionTextDto {
    pub text: String,
    pub needs_ocr: bool,
    pub ocr_reason: Option<String>,
}

impl From<RegionText> for RegionTextDto {
    fn from(r: RegionText) -> Self {
        Self {
            text: r.text,
            needs_ocr: r.needs_ocr,
            ocr_reason: r.ocr_reason,
        }
    }
}

/// Region results for one page, parallel to the requested regions.
#[derive(Debug, Serialize)]
pub struct PageRegionsDto {
    /// 0-indexed page number.
    pub page: u32,
    pub regions: Vec<RegionTextDto>,
}

impl From<PageRegionResult> for PageRegionsDto {
    fn from(r: PageRegionResult) -> Self {
        Self {
            page: r.page,
            regions: r.regions.into_iter().map(RegionTextDto::from).collect(),
        }
    }
}
