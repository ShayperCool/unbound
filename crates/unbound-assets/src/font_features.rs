//! OpenType layout records in font units, read with ttf-parser. Queries preserve lookup,
//! subtable and coverage order; filtered queries visit the caller's glyphs in their given order.
use crate::font::FontFace;
use serde::Serialize;
use ttf_parser::{GlyphId, gsub::SubstitutionSubtable, opentype_layout::Coverage};

#[derive(Clone, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LigatureRecord {
    pub components: Vec<u32>,
    pub glyph: u32,
}

#[derive(Default)]
pub(crate) struct FontFeatures {
    ligatures: Vec<(u16, u16, LigatureRecord)>,
}

/// Visit the coverage in table order, retaining each explicit coverage index.
fn coverage(coverage: Coverage<'_>, mut visit: impl FnMut(u16, GlyphId)) {
    match coverage {
        Coverage::Format1 { glyphs } => {
            for (i, glyph) in glyphs.into_iter().enumerate() {
                visit(i as u16, glyph);
            }
        }
        Coverage::Format2 { records } => {
            for range in records {
                for glyph in range.start.0..=range.end.0 {
                    if let Some(i) = range.value.checked_add(glyph - range.start.0) {
                        visit(i, GlyphId(glyph));
                    }
                }
            }
        }
    }
}

impl FontFeatures {
    fn read(face: &ttf_parser::Face<'_>) -> Self {
        let mut result = Self::default();
        if let Some(table) = face.tables().gsub {
            for lookup_index in 0..table.lookups.len() {
                let Some(lookup) = table.lookups.get(lookup_index) else {
                    continue;
                };
                for subtable_index in 0..lookup.subtables.len() {
                    let Some(SubstitutionSubtable::Ligature(subtable)) =
                        lookup.subtables.get(subtable_index)
                    else {
                        continue;
                    };
                    coverage(subtable.coverage, |i, first| {
                        let Some(set) = subtable.ligature_sets.get(i) else {
                            return;
                        };
                        for j in 0..set.len() {
                            let Some(ligature) = set.get(j) else { continue };
                            let mut components =
                                Vec::with_capacity(1 + ligature.components.len() as usize);
                            components.push(u32::from(first.0));
                            components
                                .extend(ligature.components.into_iter().map(|g| u32::from(g.0)));
                            result.ligatures.push((
                                lookup_index,
                                subtable_index,
                                LigatureRecord {
                                    components,
                                    glyph: u32::from(ligature.glyph.0),
                                },
                            ));
                        }
                    });
                }
            }
        }
        result
    }
}

impl FontFace {
    /// All GSUB ligature records, or records starting with the requested glyphs. A zero glyph
    /// terminates a filtered input, as in TextCore's glyph list queries. No size scaling applies.
    pub fn ligatures(&self, glyphs: Option<&[u32]>, lookup: Option<i32>) -> Vec<LigatureRecord> {
        let features = self
            .features
            .get_or_init(|| FontFeatures::read(&self.face()));
        let mut output = Vec::new();
        let records = &features.ligatures;
        let mut start = 0;
        while start < records.len() {
            let group = (records[start].0, records[start].1);
            let end = start
                + records[start..]
                    .iter()
                    .take_while(|r| (r.0, r.1) == group)
                    .count();
            if lookup.is_none_or(|l| l == i32::from(group.0)) {
                if let Some(glyphs) = glyphs {
                    for glyph in glyphs.iter().take_while(|&&g| g != 0) {
                        output.extend(
                            records[start..end]
                                .iter()
                                .filter(|r| r.2.components[0] == *glyph)
                                .map(|r| r.2.clone()),
                        );
                    }
                } else {
                    output.extend(records[start..end].iter().map(|r| r.2.clone()));
                }
            }
            start = end;
        }
        output
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn font() -> FontFace {
        FontFace::from_bytes(
            include_bytes!("../../../tests/projects/fontfeatures/Assets/Fonts/Controlled.ttf")
                .to_vec(),
            0,
        )
        .unwrap()
    }

    #[test]
    fn ligatures_include_all_components_and_preserve_lookup_priority() {
        let face = font();
        let f = u32::from(face.glyph_index('f'));
        let i = u32::from(face.glyph_index('i'));
        let records = face.ligatures(Some(&[f]), None);
        assert_eq!(records[0].components, [f, f, i]);
        assert_eq!(records[1].components, [f, i]);
        assert_ne!(records[1].glyph, records.last().unwrap().glyph);
        assert_eq!(face.ligatures(Some(&[f]), Some(1)).len(), 3);
        assert_eq!(face.ligatures(Some(&[f]), Some(2)).len(), 1);
        assert!(face.ligatures(Some(&[f]), Some(0)).is_empty());
        assert!(face.ligatures(Some(&[f]), Some(-1)).is_empty());
    }

    #[test]
    fn filtered_lists_repeat_glyphs_and_stop_at_zero_without_truncation() {
        let face = font();
        let f = u32::from(face.glyph_index('f'));
        let once = face.ligatures(Some(&[f]), None);
        assert_eq!(face.ligatures(Some(&[f, f]), None).len(), 2 * once.len());
        assert_eq!(face.ligatures(Some(&[f, 0, f]), None), once);
        assert!(face.ligatures(Some(&[0, f]), None).is_empty());
        assert!(face.ligatures(Some(&[f + 65536]), None).is_empty());
        assert!(face.ligatures(Some(&[]), None).is_empty());
    }
}
