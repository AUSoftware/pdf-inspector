//! Serialisable mirrors of the `pdf-inspector` result types.
//!
//! The core crate deliberately has no serde dependency, so every value that
//! crosses the ABI is projected onto a DTO here. Field names are snake_case
//! and match the Python/Node bindings so the JSON shape is familiar; the .NET
//! binding maps them onto PascalCase properties.

use pdf_inspector::detector::PdfType;
use pdf_inspector::types::ItemType;
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
