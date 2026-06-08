#!/usr/bin/env bash
# ============================================================
# A_Pair 多平台发布 — TUI 交互 / CLI 兼容
# 用法: ./publish.sh                   # TUI
#        ./publish.sh both Release opt  # CLI
#        ./publish.sh hash              # 仅哈希
# ============================================================
set -euo pipefail
cd ..

APP_NAME="A_Pair"; PROJECT="A_Pair.Presentation.Avalonia"; CONFIG="Release"
RIDS=("win-x64" "linux-x64" "osx-x64" "osx-arm64")
SUFFIXES=(".exe" "" "" ""); SEL=(0 0 0 0)
TYPE_IDX=2; TRIM_SEL=0; CURSOR=0; ITEMS=14

step(){ echo -e "  [$(date +%H:%M:%S)] \e[${2:-37}m$1\e[0m"; }

sha_table(){
    local files=() f h n
    while IFS= read -r -d '' f; do files+=("$f"); done < <(find publish -type f -name "$APP_NAME-*" -print0 2>/dev/null | sort -z)
    if [ ${#files[@]} -eq 0 ]; then echo -e "\e[33m没有找到已发布文件\e[0m"; return; fi
    echo ""; echo "| 文件 | SHA256 |"; echo "|------|--------|"
    for f in "${files[@]}"; do h=$(sha256sum "$f"|awk '{print tolower($1)}'); n=$(basename "$f"); echo "| \`$n\` | $h |"; done
}

publish_one(){
    local sc="$1" label="$2"; shift 2; local rids=("$@") rid sf tmp fn exe fb s
    local sc_flag="false"; [ "$sc" = "true" ] && sc_flag="true"
    local base="publish/$label"; mkdir -p "$base"
    for rid in "${rids[@]}"; do
        sf=""; [ "${rid:0:3}" = "win" ] && sf=".exe"
        tmp="$base/.tmp_$rid"; fn="$APP_NAME-$label-$rid$sf"
        echo -ne "\033]0;A_Pair: $label / $rid\007"
        echo ""; echo -e "\e[36m══════════════════════════════════════════\e[0m"
        echo -e "\e[36m  $label / $rid\e[0m"
        echo -e "\e[36m══════════════════════════════════════════\e[0m"
        step "开始编译..." 33
        local ta=()
        [ "$TRIM_SEL" = "1" ] && ta=(-p:PublishTrimmed=true -p:TrimMode=partial -p:SuppressTrimAnalysisWarnings=true)
        dotnet publish "$PROJECT" -c "$CONFIG" -r "$rid" --self-contained "$sc_flag" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true "${ta[@]}" -o "$tmp"
        exe="$APP_NAME$sf"
        if [ -f "$tmp/$exe" ]; then mv "$tmp/$exe" "$base/$fn"; rm -rf "$tmp"; s=$(du -h "$base/$fn"|cut -f1); step "完成 → $label/$fn ($s)" 32
        else fb=$(find "$tmp" -maxdepth 1 -type f -name "$APP_NAME*"|head -1)
            if [ -n "$fb" ]; then mv "$fb" "$base/$fn"; rm -rf "$tmp"; s=$(du -h "$base/$fn"|cut -f1); step "完成 → $label/$fn ($s)" 32
            else step "未找到可执行文件: $tmp" 31; fi
        fi
    done
}

# CLI 模式
if [ $# -gt 0 ]; then
    if [ "${1:-}" = "hash" ]; then sha_table; exit 0; fi
    MODE="${1:-both}"; CONFIG="${2:-Release}"; [ "${3:-}" = "opt" ] && TRIM_SEL=1
    S=$(date +%s)
    if [ "$MODE" != "fd" ]; then echo -e "\n\e[35m--- 自包含 ---\e[0m"; publish_one "true" "self-contained" "${RIDS[@]}"; fi
    if [ "$MODE" != "sc" ]; then echo -e "\n\e[35m--- 依赖运行时 ---\e[0m"; publish_one "false" "framework-dependent" "${RIDS[@]}"; fi
    E=$(date +%s); echo -e "\n\e[36m完成 $((E-S))s\e[0m"; sha_table; exit 0
fi

# TUI 模式
OLD=$(stty -g 2>/dev/null||true); trap 'stty "$OLD" 2>/dev/null; printf "\e[?25h\e[2J"' EXIT
stty -echo -icanon min 0 time 0 2>/dev/null||true; printf "\e[?25l\e[2J"

draw(){
    local mk hi nm
    printf "\e[2J\e[H"
    echo "  A_Pair 发布"
    echo ""
    echo "  平台（空格切换）："
    for i in 0 1 2 3; do
        mk="[ ]"; [ "${SEL[$i]}" = "1" ] && mk="[✓]"
        hi=" "; [ "$CURSOR" = "$i" ] && hi=">"
        printf -v nm "%-14s" "${RIDS[$i]}"
        printf "    %s%s %s" "$hi" "$mk" "$nm"
        [ $((i%2)) -eq 1 ] && echo ""
    done
    echo ""
    local sa=" "; [ "$CURSOR" = "4" ] && sa=">"; local sn=" "; [ "$CURSOR" = "5" ] && sn=">"
    echo "  $sa[A] 全选   $sn[N] 全不选"
    echo ""
    local tl="" types=("自包含" "依赖运行时" "两者")
    for i in 0 1 2; do mk="○"; [ "$i" = "$TYPE_IDX" ] && mk="●"; hi=" "; [ "$CURSOR" = "$((6+i))" ] && hi=">"; tl+="$hi$mk ${types[$i]}   "; done
    echo "  发布类型：${tl%   }"
    echo ""
    mk="[ ]"; [ "$TRIM_SEL" = "1" ] && mk="[✓]"; tc=" "; [ "$CURSOR" = "9" ] && tc=">"
    echo "  $tc$mk 裁剪 (TrimMode=partial)"
    echo ""
    local b1=" "; [ "$CURSOR" = "10" ] && b1=">"; local b2=" "; [ "$CURSOR" = "11" ] && b2=">"
    echo "  $b1[ 开始编译 ]   $b2[ 仅计算哈希 ]"
    echo ""
    echo "  ↑↓移动  Space切换  Enter确认  Esc退出"
}

draw
while true; do
    k=$(dd bs=3 count=1 2>/dev/null||echo "")
    case "$k" in
        $'\e[A') CURSOR=$(((CURSOR-1+ITEMS)%ITEMS)); draw ;;
        $'\e[B') CURSOR=$(((CURSOR+1)%ITEMS)); draw ;;
        " ") if [ "$CURSOR" -lt 4 ]; then [ "${SEL[$CURSOR]}" = "1" ] && SEL[$CURSOR]=0 || SEL[$CURSOR]=1
            elif [ "$CURSOR" = "9" ]; then [ "$TRIM_SEL" = "1" ] && TRIM_SEL=0 || TRIM_SEL=1; fi; draw ;;
        A|a) [ "$CURSOR" = "4" ] && { for i in 0 1 2 3; do SEL[$i]=1; done; draw; } ;;
        N|n) [ "$CURSOR" = "5" ] && { for i in 0 1 2 3; do SEL[$i]=0; done; draw; } ;;
        $'\e') exit 0 ;;
        "") if [ "$CURSOR" = "10" ]; then break
            elif [ "$CURSOR" = "11" ]; then printf "\e[?25h\e[2J"; sha_table; exit 0
            elif [ "$CURSOR" -ge 6 ] && [ "$CURSOR" -le 8 ]; then TYPE_IDX=$((CURSOR-6)); draw; fi ;;
    esac
done
printf "\e[?25h\e[2J"

SP=(); for i in 0 1 2 3; do [ "${SEL[$i]}" = "1" ] && SP+=("${RIDS[$i]}"); done
if [ ${#SP[@]} -eq 0 ]; then echo -e "\e[31m未选择任何平台\e[0m"; exit 1; fi

doSc=false; doFd=false
[ "$TYPE_IDX" = "0" ] && doSc=true; [ "$TYPE_IDX" = "1" ] && doFd=true; [ "$TYPE_IDX" = "2" ] && { doSc=true; doFd=true; }
S=$(date +%s)
if $doSc; then echo -e "\n\e[35m--- 自包含 (Self-Contained) ---\e[0m"; publish_one "true" "self-contained" "${SP[@]}"; fi
if $doFd; then echo -e "\n\e[35m--- 依赖运行时 (Framework-Dependent) ---\e[0m"; publish_one "false" "framework-dependent" "${SP[@]}"; fi
E=$(date +%s); echo -e "\n\e[36m完成 $((E-S))s\e[0m"; sha_table
