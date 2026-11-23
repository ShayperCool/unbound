//! Scalar and vector `inout` parameters split into an `in` copy and an `out` one.
//!
//! FXC passes an argument to an `inout` parameter of another scalar type by converting it on the
//! way in and back on the way out (a `float` to an `inout int`, an `int3` to an `inout float3`,
//! a number to an `inout bool`). glslang compiles an `in` and an `out` parameter given such an
//! argument, with the same conversions, but not an `inout` one: it writes a program the SPIR-V
//! optimizer rejects ("not a logical pointer") for a conversion between numbers or to or from
//! vectors of bool, and crashes (the process with it) on a number given to an `inout bool`. A
//! Visual Effect Graph output drawn by a Shader Graph keeps its particles' `alive` as a `float`
//! and passes it to its blocks' `inout bool alive`. So such a parameter of a function declared
//! outside any braces becomes `<type> <name>_unbound_in, out <type> <name>`, its body starting
//! with `<name> = <name>_unbound_in;`, and each call of the function passes the argument to both
//! (the copy is the argument's text with the same rewrite: `b[F(c)]` gives `b[F(c, c)]` twice).
//! An argument is an l-value, written twice: one with a side effect, such as `a[i++]`, has it
//! twice.
//!
//! The rewrite is made in two scopes ([`Scope`]). A program glslang rejects as written, or that
//! has a scalar `inout bool` the first scope splits ([`splits`]: glslang may crash on it),
//! compiles with the first, the scalar `inout bool` parameters split; one that still fails, with
//! the second, every `inout` parameter of a scalar or vector type (`bool`, `int`, `uint`,
//! `dword`, `half`, `float`, `double` and the minimum precision types, alone or with 1 to 4
//! components), but for a function a call of which gives such a parameter an argument with a
//! side effect (`++`, `--`, an assignment, a call of a function with an `out` or `inout`
//! parameter), which would run twice, and for a name whose declarations disagree at those
//! places: they keep the first scope's split. Programs that compiled before compile as they
//! did. Halves and floats are one type to
//! glslang (`half` is 32-bit here): neither needs the rewrite. A change of the number of
//! components is not rewritten (glslang finds no matching function; what FXC does with it is not
//! measured).
//!
//! Left as they are: functions of a name another declaration of which has no such parameter at
//! the same place (a call cannot be told apart without the arguments' types; a function
//! overloaded with an `inout bool` and a parameter of another kind at that place, given a
//! number, still crashes glslang, as does `inout bool1`), names also declared as a struct's member, functions whose
//! parameters hold a template type (`vector<float, 3>`, whose commas this reading does not
//! follow), in the second scope parameters with a semantic (an entry point's, whose input and
//! output it names together) but scalar bools, and parameters of matrix, struct, array or
//! object types.

use crate::scopes::{Kind, Token, edit, matching, text, tokens};
use std::collections::{BTreeMap, BTreeSet, HashSet};

/// Words before a name that make its parenthesis a call, not a declaration.
const NOT_TYPES: &[&str] = &[
    "return", "else", "do", "case", "in", "out", "inout", "uniform",
];

/// The suffix of the `in` copy of a split parameter.
const IN_SUFFIX: &str = "_unbound_in";

/// The scalar types whose parameters, alone or as vectors, the second scope splits.
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

/// Which `inout` parameters a rewrite splits (see the module's description).
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub(crate) enum Scope {
    /// Scalar `bool` ones, also followed by a semantic or a default.
    Bools,
    /// Those and every one of a [`splittable`] type followed by the end of the parameter, of
    /// functions no call of which gives one an argument with a side effect.
    All,
}

/// Whether the parameter starting at token `k` is an `inout <type> <name>` one the scope
/// splits.
fn inout_at(source: &str, toks: &[Token], k: usize, scope: Scope) -> bool {
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
    if word(k) != Some("inout") || word(k + 2).is_none() {
        return false;
    }
    let ty = word(k + 1);
    let end = punct(k + 3);
    let bool_parameter = ty == Some("bool") && matches!(end, Some("," | ")" | ":" | "="));
    bool_parameter
        || (scope == Scope::All && ty.is_some_and(splittable) && matches!(end, Some("," | ")")))
}

