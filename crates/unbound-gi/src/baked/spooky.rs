//! Bob Jenkins' SpookyHash V2 (public domain), the 128-bit hash of a short message (under 192
//! bytes): what keys the Lightmap Parameters a baked atlas groups by.

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
