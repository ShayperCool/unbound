//! Scalar and vector `inout` parameters split into an `in` copy and an `out` one.
//!
//! FXC passes an argument to an `inout` parameter of another scalar type by converting it on the
//! way in and back on the way out (a `float` to an `inout int`, an `int3` to an `inout float3`,
//! a number to an `inout bool`). glslang compiles an `in` and an `out` parameter given such an
//! argument, with the same conversions, but not an `inout` one: it writes a program the SPIR-V
//! optimizer rejects ("not a logical pointer") for a conversion between numbers or to or from
//! vectors of bool, and crashes (the process with it) on a number given to an `inout bool`, also
//! when the function is overloaded with an `inout` number at that place. A Visual Effect Graph
//! output drawn by a Shader Graph keeps its particles' `alive` as a `float` and passes it to its
//! blocks' `inout bool alive`. So each `inout` parameter of a scalar or vector type (`bool`,
//! `int`, `uint`, `dword`, `half`, `float`, `double` and the minimum precision types, alone or
//! with 1 to 4 components) of a function declared outside any braces becomes
//! `<type> <name>_unbound_in, out <type> <name>`, its body starting with
//! `<name> = <name>_unbound_in;`, and each call of the function passes the argument to both. An
//! argument is an l-value: its expression is written twice, so one with a side effect, such as
//! `a[i++]`, has it twice, and the copy is the argument's text as written (a call of a split
//! function inside it, such as `b[F(c)]`, is not rewritten in the copy). Halves and floats are
//! one type to glslang (`half` is 32-bit here), and a change of the number of components is no
//! conversion glslang or FXC makes for an `inout` argument: neither needs the rewrite.
//!
//! Only programs glslang rejects as written are rewritten, but for those with a scalar
//! `inout bool` the rewrite splits ([`splits`]), which compile rewritten at once: glslang may
//! crash on them.
//!
//! Left as they are: functions of a name another declaration of which has no such parameter at
//! the same place (a call cannot be told apart without the arguments' types), names also
//! declared as a struct's member, functions whose parameters hold a template type
//! (`vector<float, 3>`, whose commas this reading does not follow), parameters with a semantic
//! (an entry point's, whose input and output it names together) and those of matrix, struct,
//! array or object types.

use crate::scopes::{Kind, Token, edit, matching, text, tokens};
use std::collections::{BTreeMap, BTreeSet, HashSet};

/// Words before a name that make its parenthesis a call, not a declaration.
const NOT_TYPES: &[&str] = &[
    "return", "else", "do", "case", "in", "out", "inout", "uniform",
];

/// The suffix of the `in` copy of a split parameter.
const IN_SUFFIX: &str = "_unbound_in";

/// The scalar types whose parameters, alone or as vectors, are split.
const SCALARS: &[&str] = &[
    "bool",
    "int",
    "uint",
    "dword",
    "half",
    "float",
    "double",
    "min16float",
    "min10float",
    "min16int",
    "min12int",
    "min16uint",
];

/// Whether a type word is a scalar of [`SCALARS`] or a vector of 1 to 4 of one.
fn splittable(ty: &str) -> bool {
    let base = ty
        .strip_suffix(['1', '2', '3', '4'])
        .filter(|base| SCALARS.contains(base));
    SCALARS.contains(&ty) || base.is_some()
}

/// A split parameter: its type's and its name's tokens.
#[derive(Clone, Copy)]
struct Parameter {
    ty: usize,
    name: usize,
}

/// The `inout <type> <name>` parameter starting at token `k` of a type [`splittable`] and
/// followed by the end of the parameter (`,` or `)`; not a semantic).
fn inout_at(source: &str, toks: &[Token], k: usize) -> Option<Parameter> {
    let word = |j: usize| {
        toks.get(j)
            .filter(|t| t.kind == Kind::Word)
            .map(|t| text(source, t))
    };
    let punct = |j: usize| {
        toks.get(j)
            .filter(|t| t.kind == Kind::Punct)
            .map(|t| text(source, t))
    };
    if word(k) != Some("inout") || !word(k + 1).is_some_and(splittable) || word(k + 2).is_none() {
        return None;
    }
    matches!(punct(k + 3), Some("," | ")")).then_some(Parameter {
        ty: k + 1,
        name: k + 2,
    })
}

/// The top-level comma-separated parts of the bracket opened at token `open` and closed at
/// `close`: each part's first and last token (exclusive end).
fn parts(source: &str, toks: &[Token], open: usize, close: usize) -> Vec<(usize, usize)> {
    let mut out = Vec::new();
    let (mut depth, mut start) = (0i32, open + 1);
    for k in open + 1..close {
        if toks[k].kind != Kind::Punct {
            continue;
        }
        match text(source, &toks[k]) {
            "(" | "[" | "{" => depth += 1,
            ")" | "]" | "}" => depth -= 1,
            "," if depth == 0 => {
                out.push((start, k));
                start = k + 1;
            }
            _ => {}
        }
    }
    if start < close {
        out.push((start, close));
    }
    out
}

