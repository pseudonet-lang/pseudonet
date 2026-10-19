module Backend

open Ast
open TypeInferrer

// ── Helpers ──────────────────────────────────────────────────────────────────

let private indent (level: int) = String.replicate (level * 4) " "

let private escapeString (s: string) =
    s.Replace("\\", "\\\\").Replace("\"", "\\\"")

let private toPascal (s: string) =
    System.Text.RegularExpressions.Regex.Split(s, @"[^\p{L}\p{Nd}]+")
    |> Array.filter (fun w -> w.Length > 0)
    |> Array.map (fun w -> string (System.Char.ToUpper(w.[0])) + w.[1..])
    |> String.concat ""
    |> fun ident -> if ident.Length > 0 && System.Char.IsDigit(ident.[0]) then "_" + ident else ident

// ── Expressions ──────────────────────────────────────────────────────────────

let rec private genExpr (env: TypeEnv) (expr: ExprNode) : string =
    match expr with
    | Literal (LitInt n)    -> string n
    | Literal (LitFloat f)  -> sprintf "%g" f
    | Literal (LitBool b)   -> if b then "true" else "false"
    | Literal (LitString s) -> sprintf "\"%s\"" (escapeString s)

    | Identifier name -> name

    | BinaryOp (op, left, right) ->
        let csOp =
            match op with
            | "and" -> "&&" | "or"  -> "||"
            | "="   -> "==" | "!="  -> "!="
            | "<>"  -> "!=" | "%"   -> "%"
            | _     -> op
        let lt = inferExpr env left
        let rt = inferExpr env right
        let isNumeric = List.contains op ["-"; "*"; "/"; "%"; ">"; "<"; ">="; "<="]
        let l =
            if isNumeric && lt = TString then
                sprintf "double.Parse(%s)" (genExpr env left)
            else genExpr env left
        let r =
            if isNumeric && rt = TString then
                sprintf "double.Parse(%s)" (genExpr env right)
            else genExpr env right
        let l, r =
            match op with
            | "+" ->
                match lt, rt with
                | TString, _ | _, TString -> genExpr env left, genExpr env right
                | _ -> l, r
            | _ -> l, r
        // Un opérande de type inconnu (ex. un élément de liste, stocké comme `object`)
        // ne se compare ni ne s'additionne en C# : `(dynamic)` laisse le runtime décider
        // (et donne à == / != une sémantique de valeur, pas de référence).
        let isLogical = List.contains op ["and"; "or"; "&&"; "||"]
        let isConcat  = op = "+" && (lt = TString || rt = TString)
        let isUnknown t = (t = TUnknown || t = TObject)
        let l, r =
            if isLogical || isConcat then l, r
            else
                (if isUnknown lt then "(dynamic)" + l else l),
                (if isUnknown rt then "(dynamic)" + r else r)
        sprintf "(%s %s %s)" l csOp r

    | UnaryOp (op, operand) ->
        let csOp = match op with "not" -> "!" | _ -> op
        sprintf "%s(%s)" csOp (genExpr env operand)

    | PropertyAccess (obj, prop) ->
        sprintf "%s.%s" obj prop

    | ArrayAccess (array, index) ->
        sprintf "%s[%s]" array (genExpr env index)

    | FunctionCallExpr (name, args) ->
        let args_ = args |> List.map (genExpr env) |> String.concat ", "
        sprintf "%s(%s)" (toPascal name) args_

    | NewList -> "new List<object>()"

    | RandomNumber (min_, max_) ->
        sprintf "Random.Shared.Next(%s, %s + 1)" (genExpr env min_) (genExpr env max_)

    | ReadExpr (Some prompt) ->
        sprintf "Console.ReadLine() /* %s */" (genExpr env prompt)

    | ReadExpr None ->
        "Console.ReadLine()"

    // Les expressions Pseudraw n'ont pas de sens dans un projet console — ne
    // devrait jamais être atteint (Ast.detectProjectType route en amont).
    | PseudrawKeyPressed _ | PseudrawPoint _ | PseudrawRandomPoint _
    | PseudrawDistance _   | PseudrawOverlaps _ | PseudrawIsInside _ | PseudrawIsIn _ ->
        "null /* unsupported: Pseudraw expression in a console project */"


