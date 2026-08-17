//! C ABI shared library for `pdf-inspector`, consumed by the .NET binding.
//!
//! # Calling convention
//!
//! Every entry point returns a heap-allocated, NUL-terminated UTF-8 JSON
//! string that the caller **must** release with [`pdfi_free_string`]. The
//! payload is always a response envelope:
//!
//! ```json
//! {"ok": true,  "data":  { ... }}
//! {"ok": false, "error": {"kind": "not_a_pdf", "message": "..."}}
//! ```
//!
//! A null return means the response itself could not be allocated (out of
//! memory); callers should treat it as a fatal error.
//!
//! Panics are caught at the boundary and reported as `{"kind": "panic"}` —
//! unwinding into managed code would be undefined behaviour.
//!
//! Every function is re-entrant and holds no global mutable state, so calls
//! may be made concurrently from any number of threads.

mod dto;
mod error;
mod options;

use std::ffi::{c_char, CStr, CString};
use std::panic::{self, AssertUnwindSafe};
use std::ptr;
use std::slice;

use serde::Serialize;

use dto::{
    ClassificationDto, PageRegionsDto, PagesExtractionDto, PdfResultDto, StructureElementDto,
    TextItemDto,
};
use error::{kind, FfiError};
use options::{OptionsDto, RegionsRequest};

/// Version of this shared library, kept in lockstep with the core crate.
const VERSION: &str = concat!(env!("CARGO_PKG_VERSION"), "\0");

// ---------------------------------------------------------------------------
// Response plumbing
// ---------------------------------------------------------------------------

#[derive(Serialize)]
struct Envelope<T> {
    ok: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    data: Option<T>,
    #[serde(skip_serializing_if = "Option::is_none")]
    error: Option<FfiError>,
}

/// JSON for an error envelope, built without serde so it can never itself
/// fail. Used as the last-resort fallback when serialisation breaks down.
fn raw_error_json(kind: &str, message: &str) -> String {
    fn escape(s: &str) -> String {
        let mut out = String::with_capacity(s.len() + 8);
        for c in s.chars() {
            match c {
                '"' => out.push_str("\\\""),
                '\\' => out.push_str("\\\\"),
                '\n' => out.push_str("\\n"),
                '\r' => out.push_str("\\r"),
                '\t' => out.push_str("\\t"),
                c if (c as u32) < 0x20 => out.push_str(&format!("\\u{:04x}", c as u32)),
                c => out.push(c),
            }
        }
        out
    }
    format!(
        r#"{{"ok":false,"error":{{"kind":"{}","message":"{}"}}}}"#,
        escape(kind),
        escape(message)
    )
}

/// Move an owned `String` onto the C heap as a NUL-terminated buffer.
///
/// Returns null only if the allocation fails. Interior NUL bytes cannot occur
/// here: serde_json escapes U+0000 as a six-character `\u0000` sequence, and
/// the fallback JSON above does the same.
fn into_c_string(s: String) -> *mut c_char {
    match CString::new(s) {
        Ok(c) => c.into_raw(),
        Err(_) => match CString::new(raw_error_json(
            kind::INTERNAL,
            "response contained a NUL byte",
        )) {
            Ok(c) => c.into_raw(),
            Err(_) => ptr::null_mut(),
        },
    }
}

/// Run `f`, catching panics, and serialise the outcome into a response
/// envelope on the C heap.
fn respond<T, F>(f: F) -> *mut c_char
where
    T: Serialize,
    F: FnOnce() -> Result<T, FfiError>,
{
    // AssertUnwindSafe: every closure below owns its inputs and touches no
    // shared state, so a panic cannot leave anything observably broken.
    let outcome = panic::catch_unwind(AssertUnwindSafe(f));
    let envelope = match outcome {
        Ok(Ok(data)) => Envelope {
            ok: true,
            data: Some(data),
            error: None,
        },
        Ok(Err(e)) => Envelope {
            ok: false,
            data: None,
            error: Some(e),
        },
        Err(payload) => Envelope {
            ok: false,
            data: None,
            error: Some(FfiError::new(kind::PANIC, panic_message(&payload))),
        },
    };
    match serde_json::to_string(&envelope) {
        Ok(json) => into_c_string(json),
        Err(e) => into_c_string(raw_error_json(kind::INTERNAL, &e.to_string())),
    }
}

