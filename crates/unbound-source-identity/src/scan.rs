//! Reading the workspace for the sources an output is made by (see the crate docs). Shared
//! with the build script, which computes the identities from it.
use std::collections::{BTreeMap, BTreeSet};
use std::io;
use std::path::{Path, PathBuf};

/// What a reading of a workspace crate covers.
#[derive(Default)]
struct Reach {
    /// The modules (by name) reached.
    modules: BTreeSet<String>,
    /// The whole crate counts.
    whole: bool,
}

/// What a name read in a crate adds to its reach.
#[derive(Debug, PartialEq)]
enum Change {
    None,
    Whole,
    Module(String),
}

/// A workspace crate's directory below `crates/`, from its Rust name (`unbound_compat`).
fn crate_dir(name: &str) -> String {
    name.replace('_', "-")
}

/// Every Rust file below `dir`, sorted (none when it is not a directory).
fn rust_files(dir: &Path) -> io::Result<Vec<PathBuf>> {
    let mut out = Vec::new();
    if !dir.is_dir() {
        return Ok(out);
    }
    let mut pending = vec![dir.to_path_buf()];
    while let Some(dir) = pending.pop() {
        for entry in std::fs::read_dir(&dir)? {
            let path = entry?.path();
            if path.is_dir() {
                pending.push(path);
            } else if path.extension().is_some_and(|e| e == "rs") {
                out.push(path);
            }
        }
    }
    out.sort();
    Ok(out)
}

/// A file read for the paths its code names.
struct Code {
    /// Its code without comments and test code, its string and character literals emptied
    /// (`""`, `' '`): what they hold names nothing.
    text: String,
    /// The paths its `include_str!`, `include_bytes!` and `include!` name, with whether the
    /// include is code (`include!`).
    includes: Vec<(String, bool)>,
    /// It holds something the reading cannot follow: an include of a computed path, a crate
    /// alias, a `#[path]` module, a macro (its expansion names what the reading does not see).
    unsure: bool,
}

/// Reads a file's text for its paths ([`Code`]).
fn code(text: &str) -> Code {
    let chars: Vec<char> = text.chars().collect();
    let mut out = String::with_capacity(text.len());
    let mut includes = Vec::new();
    let mut i = 0;
    while i < chars.len() {
        let c = chars[i];
        let next = chars.get(i + 1).copied();
        if c == '/' && next == Some('/') {
            while i < chars.len() && chars[i] != '\n' {
                i += 1;
            }
        } else if c == '/' && next == Some('*') {
            let mut depth = 0;
            while i < chars.len() {
                if chars[i] == '/' && chars.get(i + 1) == Some(&'*') {
                    depth += 1;
                    i += 2;
                } else if chars[i] == '*' && chars.get(i + 1) == Some(&'/') {
                    depth -= 1;
                    i += 2;
                    if depth == 0 {
                        break;
                    }
                } else {
                    i += 1;
                }
            }
            out.push(' ');
        } else if c == 'r'
            && raw_prefix_ok(&out)
            && matches!(next, Some('"' | '#'))
            && raw_string_start(&chars[i + 1..]).is_some()
        {
            // A raw string: `r"..."`, `r#"..."#`.
            let hashes = raw_string_start(&chars[i + 1..]).unwrap();
            i += 2 + hashes;
            let start = i;
            while i < chars.len()
                && !(chars[i] == '"'
                    && chars[i + 1..].iter().take(hashes).all(|&h| h == '#')
                    && chars.len() >= i + 1 + hashes)
            {
                i += 1;
            }
            let literal: String = chars[start..i.min(chars.len())].iter().collect();
            note_include(&out, &literal, &mut includes);
            out.push_str("\"\"");
            i += 1 + hashes;
        } else if c == '"' {
            i += 1;
            let mut literal = String::new();
            while i < chars.len() && chars[i] != '"' {
                if chars[i] == '\\' {
                    i += 1;
                }
                if i < chars.len() {
                    literal.push(chars[i]);
                }
                i += 1;
            }
            note_include(&out, &literal, &mut includes);
            out.push_str("\"\"");
            i += 1;
        } else if c == '\'' {
            // A character literal (`'a'`, `'\n'`, `'{'`) or a lifetime (`'a`).
            let close = match next {
                Some('\\') => chars[i + 2..]
                    .iter()
                    .position(|&c| c == '\'')
                    .map(|p| i + 2 + p),
                Some(_) if chars.get(i + 2) == Some(&'\'') => Some(i + 2),
                _ => None,
            };
            match close {
                Some(end) => {
                    out.push_str("' '");
                    i = end + 1;
                }
                None => {
                    out.push(c);
                    i += 1;
                }
            }
        } else {
            out.push(c);
            i += 1;
        }
    }
    let text = without_tests(&out);
    // An include whose path is not a literal (`concat!`, `env!`) is not followed.
    let computed_include = ["include_str!(", "include_bytes!(", "include!("]
        .iter()
        .any(|m| {
            word_starts(&text, m).any(|at| !text[at + m.len()..].trim_start().starts_with('"'))
        });
    let unsure = computed_include
        || use_renames_self(&text)
        || text.contains("#[path")
        || text.contains("#![path")
        || text.contains("macro_rules!")
        || text.contains("#[macro_use]")
        || word_starts(&text, "extern").any(|at| text[at + 6..].trim_start().starts_with("crate"))
        || crate_aliases(&text);
    Code {
        text,
        includes,
        unsure,
    }
}

