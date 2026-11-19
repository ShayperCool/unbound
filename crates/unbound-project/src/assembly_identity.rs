//! The name and version a managed DLL declares (its `Assembly` metadata row, ECMA-335 II.22.2),
//! read from the PE image without loading it: the Editor tells precompiled plugins apart by them.
use std::io::{Read, Seek, SeekFrom};
use std::path::Path;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AssemblyIdentity {
    pub name: String,
    /// Major, minor, build, revision.
    pub version: [u16; 4],
}

impl AssemblyIdentity {
    /// `Name, Version=1.2.3.4`, as the Editor prints an assembly name (without culture and key).
    pub fn version_text(&self) -> String {
        let [a, b, c, d] = self.version;
        format!("{a}.{b}.{c}.{d}")
    }
}

/// The identity of the assembly in `file`; `None` for a file that is not a managed assembly or
/// whose metadata cannot be read.
pub fn read(file: &Path) -> Option<AssemblyIdentity> {
    let mut f = std::fs::File::open(file).ok()?;
    let mut at = |offset: u64, len: usize| -> Option<Vec<u8>> {
        let mut buf = vec![0; len];
        f.seek(SeekFrom::Start(offset)).ok()?;
        f.read_exact(&mut buf).ok()?;
        Some(buf)
    };
    let dos = at(0, 64)?;
    if &dos[..2] != b"MZ" {
        return None;
    }
    let pe = u32_at(&dos, 60)? as u64;
    let coff = at(pe, 24)?;
    if &coff[..4] != b"PE\0\0" {
        return None;
    }
    let sections = u16_at(&coff, 6)? as usize;
    let optional_size = u16_at(&coff, 20)? as usize;
    let optional = at(pe + 24, optional_size)?;
    // The data directories start at 96 (PE32) or 112 (PE32+) bytes into the optional header; the
    // CLI header's is the 15th.
    let directories = match u16_at(&optional, 0)? {
        0x10b => 96,
        0x20b => 112,
        _ => return None,
    };
    let cli_rva = u32_at(&optional, directories + 14 * 8)?;
    if cli_rva == 0 {
        return None;
    }
    let table = at(pe + 24 + optional_size as u64, sections * 40)?;
    let to_offset = |rva: u32| -> Option<u64> {
        (0..sections).find_map(|i| {
            let s = &table[i * 40..i * 40 + 40];
            let (size, address, raw) = (u32_at(s, 8)?, u32_at(s, 12)?, u32_at(s, 20)?);
            (rva >= address && rva < address + size).then(|| (raw + (rva - address)) as u64)
        })
    };
    let cli = at(to_offset(cli_rva)?, 72)?;
    let (metadata_rva, metadata_size) = (u32_at(&cli, 8)?, u32_at(&cli, 12)?);
    let metadata = at(to_offset(metadata_rva)?, metadata_size as usize)?;
    parse_metadata(&metadata)
}

fn u16_at(b: &[u8], at: usize) -> Option<u16> {
    Some(u16::from_le_bytes(b.get(at..at + 2)?.try_into().ok()?))
}

fn u32_at(b: &[u8], at: usize) -> Option<u32> {
    Some(u32::from_le_bytes(b.get(at..at + 4)?.try_into().ok()?))
}

