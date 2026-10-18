module Ast

// ── Couleurs (partagé Pseudraw) ────────────────────────────────────────────────

type ColorSpec =
    | ColorName of string
    | ColorRgb  of r: int * g: int * b: int * a: int option

// ── Expressions ──────────────────────────────────────────────────────────────

type ExprNode =
    | Literal       of LiteralValue
    | Identifier    of name: string
    | BinaryOp      of op: string * left: ExprNode * right: ExprNode
    | UnaryOp       of op: string * operand: ExprNode
    | PropertyAccess of obj: string * property: string
    | ArrayAccess   of array: string * index: ExprNode
    | FunctionCallExpr of name: string * args: ExprNode list
    | NewList
    | RandomNumber  of min: ExprNode * max: ExprNode
    | ReadExpr of prompt: ExprNode option
    // ── Pseudraw — expressions ──────────────────────────────────────────────
    | PseudrawKeyPressed  of key: string * isTapped: bool
    | PseudrawPoint       of x: ExprNode * y: ExprNode
    | PseudrawRandomPoint of x1: ExprNode * y1: ExprNode * x2: ExprNode * y2: ExprNode
    | PseudrawDistance    of from_: ExprNode * to_: ExprNode
    | PseudrawOverlaps    of left: ExprNode * right: ExprNode
    | PseudrawIsInside    of point: ExprNode * rect: ExprNode
    | PseudrawIsIn        of value: ExprNode * collection: ExprNode

and LiteralValue =
    | LitInt    of int
    | LitFloat  of float
    | LitBool   of bool
    | LitString of string

// ── Statements ───────────────────────────────────────────────────────────────

type StmtNode =
    | Assign          of target: string * value: ExprNode
    | Print           of value: ExprNode
    | Read            of target: string * prompt: ExprNode option
    | Return          of value: ExprNode option
    | Break
    | Continue
    | Comment         of text: string
    | If              of condition: ExprNode * thenBranch: StmtNode list * elseBranch: StmtNode list option
    | WhileLoop       of condition: ExprNode * body: StmtNode list
    | Loop            of variable: string * from_: ExprNode * to_: ExprNode * step: ExprNode option * body: StmtNode list
    | ForEach         of variable: string * collection: ExprNode * body: StmtNode list
    | MethodCall      of target: string option * method_: string * args: ExprNode list
    | FunctionCallStmt of name: string * args: ExprNode list
    | FunctionDecl    of name: string * params_: string list * body: StmtNode list
    | IndexedAssign   of target: string * index: ExprNode * value: ExprNode
    // ── Pseudraw — environnement / fenêtre ──────────────────────────────────
    | PseudrawWidth      of value: ExprNode
    | PseudrawHeight     of value: ExprNode
    | PseudrawTitle      of value: ExprNode
    | PseudrawBackground of color: ColorSpec
    | PseudrawFont       of fontName: string
    // ── Pseudraw — timer / boucle de jeu ────────────────────────────────────
    | PseudrawTick       of intervalMs: int
    | PseudrawOnTick     of body: StmtNode list
    | PseudrawPauseTick
    | PseudrawResumeTick
    | PseudrawStop
    // ── Pseudraw — dessin ────────────────────────────────────────────────────
    | PseudrawClear      of target: string option
    | PseudrawDrawLine   of x1: ExprNode * y1: ExprNode * x2: ExprNode * y2: ExprNode * color: ColorSpec * thickness: ExprNode option
    | PseudrawDrawRect   of x: ExprNode * y: ExprNode * w: ExprNode * h: ExprNode * color: ColorSpec * border: ColorSpec option * filled: bool * thickness: ExprNode option
    | PseudrawDrawCircle of x: ExprNode * y: ExprNode * radius: ExprNode * color: ColorSpec * border: ColorSpec option * filled: bool * thickness: ExprNode option
    | PseudrawDrawPolygon of points: (ExprNode * ExprNode) list * color: ColorSpec * filled: bool * thickness: ExprNode option
    | PseudrawDrawPoint  of x: ExprNode * y: ExprNode * color: ColorSpec
    | PseudrawDrawText   of value: ExprNode * x: ExprNode * y: ExprNode * color: ColorSpec * fontSize: ExprNode option * fontName: string option
    | PseudrawDrawObject of target: string
    // ── Pseudraw — son ───────────────────────────────────────────────────────
    | PseudrawPlayTone   of frequency: ExprNode * durationMs: ExprNode
    // ── Pseudraw — géométrie de jeu (données fixes fournies par le compilateur) ─
    // Remplit `target` avec les 4 cellules absolues d'une pièce de Tetris
    // (shape 0-6 = I,O,T,S,Z,J,L ; rotation 0-3) positionnée en (x,y).
    | PseudrawTetrominoCells of shape: ExprNode * rotation: ExprNode * x: ExprNode * y: ExprNode * target: string

