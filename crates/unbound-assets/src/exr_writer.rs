//! OpenEXR images as the Unity Editor writes the files it bakes (matches the Unity 6000.0.71f1
//! Editor): one part of scan lines, the attributes the Editor writes in its order. A reflection's
//! image has half-float channels, a lightmap 32-bit float ones. A reflection's image is PIZ-compressed (OpenEXR's wavelet and Huffman coding, the code
//! lengths built with GNU libstdc++'s heap, as the Linux Editor's files have them:
//! tests/projects/bakeenvironment `written/ReflectionProbe-0.exr`), a lightmap ZIP-compressed
//! (OpenEXR's byte split and predictor, then zlib's deflate at its default level 6:
//! tests/projects/bakedbuffers `lightmap0.exr`).

/// How the Editor compresses an image it bakes.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Compression {
    /// A reflection's image.
    Piz,
    /// A lightmap.
    Zip,
}

impl Compression {
    /// OpenEXR's number for it.
    fn number(self) -> u8 {
        match self {
            Compression::Zip => 3,
            Compression::Piz => 4,
        }
    }

    /// Scan lines in a chunk.
    fn lines(self) -> usize {
        match self {
            Compression::Zip => 16,
            Compression::Piz => 32,
        }
    }
}

/// zlib's compression level the Editor's ZIP chunks have (its default).
const ZIP_LEVEL: i32 = 6;
/// A channel's samples, rows from the top: half floats (their bits) or 32-bit floats.
#[derive(Clone, Copy, Debug)]
pub enum Samples<'a> {
    Half(&'a [u16]),
    Float(&'a [f32]),
}

impl Samples<'_> {
    /// OpenEXR's pixel type.
    fn kind(self) -> i32 {
        match self {
            Samples::Half(_) => 1,
            Samples::Float(_) => 2,
        }
    }

    /// The bytes of samples `from..to`, as the file stores them.
    fn extend_bytes(self, out: &mut Vec<u8>, from: usize, to: usize) {
        match self {
            Samples::Half(v) => out.extend(v[from..to].iter().flat_map(|h| h.to_le_bytes())),
            Samples::Float(v) => out.extend(v[from..to].iter().flat_map(|f| f.to_le_bytes())),
        }
    }
}

/// An image to write: its size, its channels (in name order, as OpenEXR stores them), a sample
/// per pixel each, and its screenWindowWidth attribute (a reflection's 1, a lightmap's its
/// width).
pub struct Image<'a> {
    pub width: usize,
    pub height: usize,
    pub channels: &'a [(&'a str, Samples<'a>)],
    pub screen_window_width: f32,
}

