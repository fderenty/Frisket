#!/usr/bin/env python3
"""Builds a specimen sheet for choosing which typefaces - and which cuts - to keep.

One card per *family*, the unit FontLibrary actually keys on. That matters
because these archives split their weights into families of their own: Alan Sans
Regular and Bold both register as "Alan Sans", while its SemiBold registers as
"Alan Sans SemiBold" - a separate family a template would have to name in full.
So dropping the weights that say nothing in a demo is a per-family decision, and
this is where it is made. Cards are grouped by folder, with per-folder shortcuts
for the common case: keep the base family, drop the rest.

The page cannot move anything itself. It collects the verdicts and writes out
the PowerShell that does, which is the half that touches the disk and therefore
the half a human should read before running.

    python tools/triage.py        # writes tests/output/fonts/triage.html

The embedded specimens are cut down to the characters shown, or the page would
weigh 27 MB. That is a modification in the OFL's sense, which the licence allows
outright - the only restriction it puts on a Modified Version is the Reserved
Font Name, and only "as presented to the users" of a *distribution*. This page
is a local preview under tests/output/, which is ignored by git and never shipped.
The files under engine/Fonts are never touched.

Needs fonttools:  pip install --user fonttools
"""

import base64, glob, io, os, sys

try:
    from fontTools.ttLib import TTFont
    from fontTools import subset
except ImportError:
    sys.exit("fonttools is missing:  pip install --user fonttools")

# Anchored on the repository rather than on where this file sits: the tool moved to
# tools/ on 2 August and silently found no typefaces at all, because it had been
# looking beside itself. Walking up to the marker is what the test suite already does
# (RepositoryRoot), and it survives the next move.
def _root():
    here = os.path.dirname(os.path.abspath(__file__))
    while True:
        if os.path.isdir(os.path.join(here, "engine", "Fonts")):
            return here
        up = os.path.dirname(here)
        if up == here:
            raise SystemExit("no engine/Fonts above " + os.path.dirname(__file__))
        here = up


ROOT = _root()
HERE = os.path.join(ROOT, "engine", "Fonts")
OUT = os.path.join(ROOT, "tests", "output", "fonts")

# Where a rejected file goes. Outside engine/Fonts on purpose: FontLibrary
# .RegisterDirectory walks that tree recursively, so anything renamed in place
# would still be loaded, still measured and still drawn in test 33.
REJECTED = "engine/Fonts-rejected"

SPECIMEN = (
    "Report FR-2025-0481"
    "Annual survey of field installations, carried out between 3 and 27 July "
    "2025. This document is a record and is to be kept for the statutory period."
    "Development of the rendering engine 12,480.00"
    "Integration and acceptance 3,275.50"
    "Total 18,906.60"
    # The accented and quoted forms stay: they are what separates a face that can
    # set a European document from one that only looks as though it can.
    "0123456789 — Il1lI0O — àâçéèêëîïôùûü ÀÉÈÊÎÔÙ « guillemets » “quotes”"
    "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"
)

WEIGHTS = ("Thin", "ExtraLight", "Light", "Medium", "SemiBold", "ExtraBold",
           "Black", "Italic")


def name(table, nid):
    rec = table.getName(nid, 3, 1, 0x409)
    if rec is None:
        found = [r for r in table.names if r.nameID == nid]
        rec = found[0] if found else None
    return rec.toUnicode() if rec else None


def licence_of(folder):
    """The licence file that must travel with anything moved out of here."""
    for pattern in ("OFL.txt", "LICENSE.txt", "LICENCE.txt", "OFL*.txt"):
        hit = sorted(glob.glob(os.path.join(folder, pattern)))
        if hit:
            return os.path.basename(hit[0]), ""
    for pattern in ("OFL*.txt", "LICEN*.txt"):
        hit = sorted(glob.glob(os.path.join(folder, "misc", pattern)))
        if hit:
            return os.path.basename(hit[0]), "misc"
    return None, None


