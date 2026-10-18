# Pseudo.NET templates for `dotnet new`

Free-form pseudocode (any language, no imposed syntax) compiled to C# by a **local** LLM ([Ollama](https://ollama.com)).
No account, no API key, nothing leaves your machine.

```bash
dotnet new install PseudoNet.Templates
dotnet new pseudocode -n Hello
cd Hello
dotnet run
```

| Short name | Project |
|---|---|
| `pseudocode` | Console application |
| `pseudrawcode` | Window (WinForms, Windows only): drawing, keyboard, timer |
| `pseudo-snake`, `pseudo-tetris`, `pseudo-bounce` | Runnable window samples |
| `pseudo-primes`, `pseudo-bubble-sort`, `pseudo-guess-number` | Runnable console samples |

Samples ship their pre-computed LLM answer: their first `dotnet run` needs no Ollama. For your own programs, install
Ollama and run `ollama pull qwen2.5-coder:7b` once.

The compiler itself is the `PseudoNet.Build` package, referenced by the generated project.
MIT licensed. Source and docs: https://github.com/pseudonet-lang/pseudonet · Website: https://pseudonet-lang.github.io/
