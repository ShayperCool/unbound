#!/usr/bin/env python3
"""Offline controls of the probe's record formatter, built from the probe's own method text.

    controls/formatter.py --out NEW_DIR [--release R] [--base COMMIT] [--witnesses MATRIX.tsv]

The changed recorder (CoreProbe.Record, which no longer runs the removed whole-message id scan
Names) and its unchanged neighbours (Id, JsonIds, the static state, Frame and ProbeFormat.cs's P)
are cut verbatim out of the current CoreProbe.cs and out of the base blob (the probe before the
change, from git), each slice checked to occur exactly once, and the change itself checked to be
exactly: Names removed, Record's call replaced, Record's comment added. The slices are compiled
into two classes (NewProbe, OldProbe) with a minimal fake UnityEngine (Time.frameCount and the
Vector3/Quaternion/Pose fields P needs) and a driver, by the release's bundled Roslyn on its
bundled .NET runtime. This harness is not a Unity or Unbound build and proves nothing about an
engine; it only executes the probe's formatting text.

Controls (every case is kept with its result in NEW_DIR/results.json):
  * witnesses: the 23 saved corruption witnesses (controls/witnesses.json). The current recorder
    turns each Native message back into its reference line byte for byte; the base recorder with
    the one named id reproduces the saved corrupted line (SHA-256, and the line itself when the
    audit's MATRIX.tsv is given) with its exact number of substitutions (27 in total);
  * payloads: ordinary numbers (10, -10, flag pairs, floats with an integer part 10), booleans,
    large and exponent floats, hex-like float bits, escaped/control/non-ASCII text and JSON-like
    text, with a name map whose ids equal those numbers: the current recorder writes them
    unchanged (the decoded line equals the message, carriage returns dropped as P.Esc does, and
    the line equals the writer format), the base recorder rewrites the ones it is expected to;
  * ids: Id() and JsonIds() still give 0, #name, #unknown+ and #unknown- (also for ids equal to
    those numbers), identically in both versions;
  * records: every event line of the existing reference, reformatted from its own fields, is
    byte-identical with the current recorder under an empty map and under a map naming every
    integer in the reference; the base recorder is identical under the empty map (Native's zero
    substitutions) and must change lines under the adversarial map (the control is sensitive).
"""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

CONTROLS = Path(__file__).resolve().parent
PROBE = CONTROLS.parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(PROBE))
import guards  # noqa: E402

SOURCE = 'tests/projects/coreapis/Assets/Scripts/CoreProbe.cs'
FORMAT = 'tests/projects/coreapis/Assets/Scripts/ProbeFormat.cs'
BASE = '3f170bd9ecd0e9c4bc901bf73b019fa645b65ef3'
BASE_SHA256 = '00103ec2409b8b2b624acac154459c7c45f8818bf55ace7489ce717f14fc9def'
FIELDS = ('    static string s_Scenario = "-";\n', '    static int s_Frame0 = -1;\n',
          '    static readonly StringBuilder s_Log = new StringBuilder();\n',
          '    static readonly Dictionary<int, string> s_Names = new Dictionary<int, string>();\n',
          '    public static int Frame => s_Frame0 < 0 ? -1 : Time.frameCount - s_Frame0;\n')
REFERENCE = PROBE/'Reference/unity-6000.0.71f1.jsonl'

