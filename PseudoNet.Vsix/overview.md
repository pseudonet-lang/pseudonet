# Pseudo.NET

Write **free-form pseudocode** (English, French, Spanish, Chinese… no imposed syntax) and let a **local** LLM
([Ollama](https://ollama.com)) compile it to C#. No account, no API key, nothing leaves your machine.

## What you get

- **PseudoCode**: console projects, ideal for algorithms and teaching.
- **PseudrawCode**: window projects with drawing, keyboard and timer, for small games.
- Six ready-to-run samples as project templates: **Snake, Tetris, Bounce, Primes, Bubble sort, Guess the number**.
  They ship their pre-computed answer, so the first run does not need Ollama.

## Requirements

- Visual Studio 2026 (tested). Visual Studio 2022 is targeted but not yet tested.
- [Ollama](https://ollama.com) with the model pulled once for your own programs: `ollama pull qwen2.5-coder:7b`.

## Getting started

*File → New → Project*, search **Pseudo**, choose a template, press **F5**.

## Links

- Website (English / Français): https://pseudonet-lang.github.io/
- Source code and issues: https://github.com/pseudonet-lang/pseudonet
- Questions, feedback and sample requests: https://github.com/pseudonet-lang/pseudonet/discussions
- `dotnet new` templates (no Visual Studio needed): `dotnet new install PseudoNet.Templates`

## Free and open source

MIT license. If it helps you or your students, a voluntary donation to say thanks is welcome (link in the
repository README). Never required.