/// What a table's row is made of, for skipping the tables before `Assembly`.
#[derive(Clone, Copy)]
enum Column {
    Fixed(usize),
    String,
    Guid,
    Blob,
    /// An index into one table.
    Table(usize),
    /// A coded index over these tables (ECMA-335 II.24.2.6).
    Coded(&'static [usize]),
}

const TYPE_DEF_OR_REF: &[usize] = &[0x02, 0x01, 0x1B];
const HAS_CONSTANT: &[usize] = &[0x04, 0x08, 0x17];
const HAS_CUSTOM_ATTRIBUTE: &[usize] = &[
    0x06, 0x04, 0x01, 0x02, 0x08, 0x09, 0x0A, 0x00, 0x0E, 0x17, 0x14, 0x11, 0x1A, 0x1B, 0x20, 0x23,
    0x26, 0x27, 0x28, 0x2A, 0x2C, 0x2B,
];
const HAS_FIELD_MARSHAL: &[usize] = &[0x04, 0x08];
const HAS_DECL_SECURITY: &[usize] = &[0x02, 0x06, 0x20];
const MEMBER_REF_PARENT: &[usize] = &[0x02, 0x01, 0x1A, 0x06, 0x1B];
const HAS_SEMANTICS: &[usize] = &[0x14, 0x17];
const METHOD_DEF_OR_REF: &[usize] = &[0x06, 0x0A];
const MEMBER_FORWARDED: &[usize] = &[0x04, 0x06];
const RESOLUTION_SCOPE: &[usize] = &[0x00, 0x1A, 0x23, 0x01];
// The custom attribute type's coded index has 3 tag bits, of which MethodDef and MemberRef are used.
const CUSTOM_ATTRIBUTE_TYPE: &[usize] = &[0x3F, 0x3F, 0x06, 0x0A, 0x3F];

/// The rows of the tables before `Assembly` (0x20), in table order.
fn schema(table: usize) -> &'static [Column] {
    use Column::*;
    match table {
        0x00 => &[Fixed(2), String, Guid, Guid, Guid],
        0x01 => &[Coded(RESOLUTION_SCOPE), String, String],
        0x02 => &[
            Fixed(4),
            String,
            String,
            Coded(TYPE_DEF_OR_REF),
            Table(0x04),
            Table(0x06),
        ],
        0x03 => &[Table(0x04)],
        0x04 => &[Fixed(2), String, Blob],
        0x05 => &[Table(0x06)],
        0x06 => &[Fixed(4), Fixed(2), Fixed(2), String, Blob, Table(0x08)],
        0x07 => &[Table(0x08)],
        0x08 => &[Fixed(2), Fixed(2), String],
        0x09 => &[Table(0x02), Coded(TYPE_DEF_OR_REF)],
        0x0A => &[Coded(MEMBER_REF_PARENT), String, Blob],
        0x0B => &[Fixed(2), Coded(HAS_CONSTANT), Blob],
        0x0C => &[
            Coded(HAS_CUSTOM_ATTRIBUTE),
            Coded(CUSTOM_ATTRIBUTE_TYPE),
            Blob,
        ],
        0x0D => &[Coded(HAS_FIELD_MARSHAL), Blob],
        0x0E => &[Fixed(2), Coded(HAS_DECL_SECURITY), Blob],
        0x0F => &[Fixed(2), Fixed(4), Table(0x02)],
        0x10 => &[Fixed(4), Table(0x04)],
        0x11 => &[Blob],
        0x12 => &[Table(0x02), Table(0x14)],
        0x13 => &[Table(0x14)],
        0x14 => &[Fixed(2), String, Coded(TYPE_DEF_OR_REF)],
        0x15 => &[Table(0x02), Table(0x17)],
        0x16 => &[Table(0x17)],
        0x17 => &[Fixed(2), String, Blob],
        0x18 => &[Fixed(2), Table(0x06), Coded(HAS_SEMANTICS)],
        0x19 => &[
            Table(0x02),
            Coded(METHOD_DEF_OR_REF),
            Coded(METHOD_DEF_OR_REF),
        ],
        0x1A => &[String],
        0x1B => &[Blob],
        0x1C => &[Fixed(2), Coded(MEMBER_FORWARDED), String, Table(0x1A)],
        0x1D => &[Fixed(4), Table(0x04)],
        0x1E => &[Fixed(4), Fixed(4)],
        0x1F => &[Fixed(4)],
        _ => &[],
    }
}

const ASSEMBLY: usize = 0x20;

