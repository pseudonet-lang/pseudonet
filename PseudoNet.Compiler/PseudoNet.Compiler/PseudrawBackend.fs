module PseudrawBackend

open System
open System.Text
open System.Collections.Generic
open Ast
open TypeInferrer

// ── Helpers (dupliqués depuis Backend.fs — voir plan : genExpr/genStmts sont
//    volontairement autonomes ici plutôt que d'appeler dans Backend.fs) ────────

let private indent (level: int) = String.replicate (level * 4) " "

let private escapeString (s: string) =
    s.Replace("\\", "\\\\").Replace("\"", "\\\"")

let private toPascal (s: string) =
    System.Text.RegularExpressions.Regex.Split(s, @"[^\p{L}\p{Nd}]+")
    |> Array.filter (fun w -> w.Length > 0)
    |> Array.map (fun w -> string (System.Char.ToUpper(w.[0])) + w.[1..])
    |> String.concat ""
    |> fun ident -> if ident.Length > 0 && System.Char.IsDigit(ident.[0]) then "_" + ident else ident

// Les noms de couleur (Color.FromName) et de touche (enum Keys) .NET sont
// anglais. Les mots d'une autre langue sont traduits via des tables JSON
// externes (mot → nom anglais .NET) : tous les `<dossier>/<préfixe>.*.json` à
// côté de l'exécutable, plus les fichiers listés dans une variable d'environnement
// (séparés par `;`). Chargés dans l'ordre, les derniers l'emportent. Tout mot
// absent de la table suit le chemin par défaut (noms anglais toujours utilisables).
let private loadWordTable (folder: string) (prefix: string) (envVar: string) : IDictionary<string, string> =
    let table = Dictionary<string, string>()
    let loadFile (path: string) =
        try
            use doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path, Encoding.UTF8))
            for p in doc.RootElement.EnumerateObject() do
                if p.Value.ValueKind = System.Text.Json.JsonValueKind.String then
                    table.[p.Name.Trim().ToLowerInvariant()] <- p.Value.GetString()
        with ex ->
            eprintfn "Avertissement : table illisible (%s) : %s" path ex.Message
    let dir = System.IO.Path.Combine(AppContext.BaseDirectory, folder)
    if System.IO.Directory.Exists(dir) then
        System.IO.Directory.GetFiles(dir, prefix + ".*.json") |> Array.sort |> Array.iter loadFile
    match Environment.GetEnvironmentVariable(envVar) with
    | null | "" -> ()
    | extra ->
        extra.Split(';', StringSplitOptions.RemoveEmptyEntries)
        |> Array.iter (fun f -> loadFile (f.Trim()))
    table :> IDictionary<string, string>

let private colorTable = lazy (loadWordTable "Colors" "colors" "PSEUDONET_COLORS")
let private keyTable   = lazy (loadWordTable "Keys" "keys" "PSEUDONET_KEYS")

let private translateColorName (n: string) : string =
    match colorTable.Value.TryGetValue(n.Trim().ToLowerInvariant()) with
    | true, english -> english
    | false, _      -> n

let private genColor (c: ColorSpec) : string =
    match c with
    // Color.FromName fait sa propre résolution insensible à la casse contre les
    // KnownColor .NET — plus robuste que deviner le PascalCase d'un mot composé
    // ("steelblue" → SteelBlue) avec un simple découpage sur les espaces.
    | ColorName n                -> sprintf "Color.FromName(\"%s\")" (escapeString (translateColorName n))
    | ColorRgb (r, g, b, None)   -> sprintf "Color.FromArgb(%d, %d, %d)" r g b
    | ColorRgb (r, g, b, Some a) -> sprintf "Color.FromArgb(%d, %d, %d, %d)" a r g b

let private mapKeyName (k: string) : string =
    let key = k.Trim().ToLowerInvariant()
    match keyTable.Value.TryGetValue(key) with
    | true, member_ -> member_
    | false, _ ->
    match key with
    | "up"       -> "Up"
    | "down"     -> "Down"
    | "left"     -> "Left"
    | "right"    -> "Right"
    | "space"    -> "Space"
    | "enter"    -> "Enter"
    | "escape" | "esc" -> "Escape"
    | "shift"    -> "ShiftKey"
    | "ctrl"     -> "ControlKey"
    | "alt"      -> "Menu"
    | "tab"      -> "Tab"
    | "backspace" | "back" -> "Back"
    | "delete"   -> "Delete"
    | "f1" -> "F1" | "f2" -> "F2" | "f3" -> "F3" | "f4"  -> "F4"
    | "f5" -> "F5" | "f6" -> "F6" | "f7" -> "F7" | "f8"  -> "F8"
    | "f9" -> "F9" | "f10" -> "F10" | "f11" -> "F11" | "f12" -> "F12"
    | s when s.Length = 1 && Char.IsLetter(s.[0]) -> s.ToUpperInvariant()
    | s when s.Length = 1 && Char.IsDigit(s.[0])  -> sprintf "D%s" s
    | s -> toPascal s

