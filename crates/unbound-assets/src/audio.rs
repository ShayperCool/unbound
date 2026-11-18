//! The audio importer: decodes a clip to interleaved `f32` samples and reads `AudioImporter`
//! settings.
//!
//! `forceToMono` (with `normalize`) and an overridden sample rate (`sampleRateSetting: 2`) change
//! the samples, as they do in Unity (the Editor imports for the active build target, so a clip's
//! `frequency` and `samples` are the overridden ones); load type and compression describe how
//! Unity stores the clip and are exposed for the runtime (`AudioClip.loadType`,
//! `preloadAudioData`, ...). "Optimize sample rate" keeps the source rate. An ADPCM clip's frames
//! round up to FMOD's blocks of 64; its samples (and a Vorbis clip's) are the source's, not the
//! lossy codec's.
use crate::{
    Importer, asset_name,
    meta::{boolean, importer, int},
};
use anyhow::{Context, Result, anyhow, bail};
use serde::{Deserialize, Serialize};
use std::path::Path;
use symphonia::core::{
    codecs::audio::{AudioCodecId, AudioDecoderOptions, well_known::*},
    errors::Error as SymphoniaError,
    formats::{FormatOptions, TrackType, probe::Hint},
    io::MediaSourceStream,
    meta::MetadataOptions,
};
use unbound_yaml::Value;

/// File extensions Unity's AudioImporter handles that this importer decodes.
pub const EXTENSIONS: &[&str] = &["wav", "mp3", "ogg", "aif", "aiff", "flac"];

/// The fileID of the AudioClip object of an imported clip.
pub const AUDIO_CLIP_FILE_ID: i64 = 8_300_000;

/// `loadType`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum LoadType {
    DecompressOnLoad,
    CompressedInMemory,
    Streaming,
}

/// The per-platform sample settings (`defaultSettings` or a platform override).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SampleSettings {
    pub load_type: LoadType,
    /// `sampleRateSetting`: 0 preserve, 1 optimize, 2 override.
    pub sample_rate_setting: i32,
    pub sample_rate_override: u32,
    /// `compressionFormat`: 0 PCM, 1 Vorbis, 2 ADPCM, ...
    pub compression_format: i32,
    pub quality: f32,
    pub preload_audio_data: bool,
}

/// The `AudioImporter` settings of a `.meta`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AudioSettings {
    pub force_to_mono: bool,
    /// Peak-normalize the mono downmix (only with `force_to_mono`).
    pub normalize: bool,
    pub load_in_background: bool,
    pub ambisonic: bool,
    /// The `defaultSettings`, or the build target's override.
    pub sample: SampleSettings,
}

impl AudioSettings {
    /// Reads the settings, applying the `platformSettingOverrides` entry of `build_target` when
    /// present.
    pub fn read(meta: &Value, build_target: &str) -> Self {
        let imp = importer(meta, "AudioImporter");
        // Before 2022 `preloadAudioData` lived at the top level; now it is per platform.
        let preload_default = boolean(imp, "preloadAudioData").unwrap_or(true);
        let sample = |v: &Value| SampleSettings {
            load_type: match int(v, "loadType").unwrap_or(0) {
                1 => LoadType::CompressedInMemory,
                2 => LoadType::Streaming,
                _ => LoadType::DecompressOnLoad,
            },
            sample_rate_setting: {
                let setting = int(v, "sampleRateSetting").unwrap_or(0) as i32;
                if setting == 1 {
                    // Optimize currently preserves the source rate.
                    unbound_yaml::reads::unapplied(v, "sampleRateSetting");
                }
                setting
            },
            sample_rate_override: int(v, "sampleRateOverride").unwrap_or(44100) as u32,
            compression_format: {
                let format = int(v, "compressionFormat").unwrap_or(1) as i32;
                if format != 0 {
                    // Clip metadata and ADPCM frame padding are applied, but the lossy sample
                    // conversion of compressed formats is not.
                    unbound_yaml::reads::unapplied(v, "compressionFormat");
                }
                format
            },
            quality: {
                let quality = crate::meta::float(v, "quality").unwrap_or(1.0);
                // The value is retained for inspection; decoded samples currently keep source
                // fidelity rather than applying the importer's lossy quality setting.
                unbound_yaml::reads::unapplied(v, "quality");
                quality
            },
            preload_audio_data: boolean(v, "preloadAudioData").unwrap_or(preload_default),
        };
        // Keyed by `BuildTargetGroup` number.
        let group = build_target_group(build_target);
        let platform = imp
            .get("platformSettingOverrides")
            .and_then(Value::as_map)
            .and_then(|m| group.and_then(|g| m.get(g)).or_else(|| m.get(build_target)));
        let default = imp.get("defaultSettings").unwrap_or(&Value::Null);
        Self {
            force_to_mono: boolean(imp, "forceToMono").unwrap_or(false),
            normalize: boolean(imp, "normalize").unwrap_or(true),
            load_in_background: boolean(imp, "loadInBackground").unwrap_or(false),
            ambisonic: boolean(imp, "ambisonic").unwrap_or(false),
            sample: sample(platform.unwrap_or(default)),
        }
    }
}

