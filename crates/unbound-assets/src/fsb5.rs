//! FMOD sound banks (FSB5), the form a player's AudioClip keeps its audio in (an AssetBundle's
//! `.resource` stream): the header, the sounds and their codecs, decoded as Unity reads them back
//! (`AudioClip.GetData` of a clip decompressed on load: 16-bit samples).
//!
//! Codecs Unity writes: PCM16, IMA ADPCM (FMOD's blocks of 64 frames: per channel a header sample
//! with its step index, then 63 nibbles, low nibble first, channels interleaved by four bytes) and
//! Vorbis. A Vorbis sound keeps its raw audio packets (each after its 16-bit length) and only the
//! CRC32 of the setup header FMOD's encoder wrote; the encoder is libvorbis, so the headers are made
//! again by libvorbis (`native/vorbis`, 1.3.7) at the qualities FMOD encodes with until the CRC
//! matches. Measured over rates, channels and qualities: every header at 32 kHz and above is
//! libvorbis 1.3.7's; below that FMOD's headers are its own, and such a sound fails with a message.
//! FMOD writes the decoded floats as 16-bit samples by truncating `x * 32767`.

use std::collections::HashMap;
use std::sync::{Arc, Mutex, OnceLock};

use anyhow::{Context, Result, bail};

/// The sample rates a sound's mode bits index.
const RATES: [u32; 11] = [4000, 8000, 11000, 11025, 16000, 22050, 24000, 32000, 44100, 48000, 96000];

/// A sound's codec (the bank's mode).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Codec {
    Pcm16,
    ImaAdpcm,
    Vorbis,
    /// Any other FMOD codec, by its number.
    Other(u32),
}

impl Codec {
    fn of(mode: u32) -> Self {
        match mode {
            2 => Self::Pcm16,
            7 => Self::ImaAdpcm,
            15 => Self::Vorbis,
            other => Self::Other(other),
        }
    }

    /// FMOD's name of the codec.
    pub fn name(self) -> &'static str {
        match self {
            Self::Pcm16 => "PCM16",
            Self::ImaAdpcm => "IMA ADPCM",
            Self::Vorbis => "Vorbis",
            Self::Other(mode) => match mode {
                1 => "PCM8",
                3 => "PCM24",
                4 => "PCM32",
                5 => "PCM float",
                6 => "GameCube ADPCM",
                8 => "VAG",
                9 => "HEVAG",
                10 => "XMA",
                11 => "MPEG",
                12 => "CELT",
                13 => "ATRAC9",
                14 => "XWMA",
                16 => "FADPCM",
                17 => "Opus",
                _ => "an unknown codec",
            },
        }
    }
}

/// A sound of a bank.
#[derive(Debug, Clone)]
pub struct Sound<'a> {
    pub codec: Codec,
    pub channels: usize,
    pub rate: u32,
    pub frames: usize,
    /// The sound's bytes in the bank's data.
    pub data: &'a [u8],
    /// A Vorbis sound's setup header CRC32.
    pub vorbis_crc: Option<u32>,
}

/// The decoded samples of a sound: interleaved, `frames * channels` of them, in `[-1, 1)`.
#[derive(Debug, Clone)]
pub struct Decoded {
    pub channels: usize,
    pub rate: u32,
    pub samples: Vec<f32>,
}

fn u32_at(bytes: &[u8], at: usize) -> Result<u32> {
    Ok(u32::from_le_bytes(bytes.get(at..at + 4).context("truncated FSB5 bank")?.try_into()?))
}

