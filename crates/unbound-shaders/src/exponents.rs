//! `frexp` and `ldexp` as FXC reads them. glslang's HLSL front end crashes on `frexp` whose
//! exponent is an integer variable (the render pipelines' GPU-driven occlusion test:
//! `frexp(radiusInPixels, mipLevel)` with an `int mipLevel`), and gives `ldexp` its float exponent
//! where SPIR-V takes an integer one (the program then fails validation). FXC converts the
//! exponent to the variable's type, and `ldexp(x, e)` is `x * 2^e`; `frexp` splits a float into a
//! mantissa in [0.5, 1) and a power of two.

/// Whether a program calls `frexp`: compiling it as it is could crash glslang (the process with
/// it), so it is compiled rewritten.
pub(crate) fn calls_frexp(source: &str) -> bool {
    calls(source, "frexp").next().is_some()
}

/// The source with each `frexp` call made a call of a helper that takes an integer or a float
/// exponent variable, and each `ldexp(x, e)` written as `((x) * exp2(e))`.
pub(crate) fn exponent_calls(source: &str) -> String {
    let frexps: Vec<usize> = calls(source, "frexp").collect();
    let ldexps: Vec<usize> = calls(source, "ldexp").collect();
    if frexps.is_empty() && ldexps.is_empty() {
        return source.to_string();
    }
    let bytes = source.as_bytes();
    let mut edits: Vec<(usize, usize, String)> = Vec::new();
    for at in frexps {
        edits.push((at, at + "frexp".len(), "unbound_frexp".to_string()));
    }
    for at in ldexps {
        let open = at + "ldexp".len() + skip_spaces(&bytes[at + "ldexp".len()..]);
        let Some((arguments, close)) = arguments(source, open) else {
            continue;
        };
        if let [x, e] = arguments.as_slice() {
            edits.push((
                at,
                close + 1,
                format!("(({}) * exp2({}))", x.trim(), e.trim()),
            ));
        }
    }
    edits.sort_by_key(|e| e.0);
    let mut out = String::with_capacity(source.len() + 2048);
    if source.contains("frexp") {
        out.push_str(&helpers());
    }
    let mut copied = 0;
    for (start, end, text) in edits {
        // A call inside an argument of an `ldexp` already rewritten keeps its text there.
        if start < copied {
            continue;
        }
        out.push_str(&source[copied..start]);
        out.push_str(&text);
        copied = end;
    }
    out.push_str(&source[copied..]);
    out
}

/// Where `name(` is called: the name as an identifier (not a member), followed by `(`.
fn calls<'a>(source: &'a str, name: &'a str) -> impl Iterator<Item = usize> + 'a {
    let bytes = source.as_bytes();
    source
        .match_indices(name)
        .map(|(at, _)| at)
        .filter(move |&at| {
            let end = at + name.len();
            (at == 0 || !(super::is_ident_byte(bytes[at - 1]) || bytes[at - 1] == b'.'))
                && bytes.get(end + skip_spaces(&bytes[end..])) == Some(&b'(')
                && !bytes.get(end).is_some_and(|&b| super::is_ident_byte(b))
        })
}

fn skip_spaces(bytes: &[u8]) -> usize {
    bytes.iter().take_while(|b| b.is_ascii_whitespace()).count()
}

/// The top-level comma-separated arguments of the call whose `(` is at `open`, and where its `)`
/// is.
fn arguments(source: &str, open: usize) -> Option<(Vec<&str>, usize)> {
    let bytes = source.as_bytes();
    if bytes.get(open) != Some(&b'(') {
        return None;
    }
    let mut depth = 0;
    let mut start = open + 1;
    let mut out = Vec::new();
    for (i, &b) in bytes.iter().enumerate().skip(open) {
        match b {
            b'(' | b'[' | b'{' => depth += 1,
            b')' | b']' | b'}' => {
                depth -= 1;
                if depth == 0 {
                    out.push(&source[start..i]);
                    return Some((out, i));
                }
            }
            b',' if depth == 1 => {
                out.push(&source[start..i]);
                start = i + 1;
            }
            _ => {}
        }
    }
    None
}

