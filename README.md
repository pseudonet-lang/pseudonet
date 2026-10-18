# Pseudo.NET

**Write pseudocode in your own words. Get a running program.**

Pseudo.NET compiles *free-form* pseudocode to C# using a **local** language model ([Ollama](https://ollama.com)).
There is no imposed syntax: write in English, French, Spanish, Chinese… with braces, with indentation only, or just
sentences. No account, no API key, nothing leaves your machine.

[Français](README.fr.md) · [Website](https://pseudonet-lang.github.io/) · [Support the project](#support)

![Tetris written in pseudocode](docs/demo-tetris.gif)

## What it looks like

```text
We want to find all the prime numbers up to 50.

primes = a new list

define whether counter is prime, which returns a boolean
	isPrime = true
	if counter is less than 2 then
		isPrime = false
	repeat divisor from 2 to 50
		if divisor is less than counter then
			remainder = counter modulo divisor
			if remainder is equal to 0 then
				isPrime = false
	return isPrime

repeat counter from 2 to 50
	candidate = whether counter is prime
	if candidate then
		add counter to primes

print the text "Prime numbers up to 50:"
for each number in primes
	print number
```

The same program works written in French (`définir est-ce que compteur est premier…`), or any other language
the model understands. Tabs, braces or plain sentences: the compiler does not care.

## Two project types

| Template | Produces | Good for |
|---|---|---|
| **PseudoCode** | Console application | algorithms, teaching, exercises |
| **PseudrawCode** | Window (WinForms): drawing, keyboard, timer | small games, animations |

Six ready-to-run samples ship as project templates: **Snake, Tetris, Bounce, Primes, Bubble sort, Guess the number**.
They carry their pre-computed answer, so the *first run needs no Ollama at all*.

## Why

Agents write code now, yet people still need to learn to *think* algorithmically. Pseudocode is the classic way to
teach it, but it has never been executable. Pseudo.NET makes it executable without taking away its freedom, and the
generated C# is a real program the learner can open and read afterwards.

## Install

1. Install **Visual Studio 2026** (Community is fine). Visual Studio 2022 is intended to work but is not tested yet.
2. Install **[Ollama](https://ollama.com)** and pull the model once (about 5 GB):
   ```bash
   ollama pull qwen2.5-coder:7b
   ```
3. Install the extension: download `PseudoNet.Vsix.vsix` from the [Releases](../../releases) page and double-click it
   (or search *Pseudo.NET* in *Extensions → Manage Extensions* once published).
4. *File → New → Project* → search **Pseudo** → pick *PseudoCode*, *PseudrawCode* or a sample. Press **F5**.

### Or from the command line (any editor, .NET 8+ SDK)

```bash
dotnet new install PseudoNet.Templates
dotnet new pseudocode -n Hello
cd Hello
dotnet run
```

Short names: `pseudocode`, `pseudrawcode` (Windows), and the samples `pseudo-snake`, `pseudo-tetris`, `pseudo-bounce`,
`pseudo-primes`, `pseudo-bubble-sort`, `pseudo-guess-number`. The compiler itself is the NuGet package
`PseudoNet.Build`, usable from any SDK project that contains `.pseudo` files.

## How it works

```
.pseudo → local LLM → JSON AST → validation / repair → C# → your normal build
```

- Answers are **cached** (by pseudocode hash) in `%LOCALAPPDATA%\PseudoNet\cache`: an unchanged program never calls
  the model again, and a rebuild is instant.
- The AST is validated; if the model slips, the compiler asks it to correct itself (max 2 rounds), and warns otherwise.
- The generated C# is hidden by default; an option makes it visible and editable.
- Compiler messages are localized; colour and key names (`rouge`, `gauche`, `红色`…) have per-language tables in
  [`PseudoNet.Compiler/PseudoNet.Compiler/Colors`](PseudoNet.Compiler/PseudoNet.Compiler/Colors) and
  `Keys`: adding your own language is a JSON file.

## Repository layout

| Folder | Contents |
|---|---|
| `PseudoNet.Compiler` | the compiler (F#, .NET 8) |
| `PseudoNet.Build` | the `PseudoNet.Build` NuGet package (MSBuild targets + compiler) |
| `PseudoNet.Templates` | the 2 base templates + 6 generated sample templates (Visual Studio) |
| `PseudoNet.DotnetNew` | the same templates as a `dotnet new` pack (`PseudoNet.Templates` NuGet package) |
| `PseudoNet.Samples` | sample catalog + `verify-samples.ps1` regression check |
| `PseudoNet.Vsix` | the Visual Studio extension |
| `docs` | notes for contributors and for publishing |

The presentation website lives in its own folder/repository, separate from this solution.

## Honest limits

- A 7B local model is good, not perfect. Very large or very ambiguous programs may need rewording; the validator
  and correction loop reduce this but cannot remove it.
- Windows + Visual Studio only for now.
- First generation of a *new* program takes seconds to minutes depending on your hardware.

## Support

Pseudo.NET is free and MIT-licensed, and will stay that way. If it helped you or your students and you would like to
say thanks for the idea, a voluntary donation is very welcome, never required:

**[Support on Ko-fi](https://ko-fi.com/pseudonetlang)** (PayPal or card)

Stars, bug reports, new language tables and sample programs help just as much.

## License

[MIT](LICENSE.txt) © 2026 Malek AIT OUARET