// ── Programme ────────────────────────────────────────────────────────────────

type ProgramNode = {
    Functions : StmtNode list   // FunctionDecl extraits
    Algorithm : StmtNode list   // Corps principal
}

// ── Type de projet ──────────────────────────────────────────────────────────

type ProjectType = PseudoConsole | Pseudraw

// ── Détection du type de projet ─────────────────────────────────────────────
// Dérivée du contenu réel de l'AST (présence d'au moins un nœud Pseudraw),
// jamais d'un sniffing de mots-clés sur le texte source ni d'un champ
// auto-déclaré par le LLM — cohérent avec l'absence de syntaxe imposée.

let rec private exprIsPseudraw (expr: ExprNode) : bool =
    match expr with
    | PseudrawKeyPressed _
    | PseudrawPoint _
    | PseudrawRandomPoint _
    | PseudrawDistance _
    | PseudrawOverlaps _
    | PseudrawIsInside _
    | PseudrawIsIn _ -> true
    | Literal _ | Identifier _ | NewList -> false
    | UnaryOp (_, operand) -> exprIsPseudraw operand
    | BinaryOp (_, left, right) -> exprIsPseudraw left || exprIsPseudraw right
    | PropertyAccess _ -> false
    | ArrayAccess (_, index) -> exprIsPseudraw index
    | FunctionCallExpr (_, args) -> args |> List.exists exprIsPseudraw
    | RandomNumber (min_, max_) -> exprIsPseudraw min_ || exprIsPseudraw max_
    | ReadExpr prompt -> prompt |> Option.map exprIsPseudraw |> Option.defaultValue false

let rec private stmtIsPseudraw (stmt: StmtNode) : bool =
    match stmt with
    | PseudrawWidth _ | PseudrawHeight _ | PseudrawTitle _
    | PseudrawBackground _ | PseudrawFont _ | PseudrawTick _
    | PseudrawOnTick _ | PseudrawPauseTick | PseudrawResumeTick
    | PseudrawStop | PseudrawClear _ | PseudrawDrawLine _
    | PseudrawDrawRect _ | PseudrawDrawCircle _ | PseudrawDrawPolygon _
    | PseudrawDrawPoint _ | PseudrawDrawText _ | PseudrawDrawObject _
    | PseudrawPlayTone _ | PseudrawTetrominoCells _ -> true
    | Assign (_, value) -> exprIsPseudraw value
    | IndexedAssign (_, index, value) -> exprIsPseudraw index || exprIsPseudraw value
    | Print value -> exprIsPseudraw value
    | Read (_, prompt) -> prompt |> Option.map exprIsPseudraw |> Option.defaultValue false
    | Return value -> value |> Option.map exprIsPseudraw |> Option.defaultValue false
    | Break | Continue | Comment _ -> false
    | If (condition, thenBranch, elseBranch) ->
        exprIsPseudraw condition
        || thenBranch |> List.exists stmtIsPseudraw
        || (elseBranch |> Option.map (List.exists stmtIsPseudraw) |> Option.defaultValue false)
    | WhileLoop (condition, body) ->
        exprIsPseudraw condition || body |> List.exists stmtIsPseudraw
    | Loop (_, from_, to_, step, body) ->
        exprIsPseudraw from_ || exprIsPseudraw to_
        || (step |> Option.map exprIsPseudraw |> Option.defaultValue false)
        || body |> List.exists stmtIsPseudraw
    | ForEach (_, collection, body) ->
        exprIsPseudraw collection || body |> List.exists stmtIsPseudraw
    | MethodCall (_, _, args) -> args |> List.exists exprIsPseudraw
    | FunctionCallStmt (_, args) -> args |> List.exists exprIsPseudraw
    | FunctionDecl (_, _, body) -> body |> List.exists stmtIsPseudraw

let detectProjectType (program: ProgramNode) : ProjectType =
    if (program.Functions @ program.Algorithm) |> List.exists stmtIsPseudraw
    then Pseudraw
    else PseudoConsole
