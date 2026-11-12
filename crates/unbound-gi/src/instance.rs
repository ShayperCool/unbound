//! The geometry the Unity 6000.0.71f1 Editor's precompute makes of a renderer's mesh for its
//! automatic realtime lightmap charts (tests/projects/realtimegi, RoomTurned): the mesh under the
//! instance's rotation and scale with its world rotation taken out. That is done numerically,
//! so a turned instance's positions and normals carry rounding in their last digits (a normal
//! of (4.3e-8, -1.1e-8, 1), say), and the charts depend on it; every operation below is single
//! precision in the order that gives the Editor's values to the last bit.

/// A 3 x 3 matrix, column by column: the instance's rotation and scale with its world
/// rotation taken out (`RealtimeGI.WorldScale` on the managed side).
pub type Matrix = [f32; 9];

/// The inverse of a 4 x 4 matrix (column by column) by elimination with partial pivoting on
/// its rows, or `None` when it is singular.
fn invert(m: &[f32; 16]) -> Option<[f32; 16]> {
    let mut rows = [[0.0f32; 8]; 4];
    for (r, row) in rows.iter_mut().enumerate() {
        for c in 0..4 {
            row[c] = m[4 * c + r];
        }
        row[4 + r] = 1.0;
    }
    let mut at = [0usize, 1, 2, 3];
    // Moves the row with the larger leading value up, of the pairs (3, 2), (2, 1), (1, 0)
    // from `from` on.
    let pivot = |at: &mut [usize; 4], rows: &[[f32; 8]; 4], from: usize, column: usize| {
        for r in (from + 1..4).rev() {
            if rows[at[r]][column].abs() > rows[at[r - 1]][column].abs() {
                at.swap(r, r - 1);
            }
        }
    };
    for column in 0..3 {
        pivot(&mut at, &rows, column, column);
        let lead = rows[at[column]];
        if lead[column] == 0.0 {
            return None;
        }
        let factors: Vec<f32> = (column + 1..4)
            .map(|r| rows[at[r]][column] / lead[column])
            .collect();
        for k in column + 1..8 {
            let s = lead[k];
            // (The first two eliminations skip a zero of the right-hand side; the third
            // does not.)
            if k >= 4 && s == 0.0 && column < 2 {
                continue;
            }
            for (i, r) in (column + 1..4).enumerate() {
                rows[at[r]][k] -= factors[i] * s;
            }
        }
    }
    let [r0, r1, r2, r3] = at;
    if rows[r3][3] == 0.0 {
        return None;
    }
    let s = 1.0 / rows[r3][3];
    for k in 4..8 {
        rows[r3][k] *= s;
    }
    let s = 1.0 / rows[r2][2];
    let m2 = rows[r2][3];
    for k in 4..8 {
        rows[r2][k] = (rows[r2][k] - rows[r3][k] * m2) * s;
    }
    for r in [r1, r0] {
        let m = rows[r][3];
        for k in 4..8 {
            rows[r][k] -= rows[r3][k] * m;
        }
    }
    let s = 1.0 / rows[r1][1];
    let m1 = rows[r1][2];
    for k in 4..8 {
        rows[r1][k] = (rows[r1][k] - rows[r2][k] * m1) * s;
    }
    let m0 = rows[r0][2];
    for k in 4..8 {
        rows[r0][k] -= rows[r2][k] * m0;
    }
    let s = 1.0 / rows[r0][0];
    let m0 = rows[r0][1];
    for k in 4..8 {
        rows[r0][k] = (rows[r0][k] - rows[r1][k] * m0) * s;
    }
    let mut out = [0.0; 16];
    for (row, &r) in at.iter().enumerate() {
        for c in 0..4 {
            out[4 * c + row] = rows[r][4 + c];
        }
    }
    Some(out)
}

/// The transpose of the matrix's inverse, which turns normals.
fn inverse_transpose(m: &Matrix) -> Option<Matrix> {
    let full = [
        m[0], m[1], m[2], 0.0, m[3], m[4], m[5], 0.0, m[6], m[7], m[8], 0.0, 0.0, 0.0, 0.0, 1.0,
    ];
    let i = invert(&full)?;
    Some([i[0], i[4], i[8], i[1], i[5], i[9], i[2], i[6], i[10]])
}

