#!/usr/bin/env pwsh
<#
.SYNOPSIS
  A_Pair 多平台发布（TUI 交互 + 命令行回退）
.EXAMPLE
  cd scripts; .\publish.ps1           # TUI 模式
  .\publish.ps1 -Mode both            # 命令行：全平台
  .\publish.ps1 -HashOnly             # 仅算哈希
  .\publish.ps1 -Mode sc -Optimize    # 命令行：自包含 + 裁剪
#>

param(
    [ValidateSet("both", "self-contained", "framework-dependent")]
    [string]$Mode,
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [switch]$Optimize,
    [switch]$HashOnly
)

$ErrorActionPreference = "Stop"
Set-Location ..

$AppName = "A_Pair"
$Project = "A_Pair.Presentation.Avalonia"
$Platforms = @(
    @{Name="win-x64";     Suffix=".exe"; Selected=$false}
    @{Name="linux-x64";   Suffix="";     Selected=$false}
    @{Name="osx-x64";     Suffix="";     Selected=$false}
    @{Name="osx-arm64";   Suffix="";     Selected=$false}
)

# ── CLI 模式 ──
if ($Mode -or $HashOnly) {
    $m = if ($Mode -eq "self-contained") { "sc" }
         elseif ($Mode -eq "framework-dependent") { "fd" }
         else { "both" }

    if ($HashOnly) {
        Print-Sha256Table
        exit 0
    }

    $Rids = if ($Mode -and $Mode -ne "both") {
        @("win-x64","linux-x64","osx-x64","osx-arm64")
    } else { @("win-x64","linux-x64","osx-x64","osx-arm64") }

    $sw = [Diagnostics.Stopwatch]::StartNew()
    if ($m -ne "fd") {
        Write-Host "`n--- 自包含 ---" -ForegroundColor Magenta
        Publish-One $true "self-contained" @Rids
    }
    if ($m -ne "sc") {
        Write-Host "`n--- 依赖运行时 ---" -ForegroundColor Magenta
        Publish-One $false "framework-dependent" @Rids
    }
    $sw.Stop()
    Write-Host ""
    Write-Host "完成，总用时 $([math]::Round($sw.Elapsed.TotalSeconds,1))s" -ForegroundColor Cyan
    Print-Sha256Table
    exit 0
}

# ── TUI 模式 ──
[Console]::Clear()
$types = @("自包含", "依赖运行时", "两者")
$typeIdx = 2  # 默认两者
$trimSel = $false
$cursor = 0   # 0-3:平台 4:全选 5:全不选 6-8:类型 9:裁剪 10:编译 11:哈希
$items = 12

function Draw-UI {
    [Console]::SetCursorPosition(0, 0)
    Write-Host "┌─────────────────────────────────────┐"
    Write-Host "│  A_Pair 发布                         │"
    Write-Host "├─────────────────────────────────────┤"
    Write-Host "│ 平台（空格切换）：                    │"
    for ($i = 0; $i -lt 4; $i++) {
        $mk = if ($Platforms[$i].Selected) { "[✓]" } else { "[ ]" }
        $hi = if ($cursor -eq $i) { ">" } else { " " }
        $nm = $Platforms[$i].Name.PadRight(14)
        if ($i % 2 -eq 0) { Write-Host "│ " -NoNewline }
        Write-Host "$hi$mk $nm" -NoNewline
        if ($i % 2 -eq 1) { Write-Host " │" }
    }
    Write-Host "  │"
    $sa = if ($cursor -eq 4) { ">" } else { " " }
    $sn = if ($cursor -eq 5) { ">" } else { " " }
    Write-Host "│ $sa[A] 全选   $sn[N] 全不选                  │"
    Write-Host "├─────────────────────────────────────┤"
    Write-Host "│ 发布类型：                            │"
    for ($i = 0; $i -lt 3; $i++) {
        $mk = if ($i -eq $typeIdx) { "●" } else { "○" }
        $hi = if ($cursor -eq 6 + $i) { ">" } else { " " }
        Write-Host "│ $hi$mk $($types[$i])" -NoNewline
        if ($i -lt 2) { Write-Host "   " -NoNewline }
    }
    Write-Host "│"
    Write-Host "├─────────────────────────────────────┤"
    $tm = if ($trimSel) { "[✓]" } else { "[ ]" }
    $tc = if ($cursor -eq 9) { ">" } else { " " }
    Write-Host "│ $tc$tm 裁剪 (TrimMode=partial)              │"
    Write-Host "├─────────────────────────────────────┤"
    $b1 = if ($cursor -eq 10) { ">" } else { " " }
    $b2 = if ($cursor -eq 11) { ">" } else { " " }
    Write-Host "│ $b1[ 开始编译 ]   $b2[ 仅计算哈希 ]            │"
    Write-Host "└─────────────────────────────────────┘"
    Write-Host "↑↓移动  Space切换  Enter确认  Esc退出"
}

$null = [Console]::TreatControlCAsInput
[Console]::CursorVisible = $false
Draw-UI