FAKE = '''// Minimal fake UnityEngine for the formatter harness (not Unity's API).
namespace UnityEngine
{
    public static class Time { public static int frameCount; }
    public struct Vector3 { public float x, y, z; }
    public struct Quaternion { public float x, y, z, w; }
    public struct Pose { public Vector3 position; public Quaternion rotation; }
}
'''
CLASS = '''// Generated: verbatim slices of {source}.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static partial class {name}
{{
{slices}}}

public static partial class {name}
{{
    static void Set(Dictionary<int, string> names)
    {{
        s_Names.Clear();
        foreach (var kv in names) s_Names[kv.Key] = kv.Value;
    }}

    public static string HarnessRecord(int frame0, int frameCount, string scenario, Dictionary<int, string> names,
                                       string who, string message)
    {{
        s_Frame0 = frame0;
        Time.frameCount = frameCount;
        s_Scenario = scenario;
        Set(names);
        s_Log.Clear();
        Record(who, message);
        return s_Log.ToString();
    }}

    public static string HarnessId(Dictionary<int, string> names, int id) {{ Set(names); return Id(id); }}
    public static string HarnessJsonIds(Dictionary<int, string> names, string json) {{ Set(names); return JsonIds(json); }}
}}
'''
DRIVER = '''// Reads one case per line, writes the base64 of each result.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

public static class Driver
{
    static Dictionary<int, string> Map(JsonElement e)
    {
        var map = new Dictionary<int, string>();
        foreach (var p in e.EnumerateObject()) map[int.Parse(p.Name)] = p.Value.GetString();
        return map;
    }

    public static int Main(string[] args)
    {
        var output = new StringBuilder();
        foreach (var line in File.ReadAllLines(args[0], Encoding.UTF8))
        {
            if (line.Length == 0) continue;
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            string op = r.GetProperty("op").GetString(), impl = r.GetProperty("impl").GetString();
            var names = Map(r.GetProperty("names"));
            bool fresh = impl == "new";
            string result;
            if (op == "record")
            {
                int f0 = r.GetProperty("frame0").GetInt32(), fc = r.GetProperty("frameCount").GetInt32();
                string s = r.GetProperty("scenario").GetString(), who = r.GetProperty("who").GetString(),
                       message = r.GetProperty("message").GetString();
                result = fresh ? NewProbe.HarnessRecord(f0, fc, s, names, who, message)
                               : OldProbe.HarnessRecord(f0, fc, s, names, who, message);
            }
            else if (op == "id")
            {
                int id = r.GetProperty("id").GetInt32();
                result = fresh ? NewProbe.HarnessId(names, id) : OldProbe.HarnessId(names, id);
            }
            else if (op == "jsonids")
            {
                string json = r.GetProperty("json").GetString();
                result = fresh ? NewProbe.HarnessJsonIds(names, json) : OldProbe.HarnessJsonIds(names, json);
            }
            else throw new InvalidOperationException(op);
            output.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(result))).Append('\\n');
        }
        File.WriteAllText(args[1], output.ToString());
        return 0;
    }
}
'''


def sha(data):
    return hashlib.sha256(data if isinstance(data, bytes) else data.encode()).hexdigest()


def once(text, piece, what):
    if text.count(piece) != 1:
        raise ValueError(f'{what} occurs {text.count(piece)} times')
    return piece


def until_blank(text, start, what):
    i = text.index(start) if text.count(start) == 1 else None
    if i is None:
        raise ValueError(f'{what}: start occurs {text.count(start)} times')
    return once(text, text[i:text.index('\n\n', i)+1], what)


def record_slice(text):
    i = text.index('    public static void Record(string who, string message) =>')
    j = i
    while True:  # include the doc comment lines directly above
        k = text.rfind('\n', 0, j-1)+1
        if text[k:j].startswith('    ///'):
            j = k
        else:
            break
    return once(text, text[j:text.index('\n\n', i)+1], 'Record')


def slices(text, old):
    parts = [once(text, f, f.strip()) for f in FIELDS]
    parts.append(record_slice(text))
    parts.append(until_blank(text, '    /// <summary>Instance ids in JsonUtility text', 'JsonIds'))
    line = text[text.index('    static string Id(int id) =>'):]
    parts.append(once(text, line[:line.index('\n')+1], 'Id'))
    if old:
        start = text.index('    /// <summary>Replaces every known instance id')
        parts.append(once(text, text[start:text.index('\n    }\n', start)+len('\n    }\n')], 'Names'))
    elif 'Names(' in text:
        raise ValueError('the current recorder still calls Names')
    return parts


def change_problems(old_text, new_text, old_parts, new_parts):
    """The change must be exactly the recorder repair: Names (and the blank line after it) removed,
    Record's line and comment replaced; nothing else in the file."""
    names = old_parts[-1]
    expected = old_text.replace(old_parts[5], new_parts[5]).replace(names+'\n', '')
    problems = [] if expected == new_text else ['CoreProbe.cs differs from the base by more than the recorder repair']
    if 'P.Esc(Names(message))' not in old_parts[5] or 'P.Esc(message)' not in new_parts[5]:
        problems.append('the Record slices are not the base/current recorder')
    for a, b, what in zip(old_parts[:5]+old_parts[6:8], new_parts[:5]+new_parts[6:8], FIELDS+('JsonIds', 'Id')):
        if a != b:
            problems.append(f'{what.strip()} changed')
    return problems


