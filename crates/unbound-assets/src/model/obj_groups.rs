//! The meshes of an OBJ file as the model importer divides it (matches the Unity 6000.0.71f1
//! Editor, tests/projects/lightmapuvs Names):
//!
//! - `g` statements alone divide the file into meshes; `o` statements are not read.
//! - A mesh and its object are named after the group: its words joined by underscores, and
//!   `default` for faces before any `g` and for a `g` without a name.
//! - Faces of one group name make one mesh wherever they stand in the file, in file order; the
//!   meshes come in the order their groups first have a face. A group without faces gives none.
//!   Names that differ only in letter case are one group, spelled as it first has a face.
//!
//! [`regroup`] rewrites the file's text into that form for the loader, which makes a mesh per
//! run of `g` statements.

use super::obj_smoothing::{group_name, statement};
use std::borrow::Cow;

/// The first word of a line and the rest.
fn split(line: &str) -> (&str, &str) {
    let line = line.trim_start();
    line.split_at(line.find(char::is_whitespace).unwrap_or(line.len()))
}

/// Whether two group names are one group: they differ at most in letter case.
fn same(a: &str, b: &str) -> bool {
    a.to_lowercase() == b.to_lowercase()
}

fn is_element(keyword: &str) -> bool {
    matches!(keyword, "f" | "l" | "p")
}

/// The file with its `o` statements removed and its `g` statements named as the importer names
/// meshes; when a group name returns after another group's faces, the faces are gathered per
/// group. The warning says what could not be gathered.
pub fn regroup(bytes: &[u8]) -> (Vec<u8>, Option<String>) {
    let text = String::from_utf8_lossy(bytes);
    // The groups in the order their faces first appear, a group again when it returns.
    let mut runs: Vec<String> = Vec::new();
    let mut name = String::from("default");
    for line in text.lines() {
        let (keyword, rest) = split(line);
        if keyword == "g" {
            name = group_name(rest);
        } else if is_element(keyword) && runs.last().is_none_or(|last| !same(last, &name)) {
            runs.push(name.clone());
        }
    }
    let returns = runs
        .iter()
        .enumerate()
        .any(|(i, run)| runs[..i].iter().any(|earlier| same(earlier, run)));
    if returns {
        match gather(&text) {
            Ok(gathered) => return (gathered.into_bytes(), None),
            Err(reason) => {
                return (
                    rename(&text).into_bytes(),
                    Some(format!(
                        "the OBJ file's group names return after other groups and {reason}: each \
                         run of faces stays a mesh of its own (not supported)"
                    )),
                );
            }
        }
    }
    (rename(&text).into_bytes(), None)
}

/// The text with `o` statements removed and `g` statements renamed, a statement that keeps the
/// current group left out.
fn rename(text: &str) -> String {
    let mut out = String::with_capacity(text.len() + 16);
    let mut name = String::from("default");
    out.push_str("g default\n");
    for line in text.split_inclusive('\n') {
        let (keyword, rest) = split(line);
        match keyword {
            "o" => {}
            "g" => {
                let next = group_name(rest);
                if !same(&next, &name) {
                    out.push_str("g ");
                    out.push_str(&next);
                    out.push('\n');
                    name = next;
                }
            }
            _ => out.push_str(line),
        }
    }
    out
}

struct Element<'a> {
    line: Cow<'a, str>,
    material: Option<&'a str>,
    smoothing: u32,
}

