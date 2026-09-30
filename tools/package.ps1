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
# Explicit entry names: Compress-Archive and ZipFile.CreateFromDirectory on Windows PowerShell 5.1 write `labs\world.json`,
# which Linux extracts as one file named that.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
Remove-Item "$root\igtap-lab.igtap" -Force -ErrorAction Ignore
$zip = [IO.Compression.ZipFile]::Open("$root\igtap-lab.igtap", 'Create')
try {
    foreach ($f in Get-ChildItem $stage -Recurse -File) {
        $name = $f.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.FullName, $name)
    }
} finally { $zip.Dispose() }
