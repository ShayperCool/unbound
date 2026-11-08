//! What the model importer of a release makes of an OBJ file's material library (MTL): which
//! statements it reads and how they become the material description that the material import
//! (and `OnPreprocessMaterialDescription`) sees.
use crate::version::UnityVersion;

/// A colour statement and the description properties it sets.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct MtlColor {
    pub keyword: &'static str,
    /// The colour property the statement's three values go to.
    pub property: &'static str,
    /// The factor property the statement sets to 1.
    pub factor: &'static str,
    /// Only Phong materials read it.
    pub phong_only: bool,
}

/// One release family's MTL reading.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct MtlRules {
    /// The table's name (part of the model import cache key).
    pub name: &'static str,
    /// The keyword choosing the shading model, and its value when a material has none.
    pub illumination: &'static str,
    pub default_illumination: i64,
    /// Materials whose illumination model is below this are Lambert (no specular, reflection or
    /// shininess); the others Phong.
    pub phong_from: i64,
    pub colors: &'static [MtlColor],
    /// The statement of the specular exponent and its property (Phong only).
    pub shininess: (&'static str, &'static str),
    /// Dissolve (opacity), transparency and transmission filter keywords.
    pub dissolve: &'static str,
    pub transparency: &'static str,
    pub filter: &'static str,
}

/// The transparency a material description gets: its colour and factor.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct MtlTransparency {
    pub color: [f64; 3],
    pub factor: f64,
}

impl MtlRules {
    /// The transparency of a material from its dissolve (`d`), transparency (`Tr`) and filter
    /// (`Tf`) statements; `None` keeps the class defaults. Dissolve wins and is inverted with a
    /// white colour; otherwise transparency is the factor with the filter as colour (white
    /// without one); a filter alone is the colour with factor 1.
    pub fn transparency(
        &self,
        dissolve: Option<f64>,
        transparency: Option<f64>,
        filter: Option<[f64; 3]>,
    ) -> Option<MtlTransparency> {
        match (dissolve, transparency, filter) {
            (Some(d), _, _) => Some(MtlTransparency {
                color: [1.0; 3],
                factor: 1.0 - d,
            }),
            (None, Some(factor), filter) => Some(MtlTransparency {
                color: filter.unwrap_or([1.0; 3]),
                factor,
            }),
            (None, None, Some(color)) => Some(MtlTransparency { color, factor: 1.0 }),
            (None, None, None) => None,
        }
    }
}

/// Measured on the Unity 6000.0.71f1 Editor (tests/projects/modelimport `mtl_properties.py` and
/// `smoothing.py` ObjKeys).
const MTL_6000_0: MtlRules = MtlRules {
    name: "mtl-6000.0",
    illumination: "illum",
    default_illumination: 2,
    phong_from: 2,
    colors: &[
        MtlColor {
            keyword: "Ka",
            property: "AmbientColor",
            factor: "AmbientFactor",
            phong_only: false,
        },
        MtlColor {
            keyword: "Kd",
            property: "DiffuseColor",
            factor: "DiffuseFactor",
            phong_only: false,
        },
        MtlColor {
            keyword: "Ks",
            property: "SpecularColor",
            factor: "SpecularFactor",
            phong_only: true,
        },
    ],
    shininess: ("Ns", "ShininessExponent"),
    dissolve: "d",
    transparency: "Tr",
    filter: "Tf",
};

impl MtlRules {
    /// The MTL reading of `version`. Only the 6000.0 family is measured; other releases use its
    /// table until they are (like the profile's data tables, the nearest measured family).
    pub fn of(_version: &UnityVersion) -> &'static MtlRules {
        &MTL_6000_0
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn dissolve_wins_then_transparency_then_filter() {
        let rules = MtlRules::of(&UnityVersion::new(6000, 0, 71));
        let t = |d, tr, tf| rules.transparency(d, tr, tf);
        assert_eq!(
            t(Some(0.6), Some(0.1), Some([0.5; 3])).unwrap().color,
            [1.0; 3]
        );
        assert!((t(Some(0.6), None, None).unwrap().factor - 0.4).abs() < 1e-12);
        assert_eq!(
            t(None, Some(0.25), Some([0.1, 0.2, 0.3])),
            Some(MtlTransparency {
                color: [0.1, 0.2, 0.3],
                factor: 0.25
            })
        );
        assert_eq!(t(None, Some(0.25), None).unwrap().color, [1.0; 3]);
        assert_eq!(t(None, None, Some([0.5; 3])).unwrap().factor, 1.0);
        assert_eq!(t(None, None, None), None);
    }
}
