# Builds the NuGet package, then the VSIX (needs Visual Studio with the "Visual Studio extension development" workload).
#   -SkipPack   reuse the package already in PseudoNet.Build\artifacts\feed
param([switch]$SkipPack)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $SkipPack) { & (Join-Path $repo 'PseudoNet.Build\pack.ps1') }

# --- stage + zip every project template (PseudoCode, PseudrawCode, Samples\*) ---------------------------------
# Each template ships its own copy of the PseudoNet.Build package in nuget-feed\ (the NuGet template wizard does
# not work with SDK-style projects). The .vstemplate sits at the root of the zip.
$nupkg = Get-ChildItem (Join-Path $repo 'PseudoNet.Build\artifacts\feed') -Filter 'PseudoNet.Build.*.nupkg' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $nupkg) { throw 'No PseudoNet.Build package found - run PseudoNet.Build\pack.ps1.' }
$templates = Join-Path $repo 'PseudoNet.Templates'
$stage = Join-Path $PSScriptRoot 'obj\stage'
$zips  = Join-Path $PSScriptRoot 'obj\templates'
foreach ($d in $stage, $zips) { if (Test-Path $d) { Remove-Item $d -Recurse -Force }; New-Item -ItemType Directory $d | Out-Null }

$dirs = @(Get-ChildItem $templates -Directory | Where-Object { $_.Name -ne 'Samples' })
$samplesRoot = Join-Path $templates 'Samples'
if (Test-Path $samplesRoot) { $dirs += @(Get-ChildItem $samplesRoot -Directory) }

Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($d in $dirs) {
    $vst = Get-ChildItem $d.FullName -Filter '*.vstemplate' | Select-Object -First 1
    if (-not $vst) { continue }
    $target = Join-Path $stage $d.Name
    Copy-Item $d.FullName $target -Recurse
    New-Item -ItemType Directory (Join-Path $target 'nuget-feed') -Force | Out-Null
    Copy-Item $nupkg.FullName (Join-Path $target 'nuget-feed')
    [IO.Compression.ZipFile]::CreateFromDirectory($target, (Join-Path $zips "$($d.Name).zip"), [IO.Compression.CompressionLevel]::Optimal, $false)
}
Write-Host ("{0} template(s) staged with {1}" -f @(Get-ChildItem $zips -Filter *.zip).Count, $nupkg.Name)

# --- build the VSIX ----------------------------------------------------------------------------------------------
$vs = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.VisualStudio.Workload.VisualStudioExtension -property installationPath
if (-not $vs) { throw "Visual Studio with the 'Visual Studio extension development' workload not found" }
& "$vs\MSBuild\Current\Bin\MSBuild.exe" (Join-Path $PSScriptRoot 'PseudoNet.Vsix.csproj') /t:Rebuild /p:Configuration=Release /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'MSBuild failed' }
Get-ChildItem (Join-Path $PSScriptRoot 'bin\Release\*.vsix') | Select-Object FullName, Length
