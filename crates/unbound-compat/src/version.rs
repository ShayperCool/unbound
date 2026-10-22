//! Unity release numbers: `6000.0.71f1`, `2022.3.10f1`, `2023.2.0b5`.
use std::{cmp::Ordering, fmt, str::FromStr};

/// The stage of a release, in the order Unity ships them.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub enum Stage {
    Alpha,
    Beta,
    Final,
    Patch,
    Experimental,
}

impl Stage {
    fn letter(self) -> char {
        match self {
            Stage::Alpha => 'a',
            Stage::Beta => 'b',
            Stage::Final => 'f',
            Stage::Patch => 'p',
            Stage::Experimental => 'x',
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct UnityVersion {
    pub major: u32,
    pub minor: u32,
    pub patch: u32,
    pub stage: Stage,
    pub build: u32,
}

impl UnityVersion {
    pub const fn new(major: u32, minor: u32, patch: u32) -> Self {
        Self {
            major,
            minor,
            patch,
            stage: Stage::Final,
            build: 1,
        }
    }

    /// `6000.0` — the family a compatibility profile covers.
    pub fn family(&self) -> (u32, u32) {
        (self.major, self.minor)
    }

    /// Reads `m_EditorVersion` from a project's `ProjectSettings/ProjectVersion.txt`.
    pub fn from_project_version_txt(text: &str) -> anyhow::Result<Self> {
        for line in text.lines() {
            if let Some(rest) = line.strip_prefix("m_EditorVersion:") {
                return rest.trim().parse();
            }
        }
        anyhow::bail!("ProjectVersion.txt has no m_EditorVersion")
    }
}

impl FromStr for UnityVersion {
    type Err = anyhow::Error;

    fn from_str(s: &str) -> anyhow::Result<Self> {
        let s = s.trim();
        let mut parts = s.splitn(3, '.');
        let major = parts.next().unwrap_or_default().parse()?;
        let minor = parts
            .next()
            .ok_or_else(|| anyhow::anyhow!("no minor version in `{s}`"))?
            .parse()?;
        let rest = parts.next().unwrap_or("0f1");
        let split = rest
            .find(|c: char| c.is_ascii_alphabetic())
            .unwrap_or(rest.len());
        let patch = rest[..split].parse()?;
        let (stage, build) = if split < rest.len() {
            let stage = match &rest[split..split + 1] {
                "a" => Stage::Alpha,
                "b" => Stage::Beta,
                "f" | "c" => Stage::Final,
                "p" => Stage::Patch,
                "x" => Stage::Experimental,
                other => anyhow::bail!("unknown release stage `{other}` in `{s}`"),
            };
            let digits: String = rest[split + 1..]
                .chars()
                .take_while(char::is_ascii_digit)
                .collect();
            (stage, digits.parse().unwrap_or(1))
        } else {
            (Stage::Final, 1)
        };
        Ok(Self {
            major,
            minor,
            patch,
            stage,
            build,
        })
    }
}

impl fmt::Display for UnityVersion {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(
            f,
            "{}.{}.{}{}{}",
            self.major,
            self.minor,
            self.patch,
            self.stage.letter(),
            self.build
        )
    }
}

impl PartialOrd for UnityVersion {
    fn partial_cmp(&self, other: &Self) -> Option<Ordering> {
        Some(self.cmp(other))
    }
}

impl Ord for UnityVersion {
    fn cmp(&self, other: &Self) -> Ordering {
        (self.major, self.minor, self.patch, self.stage, self.build).cmp(&(
            other.major,
            other.minor,
            other.patch,
            other.stage,
            other.build,
        ))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_and_orders() {
        let a: UnityVersion = "6000.0.71f1".parse().unwrap();
        assert_eq!(a.to_string(), "6000.0.71f1");
        let b: UnityVersion = "2022.3.10f1".parse().unwrap();
        let c: UnityVersion = "6000.0.71b2".parse().unwrap();
        assert!(b < a);
        assert!(c < a);
        let text = "m_EditorVersion: 6000.0.71f1\nm_EditorVersionWithRevision: 6000.0.71f1 (907bc2d768b5)\n";
        assert_eq!(UnityVersion::from_project_version_txt(text).unwrap(), a);
    }
}
