//! Unity's YAML text serialization: scenes, prefabs, assets, `.meta` files and settings.
mod parse;
mod value;

pub use value::{Guid, Map, ObjectRef, Value, parse_float};

use anyhow::{Context, Result};
use rayon::prelude::*;

/// One `--- !u!<classID> &<fileID>` document.
#[derive(Debug, Clone, PartialEq)]
pub struct Document {
    pub class_id: i32,
    pub file_id: i64,
    /// A prefab instance's object listed only so that others can reference it.
    pub stripped: bool,
    /// `GameObject`, `MonoBehaviour`, ...: the single top-level key.
    pub type_name: String,
    pub body: Value,
}

/// Parses a plain YAML block (a `.meta` file, `manifest`-like text without document headers).
pub fn parse_value(text: &str) -> Result<Value> {
    let mut parser = parse::Parser::new(text, 0);
    parser.parse_block(0)
}

/// Parses a tagged multi-document file. Documents are parsed in parallel.
pub fn parse_documents(text: &str) -> Result<Vec<Document>> {
    let mut chunks = Vec::new();
    let mut offset = 0;
    let mut line = 0;
    let mut header: Option<(&str, usize, usize)> = None;
    for raw in text.split_inclusive('\n') {
        if raw.starts_with("--- ") {
            if let Some((h, start, first_line)) = header {
                chunks.push((h, &text[start..offset], first_line));
            }
            header = Some((raw.trim_end(), offset + raw.len(), line + 1));
        }
        offset += raw.len();
        line += 1;
    }
    if let Some((h, start, first_line)) = header {
        chunks.push((h, &text[start..], first_line));
    }
    chunks
        .into_par_iter()
        .map(|(header, body, first_line)| {
            parse_document(header, body, first_line)
                .with_context(|| format!("in document `{header}` (line {first_line})"))
        })
        .collect()
}

fn parse_document(header: &str, body: &str, first_line: usize) -> Result<Document> {
    // `--- !u!1 &123 stripped`
    let mut class_id = 0;
    let mut file_id = 0;
    let mut stripped = false;
    for part in header.split_whitespace().skip(1) {
        if let Some(id) = part.strip_prefix("!u!") {
            class_id = id.parse().context("class id")?;
        } else if let Some(id) = part.strip_prefix('&') {
            file_id = id.parse().context("file id")?;
        } else if part == "stripped" {
            stripped = true;
        }
    }
    let mut parser = parse::Parser::new(body, first_line);
    let value = parser.parse_block(0)?;
    let (type_name, body) = match value {
        Value::Map(mut map) if !map.is_empty() => map.0.swap_remove(0),
        _ => (String::new(), Value::Null),
    };
    Ok(Document {
        class_id,
        file_id,
        stripped,
        type_name,
        body,
    })
}

#[cfg(test)]
mod tests;
