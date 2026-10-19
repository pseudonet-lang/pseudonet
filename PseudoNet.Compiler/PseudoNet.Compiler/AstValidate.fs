module AstValidate

open System.Text.Json.Nodes

// Contrôle STRUCTUREL du JSON renvoyé par le modèle (jamais du pseudocode de l'utilisateur) :
// chaque nœud est-il d'un type connu, à une place où ce type a un sens ? Une condition est-elle
// bien une expression et pas un bout de texte ? Les problèmes trouvés sont renvoyés au modèle
// pour qu'il corrige sa propre réponse (voir OllamaClient.refineAsync).
//
// Les deux ensembles ci-dessous doivent rester alignés avec Deserializer.parseStmt / parseExpr.

let private stmtKinds =
    set [ "Assign"; "IndexedAssign"; "Print"; "Read"; "Return"; "Break"; "Continue"; "Comment"
          "If"; "WhileLoop"; "Loop"; "ForEach"; "MethodCall"; "FunctionCallStmt"; "FunctionDecl"
          "PseudrawWidth"; "PseudrawHeight"; "PseudrawTitle"; "PseudrawBackground"; "PseudrawFont"
          "PseudrawTick"; "PseudrawOnTick"; "PseudrawPauseTick"; "PseudrawResumeTick"; "PseudrawStop"
          "PseudrawClear"; "PseudrawDrawLine"; "PseudrawDrawRect"; "PseudrawDrawCircle"
          "PseudrawDrawPolygon"; "PseudrawDrawPoint"; "PseudrawDrawText"; "PseudrawDrawObject"
          "PseudrawPlayTone"; "PseudrawTetrominoCells" ]

let private exprKinds =
    set [ "Literal"; "Identifier"; "BinaryOp"; "UnaryOp"; "PropertyAccess"; "ArrayAccess"
          "FunctionCallExpr"; "FunctionCallStmt"; "NewList"; "RandomNumber"; "Read"
          "PseudrawKeyPressed"; "PseudrawPoint"; "PseudrawRandomPoint"; "PseudrawDistance"
          "PseudrawOverlaps"; "PseudrawIsInside"; "PseudrawIsIn" ]

let private stmtListKeys = set [ "body"; "then"; "else_"; "algorithm" ]

let private kindOf (o: JsonObject) : string =
    match o.TryGetPropertyValue("kind") with
    | true, v when not (isNull v) && v.GetValueKind() = System.Text.Json.JsonValueKind.String -> v.GetValue<string>()
    | _ -> ""

/// Liste (au plus 8) de problèmes lisibles ; vide = JSON structurellement sain.
let validate (root: JsonNode) : string list =
    let problems = ResizeArray<string>()
    let add (s: string) = if problems.Count < 8 && not (problems.Contains s) then problems.Add s

    let rec visitStmt (n: JsonNode) (where: string) =
        match n with
        | :? JsonObject as o ->
            let k = kindOf o
            if k = "" then add (sprintf "%s: a statement has no \"kind\"" where)
            elif not (stmtKinds.Contains k) && exprKinds.Contains k then
                add (sprintf "%s: \"%s\" is an expression, not a statement - it cannot stand alone. If it was a condition, use it as the \"condition\" of an If node and keep the statements it guards in that If's \"then\" list" where k)
            elif not (stmtKinds.Contains k) then
                add (sprintf "%s: \"%s\" is not a valid statement kind" where k)
            children o k
        | _ -> add (sprintf "%s: a statement must be a JSON object" where)

    and visitExpr (n: JsonNode) (where: string) =
        match n with
        | :? JsonObject as o when o.ContainsKey("kind") ->
            let k = kindOf o
            if not (exprKinds.Contains k) then
                add (sprintf "%s: \"%s\" is not a valid expression kind (it cannot be used as a value or condition)" where k)
            children o k
        | _ -> ()   // scalaires nus, objets sans kind (couleurs...) : acceptés par le désérialiseur

    // Un résultat arithmétique (+ - * / %) n'est pas une condition : il faut le COMPARER (reste = 0, x > 3...).
    // Écrire "(n % 3) or (n % 5)" donne en C# un "int || int" invalide, ou un sens inversé.
    and arithmeticOp (n: JsonNode) : string option =
        match n with
        | :? JsonObject as c when kindOf c = "BinaryOp" ->
            match c.TryGetPropertyValue("op") with
            | true, v when not (isNull v) && v.GetValueKind() = System.Text.Json.JsonValueKind.String ->
                let op = v.GetValue<string>()
                if List.contains op [ "+"; "-"; "*"; "/"; "%" ] then Some op else None
            | _ -> None
        | _ -> None

    and checkLogicalOperands (o: JsonObject) (where: string) =
        match o.TryGetPropertyValue("op") with
        | true, v when not (isNull v) && v.GetValueKind() = System.Text.Json.JsonValueKind.String
                       && List.contains (v.GetValue<string>().ToLowerInvariant()) [ "and"; "or"; "&&"; "||" ] ->
            for side in [ "left"; "right" ] do
                match o.TryGetPropertyValue(side) with
                | true, operand when not (isNull operand) ->
                    match arithmeticOp operand with
                    | Some op ->
                        add (sprintf "%s: the %s operand of \"%s\" is an arithmetic result (op \"%s\"), which is a number, not a condition. Compare it, for instance a remainder test is {\"op\":\"=\",\"left\":<the \"%%\" expression>,\"right\":0}" where side (v.GetValue<string>()) op)
                    | None -> ()
                | _ -> ()
        | _ -> ()

    and checkCondition (o: JsonObject) (k: string) =
        match o.TryGetPropertyValue("condition") with
        | true, c when not (isNull c) ->
            match arithmeticOp c with
            | Some op -> add (sprintf "%s condition is an arithmetic result (op \"%s\"), which is a number, not a condition: compare it (for instance a remainder with 0)" k op)
            | None -> ()
            match c with
            | :? JsonValue as v when v.GetValueKind() = System.Text.Json.JsonValueKind.String ->
                let text = v.GetValue<string>()
                if text.Trim().Contains(" ") then
                    add (sprintf "%s condition is the text \"%s\": a condition must be an expression node (e.g. a UnaryOp \"not\", a BinaryOp, an Identifier)" k text)
            | :? JsonObject as co when kindOf co = "Literal" ->
                match co.TryGetPropertyValue("value") with
                | true, v when not (isNull v) && v.GetValueKind() = System.Text.Json.JsonValueKind.String ->
                    add (sprintf "%s condition is the text \"%s\": a condition must be an expression node (e.g. a UnaryOp \"not\", a BinaryOp, an Identifier)" k (v.GetValue<string>()))
                | _ -> ()
            | _ -> ()
        | _ -> ()

    and children (o: JsonObject) (k: string) =
        if k = "If" || k = "WhileLoop" then checkCondition o k
        if k = "BinaryOp" then checkLogicalOperands o k
        for kv in o do
            if stmtListKeys.Contains kv.Key then
                match kv.Value with
                | :? JsonArray as a -> a |> Seq.iteri (fun i s -> visitStmt s (sprintf "%s.%s[%d]" k kv.Key i))
                | _ -> ()
            else
                match kv.Value with
                | :? JsonObject as c -> visitExpr c (sprintf "%s.%s" k kv.Key)
                | :? JsonArray as a ->
                    for item in a do
                        match item with
                        | :? JsonObject as c -> visitExpr c (sprintf "%s.%s" k kv.Key)
                        | _ -> ()
                | _ -> ()

    match root with
    | :? JsonObject as o -> children o (kindOf o)
    | _ -> ()
    List.ofSeq problems
