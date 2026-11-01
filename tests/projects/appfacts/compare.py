#!/usr/bin/env python3
"""Compare an appfacts capture with the reference, and its machine facts with the OS.

    compare.py REFERENCE ACTUAL [--report REPORT.json]
    compare.py --machine CAPTURE [--editor]

REFERENCE and ACTUAL are directories holding values.jsonl. Every key of the union is compared
exactly; the only normalization is the runtime's wording of an argument's name in exception
messages (Mono ends them with "\\nParameter name: o", CoreCLR with " (Parameter 'o')"). Every
missing, extra and failing value is kept in the report and printed; the exit status is nonzero
when anything fails.

--machine checks CAPTURE/machine.jsonl against what the OS reported when it ran (os.json beside
it, plus /etc/os-release and the Editor licence file read now): the GPU's PCI ids, OpenGL
strings and memory, the CPU's vendor, model and current clock, the host, machine id, memory and
OS release, the battery, the log path (--editor: the Editor's -logFile; else the project's
Library/Unbound/Editor.log, which the probe writes as <project>/...: the file under the project
path run.py recorded in run.json must exist, be non-empty and be written during the run) and the
licence.
"""
import argparse
import json
import os
from pathlib import Path
import platform
import re
import sys
import xml.etree.ElementTree as ElementTree


def load(path):
    rows = [json.loads(s) for s in Path(path).read_text().splitlines() if s.strip()]
    result = {r['k']: r['v'] for r in rows}
    if len(rows) != len(result) or not rows:
        raise SystemExit(f'{path}: empty capture or duplicate keys')
    return result


def normalize(value):
    if isinstance(value, str):
        return re.sub(r"\nParameter name: (\w+)", r" (Parameter '\1')", value)
    if isinstance(value, list):
        return [normalize(v) for v in value]
    return value


def compare(reference, actual):
    a, b = load(Path(reference)/'values.jsonl'), load(Path(actual)/'values.jsonl')
    rows = []
    for key in list(a) + [k for k in b if k not in a]:
        if key not in b:
            rows.append({'key': key, 'status': 'missing', 'reference': a[key]})
        elif key not in a:
            rows.append({'key': key, 'status': 'extra', 'actual': b[key]})
        elif normalize(a[key]) != normalize(b[key]):
            rows.append({'key': key, 'status': 'different', 'reference': a[key], 'actual': b[key]})
    return {'reference': len(a), 'actual': len(b), 'matched': sum(1 for k in a if k in b and normalize(a[k]) == normalize(b[k])),
            'failures': rows}


def os_release():
    fields = {}
    for line in Path('/etc/os-release').read_text().splitlines():
        key, _, value = line.partition('=')
        fields[key] = value.strip().strip('"')
    return fields


def licence_pro():
    path = Path.home()/'.config/unity3d/Unity/licenses/UnityEntitlementLicense.xml'
    try:
        root = ElementTree.parse(path).getroot()
    except (OSError, ElementTree.ParseError):
        return False
    return any(g.get('Type') == 'EDITOR' and g.get('Tag') != 'UnityPersonal' for g in root.iter('EntitlementGroup'))


def unbound_log_problem(capture, log):
    """Why the reported console log is not the project's Library/Unbound/Editor.log written by
    this run (None when it is). The probe writes the project's path as <project>; run.json holds
    the absolute project path the engine was given and when the run started."""
    try:
        run = json.loads((capture/'run.json').read_text())
        project, started = Path(run['project']), float(run['started'])
    except (OSError, ValueError, KeyError, TypeError):
        return 'the capture records no project path and start (run.json)'
    if not project.is_absolute():
        return f'the recorded project path {project} is not absolute'
    if log != '<project>/Library/Unbound/Editor.log':
        return 'not the project\'s Library/Unbound/Editor.log'
    path = project/'Library/Unbound/Editor.log'
    if not path.is_file():
        return f'{path} does not exist'
    if path.stat().st_size == 0:
        return f'{path} is empty'
    if path.stat().st_mtime < started:
        return f'{path} was not written by this run'
    return None


