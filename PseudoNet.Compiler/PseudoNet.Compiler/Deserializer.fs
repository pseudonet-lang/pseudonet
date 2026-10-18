module Deserializer

open System.Text.Json
open Ast

// ── Helpers ──────────────────────────────────────────────────────────────────

let private str (node: JsonElement) (key: string) =
    match node.TryGetProperty(key) with
    | true, v -> v.GetString() |> Option.ofObj |> Option.defaultValue ""
    | _ -> ""

let private arr (node: JsonElement) (key: string) =
    match node.TryGetProperty(key) with
    | true, v when v.ValueKind = JsonValueKind.Array -> v.EnumerateArray() |> Seq.toList
    | _ -> []

let private tryArr (node: JsonElement) (key: string) =
    match node.TryGetProperty(key) with
    | true, v when v.ValueKind = JsonValueKind.Array -> Some (v.EnumerateArray() |> Seq.toList)
    | _ -> None

let private kind (node: JsonElement) = str node "kind"

let private parseColorSpec (node: JsonElement) : ColorSpec =
    match node.TryGetProperty("name") with
    | true, v when v.ValueKind = JsonValueKind.String -> ColorName (v.GetString())
    | _ ->
        match node.TryGetProperty("r"), node.TryGetProperty("g"), node.TryGetProperty("b") with
        | (true, r), (true, g), (true, b) ->
            let a =
                match node.TryGetProperty("a") with
                | true, av when av.ValueKind <> JsonValueKind.Null -> Some (av.GetInt32())
                | _ -> None
            ColorRgb (r.GetInt32(), g.GetInt32(), b.GetInt32(), a)
        | _ -> ColorName "white"

let private tryParseColorSpec (node: JsonElement) (key: string) : ColorSpec option =
    match node.TryGetProperty(key) with
    | true, v when v.ValueKind <> JsonValueKind.Null -> Some (parseColorSpec v)
    | _ -> None

// ── Expressions ──────────────────────────────────────────────────────────────

