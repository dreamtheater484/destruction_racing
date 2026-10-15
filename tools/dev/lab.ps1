# Runs the scripted damage lab (rams, wreck, wheel loss, scrape, roll), saves its shots and a contact sheet.
# Example: .\tools\dev\lab.ps1 -Model brougham
param(
    [string]$Model = 'kitsune',
    [string]$Out = 'out/lab',
    [switch]$NoBuild
)
. "$PSScriptRoot\common.ps1"
if (-not $NoBuild) {
    $build = & "$PSScriptRoot\build.ps1" 2>&1
    if ($LASTEXITCODE -ne 0) { $build | Select-String -Pattern 'error' | Select-Object -First 20; exit $LASTEXITCODE }
}
& $GodotConsole --headless --path $Root --import 2>&1 | Out-Null
$outDir = Join-Path $Root $Out
$expected = 14
$userArgs = @('--path', "`"$Root`"", '--resolution', '1920x1080', '--', '--scene=res://scenes/test/DamageLab.tscn',
              "--shots=$Out", "--model=$Model")
$log = Join-Path $Root 'out\lab_run.log'
# A window that Windows stops drawing (minimised, or covered and throttled) saves fewer shots: try once more.
for ($attempt = 1; $attempt -le 2; $attempt++) {
    Remove-Item (Join-Path $outDir "${Model}_*.png") -ErrorAction SilentlyContinue
    $p = Start-Process -FilePath $GodotConsole -ArgumentList $userArgs -RedirectStandardOutput $log `
        -RedirectStandardError (Join-Path $Root 'out\lab_err.log') -PassThru -WindowStyle Normal
    if (-not $p.WaitForExit(300000)) { Stop-Process -Id $p.Id -Force; "lab: timed out" }
    $shots = @(Get-ChildItem (Join-Path $outDir "${Model}_*.png") -ErrorAction SilentlyContinue).Count
    if ($shots -ge $expected) { break }
    "lab: only $shots of $expected shots were drawn (was the window minimised or covered?)" + $(if ($attempt -lt 2) { ' - retrying' } else { '' })
}
Get-Content $log | Select-String -Pattern 'DamageLab\]|ERROR|SCRIPT ERROR|Exception' | ForEach-Object { $_.Line }
python "$PSScriptRoot\contact_sheet.py" (Join-Path $outDir "sheet_$Model.png") (Join-Path $outDir "${Model}_*.png") --cols 4
