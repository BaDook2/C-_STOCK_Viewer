$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskProject = Join-Path $taskRoot 'StockViewer.App\StockViewer.App.csproj'
$taskPublish = Join-Path $taskRoot 'publish'
& dotnet publish $taskProject -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:EnableCompressionInSingleFile=true' -o $taskPublish --nologo
if ($LASTEXITCODE -ne 0) { throw 'StockViewer publish failed.' }
Copy-Item -LiteralPath (Join-Path $taskPublish 'StockViewer.exe') -Destination (Join-Path $taskRoot 'StockViewer.exe') -Force
Write-Output (Join-Path $taskRoot 'StockViewer.exe')
