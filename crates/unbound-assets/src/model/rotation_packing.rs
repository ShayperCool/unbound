//! Rotation curves as a Legacy clip stores them under Keyframe Reduction and Compression
//! (matches the Unity 6000.0.71f1 Editor, tests/projects/modelaxis Reduced: `Packed*` and
//! `LegacyOptimal`; the keys `AnimationUtility.GetEditorCurve` returns for those clips agree to
//! 5e-9 in value and exactly in time). The clip keeps each rotation curve in a packed form and
//! plays what unpacking gives back, which is not what was packed:
//!
//! - **Times** are whole hundredths of a second, counted from zero (or from the first key when
//!   it lies before zero) and cut off, not rounded.
//! - **Rotations** keep three components, each cut off to 10 bits (9 for the one after the
//!   largest) over -1..1; the largest in magnitude is rebuilt from the others with its sign.
//! - **Slopes** are rounded to 6 bits over the range all of the curve's slope components span;
//!   a curve whose keys all have equal incoming and outgoing slopes stores one of them.

use super::Key;
use super::curves::Slopes;

/// Seconds per stored time step, and steps per second.
const TIME_STEP: f64 = 0.01;
const TIME_STEPS_PER_SECOND: f32 = 100.0;
/// The bits of a stored rotation component: the component after the rebuilt one has one less.
const COMPONENT_BITS: u32 = 10;
/// The bits of a stored slope component.
const SLOPE_BITS: u32 = 6;

/// A key time as it is read back.
fn stored_time(time: f32, first: f32) -> f32 {
    let steps = ((time - first) * TIME_STEPS_PER_SECOND) as i64 as i32;
    (f64::from(steps) * TIME_STEP) as f32
}

/// A rotation as it is read back.
fn stored_rotation(q: [f32; 4]) -> [f32; 4] {
    // The largest component in magnitude: a later one replaces an earlier only when larger.
    let mut largest = 0;
    for i in 1..4 {
        if q[i].abs() > q[largest].abs() {
            largest = i;
        }
    }
    let negative = q[largest] < 0.0;
    let mut out = [0f32; 4];
    let mut squares = 0f32;
    for i in (0..4).filter(|&i| i != largest) {
        let bits = if i == (largest + 1) % 4 {
            COMPONENT_BITS - 1
        } else {
            COMPONENT_BITS
        };
        let steps = (1u32 << bits) - 1;
        let unit = ((q[i] + 1.0) * 0.5).max(0.0).min(1.0);
        let stored = (steps as f32 * unit) as i64 as u32 & steps;
        let value = (f64::from(stored) / (f64::from(steps) * 0.5) - 1.0) as f32;
        out[i] = value;
        squares += value * value;
    }
    // Never negative for a unit rotation; anything else rebuilds zero instead of NaN.
    let rebuilt = (1.0 - squares).max(0.0).sqrt();
    out[largest] = if negative { -rebuilt } else { rebuilt };
    out
}

/// The curve's slopes as they are read back.
fn stored_slopes(slopes: &Slopes<4>) -> Slopes<4> {
    let one_sided = slopes
        .iter()
        .all(|(incoming, outgoing)| incoming == outgoing);
    let stored = |part: fn(&([f32; 4], [f32; 4])) -> &[f32; 4]| slopes.iter().flat_map(part);
    let incoming: fn(&([f32; 4], [f32; 4])) -> &[f32; 4] = |s| &s.0;
    let outgoing: fn(&([f32; 4], [f32; 4])) -> &[f32; 4] = |s| &s.1;
    let (mut low, mut high) = (f32::INFINITY, f32::NEG_INFINITY);
    for &v in stored(incoming).chain(stored(outgoing).filter(|_| !one_sided)) {
        low = low.min(v);
        high = high.max(v);
    }
    let range = high - low;
    let steps = ((1u32 << SLOPE_BITS) - 1) as f32;
    let scale = if range > 0.0 { 1.0 / range } else { 0.0 };
    let read = |v: f32| {
        if range == 0.0 {
            // No bits are stored: every slope is the common value.
            return low;
        }
        let unit = ((v - low) * scale).max(0.0).min(1.0);
        let stored = ((unit * steps + 0.5) as i64 as u32).min(steps as u32);
        low + stored as f32 * (range / steps)
    };
    slopes
        .iter()
        .map(|(incoming, outgoing)| {
            let incoming = incoming.map(read);
            (
                incoming,
                if one_sided {
                    incoming
                } else {
                    outgoing.map(read)
                },
            )
        })
        .collect()
}