/// The file's bytes.
pub fn write(image: &Image, compression: Compression) -> Vec<u8> {
    let mut out = Vec::new();
    out.extend([0x76, 0x2f, 0x31, 0x01]);
    out.extend(2u32.to_le_bytes()); // version 2, one part of scan lines
    let attribute = |out: &mut Vec<u8>, name: &str, kind: &str, value: &[u8]| {
        out.extend(name.as_bytes());
        out.push(0);
        out.extend(kind.as_bytes());
        out.push(0);
        out.extend((value.len() as i32).to_le_bytes());
        out.extend(value);
    };
    let mut channels = Vec::new();
    for (name, samples) in image.channels {
        channels.extend(name.as_bytes());
        channels.push(0);
        channels.extend(samples.kind().to_le_bytes());
        channels.extend([0u8; 4]); // linear 0, reserved
        channels.extend(1i32.to_le_bytes());
        channels.extend(1i32.to_le_bytes());
    }
    channels.push(0);
    let window: Vec<u8> = [0i32, 0, image.width as i32 - 1, image.height as i32 - 1]
        .iter()
        .flat_map(|v| v.to_le_bytes())
        .collect();
    attribute(&mut out, "channels", "chlist", &channels);
    attribute(
        &mut out,
        "compression",
        "compression",
        &[compression.number()],
    );
    attribute(&mut out, "dataWindow", "box2i", &window);
    attribute(&mut out, "displayWindow", "box2i", &window);
    attribute(&mut out, "lineOrder", "lineOrder", &[0]);
    attribute(&mut out, "pixelAspectRatio", "float", &1f32.to_le_bytes());
    attribute(&mut out, "screenWindowCenter", "v2f", &[0u8; 8]);
    attribute(
        &mut out,
        "screenWindowWidth",
        "float",
        &image.screen_window_width.to_le_bytes(),
    );
    out.push(0);
    let lines = compression.lines();
    let chunks = image.height.div_ceil(lines);
    let table = out.len();
    out.resize(table + 8 * chunks, 0);
    for chunk in 0..chunks {
        let first = chunk * lines;
        let rows = lines.min(image.height - first);
        let at = out.len() as u64;
        out[table + 8 * chunk..table + 8 * chunk + 8].copy_from_slice(&at.to_le_bytes());
        // The lines as the file stores them uncompressed: line by line, channel by channel.
        let mut raw = Vec::new();
        for y in first..first + rows {
            for (_, samples) in image.channels {
                samples.extend_bytes(&mut raw, y * image.width, (y + 1) * image.width);
            }
        }
        let compressed = match compression {
            Compression::Piz => {
                assert!(
                    image
                        .channels
                        .iter()
                        .all(|(_, s)| matches!(s, Samples::Half(_))),
                    "PIZ images of half floats only"
                );
                let words: Vec<u16> = raw
                    .chunks_exact(2)
                    .map(|b| u16::from_le_bytes([b[0], b[1]]))
                    .collect();
                piz_compress(&words, image.width, rows, image.channels.len())
            }
            Compression::Zip => zip_compress(&raw),
        };
        out.extend((first as i32).to_le_bytes());
        if compressed.len() < raw.len() {
            out.extend((compressed.len() as i32).to_le_bytes());
            out.extend(compressed);
        } else {
            // A chunk that does not get smaller is stored as it is.
            out.extend((raw.len() as i32).to_le_bytes());
            out.extend(raw);
        }
    }
    out
}

/// One chunk's lines ZIP-compressed: their bytes split into those at even and at odd offsets,
/// each byte then as its difference from the one before (plus 128, OpenEXR's predictor), all
/// deflated by zlib.
fn zip_compress(bytes: &[u8]) -> Vec<u8> {
    let half = bytes.len().div_ceil(2);
    let mut split = vec![0u8; bytes.len()];
    for (k, &b) in bytes.iter().enumerate() {
        split[if k % 2 == 0 { k / 2 } else { half + k / 2 }] = b;
    }
    for k in (1..split.len()).rev() {
        split[k] = split[k].wrapping_sub(split[k - 1]).wrapping_add(128);
    }
    deflate(&split)
}

/// `data` compressed by zlib (the C library: its output is what the Editor's files hold, which
/// other deflate implementations do not reproduce).
fn deflate(data: &[u8]) -> Vec<u8> {
    // SAFETY: compressBound and compress2 get valid buffers of the lengths given.
    unsafe {
        let mut length = libz_sys::compressBound(data.len() as libz_sys::uLong);
        let mut out = vec![0u8; length as usize];
        let status = libz_sys::compress2(
            out.as_mut_ptr(),
            &mut length,
            data.as_ptr(),
            data.len() as libz_sys::uLong,
            ZIP_LEVEL,
        );
        assert_eq!(status, libz_sys::Z_OK, "zlib's compress2 failed");
        out.truncate(length as usize);
        out
    }
}