/// `unbound_frexp` for floats and float vectors, with an exponent of the same shape as a float or
/// an int variable. The mantissa and exponent come from the float's bits (a subnormal scaled by
/// 2^64 first): SPIR-V's frexp returns a structure the renderer's shader translation does not
/// keep (nor WGSL its isinf). Zero, infinities and NaN return themselves with a zero exponent.
fn helpers() -> String {
    let mut out = String::from(
        "float unbound_frexp(float x, out float e) {\n\
         \x20   if (x == 0.0 || ((asuint(x) >> 23) & 0xffu) == 0xffu) { e = 0.0; return x; }\n\
         \x20   float s = x; float bias = 126.0;\n\
         \x20   if (((asuint(s) >> 23) & 0xffu) == 0u) { s *= 18446744073709551616.0; bias = 190.0; }\n\
         \x20   uint b = asuint(s);\n\
         \x20   e = float((b >> 23) & 0xffu) - bias;\n\
         \x20   return asfloat((b & 0x807fffffu) | 0x3f000000u);\n\
         }\n\
         float unbound_frexp(float x, out int e) { float f; float m = unbound_frexp(x, f); e = (int)f; return m; }\n",
    );
    for (n, parts) in [("2", "xy"), ("3", "xyz"), ("4", "xyzw")] {
        for ty in ["float", "int"] {
            out.push_str(&format!(
                "float{n} unbound_frexp(float{n} x, out {ty}{n} e) {{ float{n} m;"
            ));
            for c in parts.chars() {
                out.push_str(&format!(" m.{c} = unbound_frexp(x.{c}, e.{c});"));
            }
            out.push_str(" return m; }\n");
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rewrites_calls_only() {
        let out = exponent_calls(
            "m = frexp(r, level); c = ldexp(p * q, - level); s.frexp(1); myfrexp(2);",
        );
        assert!(out.starts_with(&helpers()));
        assert_eq!(
            &out[helpers().len()..],
            "m = unbound_frexp(r, level); c = ((p * q) * exp2(- level)); s.frexp(1); myfrexp(2);"
        );
        assert_eq!(exponent_calls("float x = 1;"), "float x = 1;");
        assert!(calls_frexp("a = frexp (x, e);"));
        assert!(!calls_frexp("a = frexpx(x, e);"));
    }

    #[test]
    fn nested_arguments_stay_whole() {
        let out = exponent_calls("v = ldexp(f(a, b), g(c, d)[1]);");
        assert_eq!(out, "v = ((f(a, b)) * exp2(g(c, d)[1]));");
    }

    #[test]
    fn the_occlusion_tests_calls_compile() {
        let source = "RWByteAddressBuffer _Out; float _R; float2 _P;\n\
            [numthreads(1,1,1)] void main(uint3 id : SV_DispatchThreadID) {\n\
            int mip = 0; float part = frexp(_R, mip); mip = max(mip + 1, 0);\n\
            float2 c = ldexp(_P, - mip); float e; float m = frexp(_R, e);\n\
            _Out.Store4(0, asuint(float4(part, c, m + e))); }\n";
        let compiler = shaderc::Compiler::new().unwrap();
        let mut options = shaderc::CompileOptions::new().unwrap();
        options.set_source_language(shaderc::SourceLanguage::HLSL);
        let fixed = exponent_calls(source);
        let spv = compiler
            .compile_into_spirv(
                &fixed,
                shaderc::ShaderKind::Compute,
                "t.hlsl",
                "main",
                Some(&options),
            )
            .expect("the rewritten calls compile");
        // The renderer's translation takes it (no frexp result structure is left to register).
        let module = crate::fit::parse(spv.as_binary(), crate::fit::ClipSpace::OpenGl)
            .expect("the program translates");
        let info = naga::valid::Validator::new(
            naga::valid::ValidationFlags::all() - naga::valid::ValidationFlags::BINDINGS,
            naga::valid::Capabilities::all(),
        )
        .validate(&module)
        .expect("the translated program validates");
        naga::back::wgsl::write_string(&module, &info, naga::back::wgsl::WriterFlags::empty())
            .expect("the program writes as WGSL");
    }
}