/// Whether an `r` after `before` can open a raw string: it starts a token, or follows the `b`
/// or `c` that starts one (`br"..."`, `cr#"..."#`).
fn raw_prefix_ok(before: &str) -> bool {
    let mut back = before.chars().rev();
    match back.next() {
        None => true,
        Some('b' | 'c') => !back.next().is_some_and(is_ident),
        Some(c) => !is_ident(c),
    }
}

/// Whether a `use` names a module under another name (`use crate::{self as c}`), which the
/// reading does not follow (`self as i32` in an expression is a cast).
fn use_renames_self(text: &str) -> bool {
    word_starts(text, "use").any(|at| {
        let statement = &text[at..];
        let statement = &statement[..statement.find(';').unwrap_or(statement.len())];
        word_starts(statement, "self").any(|s| statement[s + 4..].trim_start().starts_with("as "))
    })
}

/// How many `#` open a raw string at the start of `rest` (after its `r`), if one does.
fn raw_string_start(rest: &[char]) -> Option<usize> {
    let hashes = rest.iter().take_while(|&&c| c == '#').count();
    (rest.get(hashes) == Some(&'"')).then_some(hashes)
}

/// Records a string literal that is the path of an include macro the code before it opens.
fn note_include(before: &str, literal: &str, includes: &mut Vec<(String, bool)>) {
    let before = before.trim_end();
    for (name, is_code) in [
        ("include_str!(", false),
        ("include_bytes!(", false),
        ("include!(", true),
    ] {
        if before.ends_with(name) && !before[..before.len() - name.len()].ends_with(is_ident) {
            includes.push((literal.to_string(), is_code));
        }
    }
}

/// Whether a text names its own crate or a workspace crate under another name (`use crate as
/// c`, `use unbound_x as y`): the reading does not follow such names.
fn crate_aliases(text: &str) -> bool {
    word_starts(text, "as").any(|at| {
        let before = text[..at].trim_end();
        let name: String = before
            .chars()
            .rev()
            .take_while(|c| is_ident(*c))
            .collect::<Vec<_>>()
            .into_iter()
            .rev()
            .collect();
        let path_end = before.len() - name.len();
        (name == "crate" || name.starts_with("unbound_")) && !before[..path_end].ends_with("::")
    })
}

/// A text without its items under `#[cfg(test)]` (test code is not in the output's build). An
/// item whose end it cannot find is kept.
fn without_tests(text: &str) -> String {
    const MARK: &str = "#[cfg(test)]";
    let mut out = String::with_capacity(text.len());
    let mut rest = text;
    while let Some(at) = rest.find(MARK) {
        out.push_str(&rest[..at]);
        let item = &rest[at + MARK.len()..];
        // The item ends at its first `;` outside braces, or where its first brace closes.
        let mut depth = 0i32;
        let mut end = None;
        for (i, c) in item.char_indices() {
            match c {
                '{' => depth += 1,
                '}' => {
                    depth -= 1;
                    if depth == 0 {
                        end = Some(i + 1);
                        break;
                    }
                }
                ';' if depth == 0 => {
                    end = Some(i + 1);
                    break;
                }
                _ => {}
            }
        }
        match end {
            Some(end) => rest = &item[end..],
            None => {
                out.push_str(&rest[at..]);
                rest = "";
            }
        }
    }
    out.push_str(rest);
    out
}