// ── Statements avec tracking progressif ──────────────────────────────────────

let private genStmts (fieldNames: string list) (level: int) (initialEnv: TypeEnv) (stmts: StmtNode list) : string =
    let declared = System.Collections.Generic.HashSet<string>(fieldNames)
    let fields = System.Collections.Generic.HashSet<string>(fieldNames)
    let mutable env = initialEnv

    let rec genStmt_ (level: int) (stmt: StmtNode) : string =
        let ind = indent level
        match stmt with
        | Assign (target, value) ->
            let code = genExpr env value     // génère avec l'env actuel
            let t = inferExpr env value      // infère le type avec l'env actuel
            let prefix =
                if declared.Contains(target) then ""
                else
                    declared.Add(target) |> ignore
                    "var "
            env <- env |> Map.add target t   // met à jour env après génération
            sprintf "%s%s%s = %s;" ind prefix target code

        | IndexedAssign (target, index, value) ->
            // Cible connue pour être un nombre : "total[0] = ..." n'a aucun sens en C# et traduit en pratique
            // "ajoute x au total" mal formé par le modèle. On garde l'intention (affectation simple), sans
            // jamais regarder les mots du pseudocode : seul le type de la variable décide.
            match Map.tryFind target env with
            | Some TInt | Some TFloat -> sprintf "%s%s = %s;" ind target (genExpr env value)
            | _ -> sprintf "%s%s[%s] = %s;" ind target (genExpr env index) (genExpr env value)

        | Read (target, prompt) ->
            let sb = System.Text.StringBuilder()
            match prompt with
            | Some p -> sb.AppendLine(sprintf "%sConsole.Write(%s);" ind (genExpr env p)) |> ignore
            | None   -> ()
            // Variable déjà numérique : on convertit la saisie au lieu de lui affecter une string.
            let readExpr =
                match Map.tryFind target env with
                | Some TInt   -> "int.Parse(Console.ReadLine())"
                | Some TFloat -> "double.Parse(Console.ReadLine())"
                | _ ->
                    env <- env |> Map.add target TString
                    "Console.ReadLine()"
            let prefix =
                if declared.Contains(target) then ""
                else
                    declared.Add(target) |> ignore
                    "var "
            sb.Append(sprintf "%s%s%s = %s;" ind prefix target readExpr) |> ignore
            sb.ToString()

        | Print value ->
            sprintf "%sConsole.WriteLine(%s);" ind (genExpr env value)

        | Return (Some value) ->
            sprintf "%sreturn %s;" ind (genExpr env value)

        | Return None ->
            sprintf "%sreturn;" ind

        | Break    -> sprintf "%sbreak;" ind
        | Continue -> sprintf "%scontinue;" ind

        | Comment text ->
            if text.Trim() = "" then ""
            else sprintf "%s// %s" ind text

        | If (condition, thenBranch, elseBranch) ->
            let sb = System.Text.StringBuilder()
            sb.AppendLine(sprintf "%sif (%s)" ind (genExpr env condition)) |> ignore
            sb.AppendLine(sprintf "%s{" ind) |> ignore
            thenBranch |> List.iter (fun s ->
                let line = genStmt_ (level + 1) s
                if line.Trim() <> "" then sb.AppendLine(line) |> ignore)
            sb.Append(sprintf "%s}" ind) |> ignore
            match elseBranch with
            | Some stmts ->
                sb.AppendLine() |> ignore
                sb.AppendLine(sprintf "%selse" ind) |> ignore
                sb.AppendLine(sprintf "%s{" ind) |> ignore
                stmts |> List.iter (fun s ->
                    let line = genStmt_ (level + 1) s
                    if line.Trim() <> "" then sb.AppendLine(line) |> ignore)
                sb.Append(sprintf "%s}" ind) |> ignore
            | None -> ()
            sb.ToString()

        | WhileLoop (condition, body) ->
            let sb = System.Text.StringBuilder()
            sb.AppendLine(sprintf "%swhile (%s)" ind (genExpr env condition)) |> ignore
            sb.AppendLine(sprintf "%s{" ind) |> ignore
            body |> List.iter (fun s ->
                let line = genStmt_ (level + 1) s
                if line.Trim() <> "" then sb.AppendLine(line) |> ignore)
            sb.Append(sprintf "%s}" ind) |> ignore
            sb.ToString()

        | Loop (variable, from_, to_, step, body) ->
            let sb = System.Text.StringBuilder()
            let stepExpr =
                match step with
                | Some s -> sprintf "%s += %s" variable (genExpr env s)
                | None   -> sprintf "%s++" variable
            // Si `variable` est un champ promu (boucle lue par une autre fonction),
            // on réutilise le champ directement plutôt que de le masquer avec un
            // `var` local scopé au for — sinon la fonction appelée depuis le corps
            // de la boucle verrait toujours la valeur par défaut du champ (0).
            let declPrefix = if fields.Contains(variable) then "" else "var "
            sb.AppendLine(sprintf "%sfor (%s%s = %s; %s <= %s; %s)"
                ind declPrefix variable (genExpr env from_) variable (genExpr env to_) stepExpr) |> ignore
            sb.AppendLine(sprintf "%s{" ind) |> ignore
            let prevEnv = env
            env <- env |> Map.add variable TInt
            body |> List.iter (fun s ->
                let line = genStmt_ (level + 1) s
                if line.Trim() <> "" then sb.AppendLine(line) |> ignore)
            env <- prevEnv
            sb.Append(sprintf "%s}" ind) |> ignore
            sb.ToString()

        | ForEach (variable, collection, body) ->
            let sb = System.Text.StringBuilder()
            match collection with
            | BinaryOp ("-", Literal (LitInt hi), Literal (LitInt lo)) ->
                sb.AppendLine(sprintf "%sfor (var %s = %d; %s <= %d; %s++)"
                    ind variable lo variable hi variable) |> ignore
            | _ ->
                sb.AppendLine(sprintf "%sforeach (var %s in %s)"
                    ind variable (genExpr env collection)) |> ignore
            sb.AppendLine(sprintf "%s{" ind) |> ignore
            let prevEnv = env
            env <- env |> Map.add variable TInt
            body |> List.iter (fun s ->
                let line = genStmt_ (level + 1) s
                if line.Trim() <> "" then sb.AppendLine(line) |> ignore)
            env <- prevEnv
            sb.Append(sprintf "%s}" ind) |> ignore
            sb.ToString()

        // Verbes de collection réservés — List<object> n'a pas de RemoveFirst/
        // RemoveLast/InsertFirst natifs, il faut les traduire vers RemoveAt/Insert.
        // "ajoute x au total" avec un total numérique : accumulation, pas ajout à une liste.
        | MethodCall (Some t, "Add", [ arg ]) when (match Map.tryFind t env with Some TInt | Some TFloat -> true | _ -> false) ->
            sprintf "%s%s = %s + %s;" ind t t (genExpr env arg)
        | MethodCall (Some t, "Add", args) ->
            let args_ = args |> List.map (genExpr env) |> String.concat ", "
            sprintf "%s%s.Add(%s);" ind t args_
        | MethodCall (Some t, "RemoveFirst", _) ->
            sprintf "%sif (%s.Count > 0) %s.RemoveAt(0);" ind t t
        | MethodCall (Some t, "RemoveLast", _) ->
            sprintf "%sif (%s.Count > 0) %s.RemoveAt(%s.Count - 1);" ind t t t
        | MethodCall (Some t, "RemoveAt", [ idx ]) ->
            sprintf "%s%s.RemoveAt((int)(%s));" ind t (genExpr env idx)
        | MethodCall (Some t, "InsertFirst", args) ->
            let args_ = args |> List.map (genExpr env) |> String.concat ", "
            sprintf "%s%s.Insert(0, %s);" ind t args_
        | MethodCall (Some t, "InsertAt", [ idx; value ]) ->
            sprintf "%s%s.Insert((int)(%s), %s);" ind t (genExpr env idx) (genExpr env value)

        | MethodCall (target, method_, args) ->
            let args_ = args |> List.map (genExpr env) |> String.concat ", "
            match target with
            | Some t -> sprintf "%s%s.%s(%s);" ind t (toPascal method_) args_
            | None   -> sprintf "%s%s(%s);" ind (toPascal method_) args_

        | FunctionCallStmt (name, args) ->
            let args_ = args |> List.map (genExpr env) |> String.concat ", "
            sprintf "%s%s(%s);" ind (toPascal name) args_

        | FunctionDecl _ -> ""

        // Les statements Pseudraw n'ont pas de sens dans un projet console —
        // ne devrait jamais être atteint (Ast.detectProjectType route en amont).
        | PseudrawWidth _ | PseudrawHeight _ | PseudrawTitle _
        | PseudrawBackground _ | PseudrawFont _ | PseudrawTick _
        | PseudrawOnTick _ | PseudrawPauseTick | PseudrawResumeTick | PseudrawStop
        | PseudrawClear _ | PseudrawDrawLine _ | PseudrawDrawRect _
        | PseudrawDrawCircle _ | PseudrawDrawPolygon _ | PseudrawDrawPoint _
        | PseudrawDrawText _ | PseudrawDrawObject _
        | PseudrawPlayTone _ | PseudrawTetrominoCells _ ->
            sprintf "%s// unsupported: Pseudraw statement in a console project" ind

    stmts
    |> List.map (genStmt_ level)
    |> List.filter (fun s -> s.Trim() <> "")
    |> String.concat "\n"


