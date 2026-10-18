# Announcement drafts

Ready-to-adapt texts, one per audience. Facts to keep accurate in every post: Windows only; tested on Visual Studio 2026
(2022 targeted, untested); needs Ollama + a ~5 GB model for your own programs (the six samples run without it); a 7B
local model is good, not perfect; MIT; the generated C# is a real program. Do not claim more than that.

Links to reuse
- Site: https://pseudonet-lang.github.io/ (French: https://pseudonet-lang.github.io/fr/)
- Marketplace: https://marketplace.visualstudio.com/items?itemName=pseudonet-lang.pseudonet
- Source: https://github.com/pseudonet-lang/pseudonet
- `dotnet new install PseudoNet.Templates`
- Support (optional): https://ko-fi.com/pseudonetlang  (only where self-promotion rules allow it, never in the title)

Suggested order: teachers first (core audience), then Developpez, then r/csharp, then r/ProgrammingLanguages, then
Show HN last (needs the most polish; ideally after a hand-played GIF and a VS 2022 report). One venue per day, answer
every comment within the first hours.

---

## 1. Teachers (French): NSI forums, mailing lists, LinkedIn, Mastodon

**Titre :** Un pseudocode libre, en français, qui s'exécute vraiment (gratuit, sans compte)

Bonjour,

Je viens de publier Pseudo.NET, un outil pour que le pseudocode d'un élève **s'exécute tel qu'il l'a écrit**, sans
syntaxe imposée : en français, avec ou sans accolades, avec la seule indentation, ou en phrases.

- Un modèle de langage **local** (Ollama) traduit le pseudocode en C#. Aucun compte, aucune clé d'API, rien ne quitte
  l'ordinateur : pas de donnée d'élève envoyée à un service externe.
- Deux types de projet : console (algorithmique) et fenêtre (dessin, clavier, petits jeux).
- Six exemples fournis, qui tournent sans rien installer de plus : Snake, Tétris, balle rebondissante, nombres premiers,
  tri à bulles, devine le nombre.
- Le C# généré est un vrai programme que l'élève peut ouvrir ensuite, ce qui fait le pont avec un langage « réel ».
- Gratuit, licence MIT. S'installe depuis Visual Studio (Marketplace) ou en ligne de commande (`dotnet new`).

Limites à connaître : Windows uniquement pour l'instant ; il faut installer Ollama et télécharger un modèle de 5 Go pour
ses propres programmes ; un modèle local de 7 milliards de paramètres est bon mais pas parfait, un programme très long
ou ambigu peut demander une reformulation.

Site (en français) : https://pseudonet-lang.github.io/fr/
Je serais ravi d'avoir vos retours d'enseignants : ce qui manque, ce qui gêne, des exercices à ajouter en exemples.

---

## 2. Developpez.com (French, developer audience)

**Titre :** Pseudo.NET : compiler du pseudocode libre en C# avec un LLM local (Visual Studio, `dotnet new`)

Pseudo.NET est un compilateur dont la « grammaire » est un modèle de langage local : on écrit du pseudocode sans syntaxe
imposée (français, anglais, indentation seule…), le modèle produit un AST JSON, qui est **validé et normalisé**
(avec une boucle de correction si le modèle se trompe), puis un générateur déterministe écrit le C#.

Points techniques qui pourraient vous intéresser :
- Cache des réponses du LLM par empreinte du pseudocode : un programme inchangé ne rappelle jamais le modèle.
- Le C# est caché par défaut, visible et éditable sur option ; une modification manuelle n'est pas écrasée tant que le
  pseudocode ne change pas.
- Intégration MSBuild (paquet NuGet `PseudoNet.Build`), extension Visual Studio, paquet de modèles `dotnet new`.
- Tables JSON par langue pour les couleurs et les touches : ajouter une langue = écrire un fichier.

Code source (F#, MIT) : https://github.com/pseudonet-lang/pseudonet
Limites : Windows ; qualité dépendante d'un modèle 7B ; Visual Studio 2022 non testé.

---

## 3. r/csharp (flair: Showcase)

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

## 4. r/ProgrammingLanguages (language-design angle; read the sub's rules first)

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

## 5. Show HN

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

## 6. Short posts (Mastodon / X / LinkedIn)

English:
> Pseudo.NET: write pseudocode in your own words, in any language, and run it. A local LLM compiles it to C#; no account, no API key, nothing leaves your machine. Free, MIT. Visual Studio extension + `dotnet new`. https://pseudonet-lang.github.io/

Français :
> Pseudo.NET : écrivez votre pseudocode avec vos mots, dans votre langue, et exécutez-le. Un LLM local le compile en C# : pas de compte, pas de clé d'API, rien ne quitte votre machine. Gratuit, MIT. Extension Visual Studio + `dotnet new`. https://pseudonet-lang.github.io/fr/
