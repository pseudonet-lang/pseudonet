module AstNormalize

open System.Text.Json.Nodes

// ── Canonicalisation du JSON brut renvoyé par le LLM ────────────────────────
// Porte les règles de ast_schema.py (normalize_kinds) dans le monde .NET, plus
// les nouveaux alias Pseudraw. Opère sur l'arbre mutable JsonNode, en une seule
// passe récursive, AVANT la désérialisation typée (Deserializer.fs) qui, elle,
// n'attend plus qu'une forme canonique unique par champ.

/// Lit le JSON du modèle en acceptant les clés dupliquées (la dernière gagne). JsonNode.Parse (net8) lève une
/// exception sur {"kind":"If", ..., "kind":"If"}, ce que les petits modèles produisent parfois ; JsonDocument
/// les tolère, on reconstruit donc l'arbre mutable à partir de lui. Forme de la sortie du modèle, pas du pseudocode.
let parseLenient (json: string) : JsonNode =
    use doc = System.Text.Json.JsonDocument.Parse(json)
    let rec convert (e: System.Text.Json.JsonElement) : JsonNode =
        match e.ValueKind with
        | System.Text.Json.JsonValueKind.Object ->
            let o = JsonObject()
            for p in e.EnumerateObject() do
                o.[p.Name] <- convert p.Value
            o :> JsonNode
        | System.Text.Json.JsonValueKind.Array ->
            let a = JsonArray()
            for x in e.EnumerateArray() do
                a.Add(convert x)
            a :> JsonNode
        | System.Text.Json.JsonValueKind.Null -> null
        | _ -> JsonNode.Parse(e.GetRawText())
    convert doc.RootElement

/// Wrapper explicite sur la surcharge (string, byref<JsonNode>) — évite
/// l'ambiguïté de surcharge que le sucre syntaxique tuple provoque ici.
let private tryGetNode (o: JsonObject) (key: string) : JsonNode option =
    let mutable node : JsonNode = null
    if o.TryGetPropertyValue(key, &node) then Some node else None

let private tryGetObject (o: JsonObject) (key: string) : JsonObject option =
    match tryGetNode o key with
    | Some (:? JsonObject as jo) -> Some jo
    | _ -> None

let private tryGetArray (o: JsonObject) (key: string) : JsonArray option =
    match tryGetNode o key with
    | Some (:? JsonArray as ja) -> Some ja
    | _ -> None

let private tryGetString (o: JsonObject) (key: string) : string option =
    match tryGetNode o key with
    | Some (:? JsonValue as v) ->
        match v.TryGetValue<string>() with
        | true, s -> Some s
        | false, _ -> None
    | _ -> None

let private tryGetBool (o: JsonObject) (key: string) : bool option =
    match tryGetNode o key with
    | Some (:? JsonValue as v) ->
        match v.TryGetValue<bool>() with
        | true, b -> Some b
        | false, _ -> None
    | _ -> None

/// Renomme une clé si elle existe et que la clé cible n'est pas déjà occupée.
let private rename (o: JsonObject) (oldKey: string) (newKey: string) =
    if o.ContainsKey(oldKey) && not (o.ContainsKey(newKey)) then
        let v = o.[oldKey]
        o.Remove(oldKey) |> ignore
        o.[newKey] <- v

/// Remplace une valeur "chaîne brute" par { kind:"Literal", value:<chaîne> }.
let private wrapStringAsLiteral (o: JsonObject) (key: string) =
    match tryGetString o key with
    | Some s ->
        let lit = JsonObject()
        lit.["kind"] <- JsonValue.Create("Literal")
        lit.["value"] <- JsonValue.Create(s)
        o.Remove(key) |> ignore
        o.[key] <- lit
    | None -> ()

/// Remplace une valeur "chaîne brute" par { name:<chaîne> } (spécification couleur).
let private wrapStringAsColorName (o: JsonObject) (key: string) =
    match tryGetString o key with
    | Some s ->
        let c = JsonObject()
        c.["name"] <- JsonValue.Create(s)
        o.Remove(key) |> ignore
        o.[key] <- c
    | None -> ()

/// Verbes de collection réservés (déjà spéciaux-cassés par les deux backends
/// pour un vrai MethodCall) — utilisés pour réparer un FunctionCallStmt mal
/// formé que le LLM produit parfois à leur place. Vérifiés en préfixe (avec
/// espace) plutôt qu'en égalité stricte, car le LLM replie souvent toute la
/// phrase ("remove last element from snake") dans le seul champ "name".
let private collectionVerbPrefixes =
    [ "remove the first", "RemoveFirst"
      "remove first",     "RemoveFirst"
      "remove the last",  "RemoveLast"
      "remove last",      "RemoveLast"
      "insert first",     "InsertFirst"
      "prepend",          "InsertFirst"
      "append",           "Add"
      "add",              "Add" ]

