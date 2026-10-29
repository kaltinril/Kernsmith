"""Unit tests for the regression harness scripts (regression_check.py, diff_comparisons.py).

Run from the repo root:
    python -m unittest discover -s tests/bmfont-compare -p "test_*.py" -v
"""

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

import regression_check  # noqa: E402

DIFF_SCRIPT = HERE / "diff_comparisons.py"

FNT = "info face=\"Arial\" size=24\ncommon lineHeight=27 base=22\nchar id=65 x=0 y=0 width=10 height=12\n"


def write_fnt(path: Path, body: str = FNT):
    path.write_text("kernsmith version=x commit=y\n" + body, encoding="utf-8")


def write_png(path: Path, color=(255, 255, 255, 255)):
    Image.new("RGBA", (4, 48), color).save(path)


def run_diff(directory: Path, *extra: str):
    result = subprocess.run(
        [sys.executable, str(DIFF_SCRIPT), "--dir", str(directory), *extra],
        capture_output=True, text=True, check=False,
    )
    return result.returncode, result.stdout + result.stderr


class DiffComparisonsTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self._tmp.name)
        # One pair present and identical on both sides so the run is never "nothing compared".
        write_fnt(self.dir / "main_plain-freetype.fnt")
        write_fnt(self.dir / "plain-freetype.fnt")

    def tearDown(self):
        self._tmp.cleanup()

    def test_identical_pair_passes(self):
        code, out = run_diff(self.dir)
        self.assertEqual(code, 0, out)
        self.assertIn("plain-freetype.fnt: IDENTICAL", out)

    def test_extended_metadata_change_on_the_kernsmith_line_is_a_difference(self):
        # The kernsmith line carries sdfSpread etc. after the version; only the version may differ.
        (self.dir / "main_sdf-stbtruetype.fnt").write_text(
            'kernsmith version="0.24.0" sdfSpread=8\n' + FNT, encoding="utf-8")
        (self.dir / "sdf-stbtruetype.fnt").write_text(
            'kernsmith version="0.24.0" sdfSpread=4\n' + FNT, encoding="utf-8")

        code, out = run_diff(self.dir)

        self.assertEqual(code, 1, out)
        self.assertIn("sdf-stbtruetype.fnt: DIFFERENT", out)

    def test_version_only_change_on_the_kernsmith_line_is_identical(self):
        (self.dir / "main_sdf-freetype.fnt").write_text(
            'kernsmith version="0.24.0" commit=abc sdfSpread=8\n' + FNT, encoding="utf-8")
        (self.dir / "sdf-freetype.fnt").write_text(
            'kernsmith version="0.25.0-dev" commit=def sdfSpread=8\n' + FNT, encoding="utf-8")

        code, out = run_diff(self.dir)

        self.assertEqual(code, 0, out)
        self.assertIn("sdf-freetype.fnt: IDENTICAL", out)

    def test_output_missing_on_branch_is_a_difference(self):
        # Base produced output, branch did not: the backend now fails this config.
        write_fnt(self.dir / "main_plain-native.fnt")

        code, out = run_diff(self.dir)

        self.assertEqual(code, 1, out)
        self.assertIn("plain-native.fnt: MISSING", out)

    def test_output_only_on_branch_is_new_not_a_failure(self):
        write_fnt(self.dir / "brandnew-freetype.fnt")
        write_fnt(self.dir / "brandnew-native.fnt")

        code, out = run_diff(self.dir)

        self.assertEqual(code, 0, out)
        self.assertIn("brandnew-freetype.fnt: NEW (no baseline)", out)
        self.assertIn("brandnew-native.fnt: NEW (no baseline)", out)
        self.assertNotIn("brandnew-freetype.fnt: IDENTICAL", out)

    def test_removed_config_is_reported_not_failed(self):
        write_fnt(self.dir / "main_gone-freetype.fnt")
        write_png(self.dir / "main_comparison-gone.png")

        code, out = run_diff(self.dir, "--removed-config", "gone")

        self.assertEqual(code, 0, out)
        self.assertIn("gone-freetype.fnt: REMOVED", out)
        self.assertIn("Per-font: gone: REMOVED", out)

    def test_per_font_png_missing_on_branch_is_a_difference(self):
        write_png(self.dir / "main_comparison-plain.png")

        code, out = run_diff(self.dir)

        self.assertEqual(code, 1, out)
        self.assertIn("Per-font: plain: MISSING", out)

    def test_per_font_png_only_on_branch_is_new(self):
        write_png(self.dir / "comparison-brandnew.png")

        code, out = run_diff(self.dir)

        self.assertEqual(code, 0, out)
        self.assertIn("Per-font: brandnew: NEW (no baseline)", out)


class RegressionCheckTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self._tmp.name)

    def tearDown(self):
        self._tmp.cleanup()

    def test_clean_removes_every_generated_file_including_baselines(self):
        for name in ["plain-freetype.fnt", "plain-freetype_0.png", "main_plain-freetype.fnt",
                     "main_comparison-plain.png", "diff_comparison-plain.png", "comparison2.png"]:
            (self.dir / name).write_text("x")
        (self.dir / "notes.txt").write_text("keep")

        regression_check.clean_generated_outputs(self.dir)

        self.assertEqual(sorted(p.name for p in self.dir.iterdir()), ["notes.txt"])

    def test_promote_moves_outputs_so_no_unprefixed_file_survives(self):
        # A copy would leave plain-native.fnt in place; if the branch then failed that pair
        # the diff would compare the base's file with itself and call it IDENTICAL.
        for name in ["plain-native.fnt", "plain-native_0.png", "comparison-plain.png", "comparison.png"]:
            (self.dir / name).write_text(name)

        moved = regression_check.promote_baselines(self.dir)

        self.assertEqual(moved, 4)
        self.assertEqual(
            sorted(p.name for p in self.dir.iterdir()),
            ["main_comparison-plain.png", "main_comparison.png",
             "main_plain-native.fnt", "main_plain-native_0.png"])

    def test_list_configs_reads_bmfc_stems(self):
        (self.dir / "plain.bmfc").write_text("")
        (self.dir / "fire.bmfc").write_text("")
        (self.dir / "Font24Arial.fnt").write_text("")

        self.assertEqual(regression_check.list_configs(self.dir), {"plain", "fire"})

    def test_parse_generate_output_collects_failures_per_pair(self):
        lines = [
            "Found 2 .bmfc files",
            "",
            "--- plain ---",
            "  freetype ... OK",
            "  native ... FAILED: Font is missing the required 'glyf' table.",
            "",
            "--- sdf ---",
            "  gdi ... SKIPPED (backend has no SDF support)",
            "  bmfont ... FAILED: exit=1 ",
        ]

        failures = regression_check.parse_generate_output(lines)

        self.assertEqual(failures, {
            ("plain", "native"): "Font is missing the required 'glyf' table.",
            ("sdf", "bmfont"): "exit=1",
        })

    def test_branch_only_failures_are_pairs_the_base_produced(self):
        write_fnt(self.dir / "main_plain-native.fnt")       # base OK, branch failed -> regression
        write_fnt(self.dir / "main_plain-freetype.fnt")     # both OK
        write_fnt(self.dir / "plain-freetype.fnt")
        write_fnt(self.dir / "main_gone-native.fnt")        # config removed on branch
        branch_failures = {
            ("plain", "native"): "boom",
            ("sdf", "gdi"): "failed on both sides",         # base produced nothing either
        }

        regressions = regression_check.find_branch_only_failures(
            self.dir, branch_failures, removed_configs={"gone"})

        self.assertEqual(regressions, [("plain", "native", "boom")])

    def test_new_config_backends_lists_who_produced_output(self):
        write_fnt(self.dir / "brandnew-freetype.fnt")
        write_fnt(self.dir / "brandnew-native.fnt")
        write_fnt(self.dir / "brandnew-extra-gdi.fnt")      # a different config sharing the prefix

        self.assertEqual(
            regression_check.backends_with_output(self.dir, "brandnew"),
            ["freetype", "native"])


if __name__ == "__main__":
    unittest.main()