/// The `BuildTargetGroup` number of a build target name, as audio overrides are keyed.
fn build_target_group(build_target: &str) -> Option<&'static str> {
    Some(match build_target {
        "Standalone" => "1",
        "iPhone" | "iOS" => "4",
        "Android" => "7",
        "WebGL" => "13",
        _ => return None,
    })
}

/// A decoded clip.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ImportedAudio {
    /// The AudioClip's name: the file name without extension.
    pub name: String,
    pub channels: u16,
    pub sample_rate: u32,
    /// Interleaved samples in `[-1, 1]`; `frames * channels` of them.
    #[serde(with = "f32_bytes")]
    pub samples: Vec<f32>,
    pub settings: AudioSettings,
}

impl ImportedAudio {
    pub fn frames(&self) -> usize {
        self.samples.len() / self.channels.max(1) as usize
    }

    /// `AudioClip.length` in seconds.
    pub fn length(&self) -> f64 {
        self.frames() as f64 / self.sample_rate.max(1) as f64
    }
}

/// The audio importer. Its option is the build target whose overrides apply.
#[derive(Debug, Clone)]
pub struct AudioImporter {
    pub build_target: String,
}

impl Default for AudioImporter {
    fn default() -> Self {
        Self {
            build_target: "Standalone".into(),
        }
    }
}

impl Importer for AudioImporter {
    type Output = ImportedAudio;
    const KIND: &'static str = "audio";
    // 4: converted PCM samples (mono downmix, resampling) are stored as 16-bit samples.
    // 5: so are unconverted samples finer than 16 bits (24-bit, float).
    const VERSION: u32 = 5;

    fn import(&self, source: &Path, bytes: &[u8], meta: &Value) -> Result<ImportedAudio> {
        let settings = AudioSettings::read(meta, &self.build_target);
        let decoded = decode_source(source, bytes, false).context("decoding")?;
        let (channels, mut sample_rate, mut samples) =
            (decoded.channels, decoded.sample_rate, decoded.samples);
        let mut channels = channels;
        let mut converted = false;
        if settings.force_to_mono && channels > 1 {
            samples = downmix(&samples, channels as usize, settings.normalize);
            channels = 1;
            converted = true;
        }
        let target = settings.sample.sample_rate_override;
        if settings.sample.sample_rate_setting == 2 && target > 0 && target != sample_rate {
            samples = resample(&samples, channels as usize, sample_rate, target);
            sample_rate = target;
            converted = true;
        }
        if settings.sample.compression_format == PCM {
            if converted {
                quantize_pcm16(&mut samples);
            } else {
                store_pcm16(&mut samples, decoded.format);
            }
        }
        // ADPCM (FMOD's) keeps blocks of 64 frames: the clip is as long as its last block.
        if settings.sample.compression_format == 2 {
            let frames = samples.len() / channels.max(1) as usize;
            samples.resize(frames.div_ceil(64) * 64 * channels.max(1) as usize, 0.0);
        }
        Ok(ImportedAudio {
            name: asset_name(source),
            channels,
            sample_rate,
            samples,
            settings,
        })
    }

    fn options_key(&self) -> String {
        self.build_target.clone()
    }
}

/// Decodes the default audio track: `(channels, sample rate, interleaved samples)`.
pub fn decode(source: &Path, bytes: &[u8]) -> Result<(u16, u32, Vec<f32>)> {
    decode_packets(source, bytes, false)
}

/// [`decode`]; `untrimmed` keeps the frames the container marks as encoder delay and padding
/// (an MPEG file's LAME tag), as a plain decoder outputs them.
pub fn decode_packets(
    source: &Path,
    bytes: &[u8],
    untrimmed: bool,
) -> Result<(u16, u32, Vec<f32>)> {
    let decoded = decode_source(source, bytes, untrimmed)?;
    Ok((decoded.channels, decoded.sample_rate, decoded.samples))
}

