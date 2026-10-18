<#
.SYNOPSIS
  Turns every sample of PseudoNet.Samples\ into a Visual Studio project template under Samples\<Name>\.

.DESCRIPTION
  For each sample: copies its Program.pseudo and icon, derives the .csproj from the base template
  (PseudoCode or PseudrawCode), writes the .vstemplate, and SHIPS THE LLM ANSWER with it
  (pseudo-cache\<key>.json) so the first build needs neither Ollama nor minutes of waiting.

  The cache key = SHA-256(compiler fingerprint + pseudocode): any change to the prompt, the model or the
  sample invalidates it. Re-run this script after pack.ps1 whenever the compiler/prompt changed;
  missing answers are generated through Ollama (slow), up-to-date ones are kept. Use -Fresh to redo all.

  Requires: PseudoNet.Build\pack.ps1 already run (uses the PACKAGED compiler so the key matches what users get).
#>
param([switch]$Fresh, [string[]]$Sample)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here '..')).Path
$compiler = Join-Path $repo 'PseudoNet.Build\artifacts\compiler\PseudoNet.Compiler.exe'
if (-not (Test-Path $compiler)) { throw "Packaged compiler not found: $compiler - run PseudoNet.Build\pack.ps1 first." }
$nupkg = Get-ChildItem (Join-Path $repo 'PseudoNet.Build\artifacts\feed') -Filter 'PseudoNet.Build.*.nupkg' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $nupkg) { throw 'No PseudoNet.Build package in the feed.' }
$out = Join-Path $here 'Samples'
$env:PSEUDONET_LANG = 'en'

$vstemplate = @'
<VSTemplate Version="3.0.0" Type="Project" xmlns="http://schemas.microsoft.com/developer/vstemplate/2005" xmlns:sdk="http://schemas.microsoft.com/developer/vstemplate-sdkextension/2010">
  <TemplateData>
    <Name>@@DISPLAY@@</Name>
    <Description>@@DESCRIPTION@@</Description>
    <ProjectType>CSharp</ProjectType>
    <SortOrder>@@SORT@@</SortOrder>
    <CreateNewFolder>true</CreateNewFolder>
    <PromptForSaveOnCreation>true</PromptForSaveOnCreation>
    <DefaultName>@@DEFAULTNAME@@</DefaultName>
    <ProvideDefaultName>true</ProvideDefaultName>
    <LocationField>Enabled</LocationField>
    <EnableLocationBrowseButton>true</EnableLocationBrowseButton>
    <CreateInPlace>true</CreateInPlace>
    <Icon>__TemplateIcon.png</Icon>
    <LanguageTag>csharp</LanguageTag>
    <PlatformTag>windows</PlatformTag>
    <ProjectTypeTag>@@TAG@@</ProjectTypeTag>
  </TemplateData>
  <TemplateContent>
    <Project TargetFileName="$safeprojectname$.csproj" File="@@NAME@@.csproj" ReplaceParameters="true">
      <ProjectItem ReplaceParameters="false" TargetFileName="Program.pseudo" OpenInEditor="true">Program.pseudo</ProjectItem>
      <Folder Name="pseudo-cache" TargetFolderName="pseudo-cache">
        <ProjectItem ReplaceParameters="false" TargetFileName="@@KEY@@.json">@@KEY@@.json</ProjectItem>
      </Folder>
      <Folder Name="nuget-feed" TargetFolderName="nuget-feed">
        <ProjectItem ReplaceParameters="false" TargetFileName="@@NUPKG@@">@@NUPKG@@</ProjectItem>
      </Folder>
    </Project>
  </TemplateContent>
</VSTemplate>
'@

function ConvertTo-PascalCase([string]$id) { (($id -split '-') | ForEach-Object { $_.Substring(0, 1).ToUpper() + $_.Substring(1) }) -join '' }

