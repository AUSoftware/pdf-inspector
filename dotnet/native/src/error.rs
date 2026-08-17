//! Error payloads carried across the C ABI.
//!
//! Every failure — bad arguments, PDF errors, malformed option JSON, panics —
//! is reported inside the response envelope as `{"ok":false,"error":{...}}`.
//! The `kind` is a stable machine-readable discriminant; `message` is a
//! human-readable detail that callers should treat as opaque.

use pdf_inspector::vision::OcrPipelineError;
use pdf_inspector::PdfError;
use serde::Serialize;

/// Stable error discriminants. These strings are part of the ABI contract —
/// the .NET binding maps them onto `PdfInspectorErrorKind`.
pub mod kind {
    /// A pointer argument was null, not valid UTF-8, or otherwise unusable.
    pub const INVALID_ARGUMENT: &str = "invalid_argument";
    /// The options/request JSON could not be parsed.
    pub const INVALID_OPTIONS: &str = "invalid_options";
    /// Filesystem error reading the PDF.
    pub const IO: &str = "io";
    /// The PDF could not be parsed.
    pub const PARSE: &str = "parse";
    /// The PDF is encrypted and could not be decrypted with the supplied
    /// password (or no password was supplied).
    pub const ENCRYPTED: &str = "encrypted";
    /// The PDF structure is invalid (broken xref, missing objects, …).
    pub const INVALID_STRUCTURE: &str = "invalid_structure";
    /// The input is not a PDF at all.
    pub const NOT_A_PDF: &str = "not_a_pdf";
    /// The OCR pipeline failed: PDFium or ONNX Runtime could not be loaded,
    /// a model could not be resolved, or recognition itself failed. The PDF
    /// is usually fine — the local OCR runtime is what is missing.
    pub const OCR: &str = "ocr";
    /// A panic was caught at the ABI boundary. Unwinding past this point
    /// would be undefined behaviour, so it is reported as an error instead.
    pub const PANIC: &str = "panic";
    /// The response could not be serialised to JSON.
    pub const INTERNAL: &str = "internal";
}

/// Error payload serialised into the response envelope.
#[derive(Debug, Clone, Serialize)]
pub struct FfiError {
    pub kind: &'static str,
    pub message: String,
}

impl FfiError {
    pub fn new(kind: &'static str, message: impl Into<String>) -> Self {
        Self {
            kind,
            message: message.into(),
        }
    }

    pub fn invalid_argument(message: impl Into<String>) -> Self {
        Self::new(kind::INVALID_ARGUMENT, message)
    }

    pub fn invalid_options(message: impl Into<String>) -> Self {
        Self::new(kind::INVALID_OPTIONS, message)
    }
}

impl From<PdfError> for FfiError {
    fn from(e: PdfError) -> Self {
        let kind = match e {
            PdfError::Io(_) => kind::IO,
            PdfError::Parse(_) => kind::PARSE,
            PdfError::Encrypted => kind::ENCRYPTED,
            PdfError::InvalidStructure => kind::INVALID_STRUCTURE,
            PdfError::NotAPdf(_) => kind::NOT_A_PDF,
        };
        Self::new(kind, e.to_string())
    }
}

impl From<OcrPipelineError> for FfiError {
    fn from(e: OcrPipelineError) -> Self {
        match e {
            // A document-level failure is the same failure it would be on the
            // non-OCR entry points, so it keeps the same discriminant.
            OcrPipelineError::Pdf(inner) => Self::from(inner),
            // Both of these reject a value the caller supplied, so they belong
            // with the other option-validation failures rather than with the
            // runtime ones.
            OcrPipelineError::InvalidSelectedPage { .. }
            | OcrPipelineError::InvalidMinimumConfidence { .. } => {
                Self::invalid_options(e.to_string())
            }
            // Rendering, model resolution, and recognition failures. The
            // variant list is `#[non_exhaustive]`, so anything added upstream
            // lands here rather than breaking the build.
            _ => Self::new(kind::OCR, e.to_string()),
        }
    }
}
