//! The tangents of imported clip curves as the clip documents store them, their evaluation (so
//! the importer samples a clip, for Legacy skinned bounds, with the curves the engine plays), and
//! the keyframe reduction of the reducing animation compressions.
use super::Key;

/// Per key, its (incoming, outgoing) slope.
pub type Slopes<const N: usize> = Vec<([f32; N], [f32; N])>;

/// Per key, its incoming and outgoing slope: the slope to each neighbouring key (a curve end
/// takes the other side's). `smooth`: inner keys take the slope between their neighbours on
/// both sides (see [`super::ModelClip::smooth`]).
pub fn key_slopes<const N: usize>(keys: &[Key<N>], smooth: bool) -> Vec<([f32; N], [f32; N])> {
    let slope = |a: &Key<N>, b: &Key<N>| {
        let dt = b.time - a.time;
        let mut out = [0f32; N];
        if dt > 0.0 {
            for i in 0..N {
                out[i] = (b.value[i] - a.value[i]) / dt;
            }
        }
        out
    };
    (0..keys.len())
        .map(|i| {
            let incoming = (i > 0).then(|| slope(&keys[i - 1], &keys[i]));
            let outgoing = (i + 1 < keys.len()).then(|| slope(&keys[i], &keys[i + 1]));
            match (incoming, outgoing) {
                (Some(_), Some(_)) if smooth => {
                    let both = slope(&keys[i - 1], &keys[i + 1]);
                    (both, both)
                }
                (Some(a), Some(b)) => (a, b),
                (Some(a), None) => (a, a),
                (None, Some(b)) => (b, b),
                (None, None) => ([0.0; N], [0.0; N]),
            }
        })
        .collect()
}

/// Rotation keys with each key in the hemisphere of the previous one, so slopes do not jump.
pub fn continuous_rotations(keys: &[Key<4>]) -> Vec<Key<4>> {
    let mut keys = keys.to_vec();
    for i in 1..keys.len() {
        let dot: f32 = (0..4)
            .map(|c| keys[i].value[c] * keys[i - 1].value[c])
            .sum();
        if dot < 0.0 {
            keys[i].value = keys[i].value.map(|v| -v);
        }
    }
    keys
}

/// The curve's value at `time`, clamped to its first and last keys; cubic Hermite between keys
/// with the slopes of [`key_slopes`] (an AnimationCurve with unweighted tangents).
pub fn evaluate<const N: usize>(
    keys: &[Key<N>],
    slopes: &[([f32; N], [f32; N])],
    time: f32,
) -> [f32; N] {
    let (Some(first), Some(last)) = (keys.first(), keys.last()) else {
        return [0.0; N];
    };
    if time <= first.time {
        return first.value;
    }
    if time >= last.time {
        return last.value;
    }
    let i = keys.partition_point(|k| k.time <= time).max(1) - 1;
    hermite(&keys[i], &slopes[i].1, &keys[i + 1], &slopes[i + 1].0, time)
}

/// The cubic Hermite segment from `a` (leaving with slope `out`) to `b` (arriving with `into`).
fn hermite<const N: usize>(
    a: &Key<N>,
    out: &[f32; N],
    b: &Key<N>,
    into: &[f32; N],
    time: f32,
) -> [f32; N] {
    let dt = b.time - a.time;
    let s = (time - a.time) / dt;
    let (s2, s3) = (s * s, s * s * s);
    let h00 = 2.0 * s3 - 3.0 * s2 + 1.0;
    let h10 = s3 - 2.0 * s2 + s;
    let h01 = -2.0 * s3 + 3.0 * s2;
    let h11 = s3 - s2;
    std::array::from_fn(|c| {
        h00 * a.value[c] + h10 * out[c] * dt + h01 * b.value[c] + h11 * into[c] * dt
    })
}