/// The mesh's positions and normals under the matrix: positions and normals (turned by the
/// inverse transpose) summed as ((x + y) + z) terms, normals divided by their length (its
/// square summed as (y + x) + z). `None` when the matrix is singular or a normal has no
/// length (not measured).
pub fn apply(
    m: &Matrix,
    positions: &[[f32; 3]],
    normals: &[[f32; 3]],
) -> Option<(Vec<[f32; 3]>, Vec<[f32; 3]>)> {
    let t = inverse_transpose(m)?;
    let placed = positions
        .iter()
        .map(|&[x, y, z]| [0, 1, 2].map(|r| ((m[r] * x + m[3 + r] * y) + m[6 + r] * z) + 0.0))
        .collect();
    let mut turned = Vec::with_capacity(normals.len());
    for &[x, y, z] in normals {
        let n = [0, 1, 2].map(|r| (t[r] * x + t[3 + r] * y) + t[6 + r] * z);
        let length = ((n[1] * n[1] + n[0] * n[0]) + n[2] * n[2]).sqrt();
        if !(length > 0.0) {
            return None;
        }
        turned.push(n.map(|c| c / length));
    }
    Some((placed, turned))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn bits(v: [f32; 3]) -> [u32; 3] {
        v.map(f32::to_bits)
    }

    /// Four corners of Unity's cube (with their faces' normals) under the matrices of two
    /// turned renderers of tests/projects/realtimegi RoomTurned, against the Editor's cached
    /// precompute input for them, bit for bit.
    #[test]
    fn turned_instances_are_the_editors_to_the_last_bit() {
        let corners = [
            ([0.5, -0.5, 0.5], [0.0, 0.0, 1.0]),
            ([-0.5, 0.5, -0.5], [0.0, 1.0, 0.0]),
            ([-0.5, 0.5, 0.5], [-1.0, 0.0, 0.0]),
            ([0.5, 0.5, 0.5], [1.0, 0.0, 0.0]),
        ];
        let positions: Vec<[f32; 3]> = corners.iter().map(|c| c.0).collect();
        let normals: Vec<[f32; 3]> = corners.iter().map(|c| c.1).collect();
        let matrix = |m: [u32; 9]| m.map(f32::from_bits);
        // BoxTurned (turned 20, 40, 10; scaled 0.7, 0.5, 0.6), and Child (turned 10 about X,
        // scaled 0.5, 0.3, 0.4, under a parent turned 45 about Y and scaled 1.5).
        let cases = [
            (
                matrix([
                    0x3f333333, 0x32400000, 0xb3000000, 0, 0x3f000000, 0x31c00000, 0xb3000000,
                    0xb1800000, 0x3f19999a,
                ]),
                [
                    [
                        [0x3eb33332, 0xbe800000, 0x3e999999],
                        [0x3336db6e, 0xb2400000, 0x3f800000],
                    ],
                    [
                        [0xbeb33332, 0x3e800000, 0xbe999999],
                        [0xb2892492, 0x3f800000, 0x31d55553],
                    ],
                    [
                        [0xbeb33334, 0x3e800000, 0x3e99999b],
                        [0xbf800000, 0x26200000, 0xb3555555],
                    ],
                    [
                        [0x3eb33332, 0x3e800000, 0x3e99999a],
                        [0x3f800000, 0xa6200000, 0x33555555],
                    ],
                ],
            ),
            (
                matrix([
                    0x3f400000, 0x32000000, 0, 0xb2000000, 0x3ee66667, 0x31800000, 0, 0, 0x3f19999a,
                ]),
                [
                    [
                        [0x3ec00000, 0xbe666667, 0x3e99999a],
                        [0x24bda12f, 0xb20e38e3, 0x3f800000],
                    ],
                    [
                        [0xbec00000, 0x3e666667, 0xbe99999a],
                        [0xb22aaaab, 0x3f800000, 0],
                    ],
                    [
                        [0xbec00000, 0x3e666667, 0x3e99999a],
                        [0xbf800000, 0xb28e38e3, 0],
                    ],
                    [
                        [0x3ec00000, 0x3e666667, 0x3e99999a],
                        [0x3f800000, 0x328e38e3, 0],
                    ],
                ],
            ),
        ];
        for (m, expected) in cases {
            let (placed, turned) = apply(&m, &positions, &normals).unwrap();
            for (i, [p, n]) in expected.iter().enumerate() {
                assert_eq!(bits(placed[i]), *p, "position {i}");
                assert_eq!(bits(turned[i]), *n, "normal {i}");
            }
        }
    }

    #[test]
    fn an_instance_that_is_not_turned_is_scaled() {
        let m = [2.0, 0.0, 0.0, 0.0, 3.0, 0.0, 0.0, 0.0, 0.5];
        let (placed, turned) = apply(&m, &[[1.0, -1.0, 2.0]], &[[0.6, 0.8, 0.0]]).unwrap();
        assert_eq!(placed, [[2.0, -3.0, 1.0]]);
        // The normal over the scale, as its inverse transpose turns it, then unit length.
        let n = [0.6f32 * 0.5, 0.8 * (1.0 / 3.0), 0.0];
        let length = (n[1] * n[1] + n[0] * n[0]).sqrt();
        assert_eq!(turned, [[n[0] / length, n[1] / length, 0.0]]);
        // A singular matrix and a normal of no length are not made.
        assert!(apply(&[0.0; 9], &[[0.0; 3]], &[[0.0, 1.0, 0.0]]).is_none());
        assert!(apply(&m, &[[0.0; 3]], &[[0.0; 3]]).is_none());
    }
}
