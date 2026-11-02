<#
.SYNOPSIS
    DSP 工具台：把「初始生成 / 规划 / 存档融合 / 渲染服务」串成菜单或子命令。

.DESCRIPTION
    双击 DSP.bat 进入菜单；命令行也可直接用子命令（供自动化/Web 面板复用）：
      dsp.ps1 status
      dsp.ps1 init  -Seed 84272982 -Mult 0.1
      dsp.ps1 plan  84272982 -Seconds 300
      dsp.ps1 merge 84272982 -Save "my-save.dsv"
      dsp.ps1 serve 84272982
      dsp.ps1 stop
      dsp.ps1 csv   -Save "my-save.dsv"
      dsp.ps1 all   84272982 -Save "my-save.dsv"

.NOTES
    只调用现有 exe，不修改任何工程；x86/x64 由本脚本固定（Searcher=x86，Save=x64）。
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)][string]$Command = '',
    [Parameter(Position = 1)][string]$SeedArg = '',
    [string]$Save = '',
    [double]$Mult = 0,
    [int]$Seconds = 0,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

# ============================ 路径常量 ============================
$ScriptDir = $PSScriptRoot                     # dsp\src\scripts
$SrcRoot   = Split-Path -Parent $ScriptDir     # dsp\src
$DspRoot   = Split-Path -Parent $SrcRoot       # dsp
$OutRoot   = Join-Path $SrcRoot 'DspSearcher\bin\Debug\net48\search-out'   # 与 DspSearcher 默认输出一致
$LogDir    = Join-Path $DspRoot '_scratch\logs'
$StateFile = Join-Path $DspRoot '_scratch\dsp-state.json'
$SaveDir   = if ($env:DSP_SAVE_DIR) { $env:DSP_SAVE_DIR }
             elseif (Test-Path (Join-Path $env:USERPROFILE 'Documents\Dyson Sphere Program\Save')) {
                 Join-Path $env:USERPROFILE 'Documents\Dyson Sphere Program\Save'
             } else { '' }

$ExeSearch = Join-Path $SrcRoot 'DspSearcher\bin\Debug\net48\DspSearcher.exe'
$ExePlan   = Join-Path $SrcRoot 'DspPlan\bin\Release\net8.0\DspPlan.exe'
$ExeSave   = Join-Path $SrcRoot 'DspSave\bin\Debug\net8.0\DspSave.exe'
$ExeView   = Join-Path $SrcRoot 'DspView\bin\Debug\net8.0\DspView.exe'
$ViewWork  = Join-Path $SrcRoot 'DspView\bin\Debug\net8.0'
$PlanOut   = Join-Path $SrcRoot 'DspPlan\bin\Release\net8.0\out'
$Port      = 8765

# ============================ 状态 ============================
$script:Ctx = @{ Seed = ''; Mult = 0.1; Seconds = 300; Save = '' }

function Load-State {
    if (Test-Path $StateFile) {
        try {
            $j = Get-Content $StateFile -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($j.seed)     { $script:Ctx.Seed = [string]$j.seed }
            if ($j.mult)     { $script:Ctx.Mult = [double]$j.mult }
            if ($j.seconds)  { $script:Ctx.Seconds = [int]$j.seconds }
            if ($j.save)     { $script:Ctx.Save = [string]$j.save }
        } catch { }
    }
    if ($SeedArg)          { $script:Ctx.Seed = $SeedArg }
    if ($Mult -gt 0)       { $script:Ctx.Mult = $Mult }
    if ($Seconds -gt 0)    { $script:Ctx.Seconds = $Seconds }
    if ($Save)             { $script:Ctx.Save = $Save }
}

function Save-State {
    try {
        if (-not (Test-Path (Split-Path -Parent $StateFile))) {
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $StateFile) | Out-Null
        }
        $script:Ctx | ConvertTo-Json | Set-Content -Path $StateFile -Encoding UTF8
    } catch { }
}

# ============================ 小工具 ============================
function Say-Head([string]$t) { Write-Host ''; Write-Host ("=== " + $t + " ===") -ForegroundColor Cyan }
function Say-Ok([string]$t)   { Write-Host ("[OK] " + $t) -ForegroundColor Green }
function Say-Warn([string]$t) { Write-Host ("[!] " + $t) -ForegroundColor Yellow }
function Say-Err([string]$t)  { Write-Host ("[X] " + $t) -ForegroundColor Red }
function Say-Cmd([string]$t)  { Write-Host ("  > " + $t) -ForegroundColor DarkGray }