// ── Expressions ──────────────────────────────────────────────────────────────

let rec private genExpr (env: TypeEnv) (expr: ExprNode) : string =
    match expr with
    | Literal (LitInt n)    -> string n
    | Literal (LitFloat f)  -> sprintf "%g" f
    | Literal (LitBool b)   -> if b then "true" else "false"
    | Literal (LitString s) -> sprintf "\"%s\"" (escapeString s)

    | Identifier name ->
        match name with
        | "width"  when not (Map.containsKey name env) -> "ClientSize.Width"
        | "height" when not (Map.containsKey name env) -> "ClientSize.Height"
        | _ -> name

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
        let l = if isNumeric && lt = TString then sprintf "double.Parse(%s)" (genExpr env left) else genExpr env left
        let r = if isNumeric && rt = TString then sprintf "double.Parse(%s)" (genExpr env right) else genExpr env right
        sprintf "(%s %s %s)" l csOp r

    | UnaryOp (op, operand) ->
        let csOp = match op with "not" -> "!" | _ -> op
        sprintf "%s(%s)" csOp (genExpr env operand)

    | PropertyAccess (obj, prop) ->
        match prop.ToLowerInvariant() with
        | "x"             -> sprintf "((PointF)%s).X" obj
        | "y"             -> sprintf "((PointF)%s).Y" obj
        | "width" | "w"   -> sprintf "%s.Width" obj
        | "height" | "h"  -> sprintf "%s.Height" obj
        | "top"           -> sprintf "%s.Top" obj
        | "left"          -> sprintf "%s.Left" obj
        | "bottom"        -> sprintf "%s.Bottom" obj
        | "right"         -> sprintf "%s.Right" obj
        | _               -> sprintf "%s.%s" obj (toPascal prop)

    | ArrayAccess (array, index) ->
        sprintf "%s[%s]" array (genExpr env index)

    | FunctionCallExpr (name, args) ->
        let args_ = args |> List.map (genExpr env) |> String.concat ", "
        sprintf "%s(%s)" (toPascal name) args_

    | NewList -> "new List<object>()"

    | RandomNumber (min_, max_) ->
        sprintf "Random.Shared.Next(%s, %s + 1)" (genExpr env min_) (genExpr env max_)

    | ReadExpr (Some prompt) -> sprintf "Console.ReadLine() /* %s */" (genExpr env prompt)
    | ReadExpr None          -> "Console.ReadLine()"

    // ── Pseudraw ───────────────────────────────────────────────────────────────
    | PseudrawKeyPressed (key, true)  -> sprintf "_keysTapped.Contains(Keys.%s)" (mapKeyName key)
    // « Maintenue » = enfoncée MAINTENANT ou pressée depuis le dernier tick : l'état n'est lu qu'à chaque
    // tick (ex. 400 ms) ; sans le second terme, un appui bref relâché entre deux ticks serait perdu.
    | PseudrawKeyPressed (key, false) -> sprintf "(_keysHeld.Contains(Keys.%s) || _keysTapped.Contains(Keys.%s))" (mapKeyName key) (mapKeyName key)

    | PseudrawPoint (x, y) ->
        sprintf "new PointF((float)(%s), (float)(%s))" (genExpr env x) (genExpr env y)

    | PseudrawRandomPoint (x1, y1, x2, y2) ->
        sprintf "PseudrawRandomPoint((int)(%s), (int)(%s), (int)(%s), (int)(%s))"
            (genExpr env x1) (genExpr env y1) (genExpr env x2) (genExpr env y2)

    | PseudrawDistance (from_, to_) ->
        sprintf "PseudrawDistance((PointF)(%s), (PointF)(%s))" (genExpr env from_) (genExpr env to_)

    | PseudrawOverlaps (left, right) ->
        sprintf "((RectangleF)(%s)).IntersectsWith((RectangleF)(%s))" (genExpr env left) (genExpr env right)

    | PseudrawIsInside (point, rect) ->
        sprintf "((RectangleF)(%s)).Contains((PointF)(%s))" (genExpr env rect) (genExpr env point)

    | PseudrawIsIn (value, collection) ->
        sprintf "((%s) as System.Collections.IList)?.Contains(%s) == true" (genExpr env collection) (genExpr env value)

// ── Extraction config / setup / on-tick ──────────────────────────────────────

type private PseudrawConfig = {
    Width: int
    Height: int
    Title: string
    Background: ColorSpec
    FontName: string
    TickIntervalMs: int
}

let private defaultConfig = {
    Width = 800
    Height = 600
    Title = "Pseudraw.NET"
    Background = ColorName "black"
    FontName = "Consolas"
    TickIntervalMs = 100
}