def harness_cases(witness_doc, reference):
    """(cases, expectations): one harness input per case and how to judge its output."""
    cases, judge = [], []

    def add(case, check):
        cases.append(case)
        judge.append(check)
    ref_lines = reference.read_text().splitlines()
    witness_map = witness_doc['name_map']
    for w in witness_doc['rows']:
        row = json.loads(ref_lines[w['reference_index']])
        frame0 = -1 if row['f'] < 0 else 100
        base = {'op': 'record', 'frame0': frame0, 'frameCount': 100+row['f'], 'scenario': row['s'],
                'who': row['who'], 'message': row['e'], 'names': witness_map}
        add(dict(base, impl='new'), {'group': 'witness', 'index': w['reference_index'], 'want_bytes': ref_lines[w['reference_index']]+'\n'})
        add(dict(base, impl='old'), {'group': 'witness-base', 'index': w['reference_index'], 'want_sha256': w['corrupt_sha256'],
                                     'substitutions': w['substitutions']})
    adversarial = {'10': 'LazyData', '1': 'One', '42': 'Answer', '3': 'Three', '255': 'FF', '-10': 'Neg',
                   '1065353216': 'Bits', '2147483647': 'Max', '34': 'ThirtyFour', '37': 'E37', '7': 'Seven',
                   '0': 'Zero', '11': 'Eleven', '5': 'Five', '28': 'TwentyEight'}
    payloads = [
        ('10', True), ('count=10', True), ('state A=00 A1=10 B=00 C=00 D=00 D1=10 E=11 F=11', True),
        ('name=Src(Clone) local=(10.000,0.000,0.000) (10.000,30.000,0.000) (1.000,1.000,0.500)', True),
        ('-10', True), ('x=-10 y=10', True), ('1', True), ('0', True), ('isSet=1 isBroken=0 sameId=1', True),
        ('F2 -34000000000000000000000000000000000000.00 | E6 -3.400000E+37 | max 3.4028235E+38 1E+37', True),
        ('bits (42e00000,3f800000,c2280000) (3fffffff,40800000,40c00000)', True),
        ('0x10 1065353216 2147483647 -2147483648', True), ('#LazyData is literal text', False),
        ('json {"x":10,"mat":{"instanceID":10}}', True), ('a10 10a v1.10.3 x10y', True), ('a10 x10y b42', False),
        ('quote " backslash \\ slash / newline \n tab \t cr \r ctrl \u0001\u001f\u0008\u000c end', False),
        ('unicode é 漢   \U0001f600 10', True), ('10\n10\r\n', True), ('', False),
        ('targetFrameRate=60 vSync=0 interval=2 effective=30', True),
    ]
    for message, base_changes in payloads:
        for who in ('probe', 'copy0', 'who "q" \\ 10'):
            base = {'op': 'record', 'frame0': 100, 'frameCount': 103, 'scenario': 'active.invalid', 'who': who,
                    'message': message, 'names': adversarial}
            add(dict(base, impl='new'), {'group': 'payload', 'message': message, 'who': who})
            if who == 'probe':
                add(dict(base, impl='old'), {'group': 'payload-base', 'message': message, 'base_changes': base_changes})
    ids = [(0, '0'), (10, '#LazyData'), (1, '#One'), (42, '#Answer'), (-10, '#Neg'), (1065353216, '#Bits'),
           (2147483647, '#Max'), (12345, '#unknown+'), (-7, '#unknown-'), (99, '#unknown+')]
    for impl in ('new', 'old'):
        for value, want in ids:
            add({'op': 'id', 'impl': impl, 'id': value, 'names': adversarial}, {'group': 'id', 'want_bytes': want})
        for json_in, want in (
                ('{"x":3,"mat":{"instanceID":10}}', '{"x":3,"mat":{"instanceID":"#LazyData"}}'),
                ('{"m_InstanceID":-10,"InstanceID":0}', '{"m_InstanceID":"#Neg","InstanceID":"0"}'),
                ('{"instanceID":12345,"value":10}', '{"instanceID":"#unknown+","value":10}'),
                ('{"instanceID":-7}', '{"instanceID":"#unknown-"}')):
            add({'op': 'jsonids', 'impl': impl, 'json': json_in, 'names': adversarial}, {'group': 'jsonids', 'want_bytes': want})
    tokens = sorted({t for line in ref_lines for t in re.findall(r'-?\d+', json.loads(line).get('e', ''))
                     if t not in ('0', '-0') and -2**31 <= int(t) < 2**31})
    every = {str(int(t)): 'N'+t for t in tokens}
    for index, line in enumerate(ref_lines):
        row = json.loads(line)
        if tuple(row) != guards.EVENT_KEYS:
            continue
        frame0 = -1 if row['f'] < 0 else 100
        for impl, names, group in (('new', {}, 'record'), ('new', every, 'record-adversarial'),
                                   ('old', {}, 'record-base'), ('old', every, 'record-base-adversarial')):
            add({'op': 'record', 'impl': impl, 'frame0': frame0, 'frameCount': 100+row['f'], 'scenario': row['s'],
                 'who': row['who'], 'message': row['e'], 'names': names},
                {'group': group, 'index': index, 'want_bytes': line+'\n'})
    return cases, judge, {'adversarial_map': adversarial, 'reference_integer_tokens': len(every)}


