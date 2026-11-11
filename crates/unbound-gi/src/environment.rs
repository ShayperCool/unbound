//! The environment precomputed realtime GI reads: six cube faces (+X, -X, +Y, -Y, +Z, -Z) of
//! `RESOLUTION` x `RESOLUTION` linear RGB values, and their coarser levels (each value the mean
//! of the 2 x 2 values under it) down to one value per face; all faces of a level, then the next
//! level. Matches the Unity 6000.0.71f1 Editor, tests/projects/realtimegi (`room-gradient`,
//! `room-sky-only`).
use crate::data::ENVIRONMENT_VALUES;
use anyhow::{Result, ensure};

/// Values along a face's side at the finest level.
pub const RESOLUTION: usize = 8;
/// Values of the finest level: six faces.
pub const FACE_VALUES: usize = 6 * RESOLUTION * RESOLUTION;

/// The environment values of `faces` (six faces, each row by row, `FACE_VALUES` in all) into
/// `out` (`ENVIRONMENT_VALUES`).
pub fn environment_values(faces: &[[f32; 3]], out: &mut [[f32; 3]]) -> Result<()> {
    ensure!(
        faces.len() == FACE_VALUES && out.len() == ENVIRONMENT_VALUES,
        "an environment of {} face values for {} values",
        faces.len(),
        out.len()
    );
    out[..FACE_VALUES].copy_from_slice(faces);
    let (mut size, mut at) = (RESOLUTION, 0);
    while size > 1 {
        let next = at + 6 * size * size;
        let half = size / 2;
        for face in 0..6 {
            for y in 0..half {
                for x in 0..half {
                    let mut sum = [0.0f32; 3];
                    for (dx, dy) in [(0, 0), (1, 0), (0, 1), (1, 1)] {
                        let v = out[at + face * size * size + (2 * y + dy) * size + 2 * x + dx];
                        for k in 0..3 {
                            sum[k] += v[k];
                        }
                    }
                    out[next + face * half * half + y * half + x] = sum.map(|v| v * 0.25);
                }
            }
        }
        at = next;
        size = half;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn levels_average_two_by_two_per_face() {
        let faces: Vec<[f32; 3]> = (0..FACE_VALUES)
            .map(|i| [(i / 64) as f32, (i % 64) as f32, 1.0])
            .collect();
        let mut out = vec![[0.0; 3]; ENVIRONMENT_VALUES];
        environment_values(&faces, &mut out).unwrap();
        assert_eq!(out[..FACE_VALUES], faces[..]);
        // Level 4x4 of face 2: its first value is the mean of face 2's values 0, 1, 8, 9.
        assert_eq!(out[FACE_VALUES + 2 * 16], [2.0, 4.5, 1.0]);
        // The last six values: one per face, its mean.
        for face in 0..6 {
            assert_eq!(out[ENVIRONMENT_VALUES - 6 + face], [face as f32, 31.5, 1.0]);
        }
        assert!(environment_values(&faces[1..], &mut out).is_err());
    }
}
