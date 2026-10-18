# Publishing checklist

GitHub organisation: `pseudonet-lang` (repos `pseudonet` and `pseudonet-lang.github.io`, already filled in).
No placeholder left (Marketplace publisher ID `pseudonet-lang`, donations via Ko-fi `pseudonetlang`).
(`grep -r "YOUR-" . ../PseudoNet-Site`).

## Before shipping any change to a sample or to the prompt

The automatic checks (`verify-samples.ps1`) only prove that certain code is present; they cannot prove the program
behaves. The Snake sample once passed them while its snake could not move (the model had collapsed an
if / else-if chain into a nonsense formula).

1. Run `PseudoNet.Samples\verify-samples.ps1 -NoCache` (fresh model answers) and fix anything red.
2. **Play every window sample for at least 20 seconds** (Tetris, Snake, Bounce): use every key, lose, restart, and
   compare the behaviour with the pseudocode. Read the generated C# of the main rule when something looks odd.
3. Run the console samples with their documented input and read the output.
4. Only then regenerate the shipped answers, rebuild the packages and publish.
5. When a regex cannot express a rule, add one that checks the *behaviour-critical* lines of the generated C#
   (see `PseudoNet.Samples\snake\sample.json`).

## 1. GitHub repository (this folder = repository root)
- Create the repo, push this folder. `bin/`, `obj/`, `artifacts/`, `TestOutput/` must not be committed (see `.gitignore`).
- Uncomment the line in `.github/FUNDING.yml` to show the Sponsor button.
- Record the GIF (`RECORDING.md`).
- Releases: attach `PseudoNet.Vsix/bin/Release/PseudoNet.Vsix.vsix` for each version.

## 2. Visual Studio Marketplace
1. Create a publisher at <https://marketplace.visualstudio.com/manage> (free, Microsoft account).
   The publisher **ID** goes in `PseudoNet.Vsix/publishManifest.json` (`publisher`); the manifest's
   `Publisher="Malek AIT OUARET"` is the display name and should match the publisher's display name.
2. Fill `repo` in `publishManifest.json`.
3. Optional, once the repo exists, add inside `<Metadata>` of `source.extension.vsixmanifest`:
   ```xml
   <MoreInfo>https://github.com/pseudonet-lang/pseudonet</MoreInfo>
   <GettingStartedGuide>https://pseudonet-lang.github.io/</GettingStartedGuide>
   <ReleaseNotes>https://github.com/pseudonet-lang/pseudonet/releases</ReleaseNotes>
   ```
4. Upload the `.vsix` in the web portal (or `VsixPublisher.exe publish`), with `overview.md` as the page text.
5. Before announcing VS 2022 support, find a tester; until then say "tested on Visual Studio 2026".

## 3. NuGet.org (needed for the `dotnet new` commands shown on the site)
The site and READMEs say `dotnet new install PseudoNet.Templates`: that only works once
`PseudoNet.DotnetNew/artifacts/PseudoNet.Templates.<version>.nupkg` is pushed to nuget.org (API key from your
nuget.org account: `dotnet nuget push <nupkg> -k <key> -s https://api.nuget.org/v3/index.json`). Until then use
`dotnet new install <path-to-nupkg>`. Check the id `PseudoNet.Templates` is free and consider reserving the
`PseudoNet.` ID prefix. The generated projects restore `PseudoNet.Build` from the feed they carry, so publishing
`PseudoNet.Build` itself is optional.

Release order for the pack: `PseudoNet.Build\pack.ps1` -> `PseudoNet.Templates\make-sample-templates.ps1` ->
`PseudoNet.DotnetNew\build-dotnet-templates.ps1` -> `PseudoNet.DotnetNew\validate-dotnet-templates.ps1`.
Bump `<Version>` in `PseudoNet.DotnetNew\PseudoNet.Templates.csproj` whenever the content changes.

## 4. Website
See `../PseudoNet-Site/README.md` (separate folder, intended as its own repository for GitHub Pages).

## 5. Announcements
Teachers' communities (NSI forums, Capytale), Developpez.com, r/csharp, r/dotnet, r/ProgrammingLanguages,
"Show HN". Lead with the GIF and the "pseudocode in French, no syntax" angle.

## 6. Donations
Voluntary only; never nag inside the compiler or IDE. Regular donations may have tax / status implications in
France: check before enabling the button.