/// The sound `index` of a bank.
pub fn parse(bytes: &[u8], index: usize) -> Result<Sound<'_>> {
    if !bytes.starts_with(b"FSB5") {
        bail!("not an FSB5 bank");
    }
    let version = u32_at(bytes, 4)?;
    let count = u32_at(bytes, 8)? as usize;
    let headers_size = u32_at(bytes, 12)? as usize;
    let names_size = u32_at(bytes, 16)? as usize;
    let data_size = u32_at(bytes, 20)? as usize;
    let codec = Codec::of(u32_at(bytes, 24)?);
    let header_size = if version == 0 { 0x40 } else { 0x3C };
    if index >= count {
        bail!("the FSB5 bank has {count} sounds, not a sound {index}");
    }
    let data_start = header_size + headers_size + names_size;
    let data = bytes.get(data_start..data_start + data_size).context("truncated FSB5 bank")?;

    // Each sound: 64 mode bits (more chunks, rate, channels, data offset, frames), then its chunks.
    let mut at = header_size;
    let mut sounds = Vec::with_capacity(count);
    for _ in 0..count {
        let mode = u64::from_le_bytes(bytes.get(at..at + 8).context("truncated FSB5 bank")?.try_into()?);
        at += 8;
        let mut rate = *RATES.get(((mode >> 1) & 0xF) as usize).context("an FSB5 sound has an unknown rate")?;
        let mut channels = [1, 2, 6, 8][((mode >> 5) & 3) as usize];
        let offset = (((mode >> 7) & 0x7FF_FFFF) << 5) as usize;
        let frames = (mode >> 34) as usize;
        let mut vorbis_crc = None;
        let mut more = mode & 1 != 0;
        while more {
            let chunk = u32_at(bytes, at)?;
            at += 4;
            more = chunk & 1 != 0;
            let size = ((chunk >> 1) & 0xFF_FFFF) as usize;
            let body = bytes.get(at..at + size).context("truncated FSB5 bank")?;
            match chunk >> 25 {
                1 if size >= 1 => channels = body[0] as usize,
                2 if size >= 4 => rate = u32_at(body, 0)?,
                11 if size >= 4 => vorbis_crc = Some(u32_at(body, 0)?),
                _ => {}
            }
            at += size;
        }
        sounds.push((rate, channels, offset, frames, vorbis_crc));
    }
    let (rate, channels, offset, frames, vorbis_crc) = sounds[index];
    let end = sounds.get(index + 1).map_or(data.len(), |s| s.2);
    let data = data.get(offset..end.max(offset)).context("an FSB5 sound lies past the bank's data")?;
    Ok(Sound { codec, channels, rate, frames, data, vorbis_crc })
}

/// Decodes the sound `index` of a bank.
pub fn decode(bytes: &[u8], index: usize) -> Result<Decoded> {
    let sound = parse(bytes, index)?;
    let mut samples = match sound.codec {
        Codec::Pcm16 => sound
            .data
            .chunks_exact(2)
            .map(|b| i16::from_le_bytes([b[0], b[1]]) as f32 / 32768.0)
            .collect(),
        Codec::ImaAdpcm => ima_adpcm(sound.data, sound.channels),
        Codec::Vorbis => vorbis(&sound)?,
        other => bail!("{} audio (FMOD) is not supported", other.name()),
    };
    samples.resize(sound.frames * sound.channels, 0.0);
    Ok(Decoded { channels: sound.channels, rate: sound.rate, samples })
}

const IMA_STEPS: [i32; 89] = [
    7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107,
    118, 130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963,
    1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894,
    6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794,
    32767,
];
const IMA_INDEX: [i32; 8] = [-1, -1, -1, -1, 2, 4, 6, 8];

/// FMOD's IMA ADPCM: blocks of 36 bytes a channel, 64 frames each.
fn ima_adpcm(data: &[u8], channels: usize) -> Vec<f32> {
    let block = 36 * channels;
    let mut out = Vec::with_capacity(data.len() / block * 64 * channels);
    let mut frames = vec![[0i16; 64]; channels];
    for b in data.chunks_exact(block) {
        for (c, frame) in frames.iter_mut().enumerate() {
            let mut sample = i16::from_le_bytes([b[4 * c], b[4 * c + 1]]) as i32;
            let mut index = (b[4 * c + 2] as i32).min(88);
            frame[0] = sample as i16;
            let mut n = 1;
            // Eight groups of four bytes per channel, the channels' groups in turn.
            'groups: for g in 0..8 {
                let at = 4 * channels + (g * channels + c) * 4;
                for &byte in &b[at..at + 4] {
                    for nibble in [byte & 15, byte >> 4] {
                        if n == 64 {
                            break 'groups;
                        }
                        let step = IMA_STEPS[index as usize];
                        let mut diff = step >> 3;
                        if nibble & 1 != 0 {
                            diff += step >> 2;
                        }
                        if nibble & 2 != 0 {
                            diff += step >> 1;
                        }
                        if nibble & 4 != 0 {
                            diff += step;
                        }
                        if nibble & 8 != 0 {
                            diff = -diff;
                        }
                        sample = (sample + diff).clamp(-32768, 32767);
                        index = (index + IMA_INDEX[(nibble & 7) as usize]).clamp(0, 88);
                        frame[n] = sample as i16;
                        n += 1;
                    }
                }
            }
        }
        for i in 0..64 {
            for frame in &frames {
                out.push(frame[i] as f32 / 32768.0);
            }
        }
    }
    out
}

