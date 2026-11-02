[CmdletBinding()]
param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$Version = '1.1.0',
    [string]$OutputDirectory = 'publish',
    [switch]$SkipRootCopy
)

$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskProject = Join-Path $taskRoot 'StockViewer.App\StockViewer.App.csproj'
$taskPublish = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $taskRoot $OutputDirectory))
}

& dotnet publish $taskProject -c Release -r win-x64 --self-contained true `
    '-p:PublishSingleFile=true' `
    '-p:IncludeNativeLibrariesForSelfExtract=true' `
    '-p:EnableCompressionInSingleFile=true' `
    '-p:PublishTrimmed=false' `
    '-p:DebugType=embedded' `
    '-p:CopyOutputSymbolsToPublishDirectory=false' `
    "-p:Version=$Version" `
    -o $taskPublish --nologo
if ($LASTEXITCODE -ne 0) { throw 'StockViewer publish failed.' }

$taskExecutable = Join-Path $taskPublish 'StockViewer.exe'
if (-not (Test-Path -LiteralPath $taskExecutable -PathType Leaf)) { throw 'StockViewer.exe was not produced.' }
if (-not $SkipRootCopy) {
    $taskRootExecutable = Join-Path $taskRoot 'StockViewer.exe'
    if ($taskExecutable -ne $taskRootExecutable) {
        Copy-Item -LiteralPath $taskExecutable -Destination $taskRootExecutable -Force
    }
}
Write-Output $taskExecutable
