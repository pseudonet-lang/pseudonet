# Announcement drafts

Ready-to-adapt texts, all in English, aimed at an international audience (educators, .NET developers, language
enthusiasts). The language feature itself is part of the pitch: the same pseudocode can be written in English, French,
Spanish, Chinese, or any other language the model understands.

Facts to keep accurate in every post: Windows only; tested on Visual Studio 2026 (2022 targeted, untested); needs
Ollama + a ~5 GB model for your own programs (the six samples run without it); a 7B local model is good, not perfect;
MIT; the generated C# is a real program. Do not claim more than that.

Links to reuse
- Site: https://pseudonet-lang.github.io/
- Marketplace: https://marketplace.visualstudio.com/items?itemName=pseudonet-lang.pseudonet
- Source: https://github.com/pseudonet-lang/pseudonet
- `dotnet new install PseudoNet.Templates`
- Support (optional): https://ko-fi.com/pseudonetlang (only where self-promotion rules allow it, never in a title)

Suggested order: educators first (core audience), then r/csharp and r/dotnet, then r/ProgrammingLanguages, then Show HN
last (needs the most polish; ideally after a hand-played GIF and a VS 2022 report). One venue per day, and answer every
comment within the first hours. Read each community's self-promotion rules before posting.

---

## 1. Educators: r/csteachers, CS education forums, LinkedIn, Mastodon (#csed #edtech)

**Title:** Pseudocode that actually runs, in your own words and your own language (free, local, no accounts)

Hi all,

Pseudocode is how most of us teach algorithms (it shows up in many curricula, from Cambridge and IB to AP CSP), yet it has
never been executable. I built Pseudo.NET so a student's pseudocode **runs as written**, with no imposed syntax:
braces or indentation only, "if ... then", or plain sentences, in English, French, Spanish, Chinese and more.

- A **local** language model (Ollama) turns the pseudocode into C#. No accounts, no API keys, and nothing leaves the
  machine, so no student data goes to an outside service.
- Two project types: console (algorithms, exercises) and window (drawing, keyboard, small games).
- Six ready-to-run samples: Snake, Tetris, a bouncing ball, prime numbers, bubble sort, guess-the-number.
  They run without installing anything else.
- The generated C# is a real program students can open afterwards, a bridge to "real" code.
- Free, MIT licensed. Install from the Visual Studio Marketplace or with `dotnet new`.

Things to know: Windows only for now; for your own programs you need Ollama and a ~5 GB model download; a 7B local
model is good but not perfect, so a very long or ambiguous program may need rewording.

https://pseudonet-lang.github.io/

I'd really value feedback from teachers: what is missing, what gets in the way, which exercises should be samples.

---

## 2. r/csharp (flair: Showcase) and r/dotnet

**Title:** I built a free-form pseudocode-to-C# compiler that uses a local LLM (Visual Studio extension + dotnet new)

I've been working on Pseudo.NET: you write pseudocode with no imposed syntax (English, French, braces or indentation
only, even plain sentences) and a local model (Ollama, qwen2.5-coder:7b) turns it into a JSON AST. The AST is validated,
repaired if needed, and a deterministic backend emits C#, which then builds like any .NET project.

- Two project types: console, and WinForms (drawing, keyboard, timer: Snake and Tetris are included as samples).
- MSBuild package `PseudoNet.Build`, a Visual Studio extension, and `dotnet new install PseudoNet.Templates`.
- LLM answers are cached by hash, and the six samples ship their answer so the first run needs no Ollama.
- MIT, source in F#.

Honest limits: Windows only, a 7B model is good but not perfect, VS 2022 untested. It is aimed at teaching algorithms.

Marketplace: https://marketplace.visualstudio.com/items?itemName=pseudonet-lang.pseudonet
Repo: https://github.com/pseudonet-lang/pseudonet
Feedback welcome, especially on where it breaks.

---

## 3. r/ProgrammingLanguages (language-design angle; read the sub's rules first)

**Title:** A language with no fixed grammar: an LLM as the front end, a validated AST as the contract

The design question: what if the "syntax" is whatever the user writes, and the only fixed thing is the AST? Pseudo.NET
parses free-form pseudocode with a small local LLM into a JSON AST, then checks it with an ordinary validator and
backends.

What I found along the way:
- A 7B model drops bare booleans; named enum fields (`"press": "held"`) were far more reliable than flags.
- Concrete JSON examples in the prompt beat abstract rules, but long prompts make the model forget other rules, so each
  addition needs a regression run (I keep a verification script over six sample programs).
- Repairs are limited to model-output shape (aliases); I refuse to "repair" the user's wording, since that would
  reintroduce a syntax. Remaining problems go through a validator + feedback loop (max 2 rounds), else a warning.
- Determinism comes from caching, not from the model: same pseudocode + same prompt + same model gives the same answer.

It is a teaching tool, not a general-purpose language: the interesting part for me is where the parser is probabilistic
and everything after it is not. Source (F#, MIT): https://github.com/pseudonet-lang/pseudonet
Happy to discuss the trade-offs, including why this is probably a bad idea for anything but education.

---

## 4. Show HN

**Title:** Show HN: Pseudo.NET, pseudocode with no fixed syntax that compiles to C# via a local LLM

(First comment, to post right after submitting)

Hi HN, I'm the author. Pseudo.NET lets you write pseudocode however you like (English, French, braces or just
indentation) and compiles it to C# using a local model through Ollama: nothing leaves your machine, no account or API
key. Pipeline: LLM -> JSON AST -> validation/repair -> deterministic C# backend -> normal .NET build. Answers are cached
by hash so rebuilds are instant and reproducible.

I built it for teaching algorithms: pseudocode is how we teach it, but it has never been executable. The included
samples (Snake, Tetris, bubble sort...) are written in plain pseudocode and ship with their precomputed answer.

Limits, up front: Windows only, tested on Visual Studio 2026 (2022 untested), a 7B model sometimes needs a rewording on
long programs, and the first generation of a new program takes seconds to minutes depending on hardware.

Site: https://pseudonet-lang.github.io/  Source (MIT): https://github.com/pseudonet-lang/pseudonet
I'd love to hear where it fails.

---

## 5. Other venues worth considering

- **dev.to / Hashnode:** a longer article built from section 3 ("What an LLM front end taught me about language design").
- **Hacker News alternatives:** Lobsters needs an invitation; skip unless you already have one.
- **Education newsletters and communities:** CS Teachers Association chapters, Computing At School (UK), Code.org
  teacher forums; send the educator text (section 1), shortened.

---

## 6. Short posts (Mastodon / X / LinkedIn / Bluesky)

> Pseudo.NET: write pseudocode in your own words, in any language, and run it. A local LLM compiles it to C#; no account, no API key, nothing leaves your machine. Free, MIT. Visual Studio extension + `dotnet new`. https://pseudonet-lang.github.io/

Variant for educators:
> Pseudocode is how we teach algorithms, but it never ran. Pseudo.NET makes a student's pseudocode executable, in any language and style, offline. Free and open source. https://pseudonet-lang.github.io/ #csed
