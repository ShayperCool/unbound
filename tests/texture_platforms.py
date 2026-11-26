"""The per-platform entries of a texture's `.meta` that belong to the Editor installation's platform
modules: which modules are installed decides which entries the Editor (and Unbound) write, so a
probe compares them apart from the rest of the text.
"""
import re


def entry_pattern(targets=None):
    """A texture `.meta`'s per-platform entry, as bytes: of one of `targets` (build target names),
    or, with None, of any platform module (every build target but DefaultTexturePlatform and
    Standalone)."""
    if targets is None:
        names = rb'(?!DefaultTexturePlatform\b|Standalone\b)\S+'
    else:
        names = rb'(?:' + rb'|'.join(re.escape(t.encode()) for t in sorted(targets)) + rb')' if targets else rb'(?!)'
    return re.compile(rb'(?m)^( *)- serializedVersion: [0-9]+\n\1  buildTarget: ' + names + rb'\n(?:\1  .*\n)*')


def strip(text, targets=None):
    """`text` (bytes) without those entries."""
    return entry_pattern(targets).sub(b'', text)


def installation_differences(reference, actual):
    """The platform modules one of two installations has and the other lacks: the entries a
    texture's `.meta` holds for those come from the installation, not from what wrote it."""
    return set(reference) ^ set(actual)