fn is_ident(c: char) -> bool {
    c.is_ascii_alphanumeric() || c == '_'
}

/// Where `word` stands in `text` as a word of its own (not inside a longer identifier).
fn word_starts<'a>(text: &'a str, word: &'a str) -> impl Iterator<Item = usize> + 'a {
    text.match_indices(word)
        .map(|(at, _)| at)
        .filter(move |&at| {
            !text[..at].chars().next_back().is_some_and(is_ident)
                && (!word.ends_with(is_ident)
                    || !text[at + word.len()..].chars().next().is_some_and(is_ident))
        })
}

/// The first identifiers of the paths that follow `prefix` (`crate::` names `crate::a::B` as
/// `a`, `crate::{a, b::C}` as `a` and `b`); `*` for a glob or anything else.
fn named_after(text: &str, prefix: &str) -> Vec<String> {
    let mut out = Vec::new();
    for start in word_starts(text, prefix) {
        let rest = &text[start + prefix.len()..];
        if let Some(group) = rest.strip_prefix('{') {
            // The items of the group at its own depth.
            let mut depth = 0;
            let mut item_start = true;
            for (i, c) in group.char_indices() {
                match c {
                    '{' => depth += 1,
                    '}' if depth == 0 => break,
                    '}' => depth -= 1,
                    ',' if depth == 0 => item_start = true,
                    c if item_start && depth == 0 && !c.is_whitespace() => {
                        item_start = false;
                        let ident: String =
                            group[i..].chars().take_while(|c| is_ident(*c)).collect();
                        out.push(if ident.is_empty() { "*".into() } else { ident });
                    }
                    _ => {}
                }
            }
        } else {
            let ident: String = rest.chars().take_while(|c| is_ident(*c)).collect();
            out.push(if ident.is_empty() { "*".into() } else { ident });
        }
    }
    out
}

/// The paths a text names through `super::`: where each stands, how many `super::` it chains,
/// and the first identifier after them (`*` for a glob or anything else).
fn super_paths(text: &str) -> Vec<(usize, usize, String)> {
    const SUPER: &str = "super::";
    let mut out = Vec::new();
    for start in word_starts(text, SUPER) {
        // A chain is read from its first link.
        if text[..start].ends_with(SUPER) {
            continue;
        }
        let mut rest = &text[start..];
        let mut supers = 0;
        while let Some(after) = rest.strip_prefix(SUPER) {
            supers += 1;
            rest = after;
        }
        // The path ends by its statement's end at the latest.
        let head = &rest[..rest.find(';').unwrap_or(rest.len())];
        let named = named_after(&format!("{SUPER}{head}"), SUPER);
        for ident in named.into_iter().take(first_group(rest)) {
            out.push((start, supers, ident));
        }
    }
    out
}

/// The workspace crates a text names (`unbound_compat::...`), with what it names of each.
fn named_crates(text: &str) -> BTreeMap<String, Vec<String>> {
    let mut out: BTreeMap<String, Vec<String>> = BTreeMap::new();
    let starts = text
        .match_indices("unbound_")
        .map(|(at, _)| at)
        .filter(|&at| !text[..at].chars().next_back().is_some_and(is_ident));
    for start in starts {
        let name: String = text[start..].chars().take_while(|c| is_ident(*c)).collect();
        let after = &text[start + name.len()..];
        // Under another name (`use unbound_x as y`) its paths are not followed: all of it.
        if after.starts_with(char::is_whitespace) && after.trim_start().starts_with("as ") {
            out.entry(name).or_default().push("*".into());
            continue;
        }
        if !after.starts_with("::") {
            continue;
        }
        let prefix = format!("{name}::");
        // `unbound_x::{self as y}`: under another name too.
        let group_end = after.find(['}', ';']).unwrap_or(after.len());
        if word_starts(&after[..group_end], "self")
            .any(|at| after[at + 4..group_end].trim_start().starts_with("as "))
        {
            out.entry(name.clone()).or_default().push("*".into());
        }
        let items = named_after(&text[start..], &prefix);
        // `named_after` reads every later use too: the first group is this one's.
        let first = first_group(&text[start + prefix.len()..]);
        out.entry(name)
            .or_default()
            .extend(items.into_iter().take(first));
    }
    out
}

