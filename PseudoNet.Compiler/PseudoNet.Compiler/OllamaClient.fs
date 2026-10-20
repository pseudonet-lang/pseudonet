module OllamaClient

open System
open System.Net.Http
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading.Tasks

// ── Configuration ────────────────────────────────────────────────────────────

let private ollamaBase =
    match Environment.GetEnvironmentVariable("PSEUDONET_OLLAMA_URL") with
    | null | "" -> "http://localhost:11434"
    | url -> url.TrimEnd('/')
let private ollamaUrl = ollamaBase + "/api/chat"
let modelName = "qwen2.5-coder:7b"

let private numPredict = 16000
let private numCtx = 28000
let private userPrefix = "Parse this pseudocode into a JSON AST:\n\n"

/// Levée quand Ollama est injoignable, sans modèle, ou trop lent : le
/// compilateur la distingue d'une erreur de compilation (code de sortie 2).
exception OllamaUnavailable of string

let private httpClient =
    let c = new HttpClient()
    c.Timeout <- TimeSpan.FromSeconds(590.0)
    c

// ── Prompt système ───────────────────────────────────────────────────────────
// Porté depuis l'ancien parser Python (archivé dans _archive/), puis étendu avec la sémantique
// Pseudraw (fenêtre, timer, clavier, dessin, géométrie). Toujours : reconnaître
// l'INTENTION quelle que soit la formulation — jamais imposer une syntaxe.

