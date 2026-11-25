//! Structs declared without members (`struct GraphProperties {};`, what Shader Graph declares
//! for a graph without properties). FXC takes them, and a zero cast of a struct holding one
//! (`(MotionVectorPassOutput)0`, the render pipeline's motion vector pass of a Visual Effect
//! Graph output) zeroes the other members; glslang rejects such a cast ("wrong number of
//! structure members"), so the pass did not compile. In a program compiled through the
//! rewrites (one that does not compile as it is, or one sent there at once: `fxc_compatible`),
//! each such struct gets one member of its own, which nothing reads. Not handled: an empty
//! struct inside a cbuffer or as a buffer's element (the filler moves the members after it,
//! which a writer of the buffer's bytes would not expect), or as a stage's input or output
//! (such a program did not compile before and still does not).

/// The member given to a struct declared without one.
const FILLER: &str = "uint unbound_empty_struct_filler;";

/// `source` with every struct declared without members given one ([`FILLER`]).
pub(crate) fn empty_structs(source: &str) -> String {
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
        if name_end == name_start {
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
