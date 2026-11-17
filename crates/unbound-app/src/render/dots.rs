//! DOTS instancing data of BatchRendererGroup draws (render command `DotsDraw`): what the
//! renderer writes into a program's uniform block for a draw command, as the
//! com.unity.render-pipelines.core shader library reads it (`UnityDOTSInstancing.hlsl`):
//! - each `unity_DOTSInstancing<type><bytes>_Metadata<name>` uniform of a
//!   `UnityDOTSInstancing_*` cbuffer is the batch's metadata value for the property `<name>`,
//!   zero when the batch has none (the Manual: "Any metadata value that the shader uses and not
//!   set here will be zero");
//! - `unity_DOTSVisibleInstances[i].x` (cbuffer `UnityInstancingDOTS_InstanceVisibility`) is
//!   the batch index of the draw's instance `i`; `.y` 0 (no indirect visible instances); the
//!   `.z` and `.w` of the first four elements hold the draw's header (rendering layer mask,
//!   flags, mesh bounds: `LoadDOTSInstancedData_RenderingLayer`, `_LightData`, ...);
//! - an indirect draw command has no visible instance indices: the first element's `.y` is its
//!   packed indirect visible offset, from which the shader library reads each instance's batch
//!   index in the `unity_DOTSInstancing_IndirectInstanceVisibility` buffer (`SV_InstanceID`
//!   past it), and the arguments buffer gives the instance count;
//! - in uniform buffer mode (OpenGL devices) the cbuffer `unity_DOTSInstanceData` is the window of
//!   the batch's buffer AddBatch gave, copied on the GPU into the draw's uniform block before its
//!   pass ([`window`]), so what compute kernels or copies wrote into the buffer is drawn as well as
//!   what scripts set; on the others `unity_DOTSInstanceData` is a byte address buffer, bound by
//!   name to the whole buffer.

use unbound_runtime::render_commands::{DOTS_HEADER_WORDS, DotsDraw};
use unbound_runtime::render_protocol::owned::PropertyValue;
use unbound_shaders::Compiled;

/// The buffer an indirect command's visible instances are read from. A direct command leaves it
/// unset: the shader library reads it only for an indirect command (the first visible instance
/// element's `y`), so a direct draw binds zeros there without a warning, as the Editor logs none.
pub const INDIRECT_VISIBILITY_BUFFER: &str = "unity_DOTSInstancing_IndirectInstanceVisibility";

/// The prefix of a draw property carrying a metadata value (managed `BatchCulling.MetadataPrefix`).
pub const METADATA_PREFIX: &str = "<DOTS>";
const METADATA_UNIFORM: &str = "unity_DOTSInstancing";
const METADATA_MARK: &str = "_Metadata";
const VISIBILITY_BLOCK: &str = "UnityInstancingDOTS_InstanceVisibility";
const INSTANCE_DATA_BLOCK: &str = "unity_DOTSInstanceData";
/// The bytes of one `unity_DOTSVisibleInstances` element (a `uint4`).
const VISIBLE_STRIDE: usize = 16;

/// Where a program takes DOTS instancing data (none of it: an ordinary program).
#[derive(Debug, Default, Clone, PartialEq)]
pub struct DotsLayout {
    /// Each metadata uniform: its byte offset and the property name it is the metadata of.
    metadata: Vec<(u32, Box<str>)>,
    /// The visible instance array's byte range.
    visibility: Option<(u32, u32)>,
    /// The instance data cbuffer's byte range (uniform buffer mode).
    instance_data: Option<(u32, u32)>,
}

impl DotsLayout {
    /// Whether the program draws DOTS instanced instances: it reads the visible instance array
    /// (a variant without DOTS instancing would draw every instance at the draw's own place).
    pub fn instanced(&self) -> bool {
        self.visibility.is_some()
    }

    pub fn of(program: &Compiled) -> DotsLayout {
        let metadata = program
            .uniforms
            .iter()
            .filter(|u| u.count == 1 && u.rows * u.columns == 1)
            .filter_map(|u| Some((u.offset, metadata_property(&u.name)?.into())))
            .collect();
        let block = |name: &str| {
            program
                .blocks
                .iter()
                .find(|b| b.name == name)
                .map(|b| (b.offset, b.size))
        };
        DotsLayout {
            metadata,
            visibility: block(VISIBILITY_BLOCK),
            instance_data: block(INSTANCE_DATA_BLOCK),
        }
    }
}