def specimen_face(path):
    """The font cut down to what the card shows, as a base64 data URI."""
    font = TTFont(path)
    options = subset.Options()
    options.layout_features = ["*"]
    options.notdef_outline = True
    options.drop_tables += ["DSIG"]
    options.recalc_bounds = False
    sub = subset.Subsetter(options=options)
    sub.populate(text=SPECIMEN)
    sub.subset(font)
    buf = io.BytesIO()
    font.save(buf)
    return base64.b64encode(buf.getvalue()).decode("ascii")


# ---------------------------------------------------------------------------
# Gather: folder -> family -> the files that register under it
# ---------------------------------------------------------------------------
library = {}
for folder in sorted(d for d in os.listdir(HERE)
                     if os.path.isdir(os.path.join(HERE, d))):
    path = os.path.join(HERE, folder)
    families = {}
    for f in sorted(glob.glob(os.path.join(path, "*.ttf"))
                    + glob.glob(os.path.join(path, "*.otf"))):
        try:
            font = TTFont(f, lazy=True)
            table = font["name"]
            # Bold and italic exactly as FontFile reads them: head.macStyle bits
            # 0 and 1. The subfamily string is a label; these are what the engine
            # keys the library on.
            mac = font["head"].macStyle
        except Exception:
            continue
        families.setdefault(name(table, 1) or "?", []).append(
            (os.path.basename(f), name(table, 2) or "?", bool(mac & 1), bool(mac & 2)))
    if families:
        library[folder] = families

groups, faces, total_families = [], [], 0
embedded = {}          # file path -> CSS family name, so a file is embedded once


def face_for(path):
    """A CSS family for this exact file, cut down to the specimen."""
    if path not in embedded:
        blob = specimen_face(path)
        kind = "otf" if path.lower().endswith(".otf") else "ttf"
        fmt = "opentype" if kind == "otf" else "truetype"
        css = "s" + str(len(embedded))
        faces.append(f"@font-face{{font-family:'{css}';src:url(data:font/{kind};"
                     f"base64,{blob}) format('{fmt}')}}")
        embedded[path] = css
    return embedded[path]


SPEC_BLOCK = """
        <p class="h">Report FR-2025-0481</p>
        <p class="body">Annual survey of field installations, carried out between
           3 and 27 July 2025. This document is a record.</p>
        <p class="row"><span>Rendering engine</span>
           <span class="num">12,480.00</span></p>
        <p class="row total"><span>Total</span>
           <span class="num">18,906.60</span></p>
        <p class="small">0123456789 Il1lI0O àâçéèêëîïôù « guillemets »</p>"""