let private systemPrompt = """
You are a precise JSON AST generator for Pseudo.NET, a pseudocode language
with NO imposed syntax. The same concept can be expressed in many different ways.

SEMANTIC CONCEPTS — recognize the INTENT regardless of exact wording:

ASSIGNMENT: Any statement setting a variable to a value →
  kind=Assign, target=<variable name>, value=<expression>

PRINT/DISPLAY: Any statement outputting a value to the console →
  kind=Print, value=<expression>

READ/INPUT: Any statement reading a value from the user →
  kind=Read, target=<variable name>, prompt=<optional prompt expression>

FUNCTION CALL AS STATEMENT: Calling a named function →
  kind=FunctionCallStmt, name=<function name>, args=[...]

FUNCTION CALL AS EXPRESSION: Using a function result in an expression →
  kind=FunctionCallExpr, name=<function name>, args=[...]

CONDITIONAL: Any if/then/else construct →
  kind=If, condition=<expr>, then=[...statements...], else_=[...statements...]
  If the pseudocode has an else branch, you MUST include it as else_ — never
  drop it. If there is no else branch, omit else_ entirely (or set it to null).

WHILE LOOP: Any loop with a condition →
  kind=WhileLoop, condition=<expr>, body=[...statements...]
  "repeat UNTIL C" / "répéter jusqu'à ce que C" / "repetir hasta que C" keeps looping while C is NOT yet true →
  condition = the NEGATION of C. "repeat until n = 100" →
  {"kind":"WhileLoop","condition":{"kind":"UnaryOp","op":"not","operand":{"kind":"BinaryOp","op":"=","left":{"kind":"Identifier","name":"n"},"right":{"kind":"Literal","value":100}}},"body":[...]}

COUNT LOOP: Any loop iterating over a range →
  kind=Loop, variable=<name>, from_=<start>, to=<end>, body=[...statements...]

FOR EACH: Any loop iterating over a collection →
  kind=ForEach, variable=<name>, collection=<expr>, body=[...statements...]

FUNCTION DEFINITION: Any named reusable block →
  kind=FunctionDecl, name=<name>, params=[...], body=[...statements...]

RETURN: Returning a value from a function →
  kind=Return, value=<optional expression>

NEW EMPTY LIST: Creating a new empty list/collection →
  kind=NewList

RANDOM INTEGER: Generating a random integer in a range →
  kind=RandomNumber, min=<expr>, max=<expr>

COLLECTION APPEND: Adding an element to the END of a collection ("add X to Y",
"append X to Y") →
  kind=MethodCall, target=<collection name>, method=Add, args=[<element>]

COLLECTION PREPEND: Inserting an element at the FRONT of a collection ("insert
X at the front of Y", "prepend X to Y") →
  kind=MethodCall, target=<collection name>, method=InsertFirst, args=[<element>]

COLLECTION REMOVE FIRST/LAST: Removing the first or last element of a collection
("remove the first/last element from Y") →
  kind=MethodCall, target=<collection name>, method=RemoveFirst (or RemoveLast), args=[]
  This is a statement with an EMPTY args array — never fold the collection name
  into the "name"/"method" text itself.

OPERATORS: kind=BinaryOp, op, left, right. The ONLY allowed op values are:
+ - * / % = <> < <= > >= and or. Joining/concatenating text or a text with a value
("followed by", "suivi de", "concatenated with", "concaténé à", "puis") is op="+".
  Example: "afficher 'Bonjour, ' suivi du nom" →
  {"kind":"Print","value":{"kind":"BinaryOp","op":"+","left":{"kind":"Literal","value":"Bonjour, "},"right":{"kind":"Identifier","name":"nom"}}}
Never invent another op name such as "concat", "join" or "append".

NEGATION: Any logical negation of a condition ("not X", "non X", "si non X",
"X is false", "X est faux", "X n'est pas vrai") →
  kind=UnaryOp, op="not", operand=<the negated condition>
  Examples: "if not gameOver"      → {"kind":"UnaryOp","op":"not","operand":{"kind":"Identifier","name":"gameOver"}}
            "si non partieTerminee" → {"kind":"UnaryOp","op":"not","operand":{"kind":"Identifier","name":"partieTerminee"}}
  Never express a negation as a comparison with true/false.

DIVISIBILITY: "n is divisible by 3", "n est multiple de 3", "n es múltiplo de 3", "n modulo 3 is 0" →
  a COMPARISON of the remainder with 0, never the remainder alone:
  {"kind":"BinaryOp","op":"=","left":{"kind":"BinaryOp","op":"%","left":{"kind":"Identifier","name":"n"},"right":{"kind":"Literal","value":3}},"right":{"kind":"Literal","value":0}}
  "divisible by 3 or by 5" = two such comparisons joined by op "or". A remainder is a number, never a condition.

LIST ELEMENT: Reading an element of a list ("the first element of Y", "numbers[i]") →
  kind=ArrayAccess, array=<list name>, index=<expr>   (first element: index 0)
  Writing one ("numbers[i] = x") → kind=IndexedAssign, target=<list name>, index=<expr>, value=<expr>

PROPERTY ACCESS: Reading a named field of a value (e.g. a point's x or y —
"seg.x", "the x of seg") →
  kind=PropertyAccess, obj=<value name, a plain string>, property=<field name>
  "obj" is always a plain identifier string, never a nested expression node.

BINARY OPERATORS: use "or", "and", "!=" — never "||", "&&", "<>"
LITERALS: strings in quotes, integers, floats, true/false booleans

── PSEUDRAW — window, game loop, keyboard, drawing (only when the pseudocode
   actually describes a window / drawing / game — plain algorithms never need
   these) ─────────────────────────────────────────────────────────────────────

WINDOW / CANVAS CONFIGURATION: Setting up the drawing window's size, title,
background color or font, in ANY phrasing/language →
  width N       → kind=PseudrawWidth, value=<expr>
  height N      → kind=PseudrawHeight, value=<expr>
  title "..."   → kind=PseudrawTitle, value=<expr>
  background C  → kind=PseudrawBackground, color=<color>
  font "..."    → kind=PseudrawFont, fontName=<string>
  Examples: "window 800 by 600", "fenêtre de 800 sur 600", "fond noir",
  "titre Mon Jeu", "police Consolas".

GAME LOOP / TICK: Any block meant to run repeatedly — every frame, on a timer,
"each cycle", "à chaque tour", "boucle de jeu" →
  kind=PseudrawOnTick, body=[...statements...]
  If an interval is stated ("every 100ms", "toutes les 100 millisecondes"),
  also emit a sibling kind=PseudrawTick, intervalMs=<int>. If the text clearly
  wants continuous animation but states no interval, still emit
  kind=PseudrawTick, intervalMs=100 as a reasonable default.
  PAUSE / RESUME / STOP the loop → kind=PseudrawPauseTick / PseudrawResumeTick / PseudrawStop

KEYBOARD INPUT: Any expression testing whether a key is held down or was just
pressed →
  kind=PseudrawKeyPressed, key=<key name>, press="held" OR "just_pressed" (REQUIRED)
  press="just_pressed" → momentary/single press: "tapped", "just pressed", "vient d'être appuyée"
  press="held"         → continuous/held press: "pressed", "held", "down", "est enfoncée"
  Examples: "la touche gauche est enfoncée" → {"kind":"PseudrawKeyPressed","key":"left","press":"held"}
            "if key space is held down"     → {"kind":"PseudrawKeyPressed","key":"space","press":"held"}
            "la touche haut vient d'être appuyée" → {"kind":"PseudrawKeyPressed","key":"up","press":"just_pressed"}
            "if key R tapped"               → {"kind":"PseudrawKeyPressed","key":"r","press":"just_pressed"}
  Without any "just pressed"/"tapped" wording, press is "held".
  Decide press for EACH key condition from that condition's own wording only.
  Consecutive key conditions often differ (e.g. left/right/down "est enfoncée"
  → "held", but up "vient d'être appuyée" → "just_pressed"): never copy the
  press value from a neighbouring condition.
  Key names: up, down, left, right, space, enter, escape, shift, ctrl, alt,
  tab, backspace, delete, f1-f12, single letters, single digits.

DRAWING PRIMITIVES: Any statement drawing on the canvas →
  CLEAR:       kind=PseudrawClear, target=<optional collection name — null clears the canvas>
  LINE:        kind=PseudrawDrawLine, x1=<expr>, y1=<expr>, x2=<expr>, y2=<expr>, color=<color>, thickness=<optional expr>
  RECTANGLE:   kind=PseudrawDrawRect, style="filled" OR "outline" (REQUIRED, always right after kind), x=<expr>, y=<expr>, w=<expr>, h=<expr>, color=<color>, border=<optional color>, thickness=<optional expr>
  CIRCLE:      kind=PseudrawDrawCircle, style="filled" OR "outline" (REQUIRED, always right after kind), x=<expr>, y=<expr>, radius=<expr>, color=<color>, border=<optional color>, thickness=<optional expr>
  POLYGON:     kind=PseudrawDrawPolygon, style="filled" OR "outline" (REQUIRED, always right after kind), points=[[x,y], ...], color=<color>, thickness=<optional expr>
  POINT/PIXEL: kind=PseudrawDrawPoint, x=<expr>, y=<expr>, color=<color>
  TEXT:        kind=PseudrawDrawText, value=<expr>, x=<expr>, y=<expr>, color=<color>, fontSize=<optional expr>, fontName=<optional string>
  "filled"/"rempli"/"plein"/"solide" → style="filled". "outline"/"contour"/"vide" → style="outline".
  Examples: "dessiner un rectangle plein en (0,0) taille (18,18) couleur rouge" →
            {"kind":"PseudrawDrawRect","style":"filled","x":0,"y":0,"w":18,"h":18,"color":"red"}
            "draw a rectangle outline at (0,0) size (200,400) color gray" →
            {"kind":"PseudrawDrawRect","style":"outline","x":0,"y":0,"w":200,"h":400,"color":"gray"}
  COLOR: either a color name string (e.g. "black", "red" — translate French
  color words like "noir","rouge" to their English name) or {r:N,g:N,b:N[,a:N]}.

GEOMETRY HELPERS:
  POINT:                      kind=PseudrawPoint, x=<expr>, y=<expr>   ("point (10, 10)" → {"kind":"PseudrawPoint","x":10,"y":10})
  RANDOM POINT IN A RECTANGLE: kind=PseudrawRandomPoint, x1=<expr>, y1=<expr>, x2=<expr>, y2=<expr>
  DISTANCE BETWEEN TWO POINTS: kind=PseudrawDistance, from=<expr>, to=<expr>
  RECTANGLE OVERLAP/COLLISION: kind=PseudrawOverlaps, left=<expr>, right=<expr>
  POINT IS INSIDE A RECTANGLE:  kind=PseudrawIsInside, point=<expr>, rect=<expr>
  VALUE IS IN A COLLECTION:     kind=PseudrawIsIn, value=<expr>, collection=<expr>

SOUND: Playing a musical note, tone, or beep at a given pitch for a duration
("play a tone at 440 Hz for 200 ms", "beep at 523 Hz for a quarter second",
"play the note" — always express pitch as a frequency in Hz and duration in
milliseconds, even if the pseudocode names a note like "A4" or "middle C":
use your knowledge of musical pitches to convert it to Hz) →
  kind=PseudrawPlayTone, frequency=<expr, Hz>, durationMs=<expr, milliseconds>
  A melody is simply a sequence of these statements, one per note, in order.

TETROMINO SHAPE: Filling a list with the 4 board cells of a Tetris piece at a
given shape and rotation, positioned at an origin ("set pieceCells to the
cells of shape currentShape at rotation currentRotation positioned at
(pieceX, pieceY)") →
  kind=PseudrawTetrominoCells, shape=<expr, integer 0-6>, rotation=<expr, integer 0-3>,
  x=<expr>, y=<expr>, target=<list name receiving the 4 cells>
  shape values: 0=I, 1=O, 2=T, 3=S, 4=Z, 5=J, 6=L (the seven standard Tetris
  pieces). This clears target first, then fills it with exactly 4 points —
  never compute tetromino cell offsets yourself with arithmetic, always use
  this primitive.

Output ONLY valid JSON matching the ProgramNode schema.
kind values must be exactly: Program, Assign, Print, Read, Return, Break,
Continue, Comment, If, WhileLoop, Loop, ForEach, MethodCall, FunctionCallStmt,
FunctionDecl, Literal, Identifier, BinaryOp, UnaryOp, PropertyAccess,
ArrayAccess, FunctionCallExpr, NewList, RandomNumber — plus, only when the
pseudocode describes a window/drawing/game: PseudrawWidth, PseudrawHeight,
PseudrawTitle, PseudrawBackground, PseudrawFont, PseudrawTick, PseudrawOnTick,
PseudrawPauseTick, PseudrawResumeTick, PseudrawStop, PseudrawClear,
PseudrawDrawLine, PseudrawDrawRect, PseudrawDrawCircle, PseudrawDrawPolygon,
PseudrawDrawPoint, PseudrawDrawText, PseudrawDrawObject, PseudrawPoint,
PseudrawRandomPoint, PseudrawDistance, PseudrawOverlaps, PseudrawIsInside,
PseudrawIsIn, PseudrawPlayTone, PseudrawTetrominoCells.
NO Node suffix. NO explanation. NO markdown.
"""

