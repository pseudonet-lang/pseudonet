# Installs the packed template pack into a THROWAWAY template hive (your real "dotnet new" is untouched), then for
# every template runs exactly what a user would: dotnet new <short> -n MyApp ; dotnet build, on a "clean machine"
# (EMPTY global NuGet cache). Samples are built with Ollama made UNREACHABLE (they ship the LLM answer) and the
# console ones are run and compared with the catalog's sample.json.
$ErrorActionPreference = 'Continue'  # native stderr must not abort (PS 5.1); failures are checked explicitly
$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here '..')).Path
$pack = Get-ChildItem (Join-Path $here 'artifacts') -Filter 'PseudoNet.Templates.*.nupkg' | Select-Object -First 1
if (-not $pack) { throw 'No pack - run build-dotnet-templates.ps1' }
$work = Join-Path $env:TEMP 'pseudonet-dotnetnew-check'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory $work | Out-Null
$hive = Join-Path $work '_hive'
$env:NUGET_PACKAGES = Join-Path $work '_empty-nuget-cache'
$env:PSEUDONET_LANG = 'en'
$realOllama = $env:PSEUDONET_OLLAMA_URL

& dotnet new install $pack.FullName "--debug:custom-hive" $hive | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'dotnet new install failed' }

$catalog = @{}
Get-ChildItem (Join-Path $repo 'PseudoNet.Samples') -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'sample.json') } | ForEach-Object {
    $catalog[$_.Name] = Get-Content (Join-Path $_.FullName 'sample.json') -Raw -Encoding UTF8 | ConvertFrom-Json
}
$shorts = @('pseudocode', 'pseudrawcode', 'pseudo-snake', 'pseudo-tetris', 'pseudo-bounce', 'pseudo-primes', 'pseudo-bubble-sort', 'pseudo-guess-number')
$failures = 0
foreach ($s in $shorts) {
    try {
        $dir = Join-Path $work $s
        New-Item -ItemType Directory $dir | Out-Null
        Push-Location $dir
        $created = & dotnet new $s -n MyApp "--debug:custom-hive" $hive 2>&1 | Out-String
        $createExit = $LASTEXITCODE
        Pop-Location
        if ($createExit -ne 0) { throw "dotnet new $s failed: $created" }
        $proj = Join-Path $dir 'MyApp'
        foreach ($f in 'MyApp.csproj', 'Program.pseudo', ('nuget-feed\PseudoNet.Build.*.nupkg')) {
            if (-not (Test-Path (Join-Path $proj $f))) { throw "missing $f in the generated project" }
        }
        if ((Get-Content (Join-Path $proj 'MyApp.csproj') -Raw) -match 'PseudoApp|safeprojectname') { throw 'unreplaced template token in the csproj' }

        $isSample = $s -like 'pseudo-*'
        if ($isSample) { $env:PSEUDONET_OLLAMA_URL = 'http://localhost:1' }
        $log = & dotnet build (Join-Path $proj 'MyApp.csproj') --nologo 2>&1 | Out-String
        $exit = $LASTEXITCODE
        $env:PSEUDONET_OLLAMA_URL = $realOllama
        if ($exit -ne 0) { throw ('build failed: ' + (($log -split "`r?`n" | Where-Object { $_ -match ' error ' } | Select-Object -First 2) -join ' | ')) }
        if ($log -match 'warning PSEUDO007') { throw 'PSEUDO007 (structural problems in the model answer)' }
        if ($isSample -and $log -notmatch 'LLM cache: reused') { throw 'the sample did not use its shipped LLM answer' }

        $note = ''
        $id = $s -replace '^pseudo-', ''; if ($id -eq 'guess-number') { $id = 'guess-the-number' }
        if ($isSample -and $catalog.ContainsKey($id) -and $catalog[$id].kind -eq 'console') {
            $meta = $catalog[$id]
            $dll = Get-ChildItem (Join-Path $proj 'bin') -Recurse -Filter 'MyApp.dll' | Select-Object -First 1
            $stdin = ((@($meta.input) | Where-Object { $null -ne $_ }) -join "`n") + "`n"
            # random samples (secret number) can legitimately miss a line once in a while: up to 3 attempts
            $bad = $null
            foreach ($attempt in 1..3) {
                $inFile = Join-Path $env:TEMP 'pseudonet-stdin.txt'
                [IO.File]::WriteAllText($inFile, $stdin, (New-Object System.Text.UTF8Encoding($false)))   # redirect from a file: PS 5.1 pipes add a BOM that breaks the first Parse
                $out = & cmd /c ('dotnet "' + $dll.FullName + '" < "' + $inFile + '" 2>&1') | Out-String
                $bad = $null
                foreach ($rx in @($meta.expectedOutput)) { if ($rx -and ($out -notmatch $rx)) { $bad = $rx } }
                if (-not $bad) { break }
            }
            if ($bad) { throw "output does not match /$bad/" }
            $note = ', output OK'
        }
        Write-Host ("PASS  dotnet new {0}  (clean NuGet cache{1})" -f $s, $note)
    }
    catch { $failures++; Write-Host ("FAIL  {0}: {1}" -f $s, $_.Exception.Message); try { Pop-Location } catch {} }
}
Write-Host ''
Write-Host ("{0} template(s): {1} passed, {2} failed" -f $shorts.Count, ($shorts.Count - $failures), $failures)
if ($failures -gt 0) { exit 1 }
