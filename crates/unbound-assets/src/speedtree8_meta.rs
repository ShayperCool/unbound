//! The `.meta` the Editor writes for a SpeedTree 8 (`.st`) file it imports for the first time:
//! the SpeedTreeImporter section of serializedVersion 7 with the tree's level settings (Unity
//! 6000.0.71f1, tests/projects/speedtree8 profile fresh). The file decides three things: the
//! number of levels (heights 0.5, then half the level before, the last 0.01), whether the last is
//! a billboard (it overrides the settings: no light probes or reflection probes, wind quality at
//! most Fastest) and the best wind quality (the wind's leading word), which is also the selected
//! quality and every mesh level's.

use crate::speedtree_tables::{count, entry, uint};

/// The signature a `.st` file starts with.
const SIGNATURE: &[u8] = b"SpeedTree___";
// Root entries of a SpeedTree 8 file (`Unbound.SpeedTree.SpeedTree8File`).
const ROOT_VERSION_MAJOR: usize = 0;
const ROOT_VERSION_MINOR: usize = 1;
const ROOT_LAST_LOD_IS_BILLBOARD: usize = 2;
const ROOT_LODS: usize = 3;
const ROOT_WIND: usize = 7;
/// The (major, minor) file versions whose layout is measured (as the managed reader's).
const MEASURED_VERSIONS: [(u32, u32); 2] = [(8, 0), (10, 0)];
/// `SpeedTreeImporter.windQualityNames`' Fastest.
const WIND_QUALITY_FASTEST: u32 = 1;

/// What a tree's new `.meta` takes from its file.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct TreeShape {
    pub lods: usize,
    pub last_lod_is_billboard: bool,
    pub best_wind_quality: u32,
}

/// The shape of a SpeedTree 8 file, `None` when it is not one of a measured version.
pub fn shape(bytes: &[u8]) -> Option<TreeShape> {
    if !bytes.starts_with(SIGNATURE) {
        return None;
    }
    let root = SIGNATURE.len();
    let version = (
        uint(bytes, entry(bytes, root, ROOT_VERSION_MAJOR)?)?,
        uint(bytes, entry(bytes, root, ROOT_VERSION_MINOR)?)?,
    );
    if !MEASURED_VERSIONS.contains(&version) {
        return None;
    }
    // Booleans take one byte in version 8 files and four in version 10 ones: the first counts.
    let billboard = *bytes.get(entry(bytes, root, ROOT_LAST_LOD_IS_BILLBOARD)?)? != 0;
    let lods = count(bytes, entry(bytes, root, ROOT_LODS)?, 4)?;
    let best_wind_quality = uint(bytes, entry(bytes, root, ROOT_WIND)?)?;
    Some(TreeShape {
        lods,
        last_lod_is_billboard: billboard,
        best_wind_quality,
    })
}

/// The importer section of the new `.meta` of a tree of `shape` (after `fileFormatVersion` and
/// `guid`), ending with the trailer every importer section has.
pub fn section(shape: &TreeShape) -> String {
    let best = shape.best_wind_quality;
    let mut out = format!(
        "SpeedTreeImporter:\n  externalObjects: {{}}\n  serializedVersion: 7\n  mainColor: {{r: 1, g: 1, b: 1, a: 1}}\n  \
         hueVariation: {{r: 1, g: 0.5, b: 0, a: 0.1}}\n  alphaTestRef: 0.33\n  enableHueVariation: 0\n  enableBumpMapping: 1\n  \
         enableSubsurfaceScattering: 1\n  bestWindQuality: {best}\n  selectedWindQuality: {best}\n  enableShadowCasting: 1\n  \
         enableShadowReceiving: 1\n  enableLightProbes: 1\n  reflectionProbeEnumValue: 1\n  hasBillboard: {}\n  lODSettings:\n",
        u8::from(shape.last_lod_is_billboard)
    );
    let mut height = 0.5f32;
    for level in 0..shape.lods {
        let last = level + 1 == shape.lods;
        let billboard = last && shape.last_lod_is_billboard;
        let level_height = if last { 0.01 } else { height };
        out.push_str(&format!(
            "  - enableSettingOverride: {}\n    height: {level_height}\n    castShadows: 1\n    receiveShadows: 1\n    \
             useLightProbes: {}\n    reflectionProbeUsage: {}\n    enableBump: 1\n    enableHue: 0\n    enableSubsurface: 1\n    \
             windQuality: {}\n",
            u8::from(billboard),
            u8::from(!billboard),
            u8::from(!billboard),
            if billboard { best.min(WIND_QUALITY_FASTEST) } else { best },
        ));
        height *= 0.5;
    }
    out.push_str(
        "  enableSmoothLODTransition: 1\n  animateCrossFading: 1\n  billboardTransitionCrossFadeWidth: 0.25\n  fadeOutWidth: 0.25\n  \
         scaleFactor: 0.3048\n  unitConversionEnumValue: 1\n  motionVectorModeEnumValue: 1\n  generateColliders: 1\n  \
         generateRigidbody: 1\n  materialVersion: 3\n  materialLocation: 1\n  fileIDType: 2\n  userData: \n  assetBundleName: \n  \
         assetBundleVariant: \n",
    );
    out
}

#[cfg(test)]
pub(crate) mod tests {
    use super::*;

