# Runs the rules unit tests.
. "$PSScriptRoot\common.ps1"
Push-Location $Root
try {
    dotnet test tests/Rules.Tests/Rules.Tests.csproj -nologo
    exit $LASTEXITCODE
} finally { Pop-Location }
