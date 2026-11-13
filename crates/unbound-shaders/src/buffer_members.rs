//! Buffer members of structs (`struct APVResources { StructuredBuffer<int> index; ... }`, URP's
//! Adaptive Probe Volume resources): HLSL resolves a resource held in a struct statically to the
//! global it was given, and glslang cannot hold a buffer in a struct at all ("could not create
//! assignment"). A buffer member that every assignment in the program gives the same global buffer
//! is that buffer: its declaration and assignments are dropped and its uses read the global. A
//! member given several globals, or anything but a global, is left as it is, and glslang reports
//! the program.
use crate::{function_bodies, identifier_uses, is_ident_byte};
use std::collections::{BTreeSet, HashSet};
use std::ops::Range;

/// The buffer types glslang compiles as storage blocks (textures and typed `Buffer`s in structs are
/// flattened by glslang itself).
const BUFFER_TYPES: &[&str] = &[
    "StructuredBuffer",
    "RWStructuredBuffer",
    "ByteAddressBuffer",
    "RWByteAddressBuffer",
    "AppendStructuredBuffer",
    "ConsumeStructuredBuffer",
    "RasterizerOrderedStructuredBuffer",
    "RasterizerOrderedByteAddressBuffer",
];

/// A buffer member of a top-level struct: the struct, the member and its declaration's range
/// (through its `;`).
struct Member {
    owner: String,
    name: String,
    declaration: Range<usize>,
}

/// Whether a declaration (`StructuredBuffer < int > index`) has a buffer type; its name, the last
/// identifier.
fn buffer_declaration(text: &str) -> Option<String> {
    let text = text.trim();
    let first: String = text
        .chars()
        .take_while(|&c| is_ident_byte(c as u8))
        .collect();
    if !BUFFER_TYPES.contains(&first.as_str()) {
        return None;
    }
    let name: String = text
        .chars()
        .rev()
        .skip_while(|c| c.is_whitespace())
        .take_while(|&c| is_ident_byte(c as u8))
        .collect::<Vec<_>>()
        .into_iter()
        .rev()
        .collect();
    (!name.is_empty() && name != first).then_some(name)
}

/// The top-level `struct` bodies: each struct's name and the byte range inside its braces.
fn structs(source: &str) -> Vec<(String, Range<usize>)> {
    let bytes = source.as_bytes();
    let mut found = Vec::new();
    let mut depth = 0usize;
    let mut i = 0;
    while i < bytes.len() {
        match bytes[i] {
            b'{' => depth += 1,
            b'}' => depth = depth.saturating_sub(1),
            b's' if depth == 0
                && source[i..].starts_with("struct")
                && (i == 0 || !is_ident_byte(bytes[i - 1]))
                && bytes.get(i + 6).is_some_and(|b| b.is_ascii_whitespace()) =>
            {
                let rest = &source[i + 6..];
                let name: String = rest
                    .trim_start()
                    .chars()
                    .take_while(|&c| is_ident_byte(c as u8))
                    .collect();
                let Some(open) = source[i..].find(['{', ';']).map(|o| i + o) else {
                    break;
                };
                if bytes[open] == b'{' && !name.is_empty() {
                    let mut level = 0usize;
                    let mut close = open;
                    for (j, &b) in bytes.iter().enumerate().skip(open) {
                        match b {
                            b'{' => level += 1,
                            b'}' => {
                                level -= 1;
                                if level == 0 {
                                    close = j;
                                    break;
                                }
                            }
                            _ => {}
                        }
                    }
                    if close > open {
                        found.push((name, open + 1..close));
                        i = close + 1;
                        continue;
                    }
                }
            }
            _ => {}
        }
        i += 1;
    }
    found
}

/// The names declared with type `owner` in `text` (`APVResources apvRes`, a parameter or a local).
fn variables_of(text: &str, owner: &str) -> HashSet<String> {
    let mut names = HashSet::new();
    for at in identifier_uses(text, owner) {
        let rest = &text[at + owner.len()..];
        let trimmed = rest.trim_start();
        if trimmed.len() == rest.len() {
            continue;
        }
        let name: String = trimmed
            .chars()
            .take_while(|&c| is_ident_byte(c as u8))
            .collect();
        if !name.is_empty() && !name.starts_with(|c: char| c.is_ascii_digit()) {
            names.insert(name);
        }
    }
    names
}

/// `variable.member` occurrences in `text`: where each starts and ends.
fn member_uses(text: &str, variable: &str, member: &str) -> Vec<Range<usize>> {
    let bytes = text.as_bytes();
    identifier_uses(text, variable)
        .filter_map(|at| {
            let mut j = at + variable.len();
            while j < bytes.len() && bytes[j].is_ascii_whitespace() {
                j += 1;
            }
            if bytes.get(j) != Some(&b'.') {
                return None;
            }
            j += 1;
            while j < bytes.len() && bytes[j].is_ascii_whitespace() {
                j += 1;
            }
            let end = j + member.len();
            (text[j..].starts_with(member) && (end == bytes.len() || !is_ident_byte(bytes[end])))
                .then_some(at..end)
        })
        .collect()
}

