[CmdletBinding()]
param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')][string]$Version = '2.1.1',
    [ValidateSet('win-x64','win-arm64','linux-x64','osx-arm64','osx-x64')][string]$Runtime = 'win-x64',
    [string]$OutputDirectory = 'publish',
    [switch]$SkipRootCopy
)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskPublish = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
} else { [System.IO.Path]::GetFullPath((Join-Path $taskRoot $OutputDirectory)) }
& dotnet publish (Join-Path $taskRoot 'StockViewer.App/StockViewer.App.csproj') -c Release -r $Runtime --self-contained true `
    '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' `
    '-p:EnableCompressionInSingleFile=true' '-p:PublishTrimmed=false' `
    '-p:DebugType=embedded' '-p:CopyOutputSymbolsToPublishDirectory=false' "-p:Version=$Version" -o $taskPublish --nologo
if ($LASTEXITCODE -ne 0) { throw 'Avalonia standalone publish failed.' }
$taskName = if ($Runtime.StartsWith('win-')) { 'StockViewer.exe' } else { 'StockViewer' }
$taskExecutable = Join-Path $taskPublish $taskName
if (-not (Test-Path -LiteralPath $taskExecutable -PathType Leaf)) { throw 'The standalone executable was not produced.' }
if (-not $SkipRootCopy -and $Runtime.StartsWith('win-')) {
    $taskDestination = Join-Path $taskRoot $taskName
    if ($taskExecutable -ne $taskDestination) { Copy-Item -LiteralPath $taskExecutable -Destination $taskDestination -Force }
}
Write-Output $taskExecutable
