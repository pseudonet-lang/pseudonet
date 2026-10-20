module Program

open System
open System.IO
open System.Text
open System.Text.Json.Nodes
open Messages

// ── Command line ─────────────────────────────────────────────────────────────
//   PseudoNet.Compiler <file.pseudo> [options]
//   PseudoNet.Compiler --check-ollama
//
//   --output <file.cs>      where to write the C# (default: <file>.cs next to the .pseudo)
//   --emit-csharp           visible, editable C#: kept as is while the .pseudo is unchanged;
//                           otherwise regenerated, the old one saved as <file>.cs.bak
//   --cache-dir <dir>       cache of the LLM's JSON (default: %LOCALAPPDATA%\PseudoNet\cache)
//   --no-cache              neither read nor write the cache
//   --copy-to <file.cs>     also copy the generated C# there (handy for tests)
//   --print                 print the generated C#
//   --project-type <t>      console | window: type expected by the project template.
//                           A mismatch gives a clear error (PSEUDO004)
//   --check-ollama          check that Ollama answers and has the model, then exit
//   --print-cache-key       print the LLM-cache key (file name without .json) of <file.pseudo>, then exit
//   --lang <xx>             message language (default: PSEUDONET_LANG, then the system language)
//
// Exit codes: 0 success · 1 compile error · 2 Ollama unavailable.
// Errors use the canonical MSBuild format ("file: error CODE: message").

type Options =
    { Input: string option
      Output: string option
      EmitCSharp: bool
      CacheDir: string
      NoCache: bool
      CopyTo: string option
      ExpectedType: string option
      Print: bool
      CheckOllama: bool
      PrintCacheKey: bool }

let private defaultCacheDir =
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PseudoNet", "cache")

/// --lang doit agir AVANT l'analyse des autres arguments (leurs erreurs sont localisées).
let private applyLangOption (argv: string[]) =
    match Array.tryFindIndex ((=) "--lang") argv with
    | Some i when i + 1 < argv.Length -> setLanguage argv.[i + 1]
    | _ -> ()

let private parseArgs (argv: string[]) : Result<Options, string> =
    let rec go (opts: Options) (rest: string list) =
        match rest with
        | [] -> Ok opts
        | "--lang" :: _ :: tl       -> go opts tl
        | "--output" :: v :: tl     -> go { opts with Output = Some v } tl
        | "--cache-dir" :: v :: tl  -> go { opts with CacheDir = v } tl
        | "--copy-to" :: v :: tl    -> go { opts with CopyTo = Some v } tl
        | "--project-type" :: v :: tl when v = "console" || v = "window" -> go { opts with ExpectedType = Some v } tl
        | "--project-type" :: v :: _ -> Error (t "arg.projectTypeValue" "--project-type expects console or window (got: {0})" [ box v ])
        | "--emit-csharp" :: tl     -> go { opts with EmitCSharp = true } tl
        | "--no-cache" :: tl        -> go { opts with NoCache = true } tl
        | "--print" :: tl           -> go { opts with Print = true } tl
        | "--check-ollama" :: tl    -> go { opts with CheckOllama = true } tl
        | "--print-cache-key" :: tl -> go { opts with PrintCacheKey = true } tl
        | ("--output" | "--cache-dir" | "--copy-to" | "--project-type" | "--lang") as o :: [] ->
            Error (t "arg.needsValue" "option {0} expects a value" [ box o ])
        | o :: _ when o.StartsWith("--") -> Error (t "arg.unknown" "unknown option: {0}" [ box o ])
        | f :: tl when opts.Input.IsNone -> go { opts with Input = Some f } tl
        | f :: _ -> Error (t "arg.extra" "unexpected extra argument: {0}" [ box f ])
    go { Input = None; Output = None; EmitCSharp = false; CacheDir = defaultCacheDir
         NoCache = false; CopyTo = None; ExpectedType = None; Print = false; CheckOllama = false; PrintCacheKey = false }
       (List.ofArray argv)

exception ProjectTypeMismatch of string

let private headerPrefix = "// pseudo-source-hash: "

