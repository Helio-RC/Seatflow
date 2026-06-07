#!/usr/bin/env bash
# ============================================================
# A_Pair 多平台发布 — 自包含 / 依赖运行时，单文件
# 用法: ./scripts/publish.sh [both|sc|fd] [Release|Debug]
# ============================================================
set -euo pipefail

MODE="${1:-both}"
CONFIG="${2:-Release}"
PROJECT="A_Pair.Presentation.Avalonia"
RIDS=("win-x64" "linux-x64" "osx-x64" "osx-arm64")
ROOT="$(cd "$(dirname "$0")/.." && pwd)/publish"

publish_one() {
    local sc="$1" label="$2"
    local sc_flag="false"
    [ "$sc" = "true" ] && sc_flag="true"
    local base="$ROOT/$label"

    for rid in "${RIDS[@]}"; do
        local out="$base/$rid"
        echo -e "\e[33m[$label] $rid\e[0m"

        dotnet publish "$PROJECT" \
            -c "$CONFIG" \
            -r "$rid" \
            --self-contained "$sc_flag" \
            -p:PublishSingleFile=true \
            -p:PublishTrimmed=true \
            -o "$out"

        local exe="$PROJECT"
        [ "${rid:0:3}" = "win" ] && exe="$PROJECT.exe"
        if [ -f "$out/$exe" ]; then
            local size
            size=$(du -h "$out/$exe" | cut -f1)
            echo -e "\e[32m  -> $label/$rid ($size)\e[0m"
        else
            echo -e "\e[32m  -> $label/$rid (bundle)\e[0m"
        fi
    done
}

echo -e "\e[36m=== A_Pair 发布 ($CONFIG) ===\e[0m"

if [ "$MODE" = "both" ] || [ "$MODE" = "sc" ]; then
    echo -e "\n\e[35m--- 自包含 (Self-Contained) ---\e[0m"
    publish_one "true" "sc"
fi

if [ "$MODE" = "both" ] || [ "$MODE" = "fd" ]; then
    echo -e "\n\e[35m--- 依赖运行时 (Framework-Dependent) ---\e[0m"
    publish_one "false" "fd"
fi

echo -e "\n\e[36m=== 完成 ===\e[0m"
