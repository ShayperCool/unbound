//! Integer and boolean literals cast to a float matrix (`(float4x4)0`, as the Visual Effect
//! Graph's `VFXInverseTRSMatrix` starts its result) read as float literals.
//!
//! HLSL gives every element of a scalar cast to a matrix the scalar's value. glslang folds such
//! a cast of a constant into a constant matrix without converting the scalar: a float scalar
//! comes out right, but an integer or boolean one is copied as it is and its elements read its
//! bits as a float, partly from storage the integer never set. The result is another constant
//! from one compile to the next (not a number, infinity, or a wrong value): a kernel of a
//! Visual Effect Graph sample failed to compile in some runs ("Float literal is NaN"). Casts of
//! a literal (Unity's shader sources cast `0`) are rewritten, also on the continuation lines of
//! a `#define`; not rewritten: a named integer constant cast to a matrix (none is in Unity's
//! sources), a cast on the first line of a `#define` or one a macro makes of its arguments
//! (`ZERO_INITIALIZE(float4x4, m)` through Unity's `name = (type)0;`), and a cast that
//! directly opens another cast's parenthesised operand.

use crate::scopes::{Kind, edit, text, tokens};

/// Whether `word` names a matrix of floats: `float4x4`, `half3x3`, `real2x2`, ...
fn float_matrix(word: &str) -> bool {
    let Some(dims) = [
        "float",
        "half",
        "min16float",
        "min10float",
        "double",
        "real",
        "fixed",
    ]
    .iter()
    .find_map(|t| word.strip_prefix(t)) else {
        return false;
    };
    let d = dims.as_bytes();
    d.len() == 3 && (b'1'..=b'4').contains(&d[0]) && d[1] == b'x' && (b'1'..=b'4').contains(&d[2])
}

/// The value of an integer or boolean literal (`0`, `12u`, `0x1F`, `true`), none for anything
/// else (a float's integer part is followed by `.`, which the caller checks).
fn integer_literal(word: &str) -> Option<u64> {
    match word {
        "true" => return Some(1),
        "false" => return Some(0),
        _ => {}
    }
    let digits = word.trim_end_matches(['u', 'U', 'l', 'L']);
    if let Some(hex) = digits
        .strip_prefix("0x")
        .or_else(|| digits.strip_prefix("0X"))
    {
        return u64::from_str_radix(hex, 16).ok();
    }
    if digits.is_empty() || !digits.bytes().all(|b| b.is_ascii_digit()) {
        return None;
    }
    digits.parse().ok()
}

/// The source with each integer or boolean literal that is the whole operand of a cast to a
/// float matrix (`(float4x4)0`, `(half3x3)(-1)`, `(float2x2)true`) written as a float literal
/// of its value.
pub(crate) fn matrix_literal_casts(source: &str) -> String {
    let toks = tokens(source);
    let punct = |k: usize, p: &str| {
        toks.get(k)
            .is_some_and(|t| t.kind == Kind::Punct && text(source, t) == p)
    };
    let mut inserts = Vec::new();
    let mut removed = Vec::new();
    let mut k = 0;
    while k + 3 < toks.len() {
        let cast = punct(k, "(")
            && toks[k + 1].kind == Kind::Word
            && float_matrix(text(source, &toks[k + 1]))
            && punct(k + 2, ")");
        if !cast {
            k += 1;
            continue;
        }
        // The operand: parentheses around an optional sign and the literal.
        let mut j = k + 3;
        let mut opened = 0;
        while punct(j, "(") {
            opened += 1;
            j += 1;
        }
        if punct(j, "-") || punct(j, "+") {
            j += 1;
        }
        let value = toks
            .get(j)
            .filter(|t| t.kind == Kind::Word)
            .and_then(|t| integer_literal(text(source, t)));
        let closed = (1..=opened).all(|c| punct(j + c, ")"));
        let float = punct(j + 1, ".");
        if let (Some(value), true, false) = (value, closed, float) {
            removed.push(toks[j].start..toks[j].end);
            inserts.push((toks[j].start, format!("{value}.0")));
        }
        k = j.max(k + 1);
    }
    if removed.is_empty() {
        return source.to_string();
    }
    edit(source, removed, inserts)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn integer_literals_cast_to_float_matrices_become_float_literals() {
        assert_eq!(
            matrix_literal_casts(
                "float4x4 o = (float4x4)0; float3x2 p = (float3x2) 0u; half2x2 q = (half2x2)(-3);\n\
                 float4x4 r = (float4x4)0x10; float2x2 s = (float2x2)true; real3x3 t = (real3x3)((2));"
            ),
            "float4x4 o = (float4x4)0.0; float3x2 p = (float3x2) 0.0; half2x2 q = (half2x2)(-3.0);\n\
             float4x4 r = (float4x4)16.0; float2x2 s = (float2x2)1.0; real3x3 t = (real3x3)((2.0));"
        );
        // Float literals, other operands, vectors, integer matrices and partial operands stay.
        let kept = "float4x4 a = (float4x4)0.0f; float4x4 b = (float4x4)0.5; float3x3 c = (float3x3)UNITY_MATRIX_M;\n\
                    float4 d = (float4)0; int2x2 e = (int2x2)1; float4x4 f = (float4x4)(1 + i); float4x4 g = (float4x4)1e3;\n\
                    #define Z (float4x4)0\n";
        assert_eq!(matrix_literal_casts(kept), kept);
    }

    /// glslang folds an integer constant cast to a float matrix into another constant from one
    /// compile to the next; the rewritten cast folds to the same constant every time, every
    /// element the literal's value.
    #[test]
    fn rewritten_matrix_casts_fold_to_their_value() {
        let compile = |source: &str| -> Vec<u32> {
            let compiler = shaderc::Compiler::new().unwrap();
            let mut o = shaderc::CompileOptions::new().unwrap();
            o.set_source_language(shaderc::SourceLanguage::HLSL);
            o.set_target_env(
                shaderc::TargetEnv::Vulkan,
                shaderc::EnvVersion::Vulkan1_1 as u32,
            );
            compiler
                .compile_into_spirv(source, shaderc::ShaderKind::Compute, "t", "K", Some(&o))
                .unwrap()
                .as_binary()
                .to_vec()
        };
        // The float constants of a module.
        let floats = |words: &[u32]| -> Vec<f32> {
            let (mut i, mut types, mut out) = (5, Vec::new(), Vec::new());
            while i < words.len() {
                let (n, op) = ((words[i] >> 16) as usize, words[i] & 0xffff);
                if op == 22 {
                    types.push(words[i + 1]);
                }
                if op == 43 && n == 4 && types.contains(&words[i + 1]) {
                    out.push(f32::from_bits(words[i + 3]));
                }
                i += n;
            }
            out
        };
        let source = "RWStructuredBuffer<float4> o;\n[numthreads(1, 1, 1)] void K() {\n\
                      float4x4 m = (float4x4)2; m._m00 = o[1].x; o[0] = m[0] + m[3]; }";
        let fixed = matrix_literal_casts(source);
        assert!(fixed.contains("(float4x4)2.0"), "{fixed}");
        let first = compile(&fixed);
        for _ in 0..20 {
            assert_eq!(compile(&fixed), first);
        }
        assert_eq!(floats(&first), [2.0]);
        // Without the rewrite the constant is whatever bits glslang reads, which may happen to
        // be 2 in a given compile: nothing is asserted of it.
    }
}
