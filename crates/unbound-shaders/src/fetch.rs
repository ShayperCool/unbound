//! Framebuffer fetch on the Metal device. The macOS Editor compiles every program there with
//! `UNITY_FRAMEBUFFER_FETCH_AVAILABLE` and `UNITY_FRAMEBUFFER_FETCH_ENABLED` (tests/projects/
//! shadertargets `fetch.Flags`), so HLSLSupport.cginc names color targets `CoLoR<n>` instead of
//! `SV_Target<n>` and vertex colors `VCOLOR<n>` instead of `COLOR<n>` (the same semantics to
//! Unity's compiler), and a fragment program may take its documented fetch path: an `inout` color
//! target parameter that starts as the target's color under the fragment (`fetch.*`: a draw reads
//! what the draw before it left).
//!
//! The renderer has no framebuffer reads: an `inout` color target becomes an output that starts
//! as the texel at the fragment's position of [`TEXTURE`], which the renderer fills with a copy of
//! the target before each draw of such a program. Triangles of one draw that overlap read the
//! color from before the draw (Metal's read what the earlier ones wrote).

use anyhow::{Result, bail};

/// The texture an `inout` color target reads (the copy of color target 0 before the draw).
pub const TEXTURE: &str = "_UnboundFramebufferFetch0";

/// The color target semantic HLSLSupport.cginc names `SV_Target` as with fetch enabled.
const TARGET: &str = "CoLoR";

/// The vertex color semantic HLSLSupport.cginc names `COLOR` as with fetch enabled.
const VERTEX_COLOR: &str = "VCOLOR";

/// A program preprocessed for a device with framebuffer fetch (`enabled`; other devices' are
/// kept as they are): its semantics named `SV_Target<n>` and `COLOR<n>` again and, in a fragment
/// program, its entry's `inout` color target reading [`TEXTURE`]; whether it reads it.
pub fn rewrite(source: &str, entry: &str, enabled: bool, fragment: bool) -> Result<(String, bool)> {
    if !enabled {
        return Ok((source.to_string(), false));
    }
    let mut text = source.to_string();
    let mut reads = false;
    if fragment && let Some(function) = find_definition(&text, entry) {
        let params = split_params(&text[function.open + 1..function.close]);
        let mut fetched = Vec::new();
        let mut position = None;
        for param in &params {
            let (declaration, semantic) = match param.rsplit_once(':') {
                Some((d, s)) => (d.trim(), s.trim()),
                None => (param.trim(), ""),
            };
            let words: Vec<&str> = declaration.split_whitespace().collect();
            if is_position(semantic) {
                position = words.last().map(|w| w.to_string());
            }
            // A structure of color targets taken inout (unmeasured): refused.
            if semantic.is_empty()
                && words.first() == Some(&"inout")
                && let [_, ty, _] = words.as_slice()
                && struct_members(&text, ty)
                    .is_some_and(|members| members.iter().any(|(_, s)| target_index(s).is_some()))
            {
                bail!(
                    "framebuffer fetch through a structure of color targets (`{param}`): Unbound reads an inout color target parameter only"
                );
            }
            let index = target_index(semantic);
            if words.first() == Some(&"inout")
                && let Some(index) = index
            {
                if !(index.is_empty() || index == "0") {
                    bail!(
                        "framebuffer fetch of color target {index} (`inout ... : SV_Target{index}`): Unbound reads color target 0 only"
                    );
                }
                let [_, ty @ .., name] = words.as_slice() else {
                    bail!("framebuffer fetch: cannot read the parameter `{param}`");
                };
                fetched.push((param.clone(), ty.join(" "), name.to_string()));
            }
        }
        if position.is_none() {
            position = struct_position(&text, &params);
        }
        if !fetched.is_empty() {
            let mut list: Vec<String> = params
                .iter()
                .map(|p| match fetched.iter().find(|(f, _, _)| f == p) {
                    Some((_, ty, name)) => format!("out {ty} {name} : {TARGET}"),
                    None => p.clone(),
                })
                .collect();
            let position = match position {
                Some(position) => position,
                None => {
                    list.push("float4 _unbound_FragCoord : SV_Position".into());
                    "_unbound_FragCoord".into()
                }
            };
            let loads: String = fetched
                .iter()
                .map(|(_, ty, name)| {
                    format!("\n{name} = ({ty}){TEXTURE}.Load(int3(int2(({position}).xy), 0));")
                })
                .collect();
            let mut rewritten = String::with_capacity(text.len() + 256);
            rewritten.push_str(&text[..function.line]);
            rewritten.push_str(&format!("Texture2D<float4> {TEXTURE};\n"));
            rewritten.push_str(&text[function.line..function.open + 1]);
            rewritten.push_str(&list.join(", "));
            rewritten.push_str(&text[function.close..=function.body]);
            rewritten.push_str(&loads);
            rewritten.push_str(&text[function.body + 1..]);
            text = rewritten;
            reads = true;
        }
    }
    Ok((rename_semantics(&text), reads))
}

