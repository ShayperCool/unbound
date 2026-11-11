//! The meshes of an OBJ file as the model importer divides it (matches the Unity 6000.0.71f1
//! Editor, tests/projects/lightmapuvs Names):
//!
//! - `g` statements alone divide the file into meshes; `o` statements are not read.
//! - A mesh and its object are named after the group: its words joined by underscores, and
//!   `default` for faces before any `g` and for a `g` without a name.
//! - Faces of one group name make one mesh wherever they stand in the file, in file order; the
//!   meshes come in the order their groups first have a face. A group without faces gives none.
//!   Names that differ only in ASCII letter case are one group, spelled as it first has a face.
//!
//! [`regroup`] rewrites the file into that form for the loader, which starts a mesh at the first
//! face after a `g` statement. Bytes that are not ASCII pass through untouched.

use super::obj_smoothing::statement;
use std::borrow::Cow;
use std::collections::HashMap;

/// The first word of a line and the rest (without the line end).
fn split(line: &[u8]) -> (&[u8], &[u8]) {
    let line = line.trim_ascii();
    let end = line
        .iter()
        .position(u8::is_ascii_whitespace)
        .unwrap_or(line.len());
    line.split_at(end)
}

fn is_element(keyword: &[u8]) -> bool {
    matches!(keyword, b"f" | b"l" | b"p")
}

/// The mesh a `g` statement names: its words joined by underscores, `default` without any.
fn group_name(rest: &[u8]) -> Vec<u8> {
    let rest = rest.trim_ascii();
    if rest.is_empty() {
        return b"default".to_vec();
    }
    rest.iter()
        .map(|&b| if b == b' ' { b'_' } else { b })
        .collect()
}

/// What makes two names one group: the name without its ASCII letter case.
fn group_key(name: &[u8]) -> Vec<u8> {
    name.to_ascii_lowercase()
}

/// The file with its `o` statements removed and a `g` statement, named as the importer names
/// meshes, before the first face of each run of one group; when a group returns after another
/// group's faces, the faces are gathered per group. The warning says what could not be gathered.
pub fn regroup(bytes: &[u8]) -> (Vec<u8>, Option<String>) {
    // Whether a group has faces after another group's faces followed its own.
    let mut seen: HashMap<Vec<u8>, ()> = HashMap::new();
    let mut key = group_key(b"default");
    let mut last: Option<Vec<u8>> = None;
    let mut returns = false;
    for line in bytes.split(|&b| b == b'\n') {
        let (keyword, rest) = split(line);
        if keyword == b"g" {
            key = group_key(&group_name(rest));
        } else if is_element(keyword) && last.as_ref() != Some(&key) {
            if seen.insert(key.clone(), ()).is_some() {
                returns = true;
                break;
            }
            last = Some(key.clone());
        }
    }
    if returns {
        match gather(bytes) {
            Ok(gathered) => return (gathered, None),
            Err(reason) => {
                return (
                    rename(bytes),
                    Some(format!(
                        "the OBJ file's group names return after other groups and {reason}: each \
                         run of faces stays a mesh of its own (not supported)"
                    )),
                );
            }
        }
    }
    (rename(bytes), None)
}

fn push_line(out: &mut Vec<u8>, keyword: &[u8], rest: &[u8]) {
    out.extend_from_slice(keyword);
    out.push(b' ');
    out.extend_from_slice(rest);
    out.push(b'\n');
}

/// The text without its `o` and `g` statements, with a `g` statement before each face whose group
/// differs from the face before it.
fn rename(bytes: &[u8]) -> Vec<u8> {
    let mut out = Vec::with_capacity(bytes.len() + 16);
    let mut name = b"default".to_vec();
    let mut key = group_key(&name);
    let mut written: Option<Vec<u8>> = None;
    for line in bytes.split_inclusive(|&b| b == b'\n') {
        let (keyword, rest) = split(line);
        match keyword {
            b"o" => {}
            b"g" => {
                name = group_name(rest);
                key = group_key(&name);
            }
            _ => {
                if is_element(keyword) && written.as_ref() != Some(&key) {
                    push_line(&mut out, b"g", &name);
                    written = Some(key.clone());
                }
                out.extend_from_slice(line);
            }
        }
    }
    out
}

struct Element<'a> {
    keyword: &'a [u8],
    corners: Cow<'a, [u8]>,
    material: Option<&'a [u8]>,
    smoothing: u32,
}

