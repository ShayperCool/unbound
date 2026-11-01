#!/usr/bin/env python3
"""Evaluate fixed source-derived seed hypotheses, retaining every prediction."""
import argparse
import hashlib
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import cache_artifact_path

U32, U64 = (1 << 32) - 1, (1 << 64) - 1


def particle_hash(x):
    # MinMaxCurve.cs: ParticleRandom.Hash, unchecked uint operations.
    x ^= x >> 16
    x = (x * 0x7feb352d) & U32
    x ^= x >> 15
    x = (x * 0x846ca68b) & U32
    return x ^ (x >> 16)


def subemit_word(seed):
    # Full word underlying ParticleRandom.Value(seed, ParticleRandom.SubEmit).
    return particle_hash(seed ^ particle_hash((36 * 0x9e3779b9 + 0x632be5ab) & U32))


def restart_stream(seed, count):
    # ParticleSystem.Restart and NextUInt. Not an assertion about Native internals.
    x = (seed << 32) ^ 0x9e3779b97f4a7c15 ^ seed
    values = []
    for _ in range(count):
        x ^= x >> 12
        x ^= (x << 25) & U64
        x ^= x >> 27
        values.append(((x * 0x2545f4914f6cdd1d) & U64) >> 32)
    return values


def script_stream(seed, count):
    # Math/Random.cs: State.FromSeed followed by the raw NextValue state word.
    x = seed
    y = (x * 1812433253 + 1) & U32
    z = (y * 1812433253 + 1) & U32
    w = (z * 1812433253 + 1) & U32
    values = []
    for _ in range(count):
        t = x ^ ((x << 11) & U32)
        x, y, z, w = y, z, w, w ^ (w >> 19) ^ t ^ (t >> 8)
        values.append(w)
    return values


MODELS = {
    'particle_hash_parent': lambda p, c, n, manual: particle_hash(p),
    'particle_hash_child': lambda p, c, n, manual: particle_hash(c),
    'particle_hash_parent_xor_child': lambda p, c, n, manual: particle_hash(p ^ c),
    'particle_value_parent_subemit_word': lambda p, c, n, manual: subemit_word(p),
    'particle_value_child_subemit_word': lambda p, c, n, manual: subemit_word(c),
    'current_restart_child_call_accounting': lambda p, c, n, manual: restart_stream(c, n * (2 if manual else 1))[-1],
    'current_restart_parent_per_call': lambda p, c, n, manual: restart_stream(p, 2 if manual else n)[-1],
    'script_random_child_one_word_per_emit': lambda p, c, n, manual: script_stream(c, n)[-1],
    'script_random_parent_per_call': lambda p, c, n, manual: script_stream(p, 1 if manual else n)[-1],
}


def analyze(path):
    rows = [json.loads(line) for line in path.read_text().splitlines()]
    observations, missing = [], []
    for row in rows:
        if row['step'] == 0:
            continue
        slot = row['step'] - 1
        if slot >= len(row['particles']):
            missing.append(dict(scenario=row['scenario'], step=row['step'], slot=slot, record=row))
            continue
        p = row['particles'][slot]
        manual = row['kind'] == 'manual'
        parent = row['supplied']['seed'] if manual else row['childSeed']
        observations.append(dict(scenario=row['scenario'], step=row['step'],
                                 parent_seed=parent if manual else None, child_seed=row['childSeed'],
                                 flags=row['flags'], seed=p['seed'], axis=p['axis'],
                                 predictions={name: model(parent, row['childSeed'], row['step'], manual)
                                              for name, model in MODELS.items()}))
    models = {}
    for name in MODELS:
        failures = [dict(scenario=o['scenario'], step=o['step'], expected=o['seed'],
                         predicted=o['predictions'][name]) for o in observations
                    if o['seed'] != o['predictions'][name]]
        models[name] = dict(tested=len(observations), passing=len(observations)-len(failures),
                            failing=len(failures), failures=failures,
                            predicts_all=len(observations) == 30 and not missing and not failures)
    sources = [REPO/'managed/UnityEngine/Particles/MinMaxCurve.cs',
               REPO/'managed/UnityEngine/Particles/ParticleSystem.cs',
               REPO/'managed/UnityEngine/Particles/ParticleSystem.Simulation.cs',
               REPO/'managed/UnityEngine/Math/Random.cs']
    return dict(capture_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                source_sha256={str(p.relative_to(REPO)): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources},
                ordinary_parent_convention='No parent exists; parent-input hypotheses use child seed for Emit.',
                missing=missing, observations=observations, models=models)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--unity-version', required=True)
    p.add_argument('--input', type=Path)
    p.add_argument('--output', type=Path, required=True)
    a = p.parse_args()
    cache_artifact_path(a.output)
    source = a.input or ROOT/'Reference'/('unity-'+a.unity_version+'.jsonl')
    result = analyze(source)
    a.output.parent.mkdir(parents=True, exist_ok=True)
    a.output.write_text(json.dumps(result, indent=2)+'\n')
    print({name: {k: v for k, v in m.items() if k != 'failures'} for name, m in result['models'].items()})
    # Analysis completion is separate from model acceptance. All mismatches remain explicit.


if __name__ == '__main__':
    main()