/// Whether a fragment input semantic is the fragment's position (`SV_Position`, `VPOS`, or a
/// DX9 `POSITION`).
fn is_position(semantic: &str) -> bool {
    ["SV_POSITION", "VPOS", "POSITION", "POSITION0"]
        .iter()
        .any(|s| semantic.eq_ignore_ascii_case(s))
}

/// The color target a semantic names (`CoLoR<n>`, or `SV_Target<n>` in any case), by its digits.
fn target_index(semantic: &str) -> Option<&str> {
    let digits = if let Some(rest) = semantic.strip_prefix(TARGET) {
        rest
    } else if semantic.len() >= 9 && semantic[..9].eq_ignore_ascii_case("SV_Target") {
        &semantic[9..]
    } else {
        return None;
    };
    digits.bytes().all(|c| c.is_ascii_digit()).then_some(digits)
}

/// Where a function's definition lies: the start of its line, its parameter list's parentheses
/// and the opening brace of its body.
struct Definition {
    line: usize,
    open: usize,
    close: usize,
    body: usize,
}

/// The definition of the function `name` (a call or a declaration has no body after it).
fn find_definition(text: &str, name: &str) -> Option<Definition> {
    let bytes = text.as_bytes();
    let ident = |c: u8| c.is_ascii_alphanumeric() || c == b'_';
    for (at, _) in text.match_indices(name) {
        let end = at + name.len();
        if (at > 0 && ident(bytes[at - 1])) || bytes.get(end).is_some_and(|&c| ident(c)) {
            continue;
        }
        let open = end + text[end..].len() - text[end..].trim_start().len();
        if bytes.get(open) != Some(&b'(') {
            continue;
        }
        let mut depth = 0usize;
        let mut close = None;
        for (i, &c) in bytes.iter().enumerate().skip(open) {
            match c {
                b'(' => depth += 1,
                b')' => {
                    depth -= 1;
                    if depth == 0 {
                        close = Some(i);
                        break;
                    }
                }
                _ => {}
            }
        }
        let close = close?;
        // A return semantic may follow (`: SV_Target`), then the body.
        let after = &text[close + 1..];
        let skipped = after.len() - after.trim_start().len();
        let rest = &after[skipped..];
        let body = if rest.starts_with('{') {
            close + 1 + skipped
        } else if let Some(semantic) = rest.strip_prefix(':') {
            let brace = semantic.find(['{', ';'])?;
            if semantic.as_bytes()[brace] != b'{' {
                continue;
            }
            close + 1 + skipped + 1 + brace
        } else {
            continue;
        };
        let line = text[..at].rfind('\n').map_or(0, |i| i + 1);
        return Some(Definition {
            line,
            open,
            close,
            body,
        });
    }
    None
}

