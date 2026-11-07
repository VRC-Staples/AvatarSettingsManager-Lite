import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch


SCRIPT = Path(__file__).resolve().parents[1] / "prepare-website-repair.py"
SPEC = importlib.util.spec_from_file_location("website_repair", SCRIPT)
repair = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(repair)


class WebsiteRepairTests(unittest.TestCase):
    def test_patch_changes_only_the_dependency_and_is_idempotent(self):
        published = b"before\r\n'" + repair.UNPINNED + b"'\nafter"
        expected = b"before\r\n'" + repair.PINNED + b"'\nafter"
        self.assertEqual(repair.patch_dependency(published, repair.PINNED), expected)
        self.assertEqual(repair.patch_dependency(expected, repair.PINNED), expected)

    def test_unexpected_dependencies_fail_closed(self):
        for published, source in (
            (repair.UNPINNED, repair.UNPINNED),
            (repair.UNPINNED + b"@3.1.4", repair.PINNED),
            (b"missing dependency", repair.PINNED),
            (repair.UNPINNED + b" " + repair.UNPINNED, repair.PINNED),
        ):
            with self.subTest(published=published, source=source):
                with self.assertRaises(ValueError):
                    repair.patch_dependency(published, source)

    def test_prepare_preserves_feed_and_all_other_assets_byte_for_byte(self):
        assets = {name: b"original bytes: " + name.encode() for name in repair.ASSETS}
        assets["index.json"] = json.dumps({
            "packages": {"com.staples.asm-lite": {"versions": {"1.1.0": {}}}}
        }).encode() + b"\n"
        for name in ("index.html", "app.js"):
            assets[name] = b"prefix '" + repair.UNPINNED + b"' suffix\r\n"

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "Website").mkdir()
            for name in ("index.html", "app.js"):
                (root / "Website" / name).write_bytes(repair.PINNED)
            output = root / "output"
            with patch.object(repair, "download", side_effect=assets.__getitem__):
                repair.prepare(output, root)
            self.assertEqual({path.name for path in output.iterdir()}, set(repair.ASSETS))
            for name, original in assets.items():
                expected = original
                if name in ("index.html", "app.js"):
                    expected = original.replace(repair.UNPINNED, repair.PINNED)
                self.assertEqual((output / name).read_bytes(), expected, name)


if __name__ == "__main__":
    unittest.main()
