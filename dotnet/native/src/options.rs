//! Deserialisable request/option payloads.
//!
//! Every exported function takes an optional JSON options string. A null
//! pointer or an empty string means "all defaults", so the .NET binding only
//! has to serialise the fields a caller actually set.
//!
//! Unknown fields are rejected (`deny_unknown_fields`) — a typo in an option
//! name surfaces as an `invalid_options` error rather than being silently
//! ignored.

use std::collections::HashSet;

use pdf_inspector::detector::{DetectionConfig, ScanStrategy};
use pdf_inspector::markdown::{MarkdownOptions, MarkdownProfile};
use pdf_inspector::{PdfOptions, PositionFrame, PositionOptions, ProcessMode};
use serde::Deserialize;

/// How far the pipeline should run.
#[derive(Debug, Clone, Copy, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum ModeDto {
    DetectOnly,
    Analyze,
    Full,
}

impl From<ModeDto> for ProcessMode {
    fn from(m: ModeDto) -> Self {
        match m {
            ModeDto::DetectOnly => ProcessMode::DetectOnly,
            ModeDto::Analyze => ProcessMode::Analyze,
            ModeDto::Full => ProcessMode::Full,
        }
    }
}

/// Which pages detection samples.
#[derive(Debug, Clone, Deserialize)]
#[serde(tag = "type", rename_all = "snake_case", deny_unknown_fields)]
pub enum ScanStrategyDto {
    EarlyExit,
    Full,
    Sample { count: u32 },
    Pages { pages: Vec<u32> },
}

impl From<ScanStrategyDto> for ScanStrategy {
    fn from(s: ScanStrategyDto) -> Self {
        match s {
            ScanStrategyDto::EarlyExit => ScanStrategy::EarlyExit,
            ScanStrategyDto::Full => ScanStrategy::Full,
            ScanStrategyDto::Sample { count } => ScanStrategy::Sample(count),
            ScanStrategyDto::Pages { pages } => ScanStrategy::Pages(pages),
        }
    }
}

/// Detection tuning. Omitted fields keep the crate defaults.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(default, rename_all = "snake_case", deny_unknown_fields)]
pub struct DetectionDto {
    pub strategy: Option<ScanStrategyDto>,
    pub min_text_ops_per_page: Option<u32>,
    pub text_page_ratio_threshold: Option<f32>,
}

impl DetectionDto {
    fn apply(self, mut config: DetectionConfig) -> DetectionConfig {
        if let Some(strategy) = self.strategy {
            config.strategy = strategy.into();
        }
        if let Some(min_ops) = self.min_text_ops_per_page {
            config.min_text_ops_per_page = min_ops;
        }
        if let Some(ratio) = self.text_page_ratio_threshold {
            config.text_page_ratio_threshold = ratio;
        }
        config
    }
}

/// Source-fidelity versus token-efficient post-processing.
#[derive(Debug, Clone, Copy, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum MarkdownProfileDto {
    Fidelity,
    Compact,
}

impl From<MarkdownProfileDto> for MarkdownProfile {
    fn from(p: MarkdownProfileDto) -> Self {
        match p {
            MarkdownProfileDto::Fidelity => MarkdownProfile::Fidelity,
            MarkdownProfileDto::Compact => MarkdownProfile::Compact,
        }
    }
}

/// Markdown conversion tuning. Omitted fields keep the crate defaults.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(default, rename_all = "snake_case", deny_unknown_fields)]
pub struct MarkdownDto {
    pub profile: Option<MarkdownProfileDto>,
    pub detect_headers: Option<bool>,
    pub detect_lists: Option<bool>,
    pub detect_code: Option<bool>,
    pub base_font_size: Option<f32>,
    pub remove_page_numbers: Option<bool>,
    pub format_urls: Option<bool>,
    pub fix_hyphenation: Option<bool>,
    pub detect_bold: Option<bool>,
    pub detect_italic: Option<bool>,
    pub detect_underline: Option<bool>,
    pub include_images: Option<bool>,
    pub include_links: Option<bool>,
    pub include_page_numbers: Option<bool>,
    pub strip_headers_footers: Option<bool>,
}

impl MarkdownDto {
    fn apply(self, mut options: MarkdownOptions) -> MarkdownOptions {
        macro_rules! set {
            ($field:ident) => {
                if let Some(value) = self.$field {
                    options.$field = value;
                }
            };
        }
        if let Some(profile) = self.profile {
            options.profile = profile.into();
        }
        set!(detect_headers);
        set!(detect_lists);
        set!(detect_code);
        set!(remove_page_numbers);
        set!(format_urls);
        set!(fix_hyphenation);
        set!(detect_bold);
        set!(detect_italic);
        set!(detect_underline);
        set!(include_images);
        set!(include_links);
        set!(include_page_numbers);
        set!(strip_headers_footers);
        // `base_font_size` is itself an Option in the crate: `null` means
        // "derive it from the document", so only a supplied value overrides.
        if let Some(size) = self.base_font_size {
            options.base_font_size = Some(size);
        }
        options
    }
}

