#!/usr/bin/env python3
"""sync_velopack_history.py 单元测试。

无需安装 oss2：仅测试纯函数与 FakeBucket 替身。
"""

import hashlib
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

from packaging.version import Version

SCRIPTS_DIR = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(SCRIPTS_DIR / "ci"))

from sync_velopack_history import (  # noqa: E402
    Asset,
    SyncError,
    find_gaps,
    parse_remote_assets,
    reconcile,
    run_sync,
    select_keep_set,
)


class FakeObjectResult:
    """oss2 ObjectResult 的最小替身（可迭代）。"""

    def __init__(self, data: bytes):
        self._data = data

    def __iter__(self):
        yield self._data


class FakeBucket:
    """oss2.Bucket 的最小替身：只实现 get_object。"""

    def __init__(self, objects: dict[str, bytes]):
        self.objects = objects
        self.downloads: list[str] = []

    def get_object(self, key: str) -> FakeObjectResult:
        self.downloads.append(key)
        return FakeObjectResult(self.objects[key])


def remote_entry(name: str, data: bytes) -> SimpleNamespace:
    return SimpleNamespace(
        key=f"updates/{name}",
        size=len(data),
        etag=hashlib.md5(data).hexdigest(),
    )


def make_asset(name: str, version: str, kind: str, data: bytes = b"x",
               etag: str | None = None, size: int | None = None) -> Asset:
    return Asset(
        key=f"updates/{name}",
        name=name,
        size=len(data) if size is None else size,
        etag=hashlib.md5(data).hexdigest() if etag is None else etag,
        version=Version(version),
        version_str=version,
        kind=kind,
    )


class TestParseRemoteAssets(unittest.TestCase):
    def test_filters_channel_and_non_nupkg(self):
        entries = [
            remote_entry("SeatFlow-2.0.0-win-x64-full.nupkg", b"a"),
            remote_entry("SeatFlow-2.0.0-win-x64-delta.nupkg", b"b"),
            remote_entry("SeatFlow-2.0.0-linux-x64-full.nupkg", b"c"),
            remote_entry("SeatFlow-win-x64-Setup.exe", b"d"),
            remote_entry("assets.win-x64.json", b"e"),
        ]
        assets = parse_remote_assets(entries, "SeatFlow", "win-x64")
        self.assertEqual(
            sorted(a.name for a in assets),
            ["SeatFlow-2.0.0-win-x64-delta.nupkg", "SeatFlow-2.0.0-win-x64-full.nupkg"],
        )

    def test_parses_prerelease_version(self):
        entries = [remote_entry("SeatFlow-2.2.0-beta.1-win-x64-full.nupkg", b"a")]
        assets = parse_remote_assets(entries, "SeatFlow", "win-x64")
        self.assertEqual(len(assets), 1)
        self.assertEqual(assets[0].version_str, "2.2.0-beta.1")
        self.assertEqual(assets[0].kind, "full")

    def test_ignores_unparseable_version(self):
        entries = [remote_entry("SeatFlow-not-a-version-win-x64-full.nupkg", b"a")]
        self.assertEqual(parse_remote_assets(entries, "SeatFlow", "win-x64"), [])


class TestSelectKeepSet(unittest.TestCase):
    def test_keeps_all_deltas_and_n_latest_fulls(self):
        assets = [
            make_asset("full-1.0.0.nupkg", "1.0.0", "full"),
            make_asset("full-2.0.0.nupkg", "2.0.0", "full"),
            make_asset("full-3.0.0.nupkg", "3.0.0", "full"),
            make_asset("delta-3.0.0.nupkg", "3.0.0", "delta"),
        ]
        keep = select_keep_set(assets, 2)
        self.assertEqual(sorted(keep), ["delta-3.0.0.nupkg", "full-2.0.0.nupkg", "full-3.0.0.nupkg"])

    def test_keeps_all_fulls_when_fewer_than_limit(self):
        assets = [make_asset("full-1.0.0.nupkg", "1.0.0", "full")]
        self.assertEqual(sorted(select_keep_set(assets, 2)), ["full-1.0.0.nupkg"])


class TestFindGaps(unittest.TestCase):
    def test_reports_full_without_delta(self):
        assets = [
            make_asset("full-1.0.0.nupkg", "1.0.0", "full"),
            make_asset("full-2.0.0.nupkg", "2.0.0", "full"),
            make_asset("full-3.0.0.nupkg", "3.0.0", "full"),
            make_asset("delta-3.0.0.nupkg", "3.0.0", "delta"),
        ]
        self.assertEqual(find_gaps(assets), ["2.0.0"])

    def test_oldest_full_is_never_a_gap(self):
        assets = [
            make_asset("full-1.0.0.nupkg", "1.0.0", "full"),
            make_asset("full-2.0.0.nupkg", "2.0.0", "full"),
        ]
        self.assertEqual(find_gaps(assets), ["2.0.0"])