/// A parameter list's parameters, split at its top-level commas.
fn split_params(list: &str) -> Vec<String> {
    let mut params = Vec::new();
    let (mut depth, mut start) = (0i32, 0);
    for (i, c) in list.char_indices() {
        match c {
            '(' | '<' | '[' => depth += 1,
            ')' | '>' | ']' => depth -= 1,
            ',' if depth == 0 => {
                params.push(list[start..i].trim().to_string());
                start = i + 1;
            }
            _ => {}
        }
    }
    let last = list[start..].trim();
    if !last.is_empty() {
        params.push(last.to_string());
    }
    params
}

/// The fragment position held by a member of a structure parameter (`i.pos` for
/// `v2f i` whose `pos` is `SV_POSITION`).
fn struct_position(text: &str, params: &[String]) -> Option<String> {
    for param in params {
        let declaration = param.split(':').next()?.trim();
        let words: Vec<&str> = declaration
            .split_whitespace()
            .filter(|w| !matches!(*w, "in" | "const" | "uniform"))
            .collect();
        let [ty, name] = words.as_slice() else {
            continue;
        };
        let Some(members) = struct_members(text, ty) else {
            continue;
        };
        if let Some((member, _)) = members.iter().find(|(_, s)| is_position(s)) {
            return Some(format!("{name}.{member}"));
        }
    }
    None
}

/// The members of the structure `ty` with their semantics (empty: none).
fn struct_members(text: &str, ty: &str) -> Option<Vec<(String, String)>> {
    let pattern = format!("struct {ty}");
    let start = text.match_indices(&pattern).map(|(i, _)| i).find(|&i| {
        !text[i + pattern.len()..].starts_with(|c: char| c.is_ascii_alphanumeric() || c == '_')
    })?;
    let body = &text[start..];
    let (open, close) = (body.find('{')?, body.find('}')?);
    Some(
        body[open + 1..close]
            .split(';')
            .filter_map(|member| {
                let (member, semantic) = member.rsplit_once(':').unwrap_or((member, ""));
                let name = member.split_whitespace().last()?;
                Some((name.to_string(), semantic.trim().to_string()))
            })
            .collect(),
    )
}

/// `CoLoR<n>` named `SV_Target<n>` again (the case-sensitive spelling comes from HLSLSupport.cginc's
/// macro alone), and `VCOLOR<n>` semantics `COLOR<n>` again.
fn rename_semantics(text: &str) -> String {
    let text = rename(text, TARGET, "SV_Target", false);
    rename(&text, VERTEX_COLOR, "COLOR", true)
}