let private evalIntLiteral (expr: ExprNode) (defaultVal: int) : int =
    match expr with
    | Literal (LitInt i)   -> i
    | Literal (LitFloat f) -> int f
    | _ -> defaultVal

let private evalStringLiteral (expr: ExprNode) (defaultVal: string) : string =
    match expr with
    | Literal (LitString s) -> s
    | _ -> defaultVal

let private extractConfig (body: StmtNode list) : PseudrawConfig * StmtNode list * StmtNode list =
    let mutable config = defaultConfig
    let mutable onTick : StmtNode list = []
    let setup =
        body |> List.filter (fun stmt ->
            match stmt with
            | PseudrawWidth v      -> config <- { config with Width = evalIntLiteral v config.Width }; false
            | PseudrawHeight v     -> config <- { config with Height = evalIntLiteral v config.Height }; false
            | PseudrawTitle v      -> config <- { config with Title = evalStringLiteral v config.Title }; false
            | PseudrawBackground c -> config <- { config with Background = c }; false
            | PseudrawFont f       -> config <- { config with FontName = f }; false
            | PseudrawTick ms      -> config <- { config with TickIntervalMs = ms }; false
            | PseudrawOnTick b     -> onTick <- onTick @ b; false
            | _ -> true)
    config, setup, onTick

// ── Champs (promotion des variables du setup) ────────────────────────────────

let rec private collectFieldNames (stmts: StmtNode list) : string list =
    stmts |> List.collect collectFromStmt

and private collectFromStmt (stmt: StmtNode) : string list =
    match stmt with
    | Assign (target, _)           -> [target]
    | IndexedAssign (target, _, _) -> [target]
    | Read (target, _)             -> [target]
    | If (_, thenB, elseB) ->
        collectFieldNames thenB @ (elseB |> Option.map collectFieldNames |> Option.defaultValue [])
    | WhileLoop (_, body)         -> collectFieldNames body
    | Loop (variable, _, _, _, body) -> variable :: collectFieldNames body
    | ForEach (variable, _, body)    -> variable :: collectFieldNames body
    | _ -> []

let private genFields (typedEnv: TypeEnv) (fieldNames: string list) : string =
    fieldNames
    |> List.map (fun name ->
        let t = typedEnv |> Map.tryFind name |> Option.defaultValue TUnknown |> Backend.csharpTypeName
        sprintf "    private %s %s;" t name)
    |> String.concat "\n"

// ── Hissage des variables locales ────────────────────────────────────────────
// Une variable affectée pour la première fois DANS un bloc imbriqué (if/while/
// for) puis relue en dehors de ce bloc — ou dans une autre branche — donnerait
// un `var x = ...;` scopé au bloc C# en question, invisible ensuite : erreur
// de compilation. On pré-déclare donc, en tête de méthode, toute variable
// affectée n'importe où dans le corps (hors champs/paramètres déjà connus),
// pour que `genStmts` ne la (re)déclare jamais avec `var` à l'intérieur d'un
// sous-bloc.

let rec private collectLocalsToHoist (stmts: StmtNode list) : string list =
    stmts |> List.collect localsOfStmt

and private localsOfStmt (stmt: StmtNode) : string list =
    match stmt with
    | Assign (target, _) -> [target]
    | Read (target, _)   -> [target]
    | If (_, thenB, elseB) ->
        collectLocalsToHoist thenB @ (elseB |> Option.map collectLocalsToHoist |> Option.defaultValue [])
    | WhileLoop (_, body)     -> collectLocalsToHoist body
    | Loop (_, _, _, _, body) -> collectLocalsToHoist body
    | ForEach (_, _, body)    -> collectLocalsToHoist body
    | _ -> []

let private genHoistedDecls (env: TypeEnv) (hoisted: string list) : string =
    hoisted
    |> List.map (fun name ->
        let t = env |> Map.tryFind name |> Option.defaultValue TUnknown |> Backend.csharpTypeName
        sprintf "%s%s %s = default;" (indent 2) t name)
    |> String.concat "\n"

// ── Statements ───────────────────────────────────────────────────────────────