fn panic_message(payload: &Box<dyn std::any::Any + Send>) -> String {
    if let Some(s) = payload.downcast_ref::<&str>() {
        format!("panic in pdf-inspector: {s}")
    } else if let Some(s) = payload.downcast_ref::<String>() {
        format!("panic in pdf-inspector: {s}")
    } else {
        "panic in pdf-inspector".to_string()
    }
}

// ---------------------------------------------------------------------------
// Argument decoding
// ---------------------------------------------------------------------------

/// Borrow a required C string argument as `&str`.
///
/// # Safety
/// `ptr` must be null or point to a NUL-terminated buffer that stays valid
/// for the duration of the call.
unsafe fn required_str<'a>(ptr: *const c_char, name: &str) -> Result<&'a str, FfiError> {
    if ptr.is_null() {
        return Err(FfiError::invalid_argument(format!("`{name}` is null")));
    }
    CStr::from_ptr(ptr)
        .to_str()
        .map_err(|_| FfiError::invalid_argument(format!("`{name}` is not valid UTF-8")))
}

/// Borrow a PDF byte buffer argument.
///
/// # Safety
/// `data` must be null or point to at least `len` readable bytes that stay
/// valid for the duration of the call.
unsafe fn required_bytes<'a>(data: *const u8, len: usize) -> Result<&'a [u8], FfiError> {
    if data.is_null() {
        if len == 0 {
            return Ok(&[]);
        }
        return Err(FfiError::invalid_argument(
            "`data` is null but `len` is > 0",
        ));
    }
    Ok(slice::from_raw_parts(data, len))
}

/// Parse an optional JSON payload. Null or empty means "all defaults".
///
/// # Safety
/// `ptr` must be null or point to a NUL-terminated buffer that stays valid
/// for the duration of the call.
unsafe fn parse_json<T: Default + serde::de::DeserializeOwned>(
    ptr: *const c_char,
) -> Result<T, FfiError> {
    if ptr.is_null() {
        return Ok(T::default());
    }
    let raw = CStr::from_ptr(ptr)
        .to_str()
        .map_err(|_| FfiError::invalid_options("options JSON is not valid UTF-8"))?;
    if raw.trim().is_empty() {
        return Ok(T::default());
    }
    serde_json::from_str(raw)
        .map_err(|e| FfiError::invalid_options(format!("invalid options JSON: {e}")))
}

// ---------------------------------------------------------------------------
// Library-level exports
// ---------------------------------------------------------------------------

/// Version string of this library, as a static NUL-terminated UTF-8 buffer.
///
/// The returned pointer is owned by the library and must **not** be passed to
/// [`pdfi_free_string`].
#[no_mangle]
pub extern "C" fn pdfi_version() -> *const c_char {
    VERSION.as_ptr() as *const c_char
}

/// Release a string returned by any `pdfi_*` function.
///
/// Passing null is a no-op. Passing anything not returned by this library —
/// or freeing the same pointer twice — is undefined behaviour.
///
/// # Safety
/// `s` must be null or a pointer previously returned by a `pdfi_*` call and
/// not yet freed.
#[no_mangle]
pub unsafe extern "C" fn pdfi_free_string(s: *mut c_char) {
    if !s.is_null() {
        drop(CString::from_raw(s));
    }
}

// ---------------------------------------------------------------------------
// process_pdf — detect + extract + markdown
// ---------------------------------------------------------------------------