/// A function declaration or definition: its parameters' bracket, and the brace of its body.
struct Declaration {
    open: usize,
    close: usize,
    body: Option<usize>,
}

/// The program read for the rewrite: its tokens, the declarations outside any braces by name,
/// the places of the split parameters of each name split, and whether one of them is a scalar
/// `bool` (on which glslang may crash).
struct Reading<'a> {
    toks: Vec<Token>,
    declarations: BTreeMap<&'a str, Vec<Declaration>>,
    split: BTreeMap<&'a str, Vec<usize>>,
    splits_bool: bool,
}

fn read(source: &str) -> Reading<'_> {
    let toks = tokens(source);
    let word = |j: usize| {
        toks.get(j)
            .filter(|t| t.kind == Kind::Word)
            .map(|t| text(source, t))
    };
    let punct = |j: usize| {
        toks.get(j)
            .filter(|t| t.kind == Kind::Punct)
            .map(|t| text(source, t))
    };
    // Declarations: a type word, the name, a parenthesised list followed by a body, a `;` or a
    // semantic. Those outside any braces by name; the names of members (inside a struct's
    // braces, or defined as `Struct::name`) apart.
    let mut declarations: BTreeMap<&str, Vec<Declaration>> = BTreeMap::new();
    let mut members = BTreeSet::new();
    for k in 1..toks.len() {
        let Some(name) = word(k) else {
            continue;
        };
        if punct(k + 1) != Some("(") {
            continue;
        }
        let qualified = punct(k - 1) == Some("::");
        if !qualified && word(k - 1).is_none_or(|before| NOT_TYPES.contains(&before)) {
            continue;
        }
        let Some(close) = matching(source, &toks, k + 1) else {
            continue;
        };
        let after = punct(close + 1);
        if !matches!(after, Some("{" | ";" | ":")) {
            continue;
        }
        if qualified || toks[k].depth != 0 {
            members.insert(name);
            continue;
        }
        let body = if after == Some("{") {
            Some(close + 1)
        } else if after == Some(":") && punct(close + 3) == Some("{") {
            Some(close + 3)
        } else {
            None
        };
        declarations.entry(name).or_default().push(Declaration {
            open: k + 1,
            close,
            body,
        });
    }
    // The split parameters of each name: the places of its `inout` parameters of a scalar or
    // vector type, the same in every declaration of the name, none of which holds a template
    // type.
    let mut split = BTreeMap::new();
    let mut splits_bool = false;
    for (&name, list) in &declarations {
        if members.contains(name) {
            continue;
        }
        let templated = list.iter().any(|d| {
            (d.open + 1..d.close)
                .any(|j| toks[j].kind == Kind::Punct && text(source, &toks[j]) == "<")
        });
        if templated {
            continue;
        }
        let places = |d: &Declaration| -> Vec<usize> {
            parts(source, &toks, d.open, d.close)
                .iter()
                .enumerate()
                .filter(|(_, (first, _))| inout_at(source, &toks, *first).is_some())
                .map(|(i, _)| i)
                .collect()
        };
        let first = places(&list[0]);
        if !first.is_empty() && list.iter().all(|d| places(d) == first) {
            splits_bool |= list.iter().any(|d| {
                parts(source, &toks, d.open, d.close)
                    .iter()
                    .any(|(first, _)| {
                        inout_at(source, &toks, *first)
                            .is_some_and(|p| text(source, &toks[p.ty]) == "bool")
                    })
            });
            split.insert(name, first);
        }
    }
    Reading {
        toks,
        declarations,
        split,
        splits_bool,
    }
}

/// Whether the rewrite splits a scalar `inout bool` parameter of the program, on which glslang
/// may crash: the program then compiles rewritten at once.
pub(crate) fn splits(source: &str) -> bool {
    source.contains("inout") && read(source).splits_bool
}