/// A decoded audio track.
struct Decoded {
    channels: u16,
    sample_rate: u32,
    /// Interleaved.
    samples: Vec<f32>,
    /// What the source's samples are, for a PCM source.
    format: Option<PcmSource>,
}

fn decode_source(source: &Path, bytes: &[u8], untrimmed: bool) -> Result<Decoded> {
    let cursor = std::io::Cursor::new(bytes.to_vec());
    let stream = MediaSourceStream::new(Box::new(cursor), Default::default());
    let mut hint = Hint::new();
    if let Some(ext) = source.extension().and_then(|e| e.to_str()) {
        hint.with_extension(ext);
    }
    let mut format = symphonia::default::get_probe()
        .probe(
            &hint,
            stream,
            FormatOptions::default(),
            MetadataOptions::default(),
        )
        .map_err(|e| anyhow!("unsupported audio container: {e}"))?;
    let track = format
        .default_track(TrackType::Audio)
        .ok_or_else(|| anyhow!("no audio track"))?;
    let track_id = track.id;
    let params = track
        .codec_params
        .as_ref()
        .and_then(|p| p.audio())
        .ok_or_else(|| anyhow!("no audio codec parameters"))?
        .clone();
    let mut decoder = symphonia::default::get_codecs()
        .make_audio_decoder(&params, &AudioDecoderOptions::default())
        .map_err(|e| anyhow!("unsupported audio codec: {e}"))?;

    let mut channels = params.channels.as_ref().map(|c| c.count() as u16);
    let mut sample_rate = params.sample_rate;
    let mut samples: Vec<f32> = Vec::new();
    let mut chunk: Vec<f32> = Vec::new();
    loop {
        let packet = match format.next_packet() {
            Ok(Some(packet)) => packet,
            Ok(None) => break,
            // Some encoders leave a truncated last frame: keep what decoded.
            Err(SymphoniaError::IoError(e)) if e.kind() == std::io::ErrorKind::UnexpectedEof => {
                break;
            }
            Err(e) => return Err(anyhow!("demuxing: {e}")),
        };
        if packet.track_id != track_id {
            continue;
        }
        let mut packet = packet;
        if untrimmed {
            packet.trim_start = Default::default();
            packet.trim_end = Default::default();
        }
        match decoder.decode(&packet) {
            Ok(buffer) => {
                let spec = buffer.spec();
                channels.get_or_insert(spec.channels().count() as u16);
                sample_rate.get_or_insert(spec.rate());
                buffer.copy_to_vec_interleaved(&mut chunk);
                samples.extend_from_slice(&chunk);
            }
            // A corrupt packet is skipped, as players do.
            Err(SymphoniaError::DecodeError(_)) => continue,
            Err(e) => return Err(anyhow!("decoding: {e}")),
        }
    }
    let (Some(channels), Some(sample_rate)) = (channels, sample_rate) else {
        bail!("no channel layout or sample rate");
    };
    if channels == 0 {
        bail!("no channels");
    }
    Ok(Decoded {
        channels,
        sample_rate,
        samples,
        format: PcmSource::of(params.codec),
    })
}

/// `AudioCompressionFormat.PCM`.
const PCM: i32 = 0;

/// Converted samples as a PCM import stores them: 16-bit, full scale 32767 truncated toward zero,
/// read back over 32768 (matches the Unity 6000.0.71f1 Editor, tests/projects/assetpostprocessors
/// Tone.wav forced to mono and normalized: every sample, the peak reading -32767/32768; downmixes
/// without normalization and resampled clips take the same rule, unmeasured).
pub fn quantize_pcm16(samples: &mut [f32]) {
    for s in samples {
        *s = (s.clamp(-1.0, 1.0) * 32767.0).trunc() / 32768.0;
    }
}

/// Unconverted samples as a PCM import stores them: 16-bit, read back over 32768. Integer samples
/// finer than 16 bits keep their top 16 bits (a 24-bit sample shifted right by 8, rounding toward
/// negative infinity), float samples take full scale 32767 truncated toward zero, as converted
/// samples do ([`quantize_pcm16`]); 8- and 16-bit samples are stored as they are (matches the
/// Unity 6000.0.71f1 Editor, tests/projects/assetpostprocessors Tone8, Tone24 and ToneFloat;
/// 32-bit integer and 64-bit float samples take the same rules, unmeasured). Compressed sources'
/// samples are left as decoded (unmeasured).
fn store_pcm16(samples: &mut [f32], format: Option<PcmSource>) {
    match format {
        Some(PcmSource::FineInteger) => {
            for s in samples {
                *s = (*s * 32768.0).floor().clamp(-32768.0, 32767.0) / 32768.0;
            }
        }
        Some(PcmSource::Float) => quantize_pcm16(samples),
        None => {}
    }
}