def evaluate(cases, judge, outputs, saved):
    results = []
    for case, check, out in zip(cases, judge, outputs):
        got = base64.b64decode(out).decode('utf-8')
        group, ok, detail = check['group'], False, ''
        if group in ('witness', 'id', 'jsonids', 'record', 'record-adversarial', 'record-base'):
            ok = got == check['want_bytes']
        elif group == 'witness-base':
            line = got[:-1] if got.endswith('\n') else got
            ok = sha(line) == check['want_sha256'] and line.count('#LazyData') == check['substitutions']
            if saved is not None:
                ok = ok and saved.get(check['index']) == line
            detail = f"substitutions {line.count('#LazyData')}"
        elif group == 'record-base-adversarial':
            ok = True    # judged in aggregate (must change some lines)
            detail = 'changed' if got != check['want_bytes'] else 'same'
        elif group == 'payload':
            row = json.loads(got)
            want_row = {'f': 3, 's': 'active.invalid', 'who': check['who'].replace('\r', ''),
                        'e': check['message'].replace('\r', '')}
            ok = got.endswith('\n') and got.count('\n') == 1 and row == want_row and \
                tuple(row) == guards.EVENT_KEYS and guards.written(row)+'\n' == got
        elif group == 'payload-base':
            new = next(o for c, j, o in zip(cases, judge, outputs) if j['group'] == 'payload' and
                       j['message'] == check['message'] and j['who'] == 'probe')
            changed = got != base64.b64decode(new).decode('utf-8')
            ok = changed == check['base_changes']
            detail = 'base rewrote it' if changed else 'base kept it'
        results.append({'group': group, 'case': case, 'expect': check, 'got': got, 'pass': ok, 'detail': detail})
    return results


