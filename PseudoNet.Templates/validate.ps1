# Instantiates every template the way Visual Studio would and builds it on a "clean machine":
#  - EMPTY global NuGet cache (NUGET_PACKAGES): the template must be self-contained;
#  - Visual Studio layout: <solution>\<solution>.slnx + <solution>\<project>\<project>.csproj, building the SOLUTION;
#  - every file declared in the .vstemplate must exist in the template as shipped (nuget-feed\ included);
#  - SAMPLE templates are built with Ollama made UNREACHABLE: they ship the LLM's answer, so they must not need it.
#    Console samples are then run and their output compared with the catalog's sample.json.
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$feed = Get-ChildItem (Join-Path $repo 'PseudoNet.Build\artifacts\feed') -Filter 'PseudoNet.Build.*.nupkg' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $feed) { throw 'No PseudoNet.Build package - run PseudoNet.Build\pack.ps1' }
$work = Join-Path $env:TEMP 'pseudonet-template-check'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory $work | Out-Null
$env:NUGET_PACKAGES = Join-Path $work '_empty-nuget-cache'
$env:PSEUDONET_LANG = 'en'
$realOllama = $env:PSEUDONET_OLLAMA_URL

function ConvertTo-PascalCase([string]$id) { (($id -split '-') | ForEach-Object { $_.Substring(0, 1).ToUpper() + $_.Substring(1) }) -join '' }
$catalog = @{}
Get-ChildItem (Join-Path $repo 'PseudoNet.Samples') -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'sample.json') } | ForEach-Object {
    $catalog[(ConvertTo-PascalCase $_.Name)] = Get-Content (Join-Path $_.FullName 'sample.json') -Raw -Encoding UTF8 | ConvertFrom-Json
}

$templates = @()
foreach ($t in 'PseudoCode', 'PseudrawCode') { $templates += [pscustomobject]@{ Name = $t; Dir = (Join-Path $PSScriptRoot $t); Sample = $false } }
$samplesRoot = Join-Path $PSScriptRoot 'Samples'
if (Test-Path $samplesRoot) { Get-ChildItem $samplesRoot -Directory | ForEach-Object { $templates += [pscustomobject]@{ Name = $_.Name; Dir = $_.FullName; Sample = $true } } }

$failures = 0
foreach ($t in $templates) {
    $tpl = $t.Dir; $tname = $t.Name
    try {
        [xml]$vst = Get-Content (Join-Path $tpl "$tname.vstemplate") -Raw
        $ns = New-Object System.Xml.XmlNamespaceManager($vst.NameTable); $ns.AddNamespace('v', 'http://schemas.microsoft.com/developer/vstemplate/2005')
        $staged = @(Get-ChildItem $tpl -Recurse -File | ForEach-Object { $_.FullName.Substring($tpl.Length + 1).Replace([string][char]92, '/') }) + ('nuget-feed/' + $feed.Name)
        foreach ($item in $vst.SelectNodes('//v:ProjectItem', $ns)) {
            $folder = ''
            if ($item.ParentNode.LocalName -eq 'Folder') { $folder = $item.ParentNode.GetAttribute('Name') + '/' }
            $declared = $folder + $item.InnerText.Trim()
            if ($staged -notcontains $declared) { throw "the .vstemplate declares '$declared' but the template does not contain it" }
        }

        $name = "My$tname"
        $sol = Join-Path $work $name
        $dst = Join-Path $sol $name
        New-Item -ItemType Directory $dst -Force | Out-Null
        Get-ChildItem $tpl -Recurse -File | Where-Object { $_.Extension -ne '.vstemplate' -and $_.Name -ne '__TemplateIcon.png' } | ForEach-Object {
            $rel = $_.FullName.Substring($tpl.Length + 1)
            $to = Join-Path $dst $rel
            New-Item -ItemType Directory (Split-Path $to) -Force | Out-Null
            Copy-Item $_.FullName $to
        }
        Rename-Item (Join-Path $dst "$tname.csproj") "$name.csproj"
        $cs = Join-Path $dst "$name.csproj"
        (Get-Content $cs -Raw -Encoding UTF8).Replace('$safeprojectname$', $name) | Set-Content $cs -Encoding UTF8
        New-Item -ItemType Directory (Join-Path $dst 'nuget-feed') -Force | Out-Null
        Copy-Item $feed.FullName (Join-Path $dst 'nuget-feed')
        Push-Location $sol
        dotnet new sln -n $name --force 2>&1 | Out-Null
        dotnet sln add $cs 2>&1 | Out-Null
        Pop-Location
        $slnFile = Get-ChildItem $sol -Filter "$name.sln*" | Select-Object -First 1

        if ($t.Sample) { $env:PSEUDONET_OLLAMA_URL = 'http://localhost:1' } else { $env:PSEUDONET_OLLAMA_URL = $realOllama }
        $mode = ''; if ($t.Sample) { $mode = ', Ollama unreachable' }
        $log = & dotnet build $slnFile.FullName --nologo 2>&1 | Out-String
        $buildExit = $LASTEXITCODE
        $env:PSEUDONET_OLLAMA_URL = $realOllama
        if ($buildExit -ne 0) { throw ("build failed: " + (($log -split "`r?`n" | Where-Object { $_ -match ' error ' } | Select-Object -First 2) -join ' | ')) }
        if ($log -match 'warning PSEUDO007') { throw 'the model answer has structural problems (PSEUDO007)' }
        if ($t.Sample -and $log -notmatch 'LLM cache: reused') { throw 'the sample did not use its shipped LLM answer (cache miss)' }

        $note = ''
        if ($t.Sample -and $catalog.ContainsKey($tname) -and $catalog[$tname].kind -eq 'console') {
            $meta = $catalog[$tname]
            $dll = Get-ChildItem (Join-Path $dst 'bin') -Recurse -Filter "$name.dll" | Select-Object -First 1
            $stdin = ((@($meta.input) | Where-Object { $null -ne $_ }) -join "`n") + "`n"
            $inFile = Join-Path $env:TEMP 'pseudonet-stdin.txt'
            [IO.File]::WriteAllText($inFile, $stdin, (New-Object System.Text.UTF8Encoding($false)))   # redirect from a file: PS 5.1 pipes add a BOM that breaks the first Parse
                $out = & cmd /c ('dotnet "' + $dll.FullName + '" < "' + $inFile + '" 2>&1') | Out-String
            foreach ($rx in @($meta.expectedOutput)) { if ($rx -and ($out -notmatch $rx)) { throw "output does not match /$rx/" } }
            $note = ', output OK'
        }
        Write-Host ("PASS  {0}  (clean NuGet cache{1}{2})" -f $tname, $mode, $note)
    }
    catch { $failures++; Write-Host ("FAIL  {0}: {1}" -f $tname, $_.Exception.Message) }
}
Write-Host ''
Write-Host ("{0} template(s): {1} passed, {2} failed" -f $templates.Count, ($templates.Count - $failures), $failures)
if ($failures -gt 0) { exit 1 }
