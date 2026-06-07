#!/usr/bin/env pwsh
<#
.SYNOPSIS
  清理解决方案下所有 bin/ 和 obj/ 目录
.DESCRIPTION
  递归扫描并删除，释放磁盘空间。默认先列出并确认。
.PARAMETER Force
  跳过确认直接删除。
.PARAMETER DryRun
  仅列出，不删除。
.EXAMPLE
  .\scripts\clean.ps1             # 列出 + 确认
  .\scripts\clean.ps1 -Force      # 直接删除
  .\scripts\clean.ps1 -DryRun     # 仅预览
#>

param(
    [switch]$Force,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

Write-Host "=== A_Pair 清理 bin/ & obj/ ===" -ForegroundColor Cyan
Write-Host "根目录: $Root`n" -ForegroundColor Gray

$dirs = @(Get-ChildItem -Path $Root -Directory -Recurse -Filter bin)
$dirs += @(Get-ChildItem -Path $Root -Directory -Recurse -Filter obj)
$dirs = @($dirs | Sort-Object FullName -Unique)

if ($dirs.Count -eq 0) {
    Write-Host "没有找到 bin/ 或 obj/ 目录" -ForegroundColor Green
    exit 0
}

# 计算总大小
$totalSize = 0
foreach ($d in $dirs) {
    $size = (Get-ChildItem -Path $d.FullName -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
    $totalSize += $size
    $mb = if ($size) { [math]::Round($size / 1MB, 1) } else { 0 }
    Write-Host "  $($d.FullName.Replace($Root, '.'))  ($mb MB)" -ForegroundColor Gray
}

$totalMB = [math]::Round($totalSize / 1MB, 1)
Write-Host "`n共 $($dirs.Count) 个目录，$totalMB MB" -ForegroundColor Yellow

if ($DryRun) {
    Write-Host "DryRun — 未删除任何内容" -ForegroundColor Cyan
    exit 0
}

if (-not $Force) {
    $confirm = Read-Host "确认删除? (y/N)"
    if ($confirm -ne 'y' -and $confirm -ne 'Y') {
        Write-Host "已取消" -ForegroundColor Gray
        exit 0
    }
}

foreach ($d in $dirs) {
    Remove-Item -Path $d.FullName -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "已清理 $totalMB MB" -ForegroundColor Green
