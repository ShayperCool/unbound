//! How a build target stores baked HDR lighting textures, measured in
//! tests/projects/lightmapencoding (Unity 6000.0.71f1 Editor, NVIDIA driver).
use crate::{MeasuredFamily, UnityVersion};
use serde::{Deserialize, Serialize};

/// The storage of an HDR lighting texture (a Lightmap-type texture or an HDR cubemap).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub enum HdrEncoding {
    /// Half floats as imported (`UNITY_LIGHTMAP_FULL_HDR`).
    #[default]
    Native,
    /// RGBM bytes: a shared multiplier in alpha, a range of 5 in gamma space.
    Rgbm,
    /// Double LDR bytes: half the gamma-space value, no alpha.
    DoubleLdr,
}

/// `LightmapEncodingQuality` / `HDRCubemapEncodingQuality` (Player Settings).
const LOW: i64 = 0;
const NORMAL: i64 = 1;

/// The encodings of a build target group's baked lightmaps and HDR cubemaps.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub struct LightingEncodings {
    pub lightmap: HdrEncoding,
    pub cubemap: HdrEncoding,
}

impl LightingEncodings {
    /// The encodings of `group` (`Standalone`, `Android`, `iOS`/`iPhone`, `WebGL`) given the
    /// qualities its Player Settings entries set (`m_BuildTargetGroupLightmapEncodingQuality`,
    /// `m_BuildTargetGroupHDRCubemapEncodingQuality`; None: no entry, the group's default).
    ///
    /// Measured for Unity 6000.0.71f1 with Standalone and Android: the defaults are the
    /// Editor's (Standalone High and High, Android and iOS Low and Low, WebGL High and Normal).
    /// Low quality lightmaps are double LDR where the target supports them (Android; iOS is
    /// unmeasured and taken as Android), else RGBM; Normal is RGBM; High keeps half floats. HDR
    /// cubemaps follow the quality alone: Low double LDR, Normal RGBM, High half floats. The
    /// whole 6000.0 family gets these answers; other families keep half floats for both, as
    /// before this was measured.
    pub fn resolve(
        version: UnityVersion,
        group: &str,
        lightmap: Option<i64>,
        cubemap: Option<i64>,
    ) -> Self {
        if version.measured_family() != Some(MeasuredFamily::Unity6000_0) {
            return Self::default();
        }
        let mobile = matches!(group, "Android" | "iOS" | "iPhone");
        let (default_lightmap, default_cubemap) = match group {
            "Android" | "iOS" | "iPhone" => (LOW, LOW),
            "WebGL" => (2, NORMAL),
            _ => (2, 2),
        };
        let lightmap = match lightmap.unwrap_or(default_lightmap) {
            LOW if mobile => HdrEncoding::DoubleLdr,
            LOW | NORMAL => HdrEncoding::Rgbm,
            _ => HdrEncoding::Native,
        };
        let cubemap = match cubemap.unwrap_or(default_cubemap) {
            LOW => HdrEncoding::DoubleLdr,
            NORMAL => HdrEncoding::Rgbm,
            _ => HdrEncoding::Native,
        };
        Self { lightmap, cubemap }
    }

    /// The built-in shader define naming the lightmap encoding.
    pub fn lightmap_define(self) -> &'static str {
        match self.lightmap {
            HdrEncoding::Native => "UNITY_LIGHTMAP_FULL_HDR",
            HdrEncoding::Rgbm => "UNITY_LIGHTMAP_RGBM_ENCODING",
            HdrEncoding::DoubleLdr => "UNITY_LIGHTMAP_DLDR_ENCODING",
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use HdrEncoding::*;

    const MEASURED: UnityVersion = UnityVersion::new(6000, 0, 71);

    fn of(group: &str, lightmap: Option<i64>, cubemap: Option<i64>) -> (HdrEncoding, HdrEncoding) {
        let e = LightingEncodings::resolve(MEASURED, group, lightmap, cubemap);
        (e.lightmap, e.cubemap)
    }

    #[test]
    fn measured_defaults_and_qualities() {
        assert_eq!(of("Standalone", None, None), (Native, Native));
        assert_eq!(of("Android", None, None), (DoubleLdr, DoubleLdr));
        assert_eq!(of("Android", Some(0), Some(0)), (DoubleLdr, DoubleLdr));
        assert_eq!(of("Android", Some(1), Some(1)), (Rgbm, Rgbm));
        assert_eq!(of("Android", Some(2), Some(2)), (Native, Native));
        // Standalone has no double LDR lightmaps; its Low cubemaps are double LDR.
        assert_eq!(of("Standalone", Some(0), Some(0)), (Rgbm, DoubleLdr));
        // Each list sets its own texture kind.
        assert_eq!(of("Android", Some(2), None), (Native, DoubleLdr));
    }

    #[test]
    fn the_whole_family_is_measured() {
        for version in [
            UnityVersion::new(6000, 0, 0),
            UnityVersion::new(6000, 0, 67),
        ] {
            assert_eq!(
                LightingEncodings::resolve(version, "Android", None, None),
                LightingEncodings::resolve(MEASURED, "Android", None, None)
            );
        }
    }

    #[test]
    fn other_families_keep_half_floats() {
        for version in [
            UnityVersion::new(6000, 3, 0),
            UnityVersion::new(2022, 3, 71),
        ] {
            let e = LightingEncodings::resolve(version, "Android", Some(0), Some(1));
            assert_eq!((e.lightmap, e.cubemap), (Native, Native));
            assert_eq!(e.lightmap_define(), "UNITY_LIGHTMAP_FULL_HDR");
        }
    }
}