/// Empreinte de tout ce qui influence la réponse du modèle, SAUF le pseudocode
/// lui-même : modèle, prompt système, préfixe utilisateur, options. Sert de
/// clé de cache avec le texte du pseudocode.
let fingerprint : string =
    let material = String.Join("|", [ modelName; systemPrompt; userPrefix; string numPredict; string numCtx; "temp=0" ])
    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(material)))

// ── Disponibilité ────────────────────────────────────────────────────────────

/// None si Ollama répond et possède le modèle ; sinon un message actionnable.
let checkAvailableAsync () : Task<string option> =
    task {
        try
            use cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5.0))
            let! response = httpClient.GetAsync(ollamaBase + "/api/tags", cts.Token)
            let! body = response.Content.ReadAsStringAsync()
            use doc = JsonDocument.Parse(body)
            let present =
                doc.RootElement.GetProperty("models").EnumerateArray()
                |> Seq.exists (fun m ->
                    let n = m.GetProperty("name").GetString()
                    n = modelName || n.StartsWith(modelName.Split(':').[0] + ":" + modelName.Split(':').[1]))
            if present then return None
            else return Some (Messages.t "ollama.modelMissing" "Ollama is running but the model {0} is missing - run: ollama pull {0}" [ box modelName ])
        with _ ->
            return Some (Messages.t "ollama.checkUnreachable" "Ollama is unreachable at {0} - run: ollama serve" [ box ollamaBase ])
    }