/// The keys and slopes a packed rotation curve plays.
pub fn stored(keys: &[Key<4>], slopes: &Slopes<4>) -> (Vec<Key<4>>, Slopes<4>) {
    if keys.is_empty() {
        return (Vec::new(), Vec::new());
    }
    let first = keys.iter().map(|k| k.time).fold(0.0f32, f32::min);
    let keys = keys
        .iter()
        .map(|k| Key {
            time: stored_time(k.time, first),
            value: stored_rotation(k.value),
        })
        .collect();
    (keys, stored_slopes(slopes))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn times_are_cut_to_hundredths() {
        assert_eq!(stored_time(0.0, 0.0), 0.0);
        assert_eq!(stored_time(1.0 / 30.0, 0.0), 0.03);
        assert_eq!(stored_time(0.0199, 0.0), 0.01);
        assert_eq!(stored_time(2.0, 0.0), 2.0);
        // Counted from a first key before zero.
        assert_eq!(stored_time(-0.25, -0.5), 0.25);
    }

    #[test]
    fn rotations_lose_what_ten_bits_cannot_hold() {
        // The identity survives: zero is not on the 10-bit grid, the rebuilt w absorbs it.
        let identity = stored_rotation([0.0, 0.0, 0.0, 1.0]);
        assert!(identity[3] > 0.999_99 && identity[0].abs() < 2.0 / 511.0);
        // The sign of the largest component is kept, the result is a unit quaternion.
        for q in [
            [0.5f32, -0.5, 0.5, -0.5],
            [-0.9, 0.1, 0.3, 0.3],
            [0.1, 0.2, -0.95, 0.2],
            [0.6, 0.0, 0.0, -0.8],
        ] {
            let length = q.iter().map(|c| c * c).sum::<f32>().sqrt();
            let q = q.map(|c| c / length);
            let out = stored_rotation(q);
            let norm: f32 = out.iter().map(|c| c * c).sum();
            assert!((norm - 1.0).abs() < 1e-6, "{out:?}");
            for i in 0..4 {
                assert!((out[i] - q[i]).abs() < 0.006, "{q:?} -> {out:?}");
                assert!(
                    out[i] <= q[i] + 0.004,
                    "components are cut off, not rounded"
                );
            }
        }
        // Equal magnitudes: the first is the rebuilt one.
        let tie = stored_rotation([0.5, 0.5, 0.5, 0.5]);
        assert!(tie[1] < 0.5 && tie[0] > 0.5);
    }

    /// The Spine rotation of tests/projects/modelaxis Reduced `Drift` (reduced only) and
    /// `PackedDrift` (the same clip packed), as `AnimationUtility.GetEditorCurve` returns their
    /// keys in the Unity 6000.0.71f1 Editor: time, value, slope.
    #[test]
    fn a_reduced_curve_packs_to_the_keys_the_editor_plays() {
        type Row = (f32, [f32; 4], [f32; 4]);
        let reduced: [Row; 3] = [
            (
                0.0,
                [0.0, 0.0, 0.1736482, 0.9848078],
                [0.0286757927, 0.00488447445, -0.171971157, 0.0297921877],
            ),
            (
                1.63333344,
                [0.04720401, -0.005239171, -0.110188089, 0.9927754],
                [0.0279637314, -0.0114437882, -0.173118532, -0.0206038542],
            ),
            (
                3.00000024,
                [0.0818996057, -0.02980902, -0.340718657, 0.9361168],
                [0.0221275743, -0.0240619518, -0.162704736, -0.0613654256],
            ),
        ];
        let packed: [Row; 3] = [
            (
                0.0,
                [-0.00195694715, -0.0009775171, 0.173020527, 0.9849158],
                [0.02979219, 0.004025749, -0.173118532, 0.02979219],
            ),
            (
                1.63,
                [0.0450097844, -0.00684261974, -0.110459432, 0.992837369],
                [0.0265713837, -0.0120782768, -0.173118532, -0.0217406917],
            ),
            (
                3.0,
                [0.08023483, -0.0303030312, -0.341153473, 0.9360867],
                [0.02335058, -0.0249614976, -0.163456112, -0.0603903532],
            ),
        ];
        let keys: Vec<Key<4>> = reduced
            .iter()
            .map(|&(time, value, _)| Key { time, value })
            .collect();
        let slopes: Slopes<4> = reduced.iter().map(|&(_, _, s)| (s, s)).collect();
        let (keys, slopes) = stored(&keys, &slopes);
        for (i, &(time, value, slope)) in packed.iter().enumerate() {
            assert_eq!(keys[i].time, time);
            for c in 0..4 {
                // The printed keys carry nine digits; the rebuilt component is a square root.
                assert!((keys[i].value[c] - value[c]).abs() <= 6e-8, "key {i}");
                assert!((slopes[i].0[c] - slope[c]).abs() <= 2e-8, "key {i}");
            }
            assert_eq!(slopes[i].0, slopes[i].1);
        }
    }

    #[test]
    fn odd_input_stays_finite() {
        // Not a unit rotation: the stored components alone exceed unit length.
        let out = stored_rotation([1.0, 1.0, 1.0, 1.0]);
        assert!(out.iter().all(|c| c.is_finite()), "{out:?}");
        // A curve of one key, and none.
        let one = [Key {
            time: 0.5,
            value: [0.0, 0.0, 0.0, 1.0],
        }];
        let (keys, slopes) = stored(&one, &vec![([0.0; 4], [0.0; 4])]);
        assert_eq!((keys.len(), slopes.len()), (1, 1));
        assert_eq!(keys[0].time, 0.5);
        assert!(stored(&[], &Vec::new()).0.is_empty());
        // The largest component negative (past half a turn): its sign survives.
        let turned = stored_rotation([0.1, 0.2, 0.1, -0.969_536]);
        assert!(turned[3] < -0.96);
    }

    #[test]
    fn slopes_round_to_sixty_four_levels_of_their_range() {
        let slopes: Slopes<4> = vec![
            ([0.0, 1.0, -1.0, 0.5], [0.0, 1.0, -1.0, 0.5]),
            ([0.26, 0.0, 0.0, 0.0], [0.26, 0.0, 0.0, 0.0]),
        ];
        let out = stored_slopes(&slopes);
        let step = 2.0 / 63.0;
        assert_eq!(out[0].0[1], 1.0);
        assert_eq!(out[0].0[2], -1.0);
        for (a, b) in out.iter().zip(&slopes) {
            assert_eq!(a.0, a.1);
            for i in 0..4 {
                assert!((a.0[i] - b.0[i]).abs() <= step / 2.0 + 1e-6);
            }
        }
        // All slopes equal: nothing is lost.
        let flat: Slopes<4> = vec![([0.25; 4], [0.25; 4]); 3];
        assert_eq!(stored_slopes(&flat), flat);
        // Different sides are both kept and share one range.
        let sided: Slopes<4> = vec![([0.0; 4], [6.3, 0.0, 0.0, 0.0])];
        let out = stored_slopes(&sided);
        assert_eq!(out[0].1[0], 6.3);
        assert_eq!(out[0].0, [0.0; 4]);
    }
}
