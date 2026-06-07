#!/usr/bin/env bash
# ============================================================
# 清理解决方案下所有 bin/ 和 obj/ 目录
# 用法: ./scripts/clean.sh         # 列出 + 确认
#        ./scripts/clean.sh -f      # 直接删除
#        ./scripts/clean.sh -n      # 仅预览
# ============================================================
set -euo pipefail

FORCE=false
DRY=false

while getopts "fn" opt; do
    case "$opt" in
        f) FORCE=true ;;
        n) DRY=true ;;
        *) echo "用法: $0 [-f] [-n]" && exit 1 ;;
    esac
done

ROOT="$(cd "$(dirname "$0")/.." && pwd)"

echo -e "\e[36m=== A_Pair 清理 bin/ & obj/ ===\e[0m"
echo -e "\e[90m根目录: $ROOT\e[0m"
echo ""

# 收集目录
mapfile -t dirs < <(find "$ROOT" -type d \( -name bin -o -name obj \) | sort)

if [ ${#dirs[@]} -eq 0 ]; then
    echo -e "\e[32m没有找到 bin/ 或 obj/ 目录\e[0m"
    exit 0
fi

# 列出并计算大小
total=0
for d in "${dirs[@]}"; do
    size=$(du -sk "$d" 2>/dev/null | cut -f1 || echo 0)
    total=$((total + size))
    mb=$(echo "scale=1; $size / 1024" | bc 2>/dev/null || echo "0")
    rel="${d#$ROOT/}"
    echo -e "\e[90m  ./$rel  (${mb}M)\e[0m"
done

total_mb=$(echo "scale=1; $total / 1024" | bc 2>/dev/null || echo "0")
echo -e "\n\e[33m共 ${#dirs[@]} 个目录，${total_mb}M\e[0m"

if $DRY; then
    echo -e "\e[36mDryRun — 未删除任何内容\e[0m"
    exit 0
fi

if ! $FORCE; then
    read -r -p "确认删除? (y/N) " confirm
    if [ "$confirm" != "y" ] && [ "$confirm" != "Y" ]; then
        echo -e "\e[90m已取消\e[0m"
        exit 0
    fi
fi

for d in "${dirs[@]}"; do
    rm -rf "$d"
done
echo -e "\e[32m已清理 ${total_mb}M\e[0m"
