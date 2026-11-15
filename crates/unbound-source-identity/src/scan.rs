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

/// A workspace crate's directory below `crates/`, from its Rust name (`unbound_compat`): the
/// directory whose name reads as it (`unbound-compat`, also `unbound_compat`), else the name
/// with dashes.
fn crate_dir(root: &Path, name: &str) -> PathBuf {
    let crates = root.join("crates");
    if let Ok(entries) = std::fs::read_dir(&crates) {
        for entry in entries.flatten() {
            let dir = entry.file_name().to_string_lossy().into_owned();
            if dir.replace('-', "_") == name {
                return crates.join(dir);
            }
        }
    }
    crates.join(name.replace('_', "-"))
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

/// Every file below `dir`, sorted.
fn all_files(dir: &Path) -> io::Result<Vec<PathBuf>> {
    let mut out = Vec::new();
    let mut pending = vec![dir.to_path_buf()];
    while let Some(dir) = pending.pop() {
        for entry in std::fs::read_dir(&dir)? {
            let path = entry?.path();
            if path.is_dir() {
                pending.push(path);
            } else {
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
    /// The paths its `include_str!`, `include_bytes!`, `include!` and path attributes name.
    includes: Vec<(String, Include)>,
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
    let text = tight_paths(&without_tests(&out));
    // An include whose path is not a literal (`concat!`, `env!`) is not followed.
    let computed_include = ["include_str!(", "include_bytes!(", "include!("]
        .iter()
        .any(|m| {
            word_starts(&text, m).any(|at| !text[at + m.len()..].trim_start().starts_with('"'))
        });
    let unsure = computed_include
        || module_paths(&text)
        || text.contains("macro_rules!")
        || text.contains("#[macro_use]")
        || word_starts(&text, "extern").any(|at| text[at + 6..].trim_start().starts_with("crate"))
        || odd_paths(&text);
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

/// Whether an attribute sets a module's file (`#[path = ...]`, also under `cfg_attr`).
fn module_paths(text: &str) -> bool {
    text.match_indices("#").any(|(at, _)| {
        let rest = text[at + 1..].trim_start_matches('!').trim_start();
        let Some(body) = rest.strip_prefix('[') else {
            return false;
        };
        let body = &body[..body.find(']').unwrap_or(body.len())];
        word_starts(body, "path").any(|p| body[p + 4..].trim_start().starts_with('='))
    })
}

/// Whether a text names modules in a way the reading does not follow: `crate`, `self` or
/// `super` under another name in a `use` (`use super as p`, `{self as r}`; `self as i32`
/// elsewhere is a cast), or a `super::{..}` group that climbs further (`super::{super::a}`).
fn odd_paths(text: &str) -> bool {
    let renamed = word_starts(text, "use").any(|at| {
        let statement = &text[at..];
        let statement = &statement[..statement.find(';').unwrap_or(statement.len())];
        ["crate", "self", "super"].iter().any(|kw| {
            word_starts(statement, kw).any(|k| is_as(statement[k + kw.len()..].trim_start()))
        })
    });
    let climbing_group = text.match_indices("super::{").any(|(at, _)| {
        let group = &text[at + "super::{".len()..];
        let mut depth = 0;
        let end = group
            .char_indices()
            .find(|&(_, c)| {
                match c {
                    '{' => depth += 1,
                    '}' if depth == 0 => return true,
                    '}' => depth -= 1,
                    _ => {}
                }
                false
            })
            .map_or(group.len(), |(i, _)| i);
        word_starts(&group[..end], "super").next().is_some()
    });
    renamed || climbing_group
}

/// The packages a manifest depends on under another name (`package = "x"`, `'x'`, a quoted
/// `"package"` key, a dotted `foo.package`); None when it holds what this reading cannot be
/// sure of (an escape or a multi-line string, a `package` value that is not a plain quoted
/// package name). The `[package]` table's header is no such key.
fn renamed_packages(manifest: &str) -> Option<Vec<String>> {
    // An escape can spell a key or a name the reading would not see (`"p\u0061ckage"`).
    if manifest.contains('\\') || manifest.contains("\"\"\"") || manifest.contains("'''") {
        return None;
    }
    let mut out = Vec::new();
    for (at, _) in manifest.match_indices("package") {
        let before = manifest[..at].chars().next_back();
        let after = &manifest[at + "package".len()..];
        // A key: not part of a longer word, and followed (past a closing quote) by `=`.
        if before.is_some_and(|c| is_ident(c) || c == '-') || after.starts_with(is_ident) {
            continue;
        }
        let after = after
            .strip_prefix(['"', '\''])
            .unwrap_or(after)
            .trim_start();
        let Some(value) = after.strip_prefix('=') else {
            continue;
        };
        let value = value.trim_start();
        let quote = value.chars().next().filter(|q| matches!(q, '"' | '\''))?;
        let (name, _) = value[1..].split_once(quote)?;
        if name.is_empty() || !name.chars().all(|c| is_ident(c) || c == '-') {
            return None;
        }
        out.push(name.to_string());
    }
    Some(out)
}

/// Whether a text starts with the keyword `as`.
fn is_as(text: &str) -> bool {
    text.strip_prefix("as")
        .is_some_and(|r| r.starts_with(char::is_whitespace))
}

/// A text with the whitespace around its `::` removed (`super:: super::a`, `crate ::a`): its
/// paths are read as written without it, which means the same.
fn tight_paths(text: &str) -> String {
    let mut out = String::with_capacity(text.len());
    let mut rest = text;
    while let Some(at) = rest.find("::") {
        out.push_str(rest[..at].trim_end());
        out.push_str("::");
        rest = rest[at + 2..].trim_start();
    }
    out.push_str(rest);
    out
}

/// How many `#` open a raw string at the start of `rest` (after its `r`), if one does.
fn raw_string_start(rest: &[char]) -> Option<usize> {
    let hashes = rest.iter().take_while(|&&c| c == '#').count();
    (rest.get(hashes) == Some(&'"')).then_some(hashes)
}

/// Records a string literal that is the path of an include macro the code before it opens.
fn note_include(before: &str, literal: &str, includes: &mut Vec<(String, Include)>) {
    let before = before.trim_end();
    // A module's file (`#[path = "x.rs"]`, also under `cfg_attr`): `path =` inside an open
    // attribute.
    let in_attribute = before.rfind('#').is_some_and(|at| {
        before[at..]
            .trim_start_matches(['#', '!'])
            .trim_start()
            .starts_with('[')
            && !before[at..].contains(']')
    });
    if in_attribute
        && before.ends_with('=')
        && before[..before.len() - 1].trim_end().ends_with("path")
    {
        includes.push((literal.to_string(), Include::ModuleFile));
    }
    for (name, kind) in [
        ("include_str!(", Include::Data),
        ("include_bytes!(", Include::Data),
        ("include!(", Include::Code),
    ] {
        if before.ends_with(name) && !before[..before.len() - name.len()].ends_with(is_ident) {
            includes.push((literal.to_string(), kind));
        }
    }
}

/// What a file names another file for.
#[derive(Clone, Copy, PartialEq)]
enum Include {
    /// `include_str!`, `include_bytes!`: its bytes.
    Data,
    /// `include!`: code read where it stands.
    Code,
    /// A path attribute: a module's file.
    ModuleFile,
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

/// The paths a text names through `super::`: how many `super::` each chains, and the first
/// identifier after them (`*` for a glob or anything else).
fn super_paths(text: &str) -> Vec<(usize, String)> {
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
            out.push((supers, ident));
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
        if after.starts_with(char::is_whitespace) && is_as(after.trim_start()) {
            out.entry(name).or_default().push("*".into());
            continue;
        }
        if !after.starts_with("::") {
            continue;
        }
        let prefix = format!("{name}::");
        // `unbound_x::{.., self as y}`: under another name too (anywhere up to the
        // statement's end).
        let statement_end = after.find(';').unwrap_or(after.len());
        if word_starts(&after[..statement_end], "self")
            .any(|at| is_as(after[at + 4..statement_end].trim_start()))
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

/// Whether a workspace crate's manifest plainly names its library after its directory: no
/// escape or multi-line string, nothing before its first table, only the tables a plain crate
/// has (`[package]`, dependency tables, also per target, `[features]`, `[lints]`, binary,
/// example, test and bench targets), no `lib`, `project` or `package` key, and every line that
/// says `name` outside a target is `name = "<dir>"` under `[package]`. Anything else may name
/// the library otherwise or put it at another file, and paths to it would not be followed.
fn plain_manifest(manifest: &str, dir_name: &str) -> bool {
    const TABLES: [&str; 6] = [
        "[package]",
        "[dependencies]",
        "[dev-dependencies]",
        "[build-dependencies]",
        "[features]",
        "[lints]",
    ];
    const TARGETS: [&str; 4] = ["[[bin]]", "[[example]]", "[[test]]", "[[bench]]"];
    if manifest.contains('\\') || manifest.contains("\"\"\"") || manifest.contains("'''") {
        return false;
    }
    let mut section: Option<&str> = None;
    for line in manifest.lines() {
        let line = line.trim();
        if line.is_empty() || line.starts_with('#') {
            continue;
        }
        if line.starts_with('[') {
            // Per-target dependencies: `[target.'cfg(..)'.dependencies]` and the like.
            let per_target = line.starts_with("[target.")
                && ["dependencies]", "dev-dependencies]", "build-dependencies]"]
                    .iter()
                    .any(|t| line.ends_with(&format!(".{t}")));
            if !(TABLES.contains(&line) || TARGETS.contains(&line) || per_target) {
                return false;
            }
            section = Some(line);
            continue;
        }
        let Some(section) = section else {
            // A key before any table (`package = { .. }`, `lib.name = ..`).
            return false;
        };
        let key = line.split('=').next().unwrap_or("").trim();
        let first = key
            .trim_matches(['"', '\''])
            .split(['.', ' ', '\t', '"', '\''])
            .next()
            .unwrap_or("");
        if matches!(first, "lib" | "project" | "package") {
            return false;
        }
        if word_starts(line, "name").next().is_some() && !TARGETS.contains(&section) {
            let plain = format!("name = \"{dir_name}\"");
            if section != "[package]" || line != plain {
                return false;
            }
        }
    }
    true
}

/// Whether some workspace crate's library may go by another name than its directory's
/// ([`plain_manifest`]): every crate below `crates/` with a manifest is read.
fn libraries_renamed(root: &Path) -> io::Result<bool> {
    for entry in std::fs::read_dir(root.join("crates"))? {
        let dir = entry?.path();
        let Ok(manifest) = std::fs::read_to_string(dir.join("Cargo.toml")) else {
            continue;
        };
        let name = dir
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_default();
        if !plain_manifest(&manifest, &name) {
            return Ok(true);
        }
    }
    Ok(false)
}

/// The workspace crates of `crates/` (by Rust name): its directories with a manifest.
fn workspace_crates(root: &Path) -> io::Result<Vec<String>> {
    let mut out = Vec::new();
    for entry in std::fs::read_dir(root.join("crates"))? {
        let path = entry?.path();
        if path.join("Cargo.toml").is_file()
            && let Some(dir) = path.file_name()
        {
            out.push(dir.to_string_lossy().replace('-', "_"));
        }
    }
    Ok(out)
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
    // Anything else (an item of the root's own code, a name it cannot find): the whole crate.
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
    // A workspace library that may go by another name is named by paths the reading does not
    // follow: every workspace crate counts whole.
    if libraries_renamed(root)? {
        for name in workspace_crates(root)? {
            reach.entry(name).or_default().whole = true;
        }
    }
    loop {
        let mut files = BTreeSet::new();
        let mut changes: Vec<(String, Change)> = Vec::new();
        for (name, r) in &reach {
            let dir = crate_dir(root, name);
            let src = dir.join("src");
            let lib_path = src.join("lib.rs");
            files.insert(dir.join("Cargo.toml"));
            // A crate without a root file (its library elsewhere) counts whole: its files are
            // all read.
            if lib_path.is_file() {
                files.insert(lib_path.clone());
            }
            // A workspace crate depended on under another name (`foo = { package =
            // "unbound-x", .. }`, here or in the workspace's dependencies) is named by paths
            // the reading does not follow: all of it.
            for manifest in [dir.join("Cargo.toml"), root.join("Cargo.toml")] {
                let text = std::fs::read_to_string(&manifest).unwrap_or_default();
                let Some(renamed) = renamed_packages(&text) else {
                    // What it renames cannot be told: every workspace crate counts whole.
                    for other in workspace_crates(root)? {
                        changes.push((other, Change::Whole));
                    }
                    continue;
                };
                for package in renamed {
                    let other = package.replace('-', "_");
                    let other_dir = crate_dir(root, &other);
                    if other_dir.join("Cargo.toml").is_file() {
                        changes.push((other, Change::Whole));
                    }
                }
            }
            let module_files = |m: &str| -> io::Result<Vec<PathBuf>> {
                let mut out = rust_files(&src.join(m))?;
                let file = src.join(format!("{m}.rs"));
                if file.is_file() {
                    out.push(file);
                }
                Ok(out)
            };
            // A root with code of its own (items, impls, the `use` lines they rely on), or with
            // what the reading cannot follow (`#[path]`, an alias, a computed include), is not
            // read apart: a crate only partly reached counts whole.
            // The files its manifest points at (`[lib] path`, a binary's): hashed.
            let manifest = std::fs::read_to_string(dir.join("Cargo.toml")).unwrap_or_default();
            for (at, _) in manifest.match_indices("path") {
                let value = manifest[at + 4..].trim_start();
                if let Some(value) = value.strip_prefix('=')
                    && let Some(quoted) = value.trim_start().strip_prefix(['"', '\''])
                    && let Some(target) = quoted.split(['"', '\'']).next()
                {
                    let file = lexical(&dir.join(target));
                    if file.is_file() {
                        files.insert(file);
                    }
                }
            }
            // A build script may set what the crate's code compiles to.
            if !r.whole && dir.join("build.rs").is_file() {
                changes.push((name.clone(), Change::Whole));
            }
            if !r.whole {
                let root = code(&std::fs::read_to_string(&lib_path).unwrap_or_default());
                if !lib_path.is_file() || root.unsure || !root_code(&root.text).trim().is_empty() {
                    changes.push((name.clone(), Change::Whole));
                }
            }
            // A crate that defines macros: what their uses expand to is not read, so a crate
            // only partly reached counts whole.
            if !r.whole {
                for path in rust_files(&src)? {
                    let text = code(&std::fs::read_to_string(&path)?).text;
                    if text.contains("macro_rules!") || text.contains("#[macro_use]") {
                        changes.push((name.clone(), Change::Whole));
                        break;
                    }
                }
            }
            // The texts read: the reached modules' files.
            let mut texts: Vec<(PathBuf, Code)> = Vec::new();
            if r.whole {
                // Every file of its directory (build script, other sources, data); its Rust
                // files are read.
                for path in all_files(&dir)? {
                    if path.extension().is_some_and(|e| e == "rs") {
                        texts.push((path.clone(), code(&std::fs::read_to_string(&path)?)));
                    } else {
                        files.insert(path);
                    }
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
                let here = path.parent().unwrap_or(&src);
                for (include, kind) in &read.includes {
                    // A module file is relative to the file's directory, or (from a file that
                    // is not a directory's module) to the directory of the file's own name.
                    let own_dir = here.join(path.file_stem().unwrap_or_default());
                    let candidates = match kind {
                        Include::ModuleFile => vec![here.join(include), own_dir.join(include)],
                        _ => vec![here.join(include)],
                    };
                    let found: Vec<PathBuf> = candidates
                        .iter()
                        .map(|c| lexical(c))
                        .filter(|c| c.is_file())
                        .collect();
                    if found.is_empty() && *kind == Include::ModuleFile && include.contains("..") {
                        // A module from outside the crate that this reading cannot find.
                        return Err(io::Error::other(format!(
                            "{}: cannot find the module file {include} it names",
                            path.display()
                        )));
                    }
                    files.extend(found);
                    // Included code and modules at other files are read where they stand: the
                    // reading does not follow them.
                    if *kind != Include::Data {
                        changes.push((name.clone(), Change::Whole));
                    }
                }
                for (other, idents) in named_crates(text) {
                    if other == *name {
                        continue;
                    }
                    let other_src = crate_dir(root, &other).join("src");
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
                // A path climbing past the module's directory names the crate's modules (from
                // an inline module of the file, maybe the file's own items: resolved at the
                // crate, an unknown name counts the whole crate). Test modules are not read.
                for (supers, ident) in super_paths(text) {
                    if supers > depth {
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
            // The workspace's manifest is read for renames too.
            let watched = reach
                .keys()
                .map(|name| crate_dir(root, name))
                .chain([root.join("Cargo.toml")])
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
