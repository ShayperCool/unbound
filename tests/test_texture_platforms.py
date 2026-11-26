"""texture_platforms.py and scenesave's texture .meta comparison without the platform entries of
modules only one installation has.

    python3 -m unittest discover -s tests -p test_texture_platforms.py
"""
import sys
import tempfile
import unittest
from pathlib import Path

TESTS = Path(__file__).resolve().parent
sys.path.insert(0, str(TESTS))
sys.path.insert(0, str(TESTS/'projects/scenesave'))
import scenesave_compare  # noqa: E402
import texture_platforms  # noqa: E402


def entry(target, max_size=2048):
    return (f'  - serializedVersion: 4\n    buildTarget: {target}\n    maxTextureSize: {max_size}\n'
            '    textureCompression: 0\n    overridden: 0\n')


def meta(*targets, readable=0):
    return ('fileFormatVersion: 2\nguid: 7a5e000e4c1b4d2e8f3a6b5c7d9e0f12\nTextureImporter:\n'
            f'  isReadable: {readable}\n  platformSettings:\n' + ''.join(entry(t) for t in targets)
            + '  spriteSheet:\n    sprites: []\n  userData: \n')


class TexturePlatformTests(unittest.TestCase):
    def compare(self, reference, actual, modules):
        with tempfile.TemporaryDirectory() as directory:
            ref, act = Path(directory)/'ref.png.meta', Path(directory)/'act.png.meta'
            ref.write_text(reference)
            act.write_text(actual)
            return scenesave_compare.compare_file(ref, act, 'Assets/Labels/Tex.png.meta', installation_modules=modules)

    def test_a_module_only_one_installation_has_is_left_out(self):
        # Recorded with the Android module (macOS Editor), gated with Android and iOS (Linux).
        modules = texture_platforms.installation_differences(('Android',), ('Android', 'iOS'))
        self.assertEqual(modules, {'iOS'})
        reference = meta('DefaultTexturePlatform', 'Standalone', 'Android')
        actual = meta('DefaultTexturePlatform', 'Standalone', 'Android', 'iOS')
        self.assertIsNone(self.compare(reference, actual, modules))
        self.assertEqual(self.compare(reference, actual, set()), 'differs')

    def test_a_wrong_setting_still_differs(self):
        modules = {'iOS'}
        reference = meta('DefaultTexturePlatform', 'Standalone', 'Android')
        actual = meta('DefaultTexturePlatform', 'Standalone', 'Android', 'iOS', readable=1)
        self.assertEqual(self.compare(reference, actual, modules), 'differs')

    def test_a_missing_entry_of_a_module_both_have_still_differs(self):
        modules = {'iOS'}
        reference = meta('DefaultTexturePlatform', 'Standalone', 'Android')
        actual = meta('DefaultTexturePlatform', 'Standalone', 'iOS')
        self.assertEqual(self.compare(reference, actual, modules), 'differs')
        # And a wrong value in a kept entry.
        changed = meta('DefaultTexturePlatform', 'Standalone', 'Android', 'iOS').replace(
            'buildTarget: Android\n    maxTextureSize: 2048', 'buildTarget: Android\n    maxTextureSize: 1024')
        self.assertEqual(self.compare(reference, changed, modules), 'differs')

    def test_every_module_entry_without_targets(self):
        # projectwrites' use: every platform module's entry, the default and Standalone kept.
        text = meta('DefaultTexturePlatform', 'Standalone', 'Android', 'iOS').encode()
        self.assertEqual(texture_platforms.strip(text), meta('DefaultTexturePlatform', 'Standalone').encode())
        self.assertEqual(texture_platforms.strip(text, set()), text)


class InstallationFilesTests(unittest.TestCase):
    """compare_runs leaves the entries out of the listed files only, on the gate path only."""

    FILES = {'label-metas': {'modules': ('Android',), 'files': ('Assets/Labels/Tex.png.meta',)}}

    def run_dir(self, root, metas, modules=None):
        case = Path(root)/'label-metas'
        summary = {'case': 'label-metas', 'exit': 0, 'changed': sorted(metas), 'removed': []}
        if modules is not None:
            summary['modules'] = modules
        (case/'files').mkdir(parents=True)
        (case/'summary.json').write_text(__import__('json').dumps(summary))
        (case/'rows.jsonl').write_text('')
        for rel, text in metas.items():
            (case/'files'/rel).parent.mkdir(parents=True, exist_ok=True)
            (case/'files'/rel).write_text(text)
        return root

    def compare(self, reference, actual, **kwargs):
        with tempfile.TemporaryDirectory() as ref, tempfile.TemporaryDirectory() as act:
            self.run_dir(ref, reference)
            self.run_dir(act, actual, kwargs.pop('modules', None))
            with open(__import__('os').devnull, 'w') as quiet, __import__('contextlib').redirect_stdout(quiet):
                return scenesave_compare.compare_runs(ref, act, ['label-metas'], **kwargs)

    def test_only_the_listed_files_leave_the_entries_out(self):
        authored = meta('DefaultTexturePlatform', 'Android', 'iOS')
        reference = {'Assets/Labels/Tex.png.meta': meta('DefaultTexturePlatform', 'Standalone', 'Android'),
                     'Assets/Labels/Partial.png.meta': authored}
        actual = {'Assets/Labels/Tex.png.meta': meta('DefaultTexturePlatform', 'Standalone', 'Android', 'iOS'),
                  'Assets/Labels/Partial.png.meta': authored}
        self.assertEqual(self.compare(reference, actual, modules=['Android', 'iOS'], installation_files=self.FILES), 0)
        # A change to the authored iOS entry of another texture's .meta still fails.
        actual['Assets/Labels/Partial.png.meta'] = authored.replace('buildTarget: iOS\n    maxTextureSize: 2048', 'buildTarget: iOS\n    maxTextureSize: 512')
        self.assertEqual(self.compare(reference, actual, modules=['Android', 'iOS'], installation_files=self.FILES), 1)
        # The gate's run must list its modules.
        actual['Assets/Labels/Partial.png.meta'] = authored
        self.assertEqual(self.compare(reference, actual, installation_files=self.FILES), 1)

    def test_two_editor_captures_compare_without_modules(self):
        files = {'Assets/Labels/Tex.png.meta': meta('DefaultTexturePlatform', 'Standalone', 'Android')}
        self.assertEqual(self.compare(files, dict(files)), 0)


if __name__ == '__main__':
    unittest.main()