let rec parseExpr (node: JsonElement) : ExprNode =
    // Le LLM émet parfois un scalaire JSON brut (ex: "x": 50) au lieu d'un
    // nœud {kind:"Literal", value:...} — on le traite directement comme littéral.
    if node.ValueKind <> JsonValueKind.Object then
        match node.ValueKind with
        | JsonValueKind.Number ->
            if node.TryGetInt32(ref 0) then Literal (LitInt (node.GetInt32()))
            else Literal (LitFloat (node.GetDouble()))
        | JsonValueKind.String ->
            // Une chaîne brute qui ressemble à un identifiant valide ("nx", "snake")
            // désigne quasi toujours une variable référencée sans le nœud
            // Identifier attendu. Restreint aux tokens commençant par une
            // minuscule : les variables de ce corpus sont toujours nommées
            // ainsi (nx, headX, appleX...), alors qu'un mot commençant par une
            // majuscule ("Score", "Next") est presque toujours un libellé
            // textuel voulu comme littéral, pas une référence de variable.
            let s = node.GetString()
            if System.Text.RegularExpressions.Regex.IsMatch(s, "^[a-z_][A-Za-z0-9_]*$")
            then Identifier s
            else Literal (LitString s)
        | JsonValueKind.True   -> Literal (LitBool true)
        | JsonValueKind.False  -> Literal (LitBool false)
        | _ -> Literal (LitString "")
    else
    match kind node with
    | "Literal" ->
        match node.TryGetProperty("value") with
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.Number ->
                if v.TryGetInt32(ref 0) then Literal (LitInt (v.GetInt32()))
                else Literal (LitFloat (v.GetDouble()))
            | JsonValueKind.True  -> Literal (LitBool true)
            | JsonValueKind.False -> Literal (LitBool false)
            | _                   -> Literal (LitString (v.GetString()))
        | _ -> Literal (LitString "")

    | "Identifier" ->
        Identifier (str node "name")

    | "BinaryOp" ->
        let op    = str node "op"
        let left  = node.GetProperty("left")  |> parseExpr
        let right = node.GetProperty("right") |> parseExpr
        BinaryOp (op, left, right)

    | "UnaryOp" ->
        let op      = str node "op"
        let operand = node.GetProperty("operand") |> parseExpr
        UnaryOp (op, operand)

    | "PropertyAccess" ->
        PropertyAccess (str node "obj", str node "property")

    | "ArrayAccess" ->
        let index = node.GetProperty("index") |> parseExpr
        ArrayAccess (str node "array", index)

    | "FunctionCallExpr" ->
        let args = arr node "args" |> List.map parseExpr
        FunctionCallExpr (str node "name", args)

    // Le LLM utilise parfois "FunctionCallStmt" même en position d'expression
    // (ex: "fell = move piece down") — n'importe quel nœud qui atteint parseExpr
    // est par construction attendu comme une expression, donc c'est sans
    // ambiguïté de le traiter comme un appel de fonction expression.
    | "FunctionCallStmt" ->
        let args = arr node "args" |> List.map parseExpr
        FunctionCallExpr (str node "name", args)

    | "NewList" -> NewList

    | "RandomNumber" ->
        let min_ = node.GetProperty("min") |> parseExpr
        let max_ = node.GetProperty("max") |> parseExpr
        RandomNumber (min_, max_)

    | "Read" ->
        let prompt =
            match node.TryGetProperty("prompt") with
            | true, v when v.ValueKind <> JsonValueKind.Null -> Some (parseExpr v)
            | _ -> None
        ReadExpr prompt

    // ── Pseudraw — expressions ────────────────────────────────────────────────

    | "PseudrawKeyPressed" ->
        let key = str node "key"
        let tapped =
            match node.TryGetProperty("tapped") with
            | true, v -> v.GetBoolean()
            | _ -> false
        PseudrawKeyPressed (key, tapped)

    | "PseudrawPoint" ->
        let x = node.GetProperty("x") |> parseExpr
        let y = node.GetProperty("y") |> parseExpr
        PseudrawPoint (x, y)

    | "PseudrawRandomPoint" ->
        let x1 = node.GetProperty("x1") |> parseExpr
        let y1 = node.GetProperty("y1") |> parseExpr
        let x2 = node.GetProperty("x2") |> parseExpr
        let y2 = node.GetProperty("y2") |> parseExpr
        PseudrawRandomPoint (x1, y1, x2, y2)

    | "PseudrawDistance" ->
        let from_ = node.GetProperty("from") |> parseExpr
        let to_   = node.GetProperty("to")   |> parseExpr
        PseudrawDistance (from_, to_)

    | "PseudrawOverlaps" ->
        let left  = node.GetProperty("left")  |> parseExpr
        let right = node.GetProperty("right") |> parseExpr
        PseudrawOverlaps (left, right)

    | "PseudrawIsInside" ->
        let point = node.GetProperty("point") |> parseExpr
        let rect  = node.GetProperty("rect")  |> parseExpr
        PseudrawIsInside (point, rect)

    | "PseudrawIsIn" ->
        let value      = node.GetProperty("value")      |> parseExpr
        let collection = node.GetProperty("collection") |> parseExpr
        PseudrawIsIn (value, collection)

    | other ->
        // Fallback — traite comme un identifiant inconnu
        Identifier $"[unknown expr: {other}]"


// ── Statements ───────────────────────────────────────────────────────────────