def run(cmd, log, **kw):
    process = subprocess.run(cmd, capture_output=True, stdin=subprocess.DEVNULL, **kw)
    Path(log).write_bytes(process.stdout+process.stderr)
    return process.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--release', default='6000.0.71f1')
    parser.add_argument('--base', default=BASE)
    parser.add_argument('--witnesses', type=Path, help="the audit's MATRIX.tsv, to compare the saved lines themselves")
    args = parser.parse_args()
    if args.out.exists():
        sys.exit(f'refusing to reuse {args.out}')
    old_text = subprocess.run(['git', '-C', str(REPO), 'show', f'{args.base}:{SOURCE}'], capture_output=True,
                              check=True).stdout.decode()
    base_format = subprocess.run(['git', '-C', str(REPO), 'show', f'{args.base}:{FORMAT}'], capture_output=True,
                                 check=True).stdout
    new_text, fmt = (REPO/SOURCE).read_text(), (REPO/FORMAT).read_bytes()
    record = {'base': args.base, 'base_sha256': sha(old_text), 'current_sha256': sha(new_text),
              'probe_format_sha256': sha(fmt), 'problems': []}
    if sha(old_text) != BASE_SHA256:
        record['problems'].append('the base blob is not the declared base')
    if base_format != fmt:
        record['problems'].append('ProbeFormat.cs differs from the base')
    old_parts, new_parts = slices(old_text, True), slices(new_text, False)
    record['problems'] += change_problems(old_text, new_text, old_parts, new_parts)
    record['slices'] = {'current': [sha(p) for p in new_parts], 'base': [sha(p) for p in old_parts]}
    data = Path.home()/'Unity/Hub/Editor'/args.release/'Editor/Data'
    framework = sorted((data/'NetCoreRuntime/shared/Microsoft.NETCore.App').glob('*/'))
    if len(framework) != 1:
        sys.exit(f'expected one bundled .NET runtime, found {framework}')
    args.out.mkdir(parents=True)
    src = args.out/'harness'
    src.mkdir()
    (src/'FakeUnity.cs').write_text(FAKE)
    (src/'ProbeFormat.cs').write_bytes(fmt)
    (src/'NewProbe.cs').write_text(CLASS.format(source='current '+SOURCE, name='NewProbe', slices=''.join(new_parts)))
    (src/'OldProbe.cs').write_text(CLASS.format(source=f'{args.base}:{SOURCE}', name='OldProbe', slices=''.join(old_parts)))
    (src/'Driver.cs').write_text(DRIVER)
    refs = [f'-r:{p}' for p in sorted(framework[0].glob('*.dll'))]
    dotnet, csc = data/'NetCoreRuntime/dotnet', data/'DotNetSdkRoslyn/csc.dll'
    compile_cmd = [str(dotnet), 'exec', str(csc), '/nostdlib', '/noconfig', '-nologo', '-target:exe', '-langversion:9.0',
                   '-nowarn:1701,1702,0169,0649', f'-out:{args.out/"harness.dll"}', *refs, *[str(p) for p in sorted(src.glob('*.cs'))]]
    env = dict(os.environ, DOTNET_MULTILEVEL_LOOKUP='0', DOTNET_SYSTEM_GLOBALIZATION_INVARIANT='1', DOTNET_CLI_TELEMETRY_OPTOUT='1')
    record['compile'] = {'command': compile_cmd, 'exit': run(compile_cmd, args.out/'compile.log', env=env),
                         'dotnet_sha256': sha(dotnet.read_bytes()), 'csc_sha256': sha(csc.read_bytes()),
                         'runtime': framework[0].name}
    if record['compile']['exit'] != 0:
        record['problems'].append('the harness did not compile (see compile.log)')
        (args.out/'results.json').write_text(json.dumps(record, indent=1)+'\n')
        print('FAIL: the harness did not compile')
        return 1
    (args.out/'harness.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {
        'tfm': 'net6.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': framework[0].name}}}))
    witness_doc = json.loads((CONTROLS/'witnesses.json').read_text())
    saved = None
    if args.witnesses:
        import csv
        rows = list(csv.DictReader(args.witnesses.open(), delimiter='\t'))
        saved = {int(r['pair_index']): r['raw_line'] for r in rows if r['cause'] == 'NAMES10' and r['side'].startswith('extra')}
        record['witness_source'] = {'path': str(args.witnesses), 'sha256': sha(args.witnesses.read_bytes())}
    cases, judge, info = harness_cases(witness_doc, REFERENCE)
    record.update(info)
    (args.out/'cases.jsonl').write_text(''.join(json.dumps(c, ensure_ascii=False)+'\n' for c in cases), encoding='utf-8')
    run_cmd = [str(dotnet), str(args.out/'harness.dll'), str(args.out/'cases.jsonl'), str(args.out/'outputs.b64')]
    record['run'] = {'command': run_cmd, 'exit': run(run_cmd, args.out/'run.log', env=env)}
    if record['run']['exit'] != 0:
        record['problems'].append('the harness failed (see run.log)')
        (args.out/'results.json').write_text(json.dumps(record, indent=1)+'\n')
        print('FAIL: the harness failed')
        return 1
    outputs = (args.out/'outputs.b64').read_text().splitlines()
    if len(outputs) != len(cases):
        record['problems'].append(f'{len(outputs)} outputs for {len(cases)} cases')
        outputs += ['']*(len(cases)-len(outputs))
    results = evaluate(cases, judge, outputs, saved)
    groups = {}
    for r in results:
        g = groups.setdefault(r['group'], {'cases': 0, 'pass': 0})
        g['cases'] += 1
        g['pass'] += r['pass']
    changed = sum(1 for r in results if r['group'] == 'record-base-adversarial' and r['detail'] == 'changed')
    groups['record-base-adversarial']['changed'] = changed
    if changed == 0:
        record['problems'].append('the base recorder changed nothing under the adversarial map (insensitive control)')
    subs = sum(int(r['detail'].split()[-1]) for r in results if r['group'] == 'witness-base')
    groups['witness-base']['substitutions'] = subs
    if subs != witness_doc['total_substitutions']:
        record['problems'].append(f'{subs} witness substitutions, not {witness_doc["total_substitutions"]}')
    failed = [r for r in results if not r['pass']]
    record['problems'] += [f"{r['group']}: {json.dumps(r['case'], ensure_ascii=False)[:160]} -> {r['got'][:160]!r}" for r in failed]
    record['groups'] = groups
    record['passed'] = not record['problems']
    record['results'] = results
    (args.out/'results.json').write_text(json.dumps(record, indent=1, ensure_ascii=False)+'\n')
    for name, g in groups.items():
        print(f'{name}: {g["pass"]}/{g["cases"]}' + ''.join(f', {k} {v}' for k, v in g.items() if k not in ('cases', 'pass')))
    print('PASS' if record['passed'] else f"FAIL: {len(record['problems'])} problems, first: {record['problems'][0]}")
    return 0 if record['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
