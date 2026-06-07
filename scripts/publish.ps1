#!/usr/bin/env pwsh
<#
.SYNOPSIS
  A_Pair 多平台发布 — 请在 scripts/ 目录下执行
.EXAMPLE
  cd scripts
  .\publish.ps1              # 自包含 + 依赖运行时
  .\publish.ps1 -Mode sc     # 仅自包含
  .\publish.ps1 -Mode fd     # 仅依赖运行时
#>

param(
    [ValidateSet("both", "sc", "fd")]
    [string]$Mode = "both",
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Set-Location ..

$Project = "A_Pair.Presentation.Avalonia"
$Rids = @("win-x64", "linux-x64", "osx-x64", "osx-arm64")

function Publish-One($SelfContained, $Label) {
    $scFlag = if ($SelfContained) { "true" } else { "false" }
    $base = "publish/$Label"

    foreach ($rid in $Rids) {
        $out = "$base/$rid"
        Write-Host "[$Label] $rid " -ForegroundColor Yellow -NoNewline

        dotnet publish $Project -c $Configuration -r $rid --self-contained $scFlag `
            -p:PublishSingleFile=true -p:PublishTrimmed=true -o $out 2>&1 | Select-Object -Last 1

        if ($LASTEXITCODE -ne 0) { Write-Host "  FAILED" -ForegroundColor Red; exit $LASTEXITCODE }

        $exe = if ($rid -like "win*") { "$Project.exe" } else { $Project }
        if (Test-Path "$out/$exe") {
            $size = [math]::Round((Get-Item "$out/$exe").Length / 1MB, 1)
            Write-Host "  -> $out ($size MB)" -ForegroundColor Green
        }
    }
}

Write-Host "=== A_Pair 发布 ($Configuration) ===" -ForegroundColor Cyan

if ($Mode -ne "fd") {
    Write-Host "`n--- 自包含 (Self-Contained) ---" -ForegroundColor Magenta
    Publish-One $true "sc"
}
if ($Mode -ne "sc") {
    Write-Host "`n--- 依赖运行时 (Framework-Dependent) ---" -ForegroundColor Magenta
    Publish-One $false "fd"
}

Write-Host "`n=== 完成 ===" -ForegroundColor Cyan
