module TypeInferrer

open Ast

// ── Types inférés ─────────────────────────────────────────────────────────────

type InferredType =
    | TInt
    | TFloat
    | TBool
    | TString
    | TList     // List<object> — seul type de collection du langage
    | TObject
    | TUnknown

type TypeEnv = Map<string, InferredType>

/// Clé réservée (namespacée, ne collisionne jamais avec un nom de variable)
/// utilisée pour stocker le type de retour inféré d'une fonction dans un
/// TypeEnv ordinaire — évite de faire passer une table de fonctions séparée
/// à travers toute la chaîne d'inférence.
let functionReturnKey (name: string) : string =
    "fn:" + name.Trim().ToLowerInvariant()

// ── Inférence d'expressions ───────────────────────────────────────────────────

let rec inferExpr (env: TypeEnv) (expr: ExprNode) : InferredType =
    match expr with
    | Literal (LitInt _)    -> TInt
    | Literal (LitFloat _)  -> TFloat
    | Literal (LitBool _)   -> TBool
    | Literal (LitString _) -> TString

    | ReadExpr _ -> TString

    | Identifier name ->
        env |> Map.tryFind name |> Option.defaultValue TUnknown

    | BinaryOp (op, left, right) ->
        let lt = inferExpr env left
        let rt = inferExpr env right

        match op with
        | "&&" | "||" | "==" | "!=" | "<" | ">" | "<=" | ">="
        | "and" | "or" | "=" | "<>" -> TBool
        | "+" ->
            match lt, rt with
            | TString, _ | _, TString -> TString
            | TFloat,  _ | _, TFloat  -> TFloat
            | TInt,    TInt            -> TInt
            | _                        -> TObject
        | "-" | "*" | "/" | "%" ->
            match lt, rt with
            | TFloat, _ | _, TFloat -> TFloat
            | TInt,   TInt          -> TInt
            | _                     -> TFloat  // par défaut numérique
        | _ -> TUnknown

    | UnaryOp ("not", _) -> TBool
    | UnaryOp (("-" | "+"), operand) -> inferExpr env operand
    | UnaryOp _ -> TUnknown

    | FunctionCallExpr (name, _) ->
        env |> Map.tryFind (functionReturnKey name) |> Option.defaultValue TUnknown
    | PropertyAccess _   -> TUnknown
    | ArrayAccess _      -> TUnknown
    | NewList            -> TList
    | RandomNumber _     -> TInt

    // ── Pseudraw ───────────────────────────────────────────────────────────────
    | PseudrawKeyPressed _  -> TBool
    | PseudrawPoint _       -> TObject   // PointF
    | PseudrawRandomPoint _ -> TObject   // PointF
    | PseudrawDistance _    -> TFloat
    | PseudrawOverlaps _    -> TBool
    | PseudrawIsInside _    -> TBool
    | PseudrawIsIn _        -> TBool


// ── Inférence de statements ───────────────────────────────────────────────────

let rec inferStmts (env: TypeEnv) (stmts: StmtNode list) : TypeEnv =
    stmts |> List.fold inferStmt env

and inferStmt (env: TypeEnv) (stmt: StmtNode) : TypeEnv =
    match stmt with
    | Assign (target, value) ->
        let t = inferExpr env value
        env |> Map.add target t

    | IndexedAssign _ -> env

    | Read (target, _) ->
        // ReadLine() retourne string. Mais lire dans une variable DÉJÀ numérique
        // (ex. `guess = 0` puis `read guess`) garde le type numérique : le backend
        // convertit alors la saisie (int.Parse / double.Parse).
        match Map.tryFind target env with
        | Some (TInt | TFloat) -> env
        | _ -> env |> Map.add target TString

    | If (_, thenBranch, elseBranch) ->
        let env1 = inferStmts env thenBranch
        match elseBranch with
        | Some stmts -> inferStmts env1 stmts
        | None       -> env1

    | WhileLoop (_, body)
    | ForEach   (_, _, body) ->
        inferStmts env body

    | Loop (variable, _, _, _, body) ->
        let env1 = env |> Map.add variable TInt
        inferStmts env1 body

    | FunctionDecl (_, params_, body) ->
        let env1 =
            params_ |> List.fold (fun e p -> e |> Map.add p TUnknown) env
        inferStmts env1 body |> ignore
        env  // les fonctions n'affectent pas l'env parent

    | PseudrawOnTick body -> inferStmts env body

    | _ -> env