/// Whether an argument's text assigns, increments or decrements: written twice, it would do
/// so twice (calls of functions that write their arguments are told apart in [`read`]).
fn has_side_effect(argument: &str) -> bool {
    if argument.contains("++") || argument.contains("--") {
        return true;
    }
    let b = argument.as_bytes();
    (0..b.len()).any(|i| {
        if b[i] != b'=' || b.get(i + 1) == Some(&b'=') {
            return false;
        }
        match i.checked_sub(1).map(|j| b[j]) {
            // `==`, `!=`, `<=`, `>=` compare; `<<=` and `>>=` assign.
            Some(b'=' | b'!') => false,
            Some(c @ (b'<' | b'>')) => i >= 2 && b[i - 2] == c,
            _ => true,
        }
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
/// the places of the split parameters of each name split, and the calls of those names (their
/// parentheses), innermost first.
struct Reading<'a> {
    toks: Vec<Token>,
    declarations: BTreeMap<&'a str, Vec<Declaration>>,
    split: BTreeMap<&'a str, Vec<usize>>,
    calls: Vec<(&'a str, usize, usize)>,
}

fn read(source: &str, scope: Scope) -> Reading<'_> {
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
    // The places a scope splits in each name: the same in every declaration of the name, none
    // of which holds a template type.
    let places_of = |list: &[Declaration], scope: Scope| -> Vec<usize> {
        let places = |d: &Declaration| -> Vec<usize> {
            parts(source, &toks, d.open, d.close)
                .iter()
                .enumerate()
                .filter(|(_, (first, _))| inout_at(source, &toks, *first, scope))
                .map(|(i, _)| i)
                .collect()
        };
        let first = places(&list[0]);
        if list.iter().all(|d| places(d) == first) {
            first
        } else {
            Vec::new()
        }
    };
    let mut bools = BTreeMap::new();
    let mut all = BTreeMap::new();
    for (&name, list) in &declarations {
        let templated = list.iter().any(|d| {
            (d.open + 1..d.close)
                .any(|j| toks[j].kind == Kind::Punct && text(source, &toks[j]) == "<")
        });
        if members.contains(name) || templated {
            continue;
        }
        // The second scope splits what the first does at least: a name whose declarations
        // disagree at another place keeps its first scope's places.
        let first = places_of(list, Scope::Bools);
        let second = places_of(list, Scope::All);
        if !first.is_empty() {
            bools.insert(name, first.clone());
        }
        let second = if second.is_empty() { first } else { second };
        if !second.is_empty() {
            all.insert(name, second);
        }
    }
    // Functions that write an argument (an `out` or `inout` parameter): a call of one inside a
    // split argument, written twice, would run twice.
    let writes: BTreeSet<&str> = declarations
        .iter()
        .filter(|(_, list)| {
            list.iter()
                .any(|d| (d.open + 1..d.close).any(|j| matches!(word(j), Some("out" | "inout"))))
        })
        .map(|(&name, _)| name)
        .collect();
    let declared: HashSet<usize> = declarations.values().flatten().map(|d| d.open).collect();
    // Calls: the name, not a member's (after `.` or `::`), then a parenthesised list that is no
    // declaration's; innermost first (by their closing parenthesis).
    let calls_of = |split: &BTreeMap<&str, Vec<usize>>| {
        let mut calls = Vec::new();
        for k in 0..toks.len() {
            let Some(name) = word(k) else {
                continue;
            };
            if !split.contains_key(name)
                || punct(k + 1) != Some("(")
                || declared.contains(&(k + 1))
                || (k > 0 && matches!(punct(k - 1), Some("." | "::")))
            {
                continue;
            }
            if let Some(close) = matching(source, &toks, k + 1) {
                calls.push((name, k + 1, close));
            }
        }
        calls.sort_by_key(|&(_, _, close)| close);
        calls
    };
    let split = if scope == Scope::Bools {
        bools
    } else {
        // A name a call of which gives a split parameter an argument with a side effect (an
        // assignment, an increment or a call of a function that writes an argument) keeps its
        // first scope's split.
        let mut effects = BTreeSet::new();
        for (name, open, close) in calls_of(&all) {
            let arguments = parts(source, &toks, open, close);
            if all[name].iter().any(|&place| {
                arguments.get(place).is_some_and(|&(first, end)| {
                    has_side_effect(&source[toks[first].start..toks[end - 1].end])
                        || (first..end).any(|j| {
                            word(j).is_some_and(|w| writes.contains(w)) && punct(j + 1) == Some("(")
                        })
                })
            }) {
                effects.insert(name);
            }
        }
        for name in effects {
            match bools.get(name) {
                Some(places) => all.insert(name, places.clone()),
                None => all.remove(name),
            };
        }
        all
    };
    let calls = calls_of(&split);
    Reading {
        toks,
        declarations,
        split,
        calls,
    }
}

/// Whether the first scope splits a parameter of the program (a scalar `inout bool`, on which
/// glslang may crash): the program then compiles rewritten at once.
pub(crate) fn splits(source: &str) -> bool {
    source.contains("inout") && !read(source, Scope::Bools).split.is_empty()
}

/// The program with the scope's `inout` parameters split and their calls passing the argument
/// twice (see the module's description); unchanged without such a parameter.
pub(crate) fn inout_parameters(source: &str, scope: Scope) -> String {
    if !source.contains("inout") {
        return source.to_string();
    }
    let Reading {
        toks,
        declarations,
        split,
        calls,
    } = read(source, scope);
    if split.is_empty() {
        return source.to_string();
    }
    let mut removed = Vec::new();
    let mut inserts: Vec<(usize, String)> = Vec::new();
    for (name, places) in &split {
        for d in &declarations[name] {
            let mut assignments = String::new();
            let parameters = parts(source, &toks, d.open, d.close);
            for &place in places {
                let (first, _) = parameters[place];
                let ty = text(source, &toks[first + 1]);
                let id = text(source, &toks[first + 2]);
                // `inout bool alive` -> `bool alive_unbound_in, out bool alive`.
                removed.push(toks[first].start..toks[first + 1].end);
                inserts.push((toks[first].start, format!("{ty} {id}{IN_SUFFIX}, out {ty}")));
                assignments.push_str(&format!(" {id} = {id}{IN_SUFFIX};"));
            }
            if let Some(body) = d.body {
                inserts.push((toks[body].end, assignments));
            }
        }
    }
    // Each split argument again after itself, rewritten as it is: the calls inside it come
    // first, so their copies are in it.
    for (name, open, close) in calls {
        let arguments = parts(source, &toks, open, close);
        for &place in &split[name] {
            let Some(&(first, end)) = arguments.get(place) else {
                continue;
            };
            let (start, stop) = (toks[first].start, toks[end - 1].end);
            let inside = inserts
                .iter()
                .filter(|(at, _)| (start..stop).contains(at))
                .map(|(at, text)| (at - start, text.clone()))
                .collect();
            let argument = edit(&source[start..stop], Vec::new(), inside);
            inserts.push((stop, format!(", {argument}")));
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
            inout_parameters(&source, Scope::Bools),
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
            inout_parameters(shapes, Scope::Bools),
            "bool Both(bool a_unbound_in, out bool a, int k, bool b_unbound_in, out bool b);\n\
             bool Both(bool a_unbound_in, out bool a, int k, bool b_unbound_in, out bool b) : SV_Target { a = a_unbound_in; b = b_unbound_in; a = !a; b = a; return a; }\n\
             float G(float x, float y) { if (x > 0) return Both(x, x, 1, y, y); else Both(x, x, 2, x, x); return max(Both(x, x, 3, y, y), y); }\n"
        );
    }

    #[test]
    fn scalar_and_vector_parameters_of_every_type_are_split() {
        // Numbers and vectors, overloads split at the same place, a split function calling
        // another, a call of a function that writes no argument and a comparison in an
        // argument.
        let source = "void I(inout int i, inout min16float3 v) { i += 1; v *= 2; } uint P(uint u) { return u; }\n\
                      void C(inout bool b) { b = !b; } void C(inout float2 f) { f += 2; }\n\
                      void G(inout uint u) { float x = u; float a[2]; I(u, x); C(x); C(a[P(u) == 1]); C(a[u >= 1]); }\n";
        assert_eq!(
            inout_parameters(source, Scope::All),
            "void I(int i_unbound_in, out int i, min16float3 v_unbound_in, out min16float3 v) { i = i_unbound_in; v = v_unbound_in; i += 1; v *= 2; } uint P(uint u) { return u; }\n\
             void C(bool b_unbound_in, out bool b) { b = b_unbound_in; b = !b; } void C(float2 f_unbound_in, out float2 f) { f = f_unbound_in; f += 2; }\n\
             void G(uint u_unbound_in, out uint u) { u = u_unbound_in; float x = u; float a[2]; I(u, u, x, x); C(x, x); C(a[P(u) == 1], a[P(u) == 1]); C(a[u >= 1], a[u >= 1]); }\n"
        );
        // The first scope splits none of them (`C`'s overloads differ at the bool's place):
        // the program is not compiled rewritten at once, and compiles as written first.
        assert!(!splits(source));
        assert_eq!(inout_parameters(source, Scope::Bools), source);
        // Overloads that agree at the bool's place but not at another `inout` one keep the
        // bool split in the second scope (left whole, glslang would crash on it again).
        let partial = "void F(inout bool b, inout float x) { } void F(inout bool b, float4 y) { }\n\
                       void G() { float f = 1; float x = 0; F(f, x); }\n";
        assert_eq!(
            inout_parameters(partial, Scope::All),
            inout_parameters(partial, Scope::Bools)
        );
        assert!(inout_parameters(partial, Scope::All).contains("F(f, f, x)"));
    }

    #[test]
    fn split_calls_inside_split_arguments_are_copied_rewritten() {
        let source = "int Pick(inout bool b) { return b ? 1 : 0; } void Set(inout bool b) { b = !b; }\n\
                      void G() { bool c = true; bool flags[2]; Set(flags[Pick(c)]); }\n";
        assert!(
            inout_parameters(source, Scope::Bools)
                .contains("Set(flags[Pick(c, c)], flags[Pick(c, c)]);")
        );
    }

    #[test]
    fn arguments_with_a_side_effect_keep_the_first_scope() {
        // `F`'s arguments increment or call a function that writes its argument: `F` is not
        // split, `B` (a bool) as in the first scope, `H` (no side effect) in the second.
        let source = "void F(inout float x) { } void B(inout bool b, inout int n) { } void H(inout int n) { }\n\
                      uint R(inout uint s) { s += 1; return s; }\n\
                      void G() { float a[2]; int i = 0; uint s = 0; bool c = true; F(a[i++]); F(a[R(s) & 1]); B(c, i += 1); H(i); }\n";
        assert_eq!(
            inout_parameters(source, Scope::All),
            "void F(inout float x) { } void B(bool b_unbound_in, out bool b, inout int n) { b = b_unbound_in; } void H(int n_unbound_in, out int n) { n = n_unbound_in; }\n\
             uint R(uint s_unbound_in, out uint s) { s = s_unbound_in; s += 1; return s; }\n\
             void G() { float a[2]; int i = 0; uint s = 0; bool c = true; F(a[i++]); F(a[R(s, s) & 1]); B(c, c, i += 1); H(i, i); }\n"
        );
        for (argument, effect) in [
            ("a[i++]", true),
            ("a[--i]", true),
            ("x = y", true),
            ("x <<= 1", true),
            ("a[i == j]", false),
            ("a[i != j]", false),
            ("a[i <= j]", false),
            ("a[(i >= j) ? 1 : 0]", false),
            ("a[i - -j]", false),
        ] {
            assert_eq!(has_side_effect(argument), effect, "{argument}");
        }
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
        assert_eq!(inout_parameters(kept, Scope::All), kept);
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
            assert!(splits(&source));
            let fixed = inout_parameters(&source, Scope::Bools);
            compile(&fixed).unwrap_or_else(|e| panic!("{number}: {e}\n{fixed}"));
        }
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
            let fixed = inout_parameters(&source, Scope::All);
            compile(&fixed).unwrap_or_else(|e| panic!("{parameter} <- {argument}: {e}\n{fixed}"));
        }
        let halves = "void F(inout half x) { x += 1; }\n\
                      float4 V() : SV_Position { float a = 1; F(a); return a; }\n";
        compile(halves).unwrap();
        compile(&inout_parameters(halves, Scope::All)).unwrap();
    }
}
