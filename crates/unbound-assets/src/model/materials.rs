//! Material descriptions: what Unity's FBX importer hands `AssetPostprocessor`s in
//! `OnPreprocessMaterialDescription`, and what its legacy material import reads.
//!
//! The description of an FBX material has the properties of its FBX SDK class (`FbxSurfaceLambert`,
//! `FbxSurfacePhong`) with the file's value, else the file's property template's, else the SDK's
//! default; then every other property the material itself declares (exporters' user properties).
//! A template's extra properties (Blender's `Opacity`) do not count. Numbers, integers and booleans
//! are floats; colours and vectors are four floats, `w = 1` unless the property has an alpha;
//! strings stay strings. A texture connected to a declared property is a texture property under
//! that name, with the texture's `Translation` and `Scaling` as offset and scale. (Measured with the
//! Editor's own descriptions: tests/projects/modelimport.)
use serde::{Deserialize, Serialize};

/// A texture of a material description. The texture asset itself is found by the caller (see
/// [`TextureDescription::path`]).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct TextureDescription {
    /// The description property the texture is connected to (`DiffuseColor`, `NormalMap`, ...).
    pub property: String,
    /// The file name as the FBX stores it (`FileName`; usually absolute on the exporting machine).
    pub path: String,
    /// `RelativeFilename`: relative to the FBX file.
    pub relative_path: String,
    pub offset: [f32; 2],
    pub scale: [f32; 2],
}

/// The properties of one material, by kind, in the file's order.
#[derive(Debug, Clone, Default, PartialEq, Serialize, Deserialize)]
pub struct MaterialDescription {
    pub floats: Vec<(String, f32)>,
    pub vectors: Vec<(String, [f32; 4])>,
    pub strings: Vec<(String, String)>,
    pub textures: Vec<TextureDescription>,
}

impl MaterialDescription {
    pub fn float(&self, name: &str) -> Option<f32> {
        self.floats.iter().find(|(n, _)| n == name).map(|(_, v)| *v)
    }

    pub fn vector(&self, name: &str) -> Option<[f32; 4]> {
        self.vectors
            .iter()
            .find(|(n, _)| n == name)
            .map(|(_, v)| *v)
    }

    pub fn string(&self, name: &str) -> Option<&str> {
        self.strings
            .iter()
            .find(|(n, _)| n == name)
            .map(|(_, v)| v.as_str())
    }

    pub fn texture(&self, name: &str) -> Option<&TextureDescription> {
        self.textures.iter().find(|t| t.property == name)
    }

    fn has(&self, name: &str) -> bool {
        self.float(name).is_some() || self.vector(name).is_some() || self.string(name).is_some()
    }
}

#[derive(Clone, Copy)]
enum ClassDefault {
    Float(f32),
    Color(f32),
}

/// `FbxSurfaceLambert`'s properties and the SDK's defaults.
const LAMBERT: &[(&str, ClassDefault)] = &[
    ("EmissiveColor", ClassDefault::Color(0.0)),
    ("EmissiveFactor", ClassDefault::Float(1.0)),
    ("AmbientColor", ClassDefault::Color(0.2)),
    ("AmbientFactor", ClassDefault::Float(1.0)),
    ("DiffuseColor", ClassDefault::Color(0.8)),
    ("DiffuseFactor", ClassDefault::Float(1.0)),
    ("NormalMap", ClassDefault::Color(0.0)),
    ("Bump", ClassDefault::Color(0.0)),
    ("BumpFactor", ClassDefault::Float(1.0)),
    ("TransparentColor", ClassDefault::Color(0.0)),
    ("TransparencyFactor", ClassDefault::Float(0.0)),
    ("DisplacementColor", ClassDefault::Color(0.0)),
    ("DisplacementFactor", ClassDefault::Float(1.0)),
    ("VectorDisplacementColor", ClassDefault::Color(0.0)),
    ("VectorDisplacementFactor", ClassDefault::Float(1.0)),
];

