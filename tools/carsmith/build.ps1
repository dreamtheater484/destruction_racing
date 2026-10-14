# Build a generated car: .\build.ps1 kitsune [-Voxel 0.012] [-Ratio 0.16] [-Views q34f,side,q34r,front,top,close]
# Writes assets/cars/<car>/<car>.glb and preview renders + out/cars/<car>_build.png (contact sheet).
param([Parameter(Mandatory = $true)][string]$Car, [string]$Voxel = '0.012', [string]$Ratio = '0.16',
      [string]$Views = 'q34f,side,q34r,front,top,close', [switch]$NoGlb)
$b = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$o = Join-Path $root 'out\cars'
New-Item -ItemType Directory -Force $o | Out-Null
$glb = if ($NoGlb) { '' } else { Join-Path $root "assets\cars\$Car\$Car.glb" }
$a = @('-b', '--factory-startup', '-P', (Join-Path $PSScriptRoot 'blender_build.py'), '--', $Car, '--voxel', $Voxel,
       '--ratio', $Ratio, '--preview', "$o\$Car", '--views', $Views)
if ($glb) { $a += @('--glb', $glb) }
& $b @a 2>&1 | Select-String -Pattern 'cut |detail |split |generated|finished|nodes|GLB|BUILD|Error|Traceback|line \d+|empty' | ForEach-Object { $_.Line }
$v = $Views.Split(',')
if ($v.Count -ge 4) {
  & ffmpeg -y -loglevel error -i "$o\${Car}_$($v[0]).png" -i "$o\${Car}_$($v[1]).png" -i "$o\${Car}_$($v[2]).png" -i "$o\${Car}_$($v[3]).png" -filter_complex "[0][1]hstack[a];[2][3]hstack[b];[a][b]vstack,scale=1920:-1" "$o\${Car}_build.png"
  "$o\${Car}_build.png"
}
