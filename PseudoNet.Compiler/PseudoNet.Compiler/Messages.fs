module Messages

open System
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Text

// Messages du compilateur. L'ANGLAIS est dans le code (valeur par défaut : toujours disponible, même si
// les fichiers manquent) ; les traductions vivent dans des fichiers JSON externes, comme les tables de
// couleurs et de touches : `Messages/messages.<langue>.json` à côté de l'exécutable, plus les fichiers
// listés dans PSEUDONET_MESSAGES (séparés par `;`, le nom du fichier donne la langue). Format :
// { "clé": "texte avec {0} {1}" }. Une clé absente retombe sur l'anglais.
//
// Langue : --lang xx  >  PSEUDONET_LANG  >  langue de l'interface du système.

let private tables = lazy (
    let all = Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
    let loadFile (path: string) =
        try
            // messages.fr.json -> "fr" ; messages.pt-br.json -> "pt-br"
            let stem = Path.GetFileNameWithoutExtension(path)
            let lang = (stem.Substring(stem.IndexOf('.') + 1)).ToLowerInvariant()
            use doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8))
            let table =
                match all.TryGetValue(lang) with
                | true, t -> t
                | false, _ -> let t = Dictionary<string, string>() in all.[lang] <- t; t
            for p in doc.RootElement.EnumerateObject() do
                if p.Value.ValueKind = System.Text.Json.JsonValueKind.String then
                    table.[p.Name] <- p.Value.GetString()
        with _ -> ()
    let dir = Path.Combine(AppContext.BaseDirectory, "Messages")
    if Directory.Exists(dir) then
        Directory.GetFiles(dir, "messages.*.json") |> Array.sort |> Array.iter loadFile
    match Environment.GetEnvironmentVariable("PSEUDONET_MESSAGES") with
    | null | "" -> ()
    | extra -> extra.Split(';', StringSplitOptions.RemoveEmptyEntries) |> Array.iter (fun f -> loadFile (f.Trim()))
    all)

let mutable private language = "en"

let detectLanguage () : string =
    match Environment.GetEnvironmentVariable("PSEUDONET_LANG") with
    | null | "" -> CultureInfo.CurrentUICulture.Name.ToLowerInvariant()   // ex. "fr-fr"
    | l -> l.Trim().ToLowerInvariant()

let setLanguage (lang: string) = language <- lang.Trim().ToLowerInvariant()

let private lookup (key: string) : string option =
    // "fr-fr" -> essaie "fr-fr" puis "fr"
    let candidates = [ language; (if language.Contains('-') then language.Substring(0, language.IndexOf('-')) else language) ]
    candidates
    |> List.tryPick (fun l ->
        match tables.Value.TryGetValue(l) with
        | true, t -> (match t.TryGetValue(key) with | true, s -> Some s | false, _ -> None)
        | false, _ -> None)

/// t "clé" "English text with {0}" [ box arg ]
let t (key: string) (english: string) (args: obj list) : string =
    let template = lookup key |> Option.defaultValue english
    try String.Format(template, List.toArray args)
    with _ -> String.Format(english, List.toArray args)