/// The property a metadata uniform's name is the metadata of: `<name>` of
/// `unity_DOTSInstancing<type><bytes>_Metadata<name>`.
fn metadata_property(uniform: &str) -> Option<&str> {
    let rest = uniform.strip_prefix(METADATA_UNIFORM)?;
    let at = rest.find(METADATA_MARK)?;
    let spec = &rest[..at];
    // The type letter and the size in bytes (F64, H8, I4, U16, SH's F128).
    let mut chars = spec.chars();
    let kind = chars.next()?;
    if !matches!(kind, 'F' | 'H' | 'I' | 'U')
        || !chars.as_str().bytes().all(|b| b.is_ascii_digit())
        || spec.len() < 2
    {
        return None;
    }
    let name = &rest[at + METADATA_MARK.len()..];
    (!name.is_empty()).then_some(name)
}

/// The batch's metadata value for a property: its draw property's int, else zero.
fn metadata_value(overrides: &[(String, PropertyValue)], property: &str) -> u32 {
    overrides
        .iter()
        .rev()
        .find_map(|(name, value)| match value {
            PropertyValue::Int(v) if name.strip_prefix(METADATA_PREFIX) == Some(property) => {
                Some(*v as u32)
            }
            _ => None,
        })
        .unwrap_or(0)
}

fn put(out: &mut [u8], at: usize, value: u32) {
    if let Some(slot) = out.get_mut(at..at + 4) {
        slot.copy_from_slice(&value.to_le_bytes());
    }
}

/// The part of a batch buffer a draw's uniform block reads in uniform buffer mode: the block's
/// byte offset of the instance data cbuffer, the buffer's byte offset and the bytes copied. The
/// cbuffer reads the window, zeros past it and past the buffer's end ([`write`] zeros the whole
/// cbuffer); the copy covers whole 4-byte words (`wgpu::COPY_BUFFER_ALIGNMENT`), so a window size
/// that is not a multiple of 4 reads its last partial word as zeros (the shader library reads
/// whole float4s). None without such a cbuffer or bytes to read.
pub fn window(layout: &DotsLayout, draw: &DotsDraw, buffer_size: u64) -> Option<(u64, u64, u64)> {
    let (offset, size) = layout.instance_data?;
    let start = u64::from(draw.window_offset);
    let mut bytes = u64::from(size).min(buffer_size.saturating_sub(start));
    if draw.window_size > 0 {
        bytes = bytes.min(u64::from(draw.window_size));
    }
    let bytes = bytes / wgpu::COPY_BUFFER_ALIGNMENT * wgpu::COPY_BUFFER_ALIGNMENT;
    (bytes > 0 && start % wgpu::COPY_BUFFER_ALIGNMENT == 0).then_some((
        u64::from(offset),
        start,
        bytes,
    ))
}