let private genStmts (fieldNames: string list) (level: int) (initialEnv: TypeEnv) (stmts: StmtNode list) : string =
    let declared = HashSet<string>(fieldNames |> Seq.ofList)
    let mutable env = initialEnv

    let rec genStmt_ (level: int) (stmt: StmtNode) : string =
        let ind = indent level
        match stmt with
        | Assign (target, value) ->
            let code = genExpr env value
            let t = inferExpr env value
            let prefix =
                if declared.Contains(target) then ""
                else
                    declared.Add(target) |> ignore
                    "var "
            env <- env |> Map.add target t
            sprintf "%s%s%s = %s;" ind prefix target code

        | IndexedAssign (target, index, value) ->
            sprintf "%s%s[%s] = %s;" ind target (genExpr env index) (genExpr env value)

        | Read (target, prompt) ->
            let promptLine =
                match prompt with
                | Some p -> sprintf "%sConsole.Write(%s);\n" ind (genExpr env p)
                | None   -> ""
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
            sprintf "%s%s%s%s = %s;" promptLine ind prefix target readExpr

        | Print value ->
            sprintf "%sConsole.WriteLine(%s);" ind (genExpr env value)

        | Return (Some value) -> sprintf "%sreturn %s;" ind (genExpr env value)
        | Return None         -> sprintf "%sreturn;" ind

        | Break    -> sprintf "%sbreak;" ind
        | Continue -> sprintf "%scontinue;" ind

        | Comment text ->
            if text.Trim() = "" then "" else sprintf "%s// %s" ind text

        | If (condition, thenBranch, elseBranch) ->
            let sb = StringBuilder()
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
            let sb = StringBuilder()
            sb.AppendLine(sprintf "%swhile (%s)" ind (genExpr env condition)) |> ignore
            sb.AppendLine(sprintf "%s{" ind) |> ignore
            body |> List.iter (fun s ->
                let line = genStmt_ (level + 1) s
                if line.Trim() <> "" then sb.AppendLine(line) |> ignore)
            sb.Append(sprintf "%s}" ind) |> ignore
            sb.ToString()

        | Loop (variable, from_, to_, step, body) ->
            let sb = StringBuilder()
            let stepExpr =
                match step with
                | Some s -> sprintf "%s += %s" variable (genExpr env s)
                | None   -> sprintf "%s++" variable
            sb.AppendLine(sprintf "%sfor (var %s = %s; %s <= %s; %s)"
                ind variable (genExpr env from_) variable (genExpr env to_) stepExpr) |> ignore
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
            let sb = StringBuilder()
            sb.AppendLine(sprintf "%sforeach (var %s in %s)" ind variable (genExpr env collection)) |> ignore
            sb.AppendLine(sprintf "%s{" ind) |> ignore
            let prevEnv = env
            env <- env |> Map.add variable TObject
            body |> List.iter (fun s ->
                let line = genStmt_ (level + 1) s
                if line.Trim() <> "" then sb.AppendLine(line) |> ignore)
            env <- prevEnv
            sb.Append(sprintf "%s}" ind) |> ignore
            sb.ToString()

        // Verbes de collection réservés — List<object> n'a pas de RemoveFirst/
        // RemoveLast/InsertFirst natifs, il faut les traduire vers RemoveAt/Insert.
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

        // ── Pseudraw — config/on-tick : jamais émis comme statement ─────────────
        | PseudrawWidth _ | PseudrawHeight _ | PseudrawTitle _
        | PseudrawBackground _ | PseudrawFont _ | PseudrawTick _
        | PseudrawOnTick _ -> ""

        | PseudrawPauseTick  -> sprintf "%s_timer.Stop();" ind
        | PseudrawResumeTick -> sprintf "%s_timer.Start();" ind
        | PseudrawStop       -> sprintf "%s_timer.Stop();" ind

        | PseudrawClear None         -> sprintf "%s_g.Clear(_background);" ind
        | PseudrawClear (Some name)  -> sprintf "%s%s.Clear();" ind name

        | PseudrawDrawLine (x1, y1, x2, y2, color, thickness) ->
            let th = thickness |> Option.map (genExpr env) |> Option.defaultValue "1f"
            sprintf "%s_g.DrawLine(new Pen(%s, %s), (float)(%s), (float)(%s), (float)(%s), (float)(%s));"
                ind (genColor color) th (genExpr env x1) (genExpr env y1) (genExpr env x2) (genExpr env y2)

        | PseudrawDrawRect (x, y, w, h, color, border, filled, thickness) ->
            let th = thickness |> Option.map (genExpr env) |> Option.defaultValue "1f"
            let xs, ys, ws, hs = genExpr env x, genExpr env y, genExpr env w, genExpr env h
            let mainLine =
                if filled then
                    sprintf "%s_g.FillRectangle(new SolidBrush(%s), (float)(%s), (float)(%s), (float)(%s), (float)(%s));"
                        ind (genColor color) xs ys ws hs
                else
                    sprintf "%s_g.DrawRectangle(new Pen(%s, %s), (float)(%s), (float)(%s), (float)(%s), (float)(%s));"
                        ind (genColor color) th xs ys ws hs
            match border with
            | Some b ->
                let borderLine =
                    sprintf "%s_g.DrawRectangle(new Pen(%s, %s), (float)(%s), (float)(%s), (float)(%s), (float)(%s));"
                        ind (genColor b) th xs ys ws hs
                mainLine + "\n" + borderLine
            | None -> mainLine

        | PseudrawDrawCircle (x, y, radius, color, border, filled, thickness) ->
            let th = thickness |> Option.map (genExpr env) |> Option.defaultValue "1f"
            let xs, ys, rs = genExpr env x, genExpr env y, genExpr env radius
            let bx = sprintf "(float)((%s) - (%s))" xs rs
            let by = sprintf "(float)((%s) - (%s))" ys rs
            let diam = sprintf "(float)((%s) * 2)" rs
            let mainLine =
                if filled then
                    sprintf "%s_g.FillEllipse(new SolidBrush(%s), %s, %s, %s, %s);" ind (genColor color) bx by diam diam
                else
                    sprintf "%s_g.DrawEllipse(new Pen(%s, %s), %s, %s, %s, %s);" ind (genColor color) th bx by diam diam
            match border with
            | Some b ->
                let borderLine = sprintf "%s_g.DrawEllipse(new Pen(%s, %s), %s, %s, %s, %s);" ind (genColor b) th bx by diam diam
                mainLine + "\n" + borderLine
            | None -> mainLine

        | PseudrawDrawPolygon (points, color, filled, thickness) ->
            let th = thickness |> Option.map (genExpr env) |> Option.defaultValue "1f"
            let ptsStr =
                points
                |> List.map (fun (px, py) -> sprintf "new PointF((float)(%s), (float)(%s))" (genExpr env px) (genExpr env py))
                |> String.concat ", "
            let pts = sprintf "new PointF[] { %s }" ptsStr
            if filled then
                sprintf "%s_g.FillPolygon(new SolidBrush(%s), %s);" ind (genColor color) pts
            else
                sprintf "%s_g.DrawPolygon(new Pen(%s, %s), %s);" ind (genColor color) th pts

        | PseudrawDrawPoint (x, y, color) ->
            sprintf "%s_g.FillRectangle(new SolidBrush(%s), (float)(%s), (float)(%s), 1f, 1f);"
                ind (genColor color) (genExpr env x) (genExpr env y)

        | PseudrawDrawText (value, x, y, color, fontSize, fontName) ->
            let size = fontSize |> Option.map (genExpr env) |> Option.defaultValue "12f"
            let font =
                match fontName with
                | Some f -> sprintf "new Font(\"%s\", (float)(%s), FontStyle.Regular, GraphicsUnit.Pixel)" (escapeString f) size
                | None   -> sprintf "new Font(_fontName, (float)(%s), FontStyle.Regular, GraphicsUnit.Pixel)" size
            sprintf "%s_g.DrawString(Convert.ToString(%s), %s, new SolidBrush(%s), (float)(%s), (float)(%s));"
                ind (genExpr env value) font (genColor color) (genExpr env x) (genExpr env y)

        | PseudrawDrawObject target ->
            sprintf "%s// draw %s — not yet supported" ind target

        | PseudrawPlayTone (frequency, durationMs) ->
            sprintf "%sPlayTone((int)(%s), (int)(%s));" ind (genExpr env frequency) (genExpr env durationMs)

        | PseudrawTetrominoCells (shape, rotation, x, y, target) ->
            sprintf "%s%s.Clear();\n%s%s.AddRange(TetrominoCells((int)(%s), (int)(%s), (int)(%s), (int)(%s)));"
                ind target ind target (genExpr env shape) (genExpr env rotation) (genExpr env x) (genExpr env y)

    stmts
    |> List.map (genStmt_ level)
    |> List.filter (fun s -> s.Trim() <> "")
    |> String.concat "\n"

