# Builds the game assembly, the rules library and the tests.
. "$PSScriptRoot\common.ps1"
Push-Location $Root
try {
    dotnet build DestructionRacing.sln -nologo -v minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
} finally { Pop-Location }