/// The text with every group's faces together, in the order the groups first have a face: the
/// vertex data stays where it is and the faces follow it, each with the material and smoothing
/// group it had.
fn gather(bytes: &[u8]) -> Result<Vec<u8>, &'static str> {
    let mut out = Vec::with_capacity(bytes.len() + 64);
    let mut groups: Vec<(Vec<u8>, Vec<Element>)> = Vec::new();
    let mut index: HashMap<Vec<u8>, usize> = HashMap::new();
    let mut name = b"default".to_vec();
    let mut current: Option<usize> = None;
    let mut material = None;
    let mut smoothing = 0u32;
    // The numbers of positions, texture coordinates and normals read so far.
    let mut counts = [0i64; 3];
    for line in bytes.split_inclusive(|&b| b == b'\n') {
        if line.trim_ascii_end().ends_with(b"\\") {
            return Err("a statement continues on the next line");
        }
        let (keyword, rest) = split(line);
        match keyword {
            b"o" => {}
            b"g" => {
                name = group_name(rest);
                current = None;
            }
            b"s" => smoothing = statement(&String::from_utf8_lossy(rest), smoothing),
            b"usemtl" => material = Some(rest.trim_ascii()),
            _ if is_element(keyword) => {
                let group = *current.get_or_insert_with(|| {
                    *index.entry(group_key(&name)).or_insert_with(|| {
                        groups.push((name.clone(), Vec::new()));
                        groups.len() - 1
                    })
                });
                groups[group].1.push(Element {
                    keyword,
                    corners: absolute(rest.trim_ascii(), counts),
                    material,
                    smoothing,
                });
            }
            _ => {
                match keyword {
                    b"v" => counts[0] += 1,
                    b"vt" => counts[1] += 1,
                    b"vn" => counts[2] += 1,
                    _ => {}
                }
                out.extend_from_slice(line);
                if !line.ends_with(b"\n") {
                    out.push(b'\n');
                }
            }
        }
    }
    let mut current_material = None;
    let mut current_smoothing = 0u32;
    for (name, elements) in &groups {
        push_line(&mut out, b"g", name);
        for element in elements {
            match element.material {
                Some(material) if current_material != Some(material) => {
                    push_line(&mut out, b"usemtl", material);
                    current_material = Some(material);
                }
                // A material once named stays in force: a face before the file's first
                // `usemtl` cannot follow one that has it.
                None if current_material.is_some() => {
                    return Err("faces without a material would follow faces with one");
                }
                _ => {}
            }
            if element.smoothing != current_smoothing {
                push_line(
                    &mut out,
                    b"s",
                    (element.smoothing as i32).to_string().as_bytes(),
                );
                current_smoothing = element.smoothing;
            }
            push_line(&mut out, element.keyword, &element.corners);
        }
    }
    Ok(out)
}

