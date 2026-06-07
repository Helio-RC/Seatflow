#!/usr/bin/env pwsh
<#
.SYNOPSIS
  A_Pair 多平台发布 — 在 scripts/ 目录下执行
.EXAMPLE
  cd scripts
  .\publish.ps1                  # 自包含 + 依赖运行时
  .\publish.ps1 -Mode sc         # 仅自包含
  .\publish.ps1 -Mode fd         # 仅依赖运行时
  .\publish.ps1 -HashOnly        # 仅计算已有文件的 SHA256
#>

param(
    [ValidateSet("both", "sc", "fd")]
    [string]$Mode = "both",
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [switch]$HashOnly
)

$ErrorActionPreference = "Stop"
Set-Location ..

$sw = [Diagnostics.Stopwatch]::StartNew()
$Project = "A_Pair.Presentation.Avalonia"
$Rids = @("win-x64", "linux-x64", "osx-x64", "osx-arm64")
$AppName = "A_Pair"

function Write-Step($text, $color = "White") {
    Write-Host "  [$([datetime]::Now.ToString('HH:mm:ss'))] $text" -ForegroundColor $color
}

function Print-Sha256Table {
    $files = @(Get-ChildItem publish -Recurse -File | Where-Object { $_.Name -like "$AppName-*" } | Sort-Object Name)
    if ($files.Count -eq 0) { Write-Host "没有找到已发布文件" -ForegroundColor Yellow; return }

    Write-Host ""
    Write-Host "| 文件 | SHA256 |" -ForegroundColor White
    Write-Host "|------|--------|" -ForegroundColor White
    foreach ($f in $files) {
        $hash = (Get-FileHash -Algorithm SHA256 $f.FullName).Hash.ToLower()
        Write-Host "| ``$($f.Name)`` | $hash |" -ForegroundColor Gray
    }
}

if ($HashOnly) {
    Write-Host "SHA256 校验值（Markdown 表格）：" -ForegroundColor Cyan
    Print-Sha256Table
    exit 0
}

function Publish-One($SelfContained, $Label) {
    $scFlag = if ($SelfContained) { "true" } else { "false" }
    $base = "publish/$Label"
    New-Item -ItemType Directory -Force -Path $base | Out-Null

    foreach ($rid in $Rids) {
        $tmpOut = "$base/.tmp_$rid"
        $suffix = if ($rid -like "win*") { ".exe" } else { "" }
        $finalName = "$AppName-$Label-$rid$suffix"

        $title = "A_Pair: $Label / $rid"
        $Host.UI.RawUI.WindowTitle = $title
        Write-Host ""
        Write-Host "══════════════════════════════════════════" -ForegroundColor Cyan
        Write-Host "  $title" -ForegroundColor Cyan
        Write-Host "══════════════════════════════════════════" -ForegroundColor Cyan
        Write-Step "开始编译..." -ForegroundColor Yellow

        dotnet publish $Project -c $Configuration -r $rid --self-contained $scFlag `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:IncludeAllContentForSelfExtract=true `
            -o $tmpOut

        if ($LASTEXITCODE -ne 0) {
            Write-Host ""; Write-Step "编译失败" -ForegroundColor Red; exit $LASTEXITCODE
        }

        $exeName = if ($suffix) { "$AppName$suffix" } else { $AppName }
        $built = Join-Path $tmpOut $exeName
        if (Test-Path $built) {
            Move-Item $built "$base/$finalName" -Force
            Remove-Item $tmpOut -Recurse -Force -ErrorAction SilentlyContinue
            $size = [math]::Round((Get-Item "$base/$finalName").Length / 1MB, 1)
            Write-Step "完成 → $Label/$finalName ($size MB)" -ForegroundColor Green
        }
        else {
            $fallback = Get-ChildItem $tmpOut -File | Where-Object { $_.Name -like "$AppName*" } | Select-Object -First 1
            if ($fallback) {
                Move-Item $fallback.FullName "$base/$finalName" -Force
                Remove-Item $tmpOut -Recurse -Force -ErrorAction SilentlyContinue
                $size = [math]::Round((Get-Item "$base/$finalName").Length / 1MB, 1)
                Write-Step "完成 → $Label/$finalName ($size MB)" -ForegroundColor Green
            }
            else {
                Write-Step "未找到可执行文件，临时目录保留: $tmpOut" -ForegroundColor Red
            }
        }
    }
}

$Host.UI.RawUI.WindowTitle = "A_Pair: 发布中..."
Write-Host "╔══════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  A_Pair 多平台发布               ║" -ForegroundColor Cyan
Write-Host "║  $Configuration | $(if ($Mode -eq 'both') {'SC + FD'} else {$Mode.ToUpper()})" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════╝" -ForegroundColor Cyan

if ($Mode -ne "fd") {
    Write-Host "`n┌─ 自包含 (Self-Contained) ─────────┐" -ForegroundColor Magenta
    Publish-One $true "sc"
    Write-Host "└────────────────────────────────────┘" -ForegroundColor Magenta
}
if ($Mode -ne "sc") {
    Write-Host "`n┌─ 依赖运行时 (Framework-Dependent) ──┐" -ForegroundColor Magenta
    Publish-One $false "fd"
    Write-Host "└────────────────────────────────────┘" -ForegroundColor Magenta
}

$sw.Stop()
Write-Host ""
Write-Host "══════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  全部完成，总用时 $([math]::Round($sw.Elapsed.TotalSeconds, 1)) 秒" -ForegroundColor Cyan
Write-Host "══════════════════════════════════════════" -ForegroundColor Cyan
$Host.UI.RawUI.WindowTitle = "A_Pair: 发布完成"

Write-Host ""
Write-Host "SHA256 校验值（可直接粘贴到 Release 说明）：" -ForegroundColor Cyan
Print-Sha256Table