// ── Fonctions utilisateur (méthodes d'instance) ──────────────────────────────

let rec private hasAnyReturn (stmts: StmtNode list) : bool =
    stmts |> List.exists (fun stmt ->
        match stmt with
        | Return _ -> true
        | If (_, thenB, elseB) ->
            hasAnyReturn thenB || (elseB |> Option.map hasAnyReturn |> Option.defaultValue false)
        | WhileLoop (_, body) | ForEach (_, _, body) | Loop (_, _, _, _, body) -> hasAnyReturn body
        | _ -> false)

/// Pré-calcule le type de retour de chaque fonction et le range dans un
/// TypeEnv sous la clé réservée `functionReturnKey` — sans ça, `fell = move
/// piece down` (un FunctionCallExpr) reste TUnknown → "object", et échoue à
/// la compilation dès qu'on l'utilise comme bool/int/etc.
let private computeFunctionReturnTypes (env: TypeEnv) (functions: StmtNode list) : TypeEnv =
    functions
    |> List.fold (fun acc fn ->
        match fn with
        | FunctionDecl (name, params_, body) ->
            let localEnvBase = params_ |> List.fold (fun e p -> e |> Map.add p (inferParamType p body)) env
            let localEnv = TypeInferrer.inferStmts localEnvBase body
            let rt = if hasAnyReturn body then inferReturnType localEnv body else TUnknown
            acc |> Map.add (TypeInferrer.functionReturnKey name) rt
        | _ -> acc) env