class TestReconcile(unittest.TestCase):
    def test_downloads_missing_and_deletes_extra(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            remote_payload = b"remote-full"
            (out / "SeatFlow-9.0.0-win-x64-full.nupkg").write_bytes(b"unpublished")
            (out / "SeatFlow-1.0.0-win-x64-full.nupkg").write_bytes(b"1.0.0-full")

            present = make_asset("SeatFlow-1.0.0-win-x64-full.nupkg", "1.0.0", "full", data=b"1.0.0-full")
            missing = make_asset("SeatFlow-2.0.0-win-x64-full.nupkg", "2.0.0", "full", data=remote_payload)
            bucket = FakeBucket({missing.key: remote_payload})

            downloaded, deleted, downloaded_bytes, deleted_bytes = reconcile(
                bucket, {present.name: present, missing.name: missing}, out, dry_run=False)

            self.assertEqual((downloaded, deleted), (1, 1))
            self.assertEqual((downloaded_bytes, deleted_bytes), (len(remote_payload), len(b"unpublished")))
            self.assertEqual((out / missing.name).read_bytes(), remote_payload)
            self.assertFalse((out / "SeatFlow-9.0.0-win-x64-full.nupkg").exists())
            self.assertEqual(bucket.downloads, [missing.key])

    def test_dry_run_makes_no_changes(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            (out / "SeatFlow-9.0.0-win-x64-full.nupkg").write_bytes(b"unpublished")
            asset = make_asset("SeatFlow-2.0.0-win-x64-full.nupkg", "2.0.0", "full", data=b"payload")
            bucket = FakeBucket({asset.key: b"payload"})

            downloaded, deleted, downloaded_bytes, deleted_bytes = reconcile(
                bucket, {asset.name: asset}, out, dry_run=True)

            self.assertEqual((downloaded, deleted), (1, 1))
            self.assertEqual((downloaded_bytes, deleted_bytes), (len(b"payload"), len(b"unpublished")))
            self.assertEqual(bucket.downloads, [])
            self.assertFalse((out / asset.name).exists())
            self.assertTrue((out / "SeatFlow-9.0.0-win-x64-full.nupkg").exists())

    def test_dry_run_does_not_create_output_dir(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp) / "history"
            asset = make_asset("SeatFlow-2.0.0-win-x64-full.nupkg", "2.0.0", "full", data=b"payload")
            bucket = FakeBucket({asset.key: b"payload"})

            downloaded, deleted, downloaded_bytes, deleted_bytes = reconcile(
                bucket, {asset.name: asset}, out, dry_run=True)

            self.assertFalse(out.exists())
            self.assertEqual(bucket.downloads, [])
            self.assertEqual((downloaded, deleted), (1, 0))
            self.assertEqual((downloaded_bytes, deleted_bytes), (len(b"payload"), 0))

    def test_existing_file_with_matching_size_is_kept(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            local = out / "SeatFlow-2.0.0-win-x64-full.nupkg"
            local.write_bytes(b"LOCAL-CONTENT!")
            asset = make_asset(local.name, "2.0.0", "full", data=b"REMOTE-CONTENT")
            bucket = FakeBucket({asset.key: b"REMOTE-CONTENT"})

            downloaded, deleted, downloaded_bytes, deleted_bytes = reconcile(
                bucket, {asset.name: asset}, out, dry_run=False)

            self.assertEqual((downloaded, deleted), (0, 0))
            self.assertEqual((downloaded_bytes, deleted_bytes), (0, 0))
            self.assertEqual(bucket.downloads, [])
            self.assertEqual(local.read_bytes(), b"LOCAL-CONTENT!")

    def test_size_mismatch_raises_and_removes_temp(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            payload = b"short"
            bad = make_asset("SeatFlow-2.0.0-win-x64-full.nupkg", "2.0.0", "full",
                             data=payload, size=len(payload) + 1)
            bucket = FakeBucket({bad.key: payload})

            with self.assertRaises(SyncError):
                reconcile(bucket, {bad.name: bad}, out, dry_run=False)

            self.assertFalse((out / bad.name).exists())
            self.assertEqual(list(out.glob("*.incomplete")), [])

    def test_md5_mismatch_raises(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            payload = b"payload"
            bad = make_asset("SeatFlow-2.0.0-win-x64-full.nupkg", "2.0.0", "full",
                             data=payload, etag="0" * 32)
            bucket = FakeBucket({bad.key: payload})

            with self.assertRaises(SyncError):
                reconcile(bucket, {bad.name: bad}, out, dry_run=False)

            self.assertFalse((out / bad.name).exists())
            self.assertEqual(list(out.glob("*.incomplete")), [])


class TestRunSync(unittest.TestCase):
    def test_applies_keep_set_and_returns_zero(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            (out / "SeatFlow-9.0.0-win-x64-full.nupkg").write_bytes(b"unpublished")
            full_payload = b"1.0.0-full"
            delta_payload = b"2.0.0-delta"
            entries = [
                remote_entry("SeatFlow-1.0.0-win-x64-full.nupkg", full_payload),
                remote_entry("SeatFlow-2.0.0-win-x64-delta.nupkg", delta_payload),
            ]
            bucket = FakeBucket({
                "updates/SeatFlow-1.0.0-win-x64-full.nupkg": full_payload,
                "updates/SeatFlow-2.0.0-win-x64-delta.nupkg": delta_payload,
            })

            code = run_sync(bucket, entries, "win-x64", out, full_keep=1)

            self.assertEqual(code, 0)
            self.assertEqual(
                sorted(p.name for p in out.glob("*.nupkg")),
                ["SeatFlow-1.0.0-win-x64-full.nupkg", "SeatFlow-2.0.0-win-x64-delta.nupkg"],
            )


if __name__ == "__main__":
    unittest.main()
