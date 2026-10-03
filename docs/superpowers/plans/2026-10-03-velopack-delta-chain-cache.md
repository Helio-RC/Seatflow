# Velopack 增量链 + Action 缓存 + OSS 回填 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让稳定版发布在打包前从 OSS 同步完整 Velopack 历史（全部 delta + 最近 2 个 full），通过 Action 缓存跨运行持久化，使 `releases.{channel}.json` 携带完整 delta 链。

**Architecture:** 新增 Python 同步脚本（oss2，OSS 为唯一真源，双向对账）与 bash 暂存脚本；`release.yml` 稳定版走「restore 缓存 → OSS 同步 → pack 到 `publish/history` → 暂存 `publish/dist` → save 缓存」，预发布维持现状；移除 NuGet 缓存；CI 钉 vpk 1.2.161。

**Tech Stack:** GitHub Actions（actions/cache@v6 restore/save 子动作、upload-artifact@v7）、Python 3.12（oss2、packaging、pytest+unittest）、Bash、actionlint 1.7.7、Velopack vpk 1.2.161。

**Spec:** `docs/superpowers/specs/2026-10-03-velopack-delta-chain-cache-design.md`

## Global Constraints

- CI 与 `dotnet-tools.json` 的 vpk 固定为 `1.2.161`（与客户端 `Velopack` 包一致）。
- 保留集 = 全部历史 `-delta.nupkg` + 版本最高的 **2** 个 `-full.nupkg`；不设版本数上限。
- OSS 是唯一真源：每次稳定版构建双向对账（下载缺失；删除本地 OSS 上不存在的 `*.nupkg`）。同步失败**硬失败**（非零退出，不发版）。
- 缓存仅加速：`actions/cache/save` 使用 `continue-on-error: true`。
- build job **不加** `environment:`；使用仓库级 `OSS_KEY_ID/OSS_KEY_SECRET` + `vars.OSS_ENDPOINT/OSS_BUCKET`。
- 预发布（`workflow_dispatch`，`is_pre == 'true'`）不 restore/sync/save 缓存，`fetch_previous.sh` 保留。
- 稳定版 pack 输出 `publish/history`（缓存），artifacts 只含 `publish/dist`（本次产物）。
- 所有工作流 YAML 必须通过 `actionlint -shellcheck= -pyflakes=`。
- 新增脚本的注释、日志、文档一律中文；Python 脚本在缺失 `oss2` 时必须可导入（`try/except ImportError`）。
- 不修改 `publish.yml`、`publish-web.yml`、客户端代码、OSS 归档。

---

## File Structure

| 文件 | 动作 | 职责 |
|---|---|---|
| `scripts/ci/sync_velopack_history.py` | Create | OSS → 本地打包历史同步（保留集/双向对账/缺口报告） |
| `scripts/tests/test_sync_velopack_history.py` | Create | 上述脚本单测（mock bucket，无需 oss2 安装） |
| `scripts/ci/stage_velopack_artifacts.sh` | Create | 暂存本次产物到 `publish/dist`；稳定版清理历史目录 |
| `scripts/tests/test_stage_velopack_artifacts.py` | Create | 暂存脚本单测（subprocess + 临时目录） |
| `.github/workflows/release.yml` | Modify | 稳定版缓存+同步、vpk 钉版、暂存、timeout |
| `.github/workflows/actionlint.yml` | Create | workflow YAML 校验门 |
| `.github/workflows/unit-tests.yml` | Modify | 移除 NuGet 缓存 |
| `dotnet-tools.json` | Modify | vpk 1.2.0 → 1.2.161 |
| `.github/docs/RELEASE_FLOW.md` | Modify | 流水线/delta/缓存/密钥文档 |
| `scripts/ToolsCollection.md` | Modify | 新增 ci 脚本章节 |
| `docs/INDEX.md` | Modify | 文档地图与联动规则加入 RELEASE_FLOW |
| `AGENTS.md` | Modify | 脚本工具节补充 `scripts/ci/` |
| `CHANGELOG.md` | Modify | 2.1.0 条目 |

---

### Task 1: 同步脚本 `sync_velopack_history.py`（TDD）

**Files:**
- Create: `scripts/ci/sync_velopack_history.py`
- Test: `scripts/tests/test_sync_velopack_history.py`

**Interfaces:**
- Consumes: 无（新脚本）
- Produces（Task 3 的 workflow 依赖其 CLI）：
  - `python3 scripts/ci/sync_velopack_history.py --channel <rid> --output-dir <dir> [--pack-id SeatFlow] [--full-keep 2] [--dry-run]`，环境变量 `OSS_KEY_ID/OSS_KEY_SECRET/OSS_ENDPOINT/OSS_BUCKET`；成功退出码 0，任何失败 1。
  - Python API（测试与后续脚本可能复用）：`Asset(key, name, size, etag, version, version_str, kind)`、`SyncError`、`parse_remote_assets(entries, pack_id, channel) -> list[Asset]`、`select_keep_set(assets, full_keep) -> dict[str, Asset]`、`find_gaps(assets) -> list[str]`、`reconcile(bucket, keep, output_dir, dry_run) -> tuple[int, int]`、`run_sync(bucket, entries, channel, output_dir, pack_id=..., full_keep=..., dry_run=False) -> int`。

- [ ] **Step 1: 安装测试依赖**

```bash
python3 -m pip install --break-system-packages pytest packaging
```