/// An assignment `<use> = <identifier>;` starting at a member use: the identifier and the range
/// through the `;`. None for any other use (a read, `==`, a compound or non-identifier write).
fn assignment(text: &str, use_end: usize) -> Option<(String, usize)> {
    let rest = &text[use_end..];
    let after = rest.trim_start();
    if !after.starts_with('=') || after.starts_with("==") {
        return None;
    }
    let value_start = use_end + (rest.len() - after.len()) + 1;
    let semicolon = value_start + text[value_start..].find(';')?;
    let value = text[value_start..semicolon].trim();
    (!value.is_empty() && value.bytes().all(is_ident_byte))
        .then(|| (value.to_string(), semicolon + 1))
}

/// Writes in every write of a member that is not `= <identifier>` (`+=` and the like on a buffer
/// are not HLSL, but an out-argument or a non-identifier value would be).
fn written_otherwise(text: &str, use_end: usize) -> bool {
    let after = text[use_end..].trim_start();
    after.starts_with('=') && !after.starts_with("==") && assignment(text, use_end).is_none()
}

/// The program with every buffer member that is given one global buffer replaced by it.
pub(crate) fn buffer_members(source: &str) -> String {
    if !source.contains("Buffer") {
        return source.to_string();
    }
    let mut members = Vec::new();
    for (owner, body) in structs(source) {
        let mut start = body.start;
        for (offset, _) in source[body.clone()].match_indices(';') {
            let end = body.start + offset + 1;
            if let Some(name) = buffer_declaration(&source[start..end - 1]) {
                members.push(Member {
                    owner: owner.clone(),
                    name,
                    declaration: start..end,
                });
            }
            start = end;
        }
    }
    if members.is_empty() {
        return source.to_string();
    }
    let functions = function_bodies(source);
    // Top-level buffer declarations (outside struct bodies and functions).
    let mut globals = HashSet::new();
    let mut statement_start = 0usize;
    let mut depth = 0usize;
    for (i, b) in source.bytes().enumerate() {
        match b {
            b'{' => depth += 1,
            b'}' => {
                depth = depth.saturating_sub(1);
                if depth == 0 {
                    statement_start = i + 1;
                }
            }
            b';' if depth == 0 => {
                let statement = &source[statement_start..i];
                let statement = statement
                    .lines()
                    .filter(|l| !l.trim_start().starts_with('#'))
                    .collect::<Vec<_>>()
                    .join(" ");
                let statement = statement
                    .trim_start()
                    .trim_start_matches("uniform ")
                    .to_string();
                if let Some(name) = buffer_declaration(statement.split(':').next().unwrap_or("")) {
                    globals.insert(name);
                }
                statement_start = i + 1;
            }
            _ => {}
        }
    }

    // Scopes: each function (its header and body) with the names it declares of each struct.
    let scopes: Vec<Range<usize>> = functions.iter().map(|(h, b)| *h..b.end).collect();
    let mut edits: Vec<(Range<usize>, String)> = Vec::new();
    let mut removed = HashSet::new();
    for member in &members {
        let mut assigned = BTreeSet::new();
        let mut writes = Vec::new();
        let mut reads = Vec::new();
        let mut unsupported = false;
        for scope in &scopes {
            let text = &source[scope.clone()];
            for variable in variables_of(text, &member.owner) {
                for range in member_uses(text, &variable, &member.name) {
                    let at = scope.start + range.start;
                    let end = scope.start + range.end;
                    if let Some((value, through)) = assignment(source, end) {
                        assigned.insert(value);
                        writes.push(at..through);
                    } else if written_otherwise(source, end) {
                        unsupported = true;
                    } else {
                        reads.push(at..end);
                    }
                }
            }
        }
        let global = match (unsupported, assigned.len()) {
            (false, 1) => assigned.into_iter().next().unwrap(),
            _ => continue,
        };
        if !globals.contains(&global) {
            continue;
        }
        edits.push((member.declaration.clone(), String::new()));
        edits.extend(writes.into_iter().map(|r| (r, String::new())));
        edits.extend(reads.into_iter().map(|r| (r, global.clone())));
        removed.insert(member.declaration.start);
    }
    if edits.is_empty() {
        return source.to_string();
    }
    // A struct left without members keeps one, as HLSL needs.
    for (_, body) in structs(source) {
        let declared = source[body.clone()].matches(';').count();
        let dropped = removed.iter().filter(|&&at| body.contains(&at)).count();
        if dropped > 0 && dropped == declared {
            edits.push((
                body.start..body.start,
                " int unbound_no_members;".to_string(),
            ));
        }
    }
    edits.sort_by_key(|(r, _)| std::cmp::Reverse(r.start));
    let mut out = source.to_string();
    for (range, text) in edits {
        out.replace_range(range, &text);
    }
    out
}

