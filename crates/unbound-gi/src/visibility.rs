//! A system's precomputed visibility for directional lights (`.vis`): per cluster, the share of
//! its points that see the sky in each of a set of directions, quantized to 1, 2, 4 or 8 bits.
//! The directions lie on rings of equal latitude around +y; their values are stored per group of
//! directions, run-length encoded. A light's direction reads the four stored directions around
//! it, weighted bilinearly in latitude and longitude. Matches the Unity 6000.0.71f1 Editor,
//! tests/projects/realtimegi (`room-directional*`).
use anyhow::{Context, Result, bail, ensure};

const MAGIC: u32 = 0x5356_4547;
const VERSION: i32 = 5;
/// The payload follows a 32-bit length.
const START: usize = 4;
/// Most rings the direction table holds.
const MAX_RINGS: usize = 128;
/// Interpolation weights are bytes: the four of a direction add up to this.
const WEIGHT_SCALE: u32 = 255;

#[derive(Debug, Clone)]
pub struct Visibility {
    /// Clusters per byte (8, 4, 2 or 1: 1, 2, 4 or 8 bits per value).
    values_per_byte: usize,
    directions_per_stream: usize,
    /// Per stream, its run-length encoded bytes.
    streams: Vec<std::ops::Range<usize>>,
    data: Vec<u8>,
    /// Per ring of latitude, its first direction and its number of directions.
    rings: Vec<(u32, u32)>,
}

/// The four stored directions around a light's direction and their byte weights.
pub type DirectionWeights = ([usize; 4], [u32; 4]);

fn reader(bytes: &[u8]) -> impl Fn(usize) -> Result<u32> + '_ {
    move |at: usize| {
        let at = START + at;
        bytes
            .get(at..at + 4)
            .map(|b| u32::from_le_bytes(b.try_into().unwrap()))
            .with_context(|| format!("precomputed visibility shorter than {}", at + 4))
    }
}

impl Visibility {
    pub fn read(bytes: &[u8]) -> Result<Self> {
        let u32_at = reader(bytes);
        let count = |at: usize| -> Result<usize> {
            let v = u32_at(at)? as i32;
            usize::try_from(v).with_context(|| format!("a negative count {v}"))
        };
        ensure!(u32_at(0)? == MAGIC, "not precomputed visibility data");
        let version = u32_at(4)? as i32;
        ensure!(
            version == VERSION,
            "precomputed visibility version {version}, not {VERSION}"
        );
        let values_per_byte = count(0x1c)?;
        ensure!(
            matches!(values_per_byte, 1 | 2 | 4 | 8),
            "{values_per_byte} visibility values per byte"
        );
        let streams = count(0x30)?;
        ensure!(count(0x20)? == 4 * streams, "a malformed stream table");
        let table = count(0x24)?;
        let (total, data_at) = (count(0x28)?, count(0x2c)?);
        let directions_per_stream = count(0x34)?;
        let ring_count = count(0x38)?;
        ensure!(
            directions_per_stream > 0 && (1..=MAX_RINGS).contains(&ring_count),
            "a malformed direction table"
        );
        let u16_at = |at: usize| -> Result<u32> {
            let at = START + at;
            bytes
                .get(at..at + 2)
                .map(|b| u16::from_le_bytes(b.try_into().unwrap()) as u32)
                .context("a truncated direction table")
        };
        let rings = (0..ring_count)
            .map(|r| Ok((u16_at(0x40 + 2 * r)?, u16_at(0x140 + 2 * r)?)))
            .collect::<Result<Vec<_>>>()?;
        let directions = streams * directions_per_stream;
        for &(first, n) in &rings {
            ensure!(
                n > 0 && (first + n) as usize <= directions,
                "a ring of directions outside the {directions} stored"
            );
        }
        let data = bytes
            .get(START + data_at..)
            .and_then(|d| d.get(..total))
            .context("truncated visibility streams")?
            .to_vec();
        let starts = (0..streams)
            .map(|s| count(table + 4 * s))
            .collect::<Result<Vec<_>>>()?;
        let streams = (0..streams)
            .map(|s| {
                let end = starts.get(s + 1).copied().unwrap_or(total);
                ensure!(starts[s] <= end && end <= total, "a malformed stream table");
                Ok(starts[s]..end)
            })
            .collect::<Result<Vec<_>>>()?;
        Ok(Self {
            values_per_byte,
            directions_per_stream,
            streams,
            data,
            rings,
        })
    }

    /// The bits of one value and their mask.
    fn bits(&self) -> (usize, u32) {
        let bits = 8 / self.values_per_byte;
        (bits, (1u32 << bits) - 1)
    }

