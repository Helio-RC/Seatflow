#!/usr/bin/env python3
"""stage_velopack_artifacts.sh 单元测试（subprocess + 临时目录）。"""

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPTS_DIR = Path(__file__).resolve().parent.parent
SCRIPT = SCRIPTS_DIR / "ci" / "stage_velopack_artifacts.sh"


class TestStageVelopackArtifacts(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.src = self.tmp / "history"
        self.dist = self.tmp / "dist"
        self.src.mkdir()

    def tearDown(self):
        self._tmp.cleanup()

    def populate(self, version: str = "2.1.0", channel: str = "win-x64"):
        (self.src / f"SeatFlow-{version}-{channel}-full.nupkg").write_bytes(b"full")
        (self.src / f"SeatFlow-{version}-{channel}-delta.nupkg").write_bytes(b"delta")
        (self.src / "SeatFlow-2.0.0-win-x64-full.nupkg").write_bytes(b"old-full")
        (self.src / "SeatFlow-win-x64-Setup.exe").write_bytes(b"setup")
        (self.src / "releases.win-x64.json").write_text("{}", encoding="utf-8")
        (self.src / "RELEASES-win-x64").write_text("legacy", encoding="utf-8")
        (self.src / "assets.win-x64.json").write_text("{}", encoding="utf-8")

    def run_script(self, is_pre: str = "false") -> subprocess.CompletedProcess:
        return subprocess.run(
            ["bash", str(SCRIPT), str(self.src), str(self.dist),
             "2.1.0", "win-x64", is_pre],
            capture_output=True, text=True,
        )

    def test_stable_copies_current_only_and_prunes_src(self):
        self.populate()
        result = self.run_script("false")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(
            sorted(p.name for p in self.dist.iterdir()),
            [
                "RELEASES-win-x64",
                "SeatFlow-2.1.0-win-x64-delta.nupkg",
                "SeatFlow-2.1.0-win-x64-full.nupkg",
                "SeatFlow-win-x64-Setup.exe",
                "releases.win-x64.json",
            ],
        )
        self.assertFalse((self.src / "SeatFlow-win-x64-Setup.exe").exists())
        self.assertFalse((self.src / "assets.win-x64.json").exists())
        self.assertTrue((self.src / "SeatFlow-2.0.0-win-x64-full.nupkg").exists())

    def test_pre_release_keeps_src_installers(self):
        self.populate()
        result = self.run_script("true")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertTrue((self.src / "SeatFlow-win-x64-Setup.exe").exists())
        self.assertTrue((self.src / "assets.win-x64.json").exists())
        self.assertTrue((self.dist / "SeatFlow-2.1.0-win-x64-delta.nupkg").exists())

    def test_missing_channel_files_fails(self):
        (self.src / "SeatFlow-2.1.0-win-x64-full.nupkg").write_bytes(b"full")
        result = self.run_script("false")
        self.assertNotEqual(result.returncode, 0)

    def test_missing_delta_is_allowed(self):
        self.populate()
        (self.src / "SeatFlow-2.1.0-win-x64-delta.nupkg").unlink()
        result = self.run_script("false")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse((self.dist / "SeatFlow-2.1.0-win-x64-delta.nupkg").exists())


if __name__ == "__main__":
    unittest.main()