/// One chunk's lines (line by line, channel by channel, `width` samples each) PIZ-compressed.
fn piz_compress(raw: &[u16], width: usize, rows: usize, channels: usize) -> Vec<u8> {
    // Channel by channel, each a `width` x `rows` plane.
    let mut planes = vec![0u16; raw.len()];
    for y in 0..rows {
        for c in 0..channels {
            let from = (y * channels + c) * width;
            let to = c * width * rows + y * width;
            planes[to..to + width].copy_from_slice(&raw[from..from + width]);
        }
    }
    // The values used, as a bitmap (zero always counts as used and is not stored).
    let mut bitmap = vec![0u8; 8192];
    for &v in &planes {
        bitmap[(v >> 3) as usize] |= 1 << (v & 7);
    }
    bitmap[0] &= !1;
    let (mut min, mut max) = (8191usize, 0usize);
    for (i, &b) in bitmap.iter().enumerate() {
        if b != 0 {
            min = min.min(i);
            max = max.max(i);
        }
    }
    let mut lut = vec![0u16; 65536];
    let mut k = 0u16;
    for (i, entry) in lut.iter_mut().enumerate() {
        if i == 0 || bitmap[i >> 3] & (1 << (i & 7)) != 0 {
            *entry = k;
            k += 1;
        }
    }
    let max_value = k - 1;
    for v in planes.iter_mut() {
        *v = lut[*v as usize];
    }
    let mut out = Vec::new();
    out.extend((min as u16).to_le_bytes());
    out.extend((max as u16).to_le_bytes());
    if min <= max {
        out.extend(&bitmap[min..=max]);
    }
    for c in 0..channels {
        let plane = &mut planes[c * width * rows..(c + 1) * width * rows];
        wav2_encode(plane, width, 1, rows, width, max_value);
    }
    let huffman = huf_compress(&planes);
    out.extend((huffman.len() as i32).to_le_bytes());
    out.extend(huffman);
    out
}

fn wenc14(a: u16, b: u16) -> (u16, u16) {
    let (a, b) = (a as i16 as i32, b as i16 as i32);
    (((a + b) >> 1) as i16 as u16, (a - b) as i16 as u16)
}

fn wenc16(a: u16, b: u16) -> (u16, u16) {
    const A_OFFSET: i32 = 1 << 15;
    const M_OFFSET: i32 = 1 << 15;
    const MOD_MASK: i32 = (1 << 16) - 1;
    let ao = (a as i32 + A_OFFSET) & MOD_MASK;
    let mut m = (ao + b as i32) >> 1;
    let mut d = ao - b as i32;
    if d < 0 {
        m = (m + M_OFFSET) & MOD_MASK;
    }
    d &= MOD_MASK;
    (m as u16, d as u16)
}

/// OpenEXR's 2D Haar-like wavelet of a plane (`nx` x `ny` values, `ox`/`oy` apart).
fn wav2_encode(v: &mut [u16], nx: usize, ox: usize, ny: usize, oy: usize, max: u16) {
    let w14 = max < (1 << 14);
    let enc = |a: u16, b: u16| if w14 { wenc14(a, b) } else { wenc16(a, b) };
    let n = nx.min(ny);
    let mut p = 1usize;
    let mut p2 = 2usize;
    while p2 <= n {
        let (oy1, oy2, ox1, ox2) = (oy * p, oy * p2, ox * p, ox * p2);
        let mut py = 0usize;
        let ey = oy * (ny - p2);
        while py <= ey {
            let mut px = py;
            let ex = py + ox * (nx - p2);
            while px <= ex {
                let p01 = px + ox1;
                let p10 = px + oy1;
                let p11 = p10 + ox1;
                let (i00, i01) = enc(v[px], v[p01]);
                let (i10, i11) = enc(v[p10], v[p11]);
                let (a, b) = enc(i00, i10);
                v[px] = a;
                v[p10] = b;
                let (a, b) = enc(i01, i11);
                v[p01] = a;
                v[p11] = b;
                px += ox2;
            }
            if nx & p != 0 {
                let p10 = px + oy1;
                let (i00, h) = enc(v[px], v[p10]);
                v[p10] = h;
                v[px] = i00;
            }
            py += oy2;
        }
        if ny & p != 0 {
            let mut px = py;
            let ex = py + ox * (nx - p2);
            while px <= ex {
                let p01 = px + ox1;
                let (i00, h) = enc(v[px], v[p01]);
                v[p01] = h;
                v[px] = i00;
                px += ox2;
            }
        }
        p = p2;
        p2 <<= 1;
    }
}

