//! Where a source is heard: Unity's distance rolloff, 2D/3D panning, spatial blend and doppler,
//! reduced to a gain matrix from the clip's (left, right) to the output's (left, right).
use crate::render_protocol::Vector3;

/// FMOD's (and so Unity's) speed of sound, m/s.
pub const SPEED_OF_SOUND: f32 = 340.0;

/// `AudioRolloffMode`.
pub const LOGARITHMIC: i32 = 0;
pub const LINEAR: i32 = 1;
pub const CUSTOM: i32 = 2;

/// Distance attenuation. Logarithmic is FMOD's inverse model,
/// `min / (min + rolloff_scale * (d - min))`; linear falls from 1 at `min` to 0 at `max`; custom
/// samples `curve` over [0, max] (the curve's x axis is distance / maxDistance). Beyond
/// `max` the sound keeps its level at `max`.
pub fn attenuation(
    mode: i32,
    distance: f32,
    min: f32,
    max: f32,
    rolloff_scale: f32,
    curve: Option<&[f32]>,
) -> f32 {
    let min = min.max(0.0);
    let max = max.max(min);
    match mode {
        LINEAR => {
            if distance <= min {
                1.0
            } else if distance >= max || max <= min {
                0.0
            } else {
                (max - distance) / (max - min)
            }
        }
        CUSTOM => match curve {
            Some(table) if !table.is_empty() => {
                let x = if max > 0.0 { distance / max } else { 1.0 };
                sample_table(table, x).max(0.0)
            }
            _ => 1.0,
        },
        _ => {
            let d = distance.min(max);
            if d <= min {
                1.0
            } else {
                min / (min + rolloff_scale.max(0.0) * (d - min))
            }
        }
    }
}

/// A curve sampled at `table.len()` evenly spaced points over [0, 1], read with linear
/// interpolation; `x` is clamped.
pub fn sample_table(table: &[f32], x: f32) -> f32 {
    match table.len() {
        0 => 0.0,
        1 => table[0],
        n => {
            let t = x.clamp(0.0, 1.0) * (n - 1) as f32;
            let i = (t.floor() as usize).min(n - 2);
            let f = t - i as f32;
            table[i] * (1.0 - f) + table[i + 1] * f
        }
    }
}

/// `panStereo` (2D): a balance control: the far side fades out, the near one stays at 1.
pub fn balance(pan: f32) -> [f32; 2] {
    let pan = pan.clamp(-1.0, 1.0);
    [(1.0 - pan).min(1.0), (1.0 + pan).min(1.0)]
}

/// 3D panning by `pan` (-1 left, 1 right): constant at 1 in the centre, the far side fading
/// with the square root of the balance (closer to constant power than a plain balance).
pub fn pan_3d(pan: f32) -> [f32; 2] {
    let [l, r] = balance(pan);
    [l.sqrt(), r.sqrt()]
}

pub fn sub(a: Vector3, b: Vector3) -> Vector3 {
    Vector3 {
        x: a.x - b.x,
        y: a.y - b.y,
        z: a.z - b.z,
    }
}

pub fn dot(a: Vector3, b: Vector3) -> f32 {
    a.x * b.x + a.y * b.y + a.z * b.z
}

pub fn cross(a: Vector3, b: Vector3) -> Vector3 {
    Vector3 {
        x: a.y * b.z - a.z * b.y,
        y: a.z * b.x - a.x * b.z,
        z: a.x * b.y - a.y * b.x,
    }
}

pub fn length(a: Vector3) -> f32 {
    dot(a, a).sqrt()
}

/// The listener's pose.
#[derive(Debug, Clone, Copy)]
pub struct Listener {
    pub position: Vector3,
    pub forward: Vector3,
    pub up: Vector3,
    pub velocity: Vector3,
}

impl Default for Listener {
    fn default() -> Self {
        Self {
            position: Vector3::default(),
            forward: Vector3 {
                x: 0.0,
                y: 0.0,
                z: 1.0,
            },
            up: Vector3 {
                x: 0.0,
                y: 1.0,
                z: 0.0,
            },
            velocity: Vector3::default(),
        }
    }
}

/// The doppler pitch factor of a source moving at `source_velocity` heard by a listener moving at
/// `listener_velocity`; `direction` points from the listener to the source (normalized). `scale` is
/// the source's dopplerLevel times the project's Doppler Factor.
pub fn doppler(
    direction: Vector3,
    source_velocity: Vector3,
    listener_velocity: Vector3,
    scale: f32,
) -> f32 {
    if scale <= 0.0 {
        return 1.0;
    }
    // Positive: the source moves away; the listener moves towards the source.
    let away = dot(source_velocity, direction) * scale;
    let towards = dot(listener_velocity, direction) * scale;
    let c = SPEED_OF_SOUND;
    let away = away.clamp(-0.95 * c, 10.0 * c);
    let towards = towards.clamp(-0.95 * c, 10.0 * c);
    ((c + towards) / (c + away)).clamp(0.05, 20.0)
}

