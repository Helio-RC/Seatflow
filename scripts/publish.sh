#!/usr/bin/env bash
# ============================================================
# A_Pair 多平台发布 — 请在 scripts/ 目录下执行
# 用法: ./publish.sh [both|sc|fd] [Release|Debug]
# ============================================================
set -euo pipefail
cd ..

MODE="${1:-both}"
CONFIG="${2:-Release}"
PROJECT="A_Pair.Presentation.Avalonia"
RIDS=("win-x64" "linux-x64" "osx-x64" "osx-arm64")

publish_one() {
    local sc="$1" label="$2" base="publish/$label"
    local sc_flag="false"
    [ "$sc" = "true" ] && sc_flag="true"

    for rid in "${RIDS[@]}"; do
        local out="$base/$rid"
        echo -e "\e[33m[$label] $rid\e[0m"

        dotnet publish "$PROJECT" -c "$CONFIG" -r "$rid" \
            --self-contained "$sc_flag" \
            -p:PublishSingleFile=true \
            -p:IncludeNativeLibrariesForSelfExtract=true \
            -p:IncludeAllContentForSelfExtract=true \
            -o "$out"

        local exe="$PROJECT"
        [ "${rid:0:3}" = "win" ] && exe="$PROJECT.exe"
        if [ -f "$out/$exe" ]; then
            echo -e "\e[32m  -> $out ($(du -h "$out/$exe" | cut -f1))\e[0m"
        fi
    done
}

echo -e "\e[36m=== A_Pair 发布 ($CONFIG) ===\e[0m"

if [ "$MODE" != "fd" ]; then
    echo -e "\n\e[35m--- 自包含 (Self-Contained) ---\e[0m"
    publish_one "true" "sc"
fi
if [ "$MODE" != "sc" ]; then
    echo -e "\n\e[35m--- 依赖运行时 (Framework-Dependent) ---\e[0m"
    publish_one "false" "fd"
fi

echo -e "\n\e[36m=== 完成 ===\e[0m"