fn parse_metadata(md: &[u8]) -> Option<AssemblyIdentity> {
    if u32_at(md, 0)? != 0x424A_5342 {
        return None;
    }
    let version_len = u32_at(md, 12)? as usize;
    let mut at = 16 + version_len;
    let streams = u16_at(md, at + 2)? as usize;
    at += 4;
    let (mut tables, mut strings) = (None, None);
    for _ in 0..streams {
        let (offset, size) = (u32_at(md, at)? as usize, u32_at(md, at + 4)? as usize);
        let name_start = at + 8;
        let name_end = name_start + md.get(name_start..)?.iter().position(|&c| c == 0)?;
        let name = &md[name_start..name_end];
        let range = md.get(offset..offset + size)?;
        match name {
            b"#~" | b"#-" => tables = Some(range),
            b"#Strings" => strings = Some(range),
            _ => {}
        }
        // The name is padded to a multiple of four bytes, its terminator included.
        at = name_start + (name_end - name_start + 4) / 4 * 4;
    }
    let (tables, strings) = (tables?, strings?);
    let heap_sizes = *tables.get(6)?;
    let string_size = if heap_sizes & 1 != 0 { 4 } else { 2 };
    let guid_size = if heap_sizes & 2 != 0 { 4 } else { 2 };
    let blob_size = if heap_sizes & 4 != 0 { 4 } else { 2 };
    let valid = u64::from_le_bytes(tables.get(8..16)?.try_into().ok()?);
    if valid & (1 << ASSEMBLY) == 0 {
        return None;
    }
    let mut rows = [0u32; 64];
    let mut at = 24;
    for (table, count) in rows.iter_mut().enumerate() {
        if valid & (1 << table) != 0 {
            *count = u32_at(tables, at)?;
            at += 4;
        }
    }
    let index_size = |table: usize| {
        if rows.get(table).copied().unwrap_or(0) < 1 << 16 {
            2
        } else {
            4
        }
    };
    let column_size = |column: Column| match column {
        Column::Fixed(n) => n,
        Column::String => string_size,
        Column::Guid => guid_size,
        Column::Blob => blob_size,
        Column::Table(t) => index_size(t),
        Column::Coded(targets) => {
            let tag_bits = usize::BITS - (targets.len() - 1).leading_zeros();
            let largest = targets
                .iter()
                .map(|&t| rows.get(t).copied().unwrap_or(0))
                .max()
                .unwrap_or(0);
            if largest < 1 << (16 - tag_bits) { 2 } else { 4 }
        }
    };
    for table in 0..ASSEMBLY {
        let row: usize = schema(table).iter().map(|&c| column_size(c)).sum();
        at += row * rows[table] as usize;
    }
    // HashAlgId (4), the four version parts (2 each), Flags (4), PublicKey, Name, Culture.
    let row = tables.get(at..)?;
    let version = [
        u16_at(row, 4)?,
        u16_at(row, 6)?,
        u16_at(row, 8)?,
        u16_at(row, 10)?,
    ];
    let name_at = 16 + blob_size;
    let name_index = if string_size == 4 {
        u32_at(row, name_at)? as usize
    } else {
        u16_at(row, name_at)? as usize
    };
    let name = strings.get(name_index..)?;
    let name = &name[..name.iter().position(|&c| c == 0)?];
    Some(AssemblyIdentity {
        name: String::from_utf8_lossy(name).into_owned(),
        version,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    /// The probe's plugins: two versions of one assembly, built by its build_assets.py.
    #[test]
    fn reads_the_declared_name_and_version() {
        let probe = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../tests/projects/compilesetup");
        let v1 = read(&probe.join("Assets/Plugins/Dup/ProbeDup.dll")).unwrap();
        let v2 = read(&probe.join("Packages/com.probe.setup/Plugins/ProbeDup.dll")).unwrap();
        assert_eq!(
            v1,
            AssemblyIdentity {
                name: "ProbeDup".into(),
                version: [1, 0, 0, 0]
            }
        );
        assert_eq!(v2.version, [2, 0, 0, 0]);
        assert_eq!(v2.version_text(), "2.0.0.0");
        assert_eq!(
            read(&probe.join("Assets/Plugins/Dup/ProbeDup.dll.meta")),
            None
        );
    }
}
