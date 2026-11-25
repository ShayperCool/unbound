//! The table container SpeedTree 8 (`.st`) and 9 (`.st9`) files share after their signature (as
//! `Unbound.SpeedTree.SpeedTreeTables` in the managed engine reads it for the trees' import):
//! a table is a uint32 count and that many uint32 offsets, relative to the table, to its
//! entries; a string is a uint32 length counting a terminating zero, then its bytes;
//! little-endian. Every read outside the bytes is `None`.

pub(crate) fn uint(bytes: &[u8], at: usize) -> Option<u32> {
    let b = bytes.get(at..at.checked_add(4)?)?;
    Some(u32::from_le_bytes([b[0], b[1], b[2], b[3]]))
}

/// The absolute offset of entry `index` of the table at `table`.
pub(crate) fn entry(bytes: &[u8], table: usize, index: usize) -> Option<usize> {
    let offset = uint(
        bytes,
        table.checked_add(index.checked_add(1)?.checked_mul(4)?)?,
    )?;
    table.checked_add(offset as usize)
}

/// The count at `at` of items of `size` bytes that follow it, which must fit in the file.
pub(crate) fn count(bytes: &[u8], at: usize, size: usize) -> Option<usize> {
    let n = uint(bytes, at)? as usize;
    let end = at.checked_add(4)?.checked_add(n.checked_mul(size)?)?;
    (end <= bytes.len()).then_some(n)
}

pub(crate) fn string(bytes: &[u8], at: usize) -> Option<String> {
    let length = count(bytes, at, 1)?;
    if length <= 1 {
        return Some(String::new());
    }
    Some(String::from_utf8_lossy(&bytes[at + 4..at + 4 + length - 1]).into_owned())
}
