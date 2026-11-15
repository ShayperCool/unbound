//! Buffers with a counter passed to functions (`void Append(RWStructuredBuffer<Kvp> buffer, Kvp
//! value)` called as `Append(outputBuffer, value)`): HLSL resolves a resource parameter statically
//! to the global each call gives it; glslang passes such a buffer with a second, hidden block for
//! its counter that it does not lay out, and the module is rejected ("decorated as Block must be
//! explicitly laid out with Offset decorations"). Each call that gives such a parameter a global
//! buffer calls a copy of the function made for that buffer: the parameter is dropped and the
//! copy reads the global. Copies call copies in turn (a function passing its parameter on), and a
//! function left without calls is dropped. A call that gives anything but a global buffer (an
//! element of a buffer array, a conditional) is left as it is, with its function, and glslang
//! reports the program.
//!
//! The Visual Effect Graph's output kernels append to their indirect buffers so.

use crate::scopes::{Kind, Token, edit, item_start, matching, text, tokens};
use std::collections::{BTreeMap, HashSet};
use std::ops::Range;

/// The buffer types glslang gives a hidden counter block.
const COUNTER_BUFFER_TYPES: &[&str] = &[
    "RWStructuredBuffer",
    "AppendStructuredBuffer",
    "ConsumeStructuredBuffer",
];

/// Copies of copies end: a chain of functions passing a buffer on is as long as the chain of
/// calls, and a program whose copies never settle (which no valid HLSL gives) is left to glslang.
const MAX_ROUNDS: usize = 32;

/// A top-level function with counter buffer parameters: a definition, or a declaration only.
struct Function<'a> {
    name: &'a str,
    /// Tokens: the item's first, the name, the parameter list's closing parenthesis, and the last (the
    /// brace closing the body, or the declaration's `;`).
    item: usize,
    name_token: usize,
    close: usize,
    last: usize,
    defined: bool,
    /// Per parameter its token range, and its name when it is a counter buffer.
    params: Vec<(Range<usize>, Option<&'a str>)>,
    /// How many parameters have no default value.
    required: usize,
}

/// The token ranges between the top-level commas of a bracketed list (none for an empty list).
fn list_items(source: &str, toks: &[Token], open: usize, close: usize) -> Vec<Range<usize>> {
    let mut items = Vec::new();
    let (mut start, mut nesting) = (open + 1, 0i32);
    for k in open + 1..close {
        match text(source, &toks[k]) {
            "(" | "[" | "{" => nesting += 1,
            ")" | "]" | "}" => nesting -= 1,
            "," if nesting == 0 => {
                items.push(start..k);
                start = k + 1;
            }
            _ => {}
        }
    }
    if start < close {
        items.push(start..close);
    }
    items
}

/// The name of a counter buffer a parameter or a global declares (`RWStructuredBuffer<T> name`,
/// after `in` or `uniform`), given the declaration's tokens up to its default value, semantic or
/// register.
fn counter_buffer_name<'a>(
    source: &'a str,
    toks: &[Token],
    range: Range<usize>,
) -> Option<&'a str> {
    let mut k = range.start;
    while k < range.end && matches!(text(source, &toks[k]), "in" | "uniform" | "const") {
        k += 1;
    }
    if k >= range.end || !COUNTER_BUFFER_TYPES.contains(&text(source, &toks[k])) {
        return None;
    }
    let end = (k..range.end)
        .find(|&j| matches!(text(source, &toks[j]), "=" | ":"))
        .unwrap_or(range.end);
    let name = &toks[end.checked_sub(1)?];
    // `RWStructuredBuffer<T> name`: an array of buffers (`name[4]`) is not one buffer.
    (end - 1 > k && name.kind == Kind::Word).then(|| text(source, name))
}

