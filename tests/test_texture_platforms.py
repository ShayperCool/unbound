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


if __name__ == '__main__':
    unittest.main()