and parseStmt (node: JsonElement) : StmtNode =
    match kind node with
    | "Assign" ->
        let target = str node "target"
        let value  = node.GetProperty("value") |> parseExpr
        Assign (target, value)

    | "IndexedAssign" ->
        let target = str node "target"
        let index  = node.GetProperty("index") |> parseExpr
        let value  = node.GetProperty("value") |> parseExpr
        IndexedAssign (target, index, value)

    | "Print" ->
        let value = node.GetProperty("value") |> parseExpr
        Print value

    | "Read" ->
        let target = str node "target"
        let prompt =
            match node.TryGetProperty("prompt") with
            | true, v when v.ValueKind <> JsonValueKind.Null -> Some (parseExpr v)
            | _ -> None
        Read (target, prompt)

    | "Return" ->
        let value =
            match node.TryGetProperty("value") with
            | true, v when v.ValueKind <> JsonValueKind.Null -> Some (parseExpr v)
            | _ -> None
        Return value

    | "Break"    -> Break
    | "Continue" -> Continue

    | "Comment" ->
        let text =
            match node.TryGetProperty("text") with
            | true, v -> v.GetString()
            | _ ->
                match node.TryGetProperty("value") with
                | true, v -> v.GetString()
                | _ -> ""
        Comment (text |> Option.ofObj |> Option.defaultValue "")

    | "If" ->
        let condition  = node.GetProperty("condition") |> parseExpr
        let thenBranch = arr node "then" |> List.map parseStmt
        let elseBranch = tryArr node "else_" |> Option.map (List.map parseStmt)
        If (condition, thenBranch, elseBranch)

    | "WhileLoop" ->
        let condition = node.GetProperty("condition") |> parseExpr
        let body      = arr node "body" |> List.map parseStmt
        WhileLoop (condition, body)

    | "Loop" ->
        let variable = str node "variable"
        let from_    = node.GetProperty("from_") |> parseExpr
        let to_      = node.GetProperty("to")    |> parseExpr
        let step     =
            match node.TryGetProperty("step") with
            | true, v when v.ValueKind <> JsonValueKind.Null -> Some (parseExpr v)
            | _ -> None
        let body = arr node "body" |> List.map parseStmt
        Loop (variable, from_, to_, step, body)

    | "ForEach" ->
        let variable   = str node "variable"
        let collection = node.GetProperty("collection") |> parseExpr
        let body       = arr node "body" |> List.map parseStmt
        ForEach (variable, collection, body)

    | "MethodCall" ->
        let target =
            match node.TryGetProperty("target") with
            | true, v when v.ValueKind <> JsonValueKind.Null -> Some (v.GetString())
            | _ -> None
        let method_ = str node "method"
        let args    = arr node "args" |> List.map parseExpr
        MethodCall (target, method_, args)

    | "FunctionCallStmt" ->
        let args = arr node "args" |> List.map parseExpr
        FunctionCallStmt (str node "name", args)

    | "FunctionDecl" ->
        let name    = str node "name"
        let params_ = arr node "params" |> List.map (fun p -> p.GetString())
        let body    = arr node "body" |> List.map parseStmt
        FunctionDecl (name, params_, body)

    // ── Pseudraw — environnement / fenêtre ──────────────────────────────────────

    | "PseudrawWidth"  -> PseudrawWidth  (node.GetProperty("value") |> parseExpr)
    | "PseudrawHeight" -> PseudrawHeight (node.GetProperty("value") |> parseExpr)
    | "PseudrawTitle"  -> PseudrawTitle  (node.GetProperty("value") |> parseExpr)

    | "PseudrawBackground" ->
        PseudrawBackground (parseColorSpec (node.GetProperty("color")))

    | "PseudrawFont" ->
        PseudrawFont (str node "fontName")

    // ── Pseudraw — timer / boucle de jeu ────────────────────────────────────────

    | "PseudrawTick" ->
        let ms =
            match node.TryGetProperty("intervalMs") with
            | true, v -> v.GetInt32()
            | _ -> 100
        PseudrawTick ms

    | "PseudrawOnTick" ->
        PseudrawOnTick (arr node "body" |> List.map parseStmt)

    | "PseudrawPauseTick"  -> PseudrawPauseTick
    | "PseudrawResumeTick" -> PseudrawResumeTick
    | "PseudrawStop"       -> PseudrawStop

    // ── Pseudraw — dessin ────────────────────────────────────────────────────────

    | "PseudrawClear" ->
        let target =
            match node.TryGetProperty("target") with
            | true, v when v.ValueKind <> JsonValueKind.Null -> Some (v.GetString())
            | _ -> None
        PseudrawClear target

    | "PseudrawDrawLine" ->
        let x1        = node.GetProperty("x1") |> parseExpr
        let y1        = node.GetProperty("y1") |> parseExpr
        let x2        = node.GetProperty("x2") |> parseExpr
        let y2        = node.GetProperty("y2") |> parseExpr
        let color     = parseColorSpec (node.GetProperty("color"))
        let thickness =
            match node.TryGetProperty("thickness") with
            | true, v when v.ValueKind <> JsonValueKind.Null -> Some (parseExpr v)
            | _ -> None
        PseudrawDrawLine (x1, y1, x2, y2, color, thickness)

    | "PseudrawDrawRect" ->
        let x         = node.GetProperty("x") |> parseExpr
        let y         = node.GetProperty("y") |> parseExpr
        let w         = node.GetProperty("w") |> parseExpr
        let h         = node.GetProperty("h") |> parseExpr
        let color     = parseColorSpec (node.GetProperty("color"))
        let border    = tryParseColorSpec node "border"
        let filled    =
            match node.TryGetProperty("filled") with
            | true, v -> v.GetBoolean()
            | _ -> false
        let thickness =
            match node.TryGetProperty("thickness") with
            | true, v when v.ValueKind <> JsonValueKind.Null -> Some (parseExpr v)
            | _ -> None
        PseudrawDrawRect (x, y, w, h, color, border, filled, thickness)

    | "PseudrawDrawCircle" ->
        let x         = node.GetProperty("x") |> parseExpr
        let y         = node.GetProperty("y") |> parseExpr
        let radius    = node.GetProperty("radius") |> parseExpr
        let color     = parseColorSpec (node.GetProperty("color"))
        let border    = tryParseColorSpec node "border"
        let filled    =
            match node.TryGetProperty("filled") with
            | true, v -> v.GetBoolean()
            | _ -> false
        let thickness =
            match node.TryGetProperty("thickness") with
            | true, v when v.ValueKind <> JsonValueKind.Null -> Some (parseExpr v)
            | _ -> None
        PseudrawDrawCircle (x, y, radius, color, border, filled, thickness)

    | "PseudrawDrawPolygon" ->
        let color     = parseColorSpec (node.GetProperty("color"))
        let filled    =
            match node.TryGetProperty("filled") with
            | true, v -> v.GetBoolean()
            | _ -> false
        let thickness =
            match node.TryGetProperty("thickness") with
            | true, v when v.ValueKind <> JsonValueKind.Null -> Some (parseExpr v)
            | _ -> None
        let points =
            arr node "points"
            |> List.choose (fun p ->
                if p.ValueKind = JsonValueKind.Array then
                    match p.EnumerateArray() |> Seq.toList with
                    | [ px; py ] -> Some (parseExpr px, parseExpr py)
                    | _ -> None
                else None)
        PseudrawDrawPolygon (points, color, filled, thickness)

    | "PseudrawDrawPoint" ->
        let x     = node.GetProperty("x") |> parseExpr
        let y     = node.GetProperty("y") |> parseExpr
        let color = parseColorSpec (node.GetProperty("color"))
        PseudrawDrawPoint (x, y, color)

    | "PseudrawDrawText" ->
        let value    = node.GetProperty("value") |> parseExpr
        let x        = node.GetProperty("x") |> parseExpr
        let y        = node.GetProperty("y") |> parseExpr
        let color    = parseColorSpec (node.GetProperty("color"))
        let fontSize =
            match node.TryGetProperty("fontSize") with
            | true, v when v.ValueKind <> JsonValueKind.Null -> Some (parseExpr v)
            | _ -> None
        let fontName =
            match node.TryGetProperty("fontName") with
            | true, v when v.ValueKind = JsonValueKind.String -> Some (v.GetString())
            | _ -> None
        PseudrawDrawText (value, x, y, color, fontSize, fontName)

    | "PseudrawDrawObject" ->
        PseudrawDrawObject (str node "target")

    // ── Pseudraw — son ───────────────────────────────────────────────────────────

    | "PseudrawPlayTone" ->
        let frequency   = node.GetProperty("frequency")   |> parseExpr
        let durationMs  = node.GetProperty("durationMs")  |> parseExpr
        PseudrawPlayTone (frequency, durationMs)

    // ── Pseudraw — géométrie de jeu ────────────────────────────────────────────────

    | "PseudrawTetrominoCells" ->
        let shape    = node.GetProperty("shape")    |> parseExpr
        let rotation = node.GetProperty("rotation") |> parseExpr
        let x        = node.GetProperty("x")        |> parseExpr
        let y        = node.GetProperty("y")        |> parseExpr
        let target   = str node "target"
        PseudrawTetrominoCells (shape, rotation, x, y, target)

    | other ->
        Comment $"[unknown stmt: {other}]"


// ── Programme ────────────────────────────────────────────────────────────────

let parseProgram (json: string) : ProgramNode =
    let doc  = JsonDocument.Parse(json)
    let root = doc.RootElement

    let allStmts = arr root "algorithm" |> List.map parseStmt

    // Sépare les FunctionDecl du reste
    let functions =
        allStmts |> List.choose (function
            | FunctionDecl _ as f -> Some f
            | _ -> None)

    let algorithm =
        allStmts |> List.filter (function
            | FunctionDecl _ -> false
            | _ -> true)

    { Functions = functions; Algorithm = algorithm }