/// The text with every group's faces together, in the order the groups first have a face: the
/// vertex data stays where it is and the faces follow it, each with the material and smoothing
/// group it had.
fn gather(text: &str) -> Result<String, &'static str> {
    let mut out = String::with_capacity(text.len() + 64);
    let mut groups: Vec<(String, Vec<Element>)> = Vec::new();
    let mut name = String::from("default");
    let mut material = None;
    let mut smoothing = 0u32;
    // The numbers of positions, texture coordinates and normals read so far.
    let mut counts = [0i64; 3];
    for line in text.lines() {
        if line.trim_end().ends_with('\\') {
            return Err("a statement continues on the next line");
        }
        let (keyword, rest) = split(line);
        match keyword {
            "o" => {}
            "g" => name = group_name(rest),
            "s" => smoothing = statement(rest, smoothing),
            "usemtl" => material = Some(rest.trim()),
            _ if is_element(keyword) => {
                let element = Element {
                    line: absolute(keyword, rest, counts),
                    material,
                    smoothing,
                };
                match groups.iter_mut().find(|(n, _)| same(n, &name)) {
                    Some((_, elements)) => elements.push(element),
                    None => groups.push((name.clone(), vec![element])),
                }
            }
            _ => {
                match keyword {
                    "v" => counts[0] += 1,
                    "vt" => counts[1] += 1,
                    "vn" => counts[2] += 1,
                    _ => {}
                }
                out.push_str(line);
                out.push('\n');
            }
        }
    }
    let mut current_material = None;
    let mut current_smoothing = 0u32;
    for (name, elements) in &groups {
        out.push_str("g ");
        out.push_str(name);
        out.push('\n');
        for element in elements {
            match element.material {
                Some(material) if current_material != Some(material) => {
                    out.push_str("usemtl ");
                    out.push_str(material);
                    out.push('\n');
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
                out.push_str(&format!("s {}\n", element.smoothing as i32));
                current_smoothing = element.smoothing;
            }
            out.push_str(&element.line);
            out.push('\n');
        }
    }
    Ok(out)
}

/// An element statement with indices counted from the end (negative ones) made absolute, so it
/// can move behind vertex data that follows it.
fn absolute<'a>(keyword: &str, rest: &'a str, counts: [i64; 3]) -> Cow<'a, str> {
    let whole = || Cow::Owned(format!("{keyword}{rest}"));
    if !rest.contains('-') {
        return whole();
    }
    let mut out = String::from(keyword);
    for corner in rest.split_whitespace() {
        out.push(' ');
        for (k, index) in corner.split('/').enumerate() {
            if k > 0 {
                out.push('/');
            }
            match index.parse::<i64>() {
                Ok(n) if n < 0 && k < 3 => out.push_str(&(counts[k] + n + 1).to_string()),
                _ => out.push_str(index),
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
        assert_eq!(out, "g default\nv 0 0 0\nf 1 1 1\ng one_two\nf 1 1 1\n");
        assert!(warning.is_none());
        // A group without a name is `default`, like the faces before any group.
        let (out, _) = regrouped("v 0 0 0\ng\nf 1 1 1\ng Named\nf 1 1 1\n");
        assert_eq!(out, "g default\nv 0 0 0\nf 1 1 1\ng Named\nf 1 1 1\n");
    }

    #[test]
    fn a_file_whose_groups_do_not_return_keeps_its_lines() {
        let text = "mtllib a.mtl\no A\ng First\nv 0 0 0\nusemtl m\ns 1\nf -1 -1 -1\ng First\nf 1 1 1\n\
                    o B\ng Second\nf 1 1 1\n";
        let (out, warning) = regrouped(text);
        assert_eq!(
            out,
            "g default\nmtllib a.mtl\ng First\nv 0 0 0\nusemtl m\ns 1\nf -1 -1 -1\nf 1 1 1\n\
             g Second\nf 1 1 1\n"
        );
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
        assert_eq!(out, "g default\nv 0 0 0\ng part\nf 1 1 1\nf 1 1 1\n");
        assert!(warning.is_none());
        let (out, _) = regrouped("v 0 0 0\ng Part\nf 1 1 1\ng Other\nf 1 1 1\ng pART\nf 1 1 1\n");
        assert_eq!(out, "v 0 0 0\ng Part\nf 1 1 1\nf 1 1 1\ng Other\nf 1 1 1\n");
    }

    #[test]
    fn faces_that_cannot_be_gathered_stay_in_place_with_a_warning() {
        // B's face has no material and would follow A's second face, which has one.
        let text = "g A\nv 0 0 0\nf 1 1 1\ng B\nf 1 1 1\nusemtl m\ng A\nf 1 1 1\n";
        let (out, warning) = regrouped(text);
        assert!(warning.unwrap().contains("not supported"));
        assert_eq!(
            out,
            "g default\ng A\nv 0 0 0\nf 1 1 1\ng B\nf 1 1 1\nusemtl m\ng A\nf 1 1 1\n"
        );
        let (_, warning) = regrouped("g A\nf 1 1 \\\n1\ng B\nf 1 1 1\ng A\nf 1 1 1\n");
        assert!(warning.unwrap().contains("continues"));
    }
}
