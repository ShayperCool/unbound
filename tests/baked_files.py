"""The files a bake writes beside its scene (its folder's `.meta`, LightingData.asset, the images
and their `.meta` files), as the probes' references keep them (`written/`): what changes with
every bake set aside, so that two bakes of a scene give the same files."""
import json
from pathlib import Path
import subprocess

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


def keep_written(written, target):
    """Copies a capture's `written` folder to `target`, normalized."""
    written, target = Path(written), Path(target)
    if not written.is_dir():
        return
    target.mkdir(parents=True, exist_ok=True)
    for path in sorted(written.iterdir()):
        (target/path.name).write_bytes(normalized(path.name, path.read_bytes(), written))


def written_files(folder):
    """{name: bytes} of the files a bake wrote, as kept (`folder/written`)."""
    written = Path(folder)/'written'
    return {p.name: p.read_bytes() for p in sorted(written.iterdir())} if written.is_dir() else {}


def lighting_data_fields(path, written, cli):
    """A lighting data file's objects as `unbound dump` (the CLI `cli`) gives them, normalized as
    the files are: the GUIDs of the assets the bake made (in `written`'s .meta files) as
    `(new)`, the empty realtime GI archive's unset bytes as zeros."""
    dumped = subprocess.run([str(cli), 'dump', str(path)], capture_output=True, text=True, check=True).stdout
    new = set(new_guids(written))

    def norm(value, key=None):
        if isinstance(value, dict):
            return {k: norm(v, k) for k, v in value.items()}
        if isinstance(value, list):
            return [norm(v) for v in value]
        if key == 'guid' and value in new:
            return '(new)'
        if key == 'm_EnlightenData' and len(value) == 2 * ARCHIVE_SIZE:
            return value[:2 * ARCHIVE_HEADER] + '0' * 2 * (ARCHIVE_SIZE - ARCHIVE_BLOCKS - ARCHIVE_HEADER) + value[2 * (ARCHIVE_SIZE - ARCHIVE_BLOCKS):]
        return value
    return norm(json.loads(dumped))


def _by_renderer(objects):
    """The lighting data's renderer entries keyed by their renderer (the Editor lists them in an
    order that changes from bake to bake, which Unity does not specify)."""
    out = json.loads(json.dumps(objects))
    for obj in out:
        fields = obj.get('fields', {})
        ids, data = fields.pop('m_LightmappedRendererDataIDs', None), fields.pop('m_LightmappedRendererData', None)
        if ids is not None and data is not None and len(ids) == len(data):
            fields['rendererData'] = {f"{i['targetObject']}/{i['targetPrefab']}": d for i, d in zip(ids, data)}
        elif ids is not None or data is not None:
            fields['rendererData'] = {'ids': ids, 'data': data}
    return out


# Fields that identify (objects, files): compared exactly, also where they are numbers.
IDENTIFYING = {'targetObject', 'targetPrefab', 'fileID', 'm_FileID', 'm_PathID', 'guid', 'lightmapIndex', 'lightmapIndexDynamic'}


def lighting_data_difference(got, want, tolerance):
    """None, or every way two lighting data's fields (as `lighting_data_fields` gives them)
    differ: the renderer entries by renderer, identifying fields exactly, other numbers within
    `tolerance` times the value above 1, the rest exactly."""
    problems = []

    def walk(a, b, path):
        if isinstance(b, dict):
            if not isinstance(a, dict) or a.keys() != b.keys():
                problems.append(f'{path}: keys {sorted(a) if isinstance(a, dict) else a!r}, expected {sorted(b)}')
                return
            for k in b:
                walk(a[k], b[k], f'{path}/{k}')
        elif isinstance(b, list):
            if not isinstance(a, list) or len(a) != len(b):
                problems.append(f'{path}: {len(a) if isinstance(a, list) else a!r} entries, expected {len(b)}')
                return
            for k, (x, y) in enumerate(zip(a, b)):
                walk(x, y, f'{path}[{k}]')
        elif a != b and path.rsplit('/', 1)[-1] in IDENTIFYING:
            problems.append(f'{path}: {a!r}, expected {b!r}')
        elif a != b:
            try:
                x, y = float(a), float(b)
                if abs(x - y) <= tolerance * max(1.0, abs(y)):
                    return
            except (TypeError, ValueError):
                pass
            problems.append(f'{path}: {a!r}, expected {b!r}')
    walk(_by_renderer(got), _by_renderer(want), '')
    return '; '.join(problems) or None
