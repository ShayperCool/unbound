//! The tangents of imported transform curves as the clip documents store them, and their
//! evaluation, so that the importer samples a clip (Legacy skinned bounds) with the same curves
//! the engine plays.
use super::Key;

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
    let (a, b) = (&keys[i], &keys[i + 1]);
    let dt = b.time - a.time;
    let s = (time - a.time) / dt;
    let (s2, s3) = (s * s, s * s * s);
    let h00 = 2.0 * s3 - 3.0 * s2 + 1.0;
    let h10 = s3 - 2.0 * s2 + s;
    let h01 = -2.0 * s3 + 3.0 * s2;
    let h11 = s3 - s2;
    std::array::from_fn(|c| {
        h00 * a.value[c]
            + h10 * slopes[i].1[c] * dt
            + h01 * b.value[c]
            + h11 * slopes[i + 1].0[c] * dt
    })
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