for folder, families in library.items():
    licence, licence_dir = licence_of(os.path.join(HERE, folder))
    cards = []
    for family in sorted(families):
        files = sorted(families[family])
        total_families += 1

        cuts = {(b, i): fn for fn, _, b, i in files}

        def resolve(bold, italic):
            """FontLibrary.Resolve, for one family, step for step."""
            if (bold, italic) in cuts:
                return cuts[(bold, italic)], True
            if bold and italic:
                if (True, False) in cuts:
                    return cuts[(True, False)], False
                if (False, True) in cuts:
                    return cuts[(False, True)], False
            if (False, False) in cuts:
                return cuts[(False, False)], False
            # A family with no plain cut at all - "Bold Italic" only, say - still
            # answers with something of its own rather than nothing.
            return files[0][0], False

        def describe(filename):
            bold, italic = next((b, i) for fn, _, b, i in files if fn == filename)
            return ("gras italique" if bold and italic else
                    "gras" if bold else "italique" if italic else "romain")

        # What Resolve(family, bold, italic) actually hands the renderer - not what
        # the file names suggest. The engine synthesises nothing, so where a cut is
        # missing this shows the substitute it really draws. Letting CSS do it with
        # font-weight:bold would invent a bold the engine will never produce.
        variants = []
        for label, want in (("Romain", (False, False)),
                            ("Gras", (True, False)),
                            ("Italique", (False, True))):
            filename, exact = resolve(*want)
            css = face_for(os.path.join(HERE, folder, filename))
            note = ("" if exact else
                    f'<span class="faux">le moteur rendra le {describe(filename)}</span>')
            variants.append(f"""
      <div class="variant{'' if exact else ' is-missing'}">
        <p class="vlabel">{label}{note}</p>
        <div class="sheet" style="font-family:'{css}'">{SPEC_BLOCK}
        </div>
      </div>""")

        base = not any(family.endswith(" " + w) for w in WEIGHTS)
        subs = ", ".join(sorted({s for _, s, _, _ in files}))
        size = sum(os.path.getsize(os.path.join(HERE, folder, f)) for f, *_ in files)
        lacks = [n for n, want in (("gras", (True, False)), ("italique", (False, True)))
                 if want not in cuts]
        warn = (' · <span class="warn">pas de ' + " ni d'".join(lacks) + "</span>"
                if lacks else "")
        cards.append(f"""
  <article class="card{' is-base' if base else ''}" data-folder="{folder}"
           data-family="{family}" data-base="{'1' if base else '0'}"
           data-files='{",".join(f for f, *_ in files)}'>
    <header>
      <div>
        <h3>{family}{' <span class="tag">base</span>' if base else ''}</h3>
        <p class="meta">{subs} · {len(files)} fichier(s) · {size/1024:.0f} Ko{warn}</p>
      </div>
      <div class="verdict">
        <button class="keep" data-v="keep">Garder</button>
        <button class="drop" data-v="drop">Écarter</button>
      </div>
    </header>
    <div class="variants">{''.join(variants)}
    </div>
  </article>""")

    groups.append(f"""
<section class="group" data-folder="{folder}">
  <div class="ghead">
    <h2>{folder}</h2>
    <span class="gmeta">{len(families)} famille(s){'' if licence else
      ' · <span class="warn">pas de fichier de licence trouvé</span>'}</span>
    <span class="gactions">
      <button data-g="keep">Tout garder</button>
      <button data-g="base">Ne garder que la base</button>
      <button data-g="drop">Tout écarter</button>
    </span>
  </div>
  {''.join(cards)}
</section>""")

licences = {f: licence_of(os.path.join(HERE, f)) for f in library}
lic_js = "{" + ",".join(
    f'"{f}":["{l[0]}","{l[1]}"]' for f, l in licences.items() if l[0]) + "}"