/// Coordinate frame the positioned-text and region APIs work in.
#[derive(Debug, Clone, Copy, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum FrameDto {
    Sheet,
    Display,
}

impl From<FrameDto> for PositionFrame {
    fn from(f: FrameDto) -> Self {
        match f {
            FrameDto::Sheet => PositionFrame::Sheet,
            FrameDto::Display => PositionFrame::Display,
        }
    }
}

/// Options of the positioned-text and region entry points. Omitted fields
/// keep the crate defaults: the sheet frame, bold not read from the weight
/// class.
#[derive(Debug, Clone, Copy, Default, Deserialize)]
#[serde(default, rename_all = "snake_case", deny_unknown_fields)]
pub struct PositionOptionsDto {
    pub frame: Option<FrameDto>,
    pub bold_from_weight: Option<bool>,
}

impl PositionOptionsDto {
    /// Build core [`PositionOptions`], layering the supplied fields over
    /// defaults.
    pub fn to_position_options(self) -> PositionOptions {
        let mut options = PositionOptions::new();
        if let Some(frame) = self.frame {
            options = options.frame(frame.into());
        }
        if let Some(bold_from_weight) = self.bold_from_weight {
            options = options.bold_from_weight(bold_from_weight);
        }
        options
    }
}

/// The shared options payload.
///
/// Not every function honours every field — `pages` indexing in particular
/// differs per entry point (see the function docs in `lib.rs`). Fields that
/// do not apply to a given call are ignored.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(default, rename_all = "snake_case", deny_unknown_fields)]
pub struct OptionsDto {
    pub pages: Option<Vec<u32>>,
    pub password: Option<String>,
    pub mode: Option<ModeDto>,
    pub detection: Option<DetectionDto>,
    pub markdown: Option<MarkdownDto>,
    pub position: Option<PositionOptionsDto>,
}

impl OptionsDto {
    /// Build core [`PdfOptions`], layering the supplied fields over defaults.
    pub fn to_pdf_options(&self) -> PdfOptions {
        let mut options = PdfOptions::new();
        if let Some(mode) = self.mode {
            options.mode = mode.into();
        }
        if let Some(detection) = self.detection.clone() {
            options.detection = detection.apply(options.detection);
        }
        if let Some(markdown) = self.markdown.clone() {
            options.markdown = markdown.apply(options.markdown);
        }
        if let Some(pages) = &self.pages {
            options.page_filter = Some(pages.iter().copied().collect());
        }
        options.password = self.password.clone();
        options
    }

    /// The `pages` field as an ordered slice, preserving caller order.
    pub fn page_list(&self) -> Option<&[u32]> {
        self.pages.as_deref()
    }

    /// The positioned-text options, defaulted when the caller sent none.
    pub fn position_options(&self) -> PositionOptions {
        self.position.unwrap_or_default().to_position_options()
    }

    /// Whether the caller asked for anything but the default frame and
    /// weight handling. The entry points that cannot honour those options
    /// alongside a password use this to tell the two paths apart.
    pub fn has_position_options(&self) -> bool {
        self.position_options() != PositionOptions::default()
    }

    /// The `pages` field as a set, for the page-filtering extractors.
    pub fn page_set(&self) -> Option<HashSet<u32>> {
        self.pages
            .as_ref()
            .map(|pages| pages.iter().copied().collect())
    }
}

/// Bounding boxes requested per page, in the shape the core crate's region
/// APIs take them: a 0-indexed page number and its `[x1, y1, x2, y2]` rects.
pub type PageRegionPairs = Vec<(u32, Vec<[f32; 4]>)>;

/// Regions requested for one page.
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "snake_case", deny_unknown_fields)]
pub struct PageRegionsRequest {
    /// 0-indexed page number.
    pub page: u32,
    /// Bounding boxes as `[x1, y1, x2, y2]` in PDF points, top-left origin.
    pub regions: Vec<[f32; 4]>,
}

/// Payload for the region-extraction entry points.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(default, rename_all = "snake_case", deny_unknown_fields)]
pub struct RegionsRequest {
    pub page_regions: Vec<PageRegionsRequest>,
    pub position: Option<PositionOptionsDto>,
}