/// How many items the path or group at the start of `rest` names (a group's items at its own
/// depth: `{a::{B, C}, d}` names two).
fn first_group(rest: &str) -> usize {
    let Some(group) = rest.strip_prefix('{') else {
        return 1;
    };
    let mut depth = 0;
    let mut items = 0;
    let mut in_item = false;
    for c in group.chars() {
        match c {
            '{' => depth += 1,
            '}' if depth == 0 => break,
            '}' => depth -= 1,
            ',' if depth == 0 => in_item = false,
            c if !c.is_whitespace() && depth == 0 && !in_item => {
                in_item = true;
                items += 1;
            }
            _ => {}
        }
    }
    items.max(1)
}

/// Whether `text` defines an item named `ident` (`fn`, `struct`, ... or an inline `mod`).
fn defines(text: &str, ident: &str) -> bool {
    const KEYWORDS: [&str; 10] = [
        "fn",
        "struct",
        "enum",
        "trait",
        "type",
        "const",
        "static",
        "union",
        "mod",
        "macro_rules!",
    ];
    KEYWORDS.iter().any(|kw| {
        word_starts(text, kw).any(|at| {
            let rest = &text[at + kw.len()..];
            rest.starts_with(char::is_whitespace) && {
                let rest = rest.trim_start();
                rest.starts_with(ident) && !rest[ident.len()..].starts_with(is_ident)
            }
        })
    })
}

/// Whether a text holds an inline module (`mod tests { ... }`).
fn has_inline_module(text: &str) -> bool {
    word_starts(text, "mod").any(|at| {
        let rest = text[at + 3..].trim_start();
        let name_end = rest.find(|c: char| !is_ident(c)).unwrap_or(rest.len());
        name_end > 0 && rest[name_end..].trim_start().starts_with('{')
    })
}

/// The brace depth at each byte of a text.
fn depths(text: &str) -> Vec<u32> {
    let mut out = Vec::with_capacity(text.len());
    let mut depth = 0u32;
    for b in text.bytes() {
        if b == b'}' {
            depth = depth.saturating_sub(1);
        }
        out.push(depth);
        if b == b'{' {
            depth += 1;
        }
    }
    out
}

/// The type names a text defines at its top level (`struct`, `enum`, `trait`, `type`, `union`;
/// not a trait's or impl's associated types).
fn defined_types(text: &str) -> BTreeSet<String> {
    let depth = depths(text);
    let mut out = BTreeSet::new();
    for kw in ["struct", "enum", "trait", "type", "union"] {
        for at in word_starts(text, kw) {
            let rest = &text[at + kw.len()..];
            if depth[at] != 0 || !rest.starts_with(char::is_whitespace) {
                continue;
            }
            let ident: String = rest
                .trim_start()
                .chars()
                .take_while(|c| is_ident(*c))
                .collect();
            if !ident.is_empty() {
                out.insert(ident);
            }
        }
    }
    out
}

/// The last identifier of a type path at the start of `text`, before its generic arguments
/// (`fmt::Display<'a>` is `Display`, `&mut serialized::File` is `File`).
fn path_name(text: &str) -> String {
    let text = text
        .trim_start()
        .trim_start_matches(['&', '*'])
        .trim_start();
    let text = text.strip_prefix("mut ").unwrap_or(text);
    let text = text.strip_prefix("dyn ").unwrap_or(text);
    let path: String = text
        .chars()
        .take_while(|c| is_ident(*c) || *c == ':')
        .collect();
    path.rsplit("::").next().unwrap_or("").to_string()
}