unsafe extern "C" {
    fn uav_headers(
        channels: i32,
        rate: std::ffi::c_long,
        quality: f32,
        ident: *mut u8,
        ident_cap: i32,
        ident_len: *mut i32,
        setup: *mut u8,
        setup_cap: i32,
        setup_len: *mut i32,
    ) -> i32;
    fn uav_decoder_new(ident: *const u8, ident_len: i32, setup: *const u8, setup_len: i32) -> *mut std::ffi::c_void;
    fn uav_decode(decoder: *mut std::ffi::c_void, data: *const u8, len: i32, out: *mut f32, cap: i32) -> i32;
    fn uav_decoder_free(decoder: *mut std::ffi::c_void);
}

/// The identification and setup headers libvorbis writes for a stream.
#[derive(Debug)]
struct Headers {
    ident: Vec<u8>,
    setup: Vec<u8>,
}

fn headers(channels: usize, rate: u32, quality: f32) -> Option<Headers> {
    let mut ident = vec![0u8; 64];
    let mut setup = vec![0u8; 1 << 16];
    let (mut ident_len, mut setup_len) = (0, 0);
    let result = unsafe {
        uav_headers(
            channels as i32,
            rate as std::ffi::c_long,
            quality,
            ident.as_mut_ptr(),
            ident.len() as i32,
            &mut ident_len,
            setup.as_mut_ptr(),
            setup.len() as i32,
            &mut setup_len,
        )
    };
    if result != 0 {
        return None;
    }
    ident.truncate(ident_len as usize);
    setup.truncate(setup_len as usize);
    Some(Headers { ident, setup })
}

/// The qualities to try, in the order FMOD's are likeliest: tenths, then hundredths, then
/// thousandths (every setup FMOD wrote in the measurements is reached at a hundredth).
fn qualities() -> impl Iterator<Item = f32> {
    let tenths = (-1..=10).map(|i| i * 100);
    let hundredths = (-10..=100).map(|i| i * 10).filter(|i| i % 100 != 0);
    let thousandths = (-100..=1000).filter(|i| i % 10 != 0);
    tenths.chain(hundredths).chain(thousandths).map(|i| i as f32 / 1000.0)
}

/// The headers whose setup has the CRC, by channels, rate and CRC (found once a process).
fn find_headers(channels: usize, rate: u32, crc: u32) -> Result<Arc<Headers>> {
    static FOUND: OnceLock<Mutex<HashMap<(usize, u32, u32), Option<Arc<Headers>>>>> = OnceLock::new();
    let found = FOUND.get_or_init(Default::default);
    if let Some(known) = found.lock().unwrap().get(&(channels, rate, crc)) {
        return known.clone().context("its setup header is not one libvorbis writes");
    }
    let headers = if rate < 32000 {
        None
    } else {
        qualities()
            .filter_map(|q| headers(channels, rate, q))
            .find(|h| crc32fast::hash(&h.setup) == crc)
            .map(Arc::new)
    };
    found.lock().unwrap().insert((channels, rate, crc), headers.clone());
    headers.with_context(|| {
        if rate < 32000 {
            format!("FMOD's Vorbis encoder writes setup headers of its own below 32 kHz (this sound: {rate} Hz); such clips are not supported")
        } else {
            format!("its setup header (CRC {crc:08x}) is not one libvorbis 1.3.7 writes for {channels} channels at {rate} Hz")
        }
    })
}