预期：`pytest` 与 `packaging` 导入成功（若无网络/权限，退路：`python3 -m unittest discover -s scripts/tests -v`，但本任务测试依赖 `packaging`，必须先装上）。

- [ ] **Step 2: 编写失败的测试**

创建 `scripts/tests/test_sync_velopack_history.py`：

```python
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

            downloaded, deleted = reconcile(bucket, {present.name: present, missing.name: missing}, out, dry_run=False)

            self.assertEqual((downloaded, deleted), (1, 1))
            self.assertEqual((out / missing.name).read_bytes(), remote_payload)
            self.assertFalse((out / "SeatFlow-9.0.0-win-x64-full.nupkg").exists())
            self.assertEqual(bucket.downloads, [missing.key])

    def test_dry_run_makes_no_changes(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            (out / "SeatFlow-9.0.0-win-x64-full.nupkg").write_bytes(b"unpublished")
            asset = make_asset("SeatFlow-2.0.0-win-x64-full.nupkg", "2.0.0", "full", data=b"payload")
            bucket = FakeBucket({asset.key: b"payload"})

            downloaded, deleted = reconcile(bucket, {asset.name: asset}, out, dry_run=True)

            self.assertEqual((downloaded, deleted), (1, 1))
            self.assertEqual(bucket.downloads, [])
            self.assertFalse((out / asset.name).exists())
            self.assertTrue((out / "SeatFlow-9.0.0-win-x64-full.nupkg").exists())

    def test_existing_file_with_matching_size_is_kept(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            local = out / "SeatFlow-2.0.0-win-x64-full.nupkg"
            local.write_bytes(b"LOCAL-CONTENT")
            asset = make_asset(local.name, "2.0.0", "full", data=b"REMOTE-CONTENT")
            bucket = FakeBucket({asset.key: b"REMOTE-CONTENT"})

            downloaded, deleted = reconcile(bucket, {asset.name: asset}, out, dry_run=False)

            self.assertEqual((downloaded, deleted), (0, 0))
            self.assertEqual(bucket.downloads, [])
            self.assertEqual(local.read_bytes(), b"LOCAL-CONTENT")

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
```

- [ ] **Step 3: 运行测试确认失败**

```bash
cd scripts && python3 -m pytest tests/test_sync_velopack_history.py -v
```

预期：collection error — `ModuleNotFoundError: No module named 'sync_velopack_history'`。

- [ ] **Step 4: 实现脚本**

创建 `scripts/ci/sync_velopack_history.py`：