let private readEmittedHash (path: string) : string option =
    if File.Exists(path) then
        use r = new StreamReader(path, Encoding.UTF8)
        match r.ReadLine() with
        | null -> None
        | line when line.StartsWith(headerPrefix) -> Some (line.Substring(headerPrefix.Length).Trim())
        | _ -> None
    else None

let private compileError (file: string) (code: string) (msg: string) =
    eprintfn "%s: error %s: %s" file code (msg.Replace("\r", " ").Replace("\n", " "))

let private compile (opts: Options) (file: string) : int =
    let pseudocode = File.ReadAllText(file, Encoding.UTF8)
    let srcHash = LlmCache.sourceHash pseudocode
    let outFile = opts.Output |> Option.defaultValue (Path.ChangeExtension(file, ".cs"))

    // Visible C# already up to date: leave it alone (preserves the user's edits).
    if opts.EmitCSharp && readEmittedHash outFile = Some srcHash then
        printfn "%s" (t "info.upToDate" "C# is up to date (the .pseudo has not changed), kept as is: {0}" [ box outFile ])
        0
    else
        let replayPath = Environment.GetEnvironmentVariable("PSEUDONET_REPLAY")
        let cacheKey = LlmCache.keyFor OllamaClient.fingerprint pseudocode

        let normalizeAndCheck (json: string) =
            // Diagnostic: PSEUDONET_DUMP_AST=<path> saves each raw model answer (before normalisation) next to that path.
            match Environment.GetEnvironmentVariable("PSEUDONET_DUMP_AST") with
            | null | "" -> ()
            | dumpPath -> (try System.IO.File.AppendAllText(dumpPath, json + "\n---\n") with _ -> ())
            let node = AstNormalize.normalize (AstNormalize.parseLenient json)
            node, AstValidate.validate node

        // The model's answer may contain structural slips (unknown node kinds, a condition written
        // as text...). Ask it to fix ITS OWN answer, at most twice; the user's pseudocode is untouched.
        let maxRetries = 2
        let rec settle (json: string) (attempt: int) =
            let node, problems = normalizeAndCheck json
            if not problems.IsEmpty && attempt < maxRetries then
                printfn "%s" (t "info.retry" "The model's answer has {0} structural problem(s); asking it to correct them (attempt {1} of {2})..." [ box problems.Length; box (attempt + 1); box maxRetries ])
                let refined = (OllamaClient.refineAsync pseudocode json problems).GetAwaiter().GetResult()
                settle refined (attempt + 1)
            else json, node, problems

        let cached =
            if opts.NoCache || not (String.IsNullOrEmpty(replayPath)) then None
            else LlmCache.tryRead opts.CacheDir cacheKey
            |> Option.filter (fun c -> (snd (normalizeAndCheck c)).IsEmpty)   // never trust an invalid cached answer

        let rawJson, node, problems, fromLlm =
            if not (String.IsNullOrEmpty(replayPath)) then
                printfn "%s" (t "info.replay" "Replaying JSON from {0} (no Ollama call)..." [ box replayPath ])
                let json = File.ReadAllText(replayPath, Encoding.UTF8)
                let n, p = normalizeAndCheck json
                json, n, p, false
            else
                match cached with
                | Some c ->
                    printfn "%s" (t "info.cacheHit" "LLM cache: reused (no Ollama call)." [])
                    let n, p = normalizeAndCheck c
                    c, n, p, false
                | None ->
                    printfn "%s" (t "info.analysing" "Analysing the pseudocode (Ollama)..." [])
                    let first = (OllamaClient.parseAsync pseudocode).GetAwaiter().GetResult()
                    let json, n, p = settle first 0
                    json, n, p, true

        let normalizedJson = node.ToJsonString()
        if Environment.GetEnvironmentVariable("PSEUDONET_DEBUG") = "1" then
            printfn "── Raw JSON ──\n%s\n" rawJson
            printfn "── Normalized JSON ──\n%s\n" normalizedJson
        if not problems.IsEmpty then
            eprintfn "%s: warning PSEUDO007: %s" file (t "warn.invalidAst" "the model's answer still has structural problems: {0}" [ box (String.Join(" | ", problems)) ])
        let program = Deserializer.parseProgram normalizedJson

        // Only cache a response that is structurally sound AND produced a usable AST:
        // a bad generation is retried at the next build.
        if fromLlm && problems.IsEmpty && not opts.NoCache then
            try LlmCache.write opts.CacheDir cacheKey rawJson
            with ex ->
                eprintfn "%s: warning PSEUDO003: %s" file (t "warn.cacheNotWritten" "cache not written ({0})" [ box ex.Message ])

        let projectType = Ast.detectProjectType program
        printfn "%s" (t "info.projectType" "Detected project type: {0}" [ box (sprintf "%A" projectType) ])

        match opts.ExpectedType, projectType with
        | Some "console", Ast.Pseudraw ->
            raise (ProjectTypeMismatch (t "err.mismatchConsole" "this program describes a window or drawing: use a PseudrawCode project (or remove those instructions)." []))
        | Some "window", Ast.PseudoConsole ->
            raise (ProjectTypeMismatch (t "err.mismatchWindow" "this program describes neither a window nor drawing: use a PseudoCode project, or describe a window (size, title, drawing...)." []))
        | _ -> ()

        let body =
            match projectType with
            | Ast.PseudoConsole -> Backend.generate program
            | Ast.Pseudraw      -> PseudrawBackend.generate program
        let code = headerPrefix + srcHash + "\n" + body

        let dir = Path.GetDirectoryName(Path.GetFullPath(outFile))
        if not (String.IsNullOrEmpty(dir)) then Directory.CreateDirectory(dir) |> ignore
        if opts.EmitCSharp && File.Exists(outFile) then
            File.Copy(outFile, outFile + ".bak", true)
            printfn "%s" (t "info.backup" "Previous C# saved: {0}.bak" [ box outFile ])
        File.WriteAllText(outFile, code, Encoding.UTF8)
        printfn "%s" (t "info.generated" "Generated code: {0}" [ box outFile ])

        match opts.CopyTo with
        | Some p ->
            File.WriteAllText(p, code, Encoding.UTF8)
            printfn "%s" (t "info.copied" "Copied to: {0}" [ box p ])
        | None -> ()

        if opts.Print then
            printfn ""
            printfn "%s" code
        0

