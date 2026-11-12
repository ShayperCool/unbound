"""Initialize private capture projects before their first audio callback allocation."""
import re


def prepare(project):
    # The 6000.0.71f1 Editor's script callbacks retain their startup buffer capacity.
    # Start at the largest measured buffer so later smaller Reset requests fit.
    path = project/'ProjectSettings/AudioManager.asset'
    text = path.read_text()
    for field in ('m_DSPBufferSize', 'm_RequestedDSPBufferSize'):
        text, count = re.subn(rf'({field}: )\d+', r'\g<1>8192', text)
        assert count == 1, field
    path.write_text(text)
    # This independent profile aliases fourteen existing mixer inputs. Keep only
    # those inputs in its private project rather than importing unrelated profiles.
    from common import wide_rows
    assets = {token[6:] for row in wide_rows().values() for token in row if token.startswith('asset=')}
    for mixer in (project/'Assets/Resources').glob('*.mixer'):
        if mixer.stem not in assets:
            mixer.unlink()
            meta = mixer.with_suffix('.mixer.meta')
            if meta.exists(): meta.unlink()
