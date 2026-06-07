#!/usr/bin/env bash
# ============================================================
# A_Pair 多平台发布 — 在 scripts/ 目录下执行
# 用法: ./publish.sh [both|sc|fd] [Release|Debug]
# ============================================================
set -euo pipefail
cd ..

MODE="${1:-both}"
CONFIG="${2:-Release}"
PROJECT="A_Pair.Presentation.Avalonia"
APP_NAME="A_Pair"
RIDS=("win-x64" "linux-x64" "osx-x64" "osx-arm64")
START_TIME=$(date +%s)

step() { echo -e "  [$(date +%H:%M:%S)] \e[${2:-37}m$1\e[0m"; }

publish_one() {
    local sc="$1" label="$2" base="publish/$label"
    local sc_flag="false"
    [ "$sc" = "true" ] && sc_flag="true"
    mkdir -p "$base"

    for rid in "${RIDS[@]}"; do
        local tmp_out="$base/.tmp_$rid"
        local suffix=""
        [ "${rid:0:3}" = "win" ] && suffix=".exe"
        local final_name="$APP_NAME-$rid$suffix"
        local title="A_Pair: $label / $rid"

        echo -ne "\033]0;$title\007"
        echo ""
        echo -e "\e[36m══════════════════════════════════════════\e[0m"
        echo -e "\e[36m  $title\e[0m"
        echo -e "\e[36m══════════════════════════════════════════\e[0m"
        step "开始编译..." 33

        dotnet publish "$PROJECT" -c "$CONFIG" -r "$rid" \
            --self-contained "$sc_flag" \
            -p:PublishSingleFile=true \
            -p:IncludeNativeLibrariesForSelfExtract=true \
            -p:IncludeAllContentForSelfExtract=true \
            -o "$tmp_out"

        local built
        built=$(find "$tmp_out" -maxdepth 1 -type f \( -name "$PROJECT" -o -name "$PROJECT.exe" \) | head -1)
        if [ -n "$built" ]; then
            mv "$built" "$base/$final_name"
            rm -rf "$tmp_out"
            local size; size=$(du -h "$base/$final_name" | cut -f1)
            step "完成 → $label/$final_name ($size)" 32
        else
            step "完成 → $tmp_out (未找到可执行文件)" 33
        fi
    done
}

echo -ne "\033]0;A_Pair: 发布中...\007"
echo -e "\e[36m╔══════════════════════════════════╗\e[0m"
echo -e "\e[36m║  A_Pair 多平台发布               ║\e[0m"
echo -e "\e[36m║  $CONFIG | $( [ "$MODE" = "both" ] && echo 'SC + FD' || echo "$MODE" | tr '[:lower:]' '[:upper:]' )\e[0m"
echo -e "\e[36m╚══════════════════════════════════╝\e[0m"

if [ "$MODE" != "fd" ]; then
    echo -e "\n\e[35m┌─ 自包含 (Self-Contained) ─────────┐\e[0m"
    publish_one "true" "sc"
    echo -e "\e[35m└────────────────────────────────────┘\e[0m"
fi
if [ "$MODE" != "sc" ]; then
    echo -e "\n\e[35m┌─ 依赖运行时 (Framework-Dependent) ──┐\e[0m"
    publish_one "false" "fd"
    echo -e "\e[35m└────────────────────────────────────┘\e[0m"
fi

END_TIME=$(date +%s)
ELAPSED=$((END_TIME - START_TIME))
echo ""
echo -e "\e[36m══════════════════════════════════════════\e[0m"
echo -e "\e[36m  全部完成，总用时 ${ELAPSED} 秒\e[0m"
echo -e "\e[36m══════════════════════════════════════════\e[0m"
echo -ne "\033]0;A_Pair: 发布完成\007"