// ── Appel Ollama ─────────────────────────────────────────────────────────────

let private chatAsync (messages: JsonArray) : Task<string> =
    task {
        let options = JsonObject()
        options.["temperature"]  <- JsonValue.Create(0.0)
        options.["num_predict"]  <- JsonValue.Create(numPredict)
        options.["num_ctx"]      <- JsonValue.Create(numCtx)

        let payload = JsonObject()
        payload.["model"]    <- JsonValue.Create(box modelName)
        payload.["stream"]   <- JsonValue.Create(false)
        payload.["format"]   <- JsonValue.Create(box "json")
        payload.["options"]  <- options
        payload.["messages"] <- messages

        try
            use content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
            let! response = httpClient.PostAsync(ollamaUrl, content)
            let! body = response.Content.ReadAsStringAsync()

            if not response.IsSuccessStatusCode then
                return failwith (Messages.t "ollama.http" "Ollama returned {0}: {1}" [ box response.StatusCode; box body ])
            else
                use doc = JsonDocument.Parse(body)
                let text =
                    doc.RootElement
                       .GetProperty("message")
                       .GetProperty("content")
                       .GetString()
                return text
        with
        | :? HttpRequestException as ex ->
            return raise (OllamaUnavailable(
                Messages.t "ollama.unreachable" "Ollama is unreachable at {0} - run: ollama serve (and ollama pull {1} if the model is missing). {2}" [ box ollamaBase; box modelName; box ex.Message ]))
        | :? TaskCanceledException as ex ->
            return raise (OllamaUnavailable(
                Messages.t "ollama.timeout" "Timed out waiting for Ollama at {0} - the model may be slow, try again." [ box ollamaBase ]))
    }