/// Full pipeline over a PDF file: detect type, extract text, convert to
/// Markdown. Honours every option field; `pages` is **1-indexed**.
///
/// # Safety
/// See the module docs: `path` must be a valid NUL-terminated UTF-8 string,
/// `options_json` null or valid JSON, and the result freed with
/// [`pdfi_free_string`].
#[no_mangle]
pub unsafe extern "C" fn pdfi_process_pdf_file(
    path: *const c_char,
    options_json: *const c_char,
) -> *mut c_char {
    let path = required_str(path, "path");
    let options = parse_json::<OptionsDto>(options_json);
    respond(move || {
        let result = pdf_inspector::process_pdf_with_options(path?, options?.to_pdf_options())
            .map_err(FfiError::from)?;
        Ok(PdfResultDto::from(result))
    })
}

/// Full pipeline over PDF bytes. See [`pdfi_process_pdf_file`].
///
/// # Safety
/// `data` must point to `len` readable bytes; see the module docs.
#[no_mangle]
pub unsafe extern "C" fn pdfi_process_pdf_bytes(
    data: *const u8,
    len: usize,
    options_json: *const c_char,
) -> *mut c_char {
    let buffer = required_bytes(data, len);
    let options = parse_json::<OptionsDto>(options_json);
    respond(move || {
        let result =
            pdf_inspector::process_pdf_mem_with_options(buffer?, options?.to_pdf_options())
                .map_err(FfiError::from)?;
        Ok(PdfResultDto::from(result))
    })
}

// ---------------------------------------------------------------------------
// detect_pdf — detection only, no text extraction
// ---------------------------------------------------------------------------

/// Detection-only pass over a PDF file (no text extraction, no markdown).
/// The `mode` option is ignored — it is always `DetectOnly`.
///
/// # Safety
/// See the module docs.
#[no_mangle]
pub unsafe extern "C" fn pdfi_detect_pdf_file(
    path: *const c_char,
    options_json: *const c_char,
) -> *mut c_char {
    let path = required_str(path, "path");
    let options = parse_json::<OptionsDto>(options_json);
    respond(move || {
        let mut opts = options?.to_pdf_options();
        opts.mode = pdf_inspector::ProcessMode::DetectOnly;
        let result =
            pdf_inspector::process_pdf_with_options(path?, opts).map_err(FfiError::from)?;
        Ok(PdfResultDto::from(result))
    })
}

/// Detection-only pass over PDF bytes. See [`pdfi_detect_pdf_file`].
///
/// # Safety
/// See the module docs.
#[no_mangle]
pub unsafe extern "C" fn pdfi_detect_pdf_bytes(
    data: *const u8,
    len: usize,
    options_json: *const c_char,
) -> *mut c_char {
    let buffer = required_bytes(data, len);
    let options = parse_json::<OptionsDto>(options_json);
    respond(move || {
        let mut opts = options?.to_pdf_options();
        opts.mode = pdf_inspector::ProcessMode::DetectOnly;
        let result =
            pdf_inspector::process_pdf_mem_with_options(buffer?, opts).map_err(FfiError::from)?;
        Ok(PdfResultDto::from(result))
    })
}

// ---------------------------------------------------------------------------
// classify_pdf — routing decision only
// ---------------------------------------------------------------------------

/// Lightweight classification of a PDF file: type, page count, and which
/// pages need OCR (**0-indexed**). Options are ignored.
///
/// # Safety
/// See the module docs.
#[no_mangle]
pub unsafe extern "C" fn pdfi_classify_pdf_file(path: *const c_char) -> *mut c_char {
    let path = required_str(path, "path");
    respond(move || {
        let buffer = std::fs::read(path?).map_err(|e| FfiError::new(kind::IO, e.to_string()))?;
        let result = pdf_inspector::classify_pdf_mem(&buffer).map_err(FfiError::from)?;
        Ok(ClassificationDto::from(result))
    })
}

/// Lightweight classification of PDF bytes. See [`pdfi_classify_pdf_file`].
///
/// # Safety
/// See the module docs.
#[no_mangle]
pub unsafe extern "C" fn pdfi_classify_pdf_bytes(data: *const u8, len: usize) -> *mut c_char {
    let buffer = required_bytes(data, len);
    respond(move || {
        let result = pdf_inspector::classify_pdf_mem(buffer?).map_err(FfiError::from)?;
        Ok(ClassificationDto::from(result))
    })
}

