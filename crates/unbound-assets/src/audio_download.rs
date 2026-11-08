//! Audio files downloaded while playing (`UnityWebRequestMultimedia.GetAudioClip`), decoded as
//! the Unity 6000.0.71f1 Editor decodes them (tests/projects/networkcaching):
//!
//! The `AudioType` names the container and only that container decodes (`UNKNOWN` takes any):
//! a WAV request for an Ogg file fails like data that is no audio. An Ogg Vorbis clip is as long
//! as its last page's granule position. An MPEG file decodes whole, encoder delay and padding
//! included; an Info header (a CBR file's) decodes as a silent frame, a Xing header (a VBR
//! file's) does not. An MPEG clip with a Xing header is as long as the frames that header
//! counts; one without (with or without an Info header) is as long as the frames in the file,
//! the Info frame included, plus two, the missing samples being silence. MPEG and Vorbis audio
//! comes out at 16-bit precision, MPEG truncated, Vorbis rounded. IMA ADPCM WAV data
//! decodes with the classic shift-and-add step (`step/8 + step/4 + step/2 + step` by bit). A compressed WAV file (IMA ADPCM) is as long as its `fact` chunk says, not its
//! padded last block. A partial WAV download names its whole length in its header, which a
//! streamed clip takes before the rest arrives.

use anyhow::{Result, bail};
use std::path::Path;

/// The containers `AudioType` names that downloads decode.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Container {
    /// `AudioType.UNKNOWN`: whatever the data is.
    Any,
    Aiff,
    Mpeg,
    Ogg,
    Wav,
}

impl Container {
    /// The container of a Unity `AudioType` value; None for the types Unbound does not decode
    /// (tracker modules, AAC, XMA, VAG, AudioQueue).
    pub fn of_audio_type(audio_type: i32) -> Option<Container> {
        Some(match audio_type {
            0 => Container::Any,
            2 => Container::Aiff,
            13 => Container::Mpeg,
            14 => Container::Ogg,
            20 => Container::Wav,
            _ => return None,
        })
    }

    fn extension(self) -> &'static str {
        match self {
            Container::Any => "",
            Container::Aiff => "aiff",
            Container::Mpeg => "mp3",
            Container::Ogg => "ogg",
            Container::Wav => "wav",
        }
    }

    /// Whether the data starts as this container does.
    fn holds(self, data: &[u8]) -> bool {
        match self {
            Container::Any => true,
            Container::Wav => data.len() >= 12 && &data[..4] == b"RIFF" && &data[8..12] == b"WAVE",
            Container::Aiff => {
                data.len() >= 12
                    && &data[..4] == b"FORM"
                    && (&data[8..12] == b"AIFF" || &data[8..12] == b"AIFC")
            }
            Container::Ogg => data.starts_with(b"OggS"),
            Container::Mpeg => {
                let start = id3_size(data);
                data.len() >= start + 4 && frame(&data[start..]).is_some()
            }
        }
    }
}

/// How a downloaded file's audio is coded.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Codec {
    Pcm,
    Vorbis,
    ImaAdpcm,
    Mpeg,
}

/// A decoded download: its channels, rate, interleaved samples and codec.
pub struct Decoded {
    pub channels: u16,
    pub rate: u32,
    pub samples: Vec<f32>,
    pub codec: Codec,
}

/// Decodes a downloaded file of the container `container`.
pub fn decode(data: &[u8], container: Container) -> Result<Decoded> {
    if !container.holds(data) {
        bail!("the data is not a {container:?} file");
    }
    if Container::Wav.holds(data)
        && let Some(decoded) = ima_adpcm(data)
    {
        return Ok(decoded);
    }
    let hint = Path::new("download").with_extension(container.extension());
    let detected = |c: Container| container == c || (container == Container::Any && c.holds(data));
    let mpeg = detected(Container::Mpeg);
    let (channels, rate, mut samples) = crate::audio::decode_packets(&hint, data, mpeg)?;
    let mut codec = Codec::Pcm;
    if mpeg {
        codec = Codec::Mpeg;
        if let Some((frames, info_frame)) = mpeg_length(data) {
            if let Some(silence) = info_frame {
                samples.splice(0..0, std::iter::repeat_n(0.0, silence * channels as usize));
            }
            samples.resize(frames * channels as usize, 0.0);
        }
        quantize(&mut samples, f32::trunc);
    } else if detected(Container::Ogg) {
        codec = Codec::Vorbis;
        quantize(&mut samples, f32::round);
    }
    if Container::Wav.holds(data)
        && let Some(frames) = wav_fact_length(data)
    {
        samples.truncate(frames * channels as usize);
    }
    Ok(Decoded {
        channels,
        rate,
        samples,
        codec,
    })
}

