//! Structs declared without members (`struct GraphProperties {};`, what Shader Graph declares
//! for a graph without properties). FXC takes them, and a zero cast of a struct holding one
//! (`(MotionVectorPassOutput)0`, the render pipeline's motion vector pass of a Visual Effect
//! Graph output) zeroes the other members; glslang rejects such a cast ("wrong number of
//! structure members"), so the pass did not compile. In a program compiled through the
//! rewrites (one that does not compile as it is, or one sent there at once: `fxc_compatible`),
//! each such struct gets one member of its own, which nothing reads. A struct that may lie in
//! a buffer's memory (named in a cbuffer or tbuffer, as a template's argument such as a
//! structured buffer's element, or in a struct that may) keeps no member: the filler would
//! move the members after it, which a writer of the buffer's bytes would not expect, so such
//! a program fails as before. Not handled: an empty struct as a stage's input or output (such
//! a program did not compile before and still does not).

use std::collections::{HashMap, HashSet};

/// The member given to a struct declared without one.
const FILLER: &str = "uint unbound_empty_struct_filler;";

/// Whether `b` may be part of an identifier.
fn word_byte(b: u8) -> bool {
    b.is_ascii_alphanumeric() || b == b'_'
}

/// The identifiers of `text`.
fn words(text: &str) -> impl Iterator<Item = &str> {
    text.split(|c: char| !(c.is_ascii_alphanumeric() || c == '_'))
        .filter(|w| !w.is_empty())
}

/// The text between the brace at `open` and its match (all of the rest when unmatched).
fn braced(source: &str, open: usize) -> &str {
    let mut depth = 0usize;
    for (i, b) in source.bytes().enumerate().skip(open) {
        match b {
            b'{' => depth += 1,
            b'}' => {
                depth -= 1;
                if depth == 0 {
                    return &source[open + 1..i];
                }
            }
            _ => {}
        }
    }
    &source[open + 1..]
}

/// The bodies of the blocks a keyword opens (`struct NAME {..}`, `cbuffer NAME {..}`), each with
/// the name after the keyword.
fn blocks<'a>(source: &'a str, keyword: &str) -> Vec<(&'a str, &'a str)> {
    let bytes = source.as_bytes();
    let mut found = Vec::new();
    let mut at = 0;
    while let Some(i) = source[at..].find(keyword) {
        let start = at + i;
        at = start + keyword.len();
        if start.checked_sub(1).is_some_and(|i| word_byte(bytes[i]))
            || bytes.get(at).is_some_and(|b| word_byte(*b))
        {
            continue;
        }
        let rest = source[at..].trim_start();
        let name_start = source.len() - rest.len();
        let name_end = name_start
            + rest
                .find(|c: char| !(c.is_ascii_alphanumeric() || c == '_'))
                .unwrap_or(rest.len());
        let after = source[name_end..].trim_start();
        // A cbuffer may name a register first (`cbuffer C : register(b0) {`).
        let Some(open) = after.find('{').filter(|&o| !after[..o].contains(';')) else {
            continue;
        };
        let open = source.len() - after.len() + open;
        found.push((&source[name_start..name_end], braced(source, open)));
    }
    found
}

/// The names of the structs that may lie in a buffer's memory: those named in a cbuffer or
/// tbuffer or as a template's argument, and those named in such a struct's body (any name in
/// it counts, so the set errs on the side of leaving a struct as it is).
fn in_buffers(source: &str) -> HashSet<&str> {
    let structs: HashMap<&str, &str> = blocks(source, "struct").into_iter().collect();
    let mut seen = HashSet::new();
    let mut pending: Vec<&str> = Vec::new();
    for keyword in ["cbuffer", "tbuffer"] {
        for (_, body) in blocks(source, keyword) {
            pending.extend(words(body));
        }
    }
    for (i, _) in source.match_indices('<') {
        pending.extend(words(&source[i + 1..]).next());
    }
    while let Some(name) = pending.pop() {
        if seen.insert(name) {
            if let Some(body) = structs.get(name) {
                pending.extend(words(body));
            }
        }
    }
    seen
}