def machine(capture, editor=False):
    """The machine facts that disagree with the OS (empty when all agree)."""
    capture = Path(capture)
    facts = load(capture/'machine.jsonl')
    os_facts = json.loads((capture/'os.json').read_text())
    release = os_release()
    kernel = '.'.join(os_facts['kernel'].split('-')[0].split('.')[:2])
    expected = {
        'systeminfo.deviceModel': 'PC',
        'systeminfo.deviceName': os_facts['hostname'],
        'systeminfo.deviceType': 'Desktop',
        'systeminfo.deviceUniqueIdentifier': os_facts['machineId'],
        'systeminfo.operatingSystem': f"Linux {kernel} {release.get('NAME', '')} {release.get('VERSION_ID', '')} 64bit",
        'systeminfo.operatingSystemFamily': 'Linux',
        'systeminfo.processorType': os_facts['cpuModel'],
        'systeminfo.processorModel': os_facts['cpuModel'],
        'systeminfo.processorManufacturer': os_facts['cpuVendor'],
        'systeminfo.processorCount': os_facts['cpuCount'],
        'systeminfo.systemMemorySize': os_facts['memTotalKiB']//1024,
        'systeminfo.graphicsDeviceType': 'OpenGLCore',
        'systeminfo.graphicsShaderLevel': 50,
        'application.buildGUID': '0'*32,
        'application.HasProLicense': licence_pro(),
        'application.isBatchMode': False,
        'debug.startupLogs.count': 0,
        'debug.startupLogs': [],
        'profiler.supported': True,
        'profiler.enabled': False,
    }
    if not any(Path('/sys/class/power_supply', p, 'type').exists() and
               Path('/sys/class/power_supply', p, 'type').read_text().strip() == 'Battery' for p in os_facts['powerSupplies']):
        expected['systeminfo.batteryLevel'] = -1
        expected['systeminfo.batteryStatus'] = 'Full'
    gpu = next((g for g in os_facts['gpus'] if g.get('vendor')), None)
    if gpu:
        expected['systeminfo.graphicsDeviceID'] = int(gpu['device'], 16)
        expected['systeminfo.graphicsDeviceVendorID'] = int(gpu['vendor'], 16)
    if os_facts.get('nvidiaModel') and gpu and gpu['vendor'] == '0x10de':
        expected['systeminfo.graphicsDeviceName'] = os_facts['nvidiaModel'] + '/PCIe/SSE2'
        expected['systeminfo.graphicsDeviceVendor'] = 'NVIDIA Corporation'
        expected['systeminfo.graphicsDeviceVersion'] = 'OpenGL 4.5.0 NVIDIA ' + os_facts['nvidiaVersion']
        expected['systeminfo.graphicsMemorySize'] = int(os_facts['nvidiaSmi'].split(',')[1])
    failures = []
    for key, value in expected.items():
        if facts.get(key) != value:
            failures.append({'key': key, 'os': value, 'actual': facts.get(key, '<missing>')})
    frequency = facts.get('systeminfo.processorFrequency')
    top = int(os_facts['cpufreqMax'])//1000 if os_facts.get('cpufreqMax') else None
    if not isinstance(frequency, int) or frequency <= 0 or (top and frequency > top):
        failures.append({'key': 'systeminfo.processorFrequency', 'os': f'the current clock, 1..{top} MHz', 'actual': frequency})
    log = facts.get('application.consoleLogPath', '')
    want = str(capture/'editor.log') if editor else None
    if editor and log != want:
        failures.append({'key': 'application.consoleLogPath', 'os': want, 'actual': log})
    if not editor:
        problem = unbound_log_problem(capture, log)
        if problem:
            failures.append({'key': 'application.consoleLogPath', 'os': 'the project\'s Library/Unbound/Editor.log, written',
                             'actual': log, 'problem': problem})
    if not isinstance(facts.get('profiler.graphicsDriver.start'), int) or facts['profiler.graphicsDriver.start'] < 0:
        failures.append({'key': 'profiler.graphicsDriver.start', 'os': 'a byte count', 'actual': facts.get('profiler.graphicsDriver.start')})
    return failures


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('paths', nargs='+', type=Path)
    parser.add_argument('--report', type=Path)
    parser.add_argument('--machine', action='store_true')
    parser.add_argument('--editor', action='store_true')
    args = parser.parse_args()
    if args.machine:
        failures = machine(args.paths[0], editor=args.editor)
        for f in failures:
            print(json.dumps(f))
        print(f'machine facts: {len(failures)} disagree with the OS')
        if args.report:
            args.report.write_text(json.dumps({'failures': failures}, indent=1) + '\n')
        return int(bool(failures))
    reference, actual = args.paths
    result = compare(reference, actual)
    for row in result['failures']:
        print(json.dumps(row)[:400])
    print(f"{result['matched']}/{result['reference']} reference values match; "
          f"{sum(r['status'] == 'missing' for r in result['failures'])} missing, "
          f"{sum(r['status'] == 'extra' for r in result['failures'])} extra, "
          f"{sum(r['status'] == 'different' for r in result['failures'])} different")
    if args.report:
        args.report.write_text(json.dumps(result, indent=1) + '\n')
    return int(bool(result['failures']))


if __name__ == '__main__':
    sys.exit(main())