/// Extrait un nom de cible depuis la fin d'un texte libre du type
/// "remove last element from snake" — utilisé quand le LLM replie toute la
/// phrase dans "name" au lieu d'isoler une cible dans "args".
let private tryExtractTrailingTarget (text: string) : string option =
    let lower = text.ToLowerInvariant()
    [ " from "; " into "; " to "; " of " ]
    |> List.tryPick (fun connector ->
        let idx = lower.LastIndexOf(connector)
        if idx < 0 then None
        else
            let tail = text.Substring(idx + connector.Length).Trim()
            if tail.Length > 0
               && tail |> Seq.forall (fun ch -> System.Char.IsLetterOrDigit(ch) || ch = '_')
               && not (System.Char.IsDigit(tail.[0]))
            then Some tail
            else None)

let rec private normalizeNode (node: JsonNode) : unit =
    match node with
    | :? JsonObject as o -> normalizeObject o
    | :? JsonArray as a -> normalizeArray a
    | _ -> ()

and private normalizeArray (a: JsonArray) : unit =
    // Cas particulier : le LLM place parfois "intervalMs" directement sur le
    // nœud PseudrawOnTick au lieu d'émettre un PseudrawTick frère comme
    // demandé par le prompt — on le sépare ici, avant la récursion, car cela
    // doit insérer un nœud voisin dans le tableau (impossible depuis
    // normalizeObject seul, qui n'a pas connaissance du tableau parent).
    let mutable i = 0
    while i < a.Count do
        match a.[i] with
        | :? JsonObject as o
                when (let k = tryGetString o "kind" |> Option.defaultValue "" in
                      k = "PseudrawOnTick" || k = "PseudrawOnTickNode") ->
            match tryGetNode o "intervalMs" with
            | Some ms ->
                o.Remove("intervalMs") |> ignore
                let tick = JsonObject()
                tick.["kind"] <- JsonValue.Create("PseudrawTick")
                tick.["intervalMs"] <- ms
                a.Insert(i, tick)
                i <- i + 1 // saute le nœud PseudrawTick qu'on vient d'insérer
            | None -> ()
        | _ -> ()
        i <- i + 1

    for item in a |> Seq.toList do
        match item with
        | null -> ()
        | n -> normalizeNode n

