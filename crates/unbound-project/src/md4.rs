//! MD4 (RFC 1320).

/// MD4 (RFC 1320), which the Editor hashes several identities with (sprite atlas packing seeds,
/// package root folder GUIDs).
pub fn md4(message: &[u8]) -> [u8; 16] {
    let mut h: [u32; 4] = [0x6745_2301, 0xefcd_ab89, 0x98ba_dcfe, 0x1032_5476];
    let mut data = message.to_vec();
    let bits = (message.len() as u64).wrapping_mul(8);
    data.push(0x80);
    while data.len() % 64 != 56 {
        data.push(0);
    }
    data.extend_from_slice(&bits.to_le_bytes());
    for block in data.chunks_exact(64) {
        let x: Vec<u32> = block
            .chunks_exact(4)
            .map(|c| u32::from_le_bytes(c.try_into().unwrap()))
            .collect();
        let [mut a, mut b, mut c, mut d] = h;
        let f = |x: u32, y: u32, z: u32| (x & y) | (!x & z);
        let g = |x: u32, y: u32, z: u32| (x & y) | (x & z) | (y & z);
        let hh = |x: u32, y: u32, z: u32| x ^ y ^ z;
        for i in 0..16 {
            let s = [3, 7, 11, 19][i % 4];
            let t = a.wrapping_add(f(b, c, d)).wrapping_add(x[i]).rotate_left(s);
            (a, b, c, d) = (d, t, b, c);
        }
        for i in 0..16 {
            let s = [3, 5, 9, 13][i % 4];
            let k = (i % 4) * 4 + i / 4;
            let t = a
                .wrapping_add(g(b, c, d))
                .wrapping_add(x[k])
                .wrapping_add(0x5a82_7999)
                .rotate_left(s);
            (a, b, c, d) = (d, t, b, c);
        }
        const ORDER: [usize; 16] = [0, 8, 4, 12, 2, 10, 6, 14, 1, 9, 5, 13, 3, 11, 7, 15];
        for (i, &k) in ORDER.iter().enumerate() {
            let s = [3, 9, 11, 15][i % 4];
            let t = a
                .wrapping_add(hh(b, c, d))
                .wrapping_add(x[k])
                .wrapping_add(0x6ed9_eba1)
                .rotate_left(s);
            (a, b, c, d) = (d, t, b, c);
        }
        h = [
            h[0].wrapping_add(a),
            h[1].wrapping_add(b),
            h[2].wrapping_add(c),
            h[3].wrapping_add(d),
        ];
    }
    let mut out = [0u8; 16];
    for (i, v) in h.iter().enumerate() {
        out[i * 4..i * 4 + 4].copy_from_slice(&v.to_le_bytes());
    }
    out
}

#[cfg(test)]
mod tests {
    use super::md4;

    #[test]
    fn matches_the_rfc_vectors() {
        assert_eq!(
            md4(b"abc"),
            [
                0xa4, 0x48, 0x01, 0x7a, 0xaf, 0x21, 0xd8, 0x52, 0x5f, 0xc1, 0x0a, 0xe8, 0x7a, 0xa6,
                0x72, 0x9d
            ]
        );
        assert_eq!(md4(b"")[..4], [0x31, 0xd6, 0xcf, 0xe0]);
        // Longer than one block.
        assert_eq!(
            md4(
                b"12345678901234567890123456789012345678901234567890123456789012345678901234567890"
            ),
            [
                0xe3, 0x3b, 0x4d, 0xdc, 0x9c, 0x38, 0xf2, 0x19, 0x9c, 0x3e, 0x7b, 0x16, 0x4f, 0xcc,
                0x05, 0x36
            ]
        );
    }
}
