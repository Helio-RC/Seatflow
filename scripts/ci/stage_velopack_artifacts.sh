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
