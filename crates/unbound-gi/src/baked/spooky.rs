//! Bob Jenkins' SpookyHash V2 (public domain), the 128-bit hash of a message: what keys the
//! Lightmap Parameters a baked atlas groups by, and a baked light probe set's points.

const SC_CONST: u64 = 0xdead_beef_dead_beef;

fn short_mix(h: &mut [u64; 4]) {
    for (i, j, k, r) in [
        (2, 3, 0, 50),
        (3, 0, 1, 52),
        (0, 1, 2, 30),
        (1, 2, 3, 41),
        (2, 3, 0, 54),
        (3, 0, 1, 48),
        (0, 1, 2, 38),
        (1, 2, 3, 37),
        (2, 3, 0, 62),
        (3, 0, 1, 34),
        (0, 1, 2, 5),
        (1, 2, 3, 36),
    ] {
        h[i] = h[i].rotate_left(r);
        h[i] = h[i].wrapping_add(h[j]);
        h[k] ^= h[i];
    }
}

fn short_end(h: &mut [u64; 4]) {
    for (i, j, k, r) in [
        (3, 2, 2, 15),
        (0, 3, 3, 52),
        (1, 0, 0, 26),
        (2, 1, 1, 51),
        (3, 2, 2, 28),
        (0, 3, 3, 9),
        (1, 0, 0, 47),
        (2, 1, 1, 54),
        (3, 2, 2, 32),
        (0, 3, 3, 25),
        (1, 0, 0, 63),
    ] {
        // h[i] ^= h[j]; h[k] = rot(h[k], r); h[i] += h[k]  (j == k)
        h[i] ^= h[j];
        h[k] = h[k].rotate_left(r);
        h[i] = h[i].wrapping_add(h[k]);
    }
}

fn u64_at(m: &[u8], at: usize) -> u64 {
    u64::from_le_bytes(m[at..at + 8].try_into().unwrap())
}

fn u32_at(m: &[u8], at: usize) -> u64 {
    u32::from_le_bytes(m[at..at + 4].try_into().unwrap()) as u64
}

/// SpookyHash V2 `Hash128` of a message shorter than 192 bytes with the seeds `seed`:
/// `(hash1, hash2)`.
pub fn hash128_short(message: &[u8], seed: (u64, u64)) -> (u64, u64) {
    assert!(
        message.len() < 192,
        "a long message ({} bytes)",
        message.len()
    );
    let length = message.len();
    let mut h = [seed.0, seed.1, SC_CONST, SC_CONST];
    let mut at = 0;
    let mut remainder = length % 32;
    if length > 15 {
        while at + 32 <= length - remainder {
            h[2] = h[2].wrapping_add(u64_at(message, at));
            h[3] = h[3].wrapping_add(u64_at(message, at + 8));
            short_mix(&mut h);
            h[0] = h[0].wrapping_add(u64_at(message, at + 16));
            h[1] = h[1].wrapping_add(u64_at(message, at + 24));
            at += 32;
        }
        if remainder >= 16 {
            h[2] = h[2].wrapping_add(u64_at(message, at));
            h[3] = h[3].wrapping_add(u64_at(message, at + 8));
            short_mix(&mut h);
            at += 16;
            remainder -= 16;
        }
    }
    h[3] = h[3].wrapping_add((length as u64) << 56);
    let m = &message[at..];
    let byte = |i: usize| m[i] as u64;
    match remainder {
        12..=15 => {
            for i in (12..remainder).rev() {
                h[3] = h[3].wrapping_add(byte(i) << (8 * (i - 8)));
            }
            h[3] = h[3].wrapping_add(u32_at(m, 8));
            h[2] = h[2].wrapping_add(u64_at(m, 0));
        }
        8..=11 => {
            for i in (8..remainder).rev() {
                h[3] = h[3].wrapping_add(byte(i) << (8 * (i - 8)));
            }
            h[2] = h[2].wrapping_add(u64_at(m, 0));
        }
        4..=7 => {
            for i in (4..remainder).rev() {
                h[2] = h[2].wrapping_add(byte(i) << (8 * i));
            }
            h[2] = h[2].wrapping_add(u32_at(m, 0));
        }
        1..=3 => {
            for i in (0..remainder).rev() {
                h[2] = h[2].wrapping_add(byte(i) << (8 * i));
            }
        }
        _ => {
            h[2] = h[2].wrapping_add(SC_CONST);
            h[3] = h[3].wrapping_add(SC_CONST);
        }
    }
    short_end(&mut h);
    (h[0], h[1])
}