// ── Fonctions ─────────────────────────────────────────────────────────────────

/// Mapping InferredType -> nom de type C#. Partagé avec PseudrawBackend.fs.
let csharpTypeName (t: InferredType) : string =
    match t with
    | TInt    -> "int"
    | TFloat  -> "double"
    | TBool   -> "bool"
    | TString -> "string"
    | TList   -> "List<object>"
    | _       -> "object"

let private inferredReturnTypeName (env: TypeEnv) (body: StmtNode list) : string =
    TypeInferrer.inferReturnType env body |> csharpTypeName

let private inferredParamTypeName (env: TypeEnv) (name: string) : string =
    env |> Map.tryFind name |> Option.defaultValue TUnknown |> csharpTypeName

// ── Champs (promotion des variables du niveau supérieur de l'algorithme) ─────
// Une fonction console peut librement lire/écrire une variable définie dans
// l'algorithme principal sans qu'elle lui soit passée en paramètre (le LLM
// choisit l'un ou l'autre selon la formulation — voir "compteur" dans
// nombres_premiers_sans_accolades.pseudo). Comme les fonctions sont générées
// en `static`, ces variables doivent devenir des champs statiques de la
// classe, sinon le nom n'existe pas dans le contexte de la fonction (CS0103).
// La variable d'une boucle `ForEach` n'est PAS promue : `foreach` exige de
// déclarer sa propre variable, on ne peut pas réutiliser un champ existant.
let rec private collectFieldNames (stmts: StmtNode list) : string list =
    stmts |> List.collect collectFromStmt