/// Whether a text implements something for one of `types`, or one of `types` (a trait) for
/// something: what `impl Trait for Type` and `impl Type` name, not their generic arguments.
fn implements(text: &str, types: &BTreeSet<String>) -> bool {
    word_starts(text, "impl").any(|at| {
        let mut header = text[at + 4..].trim_start();
        // The impl's own generic parameters.
        if header.starts_with('<') {
            let mut depth = 0;
            let mut end = header.len();
            for (i, c) in header.char_indices() {
                match c {
                    '<' => depth += 1,
                    '>' => {
                        depth -= 1;
                        if depth == 0 {
                            end = i + 1;
                            break;
                        }
                    }
                    _ => {}
                }
            }
            header = &header[end..];
        }
        let header = &header[..header.find('{').unwrap_or(header.len())];
        let first = path_name(header);
        let target = word_starts(header, "for")
            .next()
            .map(|f| path_name(&header[f + 3..]));
        types.contains(&first) || target.is_some_and(|t| types.contains(&t))
    })
}

/// A crate root without its declarations (`mod`, `use` and their attributes): its items with
/// code.
fn root_code(root: &str) -> String {
    let mut out = String::new();
    let mut statement = String::new();
    let mut depth = 0i32;
    for c in root.chars() {
        statement.push(c);
        match c {
            '{' => depth += 1,
            '}' => depth -= 1,
            _ => {}
        }
        if depth == 0 && (c == ';' || c == '}') {
            let mut body = statement.trim_start();
            // Attributes stand before what they apply to.
            while let Some(rest) = body.strip_prefix('#') {
                match rest.find(']') {
                    Some(end) => body = rest[end + 1..].trim_start(),
                    None => break,
                }
            }
            let body = body.strip_prefix("pub ").unwrap_or(body).trim_start();
            // A group's closing brace ends a statement early: its `;` alone is nothing.
            let declaration = body.trim() == ";"
                || (body.starts_with("mod ") && body.ends_with(';'))
                || body.starts_with("use ")
                || body.starts_with("extern crate ");
            if !declaration {
                out.push_str(&statement);
            }
            statement.clear();
        }
    }
    out.push_str(&statement);
    out
}

/// The module a crate root re-exports `ident` from (`pub use graphics::{GraphicsDevice, ...}`,
/// `pub use graphics::select as select_device`).
fn reexported_from(root: &str, ident: &str) -> Option<String> {
    for at in word_starts(root, "use") {
        let statement = &root[at + 3..];
        let statement = statement[..statement.find(';').unwrap_or(statement.len())].trim_start();
        let module: String = statement.chars().take_while(|c| is_ident(*c)).collect();
        let Some(rest) = statement[module.len()..].strip_prefix("::") else {
            continue;
        };
        // Each name exported: the last word of each item (`a`, `b as c` exports `c`).
        let group = rest.trim_start_matches('{');
        for item in group.split([',', '{', '}']) {
            let exported = item
                .split(|c: char| !is_ident(c))
                .filter(|w| !w.is_empty())
                .last();
            if exported == Some(ident) {
                return Some(module);
            }
        }
    }
    None
}

/// The modules of a crate's `src`: its files and directories other than the root.
fn crate_modules(src: &Path) -> io::Result<BTreeSet<String>> {
    let mut out = BTreeSet::new();
    for entry in std::fs::read_dir(src)? {
        let path = entry?.path();
        let Some(stem) = path.file_stem().map(|s| s.to_string_lossy().into_owned()) else {
            continue;
        };
        if path.is_dir() || (path.extension().is_some_and(|e| e == "rs") && stem != "lib") {
            out.insert(stem);
        }
    }
    Ok(out)
}