// ── Point d'entrée ────────────────────────────────────────────────────────────

let infer (program: ProgramNode) : TypeEnv =
    let env =
        program.Functions
        |> List.fold inferStmt Map.empty
    inferStmts env program.Algorithm

// Propage `None` (« rien trouvé ici ») distinctement de `Some TObject` (« un
// vrai `return` sans valeur a été trouvé ») jusqu'à la racine — sans ça, un
// `If`/boucle SANS `return` interne retombe sur le TObject par défaut de
// l'appel récursif, que le tryPick englobant prend alors à tort pour un vrai
// retour trouvé, et il ne regarde jamais les statements suivants (ex: un
// `return ok` placé juste après un `for each` qui n'en contient pas).
let rec private tryInferReturnType (env: TypeEnv) (stmts: StmtNode list) : InferredType option =
    stmts |> List.tryPick (fun stmt ->
        match stmt with
        | Return (Some value) -> Some (inferExpr env value)
        | Return None         -> Some TObject
        | If (_, thenBranch, elseBranch) ->
            let t1 = tryInferReturnType env thenBranch
            let t2 = elseBranch |> Option.bind (tryInferReturnType env)
            t1 |> Option.orElse t2
        | WhileLoop (_, body)
        | ForEach (_, _, body)
        | Loop (_, _, _, _, body) ->
            tryInferReturnType env body
        | _ -> None)

let public inferReturnType (env: TypeEnv) (stmts: StmtNode list) : InferredType =
    tryInferReturnType env stmts |> Option.defaultValue TObject

let inferParamType (paramName: string) (body: StmtNode list) : InferredType =
    // Cherche comment le paramètre est utilisé dans le corps
    let rec scanExpr (expr: ExprNode) : InferredType option =
        match expr with
        | BinaryOp (op, left, right) ->
            let isNumericOp = List.contains op ["-"; "*"; "/"; "%"; ">"; "<"; ">="; "<="]
            let isCompareOp = List.contains op ["="; "=="; "!="; "<>"]
            let involvesParam =
                match left, right with
                | Identifier n, _ when n = paramName -> true
                | _, Identifier n when n = paramName -> true
                | _ -> false
            if involvesParam then
                if isNumericOp then Some TFloat
                elif isCompareOp then
                    // Regarde l'autre opérande pour inférer
                    match left, right with
                    | Identifier n, other when n = paramName ->
                        match inferExpr Map.empty other with
                        | TInt | TFloat -> Some TFloat
                        | TString -> Some TString
                        | _ -> Some TObject
                    | other, Identifier n when n = paramName ->
                        match inferExpr Map.empty other with
                        | TInt | TFloat -> Some TFloat
                        | TString -> Some TString
                        | _ -> Some TObject
                    | _ -> None
                else None
            else
                scanExpr left |> Option.orElse (scanExpr right)
        | _ -> None

    and scanStmt (stmt: StmtNode) : InferredType option =
        match stmt with
        | Return (Some expr)          -> scanExpr expr
        | Assign (_, value)           -> scanExpr value
        | Print value                 -> scanExpr value
        | If (cond, thenB, elseB)     ->
            scanExpr cond
            |> Option.orElse (thenB |> List.tryPick scanStmt)
            |> Option.orElse (elseB |> Option.defaultValue [] |> List.tryPick scanStmt)
        | WhileLoop (_, body)
        | ForEach (_, _, body)
        | Loop (_, _, _, _, body)     -> body |> List.tryPick scanStmt
        | _                           -> None

    body |> List.tryPick scanStmt |> Option.defaultValue TObject