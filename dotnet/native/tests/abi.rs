//! End-to-end tests for the C ABI, driven exactly as .NET drives it:
//! raw pointers in, JSON envelope out, every response freed with
//! `pdfi_free_string`.

use std::ffi::{c_char, CStr, CString};
use std::path::PathBuf;

use pdf_inspector_ffi::*;
use serde_json::Value;

fn fixture(name: &str) -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("../../tests/fixtures")
        .join(name)
}

fn fixture_bytes(name: &str) -> Vec<u8> {
    std::fs::read(fixture(name)).expect("fixture should be readable")
}

fn c(s: &str) -> CString {
    CString::new(s).unwrap()
}

/// Take ownership of a response pointer, parse it, and free it.
fn take(ptr: *mut c_char) -> Value {
    assert!(!ptr.is_null(), "response pointer must not be null");
    let json = unsafe { CStr::from_ptr(ptr) }
        .to_str()
        .expect("response is UTF-8")
        .to_owned();
    unsafe { pdfi_free_string(ptr) };
    serde_json::from_str(&json).expect("response is JSON")
}

/// Unwrap a successful envelope, asserting `ok`.
fn data(ptr: *mut c_char) -> Value {
    let value = take(ptr);
    assert_eq!(
        value["ok"],
        Value::Bool(true),
        "expected success, got {value}"
    );
    value["data"].clone()
}

/// Unwrap a failed envelope and return its `kind`.
fn error_kind(ptr: *mut c_char) -> String {
    let value = take(ptr);
    assert_eq!(
        value["ok"],
        Value::Bool(false),
        "expected failure, got {value}"
    );
    value["error"]["kind"].as_str().unwrap().to_owned()
}

// ---------------------------------------------------------------------------
// Library basics
// ---------------------------------------------------------------------------

#[test]
fn version_matches_the_crate() {
    let version = unsafe { CStr::from_ptr(pdfi_version()) }.to_str().unwrap();
    assert_eq!(version, env!("CARGO_PKG_VERSION"));
}

#[test]
fn free_string_tolerates_null() {
    unsafe { pdfi_free_string(std::ptr::null_mut()) };
}

// ---------------------------------------------------------------------------
// process_pdf
// ---------------------------------------------------------------------------

#[test]
fn process_file_returns_markdown_and_metadata() {
    let path = c(fixture("2013-app2.pdf").to_str().unwrap());
    let data = data(unsafe { pdfi_process_pdf_file(path.as_ptr(), std::ptr::null()) });

    assert_eq!(data["pdf_type"], "text_based");
    assert!(data["page_count"].as_u64().unwrap() > 0);
    assert!(!data["markdown"].as_str().unwrap().is_empty());
    assert!(data["confidence"].as_f64().unwrap() > 0.0);
    assert!(data["pages_with_tables"].is_array());
    assert!(data["has_encoding_issues"].is_boolean());
}

#[test]
fn process_bytes_matches_process_file() {
    let bytes = fixture_bytes("2013-app2.pdf");
    let path = c(fixture("2013-app2.pdf").to_str().unwrap());

    let from_bytes =
        data(unsafe { pdfi_process_pdf_bytes(bytes.as_ptr(), bytes.len(), std::ptr::null()) });
    let from_file = data(unsafe { pdfi_process_pdf_file(path.as_ptr(), std::ptr::null()) });

    assert_eq!(from_bytes["markdown"], from_file["markdown"]);
    assert_eq!(from_bytes["page_count"], from_file["page_count"]);
}