// ---------------------------------------------------------------------------
// extract_text — plain text
// ---------------------------------------------------------------------------

/// Extract plain text from a PDF file. Options are ignored.
///
/// # Safety
/// See the module docs.
#[no_mangle]
pub unsafe extern "C" fn pdfi_extract_text_file(path: *const c_char) -> *mut c_char {
    let path = required_str(path, "path");
    respond(move || pdf_inspector::extract_text(path?).map_err(FfiError::from))
}

/// Extract plain text from PDF bytes. See [`pdfi_extract_text_file`].
///
/// # Safety
/// See the module docs.
#[no_mangle]
pub unsafe extern "C" fn pdfi_extract_text_bytes(data: *const u8, len: usize) -> *mut c_char {
    let buffer = required_bytes(data, len);
    respond(move || pdf_inspector::extractor::extract_text_mem(buffer?).map_err(FfiError::from))
}

// ---------------------------------------------------------------------------
// extract_text_with_positions — positioned items
// ---------------------------------------------------------------------------

/// Extract positioned text items from a PDF file. Honours `pages`
/// (**1-indexed**) and `password`; other option fields are ignored.
///
/// # Safety
/// See the module docs.
#[no_mangle]
pub unsafe extern "C" fn pdfi_extract_text_with_positions_file(
    path: *const c_char,
    options_json: *const c_char,
) -> *mut c_char {
    let path = required_str(path, "path");
    let options = parse_json::<OptionsDto>(options_json);
    respond(move || {
        let options = options?;
        let pages = options.page_set();
        let items = pdf_inspector::extract_text_with_positions_pages_with_password(
            path?,
            pages.as_ref(),
            options.password.as_deref(),
        )
        .map_err(FfiError::from)?;
        Ok(items.into_iter().map(TextItemDto::from).collect::<Vec<_>>())
    })
}

/// Extract positioned text items from PDF bytes. Honours `pages`
/// (**1-indexed**); other option fields are ignored.
///
/// # Safety
/// See the module docs.
#[no_mangle]
pub unsafe extern "C" fn pdfi_extract_text_with_positions_bytes(
    data: *const u8,
    len: usize,
    options_json: *const c_char,
) -> *mut c_char {
    let buffer = required_bytes(data, len);
    let options = parse_json::<OptionsDto>(options_json);
    respond(move || {
        let pages = options?.page_set();
        let items = pdf_inspector::extractor::extract_text_with_positions_mem_pages(
            buffer?,
            pages.as_ref(),
        )
        .map_err(FfiError::from)?;
        Ok(items.into_iter().map(TextItemDto::from).collect::<Vec<_>>())
    })
}

// ---------------------------------------------------------------------------
// extract_structure_elements — tagged PDFs
// ---------------------------------------------------------------------------

/// Extract structure-tree element references from a tagged PDF file. Honours
/// `pages` (**1-indexed**, matching `TextItem.page`). Returns an empty list
/// when the PDF is not tagged.
///
/// # Safety
/// See the module docs.
#[no_mangle]
pub unsafe extern "C" fn pdfi_extract_structure_elements_file(
    path: *const c_char,
    options_json: *const c_char,
) -> *mut c_char {
    let path = required_str(path, "path");
    let options = parse_json::<OptionsDto>(options_json);
    respond(move || {
        let options = options?;
        let elements = pdf_inspector::extract_structure_elements(path?, options.page_list())
            .map_err(FfiError::from)?;
        Ok(elements
            .into_iter()
            .map(StructureElementDto::from)
            .collect::<Vec<_>>())
    })
}