and private collectFromStmt (stmt: StmtNode) : string list =
    match stmt with
    | Assign (target, _)           -> [target]
    | IndexedAssign (target, _, _) -> [target]
    | Read (target, _)             -> [target]
    | If (_, thenB, elseB) ->
        collectFieldNames thenB @ (elseB |> Option.map collectFieldNames |> Option.defaultValue [])
    | WhileLoop (_, body)            -> collectFieldNames body
    | Loop (variable, _, _, _, body) -> variable :: collectFieldNames body
    | ForEach (_, _, body)           -> collectFieldNames body
    | _ -> []

/// Pré-calcule le type de retour de chaque fonction sous la clé réservée
/// `functionReturnKey`, sinon `premier = est-ce que compteur est premier`
/// (un FunctionCallExpr) reste TUnknown → "object", et échoue à la
/// compilation dès qu'il est utilisé comme condition d'un `if`.
let private computeFunctionReturnTypes (env: TypeEnv) (functions: StmtNode list) : TypeEnv =
    functions
    |> List.fold (fun acc fn ->
        match fn with
        | FunctionDecl (name, params_, body) ->
            let localEnvBase =
                params_ |> List.fold (fun e p -> e |> Map.add p (TypeInferrer.inferParamType p body)) env
            let localEnv = TypeInferrer.inferStmts localEnvBase body
            let rt = TypeInferrer.inferReturnType localEnv body
            acc |> Map.add (TypeInferrer.functionReturnKey name) rt
        | _ -> acc) env

