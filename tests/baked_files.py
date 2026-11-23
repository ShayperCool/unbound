"""The files a bake writes beside its scene (its folder's `.meta`, LightingData.asset, the images
and their `.meta` files), as the probes' references keep them (`written/`): what changes with
every bake set aside, so that two bakes of a scene give the same files."""
from pathlib import Path

# The empty realtime GI archive the lighting data of a bake without realtime GI holds: its size,
# its header's and its blocks' table's (the bytes between are not set).
ARCHIVE_SIZE, ARCHIVE_HEADER, ARCHIVE_BLOCKS = 74, 50, 10


def new_guids(written):
    """The GUIDs of the assets a bake made (in their .meta files), as text."""
    return [line.split()[1] for meta in sorted(Path(written).glob('*.meta'))
            for line in meta.read_text().splitlines() if line.startswith('guid: ')]


def normalized(name, data, written):
    """A file a bake wrote as the reference keeps it: the GUIDs of the assets the bake made, new
    with every bake, as `(new)` in .meta files and as zeros where the lighting data names them;
    and the data block of the lighting data's empty realtime GI archive (the 14 bytes between its
    header and its blocks' table), whose bytes are not set (zeros in one bake, other bytes in
    another)."""
    if name.endswith('.meta'):
        return b''.join(b'guid: (new)\n' if line.startswith(b'guid: ') else line
                        for line in data.splitlines(keepends=True))
    if name == 'LightingData.asset':
        data = bytearray(data)
        for guid in new_guids(written):
            # A .meta's GUID text has each byte's low nibble first.
            raw = bytes(int(guid[2 * k + 1] + guid[2 * k], 16) for k in range(16))
            at = data.find(raw)
            while at >= 0:
                data[at:at + 16] = bytes(16)
                at = data.find(raw, at + 16)
        at = data.find(b'UnityFS\0')
        if at >= 0 and int.from_bytes(data[at + 30:at + 38], 'big') == ARCHIVE_SIZE:
            data[at + ARCHIVE_HEADER:at + ARCHIVE_SIZE - ARCHIVE_BLOCKS] = bytes(ARCHIVE_SIZE - ARCHIVE_BLOCKS - ARCHIVE_HEADER)
        return bytes(data)
    return data


def keep_written(written, target, leave_out=()):
    """Copies a capture's `written` folder to `target`, normalized, but for the names in
    `leave_out` (files the reference keeps elsewhere)."""
    written, target = Path(written), Path(target)
    if not written.is_dir():
        return
    target.mkdir(parents=True, exist_ok=True)
    for path in sorted(written.iterdir()):
        if path.name not in leave_out:
            (target/path.name).write_bytes(normalized(path.name, path.read_bytes(), written))


def written_files(folder):
    """{name: bytes} of the files a bake wrote, as kept (`folder/written`)."""
    written = Path(folder)/'written'
    return {p.name: p.read_bytes() for p in sorted(written.iterdir())} if written.is_dir() else {}