/// The top-level functions that take counter buffers, and the global counter buffers.
fn declarations<'a>(source: &'a str, toks: &[Token]) -> (Vec<Function<'a>>, HashSet<&'a str>) {
    let mut functions = Vec::new();
    let mut globals = HashSet::new();
    let mut k = 0;
    while k < toks.len() {
        if toks[k].depth != 0 {
            k += 1;
            continue;
        }
        let s = text(source, &toks[k]);
        if COUNTER_BUFFER_TYPES.contains(&s) {
            // A global: `RWStructuredBuffer<T> name;` or `... name : register(u0);`.
            let end = (k..toks.len())
                .find(|&j| matches!(text(source, &toks[j]), ";" | "(" | ")" | "," | "{"))
                .unwrap_or(toks.len());
            if toks.get(end).is_some_and(|t| text(source, t) == ";")
                || (k..end).any(|j| text(source, &toks[j]) == ":")
            {
                if let Some(name) = counter_buffer_name(source, toks, k..end) {
                    globals.insert(name);
                }
            }
            k = end.max(k + 1);
            continue;
        }
        // A function: `type name ( parameters )` then its body, or `;`.
        let named = s == "("
            && k >= 2
            && toks[k - 1].kind == Kind::Word
            && (toks[k - 2].kind == Kind::Word || text(source, &toks[k - 2]) == ">");
        let Some(close) = named.then(|| matching(source, toks, k)).flatten() else {
            k += 1;
            continue;
        };
        // After the list: a semantic (`: SV_Target`), then the body or the `;`.
        let mut after = close + 1;
        while after < toks.len() && !matches!(text(source, &toks[after]), "{" | ";" | "=" | "(") {
            after += 1;
        }
        let last = match toks.get(after).map(|t| text(source, t)) {
            Some("{") => matching(source, toks, after),
            Some(";") => Some(after),
            _ => None,
        };
        let Some(last) = last else {
            k = close + 1;
            continue;
        };
        let params: Vec<_> = list_items(source, toks, k, close)
            .into_iter()
            .map(|r| {
                let name = counter_buffer_name(source, toks, r.clone());
                (r, name)
            })
            .collect();
        if params.iter().any(|(_, name)| name.is_some()) {
            let required = params
                .iter()
                .filter(|(r, _)| !r.clone().any(|j| text(source, &toks[j]) == "="))
                .count();
            functions.push(Function {
                name: text(source, &toks[k - 1]),
                item: item_start(source, toks, k, 0),
                name_token: k - 1,
                close,
                last,
                defined: text(source, &toks[after]) == "{",
                params,
                required,
            });
        }
        k = last + 1;
    }
    (functions, globals)
}

/// A call of one of the functions: the name's token and the arguments' token ranges.
struct Call {
    name_token: usize,
    args: Vec<Range<usize>>,
}

/// The calls of functions named in `names`, in function bodies.
fn calls(source: &str, toks: &[Token], names: &HashSet<&str>) -> Vec<Call> {
    let mut found = Vec::new();
    for k in 1..toks.len().saturating_sub(1) {
        if toks[k].depth == 0
            || toks[k].kind != Kind::Word
            || !names.contains(text(source, &toks[k]))
            || text(source, &toks[k + 1]) != "("
            || matches!(text(source, &toks[k - 1]), "." | "::")
        {
            continue;
        }
        if let Some(close) = matching(source, toks, k + 1) {
            found.push(Call {
                name_token: k,
                args: list_items(source, toks, k + 1, close),
            });
        }
    }
    found
}

/// The one function a call can mean: of that name, taking that many arguments.
fn callee<'f, 'a>(
    source: &str,
    toks: &[Token],
    functions: &'f [Function<'a>],
    call: &Call,
) -> Option<(usize, &'f Function<'a>)> {
    let name = text(source, &toks[call.name_token]);
    let mut fitting = functions.iter().enumerate().filter(|(_, f)| {
        f.name == name && (f.required..=f.params.len()).contains(&call.args.len())
    });
    let first = fitting.next()?;
    // A declaration and its definition are one function; two definitions are overloads this
    // does not tell apart.
    let ambiguous = fitting.any(|(_, f)| f.defined && first.1.defined);
    (!ambiguous).then_some(first)
}

/// The first token of the top-level item (a function) whose body holds token `k`.
fn enclosing_item(source: &str, toks: &[Token], k: usize) -> usize {
    let body = (0..k).rev().find(|&j| toks[j].depth == 0).unwrap_or(0);
    item_start(source, toks, body, 0)
}

/// The byte range that removes item `i` of a comma-separated list with its comma.
fn item_removal(toks: &[Token], items: &[Range<usize>], i: usize) -> Range<usize> {
    let r = &items[i];
    if i + 1 < items.len() {
        // Through the comma after it, up to the next item.
        toks[r.start].start..toks[items[i + 1].start].start
    } else if i > 0 {
        // From the end of the item before it.
        toks[items[i - 1].end - 1].end..toks[r.end - 1].end
    } else {
        toks[r.start].start..toks[r.end - 1].end
    }
}

