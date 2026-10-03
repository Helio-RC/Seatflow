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


def _format_bytes(size: int) -> str:
    """把字节数格式化为可读字符串（B/KiB/MiB）。"""
    if size < 1024:
        return f"{size} B"
    if size < 1024 * 1024:
        return f"{size / 1024:.1f} KiB"
    return f"{size / (1024 * 1024):.1f} MiB"


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


def reconcile(bucket, keep: dict[str, Asset], output_dir: Path, dry_run: bool) -> tuple[int, int, int, int]:
    """双向对账：下载缺失/大小不符者，删除本地不在保留集的 nupkg。

    返回 (下载数, 删除数, 下载字节数, 删除字节数)；dry-run 统计计划值。
    """
    if not dry_run:
        output_dir.mkdir(parents=True, exist_ok=True)
    downloaded = 0
    downloaded_bytes = 0
    for name in sorted(keep):
        asset = keep[name]
        local = output_dir / name
        if local.exists() and local.stat().st_size == asset.size:
            continue
        if dry_run:
            print(f"  [dry-run] 下载 {name}")
            downloaded += 1
            downloaded_bytes += asset.size
            continue
        _download_one(bucket, asset, output_dir)
        print(f"  ↓ {name}")
        downloaded += 1
        downloaded_bytes += asset.size

    deleted = 0
    deleted_bytes = 0
    keep_names = set(keep)
    for local in sorted(output_dir.glob("*.nupkg")):
        if local.name in keep_names:
            continue
        size = local.stat().st_size
        if dry_run:
            print(f"  [dry-run] 删除 {local.name}")
        else:
            local.unlink()
            print(f"  ✗ 删除 {local.name}")
        deleted += 1
        deleted_bytes += size
    return downloaded, deleted, downloaded_bytes, deleted_bytes


def run_sync(bucket, entries: Iterable[object], channel: str, output_dir: Path,
             pack_id: str = DEFAULT_PACK_ID, full_keep: int = DEFAULT_FULL_KEEP,
             dry_run: bool = False) -> int:
    """执行一次同步。返回 0（失败由异常向上抛）。"""
    assets = parse_remote_assets(entries, pack_id, channel)
    keep = select_keep_set(assets, full_keep)
    gaps = find_gaps(assets)
    total_bytes = sum(asset.size for asset in assets)
    keep_bytes = sum(asset.size for asset in keep.values())
    print(f"[sync] 远端 {channel}: {len(assets)} 个 nupkg（{_format_bytes(total_bytes)}）"
          f"，保留 {len(keep)} 个（{_format_bytes(keep_bytes)}）")
    downloaded, deleted, downloaded_bytes, deleted_bytes = reconcile(bucket, keep, output_dir, dry_run)
    print(f"[sync] 下载 {downloaded} 个（{_format_bytes(downloaded_bytes)}）"
          f"，删除 {deleted} 个（{_format_bytes(deleted_bytes)}）")
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