/// Brings samples to 16-bit precision, as compressed downloads come out: MPEG audio truncated,
/// Vorbis rounded.
fn quantize(samples: &mut [f32], to_integer: fn(f32) -> f32) {
    const SCALE: f32 = 32768.0;
    for sample in samples {
        *sample = to_integer(*sample * SCALE).clamp(i16::MIN as f32, i16::MAX as f32) / SCALE;
    }
}

/// The chunks of a RIFF WAVE file: (id, body).
fn wav_chunks(data: &[u8]) -> impl Iterator<Item = (&[u8], &[u8])> {
    let mut at = 12;
    std::iter::from_fn(move || {
        let id = data.get(at..at + 4)?;
        let size = u32::from_le_bytes(data.get(at + 4..at + 8)?.try_into().ok()?) as usize;
        let body = data.get(at + 8..(at + 8).saturating_add(size).min(data.len()))?;
        at = at + 8 + size + (size & 1);
        Some((id, body))
    })
}

/// WAV format tags whose frames have a fixed size.
const WAV_PCM: u16 = 1;
const WAV_FLOAT: u16 = 3;
const WAV_EXTENSIBLE: u16 = 0xfffe;

/// The length a compressed WAV file's `fact` chunk gives, None for PCM and float data.
fn wav_fact_length(data: &[u8]) -> Option<usize> {
    let (_, fmt) = wav_chunks(data).find(|(id, _)| *id == b"fmt ")?;
    let tag = u16::from_le_bytes(fmt.get(..2)?.try_into().ok()?);
    if matches!(tag, WAV_PCM | WAV_FLOAT | WAV_EXTENSIBLE) {
        return None;
    }
    let (_, fact) = wav_chunks(data).find(|(id, _)| *id == b"fact")?;
    Some(u32::from_le_bytes(fact.get(..4)?.try_into().ok()?) as usize)
}

/// The channels, rate and length in frames a partial download announces, when its header is
/// all there (WAV only).
pub fn header(data: &[u8], container: Container) -> Option<(u16, u32, u64)> {
    if !matches!(container, Container::Wav | Container::Any) || !Container::Wav.holds(data) {
        return None;
    }
    let mut format = None;
    let mut at = 12;
    // The data chunk's size is in its header, before its (partial) body.
    while let (Some(id), Some(size)) = (data.get(at..at + 4), data.get(at + 4..at + 8)) {
        let size = u32::from_le_bytes(size.try_into().ok()?) as usize;
        if id == b"fmt " {
            let fmt = data.get(at + 8..at + 24)?;
            let channels = u16::from_le_bytes([fmt[2], fmt[3]]);
            let rate = u32::from_le_bytes(fmt[4..8].try_into().ok()?);
            let align = u16::from_le_bytes([fmt[12], fmt[13]]) as u64;
            let bits = u16::from_le_bytes([fmt[14], fmt[15]]);
            let tag = u16::from_le_bytes([fmt[0], fmt[1]]);
            format = Some((channels, rate, align, bits, tag));
        } else if id == b"data" {
            let (channels, rate, align, bits, tag) = format?;
            // Only PCM and float data have a fixed frame size to count frames by.
            if align == 0 || bits == 0 || !matches!(tag, WAV_PCM | WAV_FLOAT | WAV_EXTENSIBLE) {
                return None;
            }
            return Some((channels, rate, size as u64 / align));
        }
        at = at + 8 + size + (size & 1);
    }
    None
}

/// The length of an ID3v2 tag at the start, 0 without one.
fn id3_size(data: &[u8]) -> usize {
    if data.len() < 10 || &data[..3] != b"ID3" {
        return 0;
    }
    let size = data[6..10]
        .iter()
        .fold(0usize, |size, &b| (size << 7) | (b & 0x7f) as usize);
    let footer = if data[5] & 0x10 != 0 { 10 } else { 0 };
    10 + size + footer
}