const HUF_ENCSIZE: usize = (1 << 16) + 1;
const SHORT_ZEROCODE_RUN: u64 = 59;
const LONG_ZEROCODE_RUN: u64 = 63;
const SHORTEST_LONG_RUN: u64 = 2 + LONG_ZEROCODE_RUN - SHORT_ZEROCODE_RUN;
const LONGEST_LONG_RUN: u64 = 255 + SHORTEST_LONG_RUN;

/// Bits written most significant first.
struct Bits {
    out: Vec<u8>,
    c: u64,
    lc: u32,
}

impl Bits {
    fn put(&mut self, n: u32, bits: u64) {
        self.c = self.c.wrapping_shl(n) | bits;
        self.lc += n;
        while self.lc >= 8 {
            self.lc -= 8;
            self.out.push((self.c >> self.lc) as u8);
        }
    }

    fn code(&mut self, code: u64) {
        self.put((code & 63) as u32, code >> 6);
    }
}

/// OpenEXR's Huffman coding of 16-bit values.
fn huf_compress(raw: &[u16]) -> Vec<u8> {
    if raw.is_empty() {
        return Vec::new();
    }
    let mut freq = vec![0u64; HUF_ENCSIZE];
    for &v in raw {
        freq[v as usize] += 1;
    }
    let (im, i_m) = build_enc_table(&mut freq);
    let mut table = Bits {
        out: Vec::new(),
        c: 0,
        lc: 0,
    };
    let mut i = im;
    while i <= i_m {
        let l = freq[i] & 63;
        if l == 0 {
            let mut zerun = 1u64;
            while i < i_m && zerun < LONGEST_LONG_RUN {
                if freq[i + 1] & 63 > 0 {
                    break;
                }
                i += 1;
                zerun += 1;
            }
            if zerun >= 2 {
                if zerun >= SHORTEST_LONG_RUN {
                    table.put(6, LONG_ZEROCODE_RUN);
                    table.put(8, zerun - SHORTEST_LONG_RUN);
                } else {
                    table.put(6, SHORT_ZEROCODE_RUN + zerun - 2);
                }
                i += 1;
                continue;
            }
        }
        table.put(6, l);
        i += 1;
    }
    if table.lc > 0 {
        table.out.push((table.c << (8 - table.lc)) as u8);
    }
    let mut data = Bits {
        out: Vec::new(),
        c: 0,
        lc: 0,
    };
    let send = |data: &mut Bits, s_code: u64, run: i32, run_code: u64| {
        if (s_code & 63) + (run_code & 63) + 8 < (s_code & 63) * run as u64 {
            data.code(s_code);
            data.code(run_code);
            data.put(8, run as u64);
        } else {
            for _ in 0..=run {
                data.code(s_code);
            }
        }
    };
    let rlc = i_m;
    let mut s = raw[0] as usize;
    let mut cs = 0i32;
    for &v in &raw[1..] {
        if s == v as usize && cs < 255 {
            cs += 1;
        } else {
            send(&mut data, freq[s], cs, freq[rlc]);
            cs = 0;
        }
        s = v as usize;
    }
    send(&mut data, freq[s], cs, freq[rlc]);
    let n_bits = data.out.len() as u64 * 8 + data.lc as u64;
    if data.lc > 0 {
        data.out.push((data.c << (8 - data.lc)) as u8);
    }
    let mut out = Vec::new();
    out.extend((im as u32).to_le_bytes());
    out.extend((i_m as u32).to_le_bytes());
    out.extend((table.out.len() as u32).to_le_bytes());
    out.extend((n_bits as u32).to_le_bytes());
    out.extend(0u32.to_le_bytes());
    out.extend(table.out);
    out.extend(data.out);
    out
}