let private genFields (typedEnv: TypeEnv) (fieldNames: string list) : string =
    fieldNames
    |> List.map (fun name ->
        let t = typedEnv |> Map.tryFind name |> Option.defaultValue TUnknown |> csharpTypeName
        sprintf "    private static %s %s;" t name)
    |> String.concat "\n"

let private genFunction (fieldNames: string list) (env: TypeEnv) (fn: StmtNode) : string =
    match fn with
    | FunctionDecl (name, params_, body) ->
        let sb = System.Text.StringBuilder()

        // Construit un env local avec les types des paramètres
        let localEnv =
            params_
            |> List.fold (fun e p ->
                let t = TypeInferrer.inferParamType p body
                e |> Map.add p t) env

        // Infère aussi sur le CORPS de la fonction, sinon les variables locales
        // (estPremier, ok, etc.) restent non typées et le retour tombe en "object".
        let localEnvFull = TypeInferrer.inferStmts localEnv body
        let returnType = inferredReturnTypeName localEnvFull body
        let paramsStr =
            params_
            |> List.map (fun p ->
                let t = TypeInferrer.inferParamType p body
                let typeName =
                    match t with
                    | TInt    -> "int"
                    | TFloat  -> "double"
                    | TBool   -> "bool"
                    | TString -> "string"
                    | _       -> "object"
                sprintf "%s %s" typeName p)
            |> String.concat ", "

        sb.AppendLine(sprintf "    static %s %s(%s)" returnType (toPascal name) paramsStr) |> ignore
        sb.AppendLine("    {") |> ignore
        sb.AppendLine(genStmts fieldNames 2 localEnv body) |> ignore
        sb.AppendLine("    }") |> ignore
        sb.ToString()
    | _ -> ""

// ── Programme complet ─────────────────────────────────────────────────────────

let generate (program: ProgramNode) : string =
    let fnReturnTypes = computeFunctionReturnTypes Map.empty program.Functions
    let env = TypeInferrer.inferStmts fnReturnTypes program.Algorithm
    let fieldNames = collectFieldNames program.Algorithm |> List.distinct

    let sb = System.Text.StringBuilder()
    sb.AppendLine("// Generated by Pseudo.NET") |> ignore
    sb.AppendLine("using System;") |> ignore
    sb.AppendLine("using System.Collections.Generic;") |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine("class Program") |> ignore
    sb.AppendLine("{") |> ignore

    if not fieldNames.IsEmpty then
        sb.AppendLine(genFields env fieldNames) |> ignore

    program.Functions |> List.iter (fun f ->
        sb.Append(genFunction fieldNames env f) |> ignore)

    sb.AppendLine("    static void Main(string[] args)") |> ignore
    sb.AppendLine("    {") |> ignore
    sb.AppendLine(genStmts fieldNames 2 env program.Algorithm) |> ignore
    sb.AppendLine("    }") |> ignore
    sb.AppendLine("}") |> ignore

    sb.ToString()