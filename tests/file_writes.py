"""What a run wrote into its copy of a probe project: every file of Assets/, Packages/ and
ProjectSettings/ after the process ended, as `exit.<path>` rows: `unchanged`, `<removed>`, or its
text with GUIDs written as the path they name and random file ids as their order of appearance,
the file's main document first (`exit.<path>.idsAscending` records the Editor's document order).
Used by tests/projects/urpwrites and tests/projects/tmpatlas.
"""
from pathlib import Path
import re

PARTS = ('Assets', 'Packages', 'ProjectSettings')
GUID = re.compile(r'\b[0-9a-f]{32}\b')
FILE_ID = re.compile(r'(&|fileID: )(-?[0-9]{5,})')
# The fixed file ids of a native asset's main object (material, texture, scriptable object) and of
# the references a file makes to other assets' main objects (script, shader).
KEPT_IDS = {'2100000', '11400000', '11500000', '4800000', '2800000'}
MAIN_IDS = ('2100000', '2800000', '11400000')
DOCUMENT_ID = re.compile(r'(?m)^--- !u![0-9]+ &(-?[0-9]+)')


def files(project):
    """{relative path: bytes} of every file of the project's parts."""
    return {p.relative_to(project).as_posix(): p.read_bytes()
            for part in PARTS for p in sorted((Path(project)/part).rglob('*')) if p.is_file()}


def guids(project):
    """{GUID: asset path} of the project's Assets/ metas."""
    found = {}
    for meta in (Path(project)/'Assets').rglob('*.meta'):
        match = re.search(r'^guid: ([0-9a-f]{32})', meta.read_text(errors='replace'), re.M)
        if match:
            found[match.group(1)] = meta.relative_to(project).as_posix()[:-len('.meta')]
    return found


def normalize(text, known):
    """GUIDs of the project's assets as `<guid:path>`, random file ids as `<idN>` in order."""
    text = GUID.sub(lambda m: f'<guid:{known[m.group(0)]}>' if m.group(0) in known else m.group(0), text)
    ids = {}

    def local(m):
        if m.group(2) in KEPT_IDS:
            return m.group(0)
        return m.group(1) + ids.setdefault(m.group(2), f'<id{len(ids) + 1}>')
    return FILE_ID.sub(local, text)


def main_document_first(text):
    """A file's documents with the main object's first: the others' file ids are random and the
    Editor writes documents by ascending file id."""
    ends = text.endswith('\n')
    parts = (text[:-1] if ends else text).split('\n--- ')
    if len(parts) < 3:
        return text
    documents = parts[1:]
    heads = [d.split('\n', 1)[0] for d in documents]
    main = next((i for i, head in enumerate(heads) if head.split('&')[-1] in MAIN_IDS), 0)
    documents.insert(0, documents.pop(main))
    return parts[0] + ''.join('\n--- ' + d for d in documents) + ('\n' if ends else '')


def exit_rows(project, initial):
    """The `exit.*` rows of the project against its files before the run (`files()`)."""
    now = files(project)
    known = guids(project)
    rows = []
    for path in sorted(initial.keys() | now.keys()):
        before, after = initial.get(path), now.get(path)
        if after == before:
            value = 'unchanged'
        elif after is None:
            value = '<removed>'
        else:
            text = after.decode('utf-8', 'replace')
            value = normalize(main_document_first(text), known)
            ids = [int(i) for i in DOCUMENT_ID.findall(text)]
            if len(ids) > 1:
                rows.append({'key': f'exit.{path}.idsAscending', 'value': 'true' if ids == sorted(ids) else 'false'})
        rows.append({'key': 'exit.' + path, 'value': value})
    return rows
