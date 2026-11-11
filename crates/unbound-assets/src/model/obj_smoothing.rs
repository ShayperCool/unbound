//! OBJ smoothing groups and default materials as the model importer reads them (matches the
//! Unity 6000.0.71f1 Editor, tests/projects/modelimport `smoothing.py` Obj*).
//!
//! - `s <integer>` sets the group (read as a C integer: `s 1.5` is 1, `s -1` is every bit),
//!   `s off` sets 0, and any other statement (`s on`, a bare `s`) keeps the current group. The
//!   group starts at 0 for the file and is a bit mask: faces share an edge smoothly when their
//!   groups share a bit.
//! - A mesh whose faces all have group 0 has no smoothing (normals by the smoothing angle).
//! - A mesh's faces are ordered by their group (as an unsigned 64-bit number after sign
//!   extension, so negative groups come last), in file order within a group, before materials,
//!   control points and triangles are numbered.
//! - Faces without a material use `<group>Mat`, a material of the class defaults, where the group
//!   is the `g` statement's name with spaces as underscores (`default` before any).

/// What the importer reads from an OBJ file for one mesh.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct ObjMesh {
    /// The smoothing group per face (ufbx's face order, which follows the file); empty when the
    /// file's faces could not be matched to the meshes'.
    pub groups: Vec<u32>,
    /// The material of its faces without one.
    pub default_material: String,
}

/// The group a `s` statement leaves (`rest`: the text after the keyword).
pub(super) fn statement(rest: &str, current: u32) -> u32 {
    let text = rest.trim_start();
    let (sign, digits) = match text.as_bytes().first() {
        Some(b'-') => (-1i64, &text[1..]),
        Some(b'+') => (1, &text[1..]),
        _ => (1, text),
    };
    let end = digits.bytes().take_while(u8::is_ascii_digit).count();
    if end > 0 {
        let value = digits[..end].bytes().fold(0i64, |v, d| {
            v.wrapping_mul(10).wrapping_add((d - b'0') as i64)
        });
        return (sign * value) as i32 as u32;
    }
    if text.split_whitespace().next() == Some("off") {
        0
    } else {
        current
    }
}

/// The mesh group a `g` statement names.
pub(super) fn group_name(rest: &str) -> String {
    let rest = rest.trim();
    if rest.is_empty() {
        "default".into()
    } else {
        rest.replace(' ', "_")
    }
}

/// Each mesh's smoothing groups and default material, in ufbx's mesh order (which follows the
/// file). When the file's faces do not add up to the meshes', the groups are not used (with a
/// warning when the file has any) and the default material comes from the first group.
pub fn meshes(bytes: &[u8], scene: &ufbx::Scene, warnings: &mut Vec<String>) -> Vec<ObjMesh> {
    let mut group = 0u32;
    let mut name = String::from("default");
    // Per face: its smoothing group and the mesh group then current.
    let mut faces: Vec<(u32, usize)> = Vec::new();
    let mut names: Vec<String> = Vec::new();
    for line in bytes.split(|&b| b == b'\n') {
        let line = String::from_utf8_lossy(line);
        let line = line.trim_start();
        let (keyword, rest) = line.split_at(line.find(char::is_whitespace).unwrap_or(line.len()));
        match keyword {
            "s" => group = statement(rest, group),
            "g" => name = group_name(rest),
            "f" => {
                if names.last() != Some(&name) {
                    names.push(name.clone());
                }
                faces.push((group, names.len() - 1));
            }
            _ => {}
        }
    }
    let total: usize = scene.meshes.iter().map(|m| m.faces.len()).sum();
    if faces.len() != total {
        if faces.iter().any(|&(g, _)| g != 0) {
            warnings.push(format!(
                "the OBJ file has {} faces for {total} imported ones: its smoothing groups are \
                 not used, so normals follow the smoothing angle and faces keep the file's order \
                 (not supported)",
                faces.len()
            ));
        }
        let first = names.first().cloned().unwrap_or_else(|| "default".into());
        return scene
            .meshes
            .iter()
            .map(|_| ObjMesh {
                groups: Vec::new(),
                default_material: format!("{first}Mat"),
            })
            .collect();
    }
    let mut rest = &faces[..];
    scene
        .meshes
        .iter()
        .map(|m| {
            let (mine, others) = rest.split_at(m.faces.len());
            rest = others;
            let group = mine.first().map_or("default", |&(_, n)| names[n].as_str());
            ObjMesh {
                groups: mine.iter().map(|&(g, _)| g).collect(),
                default_material: format!("{group}Mat"),
            }
        })
        .collect()
}

/// The faces ordered by group (see the module documentation).
pub fn face_order(groups: &[u32]) -> Vec<u32> {
    let mut order: Vec<u32> = (0..groups.len() as u32).collect();
    order.sort_by_key(|&f| groups[f as usize] as i32 as i64 as u64);
    order
}

/// Whether the groups give the mesh smoothing.
pub fn smooths(groups: &[u32]) -> bool {
    groups.iter().any(|&g| g != 0)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn statements_read_as_the_importer_does() {
        assert_eq!(statement(" 3", 0), 3);
        assert_eq!(statement(" 1.5", 0), 1);
        assert_eq!(statement(" -1", 0), u32::MAX);
        assert_eq!(statement(" 33", 0), 33);
        assert_eq!(statement(" off", 7), 0);
        assert_eq!(statement(" 0", 7), 0);
        assert_eq!(statement(" on", 7), 7);
        assert_eq!(statement("", 7), 7);
        assert_eq!(statement(" Off", 7), 7);
        assert_eq!(group_name(" left arm "), "left_arm");
        assert_eq!(group_name(""), "default");
    }

    #[test]
    fn faces_are_ordered_by_group_with_negative_groups_last() {
        let groups = [2, 0, u32::MAX, 1, 2, 33, 0];
        assert_eq!(face_order(&groups), vec![1, 6, 3, 0, 4, 5, 2]);
        assert!(!smooths(&[0, 0]));
        assert!(smooths(&[0, 4]));
    }

    #[test]
    fn meshes_take_their_groups_and_default_material() {
        let bytes = b"v 0 0 0\nv 1 0 0\nv 0 1 0\nv 1 1 0\ns 2\nf 1 2 3\ns on\nf 2 4 3\n";
        let opts = ufbx::LoadOpts {
            file_format: ufbx::FileFormat::Obj,
            ..Default::default()
        };
        let scene = ufbx::load_memory(bytes, opts).unwrap();
        let mut warnings = Vec::new();
        let meshes = meshes(bytes, &scene, &mut warnings);
        assert!(warnings.is_empty());
        let groups: Vec<u32> = meshes.iter().flat_map(|m| m.groups.clone()).collect();
        assert_eq!(groups, vec![2, 2]);
        assert_eq!(meshes[0].default_material, "defaultMat");
    }
}
