<#
.SYNOPSIS
  Generates the "dotnet new" template pack (PseudoNet.Templates) and packs it to artifacts\.

.DESCRIPTION
  Single source of truth = the Visual Studio templates (..\PseudoNet.Templates\) and the compiler package
  (..\PseudoNet.Build\artifacts\feed). For each template this script writes content\<shortname>\ with
  .template.config\template.json, the .csproj (renamed PseudoApp.csproj, the template's sourceName),
  Program.pseudo and, for samples, the shipped LLM answer (pseudo-cache\). The compiler package is stored ONCE in
  content\_shared\nuget-feed and copied into every generated project by the template engine.

  Order: PseudoNet.Build\pack.ps1 -> PseudoNet.Templates\make-sample-templates.ps1 -> this script.
#>
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here '..')).Path
$vsTemplates = Join-Path $repo 'PseudoNet.Templates'
$nupkg = Get-ChildItem (Join-Path $repo 'PseudoNet.Build\artifacts\feed') -Filter 'PseudoNet.Build.*.nupkg' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $nupkg) { throw 'No PseudoNet.Build package - run PseudoNet.Build\pack.ps1 first.' }

$content = Join-Path $here 'content'
if (Test-Path $content) { Remove-Item $content -Recurse -Force }
New-Item -ItemType Directory (Join-Path $content '_shared\nuget-feed') -Force | Out-Null
Copy-Item $nupkg.FullName (Join-Path $content '_shared\nuget-feed')

function ConvertTo-Kebab([string]$pascal) { ([regex]::Replace($pascal, '(?<=.)([A-Z])', '-$1')).ToLower() }
$utf8 = New-Object System.Text.UTF8Encoding($false)

$list = @()
foreach ($n in 'PseudoCode', 'PseudrawCode') { $list += [pscustomobject]@{ Name = $n; Dir = (Join-Path $vsTemplates $n); Short = $n.ToLower(); Sample = $false } }
Get-ChildItem (Join-Path $vsTemplates 'Samples') -Directory | ForEach-Object {
    $short = 'pseudo-' + (ConvertTo-Kebab $_.Name)
    if ($short -eq 'pseudo-guess-the-number') { $short = 'pseudo-guess-number' }
    $list += [pscustomobject]@{ Name = $_.Name; Dir = $_.FullName; Short = $short; Sample = $true }
}

foreach ($t in $list) {
    [xml]$vst = Get-Content (Join-Path $t.Dir "$($t.Name).vstemplate") -Raw -Encoding UTF8
    $display = $vst.VSTemplate.TemplateData.Name
    $desc = $vst.VSTemplate.TemplateData.Description
    $csproj = Get-Content (Join-Path $t.Dir "$($t.Name).csproj") -Raw -Encoding UTF8
    $isWindow = $csproj -match '<UseWindowsForms>true'

    $dst = Join-Path $content $t.Short
    New-Item -ItemType Directory (Join-Path $dst '.template.config') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $dst 'PseudoApp.csproj'), $csproj.Replace('$safeprojectname$', 'PseudoApp').TrimStart([char]0xFEFF), $utf8)
    Copy-Item (Join-Path $t.Dir 'Program.pseudo') $dst
    if (Test-Path (Join-Path $t.Dir 'pseudo-cache')) { Copy-Item (Join-Path $t.Dir 'pseudo-cache') $dst -Recurse }

    $kind = 'Console'; if ($isWindow) { $kind = 'Window' }
    $cfg = [ordered]@{
        '$schema'         = 'http://json.schemastore.org/template'
        author            = 'Malek AIT OUARET'
        classifications   = @('Pseudo.NET', $kind)
        identity          = "PseudoNet.Templates.$($t.Name)"
        name              = $display
        description       = $desc
        shortName         = $t.Short
        sourceName        = 'PseudoApp'
        preferNameDirectory = $true
        tags              = [ordered]@{ language = 'C#'; type = 'project' }
        sources           = @(
            [ordered]@{ source = './'; target = './'; modifiers = @([ordered]@{ exclude = @('.template.config/**') }) },
            [ordered]@{ source = '../_shared/'; target = './' }
        )
    }
    if ($isWindow) { $cfg['constraints'] = [ordered]@{ windows = [ordered]@{ type = 'os'; args = 'Windows' } } }
    [IO.File]::WriteAllText((Join-Path $dst '.template.config\template.json'), ($cfg | ConvertTo-Json -Depth 8), $utf8)
    Write-Host ("  {0,-24} {1}" -f $t.Short, $display)
}

$artifacts = Join-Path $here 'artifacts'
if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }
& dotnet pack (Join-Path $here 'PseudoNet.Templates.csproj') -c Release -o $artifacts --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'dotnet pack failed' }
Write-Host ''
Write-Host ('Pack: ' + (Get-ChildItem $artifacts -Filter *.nupkg | Select-Object -First 1).FullName)