/// Replaces the frequencies with the canonical codes (length in the low 6 bits); the first and
/// one past the last code used (the last: the run-length code).
fn build_enc_table(freq: &mut [u64]) -> (usize, usize) {
    let mut im = 0;
    while freq[im] == 0 {
        im += 1;
    }
    let mut hlink: Vec<usize> = (0..HUF_ENCSIZE).collect();
    let mut f_heap: Vec<usize> = Vec::new();
    let mut i_m = im;
    for i in im..HUF_ENCSIZE {
        if freq[i] != 0 {
            f_heap.push(i);
            i_m = i;
        }
    }
    i_m += 1;
    freq[i_m] = 1;
    f_heap.push(i_m);
    // A min-heap by frequency: `less(a, b)` is the comparator's "a after b".
    let mut nf = f_heap.len();
    make_heap(&mut f_heap[..nf], freq);
    let mut scode = vec![0u64; HUF_ENCSIZE];
    while nf > 1 {
        let mm = f_heap[0];
        pop_heap(&mut f_heap[..nf], freq);
        nf -= 1;
        let m = f_heap[0];
        pop_heap(&mut f_heap[..nf], freq);
        freq[m] += freq[mm];
        push_heap(&mut f_heap[..nf], freq);
        let mut j = m;
        loop {
            scode[j] += 1;
            if hlink[j] == j {
                hlink[j] = mm;
                break;
            }
            j = hlink[j];
        }
        let mut j = mm;
        loop {
            scode[j] += 1;
            if hlink[j] == j {
                break;
            }
            j = hlink[j];
        }
    }
    // Canonical codes.
    let mut n = [0u64; 59];
    for &l in &scode {
        n[l as usize] += 1;
    }
    let mut c = 0u64;
    for i in (1..=58).rev() {
        let nc = (c + n[i]) >> 1;
        n[i] = c;
        c = nc;
    }
    for i in 0..HUF_ENCSIZE {
        let l = scode[i];
        freq[i] = if l > 0 {
            let code = l | (n[l as usize] << 6);
            n[l as usize] += 1;
            code
        } else {
            0
        };
    }
    (im, i_m)
}

/// The heap's comparator: `a` comes after `b` (a greater frequency).
fn comp(freq: &[u64], a: usize, b: usize) -> bool {
    freq[a] > freq[b]
}

// The heap operations of GNU libstdc++ (`std::make_heap`, `pop_heap`, `push_heap`), which the
// Linux Editor's files follow (equal frequencies are ordered by them). The macOS Editor's C++
// library orders them otherwise: not measured, not followed.

fn make_heap(h: &mut [usize], freq: &[u64]) {
    let len = h.len();
    if len < 2 {
        return;
    }
    let mut parent = (len - 2) / 2;
    loop {
        let value = h[parent];
        adjust_heap(h, parent, len, value, freq);
        if parent == 0 {
            return;
        }
        parent -= 1;
    }
}

fn pop_heap(h: &mut [usize], freq: &[u64]) {
    let len = h.len();
    if len < 2 {
        return;
    }
    let last = len - 1;
    let value = h[last];
    h[last] = h[0];
    adjust_heap(h, 0, last, value, freq);
}

fn push_heap(h: &mut [usize], freq: &[u64]) {
    let len = h.len();
    if len > 0 {
        let value = h[len - 1];
        sift_up(h, len - 1, 0, value, freq);
    }
}

fn sift_up(h: &mut [usize], mut hole: usize, top: usize, value: usize, freq: &[u64]) {
    while hole > top {
        let parent = (hole - 1) / 2;
        if !comp(freq, h[parent], value) {
            break;
        }
        h[hole] = h[parent];
        hole = parent;
    }
    h[hole] = value;
}

