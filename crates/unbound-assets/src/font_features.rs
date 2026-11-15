//! OpenType layout records in font units, read with ttf-parser. Queries preserve lookup,
//! subtable and coverage order; filtered queries visit the caller's glyphs in their given order.
use crate::font::FontFace;
use serde::Serialize;
use ttf_parser::{
    GlyphId, Tag,
    gpos::{PairAdjustment, PositioningSubtable, SingleAdjustment, ValueRecord},
    gsub::SubstitutionSubtable,
    opentype_layout::{ClassDefinition, Coverage},
};

#[derive(Clone, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LigatureRecord {
    pub components: Vec<u32>,
    pub glyph: u32,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct MarkRecord {
    pub base: u32,
    pub base_anchor: [i16; 2],
    pub mark: u32,
    pub mark_anchor: [i16; 2],
    pub missing_anchor: bool,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize)]
pub struct AdjustmentRecord {
    pub glyph: u32,
    pub values: [i16; 4],
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PairRecord {
    pub first: u32,
    pub second: u32,
    pub first_values: [i16; 4],
    pub second_values: [i16; 4],
}

fn values(value: ValueRecord<'_>) -> [i16; 4] {
    [
        value.x_placement,
        value.y_placement,
        value.x_advance,
        value.y_advance,
    ]
}

#[derive(Default)]
pub(crate) struct FontFeatures {
    ligatures: Vec<(u16, u16, LigatureRecord)>,
    singles: Vec<(u16, u16, AdjustmentRecord)>,
    pairs: std::sync::OnceLock<Vec<(u16, u16, PairRecord)>>,
    mark_to_base: Vec<(u16, u16, MarkRecord)>,
    mark_to_mark: Vec<(u16, u16, MarkRecord)>,
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

fn second_classes(
    definition: ClassDefinition<'_>,
) -> std::collections::BTreeMap<u16, Vec<GlyphId>> {
    let mut groups = std::collections::BTreeMap::<u16, Vec<GlyphId>>::new();
    let mut add = |class, glyph| {
        groups.entry(class).or_default().push(GlyphId(glyph));
    };
    match definition {
        ClassDefinition::Format1 { start, classes } => {
            for (i, class) in classes.into_iter().enumerate() {
                // Array entries with value zero are omitted; an explicit zero-class range
                // is kept below (Unity 6000.0.71f1, tests/projects/fontpairs).
                if class != 0
                    && let Some(glyph) = start.0.checked_add(i as u16)
                {
                    add(class, glyph);
                }
            }
        }
        ClassDefinition::Format2 { records } => {
            for range in records {
                for glyph in range.start.0..=range.end.0 {
                    add(range.value, glyph);
                }
            }
        }
        ClassDefinition::Empty => {}
    }
    groups
}

fn read_pairs(
    data: &[u8],
    lookup: u16,
    subtable: u16,
    pair: PairAdjustment<'_>,
    into: &mut Vec<(u16, u16, PairRecord)>,
) {
    let mut add = |first: GlyphId, second: GlyphId, a, b| {
        into.push((
            lookup,
            subtable,
            PairRecord {
                first: u32::from(first.0),
                second: u32::from(second.0),
                first_values: values(a),
                second_values: values(b),
            },
        ))
    };
    match pair {
        PairAdjustment::Format1 {
            coverage: firsts,
            sets,
        } => {
            let Some(table) = positioning_subtable(data, lookup, subtable) else {
                return;
            };
            let (Some(a), Some(b)) = (word(table, 4), word(table, 6)) else {
                return;
            };
            let stride = 2 + 2 * ((a & 255).count_ones() + (b & 255).count_ones()) as usize;
            coverage(firsts, |i, first| {
                let Some(set) = sets.get(i) else { return };
                // The public reader provides values by second glyph. This bounded metadata
                // walk obtains those keys without scanning every glyph in the face.
                let Some(keys) = offset16(table, 10 + usize::from(i) * 2) else {
                    return;
                };
                let Some(count) = word(keys, 0) else { return };
                for j in 0..usize::from(count) {
                    let Some(second) = word(keys, 2 + j * stride) else {
                        break;
                    };
                    if let Some((a, b)) = set.get(GlyphId(second)) {
                        add(first, GlyphId(second), a, b);
                    }
                }
            });
        }
        PairAdjustment::Format2 {
            coverage: firsts,
            classes,
            matrix,
        } => {
            let seconds = second_classes(classes.1);
            for (&second_class, glyphs) in &seconds {
                coverage(firsts, |_, first| {
                    let class = classes.0.get(first);
                    let Some((a, b)) = matrix.get((class, second_class)) else {
                        return;
                    };
                    if values(a) == [0; 4] && values(b) == [0; 4] {
                        return;
                    }
                    for &second in glyphs {
                        add(first, second, a, b);
                    }
                });
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
        if let Some(table) = face.tables().gpos {
            let data = face
                .raw_face()
                .table(Tag::from_bytes(b"GPOS"))
                .unwrap_or(&[]);
            for lookup_index in 0..table.lookups.len() {
                let Some(lookup) = table.lookups.get(lookup_index) else {
                    continue;
                };
                for subtable_index in 0..lookup.subtables.len() {
                    let Some(subtable) = lookup.subtables.get(subtable_index) else {
                        continue;
                    };
                    if let PositioningSubtable::Single(single) = subtable {
                        coverage(single.coverage(), |i, glyph| {
                            let value = match single {
                                SingleAdjustment::Format1 { value, .. } => Some(value),
                                SingleAdjustment::Format2 { values, .. } => values.get(i),
                            };
                            if let Some(v) = value {
                                result.singles.push((
                                    lookup_index,
                                    subtable_index,
                                    AdjustmentRecord {
                                        glyph: u32::from(glyph.0),
                                        values: [
                                            v.x_placement,
                                            v.y_placement,
                                            v.x_advance,
                                            v.y_advance,
                                        ],
                                    },
                                ));
                            }
                        });
                        continue;
                    }
                    let header = mark_matrix(data, lookup_index, subtable_index);
                    let (marks, bases, anchors, matrix, output) = match subtable {
                        PositioningSubtable::MarkToBase(t) => (
                            t.mark_coverage,
                            t.base_coverage,
                            t.marks,
                            t.anchors,
                            &mut result.mark_to_base,
                        ),
                        PositioningSubtable::MarkToMark(t) => (
                            t.mark1_coverage,
                            t.mark2_coverage,
                            t.marks,
                            t.mark2_matrix,
                            &mut result.mark_to_mark,
                        ),
                        _ => continue,
                    };
                    coverage(marks, |mark_index, mark| {
                        let Some((class, mark_anchor)) = anchors.get(mark_index) else {
                            return;
                        };
                        if class >= matrix.cols {
                            return;
                        }
                        coverage(bases, |base_index, base| {
                            let (base_anchor, missing_anchor) = match matrix.get(base_index, class)
                            {
                                Some(a) => ([a.x, a.y], false),
                                None => match header
                                    .and_then(|h| null_anchor(h, base_index, class, matrix.cols))
                                {
                                    Some(a) => (a, true),
                                    None => return,
                                },
                            };
                            output.push((
                                lookup_index,
                                subtable_index,
                                MarkRecord {
                                    base: u32::from(base.0),
                                    base_anchor,
                                    mark: u32::from(mark.0),
                                    mark_anchor: [mark_anchor.x, mark_anchor.y],
                                    missing_anchor,
                                },
                            ));
                        });
                    });
                }
            }
        }
        result
    }
}

impl FontFace {
    /// Ordered GPOS pair records in font units. Zero terminates a filtered glyph list.
    pub fn pair_adjustments(&self, glyphs: Option<&[u32]>, lookup: Option<i32>) -> Vec<PairRecord> {
        let features = self
            .features
            .get_or_init(|| FontFeatures::read(&self.face()));
        let pairs = features.pairs.get_or_init(|| {
            let face = self.face();
            let mut records = Vec::new();
            if let Some(table) = face.tables().gpos {
                let data = face
                    .raw_face()
                    .table(Tag::from_bytes(b"GPOS"))
                    .unwrap_or(&[]);
                for lookup_index in 0..table.lookups.len() {
                    let Some(lookup) = table.lookups.get(lookup_index) else {
                        continue;
                    };
                    for subtable_index in 0..lookup.subtables.len() {
                        if let Some(PositioningSubtable::Pair(pair)) =
                            lookup.subtables.get(subtable_index)
                        {
                            read_pairs(data, lookup_index, subtable_index, pair, &mut records);
                        }
                    }
                }
            }
            records
        });
        select(pairs, glyphs, lookup, true, |r| r.first, |_| true)
    }
    /// All GSUB ligature records, or records starting with the requested glyphs. A zero glyph
    /// terminates a filtered input, as in TextCore's glyph list queries. No size scaling applies.
    pub fn ligatures(&self, glyphs: Option<&[u32]>, lookup: Option<i32>) -> Vec<LigatureRecord> {
        let features = self
            .features
            .get_or_init(|| FontFeatures::read(&self.face()));
        select(
            &features.ligatures,
            glyphs,
            lookup,
            true,
            |r| r.components[0],
            |_| true,
        )
    }

    /// Single-position adjustments in font units, including explicitly stored zero values.
    pub fn single_adjustments(&self, glyphs: &[u32], lookup: i32) -> Vec<AdjustmentRecord> {
        let features = self
            .features
            .get_or_init(|| FontFeatures::read(&self.face()));
        select(
            &features.singles,
            Some(glyphs),
            Some(lookup),
            true,
            |r| r.glyph,
            |_| true,
        )
    }

    /// GPOS mark attachments. Scalar queries can select glyph zero; list queries use it as a
    /// terminator. Missing matrix anchors follow the caller's compatibility profile.
    pub fn marks(
        &self,
        glyphs: Option<&[u32]>,
        lookup: Option<i32>,
        mark_to_mark: bool,
        zero_terminated: bool,
        preserve_missing: bool,
    ) -> Vec<MarkRecord> {
        let features = self
            .features
            .get_or_init(|| FontFeatures::read(&self.face()));
        let records = if mark_to_mark {
            &features.mark_to_mark
        } else {
            &features.mark_to_base
        };
        select(
            records,
            glyphs,
            lookup,
            zero_terminated,
            |r| r.base,
            |r| preserve_missing || !r.missing_anchor,
        )
    }
}

fn select<T: Clone>(
    records: &[(u16, u16, T)],
    glyphs: Option<&[u32]>,
    lookup: Option<i32>,
    zero_terminated: bool,
    key: impl Fn(&T) -> u32,
    include: impl Fn(&T) -> bool,
) -> Vec<T> {
    let mut output = Vec::new();
    let mut start = 0;
    while start < records.len() {
        let group = (records[start].0, records[start].1);
        let end = start
            + records[start..]
                .iter()
                .take_while(|r| (r.0, r.1) == group)
                .count();
        if lookup.is_none_or(|l| l == i32::from(group.0)) {
            let group_records = &records[start..end];
            if let Some(glyphs) = glyphs {
                for glyph in glyphs.iter().take_while(|&&g| !zero_terminated || g != 0) {
                    output.extend(
                        group_records
                            .iter()
                            .filter(|r| key(&r.2) == *glyph && include(&r.2))
                            .map(|r| r.2.clone()),
                    );
                }
            } else {
                output.extend(
                    group_records
                        .iter()
                        .filter(|r| include(&r.2))
                        .map(|r| r.2.clone()),
                );
            }
        }
        start = end;
    }
    output
}

fn word(data: &[u8], at: usize) -> Option<u16> {
    Some(u16::from_be_bytes(
        data.get(at..at.checked_add(2)?)?.try_into().ok()?,
    ))
}
fn offset16(data: &[u8], at: usize) -> Option<&[u8]> {
    data.get(usize::from(word(data, at)?)..)
}

/// ttf-parser omits null matrix anchors. This bounded supplement retains the matrix header
/// coordinates needed by the measured TextCore queries (tests/projects/fontmarks).
fn mark_matrix(data: &[u8], lookup: u16, subtable: u16) -> Option<&[u8]> {
    const ANCHOR_MATRIX: usize = 10;
    offset16(positioning_subtable(data, lookup, subtable)?, ANCHOR_MATRIX)
}

fn positioning_subtable(data: &[u8], lookup: u16, subtable: u16) -> Option<&[u8]> {
    const LOOKUP_LIST: usize = 8;
    const SUBTABLE_OFFSETS: usize = 6;
    let list = offset16(data, LOOKUP_LIST)?;
    if lookup >= word(list, 0)? {
        return None;
    }
    let lookup = offset16(list, 2 + usize::from(lookup) * 2)?;
    if subtable >= word(lookup, 4)? {
        return None;
    }
    let mut table = offset16(lookup, SUBTABLE_OFFSETS + usize::from(subtable) * 2)?;
    if word(lookup, 0)? == 9 {
        let extension = u32::from_be_bytes(table.get(4..8)?.try_into().ok()?) as usize;
        table = table.get(extension..)?;
    }
    Some(table)
}
fn null_anchor(matrix: &[u8], row: u16, class: u16, classes: u16) -> Option<[i16; 2]> {
    if row >= word(matrix, 0)? || class >= classes {
        return None;
    }
    let index = usize::from(row) * usize::from(classes) + usize::from(class);
    if word(matrix, 2 + index * 2)? != 0 {
        return None;
    }
    Some([word(matrix, 2)? as i16, word(matrix, 4)? as i16])
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn pair_records_keep_both_values_subtable_order_duplicates_and_explicit_zero() {
        let face = FontFace::from_bytes(
            include_bytes!("../../../tests/projects/fontpairs/Assets/Fonts/PairOnly.ttf").to_vec(),
            0,
        )
        .unwrap();
        let id = |c| u32::from(face.glyph_index(c));
        let (a, v, t, small_a) = (id('A'), id('V'), id('T'), id('a'));
        let all = face.pair_adjustments(None, None);
        assert_eq!(all.len(), 12);
        assert_eq!(
            (all[0].first, all[0].second, all[0].first_values),
            (a, small_a, [0; 4])
        );
        assert_eq!(all[2].first_values, [10, 20, -30, 40]);
        assert_eq!(all[2].second_values, [-11, -21, 31, -41]);
        let list = face.pair_adjustments(Some(&[t, a, a]), Some(1));
        assert_eq!(
            list.iter().map(|r| (r.first, r.second)).collect::<Vec<_>>(),
            [(t, v), (a, small_a), (a, small_a), (a, v), (a, v)]
        );
        assert!(face.pair_adjustments(Some(&[a]), Some(0)).is_empty());
        assert_eq!(
            face.pair_adjustments(Some(&[a]), Some(3))[0].first_values[2],
            -72
        );
        assert!(face.pair_adjustments(Some(&[a + 65536]), None).is_empty());
        assert_eq!(
            face.pair_adjustments(Some(&[a, 0, t]), None),
            face.pair_adjustments(Some(&[a]), None)
        );
        assert!(face.legacy_kerning(13).is_empty());
    }

    #[test]
    fn pair_class_order_distinguishes_array_zero_and_explicit_zero_ranges() {
        for (bytes, count, zero_range) in [
            (
                include_bytes!("../../../tests/projects/fontpairs/Assets/Fonts/ClassZero.ttf")
                    .as_slice(),
                14,
                true,
            ),
            (
                include_bytes!("../../../tests/projects/fontpairs/Assets/Fonts/ClassRange.ttf")
                    .as_slice(),
                18,
                false,
            ),
        ] {
            let face = FontFace::from_bytes(bytes.to_vec(), 0).unwrap();
            let a = u32::from(face.glyph_index('A'));
            let zero = u32::from(face.glyph_index('o'));
            assert_eq!(face.pair_adjustments(None, None).len(), count);
            let all = face.pair_adjustments(None, Some(2));
            assert_eq!(all.iter().any(|r| r.second == zero), zero_range);
            if zero_range {
                assert_eq!(all[0].first, a);
                assert_eq!(all[0].second, zero);
                assert_eq!(all[1].second, zero);
                assert_eq!(all[0].first_values[2], -123);
            }
            let records = face.pair_adjustments(Some(&[a]), Some(2));
            assert_eq!(
                records
                    .iter()
                    .map(|r| r.first_values[2])
                    .collect::<Vec<_>>(),
                if zero_range {
                    vec![-123, -63, -63]
                } else {
                    vec![-63; 4]
                }
            );
        }
    }

    #[test]
    fn legacy_kern_is_independent_of_gpos_and_resets_its_pixel_size() {
        let face = FontFace::from_bytes(
            include_bytes!("../../../tests/projects/fontpairs/Assets/Fonts/Both.ttf").to_vec(),
            0,
        )
        .unwrap();
        let a = face.glyph_index('A');
        let v = face.glyph_index('V');
        let t = face.glyph_index('T');
        assert_eq!(face.legacy_kerning(13), [(a, v, -1.125), (t, v, 1.796875)]);
        assert_eq!(
            face.legacy_kerning(37),
            [(a, v, -3.203125), (t, v, 5.109375)]
        );
        assert_eq!(face.legacy_kerning(13), [(a, v, -1.125), (t, v, 1.796875)]);
        assert!(face.legacy_kerning(0).is_empty());
        assert_eq!(
            face.pair_adjustments(Some(&[u32::from(a)]), Some(3))[0].first_values[2],
            -72
        );
        let neither = FontFace::from_bytes(
            include_bytes!("../../../tests/projects/fontpairs/Assets/Fonts/Neither.ttf").to_vec(),
            0,
        )
        .unwrap();
        assert!(neither.legacy_kerning(13).is_empty());
        assert!(neither.pair_adjustments(None, None).is_empty());
    }

    fn font() -> FontFace {
        FontFace::from_bytes(
            include_bytes!("../../../tests/projects/fontfeatures/Assets/Fonts/Controlled.ttf")
                .to_vec(),
            0,
        )
        .unwrap()
    }

    fn mark_font() -> FontFace {
        FontFace::from_bytes(
            include_bytes!("../../../tests/projects/fontmarks/Assets/Fonts/Marks.ttf").to_vec(),
            0,
        )
        .unwrap()
    }

    #[test]
    fn single_adjustments_preserve_four_axes_extensions_and_zero_records() {
        let face = FontFace::from_bytes(
            include_bytes!("../../../tests/projects/fontsingles/Assets/Fonts/Singles.ttf").to_vec(),
            0,
        )
        .unwrap();
        let ids: Vec<u32> = ['A', 'f', 'i', 'l', 't']
            .into_iter()
            .map(|c| u32::from(face.glyph_index(c)))
            .collect();
        let records = face.single_adjustments(&ids, 0);
        assert_eq!(records.len(), 2);
        assert_eq!(records[0].values, [11, -12, 13, -14]);
        assert_eq!(records[1].values, [-21, 22, -23, 24]);
        let shared = face.single_adjustments(&ids, 1);
        assert_eq!(shared.len(), 2);
        assert_eq!(shared[0].values, [7, 8, 9, 10]);
        assert_eq!(shared[0].values, shared[1].values);
        let zero = face.single_adjustments(&ids, 2);
        assert_eq!(zero.len(), 1);
        assert_eq!(zero[0].glyph, ids[4]);
        assert_eq!(zero[0].values, [0; 4]);
        assert!(face.single_adjustments(&ids, 3).is_empty());
        assert_eq!(
            face.single_adjustments(&[ids[0], ids[0], ids[1], 0, ids[0]], 0)
                .len(),
            3
        );
        assert!(face.single_adjustments(&[ids[0] + 65536], 0).is_empty());
        assert!(face.single_adjustments(&ids, -1).is_empty());
    }

    #[test]
    fn mark_anchors_preserve_units_lookup_order_and_nullable_classes() {
        let face = mark_font();
        let records = face.marks(None, None, false, false, true);
        assert_eq!(records.len(), 8);
        assert_eq!(records[0].base_anchor, [300, 700]);
        assert_eq!(records[0].mark_anchor, [20, 120]);
        assert_eq!(records[5].base_anchor, [10, 16]);
        assert!(records[5].missing_anchor);
        assert_eq!(records[6].base_anchor, [333, 777]);
        assert_eq!(face.marks(None, None, false, false, false).len(), 7);
        assert_eq!(face.marks(None, Some(0), false, false, true).len(), 6);
        assert_eq!(face.marks(None, Some(1), false, false, true).len(), 2);
        assert!(face.marks(None, Some(2), false, false, true).is_empty());
        let stacked = face.marks(None, None, true, false, true);
        assert_eq!(stacked.len(), 4);
        assert_eq!(stacked[0].base_anchor, [30, 400]);
        assert_eq!(stacked[1].base_anchor, [-20, 350]);
    }

    #[test]
    fn mark_lists_repeat_bases_and_reject_wide_indices_without_aliasing() {
        let face = mark_font();
        let f = u32::from(face.glyph_index('f'));
        let records = face.marks(Some(&[f, f]), None, false, true, true);
        assert_eq!(records.len(), 6);
        assert_eq!(&records[..3], &records[3..]);
        assert_eq!(
            face.marks(Some(&[f, 0, f]), None, false, true, true).len(),
            3
        );
        assert!(
            face.marks(Some(&[f + 65536]), None, false, true, true)
                .is_empty()
        );
        assert!(face.marks(Some(&[]), None, true, true, true).is_empty());
    }

    #[test]
    fn scalar_selection_can_query_zero_and_malformed_headers_are_ignored() {
        let records = [(0, 0, 0u32), (0, 0, 1u32)];
        assert_eq!(
            select(&records, Some(&[0]), None, false, |&r| r, |_| true),
            [0]
        );
        assert!(select(&records, Some(&[0, 1]), None, true, |&r| r, |_| true).is_empty());
        assert_eq!(null_anchor(&[], 0, 0, 1), None);
        assert_eq!(null_anchor(&[0, 1, 0, 0], 0, 0, 1), None);
        assert_eq!(null_anchor(&[0, 1, 0, 4, 0, 0], 0, 0, 1), None);
        assert_eq!(mark_matrix(&[0; 12], 0, 0), None);
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
