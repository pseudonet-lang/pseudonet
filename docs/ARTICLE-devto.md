---
title: What I learned using a 7B local LLM as the front end of a programming language
published: false
description: A language with no fixed grammar: the LLM parses, an ordinary validator and a deterministic backend do the rest. What worked, what broke, what I would not do again.
tags: llm, dotnet, compilers, education
---

Pseudocode is how we teach algorithms, yet it has never been executable. You can write "repeat divisor from 2 to 50"
on a whiteboard, but not run it. I wanted to change that without inventing yet another syntax, so I built
**Pseudo.NET**: you write pseudocode however you like, and a *local* language model turns it into a program.

This is not a launch post. It is what I learned about putting a small LLM at the front of a compiler. If you are
tempted to try something similar, maybe some of this saves you a few evenings.

## The idea in one picture

```text
free-form pseudocode  ->  local LLM  ->  JSON AST  ->  validate / repair  ->  deterministic C# backend  ->  normal build
```

The model never writes the final program. Its job ends at a **structured tree** (a JSON AST with a fixed set of node
kinds). Everything after that is ordinary, testable code: a validator, a type inference pass, and a backend that emits C#.
That split is the single most important design decision, and everything below follows from it.

Here is a program, written with tabs only, no braces, no `end if`:

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

The same program works written in French ("définir est-ce que compteur est premier…"), and in other languages the model
understands. The tool does not decide what the syntax is; the user does.

## What I used

- A small model, `qwen2.5-coder:7b`, run locally through Ollama. No account, no API key, and nothing leaves the machine
  (which matters if the users are students).
- Temperature 0. For identical input, the output is the same.
- F# for the compiler, C# as the target, and an MSBuild package so a `.pseudo` file builds like any other source file.

## Lesson 1: a small model drops bare booleans

My first AST had flags like `"tapped": true` for keyboard input ("is the left arrow *held* or was it *just pressed*?").
The 7B model quietly omitted them. Not always, but often enough that games behaved randomly: a piece that should move
continuously moved once.

What worked was replacing flags by **named values**: `"press": "held"` or `"press": "just_pressed"`, and for drawing
`"style": "filled"` or `"style": "outline"`. A string with a name is much harder to forget than a missing `true`. Field
order mattered too: putting `style` right after `kind` made the model fill it in far more reliably.

A simplified example of what the model now returns for "while the left key is held":

```json
{ "kind": "KeyPressed", "key": "left", "press": "held" }
```

## Lesson 2: concrete examples beat abstract rules, but long prompts backfire

Rules written in prose ("a negated condition is a unary NOT") were followed inconsistently. A couple of tiny concrete
JSON examples in the prompt fixed the same problems more reliably.

The catch: every addition makes the prompt longer, and a longer prompt made the model **forget something else**. I added a
section about drawing and, a day later, noticed negations had regressed ("if not X" quietly became "X != false" and a game
showed a black screen). The only defence I found was a regression net: a script that regenerates a set of sample programs
from scratch with the real model and checks the generated C# against expectations. Every prompt change goes through it.
If you do this, build that net first, not after the third surprise.

## Lesson 3: decide what you are allowed to repair

Models make mistakes in the *shape* of their output (a field named `operator` instead of `op`, a kind called `Call`
instead of `FunctionCallStmt`, an array where an object was expected). Repairing those is fine, and I do it in a small
normalisation pass.

What I refuse to do is repair the **user's words**: no keyword tables for "if / si / wenn" and no rewriting of text to
make the model's life easier. The moment I did that, I would have reintroduced a syntax, just a hidden one. So the rule is:

1. First choice: teach the model better, through the prompt.
2. Second: repair the shape of its JSON output.
3. Never: touch the wording of what the user wrote.

For what remains, there is a validator that checks the tree structurally. If it finds problems, the compiler sends them
back to the model for correction, at most twice. If the answer is still invalid, you get a warning, and the invalid answer
is never cached.

## Lesson 4: determinism comes from caching, not from the model

Even at temperature 0, I do not want to depend on "the model happens to answer the same way tomorrow". Answers are cached
under a hash of (model + prompt + pseudocode), so an unchanged program never calls the model again, rebuilds are instant,
and the same input always gives the same C#. The six sample programs even ship their cached answer, so a first build works
without Ollama installed.

If you edit the generated C# by hand, the tool keeps your edit until the pseudocode changes, then regenerates and saves the
old file as a backup. The model is an interactive front end; the build stays boring.

## What does not work (yet)

- A 7B model is good, not perfect. Very long or ambiguous programs sometimes need rewording.
- A model can be **charitable**: if a student writes something slightly wrong, it may guess the intent instead of
  flagging the mistake. For teaching this is a real question. I am thinking about showing the interpreted structure
  back to the student so they can check it.
- Windows only for now, and the first generation of a new program takes seconds to minutes depending on your hardware.
- It is a teaching tool. I would not use it for production code, and I would not trust the model for anything with security
  implications.

## Is it a good idea?

For education, I think it is worth trying: the barrier to running an idea drops, and the generated C# is a real program a
student can open afterwards. For anything else, probably not, because a grammar is exactly the thing that makes a language
precise and predictable. I am deliberately trading that away, and the validator, the cache and the deterministic backend
are how I try to get some of it back.

## Try it

- Visual Studio extension: https://marketplace.visualstudio.com/items?itemName=pseudonet-lang.pseudonet
- Without Visual Studio: `dotnet new install PseudoNet.Templates`
- Source (F#, MIT): https://github.com/pseudonet-lang/pseudonet
- Site: https://pseudonet-lang.github.io/

I would love to hear where it breaks, and whether you have tried a model as a *parser* rather than a chatbot.

*Disclosure: I used an AI assistant to help draft and edit this article and to build parts of the tool. The experiments and conclusions come from my own work on the project.*
