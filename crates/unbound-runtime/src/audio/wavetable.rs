//! The quarter-wave cosine table of the measured mixer Chorus, Flange and Pitch Shifter
//! (Unity 6000.0.71f1; tests/projects/audiochorus, tests/projects/audioflange,
//! tests/projects/audiopitch): `QUARTER` single-precision entries over a quarter turn, read
//! without interpolation at reflected endpoint indices. One table serves every instance.
use std::sync::OnceLock;

/// Entries per quarter turn.
pub(crate) const QUARTER: usize = 8192;
/// Table indices per whole turn.
const TURN: usize = 4 * QUARTER;

/// `cos(i / QUARTER * pi / 2)` for `i` in `0..QUARTER`.
pub(crate) fn table() -> &'static [f32] {
    static TABLE: OnceLock<Box<[f32]>> = OnceLock::new();
    TABLE.get_or_init(|| {
        (0..QUARTER)
            .map(|i| ((i as f32 / QUARTER as f32) * std::f32::consts::FRAC_PI_2).cos())
            .collect()
    })
}

/// `cos(2 pi turns)` from the table. The index truncates toward zero and wraps over a whole
/// turn; the second and third quarters reflect at `2 * QUARTER - 1` and `TURN - 1`.
pub(crate) fn cosine(turns: f32) -> f32 {
    let table = table();
    let index = ((turns * TURN as f32) as i32).unsigned_abs() as usize & (TURN - 1);
    match index / QUARTER {
        0 => table[index],
        1 => -table[2 * QUARTER - 1 - index],
        2 => -table[index - 2 * QUARTER],
        _ => table[TURN - 1 - index],
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn both_former_table_formulas_give_the_same_entries() {
        // The Pitch Shifter built its own table as `i * (pi / 2) * (1 / 8192)`; scaling by a
        // power of two commutes with rounding, so the shared table is bit-identical to it.
        for (i, entry) in table().iter().enumerate() {
            let pitch = (i as f32 * std::f32::consts::FRAC_PI_2 * (1.0 / 8192.0)).cos();
            assert_eq!(entry.to_bits(), pitch.to_bits(), "entry {i}");
        }
    }

    #[test]
    fn lookup_reflects_each_quarter_and_wraps_whole_turns() {
        assert_eq!(cosine(0.0), 1.0);
        assert_eq!(cosine(1.0), 1.0);
        assert_eq!(cosine(-0.25), cosine(0.25));
        assert_eq!(cosine(0.25), -table()[QUARTER - 1]);
        assert_eq!(cosine(0.5), -table()[0]);
        assert_eq!(cosine(0.75), table()[QUARTER - 1]);
        for turns in [0.1_f32, 0.3, 0.6, 0.9] {
            assert!((cosine(turns) - (turns * std::f32::consts::TAU).cos()).abs() < 1e-3);
        }
    }
}