/// An element's corners with indices counted from the end (negative ones) made absolute, so the
/// element can move behind vertex data that follows it.
fn absolute(corners: &[u8], counts: [i64; 3]) -> Cow<'_, [u8]> {
    if !corners.contains(&b'-') {
        return Cow::Borrowed(corners);
    }
    let mut out = Vec::with_capacity(corners.len() + 8);
    for (i, corner) in corners
        .split(u8::is_ascii_whitespace)
        .filter(|c| !c.is_empty())
        .enumerate()
    {
        if i > 0 {
            out.push(b' ');
        }
        for (k, index) in corner.split(|&b| b == b'/').enumerate() {
            if k > 0 {
                out.push(b'/');
            }
            let number = std::str::from_utf8(index)
                .ok()
                .and_then(|t| t.parse::<i64>().ok());
            match number {
                Some(n) if n < 0 && k < 3 => {
                    out.extend_from_slice((counts[k] + n + 1).to_string().as_bytes())
                }
                _ => out.extend_from_slice(index),
            }
        }
    }
    Cow::Owned(out)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn regrouped(text: &str) -> (String, Option<String>) {
        let (bytes, warning) = regroup(text.as_bytes());
        (String::from_utf8(bytes).unwrap(), warning)
    }

    #[test]
    fn objects_are_dropped_and_groups_named() {
        let (out, warning) = regrouped("o Thing\nv 0 0 0\nf 1 1 1\ng one two\nf 1 1 1\n");
        assert_eq!(out, "v 0 0 0\ng default\nf 1 1 1\ng one_two\nf 1 1 1\n");
        assert!(warning.is_none());
        // A group without a name is `default`, like the faces before any group.
        let (out, _) = regrouped("v 0 0 0\nf 1 1 1\ng\nf 1 1 1\ng Named\nf 1 1 1\n");
        assert_eq!(
            out,
            "v 0 0 0\ng default\nf 1 1 1\nf 1 1 1\ng Named\nf 1 1 1\n"
        );
    }

    #[test]
    fn a_file_whose_groups_do_not_return_keeps_its_lines() {
        let text = "mtllib a.mtl\r\no A\r\ng First\r\nv 0 0 0\r\nusemtl m\r\ns 1\r\nf -1 -1 -1\r\n\
                    g First\r\n\tf 1 1 1\r\no B\r\ng Second\r\nf 1 1 1";
        let (out, warning) = regrouped(text);
        assert_eq!(
            out,
            "mtllib a.mtl\r\nv 0 0 0\r\nusemtl m\r\ns 1\r\ng First\nf -1 -1 -1\r\n\tf 1 1 1\r\n\
             g Second\nf 1 1 1"
        );
        assert!(warning.is_none());
    }

    #[test]
    fn a_group_without_faces_does_not_divide_another() {
        // `B` has no face: `A` stays one mesh, as a returning group would.
        let (out, warning) = regrouped("v 0 0 0\ng A\nf 1 1 1\ng B\ng A\nf 1 1 1\ng C\n");
        assert_eq!(out, "v 0 0 0\ng A\nf 1 1 1\nf 1 1 1\n");
        assert!(warning.is_none());
    }

    #[test]
    fn a_returning_group_gathers_its_faces_with_their_state() {
        let text = "g A\nv 0 0 0\nvt 0 0\nusemtl m\nf 1/1 1/1 1/1\ng B\ns 2\nv 1 0 0\nf -1 -2 -1\n\
                    g A\nv 2 0 0\nusemtl n\nf -1/-1 1/1 2/1\ns off\ng B\nf 3 3 3\n";
        let (out, warning) = regrouped(text);
        assert!(warning.is_none());
        assert_eq!(
            out,
            "v 0 0 0\nvt 0 0\nv 1 0 0\nv 2 0 0\n\
             g A\nusemtl m\nf 1/1 1/1 1/1\nusemtl n\ns 2\nf 3/1 1/1 2/1\n\
             g B\nusemtl m\nf 2 1 2\nusemtl n\ns 0\nf 3 3 3\n"
        );
    }

    #[test]
    fn names_differing_in_case_are_one_group_with_its_first_spelling() {
        let (out, warning) = regrouped("v 0 0 0\ng part\nf 1 1 1\ng PART\nf 1 1 1\n");
        assert_eq!(out, "v 0 0 0\ng part\nf 1 1 1\nf 1 1 1\n");
        assert!(warning.is_none());
        let (out, _) = regrouped("v 0 0 0\ng Part\nf 1 1 1\ng Other\nf 1 1 1\ng pART\nf 1 1 1\n");
        assert_eq!(out, "v 0 0 0\ng Part\nf 1 1 1\nf 1 1 1\ng Other\nf 1 1 1\n");
    }

    #[test]
    fn bytes_that_are_not_text_pass_through() {
        // A material and a group named in another encoding keep their bytes for the loader.
        let mut text = b"v 0 0 0\nusemtl ".to_vec();
        text.extend_from_slice(&[0xC6, 0xE1, 0xBA, 0xDA]);
        text.extend_from_slice(b"\ng ");
        text.extend_from_slice(&[0xC6, 0xE1]);
        text.extend_from_slice(b"\nf 1 1 1\n");
        let (out, warning) = regroup(&text);
        assert!(warning.is_none());
        let mut expected = b"v 0 0 0\nusemtl ".to_vec();
        expected.extend_from_slice(&[0xC6, 0xE1, 0xBA, 0xDA]);
        expected.extend_from_slice(b"\ng ");
        expected.extend_from_slice(&[0xC6, 0xE1]);
        expected.extend_from_slice(b"\nf 1 1 1\n");
        assert_eq!(out, expected);
    }

    #[test]
    fn many_groups_are_read_in_one_pass() {
        // One group per face, each returning once: the work stays proportional to the file.
        let mut text = String::from("v 0 0 0\n");
        for round in 0..2 {
            for g in 0..100_000 {
                text += &format!("g part{g}\nf 1 1 1\n");
                let _ = round;
            }
        }
        let start = std::time::Instant::now();
        let (out, warning) = regrouped(&text);
        assert!(warning.is_none());
        assert_eq!(out.matches("\ng ").count(), 100_000);
        assert_eq!(out.matches("\nf ").count(), 200_000);
        assert!(start.elapsed().as_secs() < 20, "{:?}", start.elapsed());
    }

    #[test]
    fn faces_that_cannot_be_gathered_stay_in_place_with_a_warning() {
        // B's face has no material and would follow A's second face, which has one.
        let text = "g A\nv 0 0 0\nf 1 1 1\ng B\nf 1 1 1\nusemtl m\ng A\nf 1 1 1\n";
        let (out, warning) = regrouped(text);
        assert!(warning.unwrap().contains("not supported"));
        assert_eq!(
            out,
            "v 0 0 0\ng A\nf 1 1 1\ng B\nf 1 1 1\nusemtl m\ng A\nf 1 1 1\n"
        );
        let (_, warning) = regrouped("g A\nf 1 1 \\\n1\ng B\nf 1 1 1\ng A\nf 1 1 1\n");
        assert!(warning.unwrap().contains("continues"));
    }
}