fn adjust_heap(h: &mut [usize], mut hole: usize, len: usize, value: usize, freq: &[u64]) {
    let top = hole;
    let mut second = hole;
    while second < (len - 1) / 2 {
        second = 2 * (second + 1);
        if comp(freq, h[second], h[second - 1]) {
            second -= 1;
        }
        h[hole] = h[second];
        hole = second;
    }
    if len & 1 == 0 && second == (len - 2) / 2 {
        second = 2 * (second + 1);
        h[hole] = h[second - 1];
        hole = second - 1;
    }
    sift_up(h, hole, top, value, freq);
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A channel read back: half floats (bits) or 32-bit floats.
    enum Plane {
        Half(Vec<u16>),
        Float(Vec<f32>),
    }

    /// An Editor's file read back: its size and its channels.
    fn planes(bytes: &[u8]) -> ((usize, usize), Vec<(String, Plane)>) {
        use exr::prelude::*;
        let read = read()
            .no_deep_data()
            .largest_resolution_level()
            .all_channels()
            .first_valid_layer()
            .all_attributes()
            .from_buffered(std::io::Cursor::new(bytes))
            .unwrap();
        let size = read.layer_data.size;
        let planes = read
            .layer_data
            .channel_data
            .list
            .iter()
            .map(|c| {
                let samples = match &c.sample_data {
                    FlatSamples::F16(v) => Plane::Half(v.iter().map(|h| h.to_bits()).collect()),
                    FlatSamples::F32(v) => Plane::Float(v.clone()),
                    FlatSamples::U32(_) => panic!("not floats"),
                };
                (c.name.to_string(), samples)
            })
            .collect();
        ((size.0, size.1), planes)
    }

    fn samples(planes: &[(String, Plane)]) -> Vec<(&str, Samples<'_>)> {
        planes
            .iter()
            .map(|(name, plane)| {
                let samples = match plane {
                    Plane::Half(v) => Samples::Half(v),
                    Plane::Float(v) => Samples::Float(v),
                };
                (name.as_str(), samples)
            })
            .collect()
    }

    #[test]
    fn the_editors_lightmaps_are_written_again_byte_for_byte() {
        let root = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../tests/projects/bakedbuffers/Reference/unity-6000.0.71f1");
        let mut checked = 0;
        for scene in std::fs::read_dir(&root).unwrap() {
            let path = scene.unwrap().path().join("lightmap0.exr");
            let Ok(bytes) = std::fs::read(&path) else {
                continue;
            };
            let ((width, height), planes) = planes(&bytes);
            let channels = samples(&planes);
            let made = write(
                &Image {
                    width,
                    height,
                    channels: &channels,
                    screen_window_width: width as f32,
                },
                Compression::Zip,
            );
            let first = made.iter().zip(&bytes).position(|(a, b)| a != b);
            assert!(
                made == bytes,
                "{}: {} bytes, expected {}; first difference at {first:?}",
                path.display(),
                made.len(),
                bytes.len()
            );
            checked += 1;
        }
        assert!(checked >= 20, "{checked} lightmaps");
    }

    #[test]
    fn the_editors_reflection_image_is_written_again_byte_for_byte() {
        let path = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join(
            "../../tests/projects/bakeenvironment/Reference/unity-6000.0.71f1/EnvSky/written/ReflectionProbe-0.exr",
        );
        let bytes = std::fs::read(path).unwrap();
        let ((width, height), planes) = planes(&bytes);
        let channels = samples(&planes);
        let image = Image {
            width,
            height,
            channels: &channels,
            screen_window_width: 1.0,
        };
        let made = write(&image, Compression::Piz);
        let first = made.iter().zip(&bytes).position(|(a, b)| a != b);
        assert!(
            made == bytes,
            "{} bytes, expected {}; first difference at {first:?}",
            made.len(),
            bytes.len()
        );
    }
}