/// The samples of a PCM source finer than 16 bits.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum PcmSource {
    /// 24- or 32-bit integers.
    FineInteger,
    /// 32- or 64-bit floats.
    Float,
}

impl PcmSource {
    fn of(codec: AudioCodecId) -> Option<Self> {
        const FINE_INTEGER: &[AudioCodecId] = &[
            CODEC_ID_PCM_S24LE,
            CODEC_ID_PCM_S24LE_PLANAR,
            CODEC_ID_PCM_S24BE,
            CODEC_ID_PCM_S24BE_PLANAR,
            CODEC_ID_PCM_U24LE,
            CODEC_ID_PCM_U24LE_PLANAR,
            CODEC_ID_PCM_U24BE,
            CODEC_ID_PCM_U24BE_PLANAR,
            CODEC_ID_PCM_S32LE,
            CODEC_ID_PCM_S32LE_PLANAR,
            CODEC_ID_PCM_S32BE,
            CODEC_ID_PCM_S32BE_PLANAR,
            CODEC_ID_PCM_U32LE,
            CODEC_ID_PCM_U32LE_PLANAR,
            CODEC_ID_PCM_U32BE,
            CODEC_ID_PCM_U32BE_PLANAR,
        ];
        const FLOAT: &[AudioCodecId] = &[
            CODEC_ID_PCM_F32LE,
            CODEC_ID_PCM_F32LE_PLANAR,
            CODEC_ID_PCM_F32BE,
            CODEC_ID_PCM_F32BE_PLANAR,
            CODEC_ID_PCM_F64LE,
            CODEC_ID_PCM_F64LE_PLANAR,
            CODEC_ID_PCM_F64BE,
            CODEC_ID_PCM_F64BE_PLANAR,
        ];
        if FINE_INTEGER.contains(&codec) {
            Some(Self::FineInteger)
        } else if FLOAT.contains(&codec) {
            Some(Self::Float)
        } else {
            None
        }
    }
}

/// Averages the channels; with `normalize` the result is scaled so its peak is 1.
pub fn downmix(samples: &[f32], channels: usize, normalize: bool) -> Vec<f32> {
    let mut mono: Vec<f32> = samples
        .chunks_exact(channels)
        .map(|frame| frame.iter().sum::<f32>() / channels as f32)
        .collect();
    if normalize {
        let peak = mono.iter().fold(0f32, |m, s| m.max(s.abs()));
        if peak > 0.0 {
            let gain = 1.0 / peak;
            mono.iter_mut().for_each(|s| *s *= gain);
        }
    }
    mono
}

/// Converts interleaved samples to another rate (Catmull-Rom interpolation); the length becomes
/// `ceil(frames * to / from)`.
pub fn resample(samples: &[f32], channels: usize, from: u32, to: u32) -> Vec<f32> {
    let channels = channels.max(1);
    let frames = samples.len() / channels;
    if frames == 0 || from == 0 || to == 0 {
        return samples.to_vec();
    }
    let out_frames = (frames as u64 * to as u64).div_ceil(from as u64) as usize;
    let step = from as f64 / to as f64;
    let at = |i: isize, c: usize| -> f32 {
        let i = i.clamp(0, frames as isize - 1) as usize;
        samples[i * channels + c]
    };
    let mut out = Vec::with_capacity(out_frames * channels);
    for n in 0..out_frames {
        let x = n as f64 * step;
        let i = x.floor() as isize;
        let t = (x - i as f64) as f32;
        for c in 0..channels {
            let (p0, p1, p2, p3) = (at(i - 1, c), at(i, c), at(i + 1, c), at(i + 2, c));
            let a = -0.5 * p0 + 1.5 * p1 - 1.5 * p2 + 0.5 * p3;
            let b = p0 - 2.5 * p1 + 2.0 * p2 - 0.5 * p3;
            let d = -0.5 * p0 + 0.5 * p2;
            out.push(((a * t + b) * t + d) * t + p1);
        }
    }
    out
}

