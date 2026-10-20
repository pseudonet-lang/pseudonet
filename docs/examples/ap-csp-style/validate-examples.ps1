# Compiles every <id>.pseudo of this folder with the REAL compiler and the REAL model (fresh answer, empty LLM cache,
# empty NuGet cache), runs it and compares the output with <id>.expected.
# The programs are written in the style of the AP CSP reference-sheet pseudocode (written from memory, NOT copied
# from the official sheet). Requires Ollama with qwen2.5-coder:7b, and the packages built in this repository.
param([string[]]$Only)
$ErrorActionPreference = 'Continue'
$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here '..\..\..')).Path
$pack = Get-ChildItem (Join-Path $repo 'PseudoNet.DotnetNew\artifacts') -Filter 'PseudoNet.Templates.*.nupkg' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$feedDir = Join-Path $repo 'PseudoNet.Build\artifacts\feed'
$buildPkg = Get-ChildItem $feedDir -Filter 'PseudoNet.Build.*.nupkg' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$buildVersion = [regex]::Match($buildPkg.Name, 'PseudoNet\.Build\.(.+)\.nupkg').Groups[1].Value
Write-Host "Compiler package under test: PseudoNet.Build $buildVersion"
$work = Join-Path $env:TEMP 'pseudonet-apcsp'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory $work | Out-Null
$env:NUGET_PACKAGES = Join-Path $work '_empty-nuget-cache'
$hive = Join-Path $work '_hive'
$env:PSEUDONET_LANG = 'en'
dotnet new install $pack.FullName --debug:custom-hive $hive | Out-Null
$files = Get-ChildItem $here -Filter '*.pseudo' | Sort-Object Name
if ($Only) { $wanted = @($Only | ForEach-Object { $_ -split ',' }); $files = $files | Where-Object { $wanted -contains $_.BaseName } }
$results = @()
foreach ($f in $files) {
    $id = $f.BaseName
    $expected = ((Get-Content (Join-Path $here "$id.expected") -Raw -Encoding UTF8) -replace "`r", '').Trim()
    $dir = Join-Path $work $id; New-Item -ItemType Directory $dir | Out-Null
    Push-Location $dir
    dotnet new pseudocode -n App --debug:custom-hive $hive | Out-Null
    Pop-Location
    $proj = Join-Path $dir 'App'
    Copy-Item $f.FullName (Join-Path $proj 'Program.pseudo') -Force
    $csprojPath = Join-Path $proj 'App.csproj'
    $cs = Get-Content $csprojPath -Raw -Encoding UTF8
    $cs = [regex]::Replace($cs, 'PseudoNet\.Build" Version="[^"]+"', ('PseudoNet.Build" Version="' + $buildVersion + '"'))
    $cs = $cs.Replace('$(MSBuildProjectDirectory)/nuget-feed', $feedDir)
    [IO.File]::WriteAllText($csprojPath, $cs, (New-Object System.Text.UTF8Encoding($false)))
    $cache = Join-Path $work "_llm-cache-$id"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $log = dotnet build (Join-Path $proj 'App.csproj') --nologo "-p:PseudoCacheDir=$cache" 2>&1 | Out-String
    $status = 'FAIL'; $why = ''
    if ($LASTEXITCODE -ne 0) {
        $why = 'build failed: ' + (($log -split "`r?`n" | Where-Object { $_ -match ' error | warning PSEUDO' } | Select-Object -First 2 | ForEach-Object { ($_ -replace '^.*?(error|warning)', '$1') }) -join ' | ')
    } else {
        $dll = Get-ChildItem (Join-Path $proj 'bin') -Recurse -Filter 'App.dll' | Select-Object -First 1
        $out = ((& dotnet $dll.FullName 2>&1 | Out-String) -replace "`r", '').Trim()
        if ($out -eq $expected) { $status = 'PASS' } else { $why = "expected [" + ($expected -replace "`n", ' / ') + "] but got [" + ($out -replace "`n", ' / ') + "]" }
    }
    Write-Host ('{0,-5} {1}  ({2:N0}s) {3}' -f $status, $id, $sw.Elapsed.TotalSeconds, $why)
    $g = Get-ChildItem (Join-Path $proj 'obj') -Recurse -Filter 'Program.g.cs' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($g) { Copy-Item $g.FullName (Join-Path $here "$id.generated.cs.txt") -Force }
    $results += [pscustomobject]@{ Id = $id; Status = $status }
}
Write-Host ''
Write-Host ("{0}/{1} programs give the expected output" -f @($results | Where-Object Status -eq 'PASS').Count, $results.Count)
