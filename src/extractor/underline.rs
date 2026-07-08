//! Geometric underline detection.
//!
//! PDFs have no underline font flag — underlines are drawn as separate
//! graphics: stroked horizontal lines (`l`/`S` operators) or thin filled
//! rectangles (`re`/`f`). This pass correlates those graphics with text
//! items after extraction: an item is underlined when a horizontal
//! line/thin rect sits just below its baseline and covers most of its
//! horizontal extent.
//!
//! Known false-positive source: table cell borders. That is acceptable at
//! this layer — downstream consumers apply inline styling only to plain
//! text regions (table regions go through dedicated table extraction), and
//! the alternative (grid-awareness here) would couple this pass to table
//! detection.

use crate::types::{ItemType, PdfLine, PdfRect, TextItem};

/// Max thickness (pt) for a stroked line / filled rect to count as an
/// underline rule rather than a border or decorative band.
const MAX_RULE_THICKNESS: f32 = 2.0;

/// Fraction of the item's width that the rule must cover horizontally.
const MIN_X_OVERLAP: f32 = 0.6;

/// A horizontal rule candidate in page coordinates (PDF y-up).
struct Rule {
    x1: f32,
    x2: f32,
    y: f32,
}

fn rules_from_graphics(rects: &[PdfRect], lines: &[PdfLine], page: u32) -> Vec<Rule> {
    let mut rules: Vec<Rule> = Vec::new();
    for l in lines {
        if l.page != page {
            continue;
        }
        // Horizontal stroked line (tolerate slight skew).
        if (l.y1 - l.y2).abs() <= MAX_RULE_THICKNESS {
            let (x1, x2) = if l.x1 <= l.x2 {
                (l.x1, l.x2)
            } else {
                (l.x2, l.x1)
            };
            if x2 - x1 > 1.0 {
                rules.push(Rule {
                    x1,
                    x2,
                    y: (l.y1 + l.y2) / 2.0,
                });
            }
        }
    }
    for r in rects {
        if r.page != page {
            continue;
        }
        // Thin filled rect used as an underline rule.
        if r.height <= MAX_RULE_THICKNESS && r.width > 1.0 {
            rules.push(Rule {
                x1: r.x,
                x2: r.x + r.width,
                y: r.y + r.height / 2.0,
            });
        }
    }
    rules
}