/// What `ident`, named at the root of the crate with `src` (`crate::ident`,
/// `unbound_x::ident`), reaches.
fn resolve(src: &Path, ident: &str) -> io::Result<Change> {
    if matches!(ident, "self" | "crate" | "super") {
        return Ok(Change::None);
    }
    if ident == "*" {
        return Ok(Change::Whole);
    }
    if crate_modules(src)?.contains(ident) {
        return Ok(Change::Module(ident.to_string()));
    }
    let lib = code(&std::fs::read_to_string(src.join("lib.rs"))?).text;
    if let Some(module) = reexported_from(&lib, ident) {
        return Ok(match crate_modules(src)?.contains(&module) {
            true => Change::Module(module),
            // Re-exported from another crate or from a module it cannot find.
            false => Change::Whole,
        });
    }
    // An item of the root's own code: the crate counts whole (its root's code is not read
    // apart).
    if defines(&root_code(&lib), ident) {
        return Ok(Change::Whole);
    }
    Ok(Change::Whole)
}

/// `path` with its `.` and `..` components resolved in the path itself.
fn lexical(path: &Path) -> PathBuf {
    let mut out = PathBuf::new();
    for component in path.components() {
        match component {
            std::path::Component::CurDir => {}
            std::path::Component::ParentDir if out.file_name().is_some() => {
                out.pop();
            }
            other => out.push(other),
        }
    }
    out
}

/// The source files of the output `modules` of the workspace crate `start` (its Rust name; no
/// modules: all of it) make, as the module docs say, below the workspace `root`; and the source directories of the
/// crates reached, for `cargo:rerun-if-changed` (a change anywhere in them may reach more).
pub fn producing_sources(
    root: &Path,
    start: &str,
    modules: &[&str],
) -> io::Result<(BTreeSet<PathBuf>, BTreeSet<PathBuf>)> {
    // Paths below the root as the root is written without `..` (an included file's is resolved
    // so), for the identity's relative paths.
    let root = &lexical(root);
    let mut reach: BTreeMap<String, Reach> = BTreeMap::new();
    let first = reach.entry(start.to_string()).or_default();
    first.modules.extend(modules.iter().map(|m| m.to_string()));
    // No modules: the whole crate makes the output.
    first.whole = modules.is_empty();
    loop {
        let mut files = BTreeSet::new();
        let mut changes: Vec<(String, Change)> = Vec::new();
        for (name, r) in &reach {
            let dir = root.join("crates").join(crate_dir(name));
            let src = dir.join("src");
            let lib_path = src.join("lib.rs");
            files.insert(dir.join("Cargo.toml"));
            files.insert(lib_path.clone());
            let module_files = |m: &str| -> io::Result<Vec<PathBuf>> {
                let mut out = rust_files(&src.join(m))?;
                let file = src.join(format!("{m}.rs"));
                if file.is_file() {
                    out.push(file);
                }
                Ok(out)
            };
            // A crate that defines macros: what their uses expand to is not read, so a crate
            // only partly reached counts whole.
            // A root with code of its own (items, impls, the `use` lines they rely on) is not
            // read apart: a crate only partly reached counts whole.
            if !r.whole {
                let root_text = code(&std::fs::read_to_string(&lib_path)?).text;
                if !root_code(&root_text).trim().is_empty() {
                    changes.push((name.clone(), Change::Whole));
                }
            }
            if !r.whole {
                for path in rust_files(&src)? {
                    let text = code(&std::fs::read_to_string(&path)?).text;
                    if text.contains("macro_rules!") || text.contains("#[macro_use]") {
                        changes.push((name.clone(), Change::Whole));
                        break;
                    }
                }
            }
            // The texts read: the reached modules' files (and the root's items with code).
            let mut texts: Vec<(PathBuf, Code)> = Vec::new();
            if r.whole {
                for path in rust_files(&src)? {
                    texts.push((path.clone(), code(&std::fs::read_to_string(&path)?)));
                }
            } else {
                for m in &r.modules {
                    let found = module_files(m)?;
                    if found.is_empty() {
                        changes.push((name.clone(), Change::Whole));
                    }
                    for path in found {
                        texts.push((path.clone(), code(&std::fs::read_to_string(&path)?)));
                    }
                }
            }
            for (path, read) in &texts {
                let text = &read.text;
                files.insert(path.clone());
                for (include, is_code) in &read.includes {
                    let file = lexical(&path.parent().unwrap_or(&src).join(include));
                    files.insert(file);
                    // Included code is read where it stands: the reading does not follow it.
                    if *is_code {
                        changes.push((name.clone(), Change::Whole));
                    }
                }
                for (other, idents) in named_crates(text) {
                    if other == *name {
                        continue;
                    }
                    let other_src = root.join("crates").join(crate_dir(&other)).join("src");
                    if !other_src.join("lib.rs").is_file() {
                        // Not a workspace crate (or not one of `crates/`): a package.
                        continue;
                    }
                    for ident in idents {
                        changes.push((other.clone(), resolve(&other_src, &ident)?));
                    }
                }
                if r.whole {
                    continue;
                }
                if read.unsure {
                    changes.push((name.clone(), Change::Whole));
                }
                let mut named = named_after(text, "crate::");
                // How deep the file's module is below the crate: a module's own directory holds
                // its submodules, whose `super::` names the module (reached with them); a
                // `mod.rs` is its directory's module itself.
                let dirs = path
                    .parent()
                    .and_then(|p| p.strip_prefix(&src).ok())
                    .map_or(0, |p| p.components().count());
                let depth = match path.file_name().is_some_and(|f| f == "mod.rs") {
                    true => dirs.saturating_sub(1),
                    false => dirs,
                };
                let brace_depth = depths(text);
                for (at, supers, ident) in super_paths(text) {
                    // From inside an inline module (`mod tests { use super::*; }`), the file
                    // itself.
                    let in_file = brace_depth[at] > 0
                        && has_inline_module(text)
                        && (ident == "*" || defines(text, &ident));
                    if supers > depth && !in_file {
                        named.push(ident);
                    }
                }
                if *path == lib_path {
                    // The root names its modules without `crate::`.
                    for m in crate_modules(&src)? {
                        if word_starts(text, &format!("{m}::")).next().is_some() {
                            named.push(m);
                        }
                    }
                }
                for ident in named {
                    changes.push((name.clone(), resolve(&src, &ident)?));
                }
            }
            if r.whole {
                continue;
            }
            // Modules that implement the types reached.
            let mut types = BTreeSet::new();
            for (_, read) in &texts {
                types.extend(defined_types(&read.text));
            }
            for m in crate_modules(&src)? {
                if r.modules.contains(&m) {
                    continue;
                }
                for path in module_files(&m)? {
                    if implements(&code(&std::fs::read_to_string(&path)?).text, &types) {
                        changes.push((name.clone(), Change::Module(m.clone())));
                    }
                }
            }
        }
        let mut changed = false;
        for (name, change) in changes {
            let r = reach.entry(name).or_default();
            changed |= match change {
                Change::None => false,
                Change::Whole => !std::mem::replace(&mut r.whole, true),
                Change::Module(m) => r.modules.insert(m),
            };
        }
        if !changed {
            let watched = reach
                .keys()
                .map(|name| root.join("crates").join(crate_dir(name)))
                .collect();
            return Ok((files, watched));
        }
    }
}