```python
#!/usr/bin/env python3
"""CI 同步 Velopack 历史包 — 以 OSS 为唯一真源补齐本地打包目录。

职责:
  1. 列举 OSS updates/ 下本通道的 full/delta nupkg
  2. 保留集 = 全部 delta + 版本最高的 N 个 full（默认 2）
  3. 下载缺失文件（size + MD5/ETag 校验）
  4. 删除本地不在保留集的 *.nupkg（防止未发布版本进入 feed）
  5. 缺口报告：有 full 但无指向它的 delta（仅警告，不重建）

用法:
  OSS_KEY_ID=... OSS_KEY_SECRET=... OSS_ENDPOINT=... OSS_BUCKET=... \
  python3 scripts/ci/sync_velopack_history.py \
    --channel win-x64 --output-dir publish/history [--full-keep 2] [--dry-run]
"""

from __future__ import annotations

import argparse
import hashlib
import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Optional

try:
    import oss2
except ImportError:
    oss2 = None  # type: ignore

from packaging.version import InvalidVersion, Version

REMOTE_PREFIX = "updates/"
DEFAULT_PACK_ID = "SeatFlow"
DEFAULT_FULL_KEEP = 2
CHUNK_SIZE = 1024 * 1024


class SyncError(RuntimeError):
    """同步失败（硬失败）。"""


@dataclass(frozen=True)
class Asset:
    key: str
    name: str
    size: int
    etag: str
    version: Version
    version_str: str
    kind: str  # "full" | "delta"


def _asset_pattern(pack_id: str, channel: str) -> re.Pattern[str]:
    return re.compile(
        rf"^{re.escape(pack_id)}-(?P<version>.+)-{re.escape(channel)}-(?P<kind>full|delta)\.nupkg$"
    )


def parse_remote_assets(entries: Iterable[object], pack_id: str, channel: str) -> list[Asset]:
    """从 OSS 列举结果中过滤并解析本通道 nupkg。"""
    pattern = _asset_pattern(pack_id, channel)
    assets: list[Asset] = []
    for entry in entries:
        name = str(getattr(entry, "key", "")).rsplit("/", 1)[-1]
        match = pattern.match(name)
        if not match:
            continue
        version_str = match.group("version")
        try:
            version = Version(version_str)
        except InvalidVersion:
            print(f"  ! 跳过无法解析的版本: {name}")
            continue
        assets.append(Asset(
            key=str(entry.key),
            name=name,
            size=int(entry.size),
            etag=str(getattr(entry, "etag", "") or "").strip('"'),
            version=version,
            version_str=version_str,
            kind=match.group("kind"),
        ))
    return assets


def select_keep_set(assets: Iterable[Asset], full_keep: int) -> dict[str, Asset]:
    """保留集 = 全部 delta + 版本最高的 full_keep 个 full。"""
    keep: dict[str, Asset] = {}
    fulls: list[Asset] = []
    for asset in assets:
        if asset.kind == "delta":
            keep[asset.name] = asset
        else:
            fulls.append(asset)
    fulls.sort(key=lambda item: item.version, reverse=True)
    for asset in fulls[: max(full_keep, 0)]:
        keep[asset.name] = asset
    return keep


def find_gaps(assets: Iterable[Asset]) -> list[str]:
    """返回「存在 full 但没有指向它的 delta」的版本（除最早的 full）。"""
    fulls = sorted((a for a in assets if a.kind == "full"), key=lambda item: item.version)
    delta_targets = {a.version for a in assets if a.kind == "delta"}
    return [full.version_str for full in fulls[1:] if full.version not in delta_targets]


def _md5_file(path: Path) -> str:
    digest = hashlib.md5()
    with open(path, "rb") as handle:
        while chunk := handle.read(CHUNK_SIZE):
            digest.update(chunk)
    return digest.hexdigest()


def _download_one(bucket, asset: Asset, output_dir: Path) -> None:
    target = output_dir / asset.name
    tmp = output_dir / f"{asset.name}.incomplete"
    result = bucket.get_object(asset.key)
    try:
        with open(tmp, "wb") as handle:
            for chunk in result:
                handle.write(chunk)
        if tmp.stat().st_size != asset.size:
            raise SyncError(f"大小校验失败: {asset.name}（期望 {asset.size} 字节）")
        if re.fullmatch(r"[0-9a-fA-F]{32}", asset.etag) and _md5_file(tmp) != asset.etag.lower():
            raise SyncError(f"MD5 校验失败: {asset.name}")
    except Exception:
        tmp.unlink(missing_ok=True)
        raise
    tmp.replace(target)


def reconcile(bucket, keep: dict[str, Asset], output_dir: Path, dry_run: bool) -> tuple[int, int]:
    """双向对账：下载缺失/大小不符者，删除本地不在保留集的 nupkg。"""
    output_dir.mkdir(parents=True, exist_ok=True)
    downloaded = 0
    for name in sorted(keep):
        asset = keep[name]
        local = output_dir / name
        if local.exists() and local.stat().st_size == asset.size:
            continue
        if dry_run:
            print(f"  [dry-run] 下载 {name}")
            downloaded += 1
            continue
        _download_one(bucket, asset, output_dir)
        print(f"  ↓ {name}")
        downloaded += 1

    deleted = 0
    keep_names = set(keep)
    for local in sorted(output_dir.glob("*.nupkg")):
        if local.name in keep_names:
            continue
        if dry_run:
            print(f"  [dry-run] 删除 {local.name}")
        else:
            local.unlink()
            print(f"  ✗ 删除 {local.name}")
        deleted += 1
    return downloaded, deleted


def run_sync(bucket, entries: Iterable[object], channel: str, output_dir: Path,
             pack_id: str = DEFAULT_PACK_ID, full_keep: int = DEFAULT_FULL_KEEP,
             dry_run: bool = False) -> int:
    """执行一次同步。返回 0（失败由异常向上抛）。"""
    assets = parse_remote_assets(entries, pack_id, channel)
    keep = select_keep_set(assets, full_keep)
    gaps = find_gaps(assets)
    print(f"[sync] 远端 {channel}: {len(assets)} 个 nupkg（保留 {len(keep)}）")
    downloaded, deleted = reconcile(bucket, keep, output_dir, dry_run)
    print(f"[sync] 下载 {downloaded}，删除 {deleted}")
    if gaps:
        print(f"[sync] ! 缺口（有 full 无 delta）: {', '.join(gaps)}")
    else:
        print("[sync] 无缺口")
    return 0


def _env(name: str) -> Optional[str]:
    value = os.environ.get(name, "").strip()
    return value or None


def main(argv: Optional[list[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="同步 Velopack 历史包（OSS → 本地）")
    parser.add_argument("--channel", required=True, help="渠道名（如 win-x64）")
    parser.add_argument("--output-dir", required=True, help="本地打包目录（如 publish/history）")
    parser.add_argument("--pack-id", default=DEFAULT_PACK_ID, help="Velopack packId")
    parser.add_argument("--full-keep", type=int, default=DEFAULT_FULL_KEEP, help="保留的 full 数量")
    parser.add_argument("--dry-run", action="store_true", help="只打印计划，不下载/删除")
    args = parser.parse_args(argv)

    if oss2 is None:
        print("✗ 缺少 oss2 依赖，请先 pip install oss2")
        return 1

    required = ["OSS_KEY_ID", "OSS_KEY_SECRET", "OSS_ENDPOINT", "OSS_BUCKET"]
    missing = [name for name in required if not _env(name)]
    if missing:
        print(f"✗ OSS 凭证/配置缺失: {', '.join(missing)}")
        return 1

    auth = oss2.Auth(_env("OSS_KEY_ID"), _env("OSS_KEY_SECRET"))
    bucket = oss2.Bucket(auth, _env("OSS_ENDPOINT"), _env("OSS_BUCKET"))

    try:
        entries = oss2.ObjectIteratorV2(bucket, prefix=REMOTE_PREFIX)
        return run_sync(bucket, entries, args.channel, Path(args.output_dir),
                        pack_id=args.pack_id, full_keep=args.full_keep,
                        dry_run=args.dry_run)
    except Exception as exc:  # noqa: BLE001 — 顶层硬失败出口
        print(f"✗ 同步失败: {exc}")
        return 1


if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 5: 运行测试确认通过**

```bash
cd scripts && python3 -m pytest tests/test_sync_velopack_history.py -v
```

预期：13 passed（0 failed）。

- [ ] **Step 6: 回归全部脚本测试**

```bash
cd scripts && python3 -m pytest tests/ -v
```

预期：现有 `test_i18n.py` / `test_release.py` / `test_version.py` 与本测试全部通过。

- [ ] **Step 7: 赋可执行权限并提交**

```bash
chmod +x scripts/ci/sync_velopack_history.py
git add scripts/ci/sync_velopack_history.py scripts/tests/test_sync_velopack_history.py
git commit -m "feat(ci): 新增 Velopack 历史同步脚本（OSS 真源、双向对账、缺口报告）"
```

---

### Task 2: 暂存脚本 `stage_velopack_artifacts.sh`（TDD）

**Files:**
- Create: `scripts/ci/stage_velopack_artifacts.sh`
- Test: `scripts/tests/test_stage_velopack_artifacts.py`

**Interfaces:**
- Consumes: 无
- Produces（Task 3 workflow 调用）：
  - `bash scripts/ci/stage_velopack_artifacts.sh <src-dir> <dist-dir> <version> <channel> <is-pre>`
  - 退出码 0 表示成功；源目录不存在或找不到 `releases.*.json` / `RELEASES-*` 时非零退出。

- [ ] **Step 1: 编写失败的测试**

创建 `scripts/tests/test_stage_velopack_artifacts.py`：

```python
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
```

- [ ] **Step 2: 运行测试确认失败**

```bash
cd scripts && python3 -m pytest tests/test_stage_velopack_artifacts.py -v
```

预期：4 failed（脚本不存在，subprocess 返回非 0）。

- [ ] **Step 3: 实现脚本**

创建 `scripts/ci/stage_velopack_artifacts.sh`：

```bash
#!/usr/bin/env bash
# 暂存本次发布产物到 dist 目录，并从历史目录清理不进入缓存的安装包。
#
# 用法: stage_velopack_artifacts.sh <src-dir> <dist-dir> <version> <channel> <is-pre>
#   src-dir : vpk pack 输出目录（稳定版 publish/history；预发布 publish/out）
#   dist-dir: 暂存目录（publish/dist），供 upload-artifact 使用
#   is-pre  : 'true' 时保留 src 中的安装包（预发布不缓存历史目录）
set -euo pipefail
shopt -s nullglob

