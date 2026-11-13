//! Matrix swizzles of several elements (`m._m03_m13_m23`, `m._11_22`) read as calls of helper
//! functions. FXC reads such a swizzle as the vector of those elements; glslang's HLSL front end
//! types it as an `int` (the program then fails validation: "OpStore Pointer's type does not
//! match Object's type"), as in the render pipelines' GPU-driven transform kernels
//! (`InstanceTransformUpdateKernels.compute`: `m._m03_m13_m23`, `m._m00_m10_m20_m01`). Swizzles
//! of one element, and swizzles that are written to, stay as they are (glslang handles those).

/// The element type the helpers are declared for: SPIR-V has matrices of floats only (`half`
/// is `float` on desktop devices).
const ELEMENT_TYPE: &str = "float";

/// The source with each read of a matrix swizzle of two to four elements made a call
/// `unbound_mswz<n>(matrix, row0, column0, ...)`, and the helpers declared first when any is.
pub(crate) fn matrix_swizzles(source: &str) -> String {
    let bytes = source.as_bytes();
    let mut edits: Vec<(usize, usize, String)> = Vec::new();
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i] != b'.' {
            i += 1;
            continue;
        }
        let token_start = i + 1;
        let mut token_end = token_start;
        while token_end < bytes.len() && super::is_ident_byte(bytes[token_end]) {
            token_end += 1;
        }
        let Some(elements) = elements(&source[token_start..token_end]) else {
            i = token_end.max(i + 1);
            continue;
        };
        if elements.len() < 2 || written(&source[token_end..]) {
            i = token_end;
            continue;
        }
        let Some(start) = expression_start(bytes, i) else {
            i = token_end;
            continue;
        };
        let mut call = format!("unbound_mswz{}({}", elements.len(), &source[start..i]);
        for (row, column) in &elements {
            call.push_str(&format!(", {row}, {column}"));
        }
        call.push(')');
        edits.push((start, token_end, call));
        i = token_end;
    }
    if edits.is_empty() {
        return source.to_string();
    }
    // Nested swizzles keep the outermost edit (an edit inside an earlier one's range is left to
    // the second compilation of the rewritten text, which never needs it in practice).
    let mut out = String::with_capacity(source.len() + edits.len() * 32 + 8192);
    out.push_str(&helpers());
    let mut copied = 0;
    for (start, end, call) in edits {
        if start < copied {
            continue;
        }
        out.push_str(&source[copied..start]);
        out.push_str(&call);
        copied = end;
    }
    out.push_str(&source[copied..]);
    out
}

/// The elements of a matrix swizzle token (`_m03_m13`: zero-based, `_14_24`: one-based) as
/// zero-based (row, column) pairs; None for any other token.
fn elements(token: &str) -> Option<Vec<(u8, u8)>> {
    let bytes = token.as_bytes();
    let mut out = Vec::new();
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i] != b'_' {
            return None;
        }
        if bytes.get(i + 1) == Some(&b'm') {
            let (r, c) = (*bytes.get(i + 2)?, *bytes.get(i + 3)?);
            if !(b'0'..=b'3').contains(&r) || !(b'0'..=b'3').contains(&c) {
                return None;
            }
            out.push((r - b'0', c - b'0'));
            i += 4;
        } else {
            let (r, c) = (*bytes.get(i + 1)?, *bytes.get(i + 2)?);
            if !(b'1'..=b'4').contains(&r) || !(b'1'..=b'4').contains(&c) {
                return None;
            }
            out.push((r - b'1', c - b'1'));
            i += 3;
        }
    }
    (!out.is_empty() && out.len() <= 4).then_some(out)
}

/// Whether the text after a swizzle assigns to it (`=`, `+=`, ..., `++`, `--`).
fn written(after: &str) -> bool {
    let after = after.trim_start();
    (after.starts_with('=') && !after.starts_with("=="))
        || [
            "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<=", ">>=", "++", "--",
        ]
        .iter()
        .any(|op| after.starts_with(op))
}