fn vorbis(sound: &Sound) -> Result<Vec<f32>> {
    let crc = sound.vorbis_crc.context("an FSB5 Vorbis sound without its setup chunk")?;
    let headers = find_headers(sound.channels, sound.rate, crc).context("a Vorbis clip (FMOD) does not decode")?;
    let decoder = unsafe {
        uav_decoder_new(headers.ident.as_ptr(), headers.ident.len() as i32, headers.setup.as_ptr(), headers.setup.len() as i32)
    };
    if decoder.is_null() {
        bail!("libvorbis rejects the headers made for a Vorbis clip");
    }
    struct Decoder(*mut std::ffi::c_void);
    impl Drop for Decoder {
        fn drop(&mut self) {
            unsafe { uav_decoder_free(self.0) }
        }
    }
    let decoder = Decoder(decoder);
    const MAX_FRAMES: usize = 8192;
    let mut block = vec![0f32; MAX_FRAMES * sound.channels];
    let mut out = Vec::with_capacity(sound.frames * sound.channels);
    let mut at = 0;
    while at + 2 <= sound.data.len() && out.len() < sound.frames * sound.channels {
        let len = u16::from_le_bytes([sound.data[at], sound.data[at + 1]]) as usize;
        at += 2;
        if len == 0 {
            break;
        }
        let packet = sound.data.get(at..at + len).context("a Vorbis packet lies past its sound's data")?;
        at += len;
        let frames = unsafe { uav_decode(decoder.0, packet.as_ptr(), len as i32, block.as_mut_ptr(), MAX_FRAMES as i32) };
        if frames < 0 {
            bail!("a Vorbis packet does not decode");
        }
        // As FMOD stores it: 16 bits, x * 32767 truncated.
        out.extend(
            block[..frames as usize * sound.channels]
                .iter()
                .map(|&x| (x * 32767.0).clamp(-32768.0, 32767.0) as i16 as f32 / 32768.0),
        );
    }
    out.truncate(sound.frames * sound.channels);
    Ok(out)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::bundle::Bundle;
    use unbound_yaml::Value;

    fn scalar<'a>(v: &'a Value, key: &str) -> &'a str {
        match v.as_map().and_then(|m| m.get(key)) {
            Some(Value::Scalar(s)) => s,
            _ => panic!("no {key}"),
        }
    }

    /// The Editor's bundle of `tests/projects/bundleaudio`: each clip decodes to the samples
    /// Unity's GetData read (the probe's every 37th), exactly but for Vorbis (FMOD's decoder
    /// differs from libvorbis by a unit in the last bit now and then); the 22 kHz Vorbis clip fails.
    #[test]
    fn decodes_the_editors_clips_as_unity_reads_them() {
        let root = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("../../tests/projects/bundleaudio/Reference");
        let bundle = Bundle::parse(&std::fs::read(root.join("bundles/audio")).unwrap()).unwrap();
        let reference: Vec<serde_json::Value> = std::fs::read_to_string(root.join("audio.jsonl"))
            .unwrap()
            .lines()
            .map(|l| serde_json::from_str(l).unwrap())
            .collect();
        let file = bundle.serialized_files().next().unwrap().path.clone();
        let stream = bundle.files.iter().find(|f| f.path.ends_with(".resource")).unwrap();
        let mut checked = 0;
        for doc in bundle.documents(&file).unwrap().into_iter().filter(|d| d.class_id == 83) {
            let name = scalar(&doc.body, "m_Name").to_string();
            let resource = doc.body.as_map().unwrap().get("m_Resource").unwrap();
            let offset: usize = scalar(resource, "m_Offset").parse().unwrap();
            let size: usize = scalar(resource, "m_Size").parse().unwrap();
            let decoded = decode(&stream.data[offset..offset + size], 0);
            let unity = reference.iter().find(|r| r["case"] == "clip" && r["name"] == name.as_str()).unwrap();
            if name == "vorbis30_mono_22050" {
                let error = format!("{:#}", decoded.unwrap_err());
                assert!(error.contains("below 32 kHz"), "{error}");
                continue;
            }
            let decoded = decoded.unwrap_or_else(|e| panic!("{name}: {e:#}"));
            assert_eq!(decoded.samples.len(), unity["samples"].as_u64().unwrap() as usize * decoded.channels, "{name}");
            if !unity["read"].as_bool().unwrap() {
                continue;
            }
            let tolerance = if name.starts_with("vorbis") { 1.5 / 32768.0 } else { 0.0 };
            for (i, want) in unity["picked"].as_array().unwrap().iter().enumerate() {
                let (got, want) = (decoded.samples[i * 37], want.as_f64().unwrap() as f32);
                assert!((got - want).abs() <= tolerance + 1e-7, "{name} sample {}: {got} vs Unity's {want}", i * 37);
            }
            checked += 1;
        }
        assert_eq!(checked, 7);
    }

    /// A full search (every quality FMOD's headers are tried at) stays well under a frame's worth
    /// of a second per channel count and rate.
    #[test]
    fn searches_the_qualities_quickly() {
        let start = std::time::Instant::now();
        let n = qualities().filter_map(|q| headers(2, 44100, q)).count();
        let elapsed = start.elapsed();
        assert!(n > 1000);
        // Tens of milliseconds; an encoder's analyser set up for each would take seconds.
        assert!(elapsed.as_secs_f64() < 1.5, "{n} headers in {elapsed:?}");
    }

    #[test]
    fn makes_the_headers_of_libvorbis() {
        // 44.1 kHz stereo at 0.7: the header FMOD wrote for Unity's quality 0.7.
        let h = headers(2, 44100, 0.7).unwrap();
        assert_eq!(crc32fast::hash(&h.setup), 0xa4f666fc);
        assert_eq!(&h.ident[1..7], b"vorbis");
    }
}
