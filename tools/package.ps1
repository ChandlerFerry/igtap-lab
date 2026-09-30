# Builds igtap-lab.igtap, the zip Recharge installs (mod.json at its root).
param([string]$ModApiDir, [string]$ManagedDir)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$build = @('build', "$root\IgtapLab.csproj", '-c', 'Release', '-o', "$root\bin\package")
if ($ModApiDir) { $build += "-p:ModApiDir=$ModApiDir" }
if ($ManagedDir) { $build += "-p:ManagedDir=$ManagedDir" }
dotnet @build
if ($LASTEXITCODE) { exit $LASTEXITCODE }
$stage = "$root\obj\package"
Remove-Item $stage -Recurse -Force -ErrorAction Ignore
New-Item "$stage\labs" -ItemType Directory | Out-Null
Copy-Item "$root\mod.json", "$root\bin\package\IgtapLab.dll" $stage
Copy-Item "$root\labs\world.json" "$stage\labs"
Compress-Archive "$stage\*" "$root\igtap-lab.zip" -Force
Move-Item "$root\igtap-lab.zip" "$root\igtap-lab.igtap" -Force