$folders = Get-ChildItem (Join-Path $repo 'PseudoNet.Samples') -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'sample.json') }
if ($Sample) { $wanted = @($Sample | ForEach-Object { $_ -split ',' }); $folders = $folders | Where-Object { $wanted -contains $_.Name } }
$sort = 2000
foreach ($d in $folders) {
    $meta = Get-Content (Join-Path $d.FullName 'sample.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $name = ConvertTo-PascalCase $d.Name
    $kindName = 'PseudoCode'; $baseDir = 'PseudoCode'; $tag = 'console'
    if ($meta.kind -eq 'window') { $kindName = 'PseudrawCode'; $baseDir = 'PseudrawCode'; $tag = 'games' }
    $dst = Join-Path $out $name
    Write-Host ("[ {0} ] -> Samples\{1}" -f $d.Name, $name)
    New-Item -ItemType Directory (Join-Path $dst 'pseudo-cache') -Force | Out-Null

    Copy-Item (Join-Path $d.FullName 'Program.pseudo') $dst -Force
    Copy-Item (Join-Path $d.FullName 'icon.png') (Join-Path $dst '__TemplateIcon.png') -Force

    # csproj : base template + cache livré avec le projet
    $csproj = Get-Content (Join-Path $here "$baseDir\$baseDir.csproj") -Raw -Encoding UTF8
    $csproj = $csproj.Replace('    <PseudoEmitCSharp>false</PseudoEmitCSharp>', "    <PseudoEmitCSharp>false</PseudoEmitCSharp>`r`n    <!-- The LLM's answer for this sample ships with the project: the first build needs neither Ollama nor waiting. -->`r`n    <PseudoCacheDir>`$(MSBuildProjectDirectory)/pseudo-cache</PseudoCacheDir>")
    $csproj = $csproj.Replace('    <None Remove="nuget-feed\**" />', "    <None Remove=`"nuget-feed\**`" />`r`n    <None Remove=`"pseudo-cache\**`" />")
    Set-Content (Join-Path $dst "$name.csproj") $csproj -Encoding UTF8

    # clé de cache de CE pseudocode avec CE compilateur
    $pseudo = Join-Path $dst 'Program.pseudo'
    $key = (& $compiler $pseudo --print-cache-key).Trim()
    $cacheDir = Join-Path $dst 'pseudo-cache'
    $cacheFile = Join-Path $cacheDir "$key.json"
    if ($Fresh -or -not (Test-Path $cacheFile)) {
        Get-ChildItem $cacheDir -Filter '*.json' -ErrorAction SilentlyContinue | Remove-Item -Force   # entrées périmées
        Write-Host "         generating the LLM answer (Ollama)..."
        $tmpCs = Join-Path $env:TEMP "sample-$name.cs"
        & $compiler $pseudo --cache-dir $cacheDir --output $tmpCs --project-type $meta.kind
        if ($LASTEXITCODE -ne 0) { throw "$($d.Name): the compiler failed (exit $LASTEXITCODE)" }
        Remove-Item $tmpCs -ErrorAction SilentlyContinue
        if (-not (Test-Path $cacheFile)) { throw "$($d.Name): no cache entry produced (answer rejected as structurally invalid?)" }
    } else { Write-Host "         LLM answer already up to date" }
    Get-ChildItem $cacheDir -Filter '*.json' | Where-Object { $_.Name -ne "$key.json" } | Remove-Item -Force

    $description = $meta.title
    if ($meta.description) { $description = $meta.description }
    $xml = $vstemplate.Replace('@@DISPLAY@@', "$kindName Sample - $($meta.title)").Replace('@@DESCRIPTION@@', [Security.SecurityElement]::Escape($description)).Replace('@@SORT@@', "$sort").Replace('@@DEFAULTNAME@@', "$($name)Sample").Replace('@@TAG@@', $tag).Replace('@@NAME@@', $name).Replace('@@KEY@@', $key).Replace('@@NUPKG@@', $nupkg.Name)
    Set-Content (Join-Path $dst "$name.vstemplate") $xml -Encoding UTF8
    $sort += 10
}
Write-Host "Done. Samples\ now holds $(@(Get-ChildItem $out -Directory).Count) template(s)."
