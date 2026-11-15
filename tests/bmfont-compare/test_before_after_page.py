"""Unit tests for before_after_page.py.

Run from the repo root:
    python -m unittest discover -s tests/bmfont-compare -p "test_*.py" -v
"""

import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import before_after_page  # noqa: E402

LOG = """
Atlas PNG comparisons
  plain-freetype_0.png: IDENTICAL
  outline-small-12-o2-freetype_0.png: DIFFERENT (120 pixels, 1.5000%)
  outline-small-12-o1-gdi_0.png: DIFFERENT (80 pixels, 1.0000%)
  outline-small-12-o1-freetype_1.png: DIFFERENT (7 pixels, 0.1000%)
  outline-small-12-o1-freetype_0.png: DIFFERENT (90 pixels, 1.2000%)
  outline-small-9-o1-freetype_0.png: DIFFERENT (5 pixels, 0.1000%)
  variable-wght700-native.fnt: DIFFERENT (3 lines differ)
  variable-wght700-native.fnt: DIFFERENT (3 lines differ)
  NEW config 'brand-new' (no baseline): freetype, native
""".splitlines()


def write_png(path: Path, width: int = 8):
    Image.new("RGBA", (width, 8), (255, 255, 255, 255)).save(path)


def write_fnt(path: Path, pairs):
    lines = ["info face=\"Arial\" size=24"]
    lines += ["kerning first=%d second=%d amount=%d" % pair for pair in pairs]
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


class ParseLogTests(unittest.TestCase):
    def test_collects_only_different_atlases_grouped_by_config(self):
        atlases, _, _ = before_after_page.parse_log(LOG)
        self.assertEqual(sorted(atlases), ["outline-small-12-o1", "outline-small-12-o2", "outline-small-9-o1"])
        self.assertNotIn("plain", atlases)

    def test_rows_are_ordered_by_backend_then_page(self):
        atlases, _, _ = before_after_page.parse_log(LOG)
        self.assertEqual([(row[0], row[1]) for row in atlases["outline-small-12-o1"]],
                         [("freetype", 0), ("freetype", 1), ("gdi", 0)])
        self.assertEqual(atlases["outline-small-12-o1"][0][2:], (90, "1.2000"))

    def test_fnt_pairs_are_reported_once_and_new_configs_are_listed(self):
        _, fnts, new_configs = before_after_page.parse_log(LOG)
        self.assertEqual(fnts, [("variable-wght700", "native")])
        self.assertEqual(new_configs, ["brand-new"])

    def test_sized_configs_sort_by_outline_then_numeric_size(self):
        names = ["outline-small-12-o2", "outline-small-12-o1", "outline-small-9-o1", "fire"]
        self.assertEqual(sorted(names, key=before_after_page.config_order),
                         ["fire", "outline-small-9-o1", "outline-small-12-o1", "outline-small-12-o2"])


class KerningDeltaTests(unittest.TestCase):
    def test_splits_pairs_into_changed_added_and_removed(self):
        with tempfile.TemporaryDirectory() as tmp:
            before_path, after_path = Path(tmp, "a.fnt"), Path(tmp, "b.fnt")
            write_fnt(before_path, [(84, 111, -2), (65, 86, -1), (86, 97, -1)])
            write_fnt(after_path, [(84, 111, -3), (65, 86, -1), (89, 97, -4)])
            before = before_after_page.read_kerning(str(before_path))
            after = before_after_page.read_kerning(str(after_path))
        changed, added, removed = before_after_page.kerning_delta(before, after)
        self.assertEqual((changed, added, removed), ([(84, 111)], [(89, 97)], [(86, 97)]))

    def test_missing_file_reads_as_no_pairs(self):
        self.assertEqual(len(before_after_page.read_kerning("does-not-exist.fnt")), 0)


class BuildPageTests(unittest.TestCase):
    def test_page_shows_before_after_and_diff_and_reports_the_images_it_uses(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            name = "outline-small-12-o2-freetype_0.png"
            for prefix in ("main_", "", "diff_"):
                write_png(out / (prefix + name), width=64)
            write_png(out / "comparison-brand-new.png", width=32)
            write_fnt(out / "main_variable-wght700-native.fnt", [(84, 111, -2), (86, 97, -1)])
            write_fnt(out / "variable-wght700-native.fnt", [(84, 111, -3)])

            page, used = before_after_page.build_page(LOG, str(out), "v1 <old>", "v2")

        self.assertEqual(used[:3], ["main_" + name, name, "diff_" + name])
        self.assertIn("comparison-brand-new.png", used)
        self.assertIn("--w:64px", page)
        self.assertIn("before: v1 &lt;old&gt;", page)
        self.assertNotIn("v1 <old>", page)
        self.assertIn("2 pairs before, 1 after: 1 removed, 0 added, 1 changed value, 0 unchanged.", page)
        # Atlases the log names but the directory lacks are shown as missing, not linked.
        self.assertIn("(missing)", page)
        self.assertNotIn('src="outline-small-12-o1-gdi_0.png"', page)

    def test_copy_to_makes_a_standalone_folder(self):
        with tempfile.TemporaryDirectory() as tmp:
            out, shared, log = Path(tmp, "out"), Path(tmp, "shared"), Path(tmp, "run.log")
            out.mkdir()
            name = "outline-small-12-o2-freetype_0.png"
            for prefix in ("main_", "", "diff_"):
                write_png(out / (prefix + name))
            write_png(out / "unrelated-freetype_0.png")
            log.write_text("  %s: DIFFERENT (1 pixels, 0.1000%%)\n" % name, encoding="utf-8")

            code = before_after_page.main([str(log), "--dir", str(out), "--copy-to", str(shared)])

            self.assertEqual(code, 0)
            self.assertEqual(sorted(p.name for p in shared.iterdir()),
                             sorted(["before-after.html", "main_" + name, name, "diff_" + name]))


if __name__ == "__main__":
    unittest.main()
