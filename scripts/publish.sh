#!/usr/bin/env bash
# ============================================================
# A_Pair 多平台发布（TUI 交互 + 命令行回退）
# 用法: ./publish.sh                           # TUI 模式
#        ./publish.sh both Release opt          # 命令行
#        ./publish.sh hash                      # 仅算哈希
# ============================================================
set -euo pipefail
cd ..

APP_NAME="A_Pair"
PROJECT="A_Pair.Presentation.Avalonia"
CONFIG="Release"
RIDS=("win-x64" "linux-x64" "osx-x64" "osx-arm64")
SUFFIXES=(".exe" "" "" "")
declare -A PLAT_SEL=([0]=0 [1]=0 [2]=0 [3]=0)
TYPE_IDX=2   # 0=自包含 1=依赖运行时 2=两者
TRIM_SEL=0
CURSOR=0
ITEMS=12

# ── 共享函数 ──
step() { echo -e "  [$(date +%H:%M:%S)] \e[${2:-37}m$1\e[0m"; }

print_sha256_table() {
    local files=() f hash name
    while IFS= read -r -d '' f; do files+=("$f"); done < <(find publish -type f -name "$APP_NAME-*" -print0 2>/dev/null | sort -z)
    if [ ${#files[@]} -eq 0 ]; then echo -e "\e[33m没有找到已发布文件\e[0m"; return; fi
    echo ""
    echo "| 文件 | SHA256 |"
    echo "|------|--------|"
    for f in "${files[@]}"; do
        hash=$(sha256sum "$f" | awk '{print tolower($1)}')
        name=$(basename "$f")
        echo "| \`$name\` | $hash |"
    done
}

publish_one() {
    local sc="$1" label="$2"; shift 2
    local rids=("$@") rid suffix tmp_out final_name
    local sc_flag="false"; [ "$sc" = "true" ] && sc_flag="true"
    local base="publish/$label"
    mkdir -p "$base"

    for rid in "${rids[@]}"; do
        suffix=""
        [ "${rid:0:3}" = "win" ] && suffix=".exe"
        tmp_out="$base/.tmp_$rid"
        final_name="$APP_NAME-$label-$rid$suffix"

        echo -ne "\033]0;A_Pair: $label / $rid\007"
        echo ""
        echo -e "\e[36m══════════════════════════════════════════\e[0m"
        echo -e "\e[36m  $label / $rid\e[0m"
        echo -e "\e[36m══════════════════════════════════════════\e[0m"
        step "开始编译..." 33

        local trim_args=()
        [ "$TRIM_SEL" = "1" ] && trim_args=(-p:PublishTrimmed=true -p:TrimMode=partial -p:SuppressTrimAnalysisWarnings=true)

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
            local s; s=$(du -h "$base/$final_name" | cut -f1)
            step "完成 → $label/$final_name ($s)" 32
        else
            local fallback; fallback=$(find "$tmp_out" -maxdepth 1 -type f -name "$APP_NAME*" | head -1)
            if [ -n "$fallback" ]; then
                mv "$fallback" "$base/$final_name"
                rm -rf "$tmp_out"
                local s2; s2=$(du -h "$base/$final_name" | cut -f1)
                step "完成 → $label/$final_name ($s2)" 32
            else
                step "未找到可执行文件，临时目录保留: $tmp_out" 31
            fi
        fi
    done
}

# ── CLI 模式 ──
if [ $# -gt 0 ] && [ "${1:-}" != "" ]; then
    OPTIMIZE="${3:-}"
    [ "${3:-}" = "opt" ] && TRIM_SEL=1
    if [ "${1:-}" = "hash" ]; then
        print_sha256_table
        exit 0
    fi
    MODE="${1:-both}"
    CONFIG="${2:-Release}"
    START=$(date +%s)
    if [ "$MODE" != "fd" ]; then
        echo -e "\n\e[35m--- 自包含 ---\e[0m"
        publish_one "true" "self-contained" "${RIDS[@]}"
    fi
    if [ "$MODE" != "sc" ]; then
        echo -e "\n\e[35m--- 依赖运行时 ---\e[0m"
        publish_one "false" "framework-dependent" "${RIDS[@]}"
    fi
    END=$(date +%s)
    echo -e "\n\e[36m完成，总用时 $((END-START)) 秒\e[0m"
    print_sha256_table
    exit 0
fi

# ── TUI 模式 ──
OLD_STTY=$(stty -g 2>/dev/null || true)
cleanup() { stty "$OLD_STTY" 2>/dev/null; printf "\e[?25h\e[2J"; }
trap cleanup EXIT
stty -echo -icanon min 0 time 0 2>/dev/null || true
printf "\e[?25l"

draw() {
    local mk hi nm
    printf "\e[H"
    echo "┌─────────────────────────────────────┐"
    echo "│  A_Pair 发布                         │"
    echo "├─────────────────────────────────────┤"
    echo "│ 平台（空格切换）：                    │"
    for i in 0 1 2 3; do
        mk="[ ]"; [ "${PLAT_SEL[$i]}" = "1" ] && mk="[✓]"
        hi=" "; [ "$CURSOR" = "$i" ] && hi=">"
        printf "│ %s%s %-14s" "$hi" "$mk" "${RIDS[$i]}"
        [ $((i % 2)) -eq 1 ] && echo " │"
    done
    echo "  │"
    local sa=" "; [ "$CURSOR" = "4" ] && sa=">"
    local sn=" "; [ "$CURSOR" = "5" ] && sn=">"
    echo "│ ${sa}[A] 全选   ${sn}[N] 全不选                  │"
    echo "├─────────────────────────────────────┤"
    echo "│ 发布类型：                            │"
    local types=("自包含" "依赖运行时" "两者")
    for i in 0 1 2; do
        mk="○"; [ "$i" = "$TYPE_IDX" ] && mk="●"
        hi=" "; [ "$CURSOR" = "$((6+i))" ] && hi=">"
        printf "│ %s%s %s" "$hi" "$mk" "${types[$i]}"
        [ "$i" -lt 2 ] && printf "   "
    done
    echo "│"
    echo "├─────────────────────────────────────┤"
    mk="[ ]"; [ "$TRIM_SEL" = "1" ] && mk="[✓]"
    local tc=" "; [ "$CURSOR" = "9" ] && tc=">"
    echo "│ ${tc}$mk 裁剪 (TrimMode=partial)              │"
    echo "├─────────────────────────────────────┤"
    local b1=" "; [ "$CURSOR" = "10" ] && b1=">"
    local b2=" "; [ "$CURSOR" = "11" ] && b2=">"
    echo "│ ${b1}[ 开始编译 ]   ${b2}[ 仅计算哈希 ]            │"
    echo "└─────────────────────────────────────┘"
    echo "↑↓移动  Space切换  Enter确认  Esc退出"
}

printf "\e[2J"
draw

while true; do
    key=$(dd bs=3 count=1 2>/dev/null || echo "")
    case "$key" in
        $'\e[A') CURSOR=$(( (CURSOR - 1 + ITEMS) % ITEMS )); draw ;;
        $'\e[B') CURSOR=$(( (CURSOR + 1) % ITEMS )); draw ;;
        " ")  # Space
            if [ "$CURSOR" -lt 4 ]; then
                [ "${PLAT_SEL[$CURSOR]}" = "1" ] && PLAT_SEL[$CURSOR]=0 || PLAT_SEL[$CURSOR]=1
            elif [ "$CURSOR" = "9" ]; then
                [ "$TRIM_SEL" = "1" ] && TRIM_SEL=0 || TRIM_SEL=1
            fi
            draw ;;
        A|a) [ "$CURSOR" = "4" ] && { for i in 0 1 2 3; do PLAT_SEL[$i]=1; done; draw; } ;;
        N|n) [ "$CURSOR" = "5" ] && { for i in 0 1 2 3; do PLAT_SEL[$i]=0; done; draw; } ;;
        "")  # Enter
            if [ "$CURSOR" = "10" ]; then break
            elif [ "$CURSOR" = "11" ]; then print_sha256_table; exit 0
            elif [ "$CURSOR" -ge 6 ] && [ "$CURSOR" -le 8 ]; then TYPE_IDX=$((CURSOR - 6)); draw; fi ;;
        $'\e') exit 0 ;;
    esac
done

cleanup
trap - EXIT

# 提取选中平台
SEL=()
for i in 0 1 2 3; do [ "${PLAT_SEL[$i]}" = "1" ] && SEL+=("${RIDS[$i]}"); done
if [ ${#SEL[@]} -eq 0 ]; then echo -e "\e[31m未选择任何平台\e[0m"; exit 1; fi

doSc=false; doFd=false
[ "$TYPE_IDX" = "0" ] && doSc=true
[ "$TYPE_IDX" = "1" ] && doFd=true
[ "$TYPE_IDX" = "2" ] && { doSc=true; doFd=true; }

START=$(date +%s)
if $doSc; then
    echo -e "\n\e[35m--- 自包含 (Self-Contained) ---\e[0m"
    publish_one "true" "self-contained" "${SEL[@]}"
fi
if $doFd; then
    echo -e "\n\e[35m--- 依赖运行时 (Framework-Dependent) ---\e[0m"
    publish_one "false" "framework-dependent" "${SEL[@]}"
fi
END=$(date +%s)
echo -e "\n\e[36m完成，总用时 $((END-START)) 秒\e[0m"
print_sha256_table
