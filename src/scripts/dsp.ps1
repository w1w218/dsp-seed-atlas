<#
.SYNOPSIS
    DSP 工具台：把「初始生成 / 规划 / 存档融合 / 渲染服务」串成菜单或子命令。

.DESCRIPTION
    双击 DSP.bat 进入菜单；命令行也可直接用子命令（供自动化/Web 面板复用）：
      dsp.ps1 status
      dsp.ps1 init  -Seed 84272982 -Mult 0.1
      dsp.ps1 plan  84272982 -Seconds 300 [-Threads N] [-LnsWindow N] [-LnsRounds N] [-SkipLns]
      dsp.ps1 merge 84272982 -Save "982-Z01-000.dsv"      （文件名或完整路径均可）
      dsp.ps1 serve 84272982 [-Port 8765]
      dsp.ps1 stop
      dsp.ps1 csv   -Save "982-Z01-000.dsv"
      dsp.ps1 all   84272982 -Save "982-Z01-000.dsv"

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
    [int]$Threads = 0,
    [int]$LnsWindow = 0,
    [int]$LnsRounds = 0,
    [switch]$SkipLns,
    [int]$Port = 0,
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
$ServeFile = Join-Path $DspRoot '_scratch\dsp-serve.json'

$ExeSearch = Join-Path $SrcRoot 'DspSearcher\bin\Debug\net48\DspSearcher.exe'
$ExePlan   = Join-Path $SrcRoot 'DspPlan\bin\Release\net8.0\DspPlan.exe'
$ExeSave   = Join-Path $SrcRoot 'DspSave\bin\Debug\net8.0\DspSave.exe'
$ExeView   = Join-Path $SrcRoot 'DspView\bin\Debug\net8.0\DspView.exe'
$ViewWork  = Join-Path $SrcRoot 'DspView\bin\Debug\net8.0'
$PlanOut   = Join-Path $SrcRoot 'DspPlan\bin\Release\net8.0\out'

$EnvPort = 0
if ($env:DSP_PORT) { try { $EnvPort = [int]$env:DSP_PORT } catch { } }
$RunPort = if ($Port -gt 0) { $Port } elseif ($EnvPort -gt 0) { $EnvPort } else { 8765 }

# ============================ 状态 ============================
$script:Ctx = @{
    Seed      = ''
    Mult      = 0.1
    Seconds   = 300
    Threads   = 0          # 0 = 用 DspPlan 默认
    LnsWindow = 12
    LnsRounds = 10
    SkipLns   = $false
    Save      = ''
    SaveDir   = ''
}

function Load-State {
    if (Test-Path $StateFile) {
        try {
            $j = Get-Content $StateFile -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($j.seed)      { $script:Ctx.Seed = [string]$j.seed }
            if ($j.mult)      { $v = [double]$j.mult; if ($v -gt 0 -and $v -le 100) { $script:Ctx.Mult = $v } }
            if ($j.seconds)   { $v = [int]$j.seconds;  if ($v -ge 30) { $script:Ctx.Seconds = $v } }
            if ($null -ne $j.threads)    { $v = [int]$j.threads;   if ($v -ge 0 -and $v -le 256)  { $script:Ctx.Threads = $v } }
            if ($j.lnsWindow) { $v = [int]$j.lnsWindow; if ($v -ge 0 -and $v -le 1000) { $script:Ctx.LnsWindow = $v } }
            if ($j.lnsRounds) { $v = [int]$j.lnsRounds; if ($v -ge 0 -and $v -le 1000) { $script:Ctx.LnsRounds = $v } }
            if ($null -ne $j.skipLns)    { $script:Ctx.SkipLns = [bool]$j.skipLns }
            if ($j.save)      { $script:Ctx.Save = [string]$j.save }
            if ($j.saveDir)   { $script:Ctx.SaveDir = [string]$j.saveDir }
        } catch { }
    }
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

function Get-LogFile([string]$tag = 'run') {
    if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Force -Path $LogDir | Out-Null }
    return (Join-Path $LogDir ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $tag + '.log'))
}

