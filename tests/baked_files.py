"""The files a bake writes beside its scene (its folder's `.meta`, LightingData.asset, the images
and their `.meta` files), as the probes' references keep them (`written/`): what changes with
every bake set aside, so that two bakes of a scene give the same files."""
import json
from pathlib import Path
import struct
import subprocess
import zlib

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


def _cells(obj):
    """The LightProbes object's cells (its tetrahedralization's), or None for another object."""
    if obj.get('classId') != 258:
        return None
    return obj.get('fields', {}).get('m_Data', {}).get('m_Tetrahedralization', {}).get('m_Tetrahedra')


def _cell_probes(cell):
    return [int(cell[f'indices[{k}]']) for k in range(4)]


def _cell_key(probes):
    return ','.join(str(v) for v in sorted(probes))


# Where an inner cell's weights are compared: at its corners weighed so (by their probes' order).
_WEIGHED = ((0.25, 0.25, 0.25, 0.25), (0.4, 0.3, 0.2, 0.1))


def _cell_meaning(cell, cells, positions):
    """What a light probe cell means whatever the order of its corners: the cell across each of
    its faces (an inner cell's, by the face's probes) or edges (an outer cell's, its inner cell's
    as `inner`), keyed by their probes; an inner cell's weights of points among its corners,
    by probe; an outer cell's matrix (the cubic of its triangle along the hull rays, the same
    for each order of its corners)."""
    probes = _cell_probes(cell)
    neighbors = [int(cell[f'neighbors[{k}]']) for k in range(4)]
    key = lambda c: _cell_key(_cell_probes(cells[c])) if 0 <= c < len(cells) else str(c)  # noqa: E731
    across = {}
    if probes[3] >= 0:
        for k in range(4):
            across[_cell_key(probes[:k] + probes[k + 1:])] = key(neighbors[k])
        m = cell['matrix']
        p3 = positions[probes[3]]
        weights = []
        for weighed in _WEIGHED:
            # (Weights of the corners in their probes' order, so of the same point each way.)
            order = sorted(range(4), key=lambda k: probes[k])
            x = [sum(weighed[r] * positions[probes[order[r]]][a] for r in range(4)) for a in range(3)]
            d = [x[a] - p3[a] for a in range(3)]
            w = [sum(float(m[f'e{r}{a}']) * d[a] for a in range(3)) for r in range(3)]
            w.append(1.0 - sum(w))
            weights.append({str(probes[k]): w[k] for k in range(4)})
        return {'isValid': cell['isValid'], 'across': across, 'weights': weights}
    for k in range(3):
        across[_cell_key([probes[k], probes[(k + 1) % 3]])] = key(neighbors[(k + 2) % 3])
    across['inner'] = key(neighbors[3])
    return {'isValid': cell['isValid'], 'across': across, 'matrix': cell['matrix']}


def _by_cell(objects):
    """The light probes' cells keyed by their probes, each with what it means whatever the order
    of its corners (`_cell_meaning`): the order of the cells and of each cell's corners (and so
    its matrix's rows and its neighbors' numbers) is compared apart (`cell_order_difference`)."""
    out = json.loads(json.dumps(objects))
    for obj in out:
        cells = _cells(obj)
        if cells is not None:
            data = obj['fields']['m_Data']
            positions = [[float(p[a]) for a in 'xyz'] for p in data['m_Positions']]
            data['m_Tetrahedralization']['m_Tetrahedra'] = {
                _cell_key(_cell_probes(c)): _cell_meaning(c, cells, positions) for c in cells}
    return out


def cell_order_difference(got, want, tolerance):
    """None, or how the light probes' cells (of two lighting data's fields) differ in their
    order: the cells whose place, corners or neighbors differ, and of the others those whose
    matrix differs by more than `tolerance` times the value above 1."""
    g = next((c for c in map(_cells, got) if c is not None), None)
    w = next((c for c in map(_cells, want) if c is not None), None)
    if g is None or w is None or len(g) != len(w):
        return None if g == w else f'{len(g or [])} cells, expected {len(w or [])}'
    neighbors = lambda c: [int(c[f'neighbors[{k}]']) for k in range(4)]  # noqa: E731
    moved = [k for k, (a, b) in enumerate(zip(g, w)) if _cell_probes(a) != _cell_probes(b) or neighbors(a) != neighbors(b)]
    off = [k for k, (a, b) in enumerate(zip(g, w)) if k not in moved and any(
        abs(float(a['matrix'][e]) - float(b['matrix'][e])) > tolerance * max(1.0, abs(float(b['matrix'][e]))) for e in b['matrix'])]
    parts = []
    if moved:
        parts.append(f'{len(moved)} of {len(w)} cells in another place or corner order, first at {moved[:6]}')
    if off:
        parts.append(f'{len(off)} matrices of cells in place differ, first at {off[:6]}')
    return '; '.join(parts) or None


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
    walk(_by_cell(_by_renderer(got)), _by_cell(_by_renderer(want)), '')
    return '; '.join(problems) or None


def read_png(data):
    """(width, height, rows of RGBA bytes from the top) of an 8-bit RGBA PNG without interlacing
    (the images a bake writes)."""
    if data[:8] != b'\x89PNG\r\n\x1a\n':
        raise ValueError('not a PNG file')
    at, header, deflated = 8, None, b''
    while at < len(data):
        size, kind = struct.unpack('>I4s', data[at:at + 8])
        body = data[at + 8:at + 8 + size]
        if kind == b'IHDR':
            header = struct.unpack('>IIBBBBB', body)
        elif kind == b'IDAT':
            deflated += body
        at += 12 + size
    width, height, depth, colour, _, _, interlace = header
    if (depth, colour, interlace) != (8, 6, 0):
        raise ValueError(f'a PNG of depth {depth}, colour type {colour}, interlace {interlace}')
    raw, stride = zlib.decompress(deflated), width * 4
    out, above = bytearray(), bytearray(stride)
    for y in range(height):
        kind, row = raw[y * (stride + 1)], bytearray(raw[y * (stride + 1) + 1:(y + 1) * (stride + 1)])
        for k in range(stride):
            a = row[k - 4] if k >= 4 else 0
            b, c = above[k], above[k - 4] if k >= 4 else 0
            if kind == 1:
                row[k] = (row[k] + a) & 255
            elif kind == 2:
                row[k] = (row[k] + b) & 255
            elif kind == 3:
                row[k] = (row[k] + (a + b) // 2) & 255
            elif kind == 4:
                pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                row[k] = (row[k] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
            elif kind != 0:
                raise ValueError(f'PNG filter {kind}')
        out += row
        above = row
    return width, height, bytes(out)


def image_difference(got, want, tolerance):
    """None, or how two PNG images (their bytes) differ: in size, or in values of their texels
    by more than `tolerance` levels (the count, the worst and the first texels)."""
    (gw, gh, a), (w, h, b) = read_png(got), read_png(want)
    if (gw, gh) != (w, h):
        return f'{gw}x{gh}, expected {w}x{h}'
    texels = sorted({k // 4 for k in range(len(b)) if abs(a[k] - b[k]) > tolerance})
    if not texels:
        return None
    worst = max(abs(x - y) for x, y in zip(a, b))
    return f'{len(texels)} texels differ (worst {worst} levels, tolerance {tolerance}), first at {texels[:6]}'
