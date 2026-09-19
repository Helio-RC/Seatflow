#!/usr/bin/env python3
"""CI 在线版（Web/WASM）发布脚本 — OSS 上传、完整性校验、KV 原子切换、旧版本清理。

职责:
  1. 上传 wwwroot 全部原文件 + .br（跳过 .gz/.map）→ <prefix>/<version>/
  2. 用 list_objects_v2 全量比对 key/大小，确保上传完整后才允许切流
  3. 写 Cloudflare KV（key 默认 current）完成原子切换
  4. 按保留策略清理旧版本目录（绝不删除 KV 当前版本）
  5. --switch-only / --print-current 支撑秒级回滚

凭据全部通过环境变量注入（GitHub secrets/vars），脚本内无密钥:
  OSS_KEY_ID / OSS_KEY_SECRET / OSS_ENDPOINT / OSS_BUCKET
  CF_ACCOUNT_ID / CF_API_TOKEN（切换、读取、清理时必需）
  CF_KV_NAMESPACE_ID（可选，默认内置 online_worktable 命名空间）
  CF_API_BASE（可选，默认 https://api.cloudflare.com）

用法:
  # 上传 + 校验 + 切换（正式流程）
  python3 scripts/ci/upload_web_oss.py --version 2.0.0 --wwwroot web-out/wwwroot
  # 仅上传 + 校验
  python3 scripts/ci/upload_web_oss.py --version 2.0.0 --wwwroot web-out/wwwroot --no-switch
  # 回滚 / 重试切换
  python3 scripts/ci/upload_web_oss.py --version 2.0.0 --switch-only
  # 读取当前版本 / 清理旧版本
  python3 scripts/ci/upload_web_oss.py --print-current
  python3 scripts/ci/upload_web_oss.py --prune-only --keep 5
"""

import argparse
import hashlib
import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

try:
    import oss2
except ImportError:  # --print-current / --dry-run 不依赖 oss2
    oss2 = None

try:
    from packaging.version import InvalidVersion, Version
except ImportError:  # 仅 prune 需要
    InvalidVersion = ValueError
    Version = None

# online_worktable Worker 的 KV 命名空间（非密钥，可在环境变量中覆盖）
DEFAULT_KV_NAMESPACE_ID = "7461d25fa9054b02bdce716f3b41414c"
DEFAULT_PREFIX = "online_worktable"
DEFAULT_KV_KEY = "current"
DEFAULT_KEEP = 5
VERSION_PATTERN = re.compile(r"^[A-Za-z0-9._-]{1,64}$")
SKIP_SUFFIXES = (".gz", ".map")

# 入口文件最后上传：先让全部内容哈希文件落地，再写入口
ENTRY_UPLOAD_ORDER = {"_framework/blazor.boot.json": 1, "index.html": 2}

CONTENT_TYPES = {
    "html": "text/html; charset=utf-8",
    "htm": "text/html; charset=utf-8",
    "css": "text/css; charset=utf-8",
    "js": "text/javascript; charset=utf-8",
    "mjs": "text/javascript; charset=utf-8",
    "json": "application/json; charset=utf-8",
    "map": "application/json; charset=utf-8",
    "xml": "application/xml; charset=utf-8",
    "txt": "text/plain; charset=utf-8",
    "wasm": "application/wasm",
    "dat": "application/octet-stream",
    "dll": "application/octet-stream",
    "pdb": "application/octet-stream",
    "bin": "application/octet-stream",
    "ico": "image/x-icon",
    "png": "image/png",
    "jpg": "image/jpeg",
    "jpeg": "image/jpeg",
    "gif": "image/gif",
    "svg": "image/svg+xml",
    "webp": "image/webp",
    "otf": "font/otf",
    "ttf": "font/ttf",
    "woff": "font/woff",
    "woff2": "font/woff2",
    "webmanifest": "application/manifest+json",
}

OSS_REQUIRED = ("OSS_KEY_ID", "OSS_KEY_SECRET", "OSS_ENDPOINT", "OSS_BUCKET")
CF_REQUIRED = ("CF_ACCOUNT_ID", "CF_API_TOKEN")