function Get-OutDir([string]$s) { return (Join-Path $OutRoot ('single-' + $s)) }
function Get-Galaxy([string]$s) { return (Join-Path (Get-OutDir $s) 'galaxy.json') }

function Invoke-Step {
    param([string]$Exe, [string[]]$Arguments, [string]$LogFile)

    if (-not (Test-Path $Exe)) {
        throw ('找不到 ' + $Exe + '（先构建：见 docs\COMMANDS.md）')
    }
    Say-Cmd ((Split-Path -Leaf $Exe) + ' ' + ($Arguments -join ' '))

    # 每个 exe 都用自身 bin 目录作为工作目录（依赖那里的 prototypes/ data/）
    $work = Split-Path -Parent $Exe
    Push-Location $work
    try {
        if ($LogFile) {
            # 转成纯文本再 Tee：避免 stderr 变成 ErrorRecord 后污染日志
            & $Exe @Arguments 2>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath $LogFile -Append
        } else {
            & $Exe @Arguments 2>&1 | ForEach-Object { $_.ToString() }
        }
        $code = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    if ($code -ne 0) {
        if ($LogFile -and (Test-Path $LogFile)) {
            Write-Host '  最近日志：' -ForegroundColor DarkGray
            Get-Content $LogFile -Tail 12 | ForEach-Object { Write-Host ('    ' + $_) -ForegroundColor DarkGray }
            throw ('命令失败，退出码 ' + $code + '（日志：' + $LogFile + '）')
        }
        throw ('命令失败，退出码 ' + $code)
    }
}

# ============================ 校验 ============================
function Assert-Seed {
    if (-not $script:Ctx.Seed) { throw '还没有选定种子（菜单第 1 项或 -Seed）' }
    if ($script:Ctx.Seed -notmatch '^\d+$') { throw ('种子必须是数字：' + $script:Ctx.Seed) }
    $v = 0L
    if (-not [long]::TryParse($script:Ctx.Seed, [ref]$v) -or $v -lt 0 -or $v -gt 2147483647) {
        throw ('种子超出范围（0 ~ 2147483647）：' + $script:Ctx.Seed)
    }
}

function Assert-Mult {
    if ($script:Ctx.Mult -le 0) { throw ('资源倍率必须大于 0：' + $script:Ctx.Mult) }
    if ($script:Ctx.Mult -gt 100) { throw ('资源倍率过大：' + $script:Ctx.Mult) }
}

function Assert-Seconds {
    if ($script:Ctx.Seconds -lt 30) { throw ('规划秒数至少 30（当前 ' + $script:Ctx.Seconds + '）') }
}

# ============================ 存档 ============================
function Get-SaveDir {
    if ($script:Ctx.SaveDir -and (Test-Path $script:Ctx.SaveDir)) { return $script:Ctx.SaveDir }
    if ($env:DSP_SAVE_DIR -and (Test-Path $env:DSP_SAVE_DIR)) { return $env:DSP_SAVE_DIR }
    $def = Join-Path $env:USERPROFILE 'Documents\Dyson Sphere Program\Save'
    if (Test-Path $def) { return $def }
    return ''
}

# 解析用户输入：完整路径 / 目录内文件名（可省略 .dsv）。
# 成功后记忆其所在目录。
function Resolve-SavePath([string]$path) {
    if (-not $path) { throw '未指定存档' }
    if ([IO.Path]::IsPathRooted($path)) {
        if (-not (Test-Path $path)) { throw ('存档不存在：' + $path) }
        $full = (Resolve-Path $path).Path
        $script:Ctx.SaveDir = Split-Path -Parent $full
        return $full
    }
    $dir = Get-SaveDir
    if (-not $dir) {
        throw '还没有记忆的存档目录：请输入完整路径（如 <存档目录>\982-Z01-000.dsv），成功后会自动记住目录'
    }
    $p = Join-Path $dir $path
    if (-not (Test-Path $p) -and $path -notlike '*.dsv') { $p = Join-Path $dir ($path + '.dsv') }
    if (-not (Test-Path $p)) { throw ('在 ' + $dir + ' 下未找到 ' + $path) }
    $full = (Resolve-Path $p).Path
    $script:Ctx.SaveDir = Split-Path -Parent $full
    return $full
}

# 只读存档头（毫秒级，不解析行星段）
function Get-SaveHeader([string]$path) {
    if (-not (Test-Path $ExeSave)) { throw ('找不到 ' + $ExeSave) }
    $work = Split-Path -Parent $ExeSave
    Push-Location $work
    try {
        $raw = & $ExeSave '--header' $path 2>&1
        $code = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    $text = @($raw | ForEach-Object { $_.ToString() })
    if ($code -ne 0) { throw ('读取存档头失败：' + ($text -join ' ')) }
    $json = $text | Where-Object { $_ -match '^\s*\{' } | Select-Object -First 1
    if (-not $json) { throw '存档头输出无法解析' }
    return ($json | ConvertFrom-Json)
}

function Get-GalaxyMeta([string]$path) {
    $g = Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json
    $m = $g.meta
    $saveName = ''; $tick = 0L
    if ($m.save) { $saveName = [string]$m.save.gameName; $tick = [long]$m.save.gameTick }
    $slots = 0
    if ($g.plan -and $g.plan.slots) { $slots = @($g.plan.slots).Count }
    return @{
        Seed      = [int]$m.seed
        StarCount = [int]$m.starCount
        Mult      = [double]$m.resourceMultiplier
        SaveName  = $saveName
        Tick      = $tick
        Slots     = $slots
    }
}

# 存档与 galaxy.json 是否匹配（种子/恒星数；倍率只警告）
function Assert-SaveMatchesGalaxy([string]$savePath, [string]$galPath) {
    $h = Get-SaveHeader $savePath
    $m = Get-GalaxyMeta $galPath
    if ($h.seed -ne $m.Seed) {
        throw ('种子不一致：存档 ' + $h.gameName + ' seed=' + $h.seed +
               '，galaxy.json seed=' + $m.Seed + '。请先 init ' + $h.seed + '，或换一个对应 ' + $m.Seed + ' 的存档。')
    }
    if ($h.starCount -ne $m.StarCount) {
        throw ('恒星数不一致：存档 ' + $h.starCount + '，galaxy.json ' + $m.StarCount)
    }
    if ([math]::Abs([double]$h.resourceMultiplier - [double]$m.Mult) -gt 0.001) {
        Say-Warn ('资源倍率不一致：存档 ' + $h.resourceMultiplier + '，json ' + $m.Mult +
                  '（仅影响 initialAmount 对比，继续融合）')
    }
    Say-Ok ('存档校验：' + $h.gameName + '  seed=' + $h.seed + '  恒星=' + $h.starCount + '  tick=' + $h.gameTick)
}

# ============================ 动作 ============================
function Do-Init {
    Assert-Seed
    Assert-Mult
    $seed = $script:Ctx.Seed
    $gal  = Get-Galaxy $seed
    $dir  = Get-OutDir $seed

    if ((Test-Path $gal) -and (-not $Force)) {
        if ($Command -ne '') { throw ('已存在 ' + $gal + '，加 -Force 覆盖') }
        Say-Warn ('已存在 ' + $gal)
        Say-Warn '覆盖将重新生成模型数据：已融合的中文名/剩余储量会丢失，需要重新融合；已写回的规划也需重跑'
        $ans = Read-Host '确定覆盖？(y/N)'
        if ($ans -notmatch '^[Yy]') { Say-Warn '已取消'; return }
    }

    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Say-Head ('初始生成  seed=' + $seed + '  倍率=' + $script:Ctx.Mult)
    Invoke-Step $ExeSearch @(
        '--headless', '--seed', $seed,
        '--resource-multiplier', ([string]$script:Ctx.Mult),
        '--out', $dir, '--json', $gal
    ) (Get-LogFile 'init')
    Say-Ok ('产出 ' + $gal)
}

function Do-Plan {
    Assert-Seed
    Assert-Seconds
    $seed = $script:Ctx.Seed
    $gal  = Get-Galaxy $seed
    if (-not (Test-Path $gal)) { throw ('缺少 ' + $gal + '，请先做初始生成') }

    $mode = if ($script:Ctx.SkipLns) { '（跳过 LNS）' } else { ' + LNS ' + $script:Ctx.LnsWindow + '×' + $script:Ctx.LnsRounds }
    Say-Head ('规划  seed=' + $seed + '  search ' + $script:Ctx.Seconds + 's' + $mode + ' + apply')

    $log = Get-LogFile 'plan'
    $searchArgs = @('search', $gal, '--seconds', ([string]$script:Ctx.Seconds))
    if ($script:Ctx.Threads -gt 0) { $searchArgs += @('--threads', ([string]$script:Ctx.Threads)) }
    Invoke-Step $ExePlan $searchArgs $log

    if (-not $script:Ctx.SkipLns) {
        Invoke-Step $ExePlan @('lns', $gal, '--window', ([string]$script:Ctx.LnsWindow), '--rounds', ([string]$script:Ctx.LnsRounds)) $log
    }
    Invoke-Step $ExePlan @('apply', $gal) $log
    Say-Ok ('规划已写回 ' + $gal + '（产物另见 ' + $PlanOut + '）')
}

function Do-Merge {
    Assert-Seed
    $seed = $script:Ctx.Seed
    $gal  = Get-Galaxy $seed
    if (-not (Test-Path $gal)) { throw ('缺少 ' + $gal + '，请先做初始生成') }
    if (-not $script:Ctx.Save) { throw '还没有选存档（菜单第 3 项或 -Save）' }

    # -Save 允许只给文件名（在记忆目录里解析）；成功后记忆目录
    if (-not (Test-Path $script:Ctx.Save)) {
        $script:Ctx.Save = Resolve-SavePath $script:Ctx.Save
    } else {
        $script:Ctx.SaveDir = Split-Path -Parent $script:Ctx.Save
    }

    Say-Head ('存档融合  ' + (Split-Path -Leaf $script:Ctx.Save))
    Assert-SaveMatchesGalaxy $script:Ctx.Save $gal
    Invoke-Step $ExeSave @('--merge', $script:Ctx.Save, '--galaxy', $gal) (Get-LogFile 'merge')
    Say-Ok '已更新名字 / 剩余储量 / 已探索 / 工厂 / 戴森球 / 黑雾'
    Say-Warn '浏览器里按 F5 刷新即可（服务无需重启）'
}

function Test-PortListening([int]$p) {
    try {
        return [bool](Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue)
    } catch { return $false }
}

function Read-ServeInfo {
    if (Test-Path $ServeFile) {
        try { return (Get-Content $ServeFile -Raw -Encoding UTF8 | ConvertFrom-Json) } catch { }
    }
    return $null
}

function Write-ServeInfo([int]$procId, [int]$port, [string]$galaxy) {
    try {
        @{ pid = $procId; port = $port; galaxy = $galaxy; startedAt = (Get-Date).ToString('s') } |
            ConvertTo-Json | Set-Content -Path $ServeFile -Encoding UTF8
    } catch { }
}

function Clear-ServeInfo {
    Remove-Item $ServeFile -Force -ErrorAction SilentlyContinue
}

function Do-Serve {
    Assert-Seed
    $gal = Get-Galaxy $script:Ctx.Seed
    if (-not (Test-Path $gal)) { throw ('缺少 ' + $gal + '，请先做初始生成') }
    if (-not (Test-Path $ExeView)) { throw ('找不到 ' + $ExeView) }

    # 端口占用：如果不是 DspView 自己占的，直接报错
    $procs = @(Get-Process DspView -ErrorAction SilentlyContinue)
    $listener = Get-NetTCPConnection -LocalPort $RunPort -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($listener -and $procs.Count -eq 0) {
        $owner = 'unknown'
        try { $owner = (Get-Process -Id $listener.OwningProcess -ErrorAction Stop).ProcessName } catch { }
        throw ('端口 ' + $RunPort + ' 已被 ' + $owner + '（PID ' + $listener.OwningProcess +
               '）占用。可用 -Port 或环境变量 DSP_PORT 换端口。')
    }

    if ($procs.Count -gt 0) {
        $st = Read-ServeInfo
        $cur = if ($st -and $st.galaxy) { [string]$st.galaxy } else { '(未知，可能是旧版本启动)' }
        if ($st -and ($cur -ieq $gal) -and (Test-PortListening $RunPort)) {
            Say-Ok ('服务已在运行并指向当前数据（PID ' + (($procs | ForEach-Object { $_.Id }) -join ',') + '）')
        } else {
            Say-Warn ('DspView 正在运行，但指向：' + $cur)
            Say-Warn ('当前数据：' + $gal)
            $ans = 'y'
            if ($Command -eq '') { $ans = Read-Host '重启服务以切换到当前数据？(Y/n)' }
            if ($ans -notmatch '^[Nn]') {
                $procs | Stop-Process -Force
                Clear-ServeInfo
                Start-Sleep -Milliseconds 500
                $procs = @()
            } else {
                Say-Warn '保持原服务不变'
            }
        }
    }

    if ($procs.Count -eq 0) {
        Say-Head ('启动网页服务  端口 ' + $RunPort)
        $argLine = '--galaxy "' + $gal + '" --serve --port ' + $RunPort
        Start-Process -FilePath $ExeView -ArgumentList $argLine -WorkingDirectory $ViewWork -WindowStyle Hidden

        $ok = $false
        for ($i = 0; $i -lt 20; $i++) {
            Start-Sleep -Milliseconds 500
            if (Test-PortListening $RunPort) { $ok = $true; break }
            if (-not (Get-Process DspView -ErrorAction SilentlyContinue)) { break }
        }
        if (-not $ok) {
            throw ('DspView 启动失败：端口 ' + $RunPort + ' 未监听（检查端口占用或 web 目录）')
        }
        $p = Get-Process DspView -ErrorAction SilentlyContinue | Select-Object -First 1
        Write-ServeInfo ($(if ($p) { $p.Id } else { 0 })) $RunPort $gal
        Say-Ok '服务已启动'
    }

    $url = 'http://localhost:' + $RunPort + '/'
    Say-Ok ('打开 ' + $url)
    Start-Process $url
}

function Do-Stop {
    Say-Head '停止网页服务'
    $proc = @(Get-Process DspView -ErrorAction SilentlyContinue)
    if ($proc.Count -eq 0) { Say-Warn '没有在运行'; Clear-ServeInfo; return }
    # 尽量先正常关闭（等 2 秒），再强杀
    $proc | ForEach-Object { try { $_.CloseMainWindow() | Out-Null } catch { } }
    Start-Sleep -Milliseconds 800
    $left = @(Get-Process DspView -ErrorAction SilentlyContinue)
    if ($left.Count -gt 0) { $left | Stop-Process -Force }
    Clear-ServeInfo
    Say-Ok ('已停止 @(' + (($proc | ForEach-Object { $_.Id }) -join ',') + ')')
}

function Do-Csv {
    if (-not $script:Ctx.Save) { throw '还没有选存档（菜单第 3/8 项或 -Save）' }
    if (-not (Test-Path $script:Ctx.Save)) { $script:Ctx.Save = Resolve-SavePath $script:Ctx.Save }
    $name = [IO.Path]::GetFileNameWithoutExtension($script:Ctx.Save)
    $out = Join-Path $OutRoot ('save-check-' + $name + '.csv')
    Say-Head ('存档解析导出  ' + (Split-Path -Leaf $script:Ctx.Save))
    Invoke-Step $ExeSave @('--save', $script:Ctx.Save, '--out', $out) (Get-LogFile 'csv')
    Say-Ok ('产物在 ' + (Split-Path -Parent $out) + '（save-check-' + $name + '*.csv）')
}

function Get-GalaxyInfo([string]$path) {
    $info = @{ Stars = 0; Planets = 0; SizeKB = 0 }
    if (-not (Test-Path $path)) { return $info }
    $info.SizeKB = [int]((Get-Item $path).Length / 1KB)
    try {
        $g = Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json
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
            try {
                $m = Get-GalaxyMeta $gal
                $sv = if ($m.SaveName) { $m.SaveName + ' tick=' + $m.Tick } else { '（未融合存档）' }
                Write-Host ('  存档         ' + $sv)
                Write-Host ('  规划槽       ' + $m.Slots)
            } catch { }
            $web = Join-Path (Get-OutDir $script:Ctx.Seed) 'web\index.html'
            if (Test-Path $web) { Write-Host ('  web         已生成') } else { Write-Host '  web         未生成' }
        } else {
            Write-Host ('  未生成：' + $gal) -ForegroundColor Yellow
        }
    }

    Say-Head '存档'
    $dir = Get-SaveDir
    Write-Host ('  目录        ' + $(if ($dir) { $dir } else { '（未记忆）' }))
    if ($script:Ctx.Save) { Write-Host ('  当前        ' + $script:Ctx.Save) }

    Say-Head '服务'
    $procs = @(Get-Process DspView -ErrorAction SilentlyContinue)
    if ($procs.Count -gt 0) {
        $st = Read-ServeInfo
        $target = if ($st -and $st.galaxy) { [string]$st.galaxy } else { '(未知)' }
        $listen = if (Test-PortListening $RunPort) { '监听中' } else { '未监听' }
        Write-Host ('  [OK] DspView PID ' + (($procs | ForEach-Object { $_.Id }) -join ',') + '  端口 ' + $RunPort + '  ' + $listen) -ForegroundColor Green
        Write-Host ('  指向        ' + $target)
    } else {
        Write-Host '  [--] DspView 未运行'
    }

    Write-Host ('  输出根      ' + $OutRoot)
    Write-Host ('  日志目录    ' + $LogDir)
}

function Do-All {
    Do-Init
    Do-Plan
    if ($script:Ctx.Save) { Do-Merge } else { Say-Warn '没有指定存档，跳过融合' }
    Do-Serve
}

function Show-LogTail {
    Say-Head '最近日志'
    $files = @(Get-ChildItem $LogDir -Filter *.log -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 5)
    if ($files.Count -eq 0) { Say-Warn '还没有日志'; return }
    Write-Host ('当前：' + $files[0].Name) -ForegroundColor DarkGray
    Get-Content $files[0].FullName -Tail 200
    Write-Host ''
    Write-Host '最近日志文件：' -ForegroundColor DarkGray
    foreach ($f in $files) {
        Write-Host ('  ' + $f.LastWriteTime.ToString('MM-dd HH:mm:ss') + '  ' + $f.Name) -ForegroundColor DarkGray
    }
}

# ============================ 交互输入 ============================
function Ask-Seed {
    $cur = $script:Ctx.Seed
    $s = Read-Host ('种子' + $(if ($cur) { ' [' + $cur + ']' } else { '' }))
    if (-not $s) { $s = $cur }
    if (-not $s) { $s = Read-Host '请输入种子（0 ~ 2147483647）' }
    if ($s -notmatch '^\d+$') { throw ('种子必须是数字：' + $s) }
    $v = 0L
    if (-not [long]::TryParse($s, [ref]$v) -or $v -gt 2147483647) { throw ('种子超出范围（0 ~ 2147483647）：' + $s) }
    $script:Ctx.Seed = $s
}

function Ask-Mult {
    $s = Read-Host ('资源倍率 [' + $script:Ctx.Mult + ']')
    if (-not $s) { return }
    $v = 0.0
    if (-not [double]::TryParse($s, [ref]$v)) { throw ('倍率必须是数字：' + $s) }
    if ($v -le 0 -or $v -gt 100) { throw ('倍率范围应为 0 ~ 100（当前 ' + $v + '）') }
    $script:Ctx.Mult = $v
}

function Ask-Seconds {
    $s = Read-Host ('规划时长秒 [' + $script:Ctx.Seconds + ']')
    if (-not $s) { return }
    $v = 0
    if (-not [int]::TryParse($s, [ref]$v)) { throw ('秒数必须是整数：' + $s) }
    if ($v -lt 30) { throw ('规划秒数至少 30（当前 ' + $v + '）') }
    $script:Ctx.Seconds = $v
}

function Select-Save {
    $dir = Get-SaveDir
    $files = @()
    if ($dir) {
        $files = @(Get-ChildItem $dir -Filter *.dsv -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notlike '_autosave*' -and $_.Name -ne '_lastexit_.dsv' } |
            Sort-Object LastWriteTime -Descending | Select-Object -First 10)
    }

    Write-Host ''
    if (-not $dir) {
        Say-Warn '还没有记忆的存档目录（未设置 DSP_SAVE_DIR，也没有 Documents\Dyson Sphere Program\Save）'
    } elseif ($files.Count -eq 0) {
        Say-Warn ('在 ' + $dir + ' 里没有可用存档（已排除 _autosave* 与 _lastexit_.dsv）')
    } else {
        Write-Host ('存档目录：' + $dir) -ForegroundColor Cyan
        for ($i = 0; $i -lt $files.Count; $i++) {
            $f = $files[$i]
            $mark = if ($i -eq 0) { '   ← 最新' } else { '' }
            Write-Host ('  ' + ($i + 1) + ') ' + $f.Name + '   ' + [math]::Round($f.Length / 1MB, 1) +
                        ' MB   ' + $f.LastWriteTime.ToString('MM-dd HH:mm') + $mark)
        }
        Write-Host '  m) 手动输入（文件名或完整路径）'
        $n = Read-Host ('选择编号 [回车 = 最新 ' + $files[0].Name + ']')
        if (-not $n) { $n = '1' }
        if ($n -match '^[Mm]$') {
            $p = Read-Host '文件名（在当前目录下查找）或完整路径'
            $script:Ctx.Save = Resolve-SavePath $p
        } else {
            $idx = 0
            if (-not [int]::TryParse($n, [ref]$idx) -or $idx -lt 1 -or $idx -gt $files.Count) { throw ('无效编号：' + $n) }
            $script:Ctx.Save = $files[$idx - 1].FullName
            $script:Ctx.SaveDir = $dir
        }
        Say-Ok ('已选 ' + (Split-Path -Leaf $script:Ctx.Save))
        return
    }

    $p = Read-Host '请输入存档文件名或完整路径'
    $script:Ctx.Save = Resolve-SavePath $p
    Say-Ok ('已选 ' + (Split-Path -Leaf $script:Ctx.Save))
}

