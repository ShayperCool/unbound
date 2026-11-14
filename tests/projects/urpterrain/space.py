"""The URP terrain probe's color spaces: the project as authored (linear) or a gamma-space copy,
each with its own reference directory."""
from pathlib import Path

SPACES = ('linear', 'gamma')


def apply_color_space(project, space):
    """Sets the copy's Player color space (the authored project is linear)."""
    if space == 'gamma':
        settings = Path(project)/'ProjectSettings/ProjectSettings.asset'
        text = settings.read_text()
        if text.count('m_ActiveColorSpace: 1') != 1:
            raise SystemExit(f'cannot set the gamma color space in {settings}')
        settings.write_text(text.replace('m_ActiveColorSpace: 1', 'm_ActiveColorSpace: 0'))


def reference_dir(root, space):
    """Linear: the release's reference directory; gamma: its gamma/ directory."""
    return Path(root) if space == 'linear' else Path(root)/'gamma'