let private genFunction (fieldNames: string list) (env: TypeEnv) (fn: StmtNode) : string =
    match fn with
    | FunctionDecl (name, params_, body) ->
        let localEnvBase =
            params_ |> List.fold (fun e p -> e |> Map.add p (inferParamType p body)) env
        // Infère aussi sur le CORPS de la fonction — sans ça, les variables
        // locales (ok, newX, blocked, newBoard...) ne sont jamais typées et
        // retombent en "object" (bool/int/List<object> attendus).
        let localEnv = TypeInferrer.inferStmts localEnvBase body
        let returnType =
            if hasAnyReturn body then Backend.csharpTypeName (inferReturnType localEnv body)
            else "void"
        let paramsStr =
            params_
            |> List.map (fun p -> sprintf "%s %s" (Backend.csharpTypeName (inferParamType p body)) p)
            |> String.concat ", "
        let hoisted =
            collectLocalsToHoist body
            |> List.distinct
            |> List.filter (fun n -> not (List.contains n fieldNames) && not (List.contains n params_))

        let sb = StringBuilder()
        sb.AppendLine(sprintf "    private %s %s(%s)" returnType (toPascal name) paramsStr) |> ignore
        sb.AppendLine("    {") |> ignore
        if not hoisted.IsEmpty then
            sb.AppendLine(genHoistedDecls localEnv hoisted) |> ignore
        sb.AppendLine(genStmts (fieldNames @ params_ @ hoisted) 2 localEnv body) |> ignore
        sb.AppendLine("    }") |> ignore
        sb.ToString()
    | _ -> ""

// ── Helpers géométrie émis une fois ──────────────────────────────────────────

let private genGeometryHelpers () : string =
    String.concat "\n" [
        "    private static PointF PseudrawRandomPoint(int x1, int y1, int x2, int y2)"
        "    {"
        "        var rng = Random.Shared;"
        "        return new PointF(rng.Next(x1, x2 + 1), rng.Next(y1, y2 + 1));"
        "    }"
        ""
        "    private static double PseudrawDistance(PointF a, PointF b) =>"
        "        Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));"
        ""
        "    // ── Son : file d'attente + thread dédié pour jouer les notes dans l'ordre"
        "    //    sans bloquer l'UI (Console.Beep est synchrone sur l'appelant). ────────"
        "    private static readonly System.Collections.Concurrent.ConcurrentQueue<(int freq, int dur)> _audioQueue = new();"
        "    private static readonly object _audioLock = new object();"
        "    private static bool _audioThreadStarted = false;"
        "    private static void EnsureAudioThread()"
        "    {"
        "        lock (_audioLock)"
        "        {"
        "            if (_audioThreadStarted) return;"
        "            _audioThreadStarted = true;"
        "            var audioThread = new System.Threading.Thread(() =>"
        "            {"
        "                while (true)"
        "                {"
        "                    if (_audioQueue.TryDequeue(out var note))"
        "                    {"
        "                        try { Console.Beep(note.freq, note.dur); } catch { }"
        "                    }"
        "                    else"
        "                    {"
        "                        System.Threading.Thread.Sleep(10);"
        "                    }"
        "                }"
        "            });"
        "            audioThread.IsBackground = true;"
        "            audioThread.Start();"
        "        }"
        "    }"
        "    private static void PlayTone(int frequency, int durationMs)"
        "    {"
        "        EnsureAudioThread();"
        "        int f = Math.Clamp(frequency, 37, 32767);"
        "        int d = Math.Max(1, durationMs);"
        "        _audioQueue.Enqueue((f, d));"
        "    }"
        ""
        "    // ── Géométrie des 7 tétrominos (0=I,1=O,2=T,3=S,4=Z,5=J,6=L), 4 rotations —"
        "    //    donnée fixe fournie par le compilateur, jamais laissée à la charge du"
        "    //    pseudocode. ─────────────────────────────────────────────────────────"
        "    private static List<object> TetrominoCells(int shape, int rotation, int x, int y)"
        "    {"
        "        int r = ((rotation % 4) + 4) % 4;"
        "        (int dx, int dy)[] offsets;"
        "        switch (shape)"
        "        {"
        "            case 0:"
        "                offsets = (r == 1 || r == 3)"
        "                    ? new (int, int)[] { (2,0), (2,1), (2,2), (2,3) }"
        "                    : new (int, int)[] { (0,1), (1,1), (2,1), (3,1) };"
        "                break;"
        "            case 1:"
        "                offsets = new (int, int)[] { (1,0), (2,0), (1,1), (2,1) };"
        "                break;"
        "            case 2:"
        "                switch (r)"
        "                {"
        "                    case 1: offsets = new (int, int)[] { (1,0), (1,1), (2,1), (1,2) }; break;"
        "                    case 2: offsets = new (int, int)[] { (0,1), (1,1), (2,1), (1,2) }; break;"
        "                    case 3: offsets = new (int, int)[] { (1,0), (0,1), (1,1), (1,2) }; break;"
        "                    default: offsets = new (int, int)[] { (1,0), (0,1), (1,1), (2,1) }; break;"
        "                }"
        "                break;"
        "            case 3:"
        "                offsets = (r == 1 || r == 3)"
        "                    ? new (int, int)[] { (1,0), (1,1), (2,1), (2,2) }"
        "                    : new (int, int)[] { (1,0), (2,0), (0,1), (1,1) };"
        "                break;"
        "            case 4:"
        "                offsets = (r == 1 || r == 3)"
        "                    ? new (int, int)[] { (2,0), (1,1), (2,1), (1,2) }"
        "                    : new (int, int)[] { (0,0), (1,0), (1,1), (2,1) };"
        "                break;"
        "            case 5:"
        "                switch (r)"
        "                {"
        "                    case 1: offsets = new (int, int)[] { (1,0), (2,0), (1,1), (1,2) }; break;"
        "                    case 2: offsets = new (int, int)[] { (0,1), (1,1), (2,1), (2,2) }; break;"
        "                    case 3: offsets = new (int, int)[] { (1,0), (1,1), (0,2), (1,2) }; break;"
        "                    default: offsets = new (int, int)[] { (0,0), (0,1), (1,1), (2,1) }; break;"
        "                }"
        "                break;"
        "            default:"
        "                switch (r)"
        "                {"
        "                    case 1: offsets = new (int, int)[] { (1,0), (1,1), (1,2), (2,2) }; break;"
        "                    case 2: offsets = new (int, int)[] { (0,1), (1,1), (2,1), (0,2) }; break;"
        "                    case 3: offsets = new (int, int)[] { (0,0), (1,0), (1,1), (1,2) }; break;"
        "                    default: offsets = new (int, int)[] { (2,0), (0,1), (1,1), (2,1) }; break;"
        "                }"
        "                break;"
        "        }"
        "        var result = new List<object>();"
        "        foreach (var (dx, dy) in offsets)"
        "            result.Add(new PointF(x + dx, y + dy));"
        "        return result;"
        "    }"
    ]