/// Mark `is_underline` on text items that have a horizontal rule just
/// below their baseline. `items`, `rects`, and `lines` are a single
/// page's extraction output (all in PDF coordinates, y-up, where
/// `TextItem::y` is the text baseline).
pub(crate) fn mark_underlined_items(
    items: &mut [TextItem],
    rects: &[PdfRect],
    lines: &[PdfLine],
    page: u32,
) {
    let rules = rules_from_graphics(rects, lines, page);
    if rules.is_empty() {
        return;
    }

    for item in items.iter_mut() {
        if !matches!(item.item_type, ItemType::Text)
            || item.text.trim().is_empty()
            || item.width <= 0.0
        {
            continue;
        }
        // Vertical window: underlines sit at or slightly below the
        // baseline. Fonts draw them at roughly 5-15% of the em below;
        // allow up to 35% (min 3pt) below and 1pt above for rounding.
        let below = (item.font_size * 0.35).max(3.0);
        let y_min = item.y - below;
        let y_max = item.y + 1.0;

        let ix1 = item.x;
        let ix2 = item.x + item.width;
        let min_overlap = item.width * MIN_X_OVERLAP;

        for rule in &rules {
            if rule.y < y_min || rule.y > y_max {
                continue;
            }
            let overlap = rule.x2.min(ix2) - rule.x1.max(ix1);
            if overlap >= min_overlap {
                item.is_underline = true;
                break;
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::types::ItemType;

    fn item(text: &str, x: f32, y: f32, width: f32, font_size: f32) -> TextItem {
        TextItem {
            text: text.to_string(),
            x,
            y,
            width,
            height: font_size,
            font: "F1".to_string(),
            font_size,
            page: 1,
            is_bold: false,
            is_italic: false,
            is_underline: false,
            item_type: ItemType::Text,
            mcid: None,
        }
    }

    fn hline(x1: f32, x2: f32, y: f32) -> PdfLine {
        PdfLine {
            x1,
            y1: y,
            x2,
            y2: y,
            page: 1,
        }
    }

    fn thin_rect(x: f32, y: f32, width: f32) -> PdfRect {
        PdfRect {
            x,
            y,
            width,
            height: 0.8,
            page: 1,
        }
    }

    #[test]
    fn stroked_line_under_baseline_marks_underline() {
        let mut items = vec![item("underlined", 100.0, 500.0, 60.0, 10.0)];
        let lines = vec![hline(99.0, 161.0, 498.5)];
        mark_underlined_items(&mut items, &[], &lines, 1);
        assert!(items[0].is_underline);
    }

    #[test]
    fn thin_filled_rect_under_baseline_marks_underline() {
        let mut items = vec![item("underlined", 100.0, 500.0, 60.0, 10.0)];
        let rects = vec![thin_rect(100.0, 497.8, 60.0)];
        mark_underlined_items(&mut items, &rects, &[], 1);
        assert!(items[0].is_underline);
    }

    #[test]
    fn long_rule_under_multiple_items_marks_each() {
        // One underline drawn under a whole sentence: every overlapped
        // item gets the flag.
        let mut items = vec![
            item("first", 100.0, 500.0, 40.0, 10.0),
            item("second", 145.0, 500.0, 50.0, 10.0),
        ];
        let lines = vec![hline(98.0, 200.0, 498.0)];
        mark_underlined_items(&mut items, &[], &lines, 1);
        assert!(items[0].is_underline);
        assert!(items[1].is_underline);
    }

    #[test]
    fn line_far_below_baseline_is_not_an_underline() {
        // A horizontal rule 30pt below (section divider) must not mark.
        let mut items = vec![item("text", 100.0, 500.0, 60.0, 10.0)];
        let lines = vec![hline(90.0, 300.0, 470.0)];
        mark_underlined_items(&mut items, &[], &lines, 1);
        assert!(!items[0].is_underline);
    }

    #[test]
    fn line_above_baseline_is_not_an_underline() {
        // Strikethrough / overline geometry must not mark.
        let mut items = vec![item("text", 100.0, 500.0, 60.0, 10.0)];
        let lines = vec![hline(90.0, 300.0, 505.0)];
        mark_underlined_items(&mut items, &[], &lines, 1);
        assert!(!items[0].is_underline);
    }

    #[test]
    fn insufficient_horizontal_overlap_is_not_an_underline() {
        // Rule under only a quarter of the item (e.g. neighboring cell
        // border) must not mark.
        let mut items = vec![item("wide text item", 100.0, 500.0, 100.0, 10.0)];
        let lines = vec![hline(100.0, 125.0, 498.5)];
        mark_underlined_items(&mut items, &[], &lines, 1);
        assert!(!items[0].is_underline);
    }

    #[test]
    fn thick_band_is_not_an_underline() {
        // A highlight bar / filled cell background (tall rect) must not mark.
        let mut items = vec![item("text", 100.0, 500.0, 60.0, 10.0)];
        let rects = vec![PdfRect {
            x: 95.0,
            y: 495.0,
            width: 80.0,
            height: 14.0,
            page: 1,
        }];
        mark_underlined_items(&mut items, &rects, &[], 1);
        assert!(!items[0].is_underline);
    }

    #[test]
    fn vertical_line_is_not_an_underline() {
        let mut items = vec![item("text", 100.0, 500.0, 60.0, 10.0)];
        let lines = vec![PdfLine {
            x1: 120.0,
            y1: 498.0,
            x2: 120.0,
            y2: 400.0,
            page: 1,
        }];
        mark_underlined_items(&mut items, &[], &lines, 1);
        assert!(!items[0].is_underline);
    }

    #[test]
    fn other_pages_graphics_do_not_mark() {
        let mut items = vec![item("text", 100.0, 500.0, 60.0, 10.0)];
        let mut line = hline(99.0, 161.0, 498.5);
        line.page = 2;
        mark_underlined_items(&mut items, &[], &[line], 1);
        assert!(!items[0].is_underline);
    }
}