/// The source with every call that gives a counter buffer parameter a global buffer calling a
/// copy of its function made for that buffer.
pub(crate) fn buffer_parameters(source: &str) -> String {
    if !COUNTER_BUFFER_TYPES.iter().any(|t| source.contains(t)) {
        return source.to_string();
    }
    let mut source = source.to_string();
    for _ in 0..MAX_ROUNDS {
        match specialize(&source) {
            Some(next) => source = next,
            None => break,
        }
    }
    drop_uncalled(&source)
}

/// One round: the copies the calls with global buffers need, and those calls renamed. `None`
/// when no call can be resolved.
fn specialize(source: &str) -> Option<String> {
    let toks = tokens(source);
    let (functions, globals) = declarations(source, &toks);
    let definitions: Vec<&Function> = functions.iter().filter(|f| f.defined).collect();
    if definitions.is_empty() || globals.is_empty() {
        return None;
    }
    let names: HashSet<&str> = functions.iter().map(|f| f.name).collect();
    let mut removed = Vec::new();
    let mut inserts = Vec::new();
    // Each copy made once: (function, the globals of its buffer parameters) -> its name.
    let mut copies: BTreeMap<(usize, Vec<&str>), String> = BTreeMap::new();
    for call in calls(source, &toks, &names) {
        let Some((_, f)) = callee(source, &toks, &functions, &call) else {
            continue;
        };
        // The definition the call's declaration stands for.
        let Some((index, f)) = functions
            .iter()
            .enumerate()
            .find(|(_, d)| d.defined && d.name == f.name && d.params.len() == f.params.len())
        else {
            continue;
        };
        // Every buffer parameter is given a global by its name (one without an argument has a
        // default value, which a buffer parameter cannot have).
        let given: Option<Vec<&str>> = f
            .params
            .iter()
            .enumerate()
            .filter(|(_, (_, name))| name.is_some())
            .map(|(i, _)| {
                let arg = call.args.get(i)?;
                let word = text(source, &toks[arg.start]);
                (arg.len() == 1 && globals.contains(word)).then_some(word)
            })
            .collect();
        let Some(given) = given else { continue };
        let name = copies.entry((index, given.clone())).or_insert_with(|| {
            let name = format!("{}_of_{}", f.name, given.join("_"));
            // The copy stands before the function that calls it, where the buffer is declared
            // already; a function defined after its caller keeps its place, declared before.
            let caller = enclosing_item(source, &toks, call.name_token);
            let at = toks[caller].start;
            let (copy, declaration) = copy_of(source, &toks, f, &name, &given);
            if f.item < caller {
                inserts.push((at, copy));
            } else {
                inserts.push((at, declaration));
                inserts.push((toks[f.last].end, format!("\n{copy}")));
            }
            name
        });
        removed.push(toks[call.name_token].start..toks[call.name_token].end);
        inserts.push((toks[call.name_token].start, name.clone()));
        for (i, (_, buffer)) in f.params.iter().enumerate() {
            if buffer.is_some() {
                removed.push(item_removal(&toks, &call.args, i));
            }
        }
    }
    (!removed.is_empty()).then(|| edit(source, removed, inserts))
}

/// A function's copy for the globals given to its buffer parameters, and that copy's declaration.
fn copy_of(
    source: &str,
    toks: &[Token],
    f: &Function,
    name: &str,
    given: &[&str],
) -> (String, String) {
    let mut removed = vec![toks[f.name_token].start..toks[f.name_token].end];
    let mut inserts = vec![(toks[f.name_token].start, name.to_string())];
    let ranges: Vec<Range<usize>> = f.params.iter().map(|(r, _)| r.clone()).collect();
    let mut renames = Vec::new();
    for (i, parameter) in f
        .params
        .iter()
        .enumerate()
        .filter_map(|(i, (_, name))| name.map(|n| (i, n)))
    {
        removed.push(item_removal(toks, &ranges, i));
        renames.push((parameter, given[renames.len()]));
    }
    // The body reads the globals where it read the parameters.
    for k in f.close + 1..f.last {
        let t = &toks[k];
        if t.kind != Kind::Word || text(source, &toks[k - 1]) == "." {
            continue;
        }
        if let Some((_, global)) = renames.iter().find(|(p, _)| *p == text(source, t)) {
            removed.push(t.start..t.end);
            inserts.push((t.start, global.to_string()));
        }
    }
    let (start, end) = (toks[f.item].start, toks[f.last].end);
    let within = |r: &Range<usize>, limit: usize| r.start >= start && r.end <= limit;
    let shift = |removed: &[Range<usize>], inserts: &[(usize, String)], limit: usize| {
        let removed = removed
            .iter()
            .filter(|r| within(r, limit))
            .map(|r| r.start - start..r.end - start)
            .collect();
        let inserts = inserts
            .iter()
            .filter(|(at, _)| *at < limit)
            .map(|(at, s)| (at - start, s.clone()))
            .collect();
        (removed, inserts)
    };
    let (r, i) = shift(&removed, &inserts, end);
    let copy = format!("{}\n", edit(&source[start..end], r, i));
    let header = toks[f.close].end;
    let (r, i) = shift(&removed, &inserts, header);
    let declaration = format!("{};\n", edit(&source[start..header], r, i));
    (copy, declaration)
}