// ── Programme complet ─────────────────────────────────────────────────────────

let generate (program: ProgramNode) : string =
    let config, setupStmts, onTickStmts = extractConfig program.Algorithm
    let fieldNames = collectFieldNames setupStmts |> List.distinct
    let fnReturnTypes = computeFunctionReturnTypes Map.empty program.Functions
    let typedEnv = TypeInferrer.inferStmts fnReturnTypes (setupStmts @ onTickStmts)
    if System.Environment.GetEnvironmentVariable("PSEUDONET_DEBUG") = "1" then
        eprintfn "DEBUG fnReturnTypes: %A" fnReturnTypes
        eprintfn "DEBUG typedEnv['fell']: %A" (Map.tryFind "fell" typedEnv)

    let sb = StringBuilder()
    sb.AppendLine("// Generated by Pseudo.NET — Pseudraw.NET backend") |> ignore
    sb.AppendLine("using System;") |> ignore
    sb.AppendLine("using System.Collections.Generic;") |> ignore
    sb.AppendLine("using System.Drawing;") |> ignore
    sb.AppendLine("using System.Windows.Forms;") |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine("class Program : Form") |> ignore
    sb.AppendLine("{") |> ignore

    sb.AppendLine("    // ── Runtime Pseudraw ───────────────────────────") |> ignore
    sb.AppendLine("    private Bitmap   _canvas;") |> ignore
    sb.AppendLine("    private Graphics _g;") |> ignore
    sb.AppendLine("    private Timer    _timer;") |> ignore
    sb.AppendLine("    private readonly HashSet<Keys> _keysHeld   = new();") |> ignore
    sb.AppendLine("    private readonly HashSet<Keys> _keysTapped = new();") |> ignore
    sb.AppendLine("    private Color    _background = Color.Black;") |> ignore
    sb.AppendLine("    private string   _fontName   = \"Consolas\";") |> ignore
    sb.AppendLine() |> ignore

    if not fieldNames.IsEmpty then
        sb.AppendLine("    // ── Variables utilisateur ──────────────────────") |> ignore
        sb.AppendLine(genFields typedEnv fieldNames) |> ignore
        sb.AppendLine() |> ignore

    // ── Constructeur ─────────────────────────────────────────────────────────
    sb.AppendLine("    public Program()") |> ignore
    sb.AppendLine("    {") |> ignore
    sb.AppendLine(sprintf "        Text            = \"%s\";" (escapeString config.Title)) |> ignore
    sb.AppendLine(sprintf "        ClientSize      = new Size(%d, %d);" config.Width config.Height) |> ignore
    sb.AppendLine( "        FormBorderStyle = FormBorderStyle.FixedSingle;") |> ignore
    sb.AppendLine( "        MaximizeBox     = false;") |> ignore
    sb.AppendLine( "        DoubleBuffered  = true;") |> ignore
    sb.AppendLine( "        SetStyle(ControlStyles.OptimizedDoubleBuffer |") |> ignore
    sb.AppendLine( "                 ControlStyles.AllPaintingInWmPaint  |") |> ignore
    sb.AppendLine( "                 ControlStyles.UserPaint, true);") |> ignore
    sb.AppendLine( "        StartPosition   = FormStartPosition.CenterScreen;") |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine(sprintf "        _canvas = new Bitmap(%d, %d);" config.Width config.Height) |> ignore
    sb.AppendLine( "        _g = Graphics.FromImage(_canvas);") |> ignore
    sb.AppendLine( "        _g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;") |> ignore
    sb.AppendLine(sprintf "        _background = %s;" (genColor config.Background)) |> ignore
    sb.AppendLine(sprintf "        _fontName   = \"%s\";" (escapeString config.FontName)) |> ignore
    sb.AppendLine( "        _g.Clear(_background);") |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine( "        Paint      += (s, e) => e.Graphics.DrawImage(_canvas, 0, 0);") |> ignore
    sb.AppendLine( "        KeyDown    += OnKeyDown;") |> ignore
    sb.AppendLine( "        KeyUp      += OnKeyUp;") |> ignore
    sb.AppendLine( "        KeyPreview = true;") |> ignore
    sb.AppendLine() |> ignore

    if not setupStmts.IsEmpty then
        let setupCode = genStmts fieldNames 2 typedEnv setupStmts
        if setupCode.Trim() <> "" then
            sb.AppendLine("        // ── Setup utilisateur ────────────────────") |> ignore
            sb.AppendLine(setupCode) |> ignore
            sb.AppendLine() |> ignore

    if not onTickStmts.IsEmpty then
        sb.AppendLine( "        _timer          = new Timer();") |> ignore
        sb.AppendLine(sprintf "        _timer.Interval = %d;" config.TickIntervalMs) |> ignore
        sb.AppendLine( "        _timer.Tick    += (s, e) => OnTick();") |> ignore
        sb.AppendLine( "        _timer.Start();") |> ignore

    sb.AppendLine("    }") |> ignore
    sb.AppendLine() |> ignore

    // ── OnTick ───────────────────────────────────────────────────────────────
    if not onTickStmts.IsEmpty then
        sb.AppendLine("    private void OnTick()") |> ignore
        sb.AppendLine("    {") |> ignore
        let tickHoisted =
            collectLocalsToHoist onTickStmts
            |> List.distinct
            |> List.filter (fun n -> not (List.contains n fieldNames))
        if not tickHoisted.IsEmpty then
            sb.AppendLine(genHoistedDecls typedEnv tickHoisted) |> ignore
        sb.AppendLine(genStmts (fieldNames @ tickHoisted) 2 typedEnv onTickStmts) |> ignore
        sb.AppendLine("        _keysTapped.Clear();") |> ignore
        sb.AppendLine("        Invalidate();") |> ignore
        sb.AppendLine("    }") |> ignore
        sb.AppendLine() |> ignore

    // ── Clavier ──────────────────────────────────────────────────────────────
    sb.AppendLine("    private void OnKeyDown(object s, KeyEventArgs e)") |> ignore
    sb.AppendLine("    {") |> ignore
    sb.AppendLine("        if (!_keysHeld.Contains(e.KeyCode))") |> ignore
    sb.AppendLine("            _keysTapped.Add(e.KeyCode);") |> ignore
    sb.AppendLine("        _keysHeld.Add(e.KeyCode);") |> ignore
    sb.AppendLine("    }") |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine("    private void OnKeyUp(object s, KeyEventArgs e)") |> ignore
    sb.AppendLine("    {") |> ignore
    sb.AppendLine("        _keysHeld.Remove(e.KeyCode);") |> ignore
    sb.AppendLine("    }") |> ignore
    sb.AppendLine() |> ignore

    // ── Helpers géométrie (toujours émis, coût négligeable) ────────────────────
    sb.AppendLine(genGeometryHelpers ()) |> ignore
    sb.AppendLine() |> ignore

    // ── Fonctions utilisateur ────────────────────────────────────────────────
    program.Functions |> List.iter (fun fn ->
        sb.Append(genFunction fieldNames typedEnv fn) |> ignore
        sb.AppendLine() |> ignore)

    // ── Point d'entrée ───────────────────────────────────────────────────────
    sb.AppendLine("    [STAThread]") |> ignore
    sb.AppendLine("    static void Main()") |> ignore
    sb.AppendLine("    {") |> ignore
    sb.AppendLine("        Application.EnableVisualStyles();") |> ignore
    sb.AppendLine("        Application.Run(new Program());") |> ignore
    sb.AppendLine("    }") |> ignore
    sb.AppendLine("}") |> ignore

    sb.ToString()