/// Unity's keyframe reduction (Animation Compression Keyframe Reduction and Optimal) of a curve
/// keyed on every frame (`step` = 1 / sample rate) with its slopes; `within(curve, segment)`
/// says whether a value of the reduced segment is close enough to the curve's.
///
/// A curve the segment from its first to its last key (both with zero slopes) holds everywhere
/// becomes those two keys. Otherwise, walking from the first key, a key is dropped while the
/// segment from the last kept key to the next one, with their own slopes, holds the curve at
/// every `step` from its start, at every dropped key and halfway between consecutive ones, and
/// spans less than [`MAX_REDUCED_FRAMES`] steps. Kept keys keep their slopes.
pub fn reduce<const N: usize>(
    keys: &[Key<N>],
    slopes: &[([f32; N], [f32; N])],
    step: f32,
    within: impl Fn(&[f32; N], &[f32; N]) -> bool,
) -> (Vec<Key<N>>, Slopes<N>) {
    let n = keys.len();
    if n < 3 || slopes.len() != n || !(step > 0.0) {
        return (keys.to_vec(), slopes.to_vec());
    }
    // The segment from key `a` leaving with `out` to key `b` arriving with `into` against the
    // curve; `between`: the dropped keys' indices; `limited`: the span limit applies.
    let holds = |a: &Key<N>,
                 out: &[f32; N],
                 b: &Key<N>,
                 into: &[f32; N],
                 between: std::ops::Range<usize>,
                 limited: bool| {
        let check = |t: f32| within(&evaluate(keys, slopes, t), &hermite(a, out, b, into, t));
        let span = b.time - a.time;
        if limited && !(span < step * MAX_REDUCED_FRAMES) {
            return false;
        }
        let frames = (span / step) as i32;
        if !(0..=frames).all(|i| check(i as f32 * step + a.time)) {
            return false;
        }
        let mut previous = a.time;
        for k in between {
            let t = keys[k].time;
            if !check(t) || !check((previous + t) * 0.5) {
                return false;
            }
            previous = t;
        }
        check((previous + b.time) * 0.5)
    };
    let zero = [0.0; N];
    let (first, last) = (&keys[0], &keys[n - 1]);
    if holds(first, &zero, last, &zero, 1..n - 1, false) {
        return (vec![first.clone(), last.clone()], vec![(zero, zero); 2]);
    }
    let (mut out, mut out_slopes) = (vec![first.clone()], vec![slopes[0]]);
    let mut kept = 0;
    for i in 1..n - 1 {
        let next = i + 1;
        if !holds(
            &keys[kept],
            &slopes[kept].1,
            &keys[next],
            &slopes[next].0,
            kept + 1..next,
            true,
        ) {
            out.push(keys[i].clone());
            out_slopes.push(slopes[i]);
            kept = i;
        }
    }
    out.push(last.clone());
    out_slopes.push(slopes[n - 1]);
    (out, out_slopes)
}

/// A reduced segment spans fewer frames than this (whole-curve reduction aside).
pub const MAX_REDUCED_FRAMES: f32 = 50.0;

/// Below this (times the error) a value counts as zero.
const ZERO_TOLERANCE: f32 = 1e-5;

/// Positions and scales: each component, and the vector's length, within `error` (a fraction:
/// the importer's percentage / 100) of the curve's, values near zero aside.
pub fn vector_within(error: f32) -> impl Fn(&[f32; 3], &[f32; 3]) -> bool {
    move |curve, segment| {
        let zero = ZERO_TOLERANCE * error;
        let length2 = |v: &[f32; 3]| v[0] * v[0] + v[1] * v[1] + v[2] * v[2];
        let d = [
            curve[0] - segment[0],
            curve[1] - segment[1],
            curve[2] - segment[2],
        ];
        let whole = (length2(curve) <= zero * zero && length2(segment) <= zero * zero)
            || length2(&d) <= error * error * length2(curve);
        whole
            && (0..3).all(|c| {
                (curve[c].abs() <= zero && segment[c].abs() <= zero)
                    || d[c].abs() <= curve[c].abs() * error
            })
    }
}

/// Floats (blend shape weights): within `error` (a fraction) of the curve's, near zero aside.
pub fn float_within(error: f32) -> impl Fn(&[f32; 1], &[f32; 1]) -> bool {
    move |curve, segment| {
        let zero = ZERO_TOLERANCE * error;
        (curve[0].abs() <= zero && segment[0].abs() <= zero)
            || (curve[0] - segment[0]).abs() <= curve[0].abs() * error
    }
}

/// How far a reduced segment's quaternion may be from unit length.
const UNIT_TOLERANCE: f32 = 1e-3;