/// Where the expression ending just before `dot` starts: identifiers joined by `.`, with
/// indexing (`a[i]`), calls (`f(x)`) and parenthesized groups; None when no expression ends there.
fn expression_start(bytes: &[u8], dot: usize) -> Option<usize> {
    let mut at = dot;
    let mut any = false;
    loop {
        // One postfix-free part: an identifier, a call, a group or an indexed name.
        loop {
            match at.checked_sub(1).map(|p| bytes[p]) {
                Some(b')') | Some(b']') => {
                    at = matching_open(bytes, at - 1)?;
                    any = true;
                }
                Some(b) if super::is_ident_byte(b) => {
                    while at > 0 && super::is_ident_byte(bytes[at - 1]) {
                        at -= 1;
                    }
                    any = true;
                    // An index or a call's name ends here; an indexed part may follow before it.
                    if at > 0 && matches!(bytes[at - 1], b')' | b']') {
                        continue;
                    }
                    break;
                }
                _ => break,
            }
        }
        if !any {
            return None;
        }
        // A member access before it continues the expression (`a.b`), not a number (`1.0`).
        if at >= 2
            && bytes[at - 1] == b'.'
            && (super::is_ident_byte(bytes[at - 2]) || matches!(bytes[at - 2], b')' | b']'))
        {
            if bytes[at..dot].first().is_some_and(u8::is_ascii_digit) {
                return None;
            }
            at -= 1;
            continue;
        }
        if bytes[at..dot].first().is_some_and(u8::is_ascii_digit) {
            return None;
        }
        return Some(at);
    }
}

/// The opening bracket matching the closing one at `close`.
fn matching_open(bytes: &[u8], close: usize) -> Option<usize> {
    let mut depth = 0i32;
    let mut at = close;
    loop {
        match bytes[at] {
            b')' | b']' => depth += 1,
            b'(' | b'[' => {
                depth -= 1;
                if depth == 0 {
                    return Some(at);
                }
            }
            _ => {}
        }
        at = at.checked_sub(1)?;
    }
}

/// The helpers: for each matrix shape (2 to 4 rows and columns) and element count (2 to 4), the
/// vector of the elements at the given rows and columns.
fn helpers() -> String {
    let mut out = String::new();
    let ty = ELEMENT_TYPE;
    {
        for rows in 2..=4 {
            for columns in 2..=4 {
                for n in 2..=4 {
                    out.push_str(&format!("{ty}{n} unbound_mswz{n}({ty}{rows}x{columns} m"));
                    for k in 0..n {
                        out.push_str(&format!(", int r{k}, int c{k}"));
                    }
                    out.push_str(&format!(") {{ return {ty}{n}("));
                    for k in 0..n {
                        if k > 0 {
                            out.push_str(", ");
                        }
                        out.push_str(&format!("m[r{k}][c{k}]"));
                    }
                    out.push_str("); }\n");
                }
            }
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    fn body(text: &str) -> &str {
        &text[helpers().len()..]
    }

    #[test]
    fn reads_become_helper_calls() {
        let out = matrix_swizzles("float3 t = m._m03_m13_m23; float2 u = g(a)[1]._11_22;");
        assert_eq!(
            body(&out),
            "float3 t = unbound_mswz3(m, 0, 3, 1, 3, 2, 3); float2 u = unbound_mswz2(g(a)[1], 0, 0, 1, 1);"
        );
    }

    #[test]
    fn member_chains_and_groups_are_kept_whole() {
        let out = matrix_swizzles("x = (a + b)._m00_m11 + s.m._m01_m10;");
        assert_eq!(
            body(&out),
            "x = unbound_mswz2((a + b), 0, 0, 1, 1) + unbound_mswz2(s.m, 0, 1, 1, 0);"
        );
    }

    #[test]
    fn single_elements_writes_and_others_stay() {
        for text in [
            "a = m._m03;",
            "m._m00_m11 = v;",
            "m._11_22 += v;",
            "v.xyz = w.xyzw;",
            "f = 1.0._m00_m11;",
            "o._m4x_m00",
        ] {
            assert_eq!(matrix_swizzles(text), text, "{text}");
        }
    }

    #[test]
    fn the_kernels_swizzles_compile() {
        let source = "RWByteAddressBuffer _Out; float4x4 _M;\n\
            float4 Pack(float4x4 m) { return m._m00_m10_m20_m01; }\n\
            [numthreads(1,1,1)] void main(uint3 id : SV_DispatchThreadID) {\n\
            float4x4 m = _M; float3 t = m._m03_m13_m23; float2x2 k = (float2x2)m;\n\
            _Out.Store4(0, asuint(Pack(m) + float4(t, 0) + float4(k._11_22, 0, 0))); }\n";
        let compiler = shaderc::Compiler::new().unwrap();
        let mut options = shaderc::CompileOptions::new().unwrap();
        options.set_source_language(shaderc::SourceLanguage::HLSL);
        options.set_optimization_level(shaderc::OptimizationLevel::Performance);
        assert!(
            compiler
                .compile_into_spirv(
                    source,
                    shaderc::ShaderKind::Compute,
                    "t.hlsl",
                    "main",
                    Some(&options)
                )
                .is_err()
        );
        let fixed = matrix_swizzles(source);
        compiler
            .compile_into_spirv(
                &fixed,
                shaderc::ShaderKind::Compute,
                "t.hlsl",
                "main",
                Some(&options),
            )
            .expect("the rewritten swizzles compile");
    }
}
