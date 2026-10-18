<#
.SYNOPSIS
  Builds every sample of the catalog the way a user would (project template + PseudoNet.Build package,
  empty NuGet cache) and checks the result. Samples double as regression tests for the compiler/prompt.

.DESCRIPTION
  For each folder <id>\ containing Program.pseudo + sample.json:
    1. instantiates the matching project template (console -> PseudoCode, window -> PseudrawCode);
    2. builds it (this calls the compiler, i.e. the LLM unless the answer is cached);
    3. checks the generated C#: no "[unknown", plus the sample's mustMatch / mustNotMatch patterns;
    4. console samples: runs the program with sample.json "input" on stdin and checks "expectedOutput".
       window samples: with -Run, starts the program for a few seconds and fails if it crashes.

  sample.json:
    { "title": "...", "kind": "console|window", "language": "en",
      "input": ["line", ...],                       (console, optional)
      "expectedOutput": ["regex", ...],             (console, optional; every regex must match the output)
      "mustMatch":    [ { "pattern": "regex", "min": 1 } ],   (generated C#)
      "mustNotMatch": [ "regex", ... ] }

  Requires: pack.ps1 already run (PseudoNet.Build\artifacts\feed), Ollama running unless answers are cached.

.EXAMPLE
  .\verify-samples.ps1                      # everything
  .\verify-samples.ps1 -Sample snake,bubble-sort
  .\verify-samples.ps1 -NoCache             # force a fresh LLM answer for each sample
  .\verify-samples.ps1 -Run                 # also smoke-run window samples (opens windows briefly)
#>
param(
    [string[]]$Sample,
    [switch]$NoCache,
    [switch]$Run,
    [int]$RunSeconds = 4
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here '..')).Path
$feedDir = Join-Path $repo 'PseudoNet.Build\artifacts\feed'
$nupkg = Get-ChildItem $feedDir -Filter 'PseudoNet.Build.*.nupkg' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $nupkg) { throw "No PseudoNet.Build package in $feedDir - run PseudoNet.Build\pack.ps1 first." }

$work = Join-Path $env:TEMP 'pseudonet-samples-check'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory $work | Out-Null
$env:NUGET_PACKAGES = Join-Path $work '_empty-nuget-cache'      # a clean machine
$env:PSEUDONET_LANG = 'en'                                      # parseable, stable messages
$cacheFlag = @()
if ($NoCache) { $cacheFlag = @("-p:PseudoCacheDir=$(Join-Path $work '_fresh-llm-cache')") }

$folders = Get-ChildItem $here -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'sample.json') }
if ($Sample) {
    $wanted = @($Sample | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })   # -File passes "a,b" as ONE string
    $folders = $folders | Where-Object { $wanted -contains $_.Name }
}
if (-not $folders) { throw 'No sample selected.' }

