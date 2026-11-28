#!/usr/bin/env python3
"""Record or check the Legacy Input Manager's SDL joystick axes on Linux."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
import editor_runner
from probe_paths import project_copy, run_root, unity_version
import value_probe

RELEASE = "6000.0.71f1"
REFERENCE = ROOT / "Reference" / f"unity-{RELEASE}"
DEVICES = ("mapped", "stick")


def prepare(project):
    path = project / "ProjectSettings" / "InputManager.asset"
    manager = path.read_text()
    marker = "  m_UsePhysicalKeys:"
    if manager.count(marker) != 1:
        raise RuntimeError("cannot add axes to the fixture's Input Manager")
    rows = "".join(f"""  - serializedVersion: 3
    m_Name: ProbeAxis{axis}
    gravity: 0
    dead: 0
    sensitivity: 1
    snap: 0
    invert: 0
    type: 2
    axis: {axis}
    joyNum: 1
""" for axis in range(10))
    path.write_text(manager.replace(marker, rows + marker))


def load(path):
    records = [json.loads(line) for line in path.read_text().splitlines()]
    if not records or records[0].get("case") != "create" or not records[0].get("ok"):
        raise RuntimeError(f"virtual device creation failed in {path}")
    if records[-1].get("case") != "complete" or not records[-1].get("ok"):
        raise RuntimeError(f"incomplete recording in {path}")
    return records


def capture(mode, device, label):
    os.environ.setdefault("UNBOUND_PROBE_UNITY_VERSION", RELEASE)
    if unity_version() != RELEASE:
        raise ValueError(f"the legacy hat probe requires Unity {RELEASE}")
    out = run_root(ROOT, f"legacy-hats-{label}-{device}")
    if out.exists():
        raise FileExistsError(out)
    native = mode == "record"
    project = project_copy(ROOT, out / "project", package_cache=not native)
    prepare(project)
    trace = out / "legacy-hats.jsonl"
    env = {"GAMEPAD_PROBE_OUT": str(out), "GAMEPAD_LEGACY_HATS": device}
    if native:
        job = editor_runner.EditorJob(
            project, out / "editor.log", out, batch=False, graphics=True, env=env,
            label=f"legacy-hats-{label}-{device}", progress_files=[trace],
            progress_timeout=150, timeout=240,
        )
        print(editor_runner.summary(editor_runner.run(job)))
        if problem := editor_runner.result_problem(out, gpu=True):
            raise RuntimeError(problem)
    else:
        with (out / "engine.log").open("w") as log:
            subprocess.run(
                [str(value_probe.LAUNCHER), "--kind", "engine-cpu", "timeout", "120",
                 value_probe.engine_cli(), "run", "-p", str(project), "--headless", "300000",
                 "--fixed-fps", "60", "--no-watch"],
                env=dict(os.environ, **env), stdout=log, stderr=subprocess.STDOUT, check=True,
            )
    print(trace)
    return load(trace)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("record", "check"))
    parser.add_argument("--label", required=True)
    parser.add_argument("--device", choices=DEVICES)
    args = parser.parse_args()
    if Path(args.label).name != args.label or args.label in ("", ".", ".."):
        parser.error("the label must be one directory name")
    if sys.platform != "linux":
        parser.error("this Linux joystick probe requires /dev/uinput")
    if not os.access("/dev/uinput", os.W_OK):
        parser.error("this probe requires write access to /dev/uinput")
    devices = (args.device,) if args.device else DEVICES
    errors = 0
    for device in devices:
        actual = capture(args.mode, device, args.label)
        if args.mode == "check":
            expected = load(REFERENCE / f"legacy-hats-{device}.jsonl")
            if len(actual) != len(expected):
                print(f"{device}: expected {len(expected)} rows, got {len(actual)}")
                errors += 1
            for want, got in zip(expected, actual):
                if want != got:
                    print(f"{device}: expected {want!r}, got {got!r}")
                    errors += 1
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
