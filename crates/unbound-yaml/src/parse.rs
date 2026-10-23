//! A reader for the YAML subset Unity writes: block mappings and sequences (sequence items at the
//! indentation of their key), flow collections that may wrap over lines, plain, single- and
//! double-quoted scalars folded over lines, and block scalars.
use crate::value::{Map, Value};
use anyhow::{Result, bail};

#[derive(Clone, Copy)]
struct Line<'a> {
    indent: usize,
    text: &'a str,
    number: usize,
}

impl Line<'_> {
    fn is_blank(&self) -> bool {
        self.text.is_empty()
    }

    fn is_seq_item(&self) -> bool {
        self.text == "-" || self.text.starts_with("- ")
    }
}

pub(crate) struct Parser<'a> {
    lines: Vec<Line<'a>>,
    pos: usize,
}

impl<'a> Parser<'a> {
    pub(crate) fn new(text: &'a str, first_line: usize) -> Self {
        let lines = text
            .split('\n')
            .enumerate()
            .filter_map(|(i, raw)| {
                let raw = raw.strip_suffix('\r').unwrap_or(raw);
                let trimmed = raw.trim_start_matches(' ');
                if trimmed.starts_with('#') {
                    return None;
                }
                let text = trimmed.trim_end();
                Some(Line {
                    indent: raw.len() - trimmed.len(),
                    text,
                    number: first_line + i,
                })
            })
            .collect();
        Self { lines, pos: 0 }
    }