    /// The four stored directions around unit direction `d` (toward the light) and their
    /// weights: bilinear in latitude (the angle from +y over the rings) and longitude
    /// (atan2(z, -x) over each ring's directions, wrapping around), as bytes adding up to 255.
    pub fn lookup(&self, d: [f32; 3]) -> DirectionWeights {
        let rings = self.rings.len();
        let last = rings as i64 - 1;
        let t = d[1].clamp(-1.0, 1.0).acos() * last as f32 / std::f32::consts::PI;
        let below = (t as i64).clamp(0, last);
        let above = (t as i64 + 1).clamp(0, last);
        let ft = t - below as f32;
        let u = d[2].atan2(-d[0]) / (2.0 * std::f32::consts::PI) + 0.5;
        let mut index = [0; 4];
        let mut weight = [0f32; 4];
        for (k, (ring, w)) in [(below, 1.0 - ft), (above, ft)].into_iter().enumerate() {
            let (first, n) = self.rings[ring as usize];
            let p = n as f32 * u;
            let j = (p as i64).clamp(0, n as i64 - 1);
            let f = p - j as f32;
            let next = if j + 1 >= n as i64 {
                j + 1 - n as i64
            } else {
                j + 1
            };
            index[2 * k] = (first as i64 + j) as usize;
            index[2 * k + 1] = (first as i64 + next) as usize;
            weight[2 * k] = (1.0 - f) * w;
            weight[2 * k + 1] = f * w;
        }
        let mut bytes = [0u32; 4];
        for k in 1..4 {
            bytes[k] = (weight[k] * WEIGHT_SCALE as f32) as u32;
        }
        bytes[0] = WEIGHT_SCALE.saturating_sub(bytes[1] + bytes[2] + bytes[3]);
        (index, bytes)
    }

    /// Per cluster, how many of its points (`points[c]`, the first ones in order) see direction
    /// `d` (unit, toward the light): the weighted visibility of the four stored directions around
    /// it, times the point count, rounded down. `stream` is scratch space.
    pub fn visible_points(
        &self,
        d: [f32; 3],
        points: impl Fn(usize) -> u32,
        stream: &mut Vec<u8>,
        out: &mut [u32],
    ) -> Result<()> {
        let (index, weight) = self.lookup(d);
        let (bits, mask) = self.bits();
        let per_direction = out.len().div_ceil(self.values_per_byte);
        out.fill(0);
        let mut decoded = None;
        for (k, &direction) in index.iter().enumerate() {
            if weight[k] == 0 {
                continue;
            }
            let s = direction / self.directions_per_stream;
            if decoded != Some(s) {
                let range = self
                    .streams
                    .get(s)
                    .with_context(|| format!("visibility direction {direction} not stored"))?
                    .clone();
                stream.clear();
                decode_runs(&self.data[range], stream)?;
                // A stream holds whole directions of one value per cluster each.
                ensure!(
                    stream.len() % per_direction == 0
                        && stream.len() / per_direction <= self.directions_per_stream,
                    "a visibility stream of {} bytes for {} clusters",
                    stream.len(),
                    out.len()
                );
                decoded = Some(s);
            }
            let at = (direction % self.directions_per_stream) * per_direction;
            let values = stream
                .get(at..at + per_direction)
                .context("a visibility stream shorter than its directions")?;
            for (c, v) in out.iter_mut().enumerate() {
                let byte = values[c / self.values_per_byte] as u32;
                *v += ((byte >> ((c % self.values_per_byte) * bits)) & mask) * weight[k];
            }
        }
        for (c, v) in out.iter_mut().enumerate() {
            let full = mask * WEIGHT_SCALE;
            *v = (*v as u64 * points(c) as u64 / full as u64) as u32;
        }
        Ok(())
    }
}

