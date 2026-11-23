//! The numbers a bake draws its rays with: a Sobol sequence, per-texel rotations of it, and
//! for a texel's first 256 samples a sequence whose error is spread over neighbouring texels
//! as blue noise. Reading them as the Unity 6000.0.71f1 Editor's Progressive CPU lightmapper
//! does makes a bake's rays, and with them its noise, the Editor's
//! (tests/projects/bakedbuffers).

/// The bits a Sobol dimension has a generator matrix column for.
const SOBOL_BITS: usize = 52;
/// The dimensions of the Sobol matrices.
pub const SOBOL_DIMENSIONS: usize = 1024;

/// The side of the tile the blue noise tables repeat with over a lightmap.
const TILE: usize = 128;
/// The samples and the dimensions of the blue noise sequence.
const BLUE_SAMPLES: usize = 256;
const BLUE_DIMENSIONS: usize = 256;
/// The dimensions the ranking and scrambling tables have values for (further ones repeat).
const TILE_DIMENSIONS: usize = 8;
/// The values of one sample count's ranking or scrambling table.
const DISTRIBUTION: usize = TILE_DIMENSIONS * TILE * TILE;
/// The tables' distributions: for 1, 2, 4, ... 256 samples per texel.
const DISTRIBUTIONS: usize = 9;

/// The side of the square of texels the rotations repeat with, and the rotations of a texel.
const ROTATION_TILE: usize = 512;
const ROTATIONS: usize = 12;
/// The seed of the rotations' generator: with it a bake's rays, and so each texel's light, are
/// the Editor's (tests/projects/bakedbuffers).
const ROTATION_SEED: u32 = 0x05e3_0702;

/// The sizes of the three tables of the blue noise data, in the order they are stored.
pub const BLUE_NOISE_BYTES: usize =
    BLUE_SAMPLES * BLUE_DIMENSIONS + 2 * DISTRIBUTIONS * DISTRIBUTION;

/// The Sobol generator matrices (Joe and Kuo's direction numbers as tabulated by Leonhard
/// Gruenschloss, `data/SOBOL-LICENSE.txt`): 52 columns for each of 1024 dimensions.
static SOBOL_MATRICES: &[u8] = include_bytes!("../../data/sobol-matrices-1024x52.u32");

/// The 32-bit Mersenne Twister.
pub(super) struct Twister {
    state: [u32; 624],
    next: usize,
}

impl Twister {
    pub(super) fn new(seed: u32) -> Twister {
        let mut state = [0u32; 624];
        state[0] = seed;
        for i in 1..624 {
            let previous = state[i - 1];
            state[i] = 1_812_433_253u32
                .wrapping_mul(previous ^ (previous >> 30))
                .wrapping_add(i as u32);
        }
        Twister { state, next: 624 }
    }

    pub(super) fn draw(&mut self) -> u32 {
        if self.next == 624 {
            for i in 0..624 {
                let y = (self.state[i] & 0x8000_0000) | (self.state[(i + 1) % 624] & 0x7fff_ffff);
                let twist = if y & 1 != 0 { 0x9908_b0df } else { 0 };
                self.state[i] = self.state[(i + 397) % 624] ^ (y >> 1) ^ twist;
            }
            self.next = 0;
        }
        let mut y = self.state[self.next];
        self.next += 1;
        y ^= y >> 11;
        y ^= (y << 7) & 0x9d2c_5680;
        y ^= (y << 15) & 0xefc6_0000;
        y ^ (y >> 18)
    }
}

/// The tables a bake's samples are read from.
pub struct Samples {
    sobol: Vec<u32>,
    /// The blue noise sequence: a value per sample and dimension.
    sequence: Vec<u8>,
    ranking: Vec<u8>,
    scrambling: Vec<u8>,
    rotations: Vec<f32>,
}

/// The blue noise data is not [`BLUE_NOISE_BYTES`] long.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct BlueNoiseSize(pub usize);

impl Samples {
    /// The tables, with the blue noise data given: the sequence (a byte per sample and
    /// dimension), then the ranking and the scrambling tiles of every sample count. It is the
    /// `Resources/GI/SamplingData/bluenoisedata` file of a Unity 6000.0 installation.
    pub fn new(blue_noise: &[u8]) -> Result<Samples, BlueNoiseSize> {
        if blue_noise.len() != BLUE_NOISE_BYTES {
            return Err(BlueNoiseSize(blue_noise.len()));
        }
        let sobol: Vec<u32> = SOBOL_MATRICES
            .chunks_exact(4)
            .map(|b| u32::from_le_bytes([b[0], b[1], b[2], b[3]]))
            .collect();
        debug_assert_eq!(sobol.len(), SOBOL_DIMENSIONS * SOBOL_BITS);
        let (sequence, tiles) = blue_noise.split_at(BLUE_SAMPLES * BLUE_DIMENSIONS);
        let (ranking, scrambling) = tiles.split_at(DISTRIBUTIONS * DISTRIBUTION);
        let mut twister = Twister::new(ROTATION_SEED);
        let rotations = (0..ROTATION_TILE * ROTATION_TILE * ROTATIONS)
            .map(|_| (twister.draw() as f64 / u32::MAX as f64) as f32)
            .collect();
        Ok(Samples {
            sobol,
            sequence: sequence.to_vec(),
            ranking: ranking.to_vec(),
            scrambling: scrambling.to_vec(),
            rotations,
        })
    }