    fn peek_content(&mut self) -> Option<Line<'a>> {
        while let Some(line) = self.lines.get(self.pos) {
            if !line.is_blank() {
                return Some(*line);
            }
            self.pos += 1;
        }
        None
    }

    /// The block at `min_indent` or deeper; `Null` when there is none.
    pub(crate) fn parse_block(&mut self, min_indent: usize) -> Result<Value> {
        let Some(line) = self.peek_content() else {
            return Ok(Value::Null);
        };
        if line.indent < min_indent {
            return Ok(Value::Null);
        }
        if line.text.starts_with('{') || line.text.starts_with('[') {
            // A flow collection standing alone (a document that is just `{...}`).
            self.pos += 1;
            return self.parse_inline(line.text, line.indent.saturating_sub(1), line.number);
        }
        if line.is_seq_item() {
            self.parse_seq(line.indent)
        } else {
            self.parse_map(line.indent)
        }
    }

    fn parse_map(&mut self, indent: usize) -> Result<Value> {
        let mut map = Map::default();
        while let Some(line) = self.peek_content() {
            if line.indent != indent || line.is_seq_item() {
                if line.indent > indent {
                    bail!("line {}: unexpected indentation", line.number + 1);
                }
                break;
            }
            let Some((key, rest)) = split_key(line.text) else {
                bail!(
                    "line {}: expected `key:` in `{}`",
                    line.number + 1,
                    line.text
                );
            };
            self.pos += 1;
            let value = if rest.is_empty() {
                match self.peek_content() {
                    Some(next) if next.indent > indent => self.parse_block(next.indent)?,
                    Some(next) if next.indent == indent && next.is_seq_item() => {
                        self.parse_seq(indent)?
                    }
                    _ => Value::Null,
                }
            } else {
                self.parse_inline(rest, indent, line.number)?
            };
            map.0.push((key, value));
        }
        Ok(Value::Map(map))
    }

    fn parse_seq(&mut self, indent: usize) -> Result<Value> {
        let mut items = Vec::new();
        while let Some(line) = self.peek_content() {
            if line.indent != indent || !line.is_seq_item() {
                break;
            }
            let content = line.text[1..].trim_start_matches(' ');
            let column = indent + (line.text.len() - content.len());
            let item = if content.is_empty() {
                self.pos += 1;
                match self.peek_content() {
                    Some(next) if next.indent > indent => self.parse_block(next.indent)?,
                    _ => Value::Null,
                }
            } else if content == "-" || content.starts_with("- ") || starts_block_map(content) {
                // `- key: value` and `- - item`: the rest of the line opens a block at its column.
                self.lines[self.pos] = Line {
                    indent: column,
                    text: content,
                    number: line.number,
                };
                self.parse_block(column)?
            } else {
                self.pos += 1;
                self.parse_inline(content, indent, line.number)?
            };
            items.push(item);
        }
        Ok(Value::Seq(items))
    }

    /// A value that starts on the line of its key or `-`; continuation lines are deeper than
    /// `parent_indent`.
    fn parse_inline(
        &mut self,
        rest: &'a str,
        parent_indent: usize,
        number: usize,
    ) -> Result<Value> {
        match rest.as_bytes()[0] {
            b'{' | b'[' => {
                let mut text = rest.to_string();
                while !flow_balanced(&text) {
                    let Some(next) = self.lines.get(self.pos) else {
                        bail!("line {}: unterminated flow collection", number + 1);
                    };
                    text.push(' ');
                    text.push_str(next.text);
                    self.pos += 1;
                }
                let mut flow = Flow {
                    chars: text.as_bytes(),
                    pos: 0,
                    text: &text,
                };
                let value = flow.value()?;
                Ok(value)
            }
            b'\'' | b'"' => {
                let quote = rest.as_bytes()[0];
                let mut text = rest.to_string();
                while !quoted_closed(&text, quote) {
                    let Some(next) = self.lines.get(self.pos) else {
                        bail!("line {}: unterminated quoted scalar", number + 1);
                    };
                    text.push('\n');
                    text.push_str(next.text);
                    self.pos += 1;
                }
                let (value, _) = read_quoted(&text, 0, quote)?;
                Ok(Value::Scalar(value))
            }
            b'|' | b'>' => Ok(Value::Scalar(self.block_scalar(rest, parent_indent))),
            _ => {
                let mut value = rest.to_string();
                let mut pending_breaks = 0;
                let mut lookahead = self.pos;
                while let Some(next) = self.lines.get(lookahead) {
                    if next.is_blank() {
                        pending_breaks += 1;
                        lookahead += 1;
                        continue;
                    }
                    if next.indent <= parent_indent {
                        break;
                    }
                    if pending_breaks > 0 {
                        value.extend(std::iter::repeat_n('\n', pending_breaks));
                    } else {
                        value.push(' ');
                    }
                    value.push_str(next.text);
                    pending_breaks = 0;
                    lookahead += 1;
                    self.pos = lookahead;
                }
                Ok(scalar_or_null(value))
            }
        }
    }

    fn block_scalar(&mut self, header: &str, parent_indent: usize) -> String {
        let literal = header.starts_with('|');
        let keep = header.contains('+');
        let strip = header.contains('-');
        let mut lines = Vec::new();
        let mut content_indent = None;
        while let Some(next) = self.lines.get(self.pos) {
            if !next.is_blank() && next.indent <= parent_indent {
                break;
            }
            if !next.is_blank() {
                content_indent.get_or_insert(next.indent);
            }
            lines.push(*next);
            self.pos += 1;
        }
        let base = content_indent.unwrap_or(parent_indent + 1);
        let mut out = String::new();
        for (i, line) in lines.iter().enumerate() {
            let text = if line.is_blank() {
                String::new()
            } else {
                format!(
                    "{}{}",
                    " ".repeat(line.indent.saturating_sub(base)),
                    line.text
                )
            };
            if i > 0 {
                let previous_blank = lines[i - 1].is_blank();
                out.push(if literal || previous_blank || line.is_blank() {
                    '\n'
                } else {
                    ' '
                });
            }
            out.push_str(&text);
        }
        if strip {
            out.truncate(out.trim_end_matches('\n').len());
        } else if !keep {
            out.truncate(out.trim_end_matches('\n').len());
            out.push('\n');
        }
        out
    }
}

fn scalar_or_null(text: String) -> Value {
    match text.as_str() {
        "~" | "null" => Value::Null,
        _ => Value::Scalar(text),
    }
}

