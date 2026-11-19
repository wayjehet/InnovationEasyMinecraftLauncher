param([switch]$SelfContained, [string]$Rid = "win-x64")
$ErrorActionPreference = "Stop"
$sc = if ($SelfContained) { "true" } else { "false" }
if (Test-Path dist) { Remove-Item dist -Recurse -Force }
dotnet publish src/IEML.App/IEML.App.csproj -c Release -r $Rid --self-contained $sc `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
if (Test-Path native/ieml_cubiomes.dll) { Copy-Item native/ieml_cubiomes.dll dist/ -Force }
Write-Host "完成：dist\InnovationEasyMinecraftLauncher.exe"