/// Rotations: the segment's quaternion near unit length and within `degrees` of the curve's
/// (the dot product of the normalized pair at least cos(degrees / 2)).
pub fn rotation_within(degrees: f32) -> impl Fn(&[f32; 4], &[f32; 4]) -> bool {
    let cos_half = ((degrees / 360.0 + degrees / 360.0) * std::f32::consts::PI * 0.5).cos();
    move |curve, segment| {
        let length = |q: &[f32; 4]| (q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]).sqrt();
        let segment_length = length(segment);
        if (1.0 - segment_length).abs() > UNIT_TOLERANCE {
            return false;
        }
        let curve_length = length(curve);
        let curve = if curve_length >= ZERO_TOLERANCE {
            curve.map(|c| c / curve_length)
        } else {
            [0.0, 0.0, 0.0, 1.0]
        };
        let dot: f32 = (0..4)
            .map(|c| curve[c] * (segment[c] / segment_length))
            .sum();
        cos_half <= dot
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn key(time: f32, v: f32) -> Key<1> {
        Key { time, value: [v] }
    }

    #[test]
    fn slopes_take_the_neighbours() {
        let keys = [key(0.0, 0.0), key(1.0, 2.0), key(2.0, 2.0)];
        let linear = key_slopes(&keys, false);
        assert_eq!(linear, vec![([2.0], [2.0]), ([2.0], [0.0]), ([0.0], [0.0])]);
        let smooth = key_slopes(&keys, true);
        assert_eq!(smooth[1], ([1.0], [1.0]));
    }

    #[test]
    fn evaluation_hits_keys_and_clamps() {
        let keys = [key(0.0, 0.0), key(1.0, 2.0), key(2.0, 2.0)];
        let slopes = key_slopes(&keys, false);
        assert_eq!(evaluate(&keys, &slopes, -1.0), [0.0]);
        assert_eq!(evaluate(&keys, &slopes, 1.0), [2.0]);
        assert_eq!(evaluate(&keys, &slopes, 5.0), [2.0]);
        // A linear segment with matching slopes stays linear.
        assert!((evaluate(&keys, &slopes, 0.25)[0] - 0.5).abs() < 1e-6);
        // Smooth slopes give the middle key slope 1, so the last segment overshoots.
        let smooth = key_slopes(&keys, true);
        assert!(evaluate(&keys, &smooth, 1.5)[0] > 2.0);
    }

    /// Keys on every frame at 30 fps from `f(frame)`.
    fn dense(frames: usize, f: impl Fn(f32) -> f32) -> Vec<Key<1>> {
        (0..frames)
            .map(|i| key(i as f32 / 30.0, f(i as f32)))
            .collect()
    }

    #[test]
    fn a_constant_curve_reduces_to_its_ends_with_flat_slopes() {
        let keys = dense(31, |_| 2.0);
        let (kept, slopes) = reduce(
            &keys,
            &key_slopes(&keys, false),
            1.0 / 30.0,
            float_within(0.005),
        );
        assert_eq!(kept, vec![keys[0], keys[30]]);
        assert_eq!(slopes, vec![([0.0], [0.0]); 2]);
    }

    #[test]
    fn straight_runs_drop_up_to_fifty_frames_and_keep_their_slopes() {
        // A ramp: the whole-curve test (flat end slopes) fails, the walk drops every inner key.
        let keys = dense(31, |i| 1.0 + i);
        let slopes = key_slopes(&keys, false);
        let (kept, kept_slopes) = reduce(&keys, &slopes, 1.0 / 30.0, float_within(0.005));
        assert_eq!(kept, vec![keys[0], keys[30]]);
        assert_eq!(kept_slopes, vec![slopes[0], slopes[30]]);
        // Past fifty frames a key is kept in between.
        let keys = dense(61, |i| 1.0 + i);
        let (kept, _) = reduce(
            &keys,
            &key_slopes(&keys, false),
            1.0 / 30.0,
            float_within(0.005),
        );
        assert_eq!(kept.len(), 3, "{kept:?}");
    }

    #[test]
    fn a_corner_is_kept() {
        let keys = dense(31, |i| 1.0 + if i < 15.0 { i } else { 30.0 - i });
        let (kept, _) = reduce(
            &keys,
            &key_slopes(&keys, false),
            1.0 / 30.0,
            float_within(0.005),
        );
        assert!(kept.contains(&keys[15]), "{kept:?}");
        assert!(kept.len() < 8, "{kept:?}");
    }

    #[test]
    fn comparators_take_relative_and_angular_errors() {
        let within = vector_within(0.01);
        assert!(within(&[1.0, 2.0, 0.0], &[1.009, 2.0, 0.0]));
        assert!(!within(&[1.0, 2.0, 0.0], &[1.02, 2.0, 0.0]));
        // A component near zero must stay near zero.
        assert!(!within(&[1.0, 2.0, 0.0], &[1.0, 2.0, 0.001]));
        let turn = |degrees: f32| {
            let (s, c) = (degrees.to_radians() / 2.0).sin_cos();
            [0.0, s, 0.0, c]
        };
        let within = rotation_within(0.5);
        assert!(within(&turn(10.0), &turn(10.4)));
        assert!(!within(&turn(10.0), &turn(10.6)));
        // A segment quaternion far from unit length does not hold.
        assert!(!within(&turn(10.0), &turn(10.0).map(|c| c * 1.01)));
    }

    #[test]
    fn rotations_stay_in_one_hemisphere() {
        let q = |w: f32| Key {
            time: 0.0,
            value: [0.0, 0.0, 0.0, w],
        };
        let keys = continuous_rotations(&[q(1.0), q(-1.0), q(1.0)]);
        assert!(keys.iter().all(|k| k.value[3] == 1.0));
    }
}