/// The program with each scalar and vector `inout` parameter split and its calls passing the
/// argument twice (see the module's description); unchanged without such a parameter.
pub(crate) fn inout_parameters(source: &str) -> String {
    if !source.contains("inout") {
        return source.to_string();
    }
    let Reading {
        toks,
        declarations,
        split,
        ..
    } = read(source);
    if split.is_empty() {
        return source.to_string();
    }
    let word = |j: usize| {
        toks.get(j)
            .filter(|t| t.kind == Kind::Word)
            .map(|t| text(source, t))
    };
    let punct = |j: usize| {
        toks.get(j)
            .filter(|t| t.kind == Kind::Punct)
            .map(|t| text(source, t))
    };
    let mut removed = Vec::new();
    let mut inserts = Vec::new();
    let mut declared = HashSet::new();
    for name in split.keys() {
        for d in &declarations[name] {
            declared.insert(d.open);
            let mut assignments = String::new();
            for (first, _) in parts(source, &toks, d.open, d.close) {
                let Some(parameter) = inout_at(source, &toks, first) else {
                    continue;
                };
                let ty = text(source, &toks[parameter.ty]);
                let id = text(source, &toks[parameter.name]);
                // `inout bool alive` -> `bool alive_unbound_in, out bool alive`.
                removed.push(toks[first].start..toks[parameter.ty].end);
                inserts.push((toks[first].start, format!("{ty} {id}{IN_SUFFIX}, out {ty}")));
                assignments.push_str(&format!(" {id} = {id}{IN_SUFFIX};"));
            }
            if let Some(body) = d.body {
                inserts.push((toks[body].end, assignments));
            }
        }
    }
    // Calls: the name, not a member's (after `.` or `::`), then a parenthesised list that is no
    // declaration's.
    for k in 0..toks.len() {
        let Some(name) = word(k) else {
            continue;
        };
        let Some(places) = split.get(name) else {
            continue;
        };
        if punct(k + 1) != Some("(")
            || declared.contains(&(k + 1))
            || (k > 0 && matches!(punct(k - 1), Some("." | "::")))
        {
            continue;
        }
        let Some(close) = matching(source, &toks, k + 1) else {
            continue;
        };
        let arguments = parts(source, &toks, k + 1, close);
        for &place in places {
            let Some(&(first, end)) = arguments.get(place) else {
                continue;
            };
            let argument = &source[toks[first].start..toks[end - 1].end];
            inserts.push((toks[end - 1].end, format!(", {argument}")));
        }
    }
    edit(source, removed, inserts)
}

#[cfg(test)]
mod tests {
    use super::*;

    const ALIVE: &str = "struct S { float alive; uint n; };\n\
        void SetAlive(inout bool alive, bool value) { alive = value && alive; }\n";

    #[test]
    fn inout_bool_parameters_are_split_and_their_arguments_passed_twice() {
        let source = format!(
            "{ALIVE}void F(inout S s) {{ SetAlive(s.alive, true); SetAlive( s.alive , s.n != 0 ); }}\n"
        );
        assert!(splits(&source));
        assert_eq!(
            inout_parameters(&source),
            "struct S { float alive; uint n; };\n\
             void SetAlive(bool alive_unbound_in, out bool alive, bool value) { alive = alive_unbound_in; alive = value && alive; }\n\
             void F(inout S s) { SetAlive(s.alive, s.alive, true); SetAlive( s.alive, s.alive , s.n != 0 ); }\n"
        );
        // A prototype and a definition with a semantic; calls after `return` and `else`, nested
        // in another call's arguments; two parameters given one l-value.
        let shapes = "bool Both(inout bool a, int k, inout bool b);\n\
                      bool Both(inout bool a, int k, inout bool b) : SV_Target { a = !a; b = a; return a; }\n\
                      float G(float x, float y) { if (x > 0) return Both(x, 1, y); else Both(x, 2, x); return max(Both(x, 3, y), y); }\n";
        assert_eq!(
            inout_parameters(shapes),
            "bool Both(bool a_unbound_in, out bool a, int k, bool b_unbound_in, out bool b);\n\
             bool Both(bool a_unbound_in, out bool a, int k, bool b_unbound_in, out bool b) : SV_Target { a = a_unbound_in; b = b_unbound_in; a = !a; b = a; return a; }\n\
             float G(float x, float y) { if (x > 0) return Both(x, x, 1, y, y); else Both(x, x, 2, x, x); return max(Both(x, x, 3, y, y), y); }\n"
        );
    }

    #[test]
    fn scalar_and_vector_parameters_of_every_type_are_split() {
        // Numbers and vectors, overloads split at the same place (a bool and a number there:
        // glslang crashes on that overload given a number), a split function calling another.
        let source = "void I(inout int i, inout min16float3 v) { i += 1; v *= 2; }\n\
                      void C(inout bool b) { b = !b; } void C(inout float2 f) { f += 2; }\n\
                      void G(inout uint u) { float x = u; I(u, x); C(x); }\n";
        assert!(splits(source));
        assert_eq!(
            inout_parameters(source),
            "void I(int i_unbound_in, out int i, min16float3 v_unbound_in, out min16float3 v) { i = i_unbound_in; v = v_unbound_in; i += 1; v *= 2; }\n\
             void C(bool b_unbound_in, out bool b) { b = b_unbound_in; b = !b; } void C(float2 f_unbound_in, out float2 f) { f = f_unbound_in; f += 2; }\n\
             void G(uint u_unbound_in, out uint u) { u = u_unbound_in; float x = u; I(u, u, x, x); C(x, x); }\n"
        );
        // Without a scalar `inout bool`, the program is not compiled rewritten at once.
        assert!(!splits("void I(inout int i) { } void B(inout bool2 b) { }"));
    }

