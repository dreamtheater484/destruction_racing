# Clay preview sheet of a generated car: .\preview.ps1 kitsune [voxel]
param([Parameter(Mandatory = $true)][string]$Car, [string]$Voxel = '0.012', [string]$Mode = 'studio')
$b = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"
$o = Join-Path $PSScriptRoot '..\..\out\cars'
New-Item -ItemType Directory -Force $o | Out-Null
$o = (Resolve-Path $o).Path
& $b -b --factory-startup -P (Join-Path $PSScriptRoot 'blender_preview.py') -- "car:${Car}:$Voxel" "$o\$Car" $Mode 2>&1 | Select-String -Pattern 'PREVIEW|Error|Traceback|line \d+' | Select-Object -First 12
& ffmpeg -y -loglevel error -i "$o\${Car}_q34f.png" -i "$o\${Car}_side.png" -i "$o\${Car}_q34r.png" -i "$o\${Car}_front.png" -filter_complex "[0][1]hstack[a];[2][3]hstack[b];[a][b]vstack,scale=1920:-1" "$o\${Car}_sheet.png"
"$o\${Car}_sheet.png"
