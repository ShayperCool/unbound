//! The binary encoding the managed engine decodes (`YBinary` in UnityEngine.CoreModule):
//!
//! ```text
//! file  := "UYB1" u32:count doc*
//! doc   := i32:classId i64:fileId u8:stripped str:typeName value
//! value := u8:0 (null) | u8:1 str | u8:2 u32:n value* | u8:3 u32:n (str value)*
//! str   := u32:length utf8
//! ```
//! Little endian.
use unbound_yaml::{Document, Value};

pub fn encode_documents(docs: &[Document]) -> Vec<u8> {
    let mut out = Vec::with_capacity(docs.len() * 256);
    out.extend_from_slice(b"UYB1");
    out.extend_from_slice(&(docs.len() as u32).to_le_bytes());
    for doc in docs {
        out.extend_from_slice(&doc.class_id.to_le_bytes());
        out.extend_from_slice(&doc.file_id.to_le_bytes());
        out.push(doc.stripped as u8);
        string(&mut out, &doc.type_name);
        value(&mut out, &doc.body);
    }
    out
}

pub fn encode_value(v: &Value) -> Vec<u8> {
    let mut out = Vec::new();
    value(&mut out, v);
    out
}

fn string(out: &mut Vec<u8>, s: &str) {
    out.extend_from_slice(&(s.len() as u32).to_le_bytes());
    out.extend_from_slice(s.as_bytes());
}

fn value(out: &mut Vec<u8>, v: &Value) {
    match v {
        Value::Null => out.push(0),
        Value::Scalar(s) => {
            out.push(1);
            string(out, s);
        }
        Value::Seq(items) => {
            out.push(2);
            out.extend_from_slice(&(items.len() as u32).to_le_bytes());
            for item in items {
                value(out, item);
            }
        }
        Value::Map(map) => {
            out.push(3);
            out.extend_from_slice(&(map.len() as u32).to_le_bytes());
            for (k, item) in map.iter() {
                string(out, k);
                value(out, item);
            }
        }
    }
}