    #[test]
    fn parameters_a_call_cannot_be_matched_to_stay() {
        // Plain parameters and `out` ones; a name declared without the parameter at the same
        // place elsewhere (an overload of another kind); a struct's members, defined in its
        // braces or as `S::M`, and their calls; a template type among the parameters; a
        // parameter with a semantic; matrices, structs, arrays and types that only end like a
        // vector.
        let kept = "void B(bool b, out bool c) { c = b; }\n\
                    void C(inout bool b) { } void C(float f, inout bool b) { } void G() { float x = 0; C(x); }\n\
                    struct T { void M(inout bool b) { b = !b; } void N(inout bool b); };\n\
                    void T::N(inout bool b) { b = !b; }\n\
                    void H(T t) { bool b = true; t.M(b); t.N(b); }\n\
                    void V(vector<float, 3> v, inout bool b) { } void W() { bool b = true; V(0, b); }\n\
                    void E(inout float4 color : COLOR0) { }\n\
                    void M(inout float3x3 m, inout T t, inout float a[2], inout float5 f, inout float44 g) { }\n";
        assert!(!splits(kept));
        assert_eq!(inout_parameters(kept), kept);
    }

    fn compile(source: &str) -> Result<shaderc::CompilationArtifact, shaderc::Error> {
        let compiler = shaderc::Compiler::new().unwrap();
        let mut o = shaderc::CompileOptions::new().unwrap();
        o.set_source_language(shaderc::SourceLanguage::HLSL);
        o.set_target_env(
            shaderc::TargetEnv::Vulkan,
            shaderc::EnvVersion::Vulkan1_1 as u32,
        );
        compiler.compile_into_spirv(source, shaderc::ShaderKind::Vertex, "t", "V", Some(&o))
    }

    /// glslang crashes on a number passed to an `inout bool` parameter (so the unrewritten
    /// program is not compiled here); the rewritten one compiles, for each kind of number.
    #[test]
    fn numbers_passed_to_split_bool_parameters_compile() {
        for number in ["float", "half", "int", "uint", "bool"] {
            let source = format!(
                "void SetAlive(inout bool alive, bool value) {{ alive = value && alive; }}\n\
                 float4 V() : SV_Position {{ {number} a = 1; SetAlive(a, true); return a; }}\n"
            );
            let fixed = inout_parameters(&source);
            compile(&fixed).unwrap_or_else(|e| panic!("{number}: {e}\n{fixed}"));
        }
        // Overloaded with an `inout` number at that place (unrewritten, a crash too).
        let overloaded = "void C(inout bool b) { b = !b; } void C(inout float2 f) { f += 2; }\n\
                          float4 V() : SV_Position { float a = 1; C(a); return a; }\n";
        let fixed = inout_parameters(overloaded);
        compile(&fixed).unwrap_or_else(|e| panic!("{e}\n{fixed}"));
    }

    /// An argument of another scalar type given to an `inout` parameter: glslang fails such a
    /// program (its SPIR-V does not validate) for each pair of base types below, and compiles
    /// the rewritten one; halves and floats, one type to glslang, compile either way.
    #[test]
    fn arguments_converted_to_split_parameters_compile() {
        let pairs = [
            ("int", "float"),
            ("float", "int"),
            ("int", "uint"),
            ("float3", "int3"),
            ("bool3", "float3"),
            ("float3", "bool3"),
            ("float", "bool"),
        ];
        for (parameter, argument) in pairs {
            let change = if parameter.starts_with("bool") {
                "x = !x;"
            } else {
                "x += 1;"
            };
            let source = format!(
                "void F(inout {parameter} x) {{ {change} }}\n\
                 float4 V() : SV_Position {{ {argument} a = ({argument})1; F(a); return float4((float)a.x, 0, 0, 1); }}\n"
            );
            assert!(
                compile(&source).is_err(),
                "{parameter} <- {argument} compiles as written"
            );
            let fixed = inout_parameters(&source);
            compile(&fixed).unwrap_or_else(|e| panic!("{parameter} <- {argument}: {e}\n{fixed}"));
        }
        let halves = "void F(inout half x) { x += 1; }\n\
                      float4 V() : SV_Position { float a = 1; F(a); return a; }\n";
        compile(halves).unwrap();
        compile(&inout_parameters(halves)).unwrap();
    }
}
