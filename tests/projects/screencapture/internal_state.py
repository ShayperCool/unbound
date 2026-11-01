"""Exact output coverage for the bounded ScreenCapture state controls."""

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