while ($true) {
    $key = [Console]::ReadKey($true)
    switch ($key.Key) {
        UpArrow    { $cursor = ($cursor - 1 + $items) % $items; Draw-UI }
        DownArrow  { $cursor = ($cursor + 1) % $items; Draw-UI }
        Spacebar {
            if ($cursor -lt 4) { $Platforms[$cursor].Selected = -not $Platforms[$cursor].Selected }
            elseif ($cursor -eq 9) { $trimSel = -not $trimSel }
            Draw-UI
        }
        A { if ($cursor -eq 4) { foreach ($p in $Platforms) { $p.Selected = $true }; Draw-UI } }
        N { if ($cursor -eq 5) { foreach ($p in $Platforms) { $p.Selected = $false }; Draw-UI } }
        Enter {
            if ($cursor -eq 10) { break }  # 开始编译
            if ($cursor -eq 11) { Print-Sha256Table; exit 0 }  # 仅哈希
            if ($cursor -ge 6 -and $cursor -le 8) { $typeIdx = $cursor - 6; Draw-UI }
        }
        Escape { exit 0 }
    }
}

[Console]::CursorVisible = $true
[Console]::Clear()

$selPlats = @($Platforms | Where-Object Selected)
if ($selPlats.Count -eq 0) { Write-Host "未选择任何平台" -ForegroundColor Red; exit 1 }

$doSc = ($typeIdx -eq 0 -or $typeIdx -eq 2)
$doFd = ($typeIdx -eq 1 -or $typeIdx -eq 2)
$sw = [Diagnostics.Stopwatch]::StartNew()

if ($doSc) {
    Write-Host "`n--- 自包含 (Self-Contained) ---" -ForegroundColor Magenta
    Publish-One $true "self-contained" @($selPlats | ForEach-Object { $_.Name })
}
if ($doFd) {
    Write-Host "`n--- 依赖运行时 (Framework-Dependent) ---" -ForegroundColor Magenta
    Publish-One $false "framework-dependent" @($selPlats | ForEach-Object { $_.Name })
}
$sw.Stop()
Write-Host "`n完成，总用时 $([math]::Round($sw.Elapsed.TotalSeconds,1))s" -ForegroundColor Cyan
Print-Sha256Table

# ═══════════════════════════════════════════
# 共享函数
# ═══════════════════════════════════════════

function Write-Step($text, $color = "White") {
    Write-Host "  [$([datetime]::Now.ToString('HH:mm:ss'))] $text" -ForegroundColor $color
}

function Print-Sha256Table {
    $files = @(Get-ChildItem publish -Recurse -File | Where-Object { $_.Name -like "$AppName-*" } | Sort-Object Name)
    if ($files.Count -eq 0) { Write-Host "没有找到已发布文件" -ForegroundColor Yellow; return }
    Write-Host ""
    Write-Host "| 文件 | SHA256 |"
    Write-Host "|------|--------|"
    foreach ($f in $files) {
        $h = (Get-FileHash -Algorithm SHA256 $f.FullName).Hash.ToLower()
        Write-Host "| ``$($f.Name)`` | $h |" -ForegroundColor Gray
    }
}

function Publish-One($SelfContained, $Label, $Rids) {
    $scFlag = if ($SelfContained) { "true" } else { "false" }
    $base = "publish/$Label"
    New-Item -ItemType Directory -Force -Path $base | Out-Null

    foreach ($rid in $Rids) {
        $p = $Platforms | Where-Object Name -eq $rid | Select-Object -First 1
        $suffix = if ($p) { $p.Suffix } else { if ($rid -like "win*") { ".exe" } else { "" } }
        $tmpOut = "$base/.tmp_$rid"
        $finalName = "$AppName-$Label-$rid$suffix"

        $Host.UI.RawUI.WindowTitle = "A_Pair: $Label / $rid"
        Write-Host ""
        Write-Host "══════════════════════════════════════════" -ForegroundColor Cyan
        Write-Host "  $Label / $rid" -ForegroundColor Cyan
        Write-Host "══════════════════════════════════════════" -ForegroundColor Cyan
        Write-Step "开始编译..." -ForegroundColor Yellow

        $trimArgs = if ($Optimize) {
            @("-p:PublishTrimmed=true", "-p:TrimMode=partial", "-p:SuppressTrimAnalysisWarnings=true")
        } else { @() }

        dotnet publish $Project -c $Configuration -r $rid --self-contained $scFlag `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:IncludeAllContentForSelfExtract=true `
            @trimArgs `
            -o $tmpOut

        if ($LASTEXITCODE -ne 0) { Write-Host ""; Write-Step "编译失败" -ForegroundColor Red; exit $LASTEXITCODE }

        $exeName = if ($suffix) { "$AppName$suffix" } else { $AppName }
        $built = Join-Path $tmpOut $exeName
        if (Test-Path $built) {
            Move-Item $built "$base/$finalName" -Force
            Remove-Item $tmpOut -Recurse -Force -ErrorAction SilentlyContinue
            $size = [math]::Round((Get-Item "$base/$finalName").Length / 1MB, 1)
            Write-Step "完成 → $Label/$finalName ($size MB)" -ForegroundColor Green
        } else {
            $fallback = Get-ChildItem $tmpOut -File | Where-Object { $_.Name -like "$AppName*" } | Select-Object -First 1
            if ($fallback) {
                Move-Item $fallback.FullName "$base/$finalName" -Force
                Remove-Item $tmpOut -Recurse -Force -ErrorAction SilentlyContinue
                $size = [math]::Round((Get-Item "$base/$finalName").Length / 1MB, 1)
                Write-Step "完成 → $Label/$finalName ($size MB)" -ForegroundColor Green
            } else {
                Write-Step "未找到可执行文件，临时目录保留: $tmpOut" -ForegroundColor Red
            }
        }
    }
}