[<EntryPoint>]
let main argv =
    Console.OutputEncoding <- UTF8Encoding(false)
    Console.InputEncoding <- UTF8Encoding(false)
    setLanguage (detectLanguage ())
    applyLangOption argv
    match parseArgs argv with
    | Error msg ->
        eprintfn "PseudoNet.Compiler: error PSEUDO000: %s" msg
        eprintfn "%s" (t "usage" "Usage: PseudoNet.Compiler <file.pseudo> [--output f.cs] [--emit-csharp] [--cache-dir d] [--no-cache] [--copy-to f.cs] [--project-type console|window] [--lang xx] [--print] | --check-ollama" [])
        1
    | Ok opts when opts.CheckOllama ->
        match (OllamaClient.checkAvailableAsync ()).GetAwaiter().GetResult() with
        | None -> printfn "%s" (t "info.ollamaReady" "Ollama is ready (model {0})." [ box OllamaClient.modelName ]); 0
        | Some problem -> eprintfn "PseudoNet.Compiler: error PSEUDO002: %s" problem; 2
    | Ok { Input = None } ->
        eprintfn "PseudoNet.Compiler: error PSEUDO000: %s" (t "err.noInput" "no .pseudo file given" [])
        1
    | Ok ({ Input = Some file } as opts) ->
        if not (File.Exists(file)) then
            compileError file "PSEUDO000" (t "err.fileNotFound" "file not found" [])
            1
        elif opts.PrintCacheKey then
            printfn "%s" (LlmCache.keyFor OllamaClient.fingerprint (File.ReadAllText(file, Encoding.UTF8)))
            0
        else
            try
                compile opts file
            with
            | OllamaClient.OllamaUnavailable msg ->
                compileError file "PSEUDO002" msg
                2
            | ProjectTypeMismatch msg ->
                compileError file "PSEUDO004" msg
                1
            | ex ->
                compileError file "PSEUDO001" ex.Message
                1