function Test-Sample($dir) {
    $problems = New-Object System.Collections.Generic.List[string]
    $meta = Get-Content (Join-Path $dir.FullName 'sample.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $template = 'PseudoCode'; if ($meta.kind -eq 'window') { $template = 'PseudrawCode' }
    $tpl = Join-Path $repo "PseudoNet.Templates\$template"
    $name = 'S' + ($dir.Name -replace '[^A-Za-z0-9]', '')
    $proj = Join-Path $work "$name\$name"
    New-Item -ItemType Directory (Join-Path $proj 'nuget-feed') -Force | Out-Null

    $csproj = (Get-Content (Join-Path $tpl "$template.csproj") -Raw -Encoding UTF8).Replace('$safeprojectname$', $name)
    $csproj = $csproj.Replace('PseudoNet.Build" Version="' + ([regex]::Match($csproj, 'PseudoNet.Build" Version="([^"]+)"').Groups[1].Value), 'PseudoNet.Build" Version="' + ($nupkg.BaseName -replace '^PseudoNet\.Build\.', ''))
    Set-Content (Join-Path $proj "$name.csproj") $csproj -Encoding UTF8
    Copy-Item (Join-Path $dir.FullName 'Program.pseudo') $proj
    Copy-Item $nupkg.FullName (Join-Path $proj 'nuget-feed')

    # --- build (calls the compiler) ---
    $log = & dotnet build (Join-Path $proj "$name.csproj") --nologo @cacheFlag 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        $errs = ($log -split "`r?`n" | Where-Object { $_ -match ' error ' } | Select-Object -First 3) -join ' | '
        $problems.Add("build failed: $errs"); return $problems
    }

    # --- generated C# ---
    $gen = Get-ChildItem (Join-Path $proj 'obj') -Recurse -Filter 'Program.g.cs' | Select-Object -First 1
    if (-not $gen) { $problems.Add('generated C# not found'); return $problems }
    $cs = Get-Content $gen.FullName -Raw -Encoding UTF8
    if ($cs -match '\[unknown') { $problems.Add('generated C# contains an "[unknown ...]" placeholder (construct not understood)') }
    if ($cs -match '\bconcat\b') { $problems.Add('generated C# contains an invented operator "concat"') }
    foreach ($m in @($meta.mustMatch)) {
        if (-not $m) { continue }
        $min = 1; if ($null -ne $m.min) { $min = [int]$m.min }
        $count = ([regex]::Matches($cs, $m.pattern)).Count
        if ($count -lt $min) { $problems.Add("C# check: /$($m.pattern)/ matched $count time(s), expected at least $min") }
    }
    foreach ($p in @($meta.mustNotMatch)) {
        if ($p -and ($cs -match $p)) { $problems.Add("C# check: /$p/ must not appear") }
    }

    # --- run ---
    $tfmDir = Get-ChildItem (Join-Path $proj 'bin\Debug') -Directory | Select-Object -First 1
    if ($meta.kind -eq 'console') {
        $dll = Join-Path $tfmDir.FullName "$name.dll"
        $stdin = ((@($meta.input) | Where-Object { $null -ne $_ }) -join "`n") + "`n"
        $psi = New-Object System.Diagnostics.ProcessStartInfo 'dotnet', "`"$dll`""
        $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
        $psi.UseShellExecute = $false; $psi.StandardOutputEncoding = [Text.Encoding]::UTF8
        $proc = [Diagnostics.Process]::Start($psi)
        $proc.StandardInput.Write($stdin); $proc.StandardInput.Close()
        $stdout = $proc.StandardOutput.ReadToEndAsync(); $stderr = $proc.StandardError.ReadToEndAsync()
        if (-not $proc.WaitForExit(30000)) { $proc.Kill(); $problems.Add('program did not finish within 30 s') }
        else {
            if ($proc.ExitCode -ne 0) { $problems.Add("program exited with code $($proc.ExitCode): $($stderr.Result.Trim())") }
            $out = $stdout.Result
            foreach ($rx in @($meta.expectedOutput)) {
                if ($rx -and ($out -notmatch $rx)) { $problems.Add("output does not match /$rx/ (got: $(($out -replace '\s+', ' ').Trim()))") }
            }
        }
    }
    elseif ($Run) {
        $exe = Join-Path $tfmDir.FullName "$name.exe"
        $proc = Start-Process $exe -PassThru
        Start-Sleep -Seconds $RunSeconds
        if ($proc.HasExited) { $problems.Add("window program exited early (code $($proc.ExitCode))") } else { $proc.Kill() }
    }
    return $problems
}

$results = @()
foreach ($d in $folders) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Write-Host ("[ {0} ] ..." -f $d.Name) -NoNewline
    try { $p = Test-Sample $d } catch { $p = @("script error: $($_.Exception.Message)") }
    $status = 'PASS'; if ($p.Count -gt 0) { $status = 'FAIL' }
    Write-Host ("`r[ {0} ] {1}  ({2:N0}s)" -f $d.Name, $status, $sw.Elapsed.TotalSeconds)
    foreach ($x in $p) { Write-Host "         - $x" }
    $results += [pscustomobject]@{ Sample = $d.Name; Status = $status }
}
$failed = @($results | Where-Object Status -eq 'FAIL').Count
Write-Host ''
Write-Host ("{0} sample(s): {1} passed, {2} failed" -f $results.Count, ($results.Count - $failed), $failed)
if ($failed -gt 0) { exit 1 }