/// The hex identity of `files` (each by its path from `root` and its bytes) and of the
/// `[[package]]` blocks of `root/Cargo.lock` naming `locked` packages.
pub fn identity(root: &Path, files: &BTreeSet<PathBuf>, locked: &[&str]) -> io::Result<String> {
    let root = &lexical(root);
    let mut h = blake3::Hasher::new();
    let mut field = |bytes: &[u8]| {
        h.update(&(bytes.len() as u64).to_le_bytes());
        h.update(bytes);
    };
    for path in files {
        // Below the root, by their relative path (the same in every checkout); a file outside
        // it by its whole path.
        let path = lexical(path);
        let relative = path.strip_prefix(root).unwrap_or(&path);
        field(relative.to_string_lossy().replace('\\', "/").as_bytes());
        field(&std::fs::read(&path)?);
    }
    let lock = std::fs::read_to_string(root.join("Cargo.lock"))?;
    let blocks: String = lock
        .split("[[package]]")
        .filter(|block| {
            block.lines().any(|line| {
                locked
                    .iter()
                    .any(|name| line.trim() == format!("name = \"{name}\""))
            })
        })
        .collect();
    field(blocks.as_bytes());
    Ok(h.finalize().to_hex().to_string())
}

#[cfg(test)]
#[path = "tests.rs"]
mod tests;