function Show-Settings {
    while ($true) {
        Clear-Screen
        Write-Host '============ 设置 ============' -ForegroundColor Cyan
        Write-Host ('  1) 资源倍率        ' + $script:Ctx.Mult)
        Write-Host ('  2) 规划秒数        ' + $script:Ctx.Seconds)
        Write-Host ('  3) 规划线程        ' + $(if ($script:Ctx.Threads -gt 0) { $script:Ctx.Threads } else { '0（自动）' }))
        Write-Host ('  4) LNS 窗口        ' + $script:Ctx.LnsWindow)
        Write-Host ('  5) LNS 轮次        ' + $script:Ctx.LnsRounds)
        Write-Host ('  6) 跳过 LNS        ' + $(if ($script:Ctx.SkipLns) { '是' } else { '否' }))
        Write-Host '  0) 返回' -ForegroundColor DarkGray
        $c = Read-Host '选择'
        try {
            switch ($c) {
                '1' { Ask-Mult }
                '2' { Ask-Seconds }
                '3' {
                    $s = Read-Host ('规划线程（0=自动）[' + $script:Ctx.Threads + ']')
                    if ($s) { $v = 0; if (-not [int]::TryParse($s, [ref]$v) -or $v -lt 0 -or $v -gt 256) { throw '线程数应在 0~256' }; $script:Ctx.Threads = $v }
                }
                '4' {
                    $s = Read-Host ('LNS 窗口 [' + $script:Ctx.LnsWindow + ']')
                    if ($s) { $v = 0; if (-not [int]::TryParse($s, [ref]$v) -or $v -lt 0 -or $v -gt 1000) { throw '窗口应 >= 0' }; $script:Ctx.LnsWindow = $v }
                }
                '5' {
                    $s = Read-Host ('LNS 轮次 [' + $script:Ctx.LnsRounds + ']')
                    if ($s) { $v = 0; if (-not [int]::TryParse($s, [ref]$v) -or $v -lt 0 -or $v -gt 1000) { throw '轮次应 >= 0' }; $script:Ctx.LnsRounds = $v }
                }
                '6' { $script:Ctx.SkipLns = -not $script:Ctx.SkipLns }
                '0' { return }
                default { }
            }
            Save-State
        } catch {
            Say-Err $_.Exception.Message
            Start-Sleep -Milliseconds 800
        }
    }
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
        Write-Host (' 当前  seed=' + $seedTxt + '  倍率=' + $script:Ctx.Mult + '  规划=' + $script:Ctx.Seconds +
                    's  线程=' + $(if ($script:Ctx.Threads -gt 0) { $script:Ctx.Threads } else { '自动' })) -ForegroundColor DarkGray
        Write-Host ('       存档=' + $saveTxt + '  服务=' + $svc + '  端口=' + $RunPort) -ForegroundColor DarkGray
        Write-Host '============================================================' -ForegroundColor Cyan
        Write-Host '  1) 初始生成（星系 → galaxy.json）'
        Write-Host '  2) 规划（搜索 + LNS + 写回）'
        Write-Host '  3) 存档融合（选择 .dsv，默认最新）'
        Write-Host '  4) 启动 / 打开网页'
        Write-Host '  5) 一键全流程（1 → 2 → 3 → 4）'
        Write-Host '  6) 查看状态'
        Write-Host '  7) 停止网页服务'
        Write-Host '  8) 存档解析导出 CSV'
        Write-Host '  9) 查看最近日志'
        Write-Host '  s) 设置（倍率 / 秒数 / 线程 / LNS）'
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
                's' { Show-Settings }
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
    # 1) 先校验命令行参数（不合法时不写入状态，避免坏值被持久化）
    if ($Mult -ne 0 -and ($Mult -le 0 -or $Mult -gt 100)) { throw '资源倍率应在 0 ~ 100' }
    if ($Seconds -ne 0 -and $Seconds -lt 30)              { throw '规划秒数至少 30' }
    if ($Threads -lt 0 -or $Threads -gt 256)              { throw '线程数应在 0~256' }
    if ($LnsWindow -lt 0 -or $LnsRounds -lt 0)            { throw 'LNS 窗口/轮次不能为负数' }

    # 2) 校验通过后再覆盖状态
    if ($SeedArg)         { $script:Ctx.Seed = $SeedArg }
    if ($Mult -ne 0)      { $script:Ctx.Mult = $Mult }
    if ($Seconds -ne 0)   { $script:Ctx.Seconds = $Seconds }
    if ($Threads -ne 0)   { $script:Ctx.Threads = $Threads }
    if ($LnsWindow -ne 0) { $script:Ctx.LnsWindow = $LnsWindow }
    if ($LnsRounds -ne 0) { $script:Ctx.LnsRounds = $LnsRounds }
    if ($SkipLns)         { $script:Ctx.SkipLns = $true }
    if ($Save)            { $script:Ctx.Save = $Save }

    # 旧 state 可能只有完整存档路径：用它的目录初始化 SaveDir，文件名输入即可用
    if (-not $script:Ctx.SaveDir -and $script:Ctx.Save) {
        $d = Split-Path -Parent $script:Ctx.Save
        if ($d -and (Test-Path $d)) { $script:Ctx.SaveDir = $d }
    }

    if ($env:DSP_DEBUG) {
        Write-Host ('DEBUG  Command=[' + $Command + '] SeedArg=[' + $SeedArg + '] Save=[' + $Save + '] Ctx.Save=[' + $script:Ctx.Save + '] Ctx.SaveDir=[' + $script:Ctx.SaveDir + '] Ctx.Mult=[' + $script:Ctx.Mult + '] Ctx.Threads=[' + $script:Ctx.Threads + ']') -ForegroundColor Magenta
    }

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
            Write-Host '用法：dsp.ps1 [status|init|plan|merge|serve|stop|csv|all|log] [seed]' -ForegroundColor Yellow
            Write-Host '  -Save <文件名或完整路径>  -Mult <倍率>  -Seconds <秒>' -ForegroundColor Yellow
            Write-Host '  -Threads <N>  -LnsWindow <N>  -LnsRounds <N>  -SkipLns  -Port <端口>  -Force' -ForegroundColor Yellow
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