/// What `FbxSurfacePhong` adds.
const PHONG: &[(&str, ClassDefault)] = &[
    ("SpecularColor", ClassDefault::Color(0.2)),
    ("SpecularFactor", ClassDefault::Float(1.0)),
    ("ShininessExponent", ClassDefault::Float(20.0)),
    ("ReflectionColor", ClassDefault::Color(0.0)),
    ("ReflectionFactor", ClassDefault::Float(1.0)),
];

/// The value of a property as a description float, vector or string.
enum Value {
    Float(f32),
    Vector([f32; 4]),
    String(String),
}

fn value(prop: &ufbx::Prop) -> Option<Value> {
    use ufbx::PropType as T;
    let v = &prop.value_vec4;
    Some(match prop.type_ {
        T::Boolean | T::Integer | T::Number | T::Distance => Value::Float(v.x as f32),
        T::ColorWithAlpha => Value::Vector([v.x as f32, v.y as f32, v.z as f32, v.w as f32]),
        T::Vector | T::Color | T::Translation | T::Rotation | T::Scaling => {
            Value::Vector([v.x as f32, v.y as f32, v.z as f32, 1.0])
        }
        T::String => Value::String(prop.value_str.to_string()),
        _ => return None,
    })
}

/// The description of a ufbx material.
pub fn describe(material: &ufbx::Material) -> MaterialDescription {
    let mut out = MaterialDescription::default();
    let shading = material.shading_model_name.to_string();
    let (shading, class): (String, Vec<&(&str, ClassDefault)>) =
        if shading.eq_ignore_ascii_case("phong") {
            ("Phong".into(), LAMBERT.iter().chain(PHONG).collect())
        } else if shading.eq_ignore_ascii_case("lambert") {
            ("Lambert".into(), LAMBERT.iter().collect())
        } else {
            (shading, Vec::new())
        };
    let props = &material.element.props;
    let synthetic = |p: &ufbx::Prop| p.flags.has_any(ufbx::PropFlags::SYNTHETIC);
    let own = |name: &str| {
        props
            .props
            .iter()
            .find(|p| p.name.as_ref() == name && !synthetic(p))
    };
    for &&(name, default) in &class {
        // The file's value, the template's, the SDK's.
        let found = own(name)
            .or_else(|| props.defaults.as_ref().and_then(|d| d.find_prop(name)))
            .and_then(value);
        match (default, found) {
            (_, Some(Value::Float(f))) | (ClassDefault::Float(f), None) => {
                out.floats.push((name.to_string(), f))
            }
            (_, Some(Value::Vector(v))) => out.vectors.push((name.to_string(), v)),
            (ClassDefault::Color(c), None) => out.vectors.push((name.to_string(), [c, c, c, 1.0])),
            (_, Some(Value::String(s))) => out.strings.push((name.to_string(), s)),
        }
    }
    out.floats.push(("MultiLayer".into(), 0.0));
    out.strings.push(("ShadingModel".into(), shading));
    for prop in props.props.iter() {
        let name = prop.name.as_ref();
        if synthetic(prop) || class.iter().any(|(n, _)| *n == name) || out.has(name) {
            continue;
        }
        match value(prop) {
            Some(Value::Float(f)) => out.floats.push((name.to_string(), f)),
            Some(Value::Vector(v)) => out.vectors.push((name.to_string(), v)),
            Some(Value::String(s)) => out.strings.push((name.to_string(), s)),
            None => {}
        }
    }
    for texture in material.textures.iter() {
        let property = texture.material_prop.to_string();
        if !out.has(&property) || out.texture(&property).is_some() {
            continue; // the SDK drops connections to undeclared properties
        }
        let tex = &texture.texture;
        let pair = |name: &str, default: f32| {
            tex.element
                .props
                .find_prop(name)
                .map_or([default, default], |p| {
                    [p.value_vec4.x as f32, p.value_vec4.y as f32]
                })
        };
        out.textures.push(TextureDescription {
            property,
            path: tex.absolute_filename.to_string(),
            relative_path: tex.relative_filename.to_string(),
            offset: pair("Translation", 0.0),
            scale: pair("Scaling", 1.0),
        });
    }
    out
}