/// An MPEG audio frame header: its size in bytes, samples per frame and the offset of a Xing or
/// Info header in it (after the side information).
fn frame(data: &[u8]) -> Option<(usize, usize, usize)> {
    let header = u32::from_be_bytes(data.get(..4)?.try_into().ok()?);
    if header >> 21 != 0x7ff {
        return None;
    }
    let version = (header >> 19) & 3; // 3 MPEG-1, 2 MPEG-2, 0 MPEG-2.5
    let layer = (header >> 17) & 3; // 1 Layer III, 2 Layer II, 3 Layer I
    let bitrate_index = ((header >> 12) & 15) as usize;
    let rate_index = ((header >> 10) & 3) as usize;
    let padding = ((header >> 9) & 1) as usize;
    let mono = (header >> 6) & 3 == 3;
    if version == 1 || layer == 0 || bitrate_index == 0 || bitrate_index == 15 || rate_index == 3 {
        return None;
    }
    const RATES: [[u32; 3]; 4] = [
        [11025, 12000, 8000],
        [0, 0, 0],
        [22050, 24000, 16000],
        [44100, 48000, 32000],
    ];
    const MPEG1_LAYER3: [u32; 15] = [
        0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320,
    ];
    const MPEG1_LAYER2: [u32; 15] = [
        0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384,
    ];
    const MPEG1_LAYER1: [u32; 15] = [
        0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448,
    ];
    const MPEG2_LAYER1: [u32; 15] = [
        0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256,
    ];
    const MPEG2_LAYER23: [u32; 15] = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
    let mpeg1 = version == 3;
    let kbps = match (mpeg1, layer) {
        (true, 1) => MPEG1_LAYER3,
        (true, 2) => MPEG1_LAYER2,
        (true, _) => MPEG1_LAYER1,
        (false, 3) => MPEG2_LAYER1,
        (false, _) => MPEG2_LAYER23,
    }[bitrate_index];
    let rate = RATES[version as usize][rate_index];
    let bitrate = kbps * 1000;
    let (size, samples) = match layer {
        3 => ((12 * bitrate / rate) as usize * 4 + padding * 4, 384),
        2 => ((144 * bitrate / rate) as usize + padding, 1152),
        _ if mpeg1 => ((144 * bitrate / rate) as usize + padding, 1152),
        _ => ((72 * bitrate / rate) as usize + padding, 576),
    };
    let side = match (mpeg1, mono) {
        (true, false) => 32,
        (true, true) => 17,
        (false, false) => 17,
        (false, true) => 9,
    };
    Some((size, samples, 4 + side))
}

/// The length in frames of an MPEG file (see the module's documentation), and the samples of
/// the silent frame its Info header decodes as, if it has one.
fn mpeg_length(data: &[u8]) -> Option<(usize, Option<usize>)> {
    let mut at = id3_size(data);
    let (first_size, samples, tag) = frame(data.get(at..)?)?;
    let first = &data[at..(at + first_size).min(data.len())];
    let header = first.get(tag..tag + 4);
    if header == Some(b"Xing") {
        let flags = u32::from_be_bytes(first.get(tag + 4..tag + 8)?.try_into().ok()?);
        if flags & 1 != 0 {
            let count = u32::from_be_bytes(first.get(tag + 8..tag + 12)?.try_into().ok()?);
            return Some((count as usize * samples, None));
        }
    }
    let info_frame = (header == Some(b"Info")).then_some(samples);
    let mut frames = 0usize;
    while let Some((size, _, _)) = data.get(at..).and_then(frame) {
        if size == 0 {
            break;
        }
        frames += 1;
        at += size;
    }
    // Two frames beyond the file's own.
    const EXTRA_FRAMES: usize = 2;
    Some(((frames + EXTRA_FRAMES) * samples, info_frame))
}

/// The IMA ADPCM step sizes and index changes (the standard tables).
const IMA_STEPS: [i32; 89] = [
    7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66,
    73, 80, 88, 97, 107, 118, 130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449,
    494, 544, 598, 658, 724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272,
    2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493,
    10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
];
const IMA_INDEX: [i32; 8] = [-1, -1, -1, -1, 2, 4, 6, 8];
const WAV_IMA_ADPCM: u16 = 0x11;

/// One IMA ADPCM channel's decoder state.
struct Ima {
    sample: i32,
    index: i32,
}