/// `key: value` or `key:`; quoted keys are unquoted. Plain scalars on a mapping line never start
/// with a flow or quote character, so the first `: ` outside quotes ends the key.
fn split_key(text: &str) -> Option<(String, &str)> {
    let bytes = text.as_bytes();
    if bytes[0] == b'\'' || bytes[0] == b'"' {
        let (key, end) = read_quoted(text, 0, bytes[0]).ok()?;
        let rest = text[end..].trim_start();
        let rest = rest.strip_prefix(':')?;
        return Some((key, rest.trim_start()));
    }
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i] == b':' && (i + 1 == bytes.len() || bytes[i + 1] == b' ') {
            return Some((text[..i].to_string(), text[i + 1..].trim_start()));
        }
        i += 1;
    }
    None
}

fn starts_block_map(content: &str) -> bool {
    match content.as_bytes()[0] {
        b'{' | b'[' | b'|' | b'>' => false,
        b'\'' | b'"' => {
            let quote = content.as_bytes()[0];
            match read_quoted(content, 0, quote) {
                Ok((_, end)) => content[end..].trim_start().starts_with(':'),
                Err(_) => false,
            }
        }
        _ => split_key(content).is_some(),
    }
}

fn flow_balanced(text: &str) -> bool {
    let mut depth = 0i32;
    let mut quote: Option<u8> = None;
    let bytes = text.as_bytes();
    let mut i = 0;
    while i < bytes.len() {
        let c = bytes[i];
        match quote {
            Some(b'"') if c == b'\\' => i += 1,
            Some(q) if c == q => {
                if q == b'\'' && bytes.get(i + 1) == Some(&b'\'') {
                    i += 1;
                } else {
                    quote = None;
                }
            }
            Some(_) => {}
            None => match c {
                b'{' | b'[' => depth += 1,
                b'}' | b']' => depth -= 1,
                b'\'' | b'"' => quote = Some(c),
                _ => {}
            },
        }
        i += 1;
    }
    depth <= 0 && quote.is_none()
}

fn quoted_closed(text: &str, quote: u8) -> bool {
    read_quoted(text, 0, quote).is_ok()
}

/// Reads the quoted scalar starting at `start`; returns its value and the index after it.
/// Line breaks inside fold as YAML folds them: one break is a space, each blank line a newline.
fn read_quoted(text: &str, start: usize, quote: u8) -> Result<(String, usize)> {
    let bytes = text.as_bytes();
    let mut out = String::new();
    let mut i = start + 1;
    let mut segment_start = i;
    while i < bytes.len() {
        let c = bytes[i];
        if c == quote {
            if quote == b'\'' && bytes.get(i + 1) == Some(&b'\'') {
                out.push_str(&text[segment_start..i]);
                out.push('\'');
                i += 2;
                segment_start = i;
                continue;
            }
            out.push_str(&text[segment_start..i]);
            return Ok((out, i + 1));
        }
        if c == b'\n' {
            out.push_str(text[segment_start..i].trim_end_matches(' '));
            // Count the blank lines that follow.
            let mut breaks = 0;
            let mut j = i + 1;
            loop {
                while j < bytes.len() && bytes[j] == b' ' {
                    j += 1;
                }
                if j < bytes.len() && bytes[j] == b'\n' {
                    breaks += 1;
                    j += 1;
                } else {
                    break;
                }
            }
            if breaks == 0 {
                out.push(' ');
            } else {
                out.extend(std::iter::repeat_n('\n', breaks));
            }
            i = j;
            segment_start = i;
            continue;
        }
        if quote == b'"' && c == b'\\' {
            out.push_str(&text[segment_start..i]);
            let Some(&e) = bytes.get(i + 1) else {
                bail!("dangling escape");
            };
            i += 2;
            match e {
                b'n' => out.push('\n'),
                b't' | b'\t' => out.push('\t'),
                b'r' => out.push('\r'),
                b'0' => out.push('\0'),
                b'a' => out.push('\x07'),
                b'b' => out.push('\x08'),
                b'e' => out.push('\x1b'),
                b'f' => out.push('\x0c'),
                b'v' => out.push('\x0b'),
                b' ' => out.push(' '),
                b'/' => out.push('/'),
                b'"' => out.push('"'),
                b'\\' => out.push('\\'),
                b'N' => out.push('\u{85}'),
                b'_' => out.push('\u{a0}'),
                b'L' => out.push('\u{2028}'),
                b'P' => out.push('\u{2029}'),
                b'x' | b'u' | b'U' => {
                    let len = match e {
                        b'x' => 2,
                        b'u' => 4,
                        _ => 8,
                    };
                    let hex = text.get(i..i + len).unwrap_or_default();
                    let code = u32::from_str_radix(hex, 16)?;
                    out.push(char::from_u32(code).unwrap_or('\u{fffd}'));
                    i += len;
                }
                b'\n' => {
                    // An escaped line break joins the lines without a space.
                    while i < bytes.len() && bytes[i] == b' ' {
                        i += 1;
                    }
                }
                other => {
                    out.push('\\');
                    out.push(other as char);
                }
            }
            segment_start = i;
            continue;
        }
        i += 1;
    }
    bail!("unterminated quoted scalar")
}

