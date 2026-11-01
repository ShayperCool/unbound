"""Exact output coverage for the bounded ScreenCapture state controls."""
import importlib.util
import json
from pathlib import Path

IMAGES = {
    'state.source', 'state.none.first', 'state.none.second',
    'state.copy.first', 'state.copy.second', 'state.copy.image',
    'post.buffer.asTexture', 'post.buffer.into', 'post.buffer.final',
}
KEYS = {key+'.pixels' for key in IMAGES} | {
    'screen', 'colorSpace', 'profile', 'complete', 'state.source.log',
    'state.none.log', 'state.copy.log', 'post.buffer.asTexture.frame',
    'post.buffer.asTexture.log', 'post.buffer.asTexture.texture',
    'post.buffer.into.log', 'post.buffer.calls', 'post.buffer.final.log',
}


def coverage(rows):
    values = {row['k']: row['v'] for row in rows}
    missing, extra = sorted(KEYS-values.keys()), sorted(values.keys()-KEYS)
    failures = []
    expected = {'screen': [64, 32], 'colorSpace': 'Linear', 'profile': 'internal-state',
                'complete': True, 'post.buffer.calls': 1}
    expected.update({key+'.pixels': 64*32 for key in IMAGES})
    for key, value in expected.items():
        if key in values and values[key] != value:
            failures.append({'key': key, 'expected': value, 'actual': values[key]})
    if missing or extra or failures:
        return f'control coverage: missing={missing}, extra={extra}, failures={failures}'
    return None


def rendered_setup(directory, report):
    """Check that the requested caller state was actually drawn before publishing.

    Every image channel still goes through the unchanged replay comparator.
    These extra checks reject captures whose state setup did not take effect.
    """
    spec = importlib.util.spec_from_file_location('state_compare', Path(__file__).parents[1]/'rtdepth/compare.py')
    compare = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(compare)
    paths = compare.images(directory)
    results, failures = [], []
    for name in ('state.none.first', 'state.none.second', 'state.copy.first', 'state.copy.second'):
        pixels = compare.pixels(paths[name])
        # The untouched corner records the clear color, outside the authored viewport.
        changed = [i for i, pixel in enumerate(pixels) if not compare.equal(pixel.tolist(), pixels[0].tolist())]
        outside = [i for i in changed if not (20 <= i % 64 < 44 and 8 <= i // 64 < 24)]
        result = {'key': name, 'drawnPixels': changed, 'outsideScissor': outside,
                  'status': 'pass' if changed and not outside and len(changed) < 24*16 else 'different'}
        results.append(result)
        if result['status'] != 'pass':
            failures.append(name)
    image = compare.pixels(paths['state.copy.image'])
    positive = any(any(channel != 0 for channel in pixel) for pixel in image)
    results.append({'key': 'state.copy.image.written', 'status': 'pass' if positive else 'different'})
    if not positive:
        failures.append('state.copy.image.written')
    for a, b in (('state.none.first', 'state.copy.first'), ('state.none.second', 'state.copy.second'),
                 ('post.buffer.asTexture', 'post.buffer.into'), ('post.buffer.into', 'post.buffer.final')):
        result = compare.compare_images(compare.pixels(paths[a]), compare.pixels(paths[b]))
        result.update(key=a+' vs '+b)
        results.append(result)
        if result['status'] != 'pass':
            failures.append(result['key'])
    report.write_text(json.dumps({'checks': results, 'failures': failures}, indent=2)+'\n')
    return failures