def log(message: str = "") -> None:
    print(message, flush=True)


def md5_of(path: Path) -> str:
    digest = hashlib.md5()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def content_type_for(relative_path: str) -> str:
    """显式 MIME；.br 文件按解码后的扩展名取类型。"""
    name = relative_path[:-3] if relative_path.endswith(".br") else relative_path
    ext = name.rsplit(".", 1)[-1].lower() if "." in name else ""
    return CONTENT_TYPES.get(ext, "application/octet-stream")


def collect_files(wwwroot: Path) -> dict:
    """递归收集上传文件集（相对路径 → 本地路径），跳过 .gz/.map。"""
    if not wwwroot.is_dir():
        raise RuntimeError(f"wwwroot 目录不存在: {wwwroot}")
    files = {}
    for path in sorted(wwwroot.rglob("*")):
        if not path.is_file():
            continue
        relative = path.relative_to(wwwroot).as_posix()
        if relative.lower().endswith(SKIP_SUFFIXES):
            continue
        files[relative] = path
    return files


def require_env(names) -> None:
    missing = [name for name in names if not os.environ.get(name)]
    if missing:
        raise RuntimeError(f"缺少环境变量: {', '.join(missing)}")


# ── Cloudflare KV（REST API） ──────────────────────────────────────────────


def cf_api_base() -> str:
    """兼容两种配置：api.cloudflare.com 或带 /client/v4 的完整基址。"""
    base = (os.environ.get("CF_API_BASE") or "https://api.cloudflare.com").rstrip("/")
    if not base.endswith("/client/v4"):
        base += "/client/v4"
    return base


def cf_namespace_id() -> str:
    return os.environ.get("CF_KV_NAMESPACE_ID") or DEFAULT_KV_NAMESPACE_ID


