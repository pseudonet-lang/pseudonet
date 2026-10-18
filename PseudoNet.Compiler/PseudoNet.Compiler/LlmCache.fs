module LlmCache

open System
open System.IO
open System.Text
open System.Security.Cryptography

// Cache du JSON brut renvoyé par le LLM. Clé = SHA-256(empreinte du modèle/prompt,
// pseudocode). Les tables Colors/ et Keys/ n'y entrent PAS : elles ne servent
// qu'après le LLM (génération de code), donc les modifier ne doit pas rappeler Ollama.

let private normalizeEol (s: string) = s.Replace("\r\n", "\n")

let private sha256Hex (s: string) =
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant()

/// Hash du seul pseudocode : sert à savoir si un .cs émis est encore à jour.
let sourceHash (pseudocode: string) : string = sha256Hex (normalizeEol pseudocode)

let keyFor (fingerprint: string) (pseudocode: string) : string =
    sha256Hex (fingerprint + "|" + normalizeEol pseudocode)

let private entryPath (dir: string) (key: string) = Path.Combine(dir, key + ".json")

/// Une entrée tronquée ou corrompue est ignorée (donc régénérée), jamais utilisée.
let tryRead (dir: string) (key: string) : string option =
    let p = entryPath dir key
    if File.Exists(p) then
        try
            let text = File.ReadAllText(p, Encoding.UTF8)
            System.Text.Json.JsonDocument.Parse(text).Dispose()
            Some text
        with _ -> None
    else None

/// Écriture atomique (fichier temporaire puis renommage) : deux builds
/// concurrents ne peuvent pas laisser une entrée à moitié écrite.
let write (dir: string) (key: string) (json: string) : unit =
    Directory.CreateDirectory(dir) |> ignore
    let target = entryPath dir key
    let tmp = target + "." + Guid.NewGuid().ToString("N") + ".tmp"
    File.WriteAllText(tmp, json, Encoding.UTF8)
    File.Move(tmp, target, true)