/// Run-length decoding: a byte n below 0x80 copies the next n bytes, a byte 0x80 + n repeats
/// the next byte n times.
fn decode_runs(src: &[u8], out: &mut Vec<u8>) -> Result<()> {
    let mut i = 0;
    while i < src.len() {
        let n = src[i] as usize;
        if n >= 0x80 {
            let Some(&b) = src.get(i + 1) else {
                bail!("a truncated visibility run");
            };
            out.extend(std::iter::repeat_n(b, n - 0x80));
            i += 2;
        } else {
            let run = src
                .get(i + 1..i + 1 + n)
                .context("a truncated visibility run")?;
            out.extend_from_slice(run);
            i += 1 + n;
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn runs_copy_and_repeat() {
        let mut out = Vec::new();
        decode_runs(&[2, 7, 8, 0x83, 5, 0, 1, 9], &mut out).unwrap();
        assert_eq!(out, [7, 8, 5, 5, 5, 9]);
        assert!(decode_runs(&[3, 1], &mut Vec::new()).is_err());
        assert!(decode_runs(&[0x85], &mut Vec::new()).is_err());
    }

    /// Two rings of two directions (+y side, -y side), 4 clusters at 2 bits, one stream of 4
    /// directions: direction d gives cluster c the value (c + d) % 4.
    fn tiny() -> Vec<u8> {
        let mut b = vec![0u8; START + 0x240];
        let mut put = |at: usize, v: u32| {
            b[START + at..START + at + 4].copy_from_slice(&v.to_le_bytes());
        };
        put(0, MAGIC);
        put(4, VERSION as u32);
        put(0x1c, 4);
        put(0x20, 4);
        put(0x24, 0x240);
        put(0x30, 1);
        put(0x34, 4);
        put(0x38, 2);
        for (r, (first, n)) in [(0u16, 2u16), (2, 2)].into_iter().enumerate() {
            b[START + 0x40 + 2 * r..][..2].copy_from_slice(&first.to_le_bytes());
            b[START + 0x140 + 2 * r..][..2].copy_from_slice(&n.to_le_bytes());
        }
        let values: Vec<u8> = (0..4u32)
            .map(|d| (0..4u32).fold(0, |a, c| a | ((((c + d) % 4) as u8) << (2 * c))))
            .collect();
        let mut stream = vec![values.len() as u8];
        stream.extend(&values);
        let data_at = 0x244;
        b.resize(START + data_at, 0);
        b[START + 0x240..START + 0x244].copy_from_slice(&0u32.to_le_bytes());
        b.extend(&stream);
        let len = stream.len() as u32;
        b[START + 0x28..START + 0x2c].copy_from_slice(&len.to_le_bytes());
        b[START + 0x2c..START + 0x30].copy_from_slice(&(data_at as u32).to_le_bytes());
        b
    }

    #[test]
    fn reads_and_interpolates_stored_directions() {
        let v = Visibility::read(&tiny()).unwrap();
        // Straight up: the first ring alone; its longitude between its two directions.
        let (index, weight) = v.lookup([0.0, 1.0, 0.0]);
        assert_eq!(weight.iter().sum::<u32>(), WEIGHT_SCALE);
        assert!(index[..2].iter().all(|&d| d < 2));
        assert_eq!(weight[2] + weight[3], 0);
        let mut out = [0u32; 4];
        let mut stream = Vec::new();
        // 8 points per cluster: all of them where every direction around sees fully (3).
        v.visible_points([0.0, 1.0, 0.0], |_| 8, &mut stream, &mut out)
            .unwrap();
        let expected: Vec<u32> = (0..4u32)
            .map(|c| {
                let sum: u32 = (0..4)
                    .map(|k| ((c + index[k] as u32) % 4) * weight[k])
                    .sum();
                sum * 8 / (3 * WEIGHT_SCALE)
            })
            .collect();
        assert_eq!(out.to_vec(), expected);
        // More clusters than stored values fail.
        assert!(
            v.visible_points([0.0, 1.0, 0.0], |_| 8, &mut stream, &mut [0; 40])
                .is_err()
        );
    }

    #[test]
    fn longitude_wraps_around_each_ring() {
        let v = Visibility::read(&tiny()).unwrap();
        // Toward +x (to the light), just either side of the longitude seam: the ring's last
        // direction and its first.
        for z in [1e-3f32, -1e-3] {
            let d = [(1.0 - z * z).sqrt(), 0.0, z];
            let (index, weight) = v.lookup(d);
            assert_eq!(weight.iter().sum::<u32>(), WEIGHT_SCALE, "{z}");
            for k in 0..4 {
                assert!(index[k] < 4, "{index:?}");
            }
            // Each ring's two directions are one and its next, wrapping around. At the seam the
            // first direction weighs most on both sides: from just below longitude 1 it is the
            // next after the last, from just above 0 it is the first itself.
            assert_eq!((index[0] + 1) % 2, index[1] % 2);
            if z > 0.0 {
                assert_eq!((index[0] % 2, index[1] % 2), (1, 0));
                assert!(weight[1] > weight[0], "{weight:?}");
            } else {
                assert_eq!((index[0] % 2, index[1] % 2), (0, 1));
                assert!(weight[0] > weight[1], "{weight:?}");
            }
            assert_eq!(index[2] / 2, 1);
        }
    }

    #[test]
    fn malformed_visibility_fails_without_panicking() {
        let full = tiny();
        for len in 0..full.len() {
            let _ = Visibility::read(&full[..len]);
        }
        for at in 0..0x60 {
            let mut bad = full.clone();
            bad[START + at] ^= 0xFF;
            if let Ok(v) = Visibility::read(&bad) {
                let _ = v.visible_points([0.3, 0.5, -0.8], |_| 4, &mut Vec::new(), &mut [0; 4]);
                let _ = v.lookup([f32::NAN; 3]);
            }
        }
    }
}