impl Ima {
    fn next(&mut self, nibble: u8) -> f32 {
        let step = IMA_STEPS[self.index as usize];
        let mut diff = step >> 3;
        if nibble & 4 != 0 {
            diff += step;
        }
        if nibble & 2 != 0 {
            diff += step >> 1;
        }
        if nibble & 1 != 0 {
            diff += step >> 2;
        }
        self.sample = if nibble & 8 != 0 {
            self.sample - diff
        } else {
            self.sample + diff
        }
        .clamp(i16::MIN as i32, i16::MAX as i32);
        self.index = (self.index + IMA_INDEX[(nibble & 7) as usize]).clamp(0, 88);
        self.sample as f32 / 32768.0
    }
}

/// An IMA ADPCM WAV file decoded (None for other WAV data): each block holds a header per
/// channel (the first sample and step index), then groups of four bytes per channel, low
/// nibble first; the `fact` chunk gives the length.
fn ima_adpcm(data: &[u8]) -> Option<Decoded> {
    let (_, fmt) = wav_chunks(data).find(|(id, _)| *id == b"fmt ")?;
    if fmt.len() < 16 || u16::from_le_bytes([fmt[0], fmt[1]]) != WAV_IMA_ADPCM {
        return None;
    }
    let channels = u16::from_le_bytes([fmt[2], fmt[3]]) as usize;
    let rate = u32::from_le_bytes(fmt[4..8].try_into().ok()?);
    let align = u16::from_le_bytes([fmt[12], fmt[13]]) as usize;
    if channels == 0 || align <= 4 * channels {
        return None;
    }
    let (_, body) = wav_chunks(data).find(|(id, _)| *id == b"data")?;
    let mut samples = Vec::new();
    for block in body.chunks(align) {
        if block.len() < 4 * channels {
            break;
        }
        let mut states: Vec<Ima> = (0..channels)
            .map(|c| Ima {
                sample: i16::from_le_bytes([block[4 * c], block[4 * c + 1]]) as i32,
                index: (block[4 * c + 2] as i32).clamp(0, 88),
            })
            .collect();
        for state in &states {
            samples.push(state.sample as f32 / 32768.0);
        }
        let words = &block[4 * channels..];
        let groups = words.len() / (4 * channels);
        let start = samples.len();
        samples.resize(start + groups * 8 * channels, 0.0);
        for group in 0..groups {
            for (c, state) in states.iter_mut().enumerate() {
                let bytes = &words[(group * channels + c) * 4..][..4];
                for (i, byte) in bytes.iter().enumerate() {
                    let frame = start + (group * 8 + i * 2) * channels;
                    samples[frame + c] = state.next(byte & 0x0f);
                    samples[frame + channels + c] = state.next(byte >> 4);
                }
            }
        }
    }
    if let Some(frames) = wav_fact_length(data) {
        samples.truncate(frames * channels);
    }
    Some(Decoded {
        channels: channels as u16,
        rate,
        samples,
        codec: Codec::ImaAdpcm,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn wav(channels: u16, rate: u32, frames: u32) -> Vec<u8> {
        let mut data = b"RIFF\0\0\0\0WAVEfmt ".to_vec();
        data.extend(16u32.to_le_bytes());
        data.extend(1u16.to_le_bytes());
        data.extend(channels.to_le_bytes());
        data.extend(rate.to_le_bytes());
        data.extend((rate * 2 * channels as u32).to_le_bytes());
        data.extend((2 * channels).to_le_bytes());
        data.extend(16u16.to_le_bytes());
        data.extend(b"data");
        data.extend((frames * 2 * channels as u32).to_le_bytes());
        for i in 0..frames * channels as u32 {
            data.extend(((i as i16).wrapping_mul(97)).to_le_bytes());
        }
        let size = (data.len() - 8) as u32;
        data[4..8].copy_from_slice(&size.to_le_bytes());
        data
    }

    #[test]
    fn the_audio_type_names_the_only_container_that_decodes() {
        let data = wav(2, 22050, 300);
        let decoded = decode(&data, Container::Wav).unwrap();
        assert_eq!(
            (decoded.channels, decoded.rate, decoded.samples.len()),
            (2, 22050, 600)
        );
        assert!(decode(&data, Container::Any).is_ok());
        assert!(decode(&data, Container::Ogg).is_err());
        assert!(decode(&data, Container::Mpeg).is_err());
        assert!(decode(b"this is not audio data at all\n", Container::Wav).is_err());
        assert_eq!(Container::of_audio_type(12), None, "tracker modules");
    }

    #[test]
    fn a_partial_wav_announces_its_whole_length() {
        let data = wav(1, 22050, 1000);
        assert_eq!(header(&data[..60], Container::Wav), Some((1, 22050, 1000)));
        assert_eq!(header(&data[..30], Container::Wav), None);
        assert_eq!(header(&data, Container::Ogg), None);
    }

    #[test]
    fn compressed_audio_comes_out_at_16_bit_precision() {
        let mut samples = [0.5 / 32768.0, -1.7 / 32768.0, 2.0, -0.25];
        quantize(&mut samples, f32::trunc);
        assert_eq!(samples, [0.0, -1.0 / 32768.0, 32767.0 / 32768.0, -0.25]);
        let mut samples = [0.5 / 32768.0, -1.7 / 32768.0];
        quantize(&mut samples, f32::round);
        assert_eq!(samples, [1.0 / 32768.0, -2.0 / 32768.0]);
    }

    #[test]
    fn ima_adpcm_steps_add_shifted_steps() {
        // Step 7 (index 0): nibble 7 adds 7/8 + 7 + 7/2 + 7/4 = 0 + 7 + 3 + 1, index +8.
        let mut state = Ima {
            sample: 0,
            index: 0,
        };
        assert_eq!(state.next(7), 11.0 / 32768.0);
        assert_eq!(state.index, 8);
        // Nibble 15 subtracts at step 16: 2 + 16 + 8 + 4.
        assert_eq!(state.next(15), (11.0 - 30.0) / 32768.0);
        let mut low = Ima {
            sample: -32760,
            index: 88,
        };
        assert_eq!(low.next(8), -1.0, "clamped to the 16-bit range");
    }

    #[test]
    fn compressed_wav_lengths_come_from_the_fact_chunk() {
        let mut data = b"RIFF\0\0\0\0WAVEfmt ".to_vec();
        data.extend(20u32.to_le_bytes());
        data.extend(0x11u16.to_le_bytes()); // IMA ADPCM
        data.extend([
            1, 0, 0x22, 0x56, 0, 0, 0, 0, 0, 0, 0, 2, 4, 0, 2, 0, 0xf9, 0x03,
        ]);
        data.extend(b"fact");
        data.extend(4u32.to_le_bytes());
        data.extend(11025u32.to_le_bytes());
        data.extend(b"data\0\0\0\0");
        assert_eq!(wav_fact_length(&data), Some(11025));
        assert_eq!(
            wav_fact_length(&wav(1, 22050, 10)),
            None,
            "PCM data has no fact length"
        );
    }

    /// A CBR MPEG-1 Layer III stream of `count` silent 128 kbps 44.1 kHz mono frames (418 bytes
    /// with padding, as encoders alternate), optionally with an ID3v2 tag and a Xing header.
    fn mpeg(count: usize, id3: bool, xing: Option<u32>) -> Vec<u8> {
        let mut data = Vec::new();
        if id3 {
            data.extend(b"ID3\x04\0\0\0\0\0\x05abcde");
        }
        for i in 0..count {
            // MPEG-1 Layer III without CRC, 128 kbps, 44.1 kHz, mono.
            let mut header = 0xfffb_90c4u32;
            let padded = i % 3 != 0;
            if padded {
                header |= 1 << 9;
            }
            let mut f = header.to_be_bytes().to_vec();
            f.resize(417 + padded as usize, 0);
            if i == 0
                && let Some(frames) = xing
            {
                f[21..25].copy_from_slice(b"Xing");
                f[25..29].copy_from_slice(&1u32.to_be_bytes());
                f[29..33].copy_from_slice(&frames.to_be_bytes());
            }
            data.extend(f);
        }
        data
    }

    #[test]
    fn mpeg_lengths_count_xing_frames_or_the_file_frames_and_two() {
        assert_eq!(mpeg_length(&mpeg(21, false, None)), Some((23 * 1152, None)));
        assert_eq!(mpeg_length(&mpeg(22, true, None)), Some((24 * 1152, None)));
        assert_eq!(
            mpeg_length(&mpeg(41, true, Some(40))),
            Some((40 * 1152, None))
        );
        // A CBR file's Info header counts as a frame and decodes as one of silence.
        let mut info = mpeg(22, true, None);
        info[15 + 21..15 + 25].copy_from_slice(b"Info");
        assert_eq!(mpeg_length(&info), Some((24 * 1152, Some(1152))));
        assert!(Container::Mpeg.holds(&mpeg(2, true, None)));
        assert!(!Container::Mpeg.holds(b"OggS\0\0\0\0"));
    }
}
