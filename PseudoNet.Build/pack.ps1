# Construit le paquet NuGet PseudoNet.Build dans artifacts\feed\ (flux NuGet local utilisable tel quel).
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$compiler = Join-Path $here '..\PseudoNet.Compiler\PseudoNet.Compiler\PseudoNet.Compiler.fsproj'
$out = Join-Path $here 'artifacts\compiler'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish $compiler -c Release -o $out --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'publish a échoué' }
dotnet pack (Join-Path $here 'PseudoNet.Build.csproj') -c Release -o (Join-Path $here 'artifacts\feed') --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'pack a échoué' }
Get-ChildItem (Join-Path $here 'artifacts\feed') | Select-Object Name, Length