/// Extract structure-tree element references from tagged PDF bytes.
/// See [`pdfi_extract_structure_elements_file`].
///
/// # Safety
/// See the module docs.
#[no_mangle]
pub unsafe extern "C" fn pdfi_extract_structure_elements_bytes(
    data: *const u8,
    len: usize,
    options_json: *const c_char,
) -> *mut c_char {
    let buffer = required_bytes(data, len);
    let options = parse_json::<OptionsDto>(options_json);
    respond(move || {
        let options = options?;
        let elements = pdf_inspector::extract_structure_elements_mem(buffer?, options.page_list())
            .map_err(FfiError::from)?;
        Ok(elements
            .into_iter()
            .map(StructureElementDto::from)
            .collect::<Vec<_>>())
    })
}

// ---------------------------------------------------------------------------
// extract_pages_markdown — per-page markdown
// ---------------------------------------------------------------------------

/// Per-page markdown for a PDF file, with document-wide layout
/// classification. Honours `pages` (**0-indexed**, output follows the
/// caller's order); other option fields are ignored.
///
/// # Safety
/// See the module docs.
#[no_mangle]
pub unsafe extern "C" fn pdfi_extract_pages_markdown_file(
    path: *const c_char,
    options_json: *const c_char,
) -> *mut c_char {
    let path = required_str(path, "path");
    let options = parse_json::<OptionsDto>(options_json);
    respond(move || {
        let options = options?;
        let result = pdf_inspector::extract_pages_markdown(path?, options.page_list())
            .map_err(FfiError::from)?;
        Ok(PagesExtractionDto::from(result))
    })
}

/// Per-page markdown for PDF bytes. See [`pdfi_extract_pages_markdown_file`].
///
/// # Safety
/// See the module docs.
#[no_mangle]
pub unsafe extern "C" fn pdfi_extract_pages_markdown_bytes(
    data: *const u8,
    len: usize,
    options_json: *const c_char,
) -> *mut c_char {
    let buffer = required_bytes(data, len);
    let options = parse_json::<OptionsDto>(options_json);
    respond(move || {
        let options = options?;
        let result = pdf_inspector::extract_pages_markdown_mem(buffer?, options.page_list())
            .map_err(FfiError::from)?;
        Ok(PagesExtractionDto::from(result))
    })
}

// ---------------------------------------------------------------------------
// extract_text_in_regions — hybrid OCR pipelines
// ---------------------------------------------------------------------------

/// Extract text inside bounding boxes from a PDF file.
///
/// `request_json` is `{"page_regions":[{"page":0,"regions":[[x1,y1,x2,y2]]}]}`
/// with **0-indexed** pages and coordinates in PDF points, top-left origin.
///
/// # Safety
/// See the module docs.
#[no_mangle]
pub unsafe extern "C" fn pdfi_extract_text_in_regions_file(
    path: *const c_char,
    request_json: *const c_char,
) -> *mut c_char {
    let path = required_str(path, "path");
    let request = parse_json::<RegionsRequest>(request_json);
    respond(move || {
        let buffer = std::fs::read(path?).map_err(|e| FfiError::new(kind::IO, e.to_string()))?;
        let pairs = request?.into_pairs();
        let result =
            pdf_inspector::extract_text_in_regions_mem(&buffer, &pairs).map_err(FfiError::from)?;
        Ok(result
            .into_iter()
            .map(PageRegionsDto::from)
            .collect::<Vec<_>>())
    })
}

/// Extract text inside bounding boxes from PDF bytes.
/// See [`pdfi_extract_text_in_regions_file`].
///
/// # Safety
/// See the module docs.
#[no_mangle]
pub unsafe extern "C" fn pdfi_extract_text_in_regions_bytes(
    data: *const u8,
    len: usize,
    request_json: *const c_char,
) -> *mut c_char {
    let buffer = required_bytes(data, len);
    let request = parse_json::<RegionsRequest>(request_json);
    respond(move || {
        let pairs = request?.into_pairs();
        let result =
            pdf_inspector::extract_text_in_regions_mem(buffer?, &pairs).map_err(FfiError::from)?;
        Ok(result
            .into_iter()
            .map(PageRegionsDto::from)
            .collect::<Vec<_>>())
    })
}
