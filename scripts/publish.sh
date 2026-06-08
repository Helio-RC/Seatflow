#!/usr/bin/env bash
# ============================================================
# A_Pair 多平台发布 — 在 scripts/ 目录下执行
# 用法: ./publish.sh [both|sc|fd] [Release|Debug] [opt]
#        ./publish.sh hash  仅计算已有文件的 SHA256
#        ./publish.sh both Release opt  开启裁剪优化
# ============================================================
set -euo pipefail
cd ..

MODE="${1:-both}"
CONFIG="${2:-Release}"
OPTIMIZE="${3:-}"
PROJECT="A_Pair.Presentation.Avalonia"
APP_NAME="A_Pair"
RIDS=("win-x64" "linux-x64" "osx-x64" "osx-arm64")
START_TIME=$(date +%s)

step() { echo -e "  [$(date +%H:%M:%S)] \e[${2:-37}m$1\e[0m"; }

print_sha256_table() {
    local files
    mapfile -t files < <(find publish -type f -name "$APP_NAME-*" -print0 | sort -z | xargs -0 -n1)
    if [ ${#files[@]} -eq 0 ]; then
        echo -e "\e[33m没有找到已发布文件\e[0m"
        return
    fi
    echo ""
    echo "| 文件 | SHA256 |"
    echo "|------|--------|"
    for f in "${files[@]}"; do
        local hash name
        hash=$(sha256sum "$f" | awk '{print tolower($1)}')
        name=$(basename "$f")
        echo "| \`$name\` | $hash |"
    done
}

if [ "$MODE" = "hash" ]; then
    echo -e "\e[36mSHA256 校验值（Markdown 表格）：\e[0m"
    print_sha256_table
    exit 0
fi

publish_one() {
    local sc="$1" label="$2" base="publish/$label"
    local sc_flag="false"
    [ "$sc" = "true" ] && sc_flag="true"
    mkdir -p "$base"

    for rid in "${RIDS[@]}"; do
        local tmp_out="$base/.tmp_$rid"
        local suffix=""
        [ "${rid:0:3}" = "win" ] && suffix=".exe"
        local final_name="$APP_NAME-$label-$rid$suffix"
        local title="A_Pair: $label / $rid"

        echo -ne "\033]0;$title\007"
        echo ""
        echo -e "\e[36m══════════════════════════════════════════\e[0m"
        echo -e "\e[36m  $title\e[0m"
        echo -e "\e[36m══════════════════════════════════════════\e[0m"
        step "开始编译..." 33

        local trim_args=()
        if [ "$OPTIMIZE" = "opt" ]; then
            trim_args=(-p:PublishTrimmed=true -p:TrimMode=partial -p:SuppressTrimAnalysisWarnings=true)
        fi

        dotnet publish "$PROJECT" -c "$CONFIG" -r "$rid" \
            --self-contained "$sc_flag" \
            -p:PublishSingleFile=true \
            -p:IncludeNativeLibrariesForSelfExtract=true \
            -p:IncludeAllContentForSelfExtract=true \
            "${trim_args[@]}" \
            -o "$tmp_out"

        local exe_name="$APP_NAME$suffix"
        if [ -f "$tmp_out/$exe_name" ]; then
            mv "$tmp_out/$exe_name" "$base/$final_name"
            rm -rf "$tmp_out"
            local size; size=$(du -h "$base/$final_name" | cut -f1)
            step "完成 → $label/$final_name ($size)" 32
        else
            local fallback; fallback=$(find "$tmp_out" -maxdepth 1 -type f -name "$APP_NAME*" | head -1)
            if [ -n "$fallback" ]; then
                mv "$fallback" "$base/$final_name"
                rm -rf "$tmp_out"
                local size2; size2=$(du -h "$base/$final_name" | cut -f1)
                step "完成 → $label/$final_name ($size2)" 32
            else
                step "未找到可执行文件，临时目录保留: $tmp_out" 31
            fi
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

echo ""
echo -e "\e[36mSHA256 校验值（可直接粘贴到 Release 说明）：\e[0m"
print_sha256_table