/// The bytes of a long message's blocks.
const BLOCK: usize = 96;
/// Messages of at least this many bytes take the long form.
const LONG: usize = 192;

fn mix(data: &[u64; 12], s: &mut [u64; 12]) {
    const ROTATIONS: [u32; 12] = [11, 32, 43, 31, 17, 28, 39, 57, 55, 54, 22, 46];
    for i in 0..12 {
        s[i] = s[i].wrapping_add(data[i]);
        s[(i + 2) % 12] ^= s[(i + 10) % 12];
        s[(i + 11) % 12] ^= s[i];
        s[i] = s[i].rotate_left(ROTATIONS[i]);
        s[(i + 11) % 12] = s[(i + 11) % 12].wrapping_add(s[(i + 1) % 12]);
    }
}

fn end_partial(h: &mut [u64; 12]) {
    const ROTATIONS: [u32; 12] = [44, 15, 34, 21, 38, 33, 10, 13, 38, 53, 42, 54];
    for i in 0..12 {
        h[(i + 11) % 12] = h[(i + 11) % 12].wrapping_add(h[(i + 1) % 12]);
        h[(i + 2) % 12] ^= h[(i + 11) % 12];
        h[(i + 1) % 12] = h[(i + 1) % 12].rotate_left(ROTATIONS[i]);
    }
}

fn words(bytes: &[u8]) -> [u64; 12] {
    std::array::from_fn(|i| u64_at(bytes, 8 * i))
}

/// SpookyHash V2 `Hash128` of a message with the seeds `seed`: `(hash1, hash2)`.
pub fn hash128(message: &[u8], seed: (u64, u64)) -> (u64, u64) {
    if message.len() < LONG {
        return hash128_short(message, seed);
    }
    let mut h: [u64; 12] = std::array::from_fn(|i| match i % 3 {
        0 => seed.0,
        1 => seed.1,
        _ => SC_CONST,
    });
    let blocks = message.len() / BLOCK;
    for b in 0..blocks {
        mix(&words(&message[b * BLOCK..(b + 1) * BLOCK]), &mut h);
    }
    let rest = &message[blocks * BLOCK..];
    let mut last = [0u8; BLOCK];
    last[..rest.len()].copy_from_slice(rest);
    last[BLOCK - 1] = rest.len() as u8;
    for (v, d) in h.iter_mut().zip(words(&last)) {
        *v = v.wrapping_add(d);
    }
    end_partial(&mut h);
    end_partial(&mut h);
    end_partial(&mut h);
    (h[0], h[1])
}

#[cfg(test)]
mod tests {
    use super::*;

    const LONG_VECTORS: [(usize, (u64, u64), (u64, u64)); 7] = [
        (192, (0x0, 0x0), (0x02d13f94b2a31a54, 0x0ea393db758d85d3)),
        (193, (0x0, 0x0), (0x3000e51613f6e430, 0x226030278b4737a0)),
        (287, (0x1, 0x2), (0x3d7fc2599fcca286, 0xd06b2e14f6010a6b)),
        (288, (0x0, 0x0), (0x03d25f35f9d92644, 0x8b6bec13b6c91440)),
        (
            300,
            (0xdeadbeef, 0x12345678),
            (0xbb00e83cd873b99d, 0x7749f33c8a76a14d),
        ),
        (1000, (0x0, 0x0), (0x933a2fd941efd7fe, 0x0e436a670a76d2d8)),
        (4096, (0x7, 0x9), (0x10d23abbcb7eee55, 0xb231756856637dc1)),
    ];

    /// A message of `n` bytes: 0, 1, 2, ... (mod 251).
    fn message(n: usize) -> Vec<u8> {
        (0..n).map(|i| (i % 251) as u8).collect()
    }

    #[test]
    fn long_messages_hash_as_spooky_v2() {
        // Values of the reference algorithm (Bob Jenkins' SpookyV2.cpp Hash128).
        for (n, seed, expected) in LONG_VECTORS {
            assert_eq!(hash128(&message(n), seed), expected, "{n} bytes");
        }
    }

    #[test]
    fn short_messages_take_the_short_form() {
        for n in [0, 15, 16, 31, 32, 100, 191] {
            assert_eq!(
                hash128(&message(n), (1, 2)),
                hash128_short(&message(n), (1, 2))
            );
        }
    }
}