/// The source without the functions taking counter buffers that nothing calls any more (each
/// call resolved to a copy), with their declarations.
fn drop_uncalled(source: &str) -> String {
    let mut source = source.to_string();
    loop {
        let toks = tokens(&source);
        let (functions, _) = declarations(&source, &toks);
        let names: HashSet<&str> = functions.iter().map(|f| f.name).collect();
        let mut called = vec![false; functions.len()];
        for call in calls(&source, &toks, &names) {
            // A call no single function answers keeps every function of its name.
            let name = text(&source, &toks[call.name_token]);
            match callee(&source, &toks, &functions, &call) {
                Some((_, f)) => {
                    let inside = functions
                        .iter()
                        .position(|g| g.defined && (g.close..g.last).contains(&call.name_token));
                    for (i, g) in functions.iter().enumerate() {
                        // A function calling itself does not keep itself.
                        if g.name == f.name && g.params.len() == f.params.len() && inside != Some(i)
                        {
                            called[i] = true;
                        }
                    }
                }
                None => {
                    for (i, g) in functions.iter().enumerate() {
                        called[i] |= g.name == name;
                    }
                }
            }
        }
        let removed: Vec<Range<usize>> = functions
            .iter()
            .zip(&called)
            .filter(|(_, called)| !**called)
            .map(|(f, _)| toks[f.item].start..toks[f.last].end)
            .collect();
        if removed.is_empty() {
            return source;
        }
        source = edit(&source, removed, Vec::new());
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn squeezed(s: &str) -> String {
        s.split_whitespace().collect::<Vec<_>>().join(" ")
    }

    #[test]
    fn a_call_with_a_global_buffer_calls_a_copy_for_it() {
        let out = buffer_parameters(
            "struct Kvp { float key; uint index; };\n\
             RWStructuredBuffer<Kvp> first;\n\
             RWStructuredBuffer<Kvp> second : register(u1);\n\
             void Append(RWStructuredBuffer<Kvp> buffer, Kvp value, uint step = 1)\n\
             {\n    uint at;\n    InterlockedAdd(buffer[0].index, step, at);\n    buffer[at] = value;\n}\n\
             void Main(Kvp k)\n{\n    Append(first, k);\n    Append(second, k, 2);\n    Append(first, k, 3);\n}\n",
        );
        let out = squeezed(&out);
        assert!(
            out.contains(
                "void Append_of_first(Kvp value, uint step = 1) { uint at; \
                 InterlockedAdd(first[0].index, step, at); first[at] = value; }"
            ),
            "{out}"
        );
        assert!(
            out.contains("void Append_of_second(Kvp value, uint step = 1)"),
            "{out}"
        );
        assert!(
            out.contains("Append_of_first(k); Append_of_second(k, 2); Append_of_first(k, 3);"),
            "{out}"
        );
        // One copy per buffer, and the function taking a buffer is gone.
        assert_eq!(out.matches("void Append_of_first").count(), 1, "{out}");
        assert!(!out.contains("RWStructuredBuffer<Kvp> buffer"), "{out}");
    }

    #[test]
    fn a_buffer_passed_on_resolves_through_the_copies() {
        let out = buffer_parameters(
            "AppendStructuredBuffer<uint> alive;\n\
             void Push(AppendStructuredBuffer<uint> list, uint v) { list.Append(v); }\n\
             void PushTwice(uint v, AppendStructuredBuffer<uint> list) { Push(list, v); Push(list, v + 1); }\n\
             void Main() { PushTwice(3, alive); }\n",
        );
        let out = squeezed(&out);
        assert!(
            out.contains("void Push_of_alive(uint v) { alive.Append(v); }"),
            "{out}"
        );
        assert!(
            out.contains(
                "void PushTwice_of_alive(uint v) { Push_of_alive(v); Push_of_alive(v + 1); }"
            ),
            "{out}"
        );
        assert!(
            out.contains("void Main() { PushTwice_of_alive(3); }"),
            "{out}"
        );
        assert!(!out.contains("list"), "{out}");
        // A copy stands after the copies it calls.
        assert!(
            out.find("void Push_of_alive").unwrap() < out.find("void PushTwice_of_alive").unwrap(),
            "{out}"
        );
    }

    #[test]
    fn a_function_defined_after_its_caller_keeps_its_place() {
        let out = buffer_parameters(
            "RWStructuredBuffer<uint> counts;\n\
             uint scale;\n\
             void Add(RWStructuredBuffer<uint> b, uint v);\n\
             void Main() { Add(counts, 1); }\n\
             static uint bias = 2;\n\
             void Add(RWStructuredBuffer<uint> b, uint v) { b[0] += v * scale + bias; }\n",
        );
        let out = squeezed(&out);
        let declared = out.find("void Add_of_counts(uint v);").expect(&out);
        let caller = out.find("void Main() { Add_of_counts(1); }").expect(&out);
        let defined = out
            .find("void Add_of_counts(uint v) { counts[0] += v * scale + bias; }")
            .expect(&out);
        assert!(
            declared < caller && caller < out.find("static uint bias").unwrap(),
            "{out}"
        );
        assert!(out.find("static uint bias").unwrap() < defined, "{out}");
        assert!(!out.contains("RWStructuredBuffer<uint> b"), "{out}");
    }

    #[test]
    fn other_buffers_and_other_arguments_stay() {
        // Buffers without a counter compile as parameters.
        let read_only = "StructuredBuffer<uint> source;\n\
             uint Read(StructuredBuffer<uint> b, uint i) { return b[i]; }\n\
             uint Main() { return Read(source, 0); }\n";
        assert_eq!(buffer_parameters(read_only), read_only);
        // An argument that is not one global buffer: the call and its function stay, for the
        // compiler to report.
        let chosen = "RWStructuredBuffer<uint> a;\nRWStructuredBuffer<uint> b[2];\n\
             void Set(RWStructuredBuffer<uint> t, uint v) { t[0] = v; }\n\
             void Main(uint i) { Set(b[i], 1); }\n";
        assert_eq!(buffer_parameters(chosen), chosen);
        // A member of the same name, and a local use of a buffer's method, are not calls.
        let member = "RWStructuredBuffer<uint> a;\n\
             struct S { uint Set; };\n\
             void Main(S s) { a[0] = s.Set; a.IncrementCounter(); }\n";
        assert_eq!(buffer_parameters(member), member);
    }

    #[test]
    fn a_function_some_calls_cannot_resolve_stays_with_its_copies() {
        let out = buffer_parameters(
            "RWStructuredBuffer<uint> a;\nRWStructuredBuffer<uint> b[2];\n\
             void Set(RWStructuredBuffer<uint> t, uint v) { t[0] = v; }\n\
             void Main(uint i) { Set(a, 1); Set(b[i], 2); }\n",
        );
        let out = squeezed(&out);
        assert!(out.contains("void Set_of_a(uint v) { a[0] = v; }"), "{out}");
        assert!(
            out.contains("void Set(RWStructuredBuffer<uint> t, uint v)"),
            "{out}"
        );
        assert!(out.contains("Set_of_a(1); Set(b[i], 2);"), "{out}");
    }

    #[test]
    fn a_member_named_as_a_parameter_keeps_its_name() {
        let out = buffer_parameters(
            "struct Item { uint list; };\nRWStructuredBuffer<Item> items;\n\
             void Mark(RWStructuredBuffer<Item> list, uint i) { list[i].list = 1; }\n\
             void Main() { Mark(items, 0); }\n",
        );
        assert!(
            squeezed(&out).contains("void Mark_of_items(uint i) { items[i].list = 1; }"),
            "{out}"
        );
    }
}