    /// The Sobol sequence's value of a dimension at an index, in 0..1.
    pub fn sobol(&self, index: u32, dimension: usize) -> f32 {
        let columns = &self.sobol[dimension * SOBOL_BITS..];
        let mut result = 0u32;
        let mut rest = index;
        let mut bit = 0;
        while rest != 0 {
            if rest & 1 != 0 {
                result ^= columns[bit];
            }
            rest >>= 1;
            bit += 1;
        }
        // The bits are read as a signed number: its upper half wraps to the upper half of 0..1.
        let value = result as i32 as f32 * 2.328_306_4e-10;
        if value < 0.0 { value + 1.0 } else { value }
    }

    /// A texel's rotations of three dimensions from `dimension` on. The rotations of a texel
    /// are twelve and repeat; texels repeat every 512 along both sides of the lightmap.
    pub fn rotation(&self, x: usize, y: usize, width: usize, dimension: usize) -> [f32; 3] {
        let texel = x % ROTATION_TILE + (y % ROTATION_TILE) * width.min(ROTATION_TILE);
        std::array::from_fn(|k| self.rotations[texel * ROTATIONS + (dimension + k) % ROTATIONS])
    }

    /// The blue noise sequence's value of a dimension at a sample of a texel, in 0..1, for the
    /// distribution of a sample count ([`distribution`]).
    pub fn blue(
        &self,
        index: u32,
        dimension: usize,
        x: usize,
        y: usize,
        distribution: usize,
    ) -> f32 {
        let index = index as usize % BLUE_SAMPLES;
        let dimension = dimension % BLUE_DIMENSIONS;
        let tile = distribution * DISTRIBUTION
            + dimension % TILE_DIMENSIONS
            + (x % TILE + (y % TILE) * TILE) * TILE_DIMENSIONS;
        let ranked = index ^ self.ranking[tile] as usize;
        let value = self.sequence[dimension + ranked * BLUE_DIMENSIONS] ^ self.scrambling[tile];
        (0.5 + value as f32) / 256.0
    }
}

/// The samples a texel takes for a count asked for: the nearest power of two (the upper one of
/// two equally near), and no fewer than the samples the texel is rasterized with
/// (`supersampling` along each side).
pub fn sample_count(asked: u32, supersampling: u32) -> u32 {
    let above = asked.max(1).next_power_of_two();
    let nearest = if above - asked <= asked - above / 2 {
        above
    } else {
        above / 2
    };
    nearest.max(supersampling * supersampling).max(1)
}

/// The blue noise distribution of a sample count: its power of two, the last one for 256
/// samples and more.
pub fn distribution(samples: u32) -> usize {
    (samples.clamp(1, BLUE_SAMPLES as u32).ilog2() as usize).min(DISTRIBUTIONS - 1)
}

/// The fraction of a sum of two numbers of 0..1.
pub fn fraction(sum: f32) -> f32 {
    sum - sum.floor()
}

const HALF_PI: f32 = 1.570_796_326_794_897;
const QUARTER_PI: f32 = 0.785_398_163_397_448_3;

/// A point of the unit disk for a point of the unit square (Shirley's concentric map).
fn disk(u: [f32; 2]) -> [f32; 2] {
    let (a, b) = (u[0] * 2.0 - 1.0, u[1] * 2.0 - 1.0);
    let (radius, angle) = if a * a > b * b {
        (a, QUARTER_PI * (b / a))
    } else if b == 0.0 {
        (b, HALF_PI)
    } else {
        (b, HALF_PI - QUARTER_PI * (a / b))
    };
    [radius * angle.cos(), radius * angle.sin()]
}

/// A direction of the hemisphere around +z, by the cosine of its angle to the axis.
pub fn cosine_direction(u: [f32; 2]) -> [f32; 3] {
    let d = disk(u);
    [d[0], d[1], (1.0 - (d[0] * d[0] + d[1] * d[1])).sqrt()]
}

/// A direction of the whole sphere, evenly, for a point of the unit square.
pub fn sphere(u: [f32; 2]) -> [f32; 3] {
    let z = 1.0 - 2.0 * u[1];
    let r = (1.0 - z * z).sqrt();
    let angle = std::f32::consts::TAU * u[0];
    [angle.cos() * r, angle.sin() * r, z]
}

/// Two unit vectors at right angles to a unit vector and to each other.
pub fn basis(n: [f32; 3]) -> ([f32; 3], [f32; 3]) {
    let sign = if n[2] >= 0.0 { 1.0 } else { -1.0 };
    let a = -1.0 / (sign + n[2]);
    let b = n[0] * n[1] * a;
    (
        [1.0 + sign * n[0] * n[0] * a, sign * b, -sign * n[0]],
        [b, sign + n[1] * n[1] * a, -n[1]],
    )
}

/// A cosine-weighted direction around a unit normal.
pub fn around(normal: [f32; 3], u: [f32; 2]) -> [f32; 3] {
    let local = cosine_direction(u);
    let (t, b) = basis(normal);
    std::array::from_fn(|k| local[0] * t[k] + local[1] * b[k] + local[2] * normal[k])
}