and private normalizeObject (o: JsonObject) : unit =
    // ── kind : strip du suffixe "Node" ───────────────────────────────────────
    let rawKind = tryGetString o "kind" |> Option.defaultValue ""
    let kind = if rawKind.EndsWith("Node") then rawKind.Substring(0, rawKind.Length - 4) else rawKind
    // Noms de type inventés par le modèle pour un appel de fonction : alias vers le type réel.
    let kind =
        match kind with
        | "Call" | "FunctionCall" | "CallStmt" | "CallStatement" -> "FunctionCallStmt"
        | k -> k
    if kind <> rawKind then o.["kind"] <- JsonValue.Create(kind)

    // Le LLM place parfois "then" (et "else"/"else_") directement sur le nœud
    // de condition lui-même (ex: {"kind":"PseudrawKeyPressed",...,"then":[...]})
    // au lieu de l'envelopper dans un vrai nœud If — on l'enveloppe ici : tout
    // ce qui n'est pas then/else/else_ devient la "condition".
    if kind <> "If" && o.ContainsKey("then") then
        let thenNode = tryGetNode o "then"
        let elseNode = tryGetNode o "else_" |> Option.orElse (tryGetNode o "else")
        let condKeys =
            o |> Seq.toList
            |> List.map (fun kv -> kv.Key)
            |> List.filter (fun k -> k <> "then" && k <> "else" && k <> "else_")
        let condObj = JsonObject()
        for k in condKeys do
            let v = o.[k]
            o.Remove(k) |> ignore
            condObj.[k] <- v
        o.Remove("then") |> ignore
        o.Remove("else") |> ignore
        o.Remove("else_") |> ignore
        o.["kind"]      <- JsonValue.Create("If")
        o.["condition"] <- condObj
        match thenNode with Some t -> o.["then"] <- t | None -> ()
        match elseNode with Some e -> o.["else_"] <- e | None -> ()

    // Le LLM enveloppe parfois un vrai nœud d'expression dans un faux Literal
    // (ex: {"kind":"Literal","value":{"kind":"PseudrawPoint",...}}) — on déballe
    // en remplaçant ce nœud par le contenu de son "value".
    if kind = "Literal" then
        match tryGetObject o "value" with
        | Some inner when inner.ContainsKey("kind") ->
            let props = inner |> Seq.toList |> List.map (fun kv -> kv.Key, kv.Value)
            o.Remove("value") |> ignore
            o.Clear()
            for (k, v) in props do
                inner.Remove(k) |> ignore
                o.[k] <- v
        | _ -> ()

    // Un point écrit comme littéral tableau — {"kind":"Literal","value":[10,10]} — au lieu
    // d'un PseudrawPoint. Un littéral tableau de deux nombres n'a aucun autre sens dans cet
    // AST : on le replie sur un point (forme du JSON du modèle, jamais le texte utilisateur).
    if kind = "Literal" then
        match tryGetArray o "value" with
        | Some arr when arr.Count = 2 && arr |> Seq.forall (fun n -> n :? JsonValue && (n :?> JsonValue).GetValueKind() = System.Text.Json.JsonValueKind.Number) ->
            let x = arr.[0].DeepClone()
            let y = arr.[1].DeepClone()
            o.Clear()
            o.["kind"] <- JsonValue.Create("PseudrawPoint")
            o.["x"] <- x
            o.["y"] <- y
        | _ -> ()

    // UnaryOp : le champ de l'opérande s'appelle parfois "expr" ou "value" au
    // lieu de "operand".
    if kind = "UnaryOp" then
        rename o "expr" "operand"
        rename o "value" "operand"
        rename o "arg" "operand"

    // Le LLM représente parfois une négation ("not X") comme un BinaryOp à un
    // seul opérande (op="!" ou "not") au lieu d'un vrai UnaryOp — l'opérande se
    // retrouve tantôt dans "left" (sans "right"), tantôt dans "right" (sans
    // "left"). Un vrai BinaryOp binaire a toujours les deux, donc c'est sans
    // ambiguïté dans les deux cas.
    if kind = "BinaryOp" then
        let opVal = tryGetString o "op" |> Option.defaultValue ""
        if opVal = "!" || opVal.ToLowerInvariant() = "not" then
            let operandKey =
                if o.ContainsKey("operand") then Some "operand"   // {"kind":"BinaryOp","op":"not","operand":{...}}
                elif not (o.ContainsKey("right")) && o.ContainsKey("left") then Some "left"
                elif not (o.ContainsKey("left")) && o.ContainsKey("right") then Some "right"
                else None
            match operandKey |> Option.bind (tryGetNode o) with
            | Some operandNode ->
                o.Remove("left") |> ignore
                o.Remove("right") |> ignore
                o.Remove("operand") |> ignore
                o.Remove("op") |> ignore
                o.["kind"]    <- JsonValue.Create("UnaryOp")
                o.["op"]      <- JsonValue.Create("not")
                o.["operand"] <- operandNode
            | None -> ()

    // L'opérateur reste est parfois écrit "mod" / "modulo" (hors de la liste d'opérateurs enseignée au
    // modèle) : simple alias de forme vers "%", sans lien avec les mots du pseudocode de l'utilisateur.
    if kind = "BinaryOp" then
        match tryGetString o "op" with
        | Some op when List.contains (op.Trim().ToLowerInvariant()) [ "mod"; "modulo" ] ->
            o.["op"] <- JsonValue.Create("%")
        | _ -> ()

    // Le LLM utilise parfois une affectation composée ("score += 1") écrite
    // comme un BinaryOp brut directement en position de statement, au lieu de
    // {"kind":"Assign","target":"score","value":{"kind":"BinaryOp","op":"+",...}}.
    // Un vrai BinaryOp d'expression n'a jamais un opérateur composé, donc c'est
    // sans ambiguïté : on le réécrit en Assign.
    if kind = "BinaryOp" then
        let op = tryGetString o "op" |> Option.defaultValue ""
        if List.contains op [ "+="; "-="; "*="; "/="; "%=" ] then
            match tryGetObject o "left" with
            | Some leftObj when (tryGetString leftObj "kind" |> Option.defaultValue "") = "Identifier" ->
                let targetName = tryGetString leftObj "name" |> Option.defaultValue ""
                let pureOp = op.Substring(0, op.Length - 1)
                let rightNode = tryGetNode o "right"
                o.Clear()
                let valueNode = JsonObject()
                valueNode.["kind"] <- JsonValue.Create("BinaryOp")
                valueNode.["op"]   <- JsonValue.Create(pureOp)
                valueNode.["left"] <- leftObj
                match rightNode with
                | Some r -> valueNode.["right"] <- r
                | None -> ()
                o.["kind"]   <- JsonValue.Create("Assign")
                o.["target"] <- JsonValue.Create(targetName)
                o.["value"]  <- valueNode
            | _ -> ()

    // Le LLM écrit parfois un accès de propriété comme un identifiant à point
    // ("seg.x") au lieu du nœud PropertyAccess attendu — un vrai identifiant
    // ne contient jamais de point dans ce langage, donc c'est sans ambiguïté.
    if kind = "Identifier" then
        match tryGetString o "name" with
        | Some name when name.Contains(".") ->
            let idx = name.IndexOf('.')
            let objName  = name.Substring(0, idx)
            let propName = name.Substring(idx + 1)
            o.Remove("name") |> ignore
            o.["kind"]     <- JsonValue.Create("PropertyAccess")
            o.["obj"]      <- JsonValue.Create(objName)
            o.["property"] <- JsonValue.Create(propName)
        | _ -> ()

    // ── Aliases communs (portés de ast_schema.py) ────────────────────────────
    match kind with
    | "Program" ->
        rename o "body" "algorithm"
        rename o "statements" "algorithm"
        rename o "stmts" "algorithm"
    | "FunctionDecl" | "Loop" | "WhileLoop" | "ForEach" | "PseudrawOnTick" ->
        rename o "algorithm" "body"
    | "If" ->
        rename o "algorithm" "then"
        rename o "else" "else_"
    | _ -> ()

    if kind = "Loop" then rename o "from" "from_"

    wrapStringAsLiteral o "prompt"
    rename o "operator" "op"

    // params : liste d'objets → liste de strings
    match tryGetArray o "params" with
    | Some arr ->
        let normalized = JsonArray()
        for item in arr |> Seq.toList do
            match item with
            | :? JsonValue as v ->
                match v.TryGetValue<string>() with
                | true, s -> normalized.Add(JsonValue.Create(s))
                | false, _ -> ()
            | :? JsonObject as po ->
                normalized.Add(JsonValue.Create(tryGetString po "name" |> Option.defaultValue ""))
            | _ -> ()
        o.Remove("params") |> ignore
        o.["params"] <- normalized
    | None -> ()

    // ArrayAccess.array : objet Identifier → string
    if kind = "ArrayAccess" then
        match tryGetObject o "array" with
        | Some arrObj when (tryGetString arrObj "kind" |> Option.defaultValue "") = "Identifier" ->
            let name = tryGetString arrObj "name" |> Option.defaultValue ""
            o.Remove("array") |> ignore
            o.["array"] <- JsonValue.Create(name)
        | _ -> ()

    // Assign dont la target est un ArrayAccess → IndexedAssign
    if kind = "Assign" then
        match tryGetObject o "target" with
        | Some targetObj when (tryGetString targetObj "kind" |> Option.defaultValue "") = "ArrayAccess" ->
            let arrName =
                match tryGetString targetObj "array" with
                | Some s -> s
                | None ->
                    match tryGetObject targetObj "array" with
                    | Some ao -> tryGetString ao "name" |> Option.defaultValue ""
                    | None -> ""
            let indexNode =
                match tryGetNode targetObj "index" with
                | Some idx -> targetObj.Remove("index") |> ignore; idx
                | None -> null
            o.Remove("target") |> ignore
            o.["target"] <- JsonValue.Create(arrName)
            if not (isNull indexNode) then o.["index"] <- indexNode
            o.["kind"] <- JsonValue.Create("IndexedAssign")
        | _ -> ()

    // FunctionCallStmt utilisant un verbe de collection réservé (add, remove
    // first/last, insert first, ...) → MethodCall. Le LLM confond souvent les
    // deux formes ; la cible est retrouvée soit dans le dernier argument (s'il
    // s'agit d'un identifiant simple), soit à la fin du texte libre de "name"
    // (ex: "remove last element from snake").
    if kind = "FunctionCallStmt" then
        match tryGetString o "name" with
        | Some fname ->
            let lowerName = fname.Trim().ToLowerInvariant()
            match collectionVerbPrefixes |> List.tryFind (fun (prefix, _) -> lowerName.StartsWith(prefix)) with
            | Some (_, methodName) ->
                let argsArr = tryGetArray o "args" |> Option.defaultValue (JsonArray())
                let targetFromArgs =
                    if argsArr.Count >= 1 then
                        match argsArr.[argsArr.Count - 1] with
                        | :? JsonObject as lastObj
                                when (tryGetString lastObj "kind" |> Option.defaultValue "") = "Identifier" ->
                            let t = tryGetString lastObj "name" |> Option.defaultValue ""
                            argsArr.RemoveAt(argsArr.Count - 1)
                            Some t
                        | _ -> None
                    else None
                match targetFromArgs |> Option.orElse (tryExtractTrailingTarget fname) with
                | Some targetName ->
                    o.Remove("name") |> ignore
                    o.Remove("args") |> ignore
                    o.["kind"]   <- JsonValue.Create("MethodCall")
                    o.["target"] <- JsonValue.Create(targetName)
                    o.["method"] <- JsonValue.Create(methodName)
                    o.["args"]   <- argsArr
                | None -> ()
            | None -> ()
        | None -> ()

    // Le LLM appelle parfois un primitive interne (reconnue par son nom) comme
    // un simple FunctionCallStmt avec des arguments positionnels, au lieu du
    // nœud structuré enseigné dans le prompt (ex: PseudrawTetrominoCells).
    if kind = "FunctionCallStmt" then
        match tryGetString o "name" with
        | Some fname when fname.Trim().ToLowerInvariant().Replace(" ", "") = "pseudrawtetrominocells" ->
            match tryGetArray o "args" with
            | Some argsArr when argsArr.Count = 5 ->
                let shape      = argsArr.[0]
                let rotation   = argsArr.[1]
                let x          = argsArr.[2]
                let y          = argsArr.[3]
                let targetNode = argsArr.[4]
                argsArr.Clear()
                let targetName =
                    match targetNode with
                    | :? JsonObject as tobj when (tryGetString tobj "kind" |> Option.defaultValue "") = "Identifier" ->
                        tryGetString tobj "name" |> Option.defaultValue ""
                    | :? JsonValue as tv ->
                        match tv.TryGetValue<string>() with true, s -> s | false, _ -> ""
                    | _ -> ""
                o.Remove("name") |> ignore
                o.Remove("args") |> ignore
                o.["kind"]     <- JsonValue.Create("PseudrawTetrominoCells")
                o.["shape"]    <- shape
                o.["rotation"] <- rotation
                o.["x"]        <- x
                o.["y"]        <- y
                o.["target"]   <- JsonValue.Create(targetName)
            // Sur les générations longues, le modèle laisse parfois tomber un des
            // trois arguments centraux quand rotation/x/y valent tous 0 — vu deux
            // fois identiquement sur un Tétris en français ("forme, 0, 0, cible"
            // au lieu de "forme, rotation, 0, 0, cible"). Repli raisonnable : sur
            // 4 arguments, la rotation est celle qui manque le plus souvent (cas
            // le plus fréquent : "placée en (x, y)" sans rotation explicite) →
            // on l'assume à 0 plutôt que de laisser un appel à une méthode
            // inexistante (échec de compilation garanti).
            | Some argsArr when argsArr.Count = 4 ->
                let shape      = argsArr.[0]
                let x          = argsArr.[1]
                let y          = argsArr.[2]
                let targetNode = argsArr.[3]
                argsArr.Clear()
                let targetName =
                    match targetNode with
                    | :? JsonObject as tobj when (tryGetString tobj "kind" |> Option.defaultValue "") = "Identifier" ->
                        tryGetString tobj "name" |> Option.defaultValue ""
                    | :? JsonValue as tv ->
                        match tv.TryGetValue<string>() with true, s -> s | false, _ -> ""
                    | _ -> ""
                o.Remove("name") |> ignore
                o.Remove("args") |> ignore
                o.["kind"]     <- JsonValue.Create("PseudrawTetrominoCells")
                o.["shape"]    <- shape
                o.["rotation"] <- JsonValue.Create(0)
                o.["x"]        <- x
                o.["y"]        <- y
                o.["target"]   <- JsonValue.Create(targetName)
            | _ -> ()
        | _ -> ()

    // PropertyAccess : le LLM écrit parfois "object" au lieu de "obj", et/ou
    // un objet imbriqué (ex: {"kind":"Identifier","name":"seg"}) au lieu d'un
    // simple nom — même traitement que ArrayAccess.array plus haut.
    if kind = "PropertyAccess" then
        rename o "object" "obj"
        match tryGetObject o "obj" with
        | Some objObj ->
            let name = tryGetString objObj "name" |> Option.defaultValue ""
            o.Remove("obj") |> ignore
            o.["obj"] <- JsonValue.Create(name)
        | None -> ()

    // ── Pseudraw ─────────────────────────────────────────────────────────────

    rename o "colorName" "color"
    wrapStringAsColorName o "color"
    wrapStringAsColorName o "border"

    // Champs à valeur nommée enseignés au modèle (un 7B ignore souvent un
    // booléen nu) : press="held"|"just_pressed", style="filled"|"outline".
    // On les replie ici sur les booléens de l'AST (tapped / filled).
    if kind = "PseudrawKeyPressed" then
        match tryGetString o "press" with
        | Some press ->
            let p = press.Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_")
            let isTapped = (p = "just_pressed" || p = "justpressed" || p = "tapped" || p = "once" || p = "single")
            o.["tapped"] <- JsonValue.Create(isTapped)
            o.Remove("press") |> ignore
        | None -> ()

    if kind = "PseudrawDrawRect" || kind = "PseudrawDrawCircle" || kind = "PseudrawDrawPolygon" then
        match tryGetString o "style" with
        | Some style ->
            let s = style.Trim().ToLowerInvariant()
            o.["filled"] <- JsonValue.Create(s = "filled" || s = "fill" || s = "solid")
            o.Remove("style") |> ignore
        | None -> ()

    if kind = "PseudrawKeyPressed" then
        rename o "keyName" "key"
        rename o "keyCode" "key"
        rename o "justPressed" "tapped"
        rename o "isTapped" "tapped"
        if not (o.ContainsKey("tapped")) then
            match tryGetBool o "held" with
            | Some held -> o.["tapped"] <- JsonValue.Create(not held)
            | None -> ()
        o.Remove("held") |> ignore

    if kind = "PseudrawWidth" || kind = "PseudrawHeight" || kind = "PseudrawTitle" then
        rename o "width" "value"
        rename o "height" "value"
        rename o "title" "value"

    if kind = "PseudrawTick" then
        rename o "interval" "intervalMs"
        rename o "ms" "intervalMs"

    if kind = "PseudrawDrawRect" || kind = "PseudrawDrawCircle" then
        rename o "fill" "filled"
        rename o "isFilled" "filled"
        rename o "solid" "filled"
        rename o "outline" "border"
        rename o "stroke" "border"
        rename o "lineWidth" "thickness"
        rename o "strokeWidth" "thickness"

    if kind = "PseudrawDrawText" then
        rename o "text" "value"
        rename o "content" "value"
        rename o "size" "fontSize"
        rename o "font" "fontName"

    if kind = "PseudrawClear" then
        rename o "list" "target"
        rename o "collection" "target"

    if kind = "PseudrawDrawPolygon" then
        rename o "vertices" "points"

    if kind = "PseudrawPlayTone" then
        rename o "freq" "frequency"
        rename o "pitch" "frequency"
        rename o "duration" "durationMs"
        rename o "ms" "durationMs"
        rename o "length" "durationMs"

    if kind = "PseudrawTetrominoCells" then
        rename o "piece" "shape"
        rename o "pieceType" "shape"
        rename o "rot" "rotation"
        rename o "originX" "x"
        rename o "originY" "y"
        rename o "list" "target"
        rename o "collection" "target"
        match tryGetObject o "target" with
        | Some targetObj ->
            let name = tryGetString targetObj "name" |> Option.defaultValue ""
            o.Remove("target") |> ignore
            o.["target"] <- JsonValue.Create(name)
        | None -> ()
        // "shape" peut être donné comme lettre (I,O,T,S,Z,J,L) plutôt que 0-6.
        let shapeLetters = dict [ "i",0; "o",1; "t",2; "s",3; "z",4; "j",5; "l",6 ]
        match tryGetString o "shape" with
        | Some s ->
            match shapeLetters.TryGetValue(s.Trim().ToLowerInvariant()) with
            | true, idx ->
                o.Remove("shape") |> ignore
                o.["shape"] <- JsonValue.Create(idx)
            | false, _ -> ()
        | None -> ()

    // ── Récursion dans les enfants restants ──────────────────────────────────
    for kv in o |> Seq.toList do
        match kv.Value with
        | null -> ()
        | n -> normalizeNode n

let normalize (root: JsonNode) : JsonNode =
    normalizeNode root
    root
