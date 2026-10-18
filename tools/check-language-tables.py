"""Checks the Colors/ and Keys/ language tables of the compiler.

 - every value must be a real .NET name (Color.FromName / System.Windows.Forms.Keys member);
 - a word must not map to DIFFERENT values in two languages (all tables are merged at run time).
Usage: python tools/check-language-tables.py        (exit code 1 on any problem)
"""
import glob, json, os, sys, collections
root = os.path.join(os.path.dirname(__file__), '..', 'PseudoNet.Compiler', 'PseudoNet.Compiler')
KNOWN = {
  'Colors': set("""red green blue yellow orange purple violet cyan gray white black brown pink gold silver magenta
                   turquoise navy skyblue lightblue steelblue lightgreen darkgreen lightgray darkgray lime teal beige""".split()),
  'Keys': set("Up Down Left Right Space Enter Escape Back Delete Tab ShiftKey ControlKey Menu".split()),
}
problems = 0
for kind in ('Colors', 'Keys'):
    seen = collections.defaultdict(dict)
    for f in sorted(glob.glob(os.path.join(root, kind, '*.json'))):
        lang = f.split('.')[-2]
        for word, value in json.load(open(f, encoding='utf-8')).items():
            if value not in KNOWN[kind]:
                print(f'{kind}: {lang}: {word!r} -> unknown .NET name {value!r}'); problems += 1
            seen[word.strip().lower()][lang] = value
    for word, per_lang in seen.items():
        if len(set(per_lang.values())) > 1:
            print(f'{kind}: CONFLICT {word!r}: {per_lang}'); problems += 1
    print(f'{kind}: {len(seen)} distinct words checked')
print('OK' if problems == 0 else f'{problems} problem(s)')
sys.exit(1 if problems else 0)
