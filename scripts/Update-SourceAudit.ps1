[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $KeysharpRepo,

    [switch] $SkipKeysharpBuild
)

$ErrorActionPreference = 'Stop'

$docsRepo = Split-Path -Parent $PSScriptRoot
$keysharpRepoPath = (Resolve-Path -LiteralPath $KeysharpRepo).Path
$keysharpProject = Join-Path (Join-Path $keysharpRepoPath 'Keysharp.Core') 'Keysharp.Core.csproj'

if (-not (Test-Path -LiteralPath $keysharpProject -PathType Leaf)) {
    throw "Keysharp.Core.csproj was not found below '$keysharpRepoPath'."
}

if (-not $SkipKeysharpBuild) {
    & dotnet build $keysharpProject --configuration Debug --nologo

    if ($LASTEXITCODE -ne 0) {
        throw "The Keysharp.Core build failed with exit code $LASTEXITCODE."
    }
}

$platform = if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    'windows'
}
elseif ($IsMacOS) {
    'macos'
}
else {
    'linux'
}

$targetFramework = if ($platform -eq 'windows') { 'net10.0-windows' } else { 'net10.0' }
$assemblyPath = Join-Path $keysharpRepoPath "bin/Debug/$targetFramework/Keysharp.Core.dll"

if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw "Keysharp.Core.dll was not found at '$assemblyPath'."
}

$auditDirectory = Join-Path $docsRepo 'audits'
$toolProject = Join-Path $docsRepo 'tools/KeysharpApiCatalog/KeysharpApiCatalog.csproj'

New-Item -ItemType Directory -Path $auditDirectory -Force | Out-Null

foreach ($scope in @('global', 'class-members', 'module-exports')) {
    $filePrefix = switch ($scope) {
        'global' { 'global-api' }
        'class-members' { 'class-members' }
        default { 'module-exports' }
    }
    $jsonOutput = Join-Path $auditDirectory "$filePrefix.$platform.json"
    $markdownOutput = Join-Path $auditDirectory "$filePrefix.$platform.md"

    & dotnet run --project $toolProject --configuration Release -- `
        --scope $scope `
        --assembly $assemblyPath `
        --source-repo $keysharpRepoPath `
        --docs-repo $docsRepo `
        --docs (Join-Path $docsRepo 'docs') `
        --json $jsonOutput `
        --markdown $markdownOutput

    if ($LASTEXITCODE -ne 0) {
        throw "The '$scope' source audit failed with exit code $LASTEXITCODE."
    }
}