SRC="${1:?src-dir 必填}"
DIST="${2:?dist-dir 必填}"
VERSION="${3:?version 必填}"
CHANNEL="${4:?channel 必填}"
IS_PRE="${5:-false}"

[ -d "$SRC" ] || { echo "✗ 源目录不存在: $SRC"; exit 1; }
mkdir -p "$DIST"

copied=0

# 安装包（pack 每次覆盖同名，天然只有当前版本）
for f in "$SRC"/*.exe "$SRC"/*.AppImage "$SRC"/*.pkg "$SRC"/*.dmg; do
  cp "$f" "$DIST/"
  copied=$((copied + 1))
done

# 本次版本的 nupkg（首次/预发布可能没有 delta）
for kind in full delta; do
  f="$SRC/SeatFlow-${VERSION}-${CHANNEL}-${kind}.nupkg"
  if [ -f "$f" ]; then
    cp "$f" "$DIST/"
    copied=$((copied + 1))
  fi
done

# channel 文件（feed 必须存在）
feeds=( "$SRC"/releases.*.json "$SRC"/RELEASES-* )
if [ ${#feeds[@]} -eq 0 ]; then
  echo "✗ 未找到 channel 文件（releases.*.json / RELEASES-*）"
  exit 1
fi
for f in "${feeds[@]}"; do
  cp "$f" "$DIST/"
  copied=$((copied + 1))
done

# 稳定版：历史目录只保留 nupkg 与 channel 文件（安装包、Portable.zip、assets.*.json 等全部清理）
if [ "$IS_PRE" != "true" ]; then
  for f in "$SRC"/*; do
    [ -f "$f" ] || continue
    case "$(basename "$f")" in
      *.nupkg|releases.*.json|RELEASES-*) ;;
      *) rm -f "$f" ;;
    esac
  done
fi

echo "✓ 暂存 $copied 个文件 → $DIST"
ls -la "$DIST"
```

- [ ] **Step 4: 运行测试确认通过**

```bash
cd scripts && python3 -m pytest tests/test_stage_velopack_artifacts.py -v
```

预期：4 passed。

- [ ] **Step 5: 赋可执行权限并提交**

```bash
chmod +x scripts/ci/stage_velopack_artifacts.sh
git add scripts/ci/stage_velopack_artifacts.sh scripts/tests/test_stage_velopack_artifacts.py
git commit -m "feat(ci): 新增发布产物暂存脚本（dist 与历史目录分离）"
```

---

### Task 3: `release.yml` 改造 + `actionlint.yml` 门禁

**Files:**
- Modify: `.github/workflows/release.yml`
- Create: `.github/workflows/actionlint.yml`

**Interfaces:**
- Consumes: Task 1 的 `sync_velopack_history.py` CLI、Task 2 的 `stage_velopack_artifacts.sh` CLI。
- Produces: 稳定版 artifacts `release-<rid>` 只含本次产物；历史缓存 key `vpk-history-<rid>-<run_id>-<run_attempt>`。

- [ ] **Step 1: 删除 NuGet 缓存步骤**

在 `.github/workflows/release.yml` 中删除：

```yaml
      - name: Cache NuGet packages
        uses: actions/cache@v6
        with:
          path: ~/.nuget/packages
          key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}
          restore-keys: |
            ${{ runner.os }}-nuget-
```

- [ ] **Step 2: vpk 钉版**

把：

```yaml
      - name: Install vpk
        run: dotnet tool install -g vpk && echo "$HOME/.dotnet/tools" >> "$GITHUB_PATH"
```

改为：

```yaml
      - name: Install vpk
        run: dotnet tool install -g vpk --version 1.2.161 && echo "$HOME/.dotnet/tools" >> "$GITHUB_PATH"
```

- [ ] **Step 3: 在 Install vpk 之后插入历史缓存与同步步骤**

在 `Install vpk` 步骤之后、`dotnet publish` 之前插入：

```yaml
      - name: Restore Velopack history cache
        if: needs.preflight.outputs.is_pre != 'true'
        uses: actions/cache/restore@v6
        with:
          path: publish/history
          key: vpk-history-${{ matrix.rid }}-${{ github.run_id }}-${{ github.run_attempt }}
          restore-keys: |
            vpk-history-${{ matrix.rid }}-

      - name: Install oss2
        if: needs.preflight.outputs.is_pre != 'true'
        run: pip install oss2 packaging

      - name: Sync Velopack history from OSS
        if: needs.preflight.outputs.is_pre != 'true'
        env:
          OSS_KEY_ID: ${{ secrets.OSS_KEY_ID }}
          OSS_KEY_SECRET: ${{ secrets.OSS_KEY_SECRET }}
          OSS_ENDPOINT: ${{ vars.OSS_ENDPOINT }}
          OSS_BUCKET: ${{ vars.OSS_BUCKET }}
        run: |
          python3 scripts/ci/sync_velopack_history.py \
            --channel ${{ matrix.rid }} \
            --output-dir publish/history
```

- [ ] **Step 4: fetch_previous 仅预发布执行**

把：

```yaml
      - name: Fetch previous release (delta base)
        shell: bash
```

改为：

```yaml
      - name: Fetch previous release (delta base)
        if: needs.preflight.outputs.is_pre == 'true'
        shell: bash
```

（该步骤其余内容不变。）

- [ ] **Step 5: pack 输出目录按稳定/预发布分叉**

在 `vpk pack` 步骤的 `env:` 末尾追加一行：

```yaml
          VPK_OUTPUT_DIR: ${{ needs.preflight.outputs.is_pre == 'true' && 'publish/out' || 'publish/history' }}
```

并把命令中的：

```yaml
            --outputDir publish/out \
```

改为：

```yaml
            --outputDir "$VPK_OUTPUT_DIR" \
```

- [ ] **Step 6: 用暂存 + 缓存保存替换 artifacts 上传**

把整个 `Upload artifacts` 步骤：

```yaml
      - name: Upload artifacts
        uses: actions/upload-artifact@v7
        with:
          name: release-${{ matrix.rid }}
          path: |
            publish/out/*.exe
            publish/out/*.AppImage
            publish/out/*.pkg
            publish/out/*.dmg
            publish/out/*.nupkg
            publish/out/releases.*.json
            publish/out/RELEASES-*
          if-no-files-found: error
```

替换为：

```yaml
      - name: Stage release artifacts
        shell: bash
        env:
          VPK_OUTPUT_DIR: ${{ needs.preflight.outputs.is_pre == 'true' && 'publish/out' || 'publish/history' }}
        run: |
          bash scripts/ci/stage_velopack_artifacts.sh \
            "$VPK_OUTPUT_DIR" publish/dist \
            "${{ needs.preflight.outputs.version }}" \
            "${{ matrix.rid }}" \
            "${{ needs.preflight.outputs.is_pre }}"

      - name: Save Velopack history cache
        if: needs.preflight.outputs.is_pre != 'true'
        continue-on-error: true
        uses: actions/cache/save@v6
        with:
          path: publish/history
          key: vpk-history-${{ matrix.rid }}-${{ github.run_id }}-${{ github.run_attempt }}

      - name: Upload artifacts
        uses: actions/upload-artifact@v7
        with:
          name: release-${{ matrix.rid }}
          path: publish/dist/*
          if-no-files-found: error
```

- [ ] **Step 7: build job 超时 45 → 60 分钟**

把 build job 的：

```yaml
    timeout-minutes: 45
```

改为：

```yaml
    timeout-minutes: 60
```

- [ ] **Step 8: 新增 actionlint 工作流**

创建 `.github/workflows/actionlint.yml`：

```yaml
name: Actionlint

on:
  push:
    paths: ['.github/workflows/**']
  pull_request:
    paths: ['.github/workflows/**']

permissions:
  contents: read

jobs:
  actionlint:
    runs-on: ubuntu-latest
    timeout-minutes: 10
    steps:
      - uses: actions/checkout@v7

      - name: Download actionlint
        run: |
          ACTIONLINT_VERSION=1.7.7
          ACTIONLINT_SHA256=023070a287cd8cccd71515fedc843f1985bf96c436b7effaecce67290e7e0757
          curl -fsSLo actionlint.tar.gz \
            "https://github.com/rhysd/actionlint/releases/download/v${ACTIONLINT_VERSION}/actionlint_${ACTIONLINT_VERSION}_linux_amd64.tar.gz"
          echo "${ACTIONLINT_SHA256}  actionlint.tar.gz" | sha256sum -c -
          tar -xzf actionlint.tar.gz actionlint

      - name: Run actionlint
        run: ./actionlint -shellcheck= -pyflakes= -color .github/workflows/*.yml
```

- [ ] **Step 9: 本地 actionlint 校验全部工作流**

```bash
cd /tmp && rm -rf actionlint-check && mkdir actionlint-check && cd actionlint-check
curl -fsSLo actionlint.tar.gz \
  "https://github.com/rhysd/actionlint/releases/download/v1.7.7/actionlint_1.7.7_linux_amd64.tar.gz"
echo "023070a287cd8cccd71515fedc843f1985bf96c436b7effaecce67290e7e0757  actionlint.tar.gz" | sha256sum -c -
tar -xzf actionlint.tar.gz actionlint
cd /projects/Seatflow && /tmp/actionlint-check/actionlint -shellcheck= -pyflakes= -color .github/workflows/*.yml
```

预期：退出码 0，无输出。

- [ ] **Step 10: 提交**

```bash
git add .github/workflows/release.yml .github/workflows/actionlint.yml
git commit -m "ci(release): 稳定版 OSS 历史同步 + Action 缓存 + 产物暂存；vpk 钉 1.2.161；新增 actionlint 门禁"
```

---

### Task 4: `unit-tests.yml` 移除 NuGet 缓存 + `dotnet-tools.json` 版本对齐

**Files:**
- Modify: `.github/workflows/unit-tests.yml`
- Modify: `dotnet-tools.json`

- [ ] **Step 1: 删除 unit-tests 的 NuGet 缓存步骤**

在 `.github/workflows/unit-tests.yml` 中删除：

```yaml
      - name: Cache NuGet packages
        uses: actions/cache@v6
        with:
          path: ~/.nuget/packages
          key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}
          restore-keys: |
            ${{ runner.os }}-nuget-
```

- [ ] **Step 2: dotnet-tools.json 的 vpk 改为 1.2.161**

把：

```json
    "vpk": {
      "version": "1.2.0",
```

改为：

```json
    "vpk": {
      "version": "1.2.161",
```

- [ ] **Step 3: 验证工具还原**

```bash
dotnet tool restore
dotnet vpk --help | head -3
```

预期：还原成功，vpk 帮助信息显示 `Velopack CLI 1.2.161`。

- [ ] **Step 4: 提交**

```bash
git add .github/workflows/unit-tests.yml dotnet-tools.json
git commit -m "chore(ci): 移除 NuGet 缓存占用；vpk 对齐客户端 1.2.161"
```

---

### Task 5: 文档联动

**Files:**
- Modify: `.github/docs/RELEASE_FLOW.md`
- Modify: `scripts/ToolsCollection.md`
- Modify: `docs/INDEX.md`
- Modify: `AGENTS.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: 更新 `.github/docs/RELEASE_FLOW.md`**

1. 工作流一览表中 `release.yml` 行改为：

```markdown
| `release.yml` | push `version.json`（自动）/ workflow_dispatch（手动） | **仅构建**：预检 → 4 RID（win-x64 / linux-x64 / osx-x64 / osx-arm64）并行。稳定版：restore 历史缓存 → OSS 同步 → vpk 打包（delta）、预发布：拉最新 full；暂存 artifacts |
```

2. 第四节「增量更新包（delta）」整节替换为：

```markdown
## 四、增量更新包（delta）

稳定版构建在 `vpk pack` 前通过 `scripts/ci/sync_velopack_history.py` 以 OSS 为唯一真源
同步打包历史到 `publish/history`：保留全部历史 delta + 最新 2 个 full，并做双向对账
（下载缺失/大小不符者；删除 OSS 上不存在的本地 nupkg）。`vpk pack --outputDir publish/history`
据此生成 `SeatFlow-{version}-{rid}-delta.nupkg`，并让 `releases.{rid}.json` 携带完整
delta 链（客户端按版本顺序串联，超过 10 跳或 delta 总大小超过 full 时退回全量下载）。

`publish/history` 通过 `actions/cache` 跨运行缓存（仅加速）；缓存失效时自动从 OSS
全量回填。同步失败硬失败（不发版）；缓存保存失败不影响发布（`continue-on-error`）。

预发布（workflow_dispatch）不参与同步与缓存，仍由 `scripts/ci/fetch_previous.sh`
（封装 `vpk download http`）拉取最新 full 作为 delta 基础，失败容错跳过。

> 历史注意：2026-09 之前 Worker 对 nupkg 直链返回 403，`fetch_previous` 一直静默失败，
> 因此 OSS 上 1.4.x / 2.0.0 没有任何 delta；链路自 2.1.0 起积累（不回填历史缺口，
> 缺口仅在同步日志中列出）。
```

3. 第六节 Secrets/Vars 表中 `UPDATE_FEED_URL` 行改为：

```markdown
| `UPDATE_FEED_URL` | var | 预发布 delta 基础（`vpk download http` 更新源）；稳定版已由 OSS 同步取代 |
```

并把该节末尾段落：

```markdown
仓库 **Environment** 需创建 `OSS`（release job 引用）。`release.yml` push 触发时
OSS 上传步骤需要 `OSS_*` 凭证；手动触发自动跳过该步骤，凭证缺失不影响。
```

替换为：

```markdown
`release.yml` 的 build job 使用**仓库级** `OSS_KEY_ID/OSS_KEY_SECRET`（secrets）与
`OSS_ENDPOINT/OSS_BUCKET`（vars）做历史同步（不引用 Environment）；`publish.yml`
仍在 Environment `OSS` 中执行上传（该环境 branch policy 仅允许 `main`）。预发布不访问 OSS。
```

4. 第七节「缓存策略」整节替换为：

```markdown
## 七、缓存策略

- `unit-tests.yml` 不再使用 NuGet 缓存（避免 2.5 GB 级条目反复堆积挤占仓库缓存配额）
- `release.yml`（稳定版 build job）缓存 `publish/history`（Velopack 打包历史）：
  key `vpk-history-{rid}-{run_id}-{run_attempt}`，restore-keys 前缀 `vpk-history-{rid}-`；
  每次运行保存新 key（滚动）；保存失败（超限等）不阻塞发布，由 OSS 同步兜底
- 历史目录只保留 nupkg 与 channel 文件；安装包与 `assets.*.json` 在暂存后即被清理
```

5. 第八节脚本表新增两行：

```markdown
| `sync_velopack_history.py` | 以 OSS 为真源同步 Velopack 打包历史（全部 delta + 最新 2 full，双向对账，缺口报告） |
| `stage_velopack_artifacts.sh` | 暂存本次发布产物到 `publish/dist`；稳定版从历史目录清理安装包 |
```

- [ ] **Step 2: `scripts/ToolsCollection.md` 追加 CI 章节**

在文件末尾追加：

````markdown
---

# ci — 发布流水线辅助脚本

CI / 发布流水线使用的脚本（凭据全部来自环境变量，无硬编码密钥）。

## sync_velopack_history.py — 同步 Velopack 打包历史

以 OSS `updates/` 为唯一真源，将历史 nupkg 同步到本地打包目录：

```bash
OSS_KEY_ID=... OSS_KEY_SECRET=... OSS_ENDPOINT=... OSS_BUCKET=... \
python3 scripts/ci/sync_velopack_history.py \
  --channel win-x64 --output-dir publish/history [--full-keep 2] [--dry-run]
```

- 保留集 = 全部 `-delta.nupkg` + 版本最高的 2 个 `-full.nupkg`
- 双向对账：下载缺失/大小不符（校验 size + MD5/ETag）；删除本地不在保留集的 `*.nupkg`
- 缺口报告：存在 full 但没有指向它的 delta 时列出（仅警告，不重建）
- 失败非零退出（发布硬失败）；`--dry-run` 只打印计划

## stage_velopack_artifacts.sh — 暂存发布产物

```bash
bash scripts/ci/stage_velopack_artifacts.sh <src-dir> <dist-dir> <version> <channel> <is-pre>
```

- 复制：安装包（`*.exe / *.AppImage / *.pkg / *.dmg`）、本次 full/delta nupkg、`releases.*.json`、`RELEASES-*`
- 排除：`assets.*.json`（vpk 内部文件）
- 稳定版（`is-pre != true`）：从 src 清理安装包与 `assets.*.json`，保持缓存精简

## fetch_previous.sh — 预发布 delta 基础

封装 `vpk download http` 拉取最新 full（仅预发布使用；稳定版由 sync 脚本接管）。

| 环境变量 | 说明 |
|---|---|
| `VPK_CHANNEL` | 渠道名（= matrix.rid） |
| `UPDATE_FEED_URL` | 更新源 base URL |
| `OUTPUT_DIR` | 输出目录（默认 `publish/out`） |
````

- [ ] **Step 3: `docs/INDEX.md` 补充 RELEASE_FLOW**

在文档地图中 `├── scripts/` 之前插入一行：

```
├── .github/docs/RELEASE_FLOW.md ← CI/发布流水线说明（release/publish/web、delta 链、缓存、Secrets）
```

在「文档职责与联动规则」中 `### scripts/ToolsCollection.md` 之前插入：

```markdown
### .github/docs/RELEASE_FLOW.md
- **覆盖**: GitHub Actions 发布流水线（release / publish / publish-web / worker-secret-sync）、delta 链与缓存策略、所需 Secrets/Vars、scripts/ci 脚本
- **何时更新**: 工作流结构、触发条件、发布步骤、delta/缓存策略、secrets/vars 变更
- **关联文档**: AGENTS.md（脚本工具）、scripts/ToolsCollection.md、docs/adr/ADR-010
```

在「常见变更场景的文档联动清单」表末追加一行：

```markdown
| 修改 CI/发布流水线（workflows / scripts/ci） | .github/docs/RELEASE_FLOW.md、scripts/ToolsCollection.md、AGENTS.md（脚本工具）、CHANGELOG.md |
```

- [ ] **Step 4: `AGENTS.md` 脚本工具节补充**

把「脚本工具」小节中：

```markdown
`scripts/build/publish.*` 多平台发布，`scripts/build/clean.*` 清理，`scripts/release/release.py` 发布编排
（读根目录 `RELEASE.md` 作为 Release body）；`scripts/ui-inspect/` 无头 UI 查看/交互/性能采样工具链。
```

改为：

```markdown
`scripts/build/publish.*` 多平台发布，`scripts/build/clean.*` 清理，`scripts/release/release.py` 发布编排
（读根目录 `RELEASE.md` 作为 Release body）；`scripts/ci/` 发布流水线辅助（`fetch_previous.sh` 预发布
delta 基础、`sync_velopack_history.py` 稳定版从 OSS 同步打包历史、`stage_velopack_artifacts.sh` 暂存
本次产物），详见 `.github/docs/RELEASE_FLOW.md`；`scripts/ui-inspect/` 无头 UI 查看/交互/性能采样工具链。
```

- [ ] **Step 5: `CHANGELOG.md` 在 2.1.0 的 `### Changed` 末尾追加**

```markdown
- **发布流水线增量更新修复**：稳定版构建以 OSS 为唯一真源同步 Velopack 打包历史（`sync_velopack_history.py`，全部 delta + 最新 2 个 full，双向对账）并跨运行缓存 `publish/history`，`releases.{rid}.json` 携带完整 delta 链；修复此前 `fetch_previous` 因 Worker 403 静默失败导致从未生成 delta 的问题；CI vpk 钉 1.2.161；移除 NuGet 缓存
```

- [ ] **Step 6: 提交**

```bash
git add .github/docs/RELEASE_FLOW.md scripts/ToolsCollection.md docs/INDEX.md AGENTS.md CHANGELOG.md
git commit -m "docs(ci): 同步 delta 链/缓存/OSS 同步说明与索引联动"
```

---

### Task 6: 清理 NuGet 缓存 + 发布验收

**Files:** 无（仓库状态与外部验证）

- [ ] **Step 1: 删除现有 `Linux-nuget-*` 缓存**

```bash
gh cache list --limit 100 --json id,key --jq '.[] | select(.key | startswith("Linux-nuget-")) | .id' \
  | xargs -I{} gh cache delete {}
gh cache list --limit 100 --json key --jq '.[].key'
```

预期：`Linux-nuget-*` 条目消失；其余（CodeQL 等）保留。

- [ ] **Step 2: 本地真 OSS 预演（需要用户提供凭证）**

```bash
cd /projects/Seatflow
OSS_KEY_ID=... OSS_KEY_SECRET=... OSS_ENDPOINT=... OSS_BUCKET=... \
python3 scripts/ci/sync_velopack_history.py \
  --channel win-x64 --output-dir /tmp/vpk-history-dry --dry-run
```

预期输出：远端 3 个 nupkg（1.4.0 full、1.4.1 full、2.0.0 full；无 delta），
保留 2（按版本最高的 2 个 full = 1.4.1、2.0.0），计划下载 2；
缺口报告列出 `1.4.1, 2.0.0`（除最早 full 1.4.0 外，两者都缺指向自己的 delta）。
在非 dry-run 下本地目录应只留下 `SeatFlow-1.4.1-*` 与 `SeatFlow-2.0.0-*` 两个 full；
`--channel win-x64` 下 linux/macOS 文件不应出现。

- [ ] **Step 3: 复跑全部脚本测试与 actionlint**

```bash
cd /projects/Seatflow/scripts && python3 -m pytest tests/ -v
cd /projects/Seatflow && /tmp/actionlint-check/actionlint -shellcheck= -pyflakes= -color .github/workflows/*.yml
```

预期：全部通过、退出码 0。

- [ ] **Step 4: 推送并观察 2.1.0（首个新链路正式版，由用户控制）**

推送 `main` 触发 `release.yml`（`version.json` 已为 2.1.0）。逐项核对构建日志：

- `Sync Velopack history from OSS`：冷启动，4 通道各下载 2.0.0 full（约 75–80 MB/通道），无缓存命中；
- `vpk pack`：出现 `Building delta 2.0.0 -> 2.1.0`；
- `Save Velopack history cache`：成功（冷启动首次保存可能较慢）；
- `Upload artifacts`：`publish/dist` 仅含本次安装包 + 2.1.0 full/delta + channel 文件。

- [ ] **Step 5: 校验 OSS feed 与 GitHub Release**

```bash
curl -sS https://download.seatflow.work/updates/releases.win-x64.json \
  | python3 -c "import json,sys; d=json.load(sys.stdin); [print(a['Type'], a['Version'], a['FileName']) for a in d['Assets']]"
```

预期（2.1.0 发布后）：

```
Full 2.1.0 SeatFlow-2.1.0-win-x64-full.nupkg
Delta 2.1.0 SeatFlow-2.1.0-win-x64-delta.nupkg
Full 2.0.0 SeatFlow-2.0.0-win-x64-full.nupkg
Full 1.4.1 SeatFlow-1.4.1-win-x64-full.nupkg
```

（保留集 = 全部 delta + OSS 最新 2 个 full：2.0.0 与 1.4.1，故 feed 共 4 个资产。）

同时确认 GitHub Release v2.1.0 资产为安装包集合（不含 nupkg）。

- [ ] **Step 6: 第二个正式版验证缓存命中**

下一次稳定版发版时核对：

- `Restore Velopack history cache` 报告 cache hit（恢复历史目录）；
- `Sync Velopack history from OSS` 仅列举 + HEAD，无（或仅 1 个新 full）下载；
- feed 中出现 ≥2 个连续 delta（例如 `2.1.0→2.1.1` 与 `2.0.0→2.1.0`），客户端可链式增量。