struct Flow<'t> {
    chars: &'t [u8],
    pos: usize,
    text: &'t str,
}

impl Flow<'_> {
    fn skip_ws(&mut self) {
        while self.pos < self.chars.len() && matches!(self.chars[self.pos], b' ' | b'\n' | b'\t') {
            self.pos += 1;
        }
    }

    fn value(&mut self) -> Result<Value> {
        self.skip_ws();
        match self.chars.get(self.pos) {
            Some(b'{') => {
                self.pos += 1;
                let mut map = Map::default();
                loop {
                    self.skip_ws();
                    match self.chars.get(self.pos) {
                        Some(b'}') => {
                            self.pos += 1;
                            break;
                        }
                        Some(b',') => {
                            self.pos += 1;
                            continue;
                        }
                        None => bail!("unterminated flow mapping"),
                        _ => {}
                    }
                    let key = match self.value()? {
                        Value::Scalar(s) => s,
                        Value::Null => String::new(),
                        _ => bail!("flow mapping key is a collection"),
                    };
                    self.skip_ws();
                    let value = if self.chars.get(self.pos) == Some(&b':') {
                        self.pos += 1;
                        self.value()?
                    } else {
                        Value::Null
                    };
                    map.0.push((key, value));
                }
                Ok(Value::Map(map))
            }
            Some(b'[') => {
                self.pos += 1;
                let mut items = Vec::new();
                loop {
                    self.skip_ws();
                    match self.chars.get(self.pos) {
                        Some(b']') => {
                            self.pos += 1;
                            break;
                        }
                        Some(b',') => {
                            self.pos += 1;
                            continue;
                        }
                        None => bail!("unterminated flow sequence"),
                        _ => items.push(self.value()?),
                    }
                }
                Ok(Value::Seq(items))
            }
            Some(&q @ (b'\'' | b'"')) => {
                let (value, end) = read_quoted(self.text, self.pos, q)?;
                self.pos = end;
                Ok(Value::Scalar(value))
            }
            _ => {
                let start = self.pos;
                while self.pos < self.chars.len() {
                    match self.chars[self.pos] {
                        b',' | b'}' | b']' => break,
                        b':' if matches!(
                            self.chars.get(self.pos + 1),
                            None | Some(b' ' | b',' | b'}' | b']')
                        ) =>
                        {
                            break;
                        }
                        _ => self.pos += 1,
                    }
                }
                let text = self.text[start..self.pos].trim();
                Ok(if text.is_empty() {
                    Value::Null
                } else {
                    scalar_or_null(text.to_string())
                })
            }
        }
    }
}
