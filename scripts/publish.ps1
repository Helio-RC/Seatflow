#!/usr/bin/env pwsh
<#
.SYNOPSIS
  A_Pair 多平台发布 — 自包含 / 依赖运行时，单文件
.DESCRIPTION
  按平台输出到 publish/sc/{rid}/ 和 publish/fd/{rid}/。
  自包含 ~50MB（裁剪后），依赖运行时 ~20MB。
.PARAMETER Mode
  both (默认) / sc (仅自包含) / fd (仅依赖运行时)
.PARAMETER Configuration
  Release (默认) / Debug
.EXAMPLE
  .\scripts\publish.ps1                  # 两类都发布
  .\scripts\publish.ps1 -Mode sc         # 仅自包含
  .\scripts\publish.ps1 -Mode fd         # 仅依赖运行时
#>

param(
    [ValidateSet("both", "sc", "fd")]
    [string]$Mode = "both",
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$Project = "A_Pair.Presentation.Avalonia"
$Rids = @("win-x64", "linux-x64", "osx-x64", "osx-arm64")
$Root = Join-Path (Get-Location) "publish"

function Publish-One($SelfContained, $Label) {
    $base = Join-Path $Root $Label
    $scFlag = if ($SelfContained) { "true" } else { "false" }

    foreach ($rid in $Rids) {
        $out = Join-Path $base $rid
        Write-Host "[$Label] $rid " -ForegroundColor Yellow -NoNewline

        $args = @(
            "publish", $Project,
            "-c", $Configuration,
            "-r", $rid,
            "--self-contained", $scFlag,
            "-p:PublishSingleFile=true",
            "-p:PublishTrimmed=true",
            "-o", $out
        )

        dotnet @args 2>&1 | Select-Object -Last 1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  FAILED" -ForegroundColor Red
            exit $LASTEXITCODE
        }

        $exe = if ($rid -like "win*") { "$Project.exe" } else { $Project }
        $path = Join-Path $out $exe
        if (Test-Path $path) {
            $size = [math]::Round((Get-Item $path).Length / 1MB, 1)
            Write-Host "  -> $Label/$rid ($size MB)" -ForegroundColor Green
        }
        else {
            Write-Host "  -> $Label/$rid (bundle)" -ForegroundColor Green
        }
    }
}

Write-Host "=== A_Pair 发布 ($Configuration) ===" -ForegroundColor Cyan

if ($Mode -eq "both" -or $Mode -eq "sc") {
    Write-Host "`n--- 自包含 (Self-Contained) ---" -ForegroundColor Magenta
    Publish-One -SelfContained $true -Label "sc"
}

if ($Mode -eq "both" -or $Mode -eq "fd") {
    Write-Host "`n--- 依赖运行时 (Framework-Dependent) ---" -ForegroundColor Magenta
    Publish-One -SelfContained $false -Label "fd"
}

Write-Host "`n=== 完成 ===" -ForegroundColor Cyan
