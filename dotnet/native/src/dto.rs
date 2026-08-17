//! Serialisable mirrors of the `pdf-inspector` result types.
//!
//! The core crate deliberately has no serde dependency, so every value that
//! crosses the ABI is projected onto a DTO here. Field names are snake_case
//! and match the Python/Node bindings so the JSON shape is familiar; the .NET
//! binding maps them onto PascalCase properties.

use pdf_inspector::detector::PdfType;
use pdf_inspector::types::ItemType;
use pdf_inspector::vision::{
    FusedPageMarkdown, OcrPdfResult, PageContentSource, PageProvenance, VisionTimings,
};
use pdf_inspector::{
    PageMarkdown, PageOcrReasons, PageRegionResult, PagesExtractionResult, PdfClassification,
    PdfProcessResult, RegionText, StructureElement, TextItem,
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
    pub title: Option<String>,
    pub confidence: f32,
    pub is_complex_layout: bool,
    /// 1-indexed pages where tables were detected.
    pub pages_with_tables: Vec<u32>,
    /// 1-indexed pages where multi-column layout was detected.
    pub pages_with_columns: Vec<u32>,
    pub has_encoding_issues: bool,
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
            confidence: r.confidence,
            is_complex_layout: r.layout.is_complex,
            pages_with_tables: r.layout.pages_with_tables,
            pages_with_columns: r.layout.pages_with_columns,
            has_encoding_issues: r.has_encoding_issues,
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
    pub font: String,
    pub font_size: f32,
    /// 1-indexed page number.
    pub page: u32,
    pub is_bold: bool,
    pub is_italic: bool,
    pub is_underline: bool,
    pub is_strikeout: bool,
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
            font: item.font,
            font_size: item.font_size,
            page: item.page,
            is_bold: item.is_bold,
            is_italic: item.is_italic,
            is_underline: item.is_underline,
            is_strikeout: item.is_strikeout,
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

/// Stable wire name for the source of a page's final Markdown.
///
/// `PageContentSource` is `#[non_exhaustive]`, so a variant added upstream
/// reports as `"unknown"` rather than being silently relabelled as one of the
/// sources the binding does know.
pub fn page_content_source_name(source: PageContentSource) -> &'static str {
    match source {
        PageContentSource::Native => "native",
        PageContentSource::Ocr => "ocr",
        PageContentSource::Fused => "fused",
        _ => "unknown",
    }
}

/// Exact OCR model identity retained in page provenance.
#[derive(Debug, Serialize)]
pub struct OcrModelIdentityDto {
    pub name: String,
    pub revision: String,
}

/// Per-page OCR stage timings.
#[derive(Debug, Serialize)]
pub struct OcrTimingsDto {
    pub render_ms: u64,
    pub ocr_ms: u64,
    pub assembly_ms: u64,
}

impl From<VisionTimings> for OcrTimingsDto {
    fn from(t: VisionTimings) -> Self {
        Self {
            render_ms: t.render_ms,
            ocr_ms: t.ocr_ms,
            assembly_ms: t.assembly_ms,
        }
    }
}

/// Where one page's Markdown came from, and how much it cost.
#[derive(Debug, Serialize)]
pub struct OcrProvenanceDto {
    /// 1-indexed page number.
    pub page_number: u32,
    pub source: &'static str,
    pub ocr_model: Option<OcrModelIdentityDto>,
    pub render_dpi: Option<f32>,
    pub ocr_confidence: Option<f32>,
    pub timings: OcrTimingsDto,
    pub warnings: Vec<String>,
    pub hosted_recommended: bool,
}

impl From<PageProvenance> for OcrProvenanceDto {
    fn from(p: PageProvenance) -> Self {
        Self {
            page_number: p.page_number,
            source: page_content_source_name(p.source),
            ocr_model: p.ocr_model.map(|model| OcrModelIdentityDto {
                name: model.name,
                revision: model.revision,
            }),
            render_dpi: p.render_dpi,
            ocr_confidence: p.ocr_confidence,
            timings: p.timings.into(),
            warnings: p.warnings,
            hosted_recommended: p.hosted_recommended,
        }
    }
}

/// Final Markdown and provenance for one page.
#[derive(Debug, Serialize)]
pub struct OcrPageDto {
    /// 1-indexed page number.
    pub page_number: u32,
    pub markdown: String,
    pub provenance: OcrProvenanceDto,
}

impl From<FusedPageMarkdown> for OcrPageDto {
    fn from(p: FusedPageMarkdown) -> Self {
        Self {
            page_number: p.page_number,
            markdown: p.markdown,
            provenance: p.provenance.into(),
        }
    }
}

/// Complete native + OCR result. All page lists are **1-indexed**.
#[derive(Debug, Serialize)]
pub struct OcrPdfResultDto {
    pub markdown: String,
    pub pages: Vec<OcrPageDto>,
    /// Total pages in the document, independent of any page selection.
    pub page_count: u32,
    pub pages_recommended_for_ocr: Vec<u32>,
    pub pages_routed_to_ocr: Vec<u32>,
    pub pages_recommending_hosted: Vec<u32>,
    pub ocr_reasons_by_page: Vec<PageOcrReasonsDto>,
    pub pages_with_tables: Vec<u32>,
    pub pages_with_columns: Vec<u32>,
    pub is_complex: bool,
    pub processing_time_ms: u64,
    pub render_time_ms: u64,
    pub ocr_time_ms: u64,
}

impl From<OcrPdfResult> for OcrPdfResultDto {
    fn from(r: OcrPdfResult) -> Self {
        Self {
            markdown: r.markdown,
            pages: r.pages.into_iter().map(OcrPageDto::from).collect(),
            page_count: r.page_count,
            pages_recommended_for_ocr: r.pages_recommended_for_ocr,
            pages_routed_to_ocr: r.pages_routed_to_ocr,
            pages_recommending_hosted: r.pages_recommending_hosted,
            ocr_reasons_by_page: ocr_reasons(&r.ocr_reasons_by_page),
            pages_with_tables: r.pages_with_tables,
            pages_with_columns: r.pages_with_columns,
            is_complex: r.is_complex,
            processing_time_ms: r.processing_time_ms,
            render_time_ms: r.render_time_ms,
            ocr_time_ms: r.ocr_time_ms,
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
