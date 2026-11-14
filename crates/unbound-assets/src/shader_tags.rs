//! Canonical ShaderLab query tags shared by declaration and renderer metadata.
/// Tag names Unity stores upper-cased (looked up case-insensitively).
const UPPER_NAMES: [&str; 4] = ["Queue", "IgnoreProjector", "LightMode", "ShadowSupport"];
/// `LightMode` values Unity stores upper-cased; others (`Vertex`, `VertexLM`, a render
/// pipeline's own) as written.
const UPPER_LIGHT_MODES: [&str; 8] = [
    "Always",
    "ForwardBase",
    "ForwardAdd",
    "Deferred",
    "ShadowCaster",
    "Meta",
    "MotionVectors",
    "GrabPass",
];
/// Tags whose values Unity stores as `true`/`false`.
const BOOLEAN_TAGS: [&str; 5] = [
    "IgnoreProjector",
    "ForceSupported",
    "CanUseSpriteAtlas",
    "TerrainCompatible",
    "DisableBatching",
];

/// Tags as Unity stores them from a source, under the 6000.0.71f1 rules (the canonical
/// tests/projects/shaderkeywords tags, and every built-in shader's serialized tags against its
/// source): `Queue`, `IgnoreProjector`, `LightMode` and `ShadowSupport` names upper-cased; the
/// built-in pipeline's light modes upper-cased; boolean tags lower-cased (`DisableBatching`
/// `LODFading` as `LodFading`). Sorted by name.
pub fn canonical_tags<'a>(
    tags: impl Iterator<Item = (&'a String, &'a String)>,
) -> Vec<(String, String)> {
    let mut out: Vec<(String, String)> = tags
        .map(|(k, v)| {
            let name = match UPPER_NAMES.iter().find(|n| n.eq_ignore_ascii_case(k)) {
                Some(n) => n.to_ascii_uppercase(),
                None => k.clone(),
            };
            let value = if k.eq_ignore_ascii_case("LightMode")
                && UPPER_LIGHT_MODES.iter().any(|m| m.eq_ignore_ascii_case(v))
            {
                v.to_ascii_uppercase()
            } else if BOOLEAN_TAGS.iter().any(|t| t.eq_ignore_ascii_case(k))
                && (v.eq_ignore_ascii_case("true") || v.eq_ignore_ascii_case("false"))
            {
                v.to_ascii_lowercase()
            } else if k.eq_ignore_ascii_case("DisableBatching")
                && v.eq_ignore_ascii_case("LODFading")
            {
                "LodFading".to_string()
            } else {
                v.clone()
            };
            (name, value)
        })
        .collect();
    out.sort();
    out
}