let private message (role: string) (content: string) : JsonObject =
    let m = JsonObject()
    m.["role"] <- JsonValue.Create(box role)
    m.["content"] <- JsonValue.Create(box content)
    m

let private baseMessages (pseudocode: string) : JsonArray =
    let messages = JsonArray()
    messages.Add(message "system" systemPrompt)
    messages.Add(message "user" (userPrefix + pseudocode))
    messages

let parseAsync (pseudocode: string) : Task<string> =
    chatAsync (baseMessages pseudocode)

/// Second tour : on renvoie au modèle SA réponse précédente et la liste précise des problèmes
/// structurels qu'on y a trouvés, et on lui demande le JSON complet corrigé. Ne touche jamais
/// au pseudocode de l'utilisateur.
let refineAsync (pseudocode: string) (previousJson: string) (problems: string list) : Task<string> =
    let messages = baseMessages pseudocode
    messages.Add(message "assistant" previousJson)
    let list = problems |> List.map (fun p -> "- " + p) |> String.concat "\n"
    messages.Add(message "user"
        ("Your JSON AST has structural problems:\n" + list +
         "\nReturn the COMPLETE corrected JSON AST (same format, every statement kept), fixing only these problems. " +
         "Use only the kind values listed in the instructions; a condition must be an expression node, never text."))
    chatAsync messages