impl RegionsRequest {
    /// The requested regions, and the options they are to be read with.
    pub fn into_parts(self) -> (PageRegionPairs, PositionOptions) {
        let options = self.position.unwrap_or_default().to_position_options();
        let pairs = self
            .page_regions
            .into_iter()
            .map(|p| (p.page, p.regions))
            .collect();
        (pairs, options)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn empty_options_produce_crate_defaults() {
        let dto: OptionsDto = serde_json::from_str("{}").unwrap();
        let options = dto.to_pdf_options();
        assert_eq!(options.mode, ProcessMode::Full);
        assert!(options.page_filter.is_none());
        assert!(options.password.is_none());
        assert!(options.markdown.detect_headers);
    }

    #[test]
    fn markdown_overrides_only_supplied_fields() {
        let dto: OptionsDto =
            serde_json::from_str(r#"{"markdown":{"detect_headers":false,"profile":"compact"}}"#)
                .unwrap();
        let options = dto.to_pdf_options();
        assert!(!options.markdown.detect_headers);
        assert!(matches!(options.markdown.profile, MarkdownProfile::Compact));
        // Untouched fields keep their defaults.
        assert!(options.markdown.detect_lists);
    }

    #[test]
    fn detection_strategy_variants_round_trip() {
        let dto: OptionsDto =
            serde_json::from_str(r#"{"detection":{"strategy":{"type":"sample","count":3}}}"#)
                .unwrap();
        assert!(matches!(
            dto.to_pdf_options().detection.strategy,
            ScanStrategy::Sample(3)
        ));

        let dto: OptionsDto =
            serde_json::from_str(r#"{"detection":{"strategy":{"type":"pages","pages":[1,4]}}}"#)
                .unwrap();
        match dto.to_pdf_options().detection.strategy {
            ScanStrategy::Pages(pages) => assert_eq!(pages, vec![1, 4]),
            other => panic!("expected Pages, got {other:?}"),
        }

        let dto: OptionsDto =
            serde_json::from_str(r#"{"detection":{"strategy":{"type":"early_exit"}}}"#).unwrap();
        assert!(matches!(
            dto.to_pdf_options().detection.strategy,
            ScanStrategy::EarlyExit
        ));
    }

    #[test]
    fn page_list_preserves_caller_order() {
        let dto: OptionsDto = serde_json::from_str(r#"{"pages":[5,1,3]}"#).unwrap();
        assert_eq!(dto.page_list(), Some(&[5, 1, 3][..]));
        assert_eq!(dto.page_set().unwrap().len(), 3);
    }

    #[test]
    fn unknown_option_fields_are_rejected() {
        let err = serde_json::from_str::<OptionsDto>(r#"{"detect_headers":true}"#).unwrap_err();
        assert!(err.to_string().contains("unknown field"));
    }

    #[test]
    fn region_request_parses_into_parts() {
        let request: RegionsRequest =
            serde_json::from_str(r#"{"page_regions":[{"page":0,"regions":[[1.0,2.0,3.0,4.0]]}]}"#)
                .unwrap();
        let (pairs, options) = request.into_parts();
        assert_eq!(pairs, vec![(0, vec![[1.0, 2.0, 3.0, 4.0]])]);
        // An absent `position` means the crate defaults.
        assert_eq!(options, PositionOptions::default());
    }

    #[test]
    fn region_request_carries_position_options() {
        let request: RegionsRequest = serde_json::from_str(
            r#"{"page_regions":[],"position":{"frame":"display","bold_from_weight":true}}"#,
        )
        .unwrap();
        let (pairs, options) = request.into_parts();
        assert!(pairs.is_empty());
        assert_eq!(options.frame, PositionFrame::Display);
        assert!(options.bold_from_weight);
    }

    #[test]
    fn position_options_default_to_the_sheet_frame() {
        let dto: OptionsDto = serde_json::from_str("{}").unwrap();
        assert_eq!(dto.position_options(), PositionOptions::default());
        assert_eq!(dto.position_options().frame, PositionFrame::Sheet);
        assert!(!dto.has_position_options());
    }

    #[test]
    fn position_options_override_only_supplied_fields() {
        let dto: OptionsDto =
            serde_json::from_str(r#"{"position":{"bold_from_weight":true}}"#).unwrap();
        let options = dto.position_options();
        assert!(options.bold_from_weight);
        // The frame is untouched.
        assert_eq!(options.frame, PositionFrame::Sheet);
        assert!(dto.has_position_options());

        let dto: OptionsDto = serde_json::from_str(r#"{"position":{"frame":"display"}}"#).unwrap();
        let options = dto.position_options();
        assert_eq!(options.frame, PositionFrame::Display);
        assert!(!options.bold_from_weight);
        assert!(dto.has_position_options());
    }

    #[test]
    fn explicitly_default_position_options_are_not_treated_as_overrides() {
        // The file entry point refuses `password` alongside real position
        // options, so spelling out the defaults must not trip that check.
        let dto: OptionsDto =
            serde_json::from_str(r#"{"position":{"frame":"sheet","bold_from_weight":false}}"#)
                .unwrap();
        assert!(!dto.has_position_options());
    }

    #[test]
    fn unknown_position_fields_are_rejected() {
        let err = serde_json::from_str::<OptionsDto>(r#"{"position":{"bold":true}}"#).unwrap_err();
        assert!(err.to_string().contains("unknown field"));

        let err =
            serde_json::from_str::<OptionsDto>(r#"{"position":{"frame":"page"}}"#).unwrap_err();
        assert!(err.to_string().contains("unknown variant"));
    }
}
