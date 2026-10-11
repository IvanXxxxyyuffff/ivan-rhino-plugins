# 后台/免确认包装：注入 -Prompt 行为（跳确认框），调用主脚本
param([int]$TimeoutSec = 150)
& (Join-Path $PSScriptRoot 'xn-runall.ps1') -TimeoutSec $TimeoutSec -Prompt