function Pause-Menu {
    Write-Host ''
    Write-Host '按回车返回菜单…' -ForegroundColor DarkGray
    [void](Read-Host)
}

function Clear-Screen {
    try { Clear-Host } catch { }
}

function Get-LogFile {
    if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Force -Path $LogDir | Out-Null }
    return (Join-Path $LogDir ('dsp-' + (Get-Date -Format 'yyyyMMdd') + '.log'))
}

function Get-OutDir([string]$s) { return (Join-Path $OutRoot ('single-' + $s)) }
function Get-Galaxy([string]$s) { return (Join-Path (Get-OutDir $s) 'galaxy.json') }

function Assert-Seed {
    if (-not $script:Ctx.Seed) { throw '还没有选定种子（菜单第 1 项或 -Seed）' }
    if ($script:Ctx.Seed -notmatch '^\d+$') { throw ('种子必须是数字：' + $script:Ctx.Seed) }
}

function Invoke-Step {
    param([string]$Exe, [string[]]$Arguments, [string]$LogFile)

    if (-not (Test-Path $Exe)) {
        throw ('找不到 ' + $Exe + '（先构建：见 COMMANDS.md 第 0 节）')
    }
    Say-Cmd ((Split-Path -Leaf $Exe) + ' ' + ($Arguments -join ' '))

    # 每个 exe 都用自身 bin 目录作为工作目录（依赖那里的 prototypes/ data/）
    $work = Split-Path -Parent $Exe
    Push-Location $work
    try {
        if ($LogFile) {
            & $Exe @Arguments 2>&1 | Tee-Object -FilePath $LogFile -Append
        } else {
            & $Exe @Arguments
        }
        $code = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    if ($code -ne 0) {
        throw ('命令失败，退出码 ' + $code)
    }
}

# ============================ 动作 ============================
function Do-Init {
    param([switch]$SkipConfirm)
    Assert-Seed
    $seed = $script:Ctx.Seed
    $gal  = Get-Galaxy $seed
    $dir  = Get-OutDir $seed

    if ((Test-Path $gal) -and (-not $Force) -and (-not $SkipConfirm)) {
        if ($Command -ne '') { throw ('已存在 ' + $gal + '，加 -Force 覆盖') }
        Say-Warn ('已存在 ' + $gal)
        $ans = Read-Host '覆盖重算？(y/N)'
        if ($ans -notmatch '^[Yy]') { Say-Warn '已取消'; return }
    }

    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Say-Head ('初始生成  seed=' + $seed + '  倍率=' + $script:Ctx.Mult)
    Invoke-Step $ExeSearch @(
        '--headless', '--seed', $seed,
        '--resource-multiplier', ([string]$script:Ctx.Mult),
        '--out', $dir, '--json', $gal
    ) (Get-LogFile)
    Say-Ok ('产出 ' + $gal)
}

function Do-Plan {
    Assert-Seed
    $seed = $script:Ctx.Seed
    $gal  = Get-Galaxy $seed
    if (-not (Test-Path $gal)) { throw ('缺少 ' + $gal + '，请先做初始生成') }

    Say-Head ('规划  seed=' + $seed + '  search ' + $script:Ctx.Seconds + 's + LNS + apply')
    $log = Get-LogFile
    Invoke-Step $ExePlan @('search', $gal, '--seconds', ([string]$script:Ctx.Seconds)) $log
    Invoke-Step $ExePlan @('lns', $gal) $log
    Invoke-Step $ExePlan @('apply', $gal) $log
    Say-Ok ('规划已写回 ' + $gal + '（产物另见 ' + $PlanOut + '）')
}

function Do-Merge {
    Assert-Seed
    $seed = $script:Ctx.Seed
    $gal  = Get-Galaxy $seed
    if (-not (Test-Path $gal))  { throw ('缺少 ' + $gal + '，请先做初始生成') }
    if (-not $script:Ctx.Save)  { throw '还没有选存档（菜单第 3 项或 -Save）' }
    if (-not (Test-Path $script:Ctx.Save)) { throw ('存档不存在：' + $script:Ctx.Save) }

    Say-Head ('存档融合  ' + (Split-Path -Leaf $script:Ctx.Save))
    Invoke-Step $ExeSave @('--merge', $script:Ctx.Save, '--galaxy', $gal) (Get-LogFile)
    Say-Ok '已更新名字 / 剩余储量 / 已探索 / 工厂 / 戴森球 / 黑雾'
    Say-Warn '浏览器里按 F5 刷新即可（服务无需重启）'
}

function Do-Serve {
    Assert-Seed
    $gal = Get-Galaxy $script:Ctx.Seed
    if (-not (Test-Path $gal)) { throw ('缺少 ' + $gal + '，请先做初始生成') }
    if (-not (Test-Path $ExeView)) { throw ('找不到 ' + $ExeView) }

    $proc = Get-Process DspView -ErrorAction SilentlyContinue
    if ($proc) {
        Say-Warn ('DspView 已在运行（PID ' + $proc.Id + '）；若数据换了请先停止再启动')
    } else {
        Say-Head ('启动网页服务 端口 ' + $Port)
        $argLine = '--galaxy "' + $gal + '" --serve --port ' + $Port
        Start-Process -FilePath $ExeView -ArgumentList $argLine -WorkingDirectory $ViewWork -WindowStyle Hidden
        Start-Sleep -Seconds 2
        if (-not (Get-Process DspView -ErrorAction SilentlyContinue)) { throw 'DspView 启动失败（看 web 目录/端口占用）' }
        Say-Ok '服务已启动'
    }
    $url = 'http://localhost:' + $Port + '/'
    Say-Ok ('打开 ' + $url)
    Start-Process $url
}

function Do-Stop {
    Say-Head '停止网页服务'
    $proc = Get-Process DspView -ErrorAction SilentlyContinue
    if (-not $proc) { Say-Warn '没有在运行'; return }
    $proc | Stop-Process -Force
    Say-Ok ('已停止 @(' + ($proc.Id -join ',') + ')')
}

function Do-Csv {
    if (-not $script:Ctx.Save) { throw '还没有选存档（菜单第 3/8 项或 -Save）' }
    if (-not (Test-Path $script:Ctx.Save)) { throw ('存档不存在：' + $script:Ctx.Save) }
    $out = Join-Path $OutRoot 'save-check.csv'
    Say-Head ('存档解析导出  ' + (Split-Path -Leaf $script:Ctx.Save))
    Invoke-Step $ExeSave @('--save', $script:Ctx.Save, '--out', $out) (Get-LogFile)
    Say-Ok ('产物在 ' + (Split-Path -Parent $out) + '（save-check*.csv）')
}

function Get-GalaxyInfo([string]$path) {
    $info = @{ Stars = 0; Planets = 0; Seed = ''; SizeKB = 0 }
    if (-not (Test-Path $path)) { return $info }
    $info.SizeKB = [int]((Get-Item $path).Length / 1KB)
    try {
        $g = Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($g.meta -and $g.meta.seed) { $info.Seed = [string]$g.meta.seed }
        if ($g.stars) {
            $info.Stars = @($g.stars).Count
            foreach ($st in $g.stars) {
                if ($st.planets) { $info.Planets += @($st.planets).Count }
            }
        }
    } catch { }
    return $info
}

function Show-Status {
    Say-Head '环境'
    $exes = @(
        @{ N = 'DspSearcher (x86 精算)'; P = $ExeSearch },
        @{ N = 'DspPlan (Release)';      P = $ExePlan },
        @{ N = 'DspSave (x64 存档)';     P = $ExeSave },
        @{ N = 'DspView (渲染)';         P = $ExeView }
    )
    foreach ($e in $exes) {
        if (Test-Path $e.P) {
            $t = (Get-Item $e.P).LastWriteTime.ToString('MM-dd HH:mm')
            Write-Host ('  [OK] ' + $e.N + '  ' + $t) -ForegroundColor Green
        } else {
            Write-Host ('  [X]  ' + $e.N + '  缺失：' + $e.P) -ForegroundColor Red
        }
    }

    Say-Head ('当前种子 ' + ($(if ($script:Ctx.Seed) { $script:Ctx.Seed } else { '(未选)' })))
    if ($script:Ctx.Seed -match '^\d+$') {
        $gal = Get-Galaxy $script:Ctx.Seed
        if (Test-Path $gal) {
            $i = Get-GalaxyInfo $gal
            Write-Host ('  galaxy.json  ' + $i.SizeKB + ' KB  恒星 ' + $i.Stars + '  行星 ' + $i.Planets)
            $web = Join-Path (Get-OutDir $script:Ctx.Seed) 'web\index.html'
            if (Test-Path $web) { Write-Host ('  web        已生成  ' + $web) } else { Write-Host '  web        未生成' }
        } else {
            Write-Host ('  未生成：' + $gal) -ForegroundColor Yellow
        }
    }

    Say-Head '服务'
    $proc = Get-Process DspView -ErrorAction SilentlyContinue
    if ($proc) { Write-Host ('  [OK] DspView 运行中 PID ' + ($proc.Id -join ',') + '  http://localhost:' + $Port + '/') -ForegroundColor Green }
    else       { Write-Host '  [--] DspView 未运行' }

    if ($script:Ctx.Save) { Write-Host ('  存档      ' + $script:Ctx.Save) }
    Write-Host ('  输出根    ' + $OutRoot)
    Write-Host ('  日志      ' + (Join-Path $LogDir ('dsp-' + (Get-Date -Format 'yyyyMMdd') + '.log')))
}

function Do-All {
    Do-Init                 # 不跳过确认：galaxy.json 已存在时会询问是否覆盖
    Do-Plan
    if ($script:Ctx.Save) { Do-Merge } else { Say-Warn '没有指定存档，跳过融合' }
    Do-Serve
}

function Show-LogTail {
    $log = Join-Path $LogDir ('dsp-' + (Get-Date -Format 'yyyyMMdd') + '.log')
    Say-Head '最近日志'
    if (-not (Test-Path $log)) { Say-Warn '今天还没有日志'; return }
    Get-Content $log -Tail 200
}

# ============================ 交互输入 ============================
function Ask-Seed {
    $cur = $script:Ctx.Seed
    $s = Read-Host ('种子' + $(if ($cur) { ' [' + $cur + ']' } else { '' }))
    if (-not $s) { $s = $cur }
    if (-not $s) { $s = Read-Host '请输入 8 位种子' }
    if ($s -notmatch '^\d+$') { throw ('种子必须是数字：' + $s) }
    $script:Ctx.Seed = $s
}

function Ask-Mult {
    $s = Read-Host ('资源倍率 [' + $script:Ctx.Mult + ']')
    if (-not $s) { return }
    $v = 0.0
    if (-not [double]::TryParse($s, [ref]$v)) { throw ('倍率必须是数字：' + $s) }
    $script:Ctx.Mult = $v
}

function Ask-Seconds {
    $s = Read-Host ('规划时长秒 [' + $script:Ctx.Seconds + ']')
    if (-not $s) { return }
    $v = 0
    if (-not [int]::TryParse($s, [ref]$v)) { throw ('秒数必须是整数：' + $s) }
    $script:Ctx.Seconds = $v
}

function Select-Save {
    $files = @()
    if (Test-Path $SaveDir) {
        $files = @(Get-ChildItem $SaveDir -Filter *.dsv -File | Sort-Object LastWriteTime -Descending | Select-Object -First 10)
    }
    Write-Host ''
    if ($files.Count -eq 0) {
        Say-Warn ('没有在 ' + $SaveDir + ' 找到 .dsv')
        $p = Read-Host '请输入存档完整路径'
        if (Test-Path $p) { $script:Ctx.Save = $p } else { throw ('存档不存在：' + $p) }
        return
    }
    Write-Host '最近存档：' -ForegroundColor Cyan
    for ($i = 0; $i -lt $files.Count; $i++) {
        $f = $files[$i]
        $mb = [math]::Round($f.Length / 1MB, 1)
        Write-Host ('  ' + ($i + 1) + ') ' + $f.Name + '   ' + $mb + ' MB   ' + $f.LastWriteTime.ToString('MM-dd HH:mm'))
    }
    Write-Host '  0) 手动输入路径'
    $n = Read-Host '选择编号'
    if ($n -eq '0') {
        $p = Read-Host '存档完整路径'
        if (Test-Path $p) { $script:Ctx.Save = $p } else { throw ('存档不存在：' + $p) }
        return
    }
    $idx = 0
    if (-not [int]::TryParse($n, [ref]$idx) -or $idx -lt 1 -or $idx -gt $files.Count) { throw ('无效编号：' + $n) }
    $script:Ctx.Save = $files[$idx - 1].FullName
    Say-Ok ('已选 ' + (Split-Path -Leaf $script:Ctx.Save))
}

# ============================ 菜单 ============================
function Show-Menu {
    if ([Console]::IsInputRedirected) {
        Say-Err '当前不是交互式控制台（stdin 被重定向）：请双击 DSP.bat，或改用子命令（如 dsp.ps1 status）'
        return
    }
    while ($true) {
        Clear-Screen
        $svc = if (Get-Process DspView -ErrorAction SilentlyContinue) { '运行中' } else { '未启动' }
        $seedTxt = if ($script:Ctx.Seed) { $script:Ctx.Seed } else { '未选' }
        $saveTxt = if ($script:Ctx.Save) { Split-Path -Leaf $script:Ctx.Save } else { '未选' }
        Write-Host '============================================================' -ForegroundColor Cyan
        Write-Host ' DSP 工具台' -ForegroundColor White
        Write-Host (' 当前  seed=' + $seedTxt + '  倍率=' + $script:Ctx.Mult + '  规划=' + $script:Ctx.Seconds + 's') -ForegroundColor DarkGray
        Write-Host ('       存档=' + $saveTxt + '  服务=' + $svc) -ForegroundColor DarkGray
        Write-Host '============================================================' -ForegroundColor Cyan
        Write-Host '  1) 初始生成（星系 → galaxy.json）'
        Write-Host '  2) 规划（搜索 + LNS + 写回）'
        Write-Host '  3) 存档融合（选择 .dsv）'
        Write-Host '  4) 启动 / 打开网页'
        Write-Host '  5) 一键全流程（1 → 2 → 3 → 4）'
        Write-Host '  6) 查看状态'
        Write-Host '  7) 停止网页服务'
        Write-Host '  8) 存档解析导出 CSV'
        Write-Host '  9) 查看最近日志'
        Write-Host '  s) 设置（倍率 / 规划秒数）'
        Write-Host '  0) 退出' -ForegroundColor DarkGray
        Write-Host '------------------------------------------------------------'
        $c = Read-Host '请输入编号后回车'

        try {
            switch ($c) {
                '1' { Ask-Seed; Ask-Mult; Do-Init;            Pause-Menu }
                '2' { Ask-Seed; Do-Plan;                      Pause-Menu }
                '3' { Ask-Seed; Select-Save; Do-Merge;        Pause-Menu }
                '4' { Ask-Seed; Do-Serve;                     Pause-Menu }
                '5' { Ask-Seed; Ask-Mult; Select-Save; Do-All; Pause-Menu }
                '6' { Show-Status;                            Pause-Menu }
                '7' { Do-Stop;                                Pause-Menu }
                '8' { Select-Save; Do-Csv;                    Pause-Menu }
                '9' { Show-LogTail;                           Pause-Menu }
                's' { Ask-Mult; Ask-Seconds;                  Pause-Menu }
                'S' { Ask-Mult; Ask-Seconds;                  Pause-Menu }
                '0' { return }
                default { Say-Warn ('无效编号：' + $c); Start-Sleep -Milliseconds 600 }
            }
        } catch {
            Say-Err $_.Exception.Message
            Pause-Menu
        }
        Save-State
    }
}

# ============================ 入口 ============================
Load-State

try {
    switch ($Command.ToLower()) {
        ''      { Show-Menu }
        'status'{ Show-Status }
        'init'  { Do-Init }
        'plan'  { Do-Plan }
        'merge' { Do-Merge }
        'serve' { Do-Serve }
        'stop'  { Do-Stop }
        'csv'   { Do-Csv }
        'all'   { Do-All }
        'log'   { Show-LogTail }
        default {
            Write-Host '用法：dsp.ps1 [status|init|plan|merge|serve|stop|csv|all|log] [seed] [-Save 存档] [-Mult 0.1] [-Seconds 300]' -ForegroundColor Yellow
            Write-Host '不带子命令 = 进入菜单' -ForegroundColor Yellow
            exit 1
        }
    }
} catch {
    Say-Err $_.Exception.Message
    exit 1
} finally {
    Save-State
}