    fn table(entries: &[Vec<u8>]) -> Vec<u8> {
        let mut out = (entries.len() as u32).to_le_bytes().to_vec();
        let mut at = 4 + 4 * entries.len();
        for e in entries {
            out.extend((at as u32).to_le_bytes());
            at += e.len();
        }
        for e in entries {
            out.extend(e);
        }
        out
    }

    /// A SpeedTree 8 file with `lods` empty levels, the billboard flag (one byte, as version 8
    /// writes it) and the wind's leading word.
    pub(crate) fn tree(major: u32, lods: usize, billboard: bool, wind: u32) -> Vec<u8> {
        let lod = table(&[
            0u32.to_le_bytes().to_vec(),
            0u32.to_le_bytes().to_vec(),
            0u32.to_le_bytes().to_vec(),
        ]);
        let mut out = SIGNATURE.to_vec();
        out.extend(table(&[
            major.to_le_bytes().to_vec(),
            0u32.to_le_bytes().to_vec(),
            vec![u8::from(billboard)],
            table(&vec![lod; lods]),
            0u32.to_le_bytes().to_vec(),
            0u32.to_le_bytes().to_vec(),
            128u32.to_le_bytes().to_vec(),
            wind.to_le_bytes().to_vec(),
            0u32.to_le_bytes().to_vec(),
        ]));
        out
    }

    #[test]
    fn the_shape_is_the_files_levels_billboard_and_best_wind_quality() {
        assert_eq!(
            shape(&tree(8, 4, true, 4)),
            Some(TreeShape {
                lods: 4,
                last_lod_is_billboard: true,
                best_wind_quality: 4
            })
        );
        assert_eq!(
            shape(&tree(10, 3, false, 2)),
            Some(TreeShape {
                lods: 3,
                last_lod_is_billboard: false,
                best_wind_quality: 2
            })
        );
        // Unmeasured versions, other files and truncated ones have no shape.
        assert_eq!(shape(&tree(9, 3, false, 4)), None);
        assert_eq!(shape(b"SpeedTree9______"), None);
        let whole = tree(8, 4, true, 4);
        assert_eq!(shape(&whole[..40]), None);
    }

    /// The Editor's new .meta of the samples' Broadleaf_Desktop_Field.st (four levels, the last a
    /// billboard, best wind quality 4), tests/projects/speedtree8 profile fresh.
    #[test]
    fn a_billboard_trees_section_is_the_editors() {
        let text = section(&TreeShape {
            lods: 4,
            last_lod_is_billboard: true,
            best_wind_quality: 4,
        });
        let level = |over: u8, height: &str, probes: u8, wind: u8| {
            format!(
                "  - enableSettingOverride: {over}\n    height: {height}\n    castShadows: 1\n    receiveShadows: 1\n    \
                 useLightProbes: {probes}\n    reflectionProbeUsage: {probes}\n    enableBump: 1\n    enableHue: 0\n    \
                 enableSubsurface: 1\n    windQuality: {wind}\n"
            )
        };
        let expected = String::from(
            "SpeedTreeImporter:\n  externalObjects: {}\n  serializedVersion: 7\n  mainColor: {r: 1, g: 1, b: 1, a: 1}\n  \
             hueVariation: {r: 1, g: 0.5, b: 0, a: 0.1}\n  alphaTestRef: 0.33\n  enableHueVariation: 0\n  enableBumpMapping: 1\n  \
             enableSubsurfaceScattering: 1\n  bestWindQuality: 4\n  selectedWindQuality: 4\n  enableShadowCasting: 1\n  \
             enableShadowReceiving: 1\n  enableLightProbes: 1\n  reflectionProbeEnumValue: 1\n  hasBillboard: 1\n  lODSettings:\n",
        ) + &level(0, "0.5", 1, 4)
            + &level(0, "0.25", 1, 4)
            + &level(0, "0.125", 1, 4)
            + &level(1, "0.01", 0, 1)
            + "  enableSmoothLODTransition: 1\n  animateCrossFading: 1\n  billboardTransitionCrossFadeWidth: 0.25\n  \
               fadeOutWidth: 0.25\n  scaleFactor: 0.3048\n  unitConversionEnumValue: 1\n  motionVectorModeEnumValue: 1\n  \
               generateColliders: 1\n  generateRigidbody: 1\n  materialVersion: 3\n  materialLocation: 1\n  fileIDType: 2\n  \
               userData: \n  assetBundleName: \n  assetBundleVariant: \n";
        assert_eq!(text, expected);
    }

    /// The card tree's copy whose wind's leading word is 2: three mesh levels, all of wind quality 2.
    #[test]
    fn a_mesh_only_tree_takes_its_best_wind_quality_on_every_level() {
        let text = section(&TreeShape {
            lods: 3,
            last_lod_is_billboard: false,
            best_wind_quality: 2,
        });
        assert!(text.contains("  bestWindQuality: 2\n  selectedWindQuality: 2\n"));
        assert!(text.contains("  hasBillboard: 0\n"));
        assert_eq!(text.matches("    windQuality: 2\n").count(), 3);
        assert_eq!(text.matches("enableSettingOverride: 0").count(), 3);
        assert!(text.contains("    height: 0.25\n"));
        assert!(text.contains("    height: 0.01\n"));
    }
}