def cf_request(method: str, path: str, data: bytes = None, content_type: str = None):
    url = f"{cf_api_base()}/accounts/{os.environ['CF_ACCOUNT_ID']}{path}"
    headers = {"Authorization": f"Bearer {os.environ['CF_API_TOKEN']}"}
    if content_type:
        headers["Content-Type"] = content_type
    request = urllib.request.Request(url, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return response.status, response.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace")
    except urllib.error.URLError as e:
        raise RuntimeError(f"Cloudflare API 网络错误: {e.reason}")


def kv_get(key: str):
    path = f"/storage/kv/namespaces/{cf_namespace_id()}/values/{urllib.parse.quote(key, safe='')}"
    status, body = cf_request("GET", path)
    if status == 404:
        return None
    if status != 200:
        raise RuntimeError(f"读取 KV 失败（HTTP {status}）: {body[:300]}")
    return body.strip()


def kv_put(key: str, value: str) -> None:
    path = f"/storage/kv/namespaces/{cf_namespace_id()}/values/{urllib.parse.quote(key, safe='')}"
    status, body = cf_request(
        "PUT", path, data=value.encode("utf-8"), content_type="text/plain; charset=utf-8"
    )
    if status != 200:
        raise RuntimeError(f"写入 KV 失败（HTTP {status}）: {body[:300]}")
    try:
        payload = json.loads(body)
    except json.JSONDecodeError:
        payload = {}
    if not payload.get("success"):
        raise RuntimeError(f"写入 KV 失败: {body[:300]}")


# ── OSS ────────────────────────────────────────────────────────────────────


def make_bucket():
    if oss2 is None:
        raise RuntimeError("缺少 oss2 依赖，请先 pip install oss2")
    auth = oss2.Auth(os.environ["OSS_KEY_ID"], os.environ["OSS_KEY_SECRET"])
    return oss2.Bucket(auth, os.environ["OSS_ENDPOINT"], os.environ["OSS_BUCKET"])


def list_objects(bucket, prefix: str) -> dict:
    """返回 {key: size}；自动分页。"""
    result = {}
    for obj in oss2.ObjectIteratorV2(bucket, prefix=prefix):
        if not obj.is_prefix():
            result[obj.key] = obj.size
    return result


def do_upload(bucket, prefix: str, version: str, files: dict) -> None:
    version_prefix = f"{prefix}/{version}/"
    ordered = sorted(files, key=lambda rel: (ENTRY_UPLOAD_ORDER.get(rel, 0), rel))
    total = sum(path.stat().st_size for path in files.values())
    log(f"  上传 {len(files)} 个对象（{total / 1048576:.1f} MiB），跳过 .gz/.map")
    for index, relative in enumerate(ordered, 1):
        path = files[relative]
        key = version_prefix + relative
        result = bucket.put_object_from_file(
            key, str(path), headers={"Content-Type": content_type_for(relative)}
        )
        etag = (result.etag or "").strip('"').lower()
        local_md5 = md5_of(path)
        if etag and etag != local_md5:
            raise RuntimeError(f"上传完整性异常（ETag 与本地 MD5 不一致）: {key}")
        log(f"    ✓ [{index}/{len(ordered)}] {key} ({path.stat().st_size / 1048576:.1f} MiB)")


def do_verify(bucket, version_prefix: str, files: dict) -> None:
    remote = list_objects(bucket, version_prefix)
    expected = {version_prefix + rel: path.stat().st_size for rel, path in files.items()}
    missing = sorted(set(expected) - set(remote))
    extra = sorted(set(remote) - set(expected))
    mismatched = sorted(
        key for key in set(expected) & set(remote) if expected[key] != remote[key]
    )
    if missing or extra or mismatched:
        if missing:
            log(f"    ✗ 缺失 {len(missing)} 个对象，如: {missing[:5]}")
        if extra:
            log(f"    ✗ 多出 {len(extra)} 个对象，如: {extra[:5]}")
        if mismatched:
            log(f"    ✗ 大小不一致 {len(mismatched)} 个对象，如: {mismatched[:5]}")
        raise RuntimeError("完整性校验失败，未写入 KV")
    log(
        f"  ✓ 完整性校验通过：{len(expected)} 个对象，"
        f"{sum(expected.values()) / 1048576:.1f} MiB"
    )


def list_version_dirs(bucket, prefix: str):
    """列出版本目录名（prefix/ 下的一级公共前缀）。"""
    versions = []
    directory_prefix = f"{prefix}/"
    for obj in oss2.ObjectIteratorV2(bucket, prefix=directory_prefix, delimiter="/"):
        if not obj.is_prefix():
            continue
        name = obj.key[len(directory_prefix):].rstrip("/")
        if name and "/" not in name:
            versions.append(name)
    return versions


def do_prune(bucket, prefix: str, keep: int, key: str = DEFAULT_KV_KEY, dry_run: bool = False) -> None:
    if dry_run:
        log(f"  [dry-run] 保留最近 {keep} 个版本 + KV {key} 指向的版本，删除其余目录")
        return
    if Version is None:
        raise RuntimeError("缺少 packaging 依赖，请先 pip install packaging")

    current = kv_get(key) or ""

    versions = list_version_dirs(bucket, prefix)
    if not versions:
        log("  无历史版本目录")
        return

    valid = []
    protected = set()
    for name in versions:
        try:
            valid.append((name, Version(name)))
        except InvalidVersion:
            log(f"    - 无法排序，跳过清理: {name}")
            protected.add(name)

    valid.sort(key=lambda item: item[1], reverse=True)
    keep_set = {name for name, _ in valid[: max(keep, 0)]}
    keep_set |= protected
    if current:
        keep_set.add(current)

    deletable = [name for name in versions if name not in keep_set]
    if not deletable:
        log(f"  ✓ 无需清理（保留: {', '.join(sorted(keep_set))}）")
        return

    for name in deletable:
        version_prefix = f"{prefix}/{name}/"
        keys = list(list_objects(bucket, version_prefix).keys())
        for index in range(0, len(keys), 1000):
            batch = keys[index:index + 1000]
            bucket.batch_delete_objects(batch)
        remaining = list_objects(bucket, version_prefix)
        if remaining:
            raise RuntimeError(
                f"清理未完成: {version_prefix} 仍剩余 {len(remaining)} 个对象"
            )
        log(f"    ✓ 已删除旧版本 {name}（{len(keys)} 个对象）")
    log(f"  ✓ 清理完成（保留: {', '.join(sorted(keep_set))}）")


# ── 入口 ───────────────────────────────────────────────────────────────────


def main() -> int:
    parser = argparse.ArgumentParser(description="在线版（Web/WASM）OSS 发布")
    parser.add_argument("--version", help="版本号（如 2.0.0）")
    parser.add_argument("--wwwroot", help="SeatFlow.Browser 发布产物 wwwroot 目录")
    parser.add_argument("--prefix", default=DEFAULT_PREFIX, help="OSS 键前缀")
    parser.add_argument("--key", default=DEFAULT_KV_KEY, help="KV 版本键名")
    parser.add_argument("--keep", type=int, default=DEFAULT_KEEP, help="保留版本数（默认 5）")
    parser.add_argument("--dry-run", action="store_true", help="仅打印计划，不访问网络")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--no-switch", action="store_true", help="仅上传 + 校验，不写 KV")
    mode.add_argument("--switch-only", action="store_true", help="仅写 KV（回滚/重试）")
    mode.add_argument("--prune-only", action="store_true", help="仅清理旧版本目录")
    mode.add_argument("--print-current", action="store_true", help="打印 KV 当前版本")
    args = parser.parse_args()

    try:
        if args.print_current:
            if args.dry_run:
                log("  [dry-run] 读取 KV 当前版本（跳过网络）")
                return 0
            require_env(CF_REQUIRED)
            current = kv_get(args.key)
            if current:
                print(current)
            return 0

        if args.prune_only:
            if args.dry_run:
                do_prune(None, args.prefix, args.keep, args.key, dry_run=True)
                return 0
            require_env(OSS_REQUIRED)
            require_env(CF_REQUIRED)
            log(f"清理旧版本（keep={args.keep}）...")
            do_prune(make_bucket(), args.prefix, args.keep, args.key)
            return 0

        if not args.version or not VERSION_PATTERN.match(args.version):
            raise RuntimeError("--version 缺失或格式非法（允许 [A-Za-z0-9._-]{1,64}）")

        if args.switch_only:
            if args.dry_run:
                log(f"  [dry-run] 将写入 KV {args.key} = {args.version}")
                return 0
            require_env(CF_REQUIRED)
            kv_put(args.key, args.version)
            log(f"✓ KV 切换完成: {args.key} = {args.version}")
            return 0

        # 默认 / --no-switch：上传 + 校验（+ 切换）
        if not args.wwwroot:
            raise RuntimeError("上传模式需要 --wwwroot")
        files = collect_files(Path(args.wwwroot))
        if not files:
            raise RuntimeError(f"未收集到任何文件: {args.wwwroot}")
        version_prefix = f"{args.prefix}/{args.version}/"

        log(f"在线版发布: {args.version} → {version_prefix}")
        if args.dry_run:
            total = sum(path.stat().st_size for path in files.values())
            log(f"  [dry-run] 将上传 {len(files)} 个对象（{total / 1048576:.1f} MiB）")
            for relative in sorted(files, key=lambda rel: (ENTRY_UPLOAD_ORDER.get(rel, 0), rel))[:5]:
                log(f"    [dry-run] {version_prefix}{relative}")
            if len(files) > 5:
                log(f"    [dry-run] ... 其余 {len(files) - 5} 个")
            if args.no_switch:
                log("  [dry-run] --no-switch：不写 KV")
            else:
                log(f"  [dry-run] 校验通过后将写入 KV {args.key} = {args.version}")
            return 0

        require_env(OSS_REQUIRED)
        bucket = make_bucket()
        do_upload(bucket, args.prefix, args.version, files)
        do_verify(bucket, version_prefix, files)

        if args.no_switch:
            log("✓ 上传与完整性校验完成（--no-switch：未切换 KV）")
            return 0

        require_env(CF_REQUIRED)
        kv_put(args.key, args.version)
        log(f"✓ 上传、校验、切换全部完成: KV {args.key} = {args.version}")
        return 0
    except RuntimeError as error:
        log(f"✗ {error}")
        return 1


if __name__ == "__main__":
    sys.exit(main())
