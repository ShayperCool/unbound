//! The audio importer: decodes a clip to interleaved `f32` samples and reads `AudioImporter`
//! settings.
//!
//! Of the settings only `forceToMono` (with `normalize`) changes the samples; load type,
//! compression and sample-rate settings describe how Unity stores the clip in a player and are
//! exposed for the runtime (`AudioClip.loadType`, `preloadAudioData`, ...). The decoded data is
//! kept at the source sample rate.
use crate::{
    Importer, asset_name,
    meta::{boolean, importer, int},
};
use anyhow::{Context, Result, anyhow, bail};
use serde::{Deserialize, Serialize};
use std::path::Path;
use symphonia::core::{
    codecs::audio::AudioDecoderOptions,
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
            sample_rate_setting: int(v, "sampleRateSetting").unwrap_or(0) as i32,
            sample_rate_override: int(v, "sampleRateOverride").unwrap_or(44100) as u32,
            compression_format: int(v, "compressionFormat").unwrap_or(1) as i32,
            quality: crate::meta::float(v, "quality").unwrap_or(1.0),
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
    const VERSION: u32 = 1;

    fn import(&self, source: &Path, bytes: &[u8], meta: &Value) -> Result<ImportedAudio> {
        let settings = AudioSettings::read(meta, &self.build_target);
        let (channels, sample_rate, mut samples) = decode(source, bytes).context("decoding")?;
        let mut channels = channels;
        if settings.force_to_mono && channels > 1 {
            samples = downmix(&samples, channels as usize, settings.normalize);
            channels = 1;
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
    Ok((channels, sample_rate, samples))
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
        let data_len = samples.len() as u32 * 2;
        let mut out = Vec::new();
        out.extend_from_slice(b"RIFF");
        out.extend_from_slice(&(36 + data_len).to_le_bytes());
        out.extend_from_slice(b"WAVEfmt ");
        out.extend_from_slice(&16u32.to_le_bytes());
        out.extend_from_slice(&1u16.to_le_bytes()); // PCM
        out.extend_from_slice(&channels.to_le_bytes());
        out.extend_from_slice(&rate.to_le_bytes());
        out.extend_from_slice(&(rate * channels as u32 * 2).to_le_bytes());
        out.extend_from_slice(&(channels * 2).to_le_bytes());
        out.extend_from_slice(&16u16.to_le_bytes());
        out.extend_from_slice(b"data");
        out.extend_from_slice(&data_len.to_le_bytes());
        for s in samples {
            out.extend_from_slice(&s.to_le_bytes());
        }
        out
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

        let meta =
            unbound_yaml::parse_value("AudioImporter:\n  forceToMono: 1\n  normalize: 1\n").unwrap();
        let mono = AudioImporter::default()
            .import(Path::new("a.wav"), &bytes, &meta)
            .unwrap();
        assert_eq!(mono.channels, 1);
        // (0.25, -0.25, 0.25) normalized by its peak.
        assert_eq!(mono.samples, [1.0, -1.0, 1.0]);
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
