//! Which class a script file stands for. Unity binds a `.cs` file's GUID to the class named like
//! the file *declared in that file*: two classes may share the name in different namespaces (a
//! game's `Game.Managers.CameraController` and TextMesh Pro's `TMPro.Examples.CameraController`).
//! This finds the namespace of the declaration with a light scan of the source.

/// The namespace (`A.B`, or `""` for the global namespace) of the type named `class_name`
/// declared in `source`; `None` when the file declares no such type.
pub fn declared_namespace(source: &str, class_name: &str) -> Option<String> {
    let code = strip(source);
    let tokens = tokenize(&code);
    let mut stack: Vec<(String, usize)> = Vec::new(); // (namespace, brace depth inside it)
    let mut file_scoped: Option<String> = None;
    let mut depth = 0usize;
    let mut i = 0;
    while i < tokens.len() {
        match tokens[i] {
            "{" => depth += 1,
            "}" => {
                depth = depth.saturating_sub(1);
                if stack.last().is_some_and(|(_, d)| *d > depth) {
                    stack.pop();
                }
            }
            "namespace" => {
                let mut name = String::new();
                let mut j = i + 1;
                while j < tokens.len() && tokens[j] != "{" && tokens[j] != ";" {
                    name.push_str(tokens[j]);
                    j += 1;
                }
                if j < tokens.len() && tokens[j] == ";" {
                    file_scoped = Some(name);
                } else if j < tokens.len() {
                    depth += 1;
                    stack.push((name, depth));
                }
                i = j + 1;
                continue;
            }
            "class" | "struct" | "interface" | "enum" | "record" => {
                if tokens.get(i + 1) == Some(&class_name) {
                    let mut parts: Vec<&str> = Vec::new();
                    if let Some(f) = &file_scoped {
                        parts.push(f);
                    }
                    parts.extend(stack.iter().map(|(n, _)| n.as_str()));
                    return Some(parts.join("."));
                }
            }
            _ => {}
        }
        i += 1;
    }
    None
}

/// The class a script file stands for, as Unity binds it: the type named like the file, else the
/// first type the file declares (measured, 6000.0: `LayoutBoardComponentView.cs` declaring only
/// `LayoutBoardComponentView3` is that class). `(namespace, name)`; `None` when it declares none.
pub fn script_class(source: &str, file_stem: &str) -> Option<(String, String)> {
    if let Some(namespace) = declared_namespace(source, file_stem) {
        return Some((namespace, file_stem.to_string()));
    }
    let code = strip(source);
    let tokens = tokenize(&code);
    let first = tokens.windows(2).find_map(|w| {
        matches!(w[0], "class" | "struct" | "interface" | "enum" | "record")
            .then_some(w[1])
            .filter(|name| name.chars().next().is_some_and(|c| c.is_alphabetic() || c == '_'))
    })?;
    let namespace = declared_namespace(source, first)?;
    Some((namespace, first.to_string()))
}

/// The source without comments, string and character literals (their text could look like code).
fn strip(source: &str) -> String {
    let bytes = source.as_bytes();
    let mut out = String::with_capacity(source.len());
    let mut i = 0;
    while i < bytes.len() {
        let c = bytes[i];
        if c == b'/' && bytes.get(i + 1) == Some(&b'/') {
            while i < bytes.len() && bytes[i] != b'\n' {
                i += 1;
            }
        } else if c == b'/' && bytes.get(i + 1) == Some(&b'*') {
            i += 2;
            while i + 1 < bytes.len() && !(bytes[i] == b'*' && bytes[i + 1] == b'/') {
                i += 1;
            }
            i += 2;
        } else if c == b'"' || c == b'\'' {
            let verbatim = i > 0 && bytes[i - 1] == b'@';
            i += 1;
            while i < bytes.len() {
                if !verbatim && bytes[i] == b'\\' {
                    i += 2;
                    continue;
                }
                if bytes[i] == c {
                    if verbatim && bytes.get(i + 1) == Some(&c) {
                        i += 2;
                        continue;
                    }
                    break;
                }
                i += 1;
            }
            i += 1;
            out.push(' ');
        } else {
            out.push(c as char);
            i += 1;
        }
    }
    out
}

fn tokenize(code: &str) -> Vec<&str> {
    let mut tokens = Vec::new();
    let bytes = code.as_bytes();
    let mut i = 0;
    while i < bytes.len() {
        let c = bytes[i];
        if c.is_ascii_alphanumeric() || c == b'_' || c >= 0x80 {
            let start = i;
            while i < bytes.len() && (bytes[i].is_ascii_alphanumeric() || bytes[i] == b'_' || bytes[i] >= 0x80) {
                i += 1;
            }
            tokens.push(&code[start..i]);
        } else if c == b'{' || c == b'}' || c == b';' || c == b'.' {
            tokens.push(&code[i..i + 1]);
            i += 1;
        } else {
            i += 1;
        }
    }
    tokens
}

#[cfg(test)]
mod tests {
    use super::{declared_namespace, script_class};

    #[test]
    fn a_file_without_its_class_stands_for_its_first_type() {
        let source = "using X;\nnamespace A.B {\n  // class Commented {}\n  public class Layout3 : Base<T> where T : class {}\n  class Other {}\n}";
        assert_eq!(script_class(source, "Layout"), Some(("A.B".into(), "Layout3".into())));
        assert_eq!(script_class(source, "Other"), Some(("A.B".into(), "Other".into())));
        assert_eq!(script_class("// nothing", "Layout"), None);
    }

    #[test]
    fn finds_block_and_file_scoped_namespaces() {
        let block = "using X;\nnamespace Game.Managers {\n  public class CameraController : MonoBehaviour { void A() { } }\n}";
        assert_eq!(declared_namespace(block, "CameraController").as_deref(), Some("Game.Managers"));
        let file = "namespace TMPro.Examples;\npublic class CameraController {}";
        assert_eq!(declared_namespace(file, "CameraController").as_deref(), Some("TMPro.Examples"));
        let global = "public class Foo { }";
        assert_eq!(declared_namespace(global, "Foo").as_deref(), Some(""));
    }

    #[test]
    fn nested_namespaces_join_and_close() {
        let source = "namespace A { namespace B { class X {} } class Y {} }";
        assert_eq!(declared_namespace(source, "X").as_deref(), Some("A.B"));
        assert_eq!(declared_namespace(source, "Y").as_deref(), Some("A"));
    }

    #[test]
    fn ignores_comments_strings_and_other_names() {
        let source = "// class Foo\nnamespace N { string s = \"class Foo\"; /* class Foo */ class FooBar {} class Foo<T> {} }";
        assert_eq!(declared_namespace(source, "Foo").as_deref(), Some("N"));
        assert_eq!(declared_namespace("class Other {}", "Foo"), None);
    }
}