/// Serializes `Vec<f32>` as one little-endian byte string (much faster than a sequence).
mod f32_bytes {
    use serde::{Deserialize, Deserializer, Serializer};

    pub fn serialize<S: Serializer>(samples: &[f32], s: S) -> Result<S::Ok, S::Error> {
        let mut bytes = Vec::with_capacity(samples.len() * 4);
        for v in samples {
            bytes.extend_from_slice(&v.to_le_bytes());
        }
        serde_bytes::serialize(&bytes, s)
    }

    pub fn deserialize<'de, D: Deserializer<'de>>(d: D) -> Result<Vec<f32>, D::Error> {
        let bytes: serde_bytes::ByteBuf = Deserialize::deserialize(d)?;
        Ok(bytes
            .as_chunks::<4>()
            .0
            .iter()
            .map(|c| f32::from_le_bytes(*c))
            .collect())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn a_pcm_mono_downmix_is_stored_as_16_bit_samples_with_its_peak_at_32767() {
        // Two stereo frames (-8000, -12000) and (-7224, -11752) of 32768: their means are -10000
        // and -9488, and normalized the first is the peak.
        let stereo = [-8000.0, -12000.0, -7224.0, -11752.0].map(|s: f32| s / 32768.0);
        let mut mono = super::downmix(&stereo, 2, true);
        super::quantize_pcm16(&mut mono);
        assert_eq!(mono, vec![-32767.0 / 32768.0, -31089.0 / 32768.0]);
        // Out of range samples clamp; fractions truncate toward zero; silence stays silent.
        let mut wide = vec![1.5, -2.0, 0.0, 0.99 / 32767.0, -1.99 / 32767.0];
        super::quantize_pcm16(&mut wide);
        assert_eq!(
            wide,
            vec![
                32767.0 / 32768.0,
                -32767.0 / 32768.0,
                0.0,
                0.0,
                -1.0 / 32768.0
            ]
        );
    }

    const META: &str = r#"fileFormatVersion: 2
guid: 95ae7d4f554d8b944a386dd2f0648975
AudioImporter:
  externalObjects: {}
  serializedVersion: 6
  defaultSettings:
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 1
    conversionMode: 0
  platformSettingOverrides:
    7:
      loadType: 2
      sampleRateSetting: 2
      sampleRateOverride: 22050
      compressionFormat: 1
      quality: 0.5
      conversionMode: 0
  forceToMono: 1
  normalize: 0
  preloadAudioData: 0
  loadInBackground: 1
  ambisonic: 0
  3D: 1
"#;

    #[test]
    fn reads_settings() {
        let meta = unbound_yaml::parse_value(META).unwrap();
        let s = AudioSettings::read(&meta, "Standalone");
        assert!(s.force_to_mono && !s.normalize && s.load_in_background && !s.ambisonic);
        assert_eq!(s.sample.load_type, LoadType::DecompressOnLoad);
        assert_eq!(s.sample.compression_format, 1);
        assert!(!s.sample.preload_audio_data);
        let android = AudioSettings::read(&meta, "Android");
        assert_eq!(android.sample.load_type, LoadType::Streaming);
        assert_eq!(android.sample.sample_rate_override, 22050);
        assert_eq!(android.sample.quality, 0.5);
    }

    fn wav(channels: u16, rate: u32, samples: &[i16]) -> Vec<u8> {
        let data: Vec<u8> = samples.iter().flat_map(|s| s.to_le_bytes()).collect();
        wav_data(1, 16, channels, rate, &data)
    }

    /// A WAVE file of `bits`-bit samples in `data` (format 1: integer PCM, 3: IEEE float).
    fn wav_data(format: u16, bits: u16, channels: u16, rate: u32, data: &[u8]) -> Vec<u8> {
        let block = channels * bits / 8;
        let mut out = Vec::new();
        out.extend_from_slice(b"RIFF");
        out.extend_from_slice(&(36 + data.len() as u32).to_le_bytes());
        out.extend_from_slice(b"WAVEfmt ");
        out.extend_from_slice(&16u32.to_le_bytes());
        out.extend_from_slice(&format.to_le_bytes());
        out.extend_from_slice(&channels.to_le_bytes());
        out.extend_from_slice(&rate.to_le_bytes());
        out.extend_from_slice(&(rate * block as u32).to_le_bytes());
        out.extend_from_slice(&block.to_le_bytes());
        out.extend_from_slice(&bits.to_le_bytes());
        out.extend_from_slice(b"data");
        out.extend_from_slice(&(data.len() as u32).to_le_bytes());
        out.extend_from_slice(data);
        out
    }

    #[test]
    fn unconverted_pcm_finer_than_16_bits_is_stored_as_16_bit_samples() {
        // tests/projects/assetpostprocessors Tone24.wav and ToneFloat.wav: their first samples
        // and what a PCM import gives back (in units of 2^-15).
        let ramp = |i: i32| ((i * 7919) % 20000) as f64 / 10000.0 - 1.0 + 1.0 / 3_000_000.0;
        let int24: Vec<u8> = (0..4)
            .flat_map(|i| {
                let q = (ramp(i) * 8_388_608.0) as i32;
                q.to_le_bytes()[..3].to_vec()
            })
            .collect();
        let float: Vec<u8> = (0..4)
            .flat_map(|i| (ramp(i) as f32).to_le_bytes())
            .collect();
        let pcm = unbound_yaml::parse_value(
            "AudioImporter:\n  defaultSettings:\n    loadType: 0\n    compressionFormat: 0\n",
        )
        .unwrap();
        let units = |samples: &[f32]| samples.iter().map(|s| s * 32768.0).collect::<Vec<_>>();
        let import = |bytes: &[u8], meta: &Value| {
            AudioImporter::default()
                .import(Path::new("a.wav"), bytes, meta)
                .unwrap()
                .samples
        };
        let bytes24 = wav_data(1, 24, 1, 22050, &int24);
        assert_eq!(
            units(&import(&bytes24, &pcm)),
            [-32768.0, -6820.0, 19129.0, -20458.0]
        );
        let bytes_float = wav_data(3, 32, 1, 22050, &float);
        assert_eq!(
            units(&import(&bytes_float, &pcm)),
            [-32766.0, -6818.0, 19129.0, -20456.0]
        );
        // 16-bit samples are stored as they are; another compression format keeps the decoded
        // samples whole.
        let bytes16 = wav(1, 22050, &[-32768, 1, 32767]);
        assert_eq!(units(&import(&bytes16, &pcm)), [-32768.0, 1.0, 32767.0]);
        let vorbis = Value::Null;
        assert_eq!(
            import(&bytes24, &vorbis)[1],
            (ramp(1) * 8_388_608.0) as i32 as f32 / 8_388_608.0
        );
    }

    #[test]
    fn decodes_wav_and_forces_mono() {
        let bytes = wav(2, 22050, &[16384, 0, -16384, 0, 8192, 8192]);
        let stereo = AudioImporter::default()
            .import(Path::new("a.wav"), &bytes, &Value::Null)
            .unwrap();
        assert_eq!((stereo.channels, stereo.sample_rate), (2, 22050));
        assert_eq!(stereo.frames(), 3);
        assert_eq!(stereo.samples[0], 0.5);

        let meta = unbound_yaml::parse_value("AudioImporter:\n  forceToMono: 1\n  normalize: 1\n")
            .unwrap();
        let mono = AudioImporter::default()
            .import(Path::new("a.wav"), &bytes, &meta)
            .unwrap();
        assert_eq!(mono.channels, 1);
        // (0.25, -0.25, 0.25) normalized by its peak.
        assert_eq!(mono.samples, [1.0, -1.0, 1.0]);
    }

    #[test]
    fn overridden_sample_rate_resamples() {
        let samples: Vec<i16> = (0..100).map(|i| (i * 100) as i16).collect();
        let bytes = wav(1, 44100, &samples);
        let meta = unbound_yaml::parse_value(
            "AudioImporter:\n  defaultSettings:\n    sampleRateSetting: 2\n    sampleRateOverride: 22050\n",
        )
        .unwrap();
        let clip = AudioImporter::default()
            .import(Path::new("a.wav"), &bytes, &meta)
            .unwrap();
        assert_eq!(clip.sample_rate, 22050);
        assert_eq!(clip.frames(), 50);
        // A ramp stays a ramp: every other source sample.
        assert!((clip.samples[10] - samples[20] as f32 / 32768.0).abs() < 1e-4);
    }

    #[test]
    fn samples_round_trip_through_postcard() {
        let bytes = wav(1, 8000, &[1, 2, 3]);
        let clip = AudioImporter::default()
            .import(Path::new("a.wav"), &bytes, &Value::Null)
            .unwrap();
        let encoded = postcard::to_allocvec(&clip).unwrap();
        let decoded: ImportedAudio = postcard::from_bytes(&encoded).unwrap();
        assert_eq!(decoded, clip);
    }
}
