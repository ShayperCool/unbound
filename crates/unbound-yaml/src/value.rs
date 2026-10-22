//! The tree a Unity YAML document reads into. Scalars stay text: their type is only known to the
//! reader of a field (the engine or the game's serialized class).
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub enum Value {
    #[default]
    Null,
    Scalar(String),
    Seq(Vec<Value>),
    Map(Map),
}

/// A mapping in source order. Unity mappings are small; a linear scan beats hashing.
#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct Map(pub Vec<(String, Value)>);

impl Map {
    pub fn get(&self, key: &str) -> Option<&Value> {
        self.0.iter().find(|(k, _)| k == key).map(|(_, v)| v)
    }

    pub fn get_mut(&mut self, key: &str) -> Option<&mut Value> {
        self.0.iter_mut().find(|(k, _)| k == key).map(|(_, v)| v)
    }

    /// Sets `key`, keeping its position when it exists and appending it otherwise.
    pub fn insert(&mut self, key: &str, value: Value) {
        match self.get_mut(key) {
            Some(slot) => *slot = value,
            None => self.0.push((key.to_string(), value)),
        }
    }

    pub fn remove(&mut self, key: &str) -> Option<Value> {
        let index = self.0.iter().position(|(k, _)| k == key)?;
        Some(self.0.remove(index).1)
    }

    pub fn iter(&self) -> impl Iterator<Item = (&str, &Value)> {
        self.0.iter().map(|(k, v)| (k.as_str(), v))
    }

    pub fn len(&self) -> usize {
        self.0.len()
    }

    pub fn is_empty(&self) -> bool {
        self.0.is_empty()
    }
}

/// `{fileID: 123, guid: abc, type: 3}`: a reference to an object, local when `guid` is empty.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub struct ObjectRef {
    pub file_id: i64,
    pub guid: Guid,
    pub kind: i32,
}

impl ObjectRef {
    pub fn is_null(&self) -> bool {
        self.file_id == 0
    }

    pub fn is_local(&self) -> bool {
        self.guid.is_zero()
    }
}

/// A 128-bit asset GUID as Unity writes it: 32 lowercase hex digits.
#[derive(
    Debug, Clone, Copy, PartialEq, Eq, Hash, PartialOrd, Ord, Default, Serialize, Deserialize,
)]
pub struct Guid(pub u128);

impl Guid {
    pub fn parse(text: &str) -> Option<Self> {
        let text = text.trim();
        if text.len() != 32 {
            return None;
        }
        u128::from_str_radix(text, 16).ok().map(Guid)
    }

    pub fn is_zero(&self) -> bool {
        self.0 == 0
    }
}

impl std::fmt::Display for Guid {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "{:032x}", self.0)
    }
}

impl Value {
    pub fn as_str(&self) -> Option<&str> {
        match self {
            Value::Scalar(s) => Some(s),
            _ => None,
        }
    }

    pub fn as_map(&self) -> Option<&Map> {
        match self {
            Value::Map(m) => Some(m),
            _ => None,
        }
    }

    pub fn as_map_mut(&mut self) -> Option<&mut Map> {
        match self {
            Value::Map(m) => Some(m),
            _ => None,
        }
    }

    pub fn as_seq(&self) -> Option<&[Value]> {
        match self {
            Value::Seq(s) => Some(s),
            _ => None,
        }
    }

    pub fn get(&self, key: &str) -> Option<&Value> {
        self.as_map()?.get(key)
    }

    /// Follows a dotted path of keys: `m_LocalPosition.x`.
    pub fn path(&self, path: &str) -> Option<&Value> {
        path.split('.').try_fold(self, |value, key| value.get(key))
    }

    pub fn as_i64(&self) -> Option<i64> {
        self.as_str()?.trim().parse().ok()
    }

    pub fn as_f64(&self) -> Option<f64> {
        parse_float(self.as_str()?)
    }

    pub fn as_bool(&self) -> Option<bool> {
        match self.as_str()?.trim() {
            "0" | "false" => Some(false),
            "1" | "true" => Some(true),
            _ => None,
        }
    }

    pub fn as_guid(&self) -> Option<Guid> {
        Guid::parse(self.as_str()?)
    }

    /// Reads `{fileID, guid, type}`. A missing or malformed field reads as null.
    pub fn as_ref(&self) -> Option<ObjectRef> {
        let map = self.as_map()?;
        let file_id = map.get("fileID")?.as_i64()?;
        let guid = map.get("guid").and_then(Value::as_guid).unwrap_or_default();
        let kind = map.get("type").and_then(Value::as_i64).unwrap_or(0) as i32;
        Some(ObjectRef {
            file_id,
            guid,
            kind,
        })
    }
}

/// Unity writes floats in C's `%g`-like forms, plus `Infinity`, `-Infinity` and `NaN`.
pub fn parse_float(text: &str) -> Option<f64> {
    let text = text.trim();
    match text {
        "Infinity" | "inf" => Some(f64::INFINITY),
        "-Infinity" | "-inf" => Some(f64::NEG_INFINITY),
        "NaN" | "nan" => Some(f64::NAN),
        _ => text.parse().ok(),
    }
}