/// `source` with every struct declared without members given one ([`FILLER`]), but those that
/// may lie in a buffer's memory ([`in_buffers`]).
pub(crate) fn empty_structs(source: &str) -> String {
    let kept = in_buffers(source);
    let bytes = source.as_bytes();
    let mut out = String::with_capacity(source.len());
    let mut copied = 0;
    let mut at = 0;
    while let Some(found) = source[at..].find("struct") {
        let start = at + found;
        at = start + "struct".len();
        // The keyword alone, not part of a longer name.
        let before = start.checked_sub(1).map(|i| bytes[i]);
        if before.is_some_and(|b| b.is_ascii_alphanumeric() || b == b'_')
            || bytes
                .get(at)
                .is_some_and(|b| b.is_ascii_alphanumeric() || *b == b'_')
        {
            continue;
        }
        // struct NAME { } with only blanks between the braces.
        let rest = &source[at..];
        let name_len = rest.trim_start().len();
        let name_start = at + rest.len() - name_len;
        let name_end = name_start
            + source[name_start..]
                .find(|c: char| !(c.is_ascii_alphanumeric() || c == '_'))
                .unwrap_or(source.len() - name_start);
        if name_end == name_start || kept.contains(&source[name_start..name_end]) {
            continue;
        }
        let after_name = source[name_end..].trim_start();
        if !after_name.starts_with('{') {
            continue;
        }
        let open = source.len() - after_name.len();
        let inside = &source[open + 1..];
        let body_len = inside.len() - inside.trim_start().len();
        if !inside[body_len..].starts_with('}') {
            continue;
        }
        out.push_str(&source[copied..=open]);
        out.push(' ');
        out.push_str(FILLER);
        out.push(' ');
        copied = open + 1 + body_len;
        at = copied;
    }
    out.push_str(&source[copied..]);
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn empty_structs_get_a_member() {
        assert_eq!(
            empty_structs("struct GraphProperties\n{\n};\nstruct A { float x; };"),
            format!("struct GraphProperties\n{{ {FILLER} }};\nstruct A {{ float x; }};")
        );
        // Names that hold the word, members and other uses of braces stay.
        let kept =
            "struct structure { float a; }; mystruct {} ; struct_b {}; float4 f() { return 0; }";
        assert_eq!(empty_structs(kept), kept);
    }

    /// A struct that may lie in a buffer's memory keeps no member: named in a cbuffer, as a
    /// structured buffer's element, or held by such a struct.
    #[test]
    fn structs_in_buffers_stay_empty() {
        for source in [
            "struct E { };\ncbuffer C : register(b0) { float4 a; E e; float4 b; };",
            "struct E { };\nStructuredBuffer<E> s;",
            "struct E { };\nstruct H { float x; E e; };\nRWStructuredBuffer<H> s;",
            "struct E { };\nstruct H { float x; E e; };\ncbuffer C { H h; };",
        ] {
            assert_eq!(empty_structs(source), source);
        }
        // One beside them that is only a local's type gets its member.
        let mixed = "struct E { };\nstruct L { };\nStructuredBuffer<E> s;";
        assert_eq!(
            empty_structs(mixed),
            format!("struct E {{ }};\nstruct L {{ {FILLER} }};\nStructuredBuffer<E> s;")
        );
    }

    /// glslang rejects a zero cast of a struct that holds one without members; with the
    /// filler it compiles.
    #[test]
    fn zero_casts_of_structs_holding_empty_ones_compile() {
        let compile = |source: &str| {
            let compiler = shaderc::Compiler::new().unwrap();
            let mut o = shaderc::CompileOptions::new().unwrap();
            o.set_source_language(shaderc::SourceLanguage::HLSL);
            o.set_target_env(
                shaderc::TargetEnv::Vulkan,
                shaderc::EnvVersion::Vulkan1_1 as u32,
            );
            compiler
                .compile_into_spirv(source, shaderc::ShaderKind::Compute, "t", "K", Some(&o))
                .map(|_| ())
                .map_err(|e| e.to_string())
        };
        let source = "struct Inner { };\nstruct Outer { float3 position; Inner inner; };\n\
                      RWStructuredBuffer<float4> o;\n[numthreads(1, 1, 1)] void K() {\n\
                      Outer v = (Outer)0; v.position.x = o[1].x; o[0] = float4(v.position, 1); }";
        let error = compile(source).unwrap_err();
        assert!(
            error.contains("wrong number of structure members"),
            "{error}"
        );
        compile(&empty_structs(source)).unwrap();
    }
}
