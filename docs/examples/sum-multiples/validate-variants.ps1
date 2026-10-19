# Compiles every *.pseudo variant of this folder with the REAL compiler and the REAL model (fresh answer each time,
# empty LLM cache, empty NuGet cache), runs the program and compares its output with the Python reference.
# Requires: Ollama running with qwen2.5-coder:7b, and PseudoNet.DotnetNew\artifacts\PseudoNet.Templates.*.nupkg.
param([string[]]$Only)
$ErrorActionPreference = 'Continue'
$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here '..\..\..')).Path
$pack = Get-ChildItem (Join-Path $repo 'PseudoNet.DotnetNew\artifacts') -Filter 'PseudoNet.Templates.*.nupkg' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$feedDir = Join-Path $repo 'PseudoNet.Build\artifacts\feed'
$buildPkg = Get-ChildItem $feedDir -Filter 'PseudoNet.Build.*.nupkg' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$buildVersion = [regex]::Match($buildPkg.Name, 'PseudoNet\.Build\.(.+)\.nupkg').Groups[1].Value
Write-Host "Compiler package under test: PseudoNet.Build $buildVersion"
$expected = (& python (Join-Path $here 'reference.py') | Out-String).Trim()
$work = Join-Path $env:TEMP 'pseudonet-variants'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory $work | Out-Null
$env:NUGET_PACKAGES = Join-Path $work '_empty-nuget-cache'
$hive = Join-Path $work '_hive'
$env:PSEUDONET_LANG = 'en'
dotnet new install $pack.FullName --debug:custom-hive $hive | Out-Null
Write-Host "Python reference output: $expected"
$files = Get-ChildItem $here -Filter '*.pseudo' | Sort-Object Name
if ($Only) { $wanted = @($Only | ForEach-Object { $_ -split ',' }); $files = $files | Where-Object { $wanted -contains $_.BaseName } }
$results = @()
foreach ($f in $files) {
    $id = $f.BaseName
    $dir = Join-Path $work $id; New-Item -ItemType Directory $dir | Out-Null
    Push-Location $dir
    dotnet new pseudocode -n App --debug:custom-hive $hive | Out-Null
    Pop-Location
    $proj = Join-Path $dir 'App'
    Copy-Item $f.FullName (Join-Path $proj 'Program.pseudo') -Force
    # Use the compiler package you just built (PseudoNet.Build\artifacts\feed), not the one the template embeds.
    $csprojPath = Join-Path $proj 'App.csproj'
    $cs = Get-Content $csprojPath -Raw -Encoding UTF8
    $cs = [regex]::Replace($cs, 'PseudoNet\.Build" Version="[^"]+"', ('PseudoNet.Build" Version="' + $buildVersion + '"'))
    $cs = $cs.Replace('$(MSBuildProjectDirectory)/nuget-feed', $feedDir)
    [IO.File]::WriteAllText($csprojPath, $cs, (New-Object System.Text.UTF8Encoding($false)))
    $cache = Join-Path $work "_llm-cache-$id"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $log = dotnet build (Join-Path $proj 'App.csproj') --nologo "-p:PseudoCacheDir=$cache" 2>&1 | Out-String
    $buildExit = $LASTEXITCODE
    $out = ''
    $status = 'FAIL'; $why = ''
    if ($buildExit -ne 0) {
        $why = 'build failed: ' + (($log -split "`r?`n" | Where-Object { $_ -match ' error | warning PSEUDO' } | Select-Object -First 2) -join ' | ')
    } else {
        $dll = Get-ChildItem (Join-Path $proj 'bin') -Recurse -Filter 'App.dll' | Select-Object -First 1
        $out = (& dotnet $dll.FullName 2>&1 | Out-String).Trim()
        if ($out -eq $expected) { $status = 'PASS' } else { $why = "output was '$out'" }
        if ($log -match 'warning PSEUDO007') { $why += ' (PSEUDO007: the model needed corrections)' }
    }
    Write-Host ('{0,-5} {1}  ({2:N0}s) {3}' -f $status, $id, $sw.Elapsed.TotalSeconds, $why)
    $g = Get-ChildItem (Join-Path $proj 'obj') -Recurse -Filter 'Program.g.cs' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($g) { Copy-Item $g.FullName (Join-Path $here "$id.generated.cs.txt") -Force }
    $results += [pscustomobject]@{ Id = $id; Status = $status }
}
Write-Host ''
Write-Host ("{0}/{1} variants produce {2}" -f @($results | Where-Object Status -eq 'PASS').Count, $results.Count, $expected)