/// Inputs of [`spatialize`] (an AudioSource's 3D settings).
#[derive(Debug, Clone, Copy)]
pub struct SourceSpatial<'a> {
    pub position: Vector3,
    pub velocity: Vector3,
    pub pan_stereo: f32,
    pub spatial_blend: f32,
    pub spread: f32,
    pub doppler_level: f32,
    pub min_distance: f32,
    pub max_distance: f32,
    pub rolloff_mode: i32,
    pub rolloff_curve: Option<&'a [f32]>,
    pub spatial_blend_curve: Option<&'a [f32]>,
    /// Normalized: 1 = 360 degrees.
    pub spread_curve: Option<&'a [f32]>,
}

/// How a source is heard: `matrix` maps the clip's (left, right) to the output's (left, right) as
/// `[ll, lr, rl, rr]` (out_left = ll·l + lr·r, out_right = rl·l + rr·r); `pitch` is the doppler
/// factor; `audibility` the gain at the listener (for voice priority).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Spatialized {
    pub matrix: [f32; 4],
    pub pitch: f32,
    pub audibility: f32,
}

/// The 2D part (`panStereo`) and the 3D part (distance rolloff, direction, spread, doppler) mixed
/// by the spatial blend. In 3D a stereo clip is heard as one point (its channels averaged).
pub fn spatialize(
    s: &SourceSpatial,
    listener: &Listener,
    rolloff_scale: f32,
    doppler_factor: f32,
) -> Spatialized {
    let offset = sub(s.position, listener.position);
    let distance = length(offset);
    let normalized = if s.max_distance > 0.0 {
        distance / s.max_distance
    } else {
        1.0
    };
    let blend = match s.spatial_blend_curve {
        Some(table) if !table.is_empty() => sample_table(table, normalized),
        _ => s.spatial_blend,
    }
    .clamp(0.0, 1.0);
    let [l2, r2] = balance(s.pan_stereo);
    if blend <= 0.0 {
        return Spatialized {
            matrix: [l2, 0.0, 0.0, r2],
            pitch: 1.0,
            audibility: 1.0,
        };
    }
    let gain = attenuation(
        s.rolloff_mode,
        distance,
        s.min_distance,
        s.max_distance,
        rolloff_scale,
        s.rolloff_curve,
    );
    let (pan, direction) = if distance > 1e-5 {
        let direction = Vector3 {
            x: offset.x / distance,
            y: offset.y / distance,
            z: offset.z / distance,
        };
        let right = cross(listener.up, listener.forward);
        let right_length = length(right);
        let pan = if right_length > 1e-6 {
            dot(direction, right) / right_length
        } else {
            0.0
        };
        (pan, Some(direction))
    } else {
        (0.0, None)
    };
    let spread = match s.spread_curve {
        Some(table) if !table.is_empty() => sample_table(table, normalized) * 360.0,
        _ => s.spread,
    };
    // Spread widens the source: 180 degrees fills both sides evenly, 360 mirrors it.
    let pan = pan * (spread.clamp(0.0, 360.0).to_radians() * 0.5).cos();
    let [l3, r3] = pan_3d(pan);
    let (l3, r3) = (l3 * gain * 0.5, r3 * gain * 0.5);
    let flat = 1.0 - blend;
    let pitch = match direction {
        Some(direction) => {
            let factor = doppler(
                direction,
                s.velocity,
                listener.velocity,
                s.doppler_level * doppler_factor,
            );
            1.0 + (factor - 1.0) * blend
        }
        None => 1.0,
    };
    Spatialized {
        matrix: [
            flat * l2 + blend * l3,
            blend * l3,
            blend * r3,
            flat * r2 + blend * r3,
        ],
        pitch,
        audibility: flat + blend * gain,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn v(x: f32, y: f32, z: f32) -> Vector3 {
        Vector3 { x, y, z }
    }

    fn close(a: f32, b: f32) -> bool {
        (a - b).abs() < 1e-4
    }

    #[test]
    fn logarithmic_rolloff_is_inverse_distance_clamped_at_max() {
        assert_eq!(attenuation(LOGARITHMIC, 0.5, 1.0, 500.0, 1.0, None), 1.0);
        assert!(close(
            attenuation(LOGARITHMIC, 2.0, 1.0, 500.0, 1.0, None),
            0.5
        ));
        assert!(close(
            attenuation(LOGARITHMIC, 11.0, 1.0, 500.0, 1.0, None),
            1.0 / 11.0
        ));
        // Rolloff scale 2 halves faster.
        assert!(close(
            attenuation(LOGARITHMIC, 2.0, 1.0, 500.0, 2.0, None),
            1.0 / 3.0
        ));
        // Past max distance the level stays.
        let at_max = attenuation(LOGARITHMIC, 10.0, 1.0, 10.0, 1.0, None);
        assert!(close(
            attenuation(LOGARITHMIC, 50.0, 1.0, 10.0, 1.0, None),
            at_max
        ));
    }

    #[test]
    fn linear_rolloff_reaches_zero_at_max() {
        assert_eq!(attenuation(LINEAR, 1.0, 1.0, 11.0, 1.0, None), 1.0);
        assert!(close(attenuation(LINEAR, 6.0, 1.0, 11.0, 1.0, None), 0.5));
        assert_eq!(attenuation(LINEAR, 11.0, 1.0, 11.0, 1.0, None), 0.0);
        assert_eq!(attenuation(LINEAR, 20.0, 1.0, 11.0, 1.0, None), 0.0);
    }

    #[test]
    fn custom_rolloff_samples_the_curve_over_max_distance() {
        let curve = [1.0, 0.5, 0.0];
        assert!(close(
            attenuation(CUSTOM, 0.0, 1.0, 10.0, 1.0, Some(&curve)),
            1.0
        ));
        assert!(close(
            attenuation(CUSTOM, 5.0, 1.0, 10.0, 1.0, Some(&curve)),
            0.5
        ));
        assert!(close(
            attenuation(CUSTOM, 7.5, 1.0, 10.0, 1.0, Some(&curve)),
            0.25
        ));
        assert!(close(
            attenuation(CUSTOM, 30.0, 1.0, 10.0, 1.0, Some(&curve)),
            0.0
        ));
    }

    fn source(position: Vector3, blend: f32) -> SourceSpatial<'static> {
        SourceSpatial {
            position,
            velocity: Vector3::default(),
            pan_stereo: 0.0,
            spatial_blend: blend,
            spread: 0.0,
            doppler_level: 1.0,
            min_distance: 1.0,
            max_distance: 500.0,
            rolloff_mode: LOGARITHMIC,
            rolloff_curve: None,
            spatial_blend_curve: None,
            spread_curve: None,
        }
    }

    #[test]
    fn two_d_ignores_distance_and_pans_by_balance() {
        let mut s = source(v(100.0, 0.0, 0.0), 0.0);
        let out = spatialize(&s, &Listener::default(), 1.0, 1.0);
        assert_eq!(out.matrix, [1.0, 0.0, 0.0, 1.0]);
        s.pan_stereo = 0.5;
        let out = spatialize(&s, &Listener::default(), 1.0, 1.0);
        assert_eq!(out.matrix, [0.5, 0.0, 0.0, 1.0]);
    }

    #[test]
    fn three_d_pans_to_the_side_the_source_is_on() {
        // Unity is left-handed: +x is to the right of a listener looking down +z.
        let right = spatialize(
            &source(v(2.0, 0.0, 0.0), 1.0),
            &Listener::default(),
            1.0,
            1.0,
        );
        let left_gain = right.matrix[0] + right.matrix[1];
        let right_gain = right.matrix[2] + right.matrix[3];
        assert!(close(right_gain, 0.5), "{right:?}");
        assert!(close(left_gain, 0.0));
        let ahead = spatialize(
            &source(v(0.0, 0.0, 2.0), 1.0),
            &Listener::default(),
            1.0,
            1.0,
        );
        assert!(close(ahead.matrix[0] + ahead.matrix[1], 0.5));
        assert!(close(ahead.matrix[2] + ahead.matrix[3], 0.5));
        // Spread 180: no direction any more.
        let mut wide = source(v(2.0, 0.0, 0.0), 1.0);
        wide.spread = 180.0;
        let wide = spatialize(&wide, &Listener::default(), 1.0, 1.0);
        assert!(close(wide.matrix[0] + wide.matrix[1], 0.5));
    }

    #[test]
    fn spatial_blend_mixes_2d_and_3d() {
        let out = spatialize(
            &source(v(0.0, 0.0, 3.0), 0.5),
            &Listener::default(),
            1.0,
            1.0,
        );
        // 0.5 * 1 (2D) + 0.5 * (1/3) (3D at 3 m).
        assert!(
            close(out.matrix[0] + out.matrix[1], 0.5 + 0.5 / 3.0),
            "{out:?}"
        );
    }

    #[test]
    fn doppler_raises_the_pitch_of_an_approaching_source() {
        let mut s = source(v(0.0, 0.0, 10.0), 1.0);
        s.velocity = v(0.0, 0.0, -34.0);
        let out = spatialize(&s, &Listener::default(), 1.0, 1.0);
        assert!(close(out.pitch, 340.0 / 306.0), "{}", out.pitch);
        s.doppler_level = 0.0;
        assert_eq!(spatialize(&s, &Listener::default(), 1.0, 1.0).pitch, 1.0);
        // A listener moving towards the source.
        let listener = Listener {
            velocity: v(0.0, 0.0, 34.0),
            ..Listener::default()
        };
        let still = source(v(0.0, 0.0, 10.0), 1.0);
        assert!(close(
            spatialize(&still, &listener, 1.0, 1.0).pitch,
            374.0 / 340.0
        ));
    }

    #[test]
    fn tables_interpolate() {
        assert_eq!(sample_table(&[0.0, 1.0], 0.25), 0.25);
        assert_eq!(sample_table(&[0.0, 1.0], 2.0), 1.0);
        assert_eq!(sample_table(&[3.0], 0.7), 3.0);
    }
}