#[cfg(test)]
mod tests {
    use super::buffer_members;

    /// glslang (as the compiler runs it) on a vertex program.
    fn compiles(source: &str) -> Result<(), String> {
        let compiler = shaderc::Compiler::new().unwrap();
        let mut o = shaderc::CompileOptions::new().unwrap();
        o.set_source_language(shaderc::SourceLanguage::HLSL);
        o.set_auto_bind_uniforms(true);
        o.set_hlsl_functionality1(true);
        o.set_target_env(
            shaderc::TargetEnv::Vulkan,
            shaderc::EnvVersion::Vulkan1_1 as u32,
        );
        compiler
            .compile_into_spirv(source, shaderc::ShaderKind::Vertex, "t", "main", Some(&o))
            .map(|_| ())
            .map_err(|e| e.to_string())
    }

    /// URP's Adaptive Probe Volume resources: a struct holding a structured buffer and a texture,
    /// filled from globals and read by another function.
    const APV: &str = "StructuredBuffer<int> _APVResIndex;\n\
        Texture3D _APVResL0;\n\
        SamplerState s_linear_clamp_sampler;\n\
        struct APVResources\n{\n StructuredBuffer < int > index;\n Texture3D L0;\n};\n\
        struct Other { int index; };\n\
        APVResources FillAPVResources()\n{\n APVResources apvRes;\n apvRes.index = _APVResIndex;\n apvRes.L0 = _APVResL0;\n return apvRes;\n}\n\
        float Sample(APVResources apvRes, float3 uvw, Other other)\n{\n \
        return apvRes.L0.SampleLevel(s_linear_clamp_sampler, uvw, 0).x + apvRes . index[other.index];\n}\n\
        float4 main(float4 p : POSITION) : SV_Position\n{\n Other o; o.index = 2;\n \
        APVResources r = FillAPVResources();\n return float4(Sample(r, p.xyz, o), 0, 0, 1);\n}\n";

    #[test]
    fn a_buffer_member_given_one_global_is_that_global() {
        assert!(
            compiles(APV).is_err(),
            "glslang holds no buffer in a struct"
        );
        let fixed = buffer_members(APV);
        assert!(
            !fixed.contains("StructuredBuffer < int > index;"),
            "{fixed}"
        );
        assert!(!fixed.contains("apvRes.index = _APVResIndex;"), "{fixed}");
        assert!(fixed.contains("_APVResIndex[other.index]"), "{fixed}");
        // The texture member and the other struct's `index` are left alone.
        assert!(fixed.contains("apvRes.L0 = _APVResL0;"), "{fixed}");
        assert!(fixed.contains("o.index = 2;"), "{fixed}");
        compiles(&fixed).unwrap();
    }

    #[test]
    fn a_member_given_two_globals_is_left_for_glslang_to_report() {
        let two = APV
            .replace(
                "StructuredBuffer<int> _APVResIndex;",
                "StructuredBuffer<int> _APVResIndex;\nStructuredBuffer<int> _Other;",
            )
            .replace(
                "return float4(",
                "APVResources s = r; s.index = _Other;\n return float4(",
            );
        assert_eq!(buffer_members(&two), two);
        assert!(compiles(&two).is_err());
    }

    #[test]
    fn a_member_given_a_non_global_is_left_alone() {
        let local = APV.replace(
            "apvRes.index = _APVResIndex;",
            "apvRes.index = Pick(_APVResIndex);",
        );
        assert_eq!(buffer_members(&local), local);
    }

    #[test]
    fn a_struct_left_without_members_keeps_one() {
        let only = "StructuredBuffer<float> _B;\nstruct R { StructuredBuffer<float> b; };\n\
            float4 main(float4 p : POSITION) : SV_Position { R r; r.b = _B; return float4(r.b[0], 0, 0, 1); }";
        let fixed = buffer_members(only);
        assert!(
            fixed.contains("struct R { int unbound_no_members; }"),
            "{fixed}"
        );
        assert!(fixed.contains("float4(_B[0], 0, 0, 1)"), "{fixed}");
        compiles(&fixed).unwrap();
    }

    #[test]
    fn programs_without_buffer_members_are_unchanged() {
        let plain = "StructuredBuffer<float> _B;\nstruct R { float x; };\nfloat4 main() : SV_Position { return _B[0]; }";
        assert_eq!(buffer_members(plain), plain);
    }
}