page = f"""<!doctype html>
<html lang="fr"><head><meta charset="utf-8">
<title>Tri des fontes — famille par famille</title>
<style>
{''.join(faces)}
*{{box-sizing:border-box}}
body{{margin:0;padding:24px;background:#1e2127;color:#dfe3e8;
     font:14px/1.5 system-ui,sans-serif}}
h1{{font-size:18px;margin:0 0 4px}}
.lead{{color:#9aa4b2;margin:0 0 20px;max-width:76ch}}
.lead code{{color:#c9d1d9}}
.bar{{position:sticky;top:0;z-index:5;background:#1e2127;padding:12px 0;
      border-bottom:1px solid #333a45;margin-bottom:20px;display:flex;
      gap:10px;align-items:center;flex-wrap:wrap}}
button{{font:inherit;font-size:13px;padding:6px 12px;border-radius:6px;
        cursor:pointer;border:1px solid #4a5361;background:#2a2f38;color:#dfe3e8}}
button.primary{{background:#2f6feb;border-color:#2f6feb;color:#fff}}
.tally{{color:#9aa4b2;margin-left:auto}}
.group{{margin-bottom:28px}}
.ghead{{display:flex;align-items:center;gap:12px;flex-wrap:wrap;
        padding:8px 0;border-bottom:1px solid #333a45;margin-bottom:12px}}
.ghead h2{{font-size:15px;margin:0;color:#c9d1d9}}
.gmeta{{font-size:12px;color:#9aa4b2}}
.gactions{{margin-left:auto;display:flex;gap:6px}}
.warn{{color:#f0a04b}}
.card{{background:#252a32;border:1px solid #333a45;border-radius:10px;
       margin-bottom:10px;overflow:hidden}}
.card.is-base{{border-left:3px solid #6b7688}}
.card.is-keep{{border-color:#2ea043;box-shadow:inset 3px 0 0 #2ea043}}
.card.is-drop{{border-color:#d1444b;opacity:.45}}
header{{display:flex;justify-content:space-between;align-items:flex-start;
        gap:16px;padding:10px 14px;font-family:system-ui,sans-serif}}
h3{{font-size:14px;margin:0 0 2px}}
.tag{{font-size:10px;background:#3a4351;color:#c9d1d9;border-radius:4px;
      padding:1px 5px;vertical-align:middle;margin-left:4px}}
.meta{{margin:0;font-size:12px;color:#9aa4b2}}
.verdict{{display:flex;gap:6px;flex:none}}
.card.is-keep .keep{{background:#2ea043;border-color:#2ea043;color:#fff}}
.card.is-drop .drop{{background:#d1444b;border-color:#d1444b;color:#fff}}
/* Roman, bold and italic side by side, each set in its own real file. */
.variants{{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));
           gap:1px;background:#333a45;border-top:1px solid #333a45}}
.variant{{background:#fff;display:flex;flex-direction:column}}
.variant.is-missing .sheet{{background:#fbfbfb}}
.vlabel{{font:600 11px/1.4 system-ui,sans-serif;color:#5c6673;margin:0;
         padding:6px 14px 0;display:flex;gap:8px;align-items:baseline;
         justify-content:space-between}}
.faux{{font-weight:400;color:#b06a12;font-size:10px}}
/* Black on white at print sizes: that is the question being asked. Note there
   is no font-weight or font-style here - each block carries the actual cut. */
.sheet{{background:#fff;color:#111;padding:8px 14px 14px;flex:1}}
.h{{font-size:13pt;margin:0 0 6px}}
.body{{font-size:9.5pt;margin:0 0 6px}}
.row{{font-size:9.5pt;margin:0;display:flex;justify-content:space-between;
      gap:10px;border-bottom:1px solid #ddd;padding:2px 0}}
.row.total{{border-bottom:none;border-top:2px solid #111}}
.num{{font-variant-numeric:tabular-nums;white-space:nowrap}}
.small{{font-size:8pt;color:#333;margin:6px 0 0}}
textarea{{width:100%;height:220px;background:#11141a;color:#dfe3e8;
          border:1px solid #333a45;border-radius:8px;padding:12px;
          font:12px/1.5 ui-monospace,monospace}}
#out{{display:none;margin:16px 0}}
</style></head><body>

<h1>Tri des fontes — famille par famille</h1>
<p class="lead">Une carte par <strong>famille</strong>, telle que le moteur
l'enregistre. <code>Alan Sans</code> porte son romain et son gras&nbsp;;
<code>Alan Sans SemiBold</code> est une famille à part, qu'un gabarit devrait
nommer en toutes lettres. <em>Ne garder que la base</em> écarte d'un coup les
graisses intermédiaires d'une fonte.</p>

<div class="bar">
  <button id="all-keep">Tout garder</button>
  <button id="only-base">Ne garder que les bases</button>
  <button id="reset">Réinitialiser</button>
  <button class="primary" id="make">Générer les commandes</button>
  <span class="tally" id="tally"></span>
</div>

<div id="out">
  <h2 style="font-size:15px">À lancer depuis la racine du dépôt (PowerShell)</h2>
  <textarea id="script" readonly></textarea>
</div>

{''.join(groups)}

<script>
const KEY = "frisket-font-triage-families";
// The project was called EasyReport until 1 August 2026. Verdicts already made
// under the old key are carried over rather than silently lost - sixty-eight
// decisions is not something to ask anyone to make twice.
const OLD_KEY = "easyreport-font-triage-families";
const LIC = {lic_js};
const verdicts = JSON.parse(localStorage.getItem(KEY)
                        || localStorage.getItem(OLD_KEY) || "{{}}");
const cards = [...document.querySelectorAll(".card")];
const idOf = c => c.dataset.folder + "|" + c.dataset.family;

function paint() {{
  let keep = 0, drop = 0;
  for (const c of cards) {{
    const v = verdicts[idOf(c)];
    c.classList.toggle("is-keep", v === "keep");
    c.classList.toggle("is-drop", v === "drop");
    if (v === "keep") keep++;
    if (v === "drop") drop++;
  }}
  document.getElementById("tally").textContent =
    `${{keep}} gardée(s), ${{drop}} écartée(s), ${{cards.length - keep - drop}} sans avis`;
  localStorage.setItem(KEY, JSON.stringify(verdicts));
}}

document.addEventListener("click", e => {{
  const b = e.target.closest("button");
  if (!b) return;

  if (b.closest(".verdict")) {{
    const id = idOf(b.closest(".card"));
    verdicts[id] = verdicts[id] === b.dataset.v ? undefined : b.dataset.v;
    if (!verdicts[id]) delete verdicts[id];
    return paint();
  }}
  if (b.dataset.g) {{
    const folder = b.closest(".group").dataset.folder;
    for (const c of cards.filter(c => c.dataset.folder === folder))
      verdicts[idOf(c)] = b.dataset.g === "base"
        ? (c.dataset.base === "1" ? "keep" : "drop") : b.dataset.g;
    return paint();
  }}
}});

document.getElementById("all-keep").onclick = () => {{
  for (const c of cards) verdicts[idOf(c)] = "keep"; paint();
}};
document.getElementById("only-base").onclick = () => {{
  for (const c of cards) verdicts[idOf(c)] = c.dataset.base === "1" ? "keep" : "drop";
  paint();
}};
document.getElementById("reset").onclick = () => {{
  for (const k of Object.keys(verdicts)) delete verdicts[k]; paint();
}};

document.getElementById("make").onclick = () => {{
  const dropped = cards.filter(c => verdicts[idOf(c)] === "drop");
  const lines = [];
  if (!dropped.length) {{
    lines.push("# Rien d'écarté - il n'y a rien à déplacer.");
  }} else {{
    lines.push("# Sort les familles écartées de l'arbre que");
    lines.push("# FontLibrary.RegisterDirectory balaie. Le texte de licence suit");
    lines.push("# les fichiers: tant qu'ils sont dans le dépôt, l'OFL veut qu'il");
    lines.push("# soit à côté d'eux.");
    lines.push("# Supprimer ensuite: Remove-Item -Recurse -Force {REJECTED}");
    lines.push("");

    // A folder losing every one of its families moves whole, licence and all.
    const byFolder = {{}};
    for (const c of dropped) (byFolder[c.dataset.folder] ??= []).push(c);

    for (const folder of Object.keys(byFolder).sort()) {{
      const all = cards.filter(c => c.dataset.folder === folder);
      const out = byFolder[folder];
      lines.push(`# ${{folder}}`);
      if (out.length === all.length) {{
        lines.push(`git mv "engine/Fonts/${{folder}}" "{REJECTED}/${{folder}}"`);
      }} else {{
        lines.push(`New-Item -ItemType Directory -Force ` +
                   `"{REJECTED}/${{folder}}" | Out-Null`);
        const lic = LIC[folder];
        if (lic) {{
          const from = lic[1] ? `engine/Fonts/${{folder}}/${{lic[1]}}/${{lic[0]}}`
                              : `engine/Fonts/${{folder}}/${{lic[0]}}`;
          lines.push(`Copy-Item "${{from}}" "{REJECTED}/${{folder}}/" -Force`);
          lines.push(`git add "{REJECTED}/${{folder}}/${{lic[0]}}"`);
        }}
        for (const c of out)
          for (const f of c.dataset.files.split(","))
            lines.push(`git mv "engine/Fonts/${{folder}}/${{f}}" ` +
                       `"{REJECTED}/${{folder}}/${{f}}"`);
      }}
      lines.push("");
    }}
    lines.push("dotnet test tests/test.csproj");
  }}
  document.getElementById("script").value = lines.join("\\n");
  document.getElementById("out").style.display = "block";
  document.getElementById("script").select();
  window.scrollTo({{ top: 0, behavior: "smooth" }});
}};

paint();
</script>
</body></html>
"""

os.makedirs(OUT, exist_ok=True)
target = os.path.join(OUT, "triage.html")
with open(target, "w", encoding="utf-8", newline="\n") as f:
    f.write(page)

print(f"{len(library)} fontes, {total_families} familles -> {target}")
print(f"{os.path.getsize(target)/1048576:.1f} Mo")