/// Writes a draw command's DOTS instancing data into a program's uniform block (`out`, filled
/// for the draw otherwise); the instance data cbuffer is zeroed for its [`window`] copy.
pub fn write(
    layout: &DotsLayout,
    draw: &DotsDraw,
    overrides: &[(String, PropertyValue)],
    out: &mut [u8],
) {
    for (offset, property) in &layout.metadata {
        put(out, *offset as usize, metadata_value(overrides, property));
    }
    if let Some((offset, size)) = layout.visibility {
        let (offset, size) = (offset as usize, size as usize);
        let elements = size / VISIBLE_STRIDE;
        if draw.indirect != 0 && elements > 0 {
            put(out, offset, 0);
            put(out, offset + 4, draw.indirect);
        }
        for (i, index) in draw.visible.iter().take(elements).enumerate() {
            let at = offset + i * VISIBLE_STRIDE;
            put(out, at, *index);
            put(out, at + 4, 0);
            put(out, at + 8, 0);
            put(out, at + 12, 0);
        }
        // The header in the z and w of the first elements.
        for (k, word) in draw
            .header
            .iter()
            .enumerate()
            .take(DOTS_HEADER_WORDS.min(elements * 2))
        {
            put(
                out,
                offset + (k / 2) * VISIBLE_STRIDE + 8 + (k % 2) * 4,
                *word,
            );
        }
    }
    if let Some((offset, size)) = layout.instance_data
        && let Some(region) = out.get_mut(offset as usize..(offset + size) as usize)
    {
        region.fill(0);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn metadata_names() {
        assert_eq!(
            metadata_property("unity_DOTSInstancingF64_Metadataunity_ObjectToWorld"),
            Some("unity_ObjectToWorld")
        );
        assert_eq!(
            metadata_property("unity_DOTSInstancingF16_Metadata_BaseColor"),
            Some("_BaseColor")
        );
        assert_eq!(
            metadata_property("unity_DOTSInstancingF128_Metadataunity_SHCoefficients"),
            Some("unity_SHCoefficients")
        );
        assert_eq!(
            metadata_property("unity_DOTSInstancingH8_Metadata_Color"),
            Some("_Color")
        );
        assert_eq!(metadata_property("unity_DOTSInstanceDataRaw"), None);
        assert_eq!(metadata_property("unity_DOTSInstancingX4_Metadata_A"), None);
        assert_eq!(metadata_property("unity_DOTSInstancingF_Metadata_A"), None);
        assert_eq!(metadata_property("unity_DOTSInstancingF16_Metadata"), None);
        assert_eq!(metadata_property("_BaseColor"), None);
    }

    fn layout() -> DotsLayout {
        DotsLayout {
            metadata: vec![
                (0, "unity_ObjectToWorld".into()),
                (4, "_BaseColor".into()),
                (8, "_Missing".into()),
            ],
            visibility: Some((16, 32)),
            instance_data: Some((48, 32)),
        }
    }

    #[test]
    fn writes_metadata_visibility_and_window() {
        let overrides = vec![
            (
                "<DOTS>unity_ObjectToWorld".to_string(),
                PropertyValue::Int(0x8000_0060u32 as i32),
            ),
            (
                "<DOTS>_BaseColor".to_string(),
                PropertyValue::Int(0x8000_0420u32 as i32),
            ),
            // A material property of the same name is no metadata.
            ("_Missing".to_string(), PropertyValue::Int(7)),
        ];
        let draw = DotsDraw {
            header: [3, 0x11, 10, 11, 12, 13, 14, 15],
            window_offset: 16,
            window_size: 24,
            indirect: 0,
            visible: vec![5, 9, 11],
        };
        let mut out = vec![0xAAu8; 80];
        write(&layout(), &draw, &overrides, &mut out);
        let word = |at: usize| u32::from_le_bytes(out[at..at + 4].try_into().unwrap());
        assert_eq!(word(0), 0x8000_0060, "exact metadata past 2^24");
        assert_eq!(word(4), 0x8000_0420);
        assert_eq!(word(8), 0, "missing metadata is zero");
        // Two elements fit the 32-byte block: the third index is left out, and so is the header
        // past their z and w.
        assert_eq!([word(16), word(20), word(24), word(28)], [5, 0, 3, 0x11]);
        assert_eq!([word(32), word(36), word(40), word(44)], [9, 0, 10, 11]);
        // The instance data cbuffer is zero until its window is copied in.
        assert!(out[48..80].iter().all(|b| *b == 0));
        // The window: 24 bytes from byte 16 into the cbuffer at 48 (the rest stays zero).
        assert_eq!(window(&layout(), &draw, 64), Some((48, 16, 24)));
    }

    #[test]
    fn windows_end_at_the_cbuffer_and_the_buffer() {
        let draw = |window_offset, window_size| DotsDraw {
            header: [0; DOTS_HEADER_WORDS],
            window_offset,
            window_size,
            indirect: 0,
            visible: vec![0],
        };
        // No window size: the cbuffer's 32 bytes, or what the buffer holds past the offset.
        assert_eq!(window(&layout(), &draw(0, 0), 64), Some((48, 0, 32)));
        assert_eq!(window(&layout(), &draw(60, 0), 64), Some((48, 60, 4)));
        // A window larger than the cbuffer copies the cbuffer's bytes.
        assert_eq!(window(&layout(), &draw(16, 4096), 8192), Some((48, 16, 32)));
        // At or past the buffer's end, or a partial word: nothing (the cbuffer reads zeros).
        assert_eq!(window(&layout(), &draw(64, 0), 64), None);
        assert_eq!(window(&layout(), &draw(80, 16), 64), None);
        assert_eq!(window(&layout(), &draw(16, 3), 64), None);
        // A program without the cbuffer (raw buffer devices) copies nothing.
        assert_eq!(window(&DotsLayout::default(), &draw(0, 0), 64), None);
    }

    #[test]
    fn indirect_draws_take_their_packed_offset() {
        let draw = DotsDraw {
            header: [3, 0x11, 10, 11, 12, 13, 14, 15],
            window_offset: 0,
            window_size: 0,
            indirect: 0x8000_0007,
            visible: vec![],
        };
        let mut out = vec![0xAAu8; 80];
        write(&layout(), &draw, &[], &mut out);
        let word = |at: usize| u32::from_le_bytes(out[at..at + 4].try_into().unwrap());
        assert_eq!(
            [word(16), word(20), word(24), word(28)],
            [0, 0x8000_0007, 3, 0x11]
        );
        assert_eq!([word(40), word(44)], [10, 11]);
    }

    #[test]
    fn ordinary_programs_take_nothing() {
        let plain = DotsLayout::default();
        assert!(!plain.instanced());
        assert!(layout().instanced());
        let mut out = vec![0xAAu8; 16];
        write(&plain, &DotsDraw::default(), &[], &mut out);
        assert!(out.iter().all(|b| *b == 0xAA));
    }
}
