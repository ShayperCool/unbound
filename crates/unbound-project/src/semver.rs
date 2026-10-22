//! Version expressions of `versionDefines`: `1.2.3` (at least), `[1.0,2.0)`, `(,3.0]`, `[1.5]`.
use std::cmp::Ordering;

/// A package or Unity version reduced to comparable parts. Pre-release tags sort before the
/// release, as in semver; Unity's `6000.0.71f1` compares by its numbers and stage letter.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Version {
    numbers: Vec<u64>,
    pre: Option<String>,
}

impl Version {
    pub fn parse(text: &str) -> Option<Self> {
        let text = text.trim();
        if text.is_empty() {
            return None;
        }
        let (core, pre) = match text.split_once('-') {
            Some((core, pre)) => (core, Some(pre.to_string())),
            None => (text, None),
        };
        let mut numbers = Vec::new();
        for part in core.split('.') {
            // `71f1` → 71, 'f' (as 102), 1: keeps Unity releases ordered.
            let digits: String = part.chars().take_while(char::is_ascii_digit).collect();
            numbers.push(digits.parse().ok()?);
            let rest = &part[digits.len()..];
            if let Some(stage) = rest.chars().next() {
                numbers.push(stage as u64);
                numbers.push(rest[1..].parse().unwrap_or(0));
            }
        }
        Some(Self { numbers, pre })
    }
}

impl PartialOrd for Version {
    fn partial_cmp(&self, other: &Self) -> Option<Ordering> {
        Some(self.cmp(other))
    }
}

impl Ord for Version {
    fn cmp(&self, other: &Self) -> Ordering {
        let len = self.numbers.len().max(other.numbers.len());
        for i in 0..len {
            let a = self.numbers.get(i).copied().unwrap_or(0);
            let b = other.numbers.get(i).copied().unwrap_or(0);
            match a.cmp(&b) {
                Ordering::Equal => {}
                unequal => return unequal,
            }
        }
        match (&self.pre, &other.pre) {
            (None, None) => Ordering::Equal,
            (None, Some(_)) => Ordering::Greater,
            (Some(_), None) => Ordering::Less,
            (Some(a), Some(b)) => a.cmp(b),
        }
    }
}

/// Whether `version` satisfies a `versionDefines` expression. An empty expression matches any
/// version.
pub fn matches(expression: &str, version: &str) -> bool {
    let Some(version) = Version::parse(version) else {
        return false;
    };
    let expression = expression.trim();
    if expression.is_empty() {
        return true;
    }
    let first = expression.as_bytes()[0];
    if first != b'[' && first != b'(' {
        return Version::parse(expression).is_some_and(|min| version >= min);
    }
    let last = expression.as_bytes()[expression.len() - 1];
    let inner = &expression[1..expression.len() - 1];
    let (low, high) = match inner.split_once(',') {
        Some((low, high)) => (low.trim(), high.trim()),
        None => (inner.trim(), inner.trim()),
    };
    if let Some(low) = Version::parse(low) {
        let ok = if first == b'[' {
            version >= low
        } else {
            version > low
        };
        if !ok {
            return false;
        }
    }
    if let Some(high) = Version::parse(high) {
        let ok = if last == b']' {
            version <= high
        } else {
            version < high
        };
        if !ok {
            return false;
        }
    }
    true
}

#[cfg(test)]
mod tests {
    use super::matches;

    #[test]
    fn expressions() {
        assert!(matches("1.0.0", "1.2.0"));
        assert!(!matches("1.3.0", "1.2.0"));
        assert!(matches("[1.0,2.0)", "1.9.9"));
        assert!(!matches("[1.0,2.0)", "2.0.0"));
        assert!(matches("(,2.0]", "2.0.0"));
        assert!(matches("1.0.0-pre", "1.0.0"));
        assert!(!matches("1.0.0", "1.0.0-preview.3"));
        assert!(matches("2021.2", "6000.0.71f1"));
        assert!(matches("6000.0.0f1", "6000.0.71f1"));
        assert!(matches("", "1.0.0"));
    }
}