/// `from<n>` tokens (`semantic`: only after a `:`) named `to<n>`.
fn rename(text: &str, from: &str, to: &str, semantic: bool) -> String {
    let bytes = text.as_bytes();
    let mut out = String::with_capacity(text.len());
    let mut last = 0;
    for (at, _) in text.match_indices(from) {
        let before = at.checked_sub(1).map(|i| bytes[i]);
        if before.is_some_and(|c| c.is_ascii_alphanumeric() || c == b'_') {
            continue;
        }
        if semantic && !text[..at].trim_end().ends_with(':') {
            continue;
        }
        let digits = text[at + from.len()..]
            .bytes()
            .take_while(u8::is_ascii_digit)
            .count();
        let end = at + from.len() + digits;
        if bytes
            .get(end)
            .is_some_and(|&c| c.is_ascii_alphabetic() || c == b'_')
        {
            continue;
        }
        out.push_str(&text[last..at]);
        out.push_str(to);
        out.push_str(&text[at + from.len()..end]);
        last = end;
    }
    out.push_str(&text[last..]);
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn an_inout_target_reads_the_copy_at_the_fragment_position() {
        let source = "float4 vert(float4 v : POSITION) : SV_POSITION { return v; }\n\
                      void frag(float4 pos : SV_POSITION, inout half4 ocol : CoLoR)\n\
                      {\n    ocol = ocol * 0.5;\n}\n";
        let (text, reads) = rewrite(source, "frag", true, true).unwrap();
        assert!(reads);
        assert!(text.contains("Texture2D<float4> _UnboundFramebufferFetch0;\nvoid frag("));
        assert!(text.contains("out half4 ocol : SV_Target)"), "{text}");
        assert!(
            text.contains(
                "{\nocol = (half4)_UnboundFramebufferFetch0.Load(int3(int2((pos).xy), 0));"
            )
        );
        assert!(!text.contains("CoLoR"));
    }

    #[test]
    fn the_position_comes_from_a_structure_or_a_parameter_of_its_own() {
        let source = "struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };\n\
                      void frag(v2f i, inout float4 c : CoLoR0) { c += 1; }\n";
        let (text, _) = rewrite(source, "frag", true, true).unwrap();
        assert!(
            text.contains("Load(int3(int2((i.vertex).xy), 0))"),
            "{text}"
        );
        let source = "struct v2f { float2 uv : TEXCOORD0; };\n\
                      void frag(v2f i, inout float4 c : CoLoR) { c += 1; }\n";
        let (text, _) = rewrite(source, "frag", true, true).unwrap();
        assert!(
            text.contains("out float4 c : SV_Target, float4 _unbound_FragCoord : SV_Position)"),
            "{text}"
        );
    }

    #[test]
    fn programs_without_an_inout_target_keep_their_targets() {
        // The prototype is not the definition; a structure's targets are renamed back.
        let source = "struct o { half4 a : CoLoR0; half4 b : CoLoR1; };\n\
                      o frag(float4 p : SV_POSITION);\n\
                      o frag(float4 p : SV_POSITION) { o r; r.a = 1; r.b = 0; return r; }\n\
                      half4 COLORS; half4 CoLoRful;\n";
        let (text, reads) = rewrite(source, "frag", true, true).unwrap();
        assert!(!reads);
        assert!(text.contains("half4 a : SV_Target0; half4 b : SV_Target1;"));
        assert!(text.contains("half4 COLORS; half4 CoLoRful;"));
        let (vertex, _) = rewrite("float4 v : CoLoR;", "vert", true, false).unwrap();
        assert_eq!(vertex, "float4 v : SV_Target;");
    }

    #[test]
    fn vertex_colors_are_colors_again_and_other_devices_keep_their_text() {
        let source = "struct a { float4 c : VCOLOR; float4 d : VCOLOR1; };\n\
                      half4 frag(float4 c : VCOLOR0) : CoLoR { half4 VCOLOR = c; return VCOLOR; }\n";
        let (text, reads) = rewrite(source, "frag", true, true).unwrap();
        assert!(!reads);
        assert_eq!(
            text,
            "struct a { float4 c : COLOR; float4 d : COLOR1; };\n\
             half4 frag(float4 c : COLOR0) : SV_Target { half4 VCOLOR = c; return VCOLOR; }\n"
        );
        assert_eq!(
            rewrite(source, "frag", false, true).unwrap(),
            (source.into(), false)
        );
    }

    #[test]
    fn a_dx9_position_is_the_fragment_position() {
        let source = "struct v2f { float4 pos : POSITION; };\n\
                      void frag(v2f i, inout half4 c : CoLoR) { c *= 2; }\n";
        let (text, _) = rewrite(source, "frag", true, true).unwrap();
        assert!(text.contains("Load(int3(int2((i.pos).xy), 0))"), "{text}");
        assert!(!text.contains("_unbound_FragCoord"));
    }

    #[test]
    fn a_structure_of_targets_taken_inout_is_refused() {
        let source = "struct o { half4 a : CoLoR0; half4 b : CoLoR1; };\n\
                      void frag(float4 p : SV_POSITION, inout o r) { }\n";
        assert!(rewrite(source, "frag", true, true).is_err());
    }

    #[test]
    fn a_further_target_is_refused() {
        let source = "void frag(float4 p : SV_POSITION, inout half4 c : CoLoR1) { }\n";
        assert!(rewrite(source, "frag", true, true).is_err());
    }
}