#[test]
fn page_filter_narrows_the_output() {
    let bytes = fixture_bytes("shannon-entropy-p1-2.pdf");
    let options = c(r#"{"pages":[1]}"#);

    let one_page =
        data(unsafe { pdfi_process_pdf_bytes(bytes.as_ptr(), bytes.len(), options.as_ptr()) });
    let all_pages =
        data(unsafe { pdfi_process_pdf_bytes(bytes.as_ptr(), bytes.len(), std::ptr::null()) });

    let one = one_page["markdown"].as_str().unwrap();
    let all = all_pages["markdown"].as_str().unwrap();
    assert!(!one.is_empty());
    assert!(
        one.len() < all.len(),
        "filtering to page 1 should shorten the markdown ({} vs {})",
        one.len(),
        all.len()
    );
}

#[test]
fn markdown_options_reach_the_converter() {
    // Page markers are emitted on page transitions, so this needs a
    // multi-page fixture.
    let bytes = fixture_bytes("shannon-entropy-p1-2.pdf");
    let options = c(r#"{"markdown":{"include_page_numbers":true}}"#);
    let data =
        data(unsafe { pdfi_process_pdf_bytes(bytes.as_ptr(), bytes.len(), options.as_ptr()) });
    assert!(
        data["markdown"].as_str().unwrap().contains("<!-- Page"),
        "include_page_numbers should emit page break markers"
    );
}

#[test]
fn detect_only_skips_markdown() {
    let bytes = fixture_bytes("2013-app2.pdf");
    let data =
        data(unsafe { pdfi_detect_pdf_bytes(bytes.as_ptr(), bytes.len(), std::ptr::null()) });
    assert_eq!(data["markdown"], Value::Null);
    assert_eq!(data["pdf_type"], "text_based");
}

#[test]
fn detect_only_ignores_a_caller_supplied_mode() {
    let bytes = fixture_bytes("2013-app2.pdf");
    let options = c(r#"{"mode":"full"}"#);
    let data =
        data(unsafe { pdfi_detect_pdf_bytes(bytes.as_ptr(), bytes.len(), options.as_ptr()) });
    assert_eq!(
        data["markdown"],
        Value::Null,
        "detect entry points must stay detection-only"
    );
}

#[test]
fn password_option_decrypts_an_encrypted_pdf() {
    let path = c(fixture("encrypted-secret123.pdf").to_str().unwrap());

    let without = error_kind(unsafe { pdfi_process_pdf_file(path.as_ptr(), std::ptr::null()) });
    assert_eq!(without, "encrypted");

    let options = c(r#"{"password":"secret123"}"#);
    let with = data(unsafe { pdfi_process_pdf_file(path.as_ptr(), options.as_ptr()) });
    assert!(!with["markdown"].as_str().unwrap().is_empty());
}

// ---------------------------------------------------------------------------
// classify / extract_text
// ---------------------------------------------------------------------------

#[test]
fn classify_reports_type_and_page_count() {
    let bytes = fixture_bytes("2013-app2.pdf");
    let data = data(unsafe { pdfi_classify_pdf_bytes(bytes.as_ptr(), bytes.len()) });
    assert_eq!(data["pdf_type"], "text_based");
    assert!(data["page_count"].as_u64().unwrap() > 0);
    assert!(data["pages_needing_ocr"].is_array());
}

#[test]
fn classify_file_and_bytes_agree() {
    let path = c(fixture("2013-app2.pdf").to_str().unwrap());
    let bytes = fixture_bytes("2013-app2.pdf");
    let from_file = data(unsafe { pdfi_classify_pdf_file(path.as_ptr()) });
    let from_bytes = data(unsafe { pdfi_classify_pdf_bytes(bytes.as_ptr(), bytes.len()) });
    assert_eq!(from_file, from_bytes);
}

#[test]
fn extract_text_returns_a_plain_string() {
    let bytes = fixture_bytes("2013-app2.pdf");
    let data = data(unsafe { pdfi_extract_text_bytes(bytes.as_ptr(), bytes.len()) });
    assert!(!data.as_str().unwrap().trim().is_empty());
}

// ---------------------------------------------------------------------------
// positioned items / structure tree
// ---------------------------------------------------------------------------

#[test]
fn positions_carry_geometry_and_font_metadata() {
    let bytes = fixture_bytes("2013-app2.pdf");
    let data = data(unsafe {
        pdfi_extract_text_with_positions_bytes(bytes.as_ptr(), bytes.len(), std::ptr::null())
    });
    let items = data.as_array().unwrap();
    assert!(!items.is_empty());

    let first = &items[0];
    assert!(first["text"].is_string());
    assert!(first["x"].is_number());
    assert!(first["y"].is_number());
    assert!(first["font_size"].is_number());
    assert_eq!(first["page"], 1);
    assert!(first["is_bold"].is_boolean());
    assert_eq!(first["item_type"], "text");
    // `url` is only present on link items.
    assert!(first.get("url").is_none());
}

#[test]
fn positions_honour_the_page_filter() {
    let bytes = fixture_bytes("shannon-entropy-p1-2.pdf");
    let options = c(r#"{"pages":[2]}"#);
    let data = data(unsafe {
        pdfi_extract_text_with_positions_bytes(bytes.as_ptr(), bytes.len(), options.as_ptr())
    });
    let items = data.as_array().unwrap();
    assert!(!items.is_empty());
    assert!(
        items.iter().all(|item| item["page"] == 2),
        "page filter is 1-indexed and must exclude other pages"
    );
}

#[test]
fn structure_elements_resolve_roles_for_tagged_pdfs() {
    let bytes = fixture_bytes("firecrawl_docs_tagged.pdf");
    let data = data(unsafe {
        pdfi_extract_structure_elements_bytes(bytes.as_ptr(), bytes.len(), std::ptr::null())
    });
    let elements = data.as_array().unwrap();
    assert!(!elements.is_empty(), "fixture is a tagged PDF");
    assert!(elements[0]["role"].is_string());
    assert!(elements[0]["mcid"].is_number());
    assert!(elements[0]["page"].is_number());
}

#[test]
fn structure_elements_are_empty_for_untagged_pdfs() {
    let bytes = fixture_bytes("thermo-freon12.pdf");
    let data = data(unsafe {
        pdfi_extract_structure_elements_bytes(bytes.as_ptr(), bytes.len(), std::ptr::null())
    });
    assert!(data.as_array().unwrap().is_empty());
}

// ---------------------------------------------------------------------------
// per-page markdown / regions
// ---------------------------------------------------------------------------

#[test]
fn pages_markdown_returns_requested_pages_in_order() {
    let bytes = fixture_bytes("shannon-entropy-p1-2.pdf");
    let options = c(r#"{"pages":[1,0]}"#);
    let data = data(unsafe {
        pdfi_extract_pages_markdown_bytes(bytes.as_ptr(), bytes.len(), options.as_ptr())
    });

    let pages = data["pages"].as_array().unwrap();
    assert_eq!(pages.len(), 2);
    // Pages here are 0-indexed and follow the caller's order.
    assert_eq!(pages[0]["page"], 1);
    assert_eq!(pages[1]["page"], 0);
    assert!(data["is_complex"].is_boolean());
}

#[test]
fn region_extraction_returns_one_result_per_region() {
    let bytes = fixture_bytes("2013-app2.pdf");
    let request = c(r#"{"page_regions":[{"page":0,"regions":[[0,0,612,400],[0,400,612,792]]}]}"#);
    let data = data(unsafe {
        pdfi_extract_text_in_regions_bytes(bytes.as_ptr(), bytes.len(), request.as_ptr())
    });

    let pages = data.as_array().unwrap();
    assert_eq!(pages.len(), 1);
    assert_eq!(pages[0]["page"], 0);
    let regions = pages[0]["regions"].as_array().unwrap();
    assert_eq!(regions.len(), 2);
    assert!(regions[0]["needs_ocr"].is_boolean());
    assert!(
        regions
            .iter()
            .any(|r| !r["text"].as_str().unwrap().is_empty()),
        "at least one half-page region should carry text"
    );
}

// ---------------------------------------------------------------------------
// Error handling
// ---------------------------------------------------------------------------

#[test]
fn non_pdf_bytes_report_not_a_pdf() {
    let bytes = b"this is definitely not a PDF".to_vec();
    let kind = error_kind(unsafe {
        pdfi_process_pdf_bytes(bytes.as_ptr(), bytes.len(), std::ptr::null())
    });
    assert_eq!(kind, "not_a_pdf");
}

#[test]
fn missing_file_reports_io() {
    let path = c("/nonexistent/definitely-missing.pdf");
    let kind = error_kind(unsafe { pdfi_process_pdf_file(path.as_ptr(), std::ptr::null()) });
    assert_eq!(kind, "io");
}

#[test]
fn null_path_reports_invalid_argument() {
    let kind = error_kind(unsafe { pdfi_process_pdf_file(std::ptr::null(), std::ptr::null()) });
    assert_eq!(kind, "invalid_argument");
}

#[test]
fn null_data_with_nonzero_length_reports_invalid_argument() {
    let kind =
        error_kind(unsafe { pdfi_process_pdf_bytes(std::ptr::null(), 16, std::ptr::null()) });
    assert_eq!(kind, "invalid_argument");
}

#[test]
fn empty_buffer_reports_not_a_pdf_rather_than_crashing() {
    let kind = error_kind(unsafe { pdfi_process_pdf_bytes(std::ptr::null(), 0, std::ptr::null()) });
    assert_eq!(kind, "not_a_pdf");
}

#[test]
fn malformed_options_report_invalid_options() {
    let bytes = fixture_bytes("2013-app2.pdf");
    let options = c("{not json");
    let kind = error_kind(unsafe {
        pdfi_process_pdf_bytes(bytes.as_ptr(), bytes.len(), options.as_ptr())
    });
    assert_eq!(kind, "invalid_options");
}

#[test]
fn unknown_option_field_reports_invalid_options() {
    let bytes = fixture_bytes("2013-app2.pdf");
    let options = c(r#"{"pagez":[1]}"#);
    let kind = error_kind(unsafe {
        pdfi_process_pdf_bytes(bytes.as_ptr(), bytes.len(), options.as_ptr())
    });
    assert_eq!(kind, "invalid_options");
}

#[test]
fn empty_options_string_means_defaults() {
    let bytes = fixture_bytes("2013-app2.pdf");
    let options = c("   ");
    let data =
        data(unsafe { pdfi_process_pdf_bytes(bytes.as_ptr(), bytes.len(), options.as_ptr()) });
    assert_eq!(data["pdf_type"], "text_based");
}

#[test]
fn broken_pdfs_surface_a_structured_error_not_a_panic() {
    let bytes = fixture_bytes("broken_startxref_pointer.pdf");
    let value =
        take(unsafe { pdfi_process_pdf_bytes(bytes.as_ptr(), bytes.len(), std::ptr::null()) });
    // Whatever the outcome, it must be a well-formed envelope and never a panic.
    assert!(value["ok"].is_boolean());
    if value["ok"] == Value::Bool(false) {
        assert_ne!(value["error"]["kind"], "panic");
    }
}

// ---------------------------------------------------------------------------
// Concurrency
// ---------------------------------------------------------------------------

#[test]
fn concurrent_calls_are_independent() {
    let bytes = std::sync::Arc::new(fixture_bytes("2013-app2.pdf"));
    let handles: Vec<_> = (0..4)
        .map(|_| {
            let bytes = std::sync::Arc::clone(&bytes);
            std::thread::spawn(move || {
                let data = data(unsafe { pdfi_classify_pdf_bytes(bytes.as_ptr(), bytes.len()) });
                data["page_count"].as_u64().unwrap()
            })
        })
        .collect();

    let counts: Vec<u64> = handles.into_iter().map(|h| h.join().unwrap()).collect();
    assert!(counts.windows(2).all(|w| w[0] == w[1]));
}
