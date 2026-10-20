// Frisket designer - the core.
//
// Everything here answers a question about a document rather than about a page in a
// browser: the model and its defaults, the layout echo of LayoutResolver, the SVG
// rendering, the data binding, the template validator, and the wording all three
// speak. It touches no element, reads no control and stores no preference.
//
// That is not tidiness. It is what lets the same code carry two faces - the full
// designer and a flow editor - without either growing a second copy of the engine's
// echo, which would drift the way two of anything drift. It is also what
// `check-against-engine.js` has been loading all along with a stubbed document,
// which is why the seam was already most of the way here before it was drawn.
//
// The one exception is deliberate and marked: the text measurer is a canvas, so it
// is made of DOM. It is a ruler, not chrome, and it waits until it is needed.
//
// Loaded as a classic script, never a module: this page is opened from a file:// URL
// as often as not, and a module there is refused outright by CORS.

"use strict";

// ---------------------------------------------------------------------------
// The engine writes NaN as the string "NaN" (AllowNamedFloatingPointLiterals),
// so every number crossing the JSON boundary goes through these two.
// ---------------------------------------------------------------------------
const num = v => (v === "NaN" || v === null || v === undefined || v === "") ? NaN : Number(v);
const ser = v => Number.isFinite(v) ? v : "NaN";
const has = v => Number.isFinite(num(v));

const DOCK = { Left: 1, Top: 2, Right: 4, Bottom: 8 };

function dockOf(loc) {
  const a = loc.Dock;
  if (typeof a === "number") return a;
  if (!a || a === "None") return 0;
  if (a === "Fill") return 15;
  return String(a).split(",").reduce((m, n) => m | (DOCK[n.trim()] || 0), 0);
}
function dockToString(mask) {
  if (mask === 0) return "None";
  if (mask === 15) return "Fill";
  return Object.keys(DOCK).filter(k => mask & DOCK[k]).join(", ");
}

const edges = (l = 0, t = 0, r = 0, b = 0) => ({ Left: l, Top: t, Right: r, Bottom: b });

// The presets offered for a section's Dimension - a plain width/height, chosen once
// when a template is created and rarely revisited, which is why it lives with
// Title and Language in the section panel rather than in the toolbar.
/// The sheets a person actually asks for, in the order they ask for them.
///
/// One list, read by both faces: the flow editor kept a second array of its own and the
/// two had already drifted - it offered A3 and Letter landscape, this offered A5, and
/// neither offered Legal. Section 1 at the scale of a constant.
const PAPER_SIZES = {
  a4p: [210, 297], a4l: [297, 210],
  a3p: [297, 420], a3l: [420, 297],
  a5p: [148, 210], a5l: [210, 148],
  a6p: [105, 148],
  letter: [216, 279], letterl: [279, 216],
  legal: [216, 356], tabloid: [279, 432]
};

function newStyle() {
  return {
    Location: {
      LeftValue: "NaN", LeftUnit: "Millimeter", RightValue: "NaN", RightUnit: "Millimeter",
      WidthValue: "NaN", WidthUnit: "Millimeter", TopValue: "NaN", TopUnit: "Millimeter",
      BottomValue: "NaN", BottomUnit: "Millimeter", HeightValue: "NaN", HeightUnit: "Millimeter",
      AutoWidth: false, AutoHeight: false, Dock: "None"
    },
    Margin: edges(), Padding: edges(),
    Constraints: { MinWidth: 0, MinHeight: 0, MaxWidth: 0, MaxHeight: 0 },
    Visibility: "Visible", PageBreak: "None", Splitting: "Auto", PageScope: "All",
    PageRange: null, Border: null, Background: null
  };
}
const newFont = () => ({
  Family: "", Size: 1, SizeUnit: "Rem", Bold: false, Italic: false, Underline: false,
  Strikethrough: false,
  Color: { R: 0, G: 0, B: 0, A: 255 },
  HorizontalAlignment: "Left", VerticalAlignment: "Top", Wrap: true, LineSpacing: null
});
const newPen = () => ({ Color: { R: 0, G: 0, B: 0, A: 255 }, Width: 0.2, DashStyle: "Solid" });

function newItem(kind) {
  const item = { $type: kind, Style: newStyle() };
  if (kind === "TextItem") { item.Text = "Nouveau texte"; item.Font = newFont(); }
  if (kind === "LineItem") { item.Orientation = "Horizontal"; item.Pen = newPen(); }
  if (kind === "ImageItem") {
    Object.assign(item, { Source: { Data: "", SourcePath: null, MediaType: "image/png" },
                          NaturalWidth: 40, NaturalHeight: 20, Scaling: "Uniform" });
    item.Style.Location.WidthValue = 40;
  }
  if (kind === "ContainerItem") { item.Layout = "Absolute"; item.Childs = []; }
  if (kind === "BarcodeItem") {
    Object.assign(item, {
      Value: "{InvoiceNumber}", Symbology: "Qr", Correction: "Medium",
      QuietZone: 0, ShowText: true,
      Ink: { R: 0, G: 0, B: 0, A: 255 }, Font: newFont()
    });
    item.Style.Location.WidthValue = 30;
    item.Style.Location.HeightValue = 30;
  }
  if (kind === "FieldItem") {
    Object.assign(item, {
      Name: "champ", Kind: "Text", Value: null, Tooltip: null,
      ReadOnly: false, Required: false, Multiline: false, MaxLength: 0,
      Signing: "ByHand",
      Font: newFont(),
      Outline: { Color: { R: 90, G: 90, B: 90, A: 255 }, Width: 0.2, DashStyle: "Solid" },
      Fill: null
    });
  }
  if (kind === "TableItem") {
    item.Layout = "Stack";
    item.Childs = [];
    item.Columns = [{ Width: "NaN", WidthUnit: "Millimeter" }, { Width: "NaN", WidthUnit: "Millimeter" }];
    item.RepeatHeader = true;
    item.Grid = { Color: { R: 180, G: 180, B: 180, A: 255 }, Width: 0.15, DashStyle: "Solid" };
    item.Childs.push(tableRow(["Colonne A", "Colonne B"], true));
    item.Childs.push(tableRow(["valeur", "valeur"], false));
  }
  if (kind === "ImageGridItem") {
    item.Layout = "Stack";
    item.GridColumns = 3;
    item.RowHeight = 40;
    item.Gap = 4;
    item.Childs = [newItem("ImageItem")];
  }
  if (kind === "ImportedPageItem") {
    item.Data = "";
    item.SourcePath = "";
    item.Page = 1;
    item.Scaling = "Uniform";
    item.Width = 120;
  }
  if (kind === "SubTemplateItem") {
    item.Layout = "Stack";
    item.Mode = "Splice";
    item.TemplatePath = "";
    item.TemplateData = "";
    item.Childs = [];
  }
  if (kind === "AdvancedTextItem") {
    item.Font = newFont();
    item.Runs = [{ Text: "Nouveau texte", Font: null }];
  }
  return item;
}
function tableRow(texts, header) {
  const row = { $type: "TableRow", Style: newStyle(), Layout: "Absolute", IsHeader: !!header, Childs: [] };
  row.Style.Padding = edges(1.5, 1.2, 1.5, 1.2);
  for (const t of texts) {
    const cell = newItem("TextItem");
    cell.Text = t;
    cell.Font.Bold = !!header;
    row.Childs.push(cell);
  }
  return row;
}

function blankReport() {
  const header = { $type: "Header", Style: newStyle(), Layout: "Absolute", Childs: [] };
  header.Style.Location.Dock = "Left, Top, Right";
  header.Style.Location.HeightValue = 25;

  const body = { $type: "Body", Style: newStyle(), Layout: "Stack", Childs: [] };
  body.Style.Location.Dock = "Fill";

  const footer = { $type: "Footer", Style: newStyle(), Layout: "Absolute", Childs: [] };
  footer.Style.Location.Dock = "Left, Right, Bottom";
  footer.Style.Location.HeightValue = 14;

  const title = newItem("TextItem");
  title.Text = "{Title}";
  title.Font.Size = 2; title.Font.Bold = true;
  title.Style.Location.LeftValue = 0; title.Style.Location.TopValue = 0;
  header.Childs.push(title);

  const counter = newItem("TextItem");
  counter.Text = "Page {Page} / {Pages}";
  counter.Font.HorizontalAlignment = "Right"; counter.Font.Size = 0.85;
  counter.Style.Location.RightValue = 0; counter.Style.Location.BottomValue = 0;
  footer.Childs.push(counter);

  const doc = { $type: "Section", Style: newStyle(), Layout: "Absolute", Childs: [header, footer, body] };
  doc.Style.Padding = edges(15, 12, 15, 12);

  doc.Dimension = { Width: 210, Height: 297 };

  return {
    Title: "Nouveau rapport",
    RootFontSize: 3,
    Fields: {},
    GeneratedAt: null,
    Sections: [doc]
  };
}

// The section being edited lived here until 20 October 2026 - doc0, setSectionIndex and
// sectionIndex - and read the designer's `report` as a free variable, which worked only
// while that report was a global of the page. It is the designer's, inside
// frisketDesigner, now: the core knows nothing of the page (CLAUDE.md 5c).

// A file written before sections carries "Section" and "Dimension" at the root. The
// engine reads both and writes neither; this does the same so an old template opens
// here and is saved in the new shape.
function normaliseReport(r) {
  // Not a compatibility shim - there is no older shape to read - but the guarantee
  // the rest of the page relies on: there is always a section, and it always has a
  // page size. A template that says neither would otherwise lay out on nothing.
  if (!Array.isArray(r.Sections) || r.Sections.length === 0)
    r.Sections = [{ $type: "Section", Style: newStyle(), Layout: "Absolute", Childs: [] }];

  // Every section, not only the first: a section without a size lays out on nothing,
  // and the second one is exactly the one nobody thinks to check.
  for (const section of r.Sections)
    if (!section.Dimension) section.Dimension = { Width: 210, Height: 297 };

  return r;
}

// ---------------------------------------------------------------------------
// Layout — a deliberately small echo of LayoutResolver: reserve the bands,
// arrange absolutely, stack a flow. It does not paginate and it measures with
// the browser's fonts, so it is a guide, not the truth.
// ---------------------------------------------------------------------------
// Colours drawn onto the sheet, and therefore fixed in both themes. What the canvas
// shows is a printed page: white stock, ink, and the marks this tool lays over them.
// A mark that changed with the interface's theme would make the preview disagree with
// the printer, which is the one thing a proofing tool may not do.
//
// Named here rather than spelled out at each site so that "these do not follow the
// theme" is a place with a rule attached - the harness refuses a hex literal anywhere
// outside this block and the CSS palette.
//
// The selection marker is deliberately in here rather than tied to --accent: a host
// setting a pale accent would make the selection invisible on white paper.
const ON_PAPER = {
  headerBand: "#c8e6c9", bodyBand: "#e3f2fd", footerBand: "#ffe0b2",
  outline: "#c0c0c0",            // the optional box around every element
  selection: "#4c9be8",

  // One set for both placeholders. They say the same thing - "nothing here yet" -
  // and were saying it in two slightly different greys for no reason anybody chose.
  placeholderFill: "#f4f4f4", placeholderLine: "#9a9a9a", placeholderInk: "#7a7a7a",

  defaultInk: "#000000"          // a run with no colour of its own
};

const CAP_RATIO = 0.70;            // stand-in: the real value comes from the font's OS/2
// The one thing this file needs from whoever loads it: a way to measure a run of
// text. The engine draws the same boundary and calls it ITextMeasurer, for the same
// reason - layout and drawing have to agree on widths or text overflows its frame.
//
// A browser has a canvas, so that is the default and no caller has to think about it.
// Anywhere else - a harness, a server, a host embedding only the core - hands one in
// and the file needs no document at all. Naming the seam is what turns "it happens to
// work if you stub a canvas" into a declared dependency.
let ruler = null;

/// Supplies the measurer: (text, cssFont) -> width in CSS pixels.
function setTextRuler(fn) { ruler = fn; }

// What text that names no family is drawn in - declared, like the ruler, because only
// the page knows which output it is showing. The engine gives such text the default
// its font directory names (engine/Fonts/default-family.txt); a mail reader gets the
// stack FlowingHtml writes, which the harness reads out of that file rather than
// trusting this copy. Until the page says, the browser's own sans-serif.
//
// It used to be assumed, and three ways at once: this file drew "sans-serif", the
// content editor "Georgia, serif", and the PDF came out in Mulish.
const MAIL_FACE = "Helvetica, Arial, sans-serif";
let unnamedFace = "sans-serif";

/// Supplies the face for text that names no family, as a CSS font-family list.
function setDefaultFace(face) { unnamedFace = face || "sans-serif"; }
function defaultFace() { return unnamedFace; }

/// A family as a CSS font-family list, quoted, with a generic face behind it: a family
/// the browser has no file for falls back to its default, which is a serif.
const faceOf = family => `'${String(family).replace(/'/g, "")}', sans-serif`;

/// What a font server's list (GET /fonts) asks a page to load: one face per cut, at the
/// address the server hands that file out from, and the family it gives text that names
/// none. Pure, so the harness can ask it; loading is the browser's, so the page does it.
/// Either spelling of each field: the model says Family and ASP.NET publishes family.
function fontsFromServer(list, base) {
  const field = (f, name) => f[name] ?? f[name[0].toLowerCase() + name.slice(1)];
  const faces = [];
  for (const f of list || []) {
    const family = field(f, "Family");
    if (!family) continue;
    const bold = !!field(f, "Bold"), italic = !!field(f, "Italic");
    faces.push({
      family, weight: bold ? "700" : "400", style: italic ? "italic" : "normal",
      url: `${base}/fonts/file?family=${encodeURIComponent(family)}&bold=${bold}&italic=${italic}`
    });
  }
  const marked = (list || []).find(f => field(f, "IsDefault"));
  return {
    families: [...new Set(faces.map(f => f.family))].sort(),
    faces,
    defaultFamily: marked ? field(marked, "Family") : null
  };
}

let _gauge = null;
function measureRun(text, font) {
  if (ruler) return ruler(text, font);
  _gauge ??= document.createElement("canvas").getContext("2d");
  _gauge.font = font;
  return _gauge.measureText(text).width;
}

const rect = (l, t, r, b) => ({ left: l, top: t, right: r, bottom: b, get width() { return r - l; }, get height() { return b - t; } });
const deflate = (x, e) => rect(x.left + e.Left, x.top + e.Top, x.right - e.Right, x.bottom - e.Bottom);
const mm = (value, unit, ref) => unit === "Percentage" ? ref * value / 100 : value;

// Anything a text leaves undecided comes from the document's default, and what
// neither settles falls back to the built-in value - the same cascade the engine
// applies. Without it an unset size reads as undefined and every measurement is NaN.
function fontOf(font, report) {
  const d = (report && report.DefaultFont) || {};
  const f = font || {};
  const pick = (a, b, fallback) => a ?? b ?? fallback;

  return {
    Family: pick(f.Family, d.Family, ""),
    Size: pick(f.Size, d.Size, 1),
    SizeUnit: pick(f.SizeUnit, d.SizeUnit, "Rem"),
    Bold: pick(f.Bold, d.Bold, false),
    Italic: pick(f.Italic, d.Italic, false),
    Underline: pick(f.Underline, d.Underline, false),
    Strikethrough: pick(f.Strikethrough, d.Strikethrough, false),
    Color: pick(f.Color, d.Color, { R: 0, G: 0, B: 0, A: 255 }),
    HorizontalAlignment: pick(f.HorizontalAlignment, d.HorizontalAlignment, "Left"),
    VerticalAlignment: pick(f.VerticalAlignment, d.VerticalAlignment, "Top"),
    Wrap: pick(f.Wrap, d.Wrap, true),
    LineSpacing: pick(f.LineSpacing, d.LineSpacing, 1.5)
  };
}

function emMm(font, report) {
  const f = fontOf(font, report);
  const cap = f.SizeUnit === "Point" ? f.Size * 25.4 / 72 * CAP_RATIO
            : f.SizeUnit === "Millimeter" ? f.Size
            : f.Size * (report.RootFontSize || 3);
  return cap / CAP_RATIO;
}
function cssFont(font, report) {
  const f = fontOf(font, report);
  const px = emMm(font, report) * 3.7795;      // 1mm at 96dpi
  return `${f.Italic ? "italic " : ""}${f.Bold ? "700 " : ""}${px}px ${f.Family || unnamedFace}`;
}
function textWidth(text, font, report) {
  return measureRun(text, cssFont(font, report)) / 3.7795;
}
// A line is a multiple of the cap height - the size that was asked for - not of the
// em and not of the font's own line height, so a row keeps its height whatever the
// typeface. Same rule as the engine.
function lineHeight(font, report) {
  return emMm(font, report) * CAP_RATIO * fontOf(font, report).LineSpacing;
}

// A box auto-sized to its own text is measured twice - once to size it, once
// to wrap into it - and the two measurements are not always bit-identical:
// a subtraction and a re-addition a few lines apart can land a ULP short of
// the original value. Real wrapping decisions differ by far more than a
// micron, so a micron of slack absorbs that noise without ever masking an
// actual overflow.
const WRAP_SLACK_MM = 1e-6;

// Where a line may break: the engine's LineBreaks.At, which is char.IsWhiteSpace less
// the three spaces Unicode calls glue (U+00A0, U+2007, U+202F). Spelled out rather than
// written as \s, which differs from .NET on two characters: it counts U+FEFF, which .NET
// does not, and misses U+0085, which it does. The harness reads LineBreaks.Glue and asks
// this pattern about each of them.
const BREAK_CHARS = "\\t\\n\\v\\f\\r \\u0085\\u1680\\u2000-\\u2006\\u2008-\\u200A\\u2028\\u2029\\u205F\\u3000";
const BREAKS_AT = new RegExp("[" + BREAK_CHARS + "]");
const BREAK_RUN = new RegExp("([" + BREAK_CHARS + "]+)");

function breakLines(text, font, report, width) {
  const out = [];
  const wraps = fontOf(font, report).Wrap;
  for (const para of String(text ?? "").split("\n")) {
    if (!wraps || !(width > 0) || textWidth(para, font, report) <= width + WRAP_SLACK_MM) { out.push(para); continue; }
    let line = "";
    for (const word of para.split(BREAK_RUN)) {
      const next = line + word;
      if (line && textWidth(next.trimEnd(), font, report) > width + WRAP_SLACK_MM) { out.push(line.trimEnd()); line = word.trimStart(); }
      else line = next;
    }
    out.push(line.trimEnd());
  }
  return out;
}

/// What the data pane holds, parsed once per refresh. Null when it is empty or
/// not yet valid JSON - which it will not be, half the time somebody is typing.
let bound = null;

/// The data the preview binds against, parsed. Given the empty string, or something
/// that is not JSON yet because somebody is still typing it, there is simply no data -
/// which is a state the preview already draws, so it is not an error.
function setData(text) {
  const trimmed = (text || "").trim();
  if (!trimmed) { bound = null; return; }

  try {
    bound = JSON.parse(trimmed);
  } catch {
    bound = null;                       // mid-keystroke; the panel already says so
  }
}

/// Follows a dotted path through the data. Undefined when it leads nowhere, which
/// is what tells the caller to leave the token visible.
function fromData(path) {
  if (!bound) return undefined;

  let at = bound;
  for (const step of path.split(".")) {
    if (at == null || typeof at !== "object") return undefined;
    at = at[step];
  }
  return at;
}

/// True when the data holds something worth showing at `path`. Same reading as the
/// engine: absent, null, blank, zero, false and an empty list all mean nothing.
function hasSomething(path) {
  const value = fromData(String(path).trim().replace(/^\{|\}$/g, "").trim());
  if (value === undefined || value === null) return false;
  if (Array.isArray(value)) return value.length > 0;
  if (typeof value === "boolean") return value;
  if (typeof value === "number") return value !== 0;
  if (typeof value === "object") return true;
  return String(value).trim().length > 0;
}

/// Whether an item earns its place, given the data. With no data loaded nothing is
/// hidden: the preview draws it and the tree marks it.
function showsWithData(item) {
  if (!bound) return true;

  // An image bound to a path in the data disappears when nothing is there, the same
  // way an unsigned quote is left with no signature rather than an empty box. This
  // is the binder's SourceField rule, not ShowWhen/HideWhen - independent of them,
  // and checked first because it applies whether or not either is set.
  if (item.$type === "ImageItem" && item.SourceField && !resolvedImageSource(item))
    return false;

  if (item.ShowWhen && !hasSomething(item.ShowWhen)) return false;
  return !(item.HideWhen && hasSomething(item.HideWhen));
}

/// Heading numbering, the echo of engine/Model/HeadingNumbers.cs.
///
/// The counters live in a Map rather than on the items. Everything on an item here is
/// enumerable, so stamping them on would put worked-out numbers into the template the
/// designer saves - the C# side has internal fields the serializer cannot see, which is
/// a protection this language does not offer.
const headingCounters = new Map();

const HEADING_LEVELS = 6;

function headingLevelOf(item) {
  const at = ["Heading1", "Heading2", "Heading3", "Heading4", "Heading5", "Heading6"]
    .indexOf(item && item.Role);
  return at < 0 ? 0 : at + 1;
}

/// The level written inside the brackets of {HeadingNumber[2]}, or zero.
function headingLevelIn(name) {
  const found = /\[(\d+)\]/.exec(name || "");
  if (!found) return 0;
  const level = Number(found[1]);
  return level >= 1 && level <= HEADING_LEVELS ? level : 0;
}

/// "1.1.1", "A.a.1", "A) a. 1" - the five symbol characters are the alphabets, and
/// everything between them belongs to the level that follows. Truncating "1.1.1" to two
/// levels therefore gives "1.1" and never "1.1."; the closing punctuation is the
/// author's, typed in the text beside the token.
function headingFormat(format) {
  const shape = [];
  let lead = "";
  for (const c of String(format || "")) {
    if ("1AaIi".includes(c)) { shape.push({ lead, kind: c }); lead = ""; }
    else lead += c;
  }
  return shape;
}

function headingDepth(format) { return headingFormat(format).length; }

function headingLetters(value, first) {
  let out = "";
  const base = first.charCodeAt(0);
  while (value > 0) {
    value--;
    out = String.fromCharCode(base + (value % 26)) + out;
    value = Math.floor(value / 26);
  }
  return out;
}

const ROMAN_SIGNS = [[1000, "M"], [900, "CM"], [500, "D"], [400, "CD"], [100, "C"],
                     [90, "XC"], [50, "L"], [40, "XL"], [10, "X"], [9, "IX"],
                     [5, "V"], [4, "IV"], [1, "I"]];

function headingRoman(value, upper) {
  if (value <= 0 || value > 3999) return String(value);
  let out = "";
  for (const pair of ROMAN_SIGNS)
    while (value >= pair[0]) { out += pair[1]; value -= pair[0]; }
  return upper ? out : out.toLowerCase();
}

function headingSpell(value, kind) {
  if (kind === "A") return headingLetters(value, "A");
  if (kind === "a") return headingLetters(value, "a");
  if (kind === "I") return headingRoman(value, true);
  if (kind === "i") return headingRoman(value, false);
  return String(value);
}

function headingRender(counters, level, shape) {
  let out = "";
  for (let i = 0; i < level && i < counters.length; i++) {
    const symbol = shape[Math.min(i, shape.length - 1)];
    out += (i === 0 ? "" : symbol.lead) + headingSpell(counters[i], symbol.kind);
  }
  return out;
}

/// Walks every section in declaration order and remembers where each item sits. Run
/// once before laying out and never while drawing: a counter advanced as items are
/// placed would be advanced again by every redraw, and this page redraws constantly.
function applyHeadingNumbers(report) {
  headingCounters.clear();
  const file = new Array(HEADING_LEVELS).fill(0);

  const walk = (item, own) => {
    if (item.Style && item.Style.Visibility === "Gone") return;

    const level = headingLevelOf(item);
    if (level > 0) {
      file[level - 1]++; own[level - 1]++;
      for (let d = level; d < HEADING_LEVELS; d++) { file[d] = 0; own[d] = 0; }
    }

    headingCounters.set(item, { file: file.slice(), own: own.slice() });
    (item.Childs || []).forEach(child => walk(child, own));
  };

  (report.Sections || []).forEach(s => walk(s, new Array(HEADING_LEVELS).fill(0)));
}

/// The paper the report says it is meant for, as a CSS colour, or null for white.
///
/// Applied to --paper by whichever face is showing the page, and deliberately NOT drawn
/// into the SVG: that SVG is the echo of the engine's own, the two are held to each
/// other, and the engine never draws this. A colour in one and not the other is exactly
/// the divergence that pairing exists to catch.
///
/// So the sheet is tinted by the chrome, under a proof that stays honest.
function paperColour(report) {
  const c = report && report.ProofBackground;
  if (!c) return null;
  const a = (c.A ?? 255) / 255;
  return a < 1 ? `rgba(${c.R | 0}, ${c.G | 0}, ${c.B | 0}, ${a})`
               : `rgb(${c.R | 0}, ${c.G | 0}, ${c.B | 0})`;
}

function tokens(text, report, pageNo, pageCount, owner) {
  return String(text ?? "").replace(/\{\{|\}\}|\{([^}]*)\}/g, (whole, token) => {
    if (whole === "{{") return "{";
    if (whole === "}}") return "}";
    const [name, format] = token.split(":");
    const key = name.toLowerCase();

    // A heading number, read from what the walk stored rather than from a counter
    // running here - so redrawing the page cannot change it.
    const heading = /^(section)?headingnumber(\[\d+\])?$/.exec(key);
    if (heading) {
      const shape = headingFormat(report.HeadingNumbering);
      const at = headingCounters.get(owner);
      if (!shape.length || !at) return whole;

      const counters = heading[1] ? at.own : at.file;
      const level = headingLevelIn(name) || headingLevelOf(owner);

      // No level to be at, or nothing reached at that level yet - which is what a page
      // header asking for a chapter number gets, being laid out before the body it
      // heads. The token stays visible, the way an unfilled field does.
      if (level <= 0 || !counters[level - 1]) return whole;
      return headingRender(counters, level, shape);
    }

    if (key === "page") return String(pageNo);
    if (key === "pages") return String(pageCount);
    if (key === "title") return report.Title || "";
    if (key === "date") return (report.GeneratedAt || new Date().toISOString()).slice(0, 10);
    if (key === "time") return (report.GeneratedAt || new Date().toISOString()).slice(11, 16);
    if (report.Fields && key in lower(report.Fields)) return lower(report.Fields)[key];

    // The data comes last, so nothing it holds can shadow {Page} or {Title}. A path
    // that leads nowhere leaves the token on the page, which is how the engine says
    // "this was never filled in" rather than quietly printing a blank.
    const value = fromData(name.trim());
    if (value === undefined || value === null) return whole;

    return format ? formatted(value, format) : String(value);
  });
}
/// The few formats a preview needs. Not the engine's whole vocabulary - the exact
/// preview is the answer when the difference matters - but enough that an amount
/// reads as an amount rather than as 1234.5.
function formatted(value, format) {
  if (typeof value !== "number") return String(value);

  // Invariant only, to match the engine's own default: comma for grouping, point
  // for the decimal. A French preview here would be a choice this page makes for
  // the reader rather than one the template or the data made - the designer is
  // meant to stay a neutral picture of what the engine will do.
  const decimals = (format.split(".")[1] || "").replace(/[^0#]/g, "").length;
  const grouped = format.includes(",");

  let text = value.toFixed(decimals);
  if (grouped) {
    const [whole, rest] = text.split(".");
    text = whole.replace(/\B(?=(\d{3})+(?!\d))/g, ",") + (rest ? "." + rest : "");
  }
  return text;
}

let _lowerCache = new WeakMap();
function lower(fields) {
  if (!_lowerCache.has(fields)) {
    const m = {}; for (const k of Object.keys(fields)) m[k.toLowerCase()] = fields[k];
    _lowerCache.set(fields, m);
  }
  return _lowerCache.get(fields);
}

function intrinsicHeight(item, boxWidth, report) {
  const h = num(item.Style.Location.HeightValue);
  if (Number.isFinite(h)) return mm(h, item.Style.Location.HeightUnit, 0);
  const p = item.Style.Padding;
  const content = Math.max(0, boxWidth - p.Left - p.Right);
  let height = 0;
  if (item.$type === "TextItem") height = breakLines(item.Text, item.Font, report, content).length * lineHeight(item.Font, report);
  else if (item.$type === "LineItem") height = item.Pen.Width;
  else if (item.$type === "FieldItem") {
    const lines = item.Kind === "Signature" || item.Multiline ? 3 : 1;
    height = lines * lineHeight(item.Font, report) + 1;
  }
  else if (item.$type === "ImageItem") height = item.NaturalWidth > 0 ? content * item.NaturalHeight / item.NaturalWidth : 0;
  else if (item.$type === "ImageGridItem") {
    const columns = Math.max(1, item.GridColumns || 1);
    const gap = Math.max(0, item.Gap ?? 0);
    const visible = (item.Childs || []).filter(c => c.Style.Visibility !== "Gone").length;
    const rows = Math.max(1, Math.ceil(visible / columns));
    height = rows * (item.RowHeight || 0) + Math.max(0, rows - 1) * gap;
  }
  else if (item.$type === "AdvancedTextItem") {
    height = breakAdvancedRuns(item, content, report).reduce((sum, line) => sum + line.height, 0);
  }
  else if (item.Childs) {
    const of = c => intrinsicHeight(c, content - c.Style.Margin.Left - c.Style.Margin.Right, report)
                  + c.Style.Margin.Top + c.Style.Margin.Bottom;

    if (item.Layout === "Stack") {
      // Children scoped to a page are alternatives, not a sequence: only the
      // heaviest scope counts, exactly as the engine reserves the space.
      let always = 0;
      const scoped = {};
      for (const c of item.Childs || []) {
        if (c.Style.Visibility === "Gone") continue;
        const scope = c.Style.PageScope || "All";
        if (scope === "All") always += of(c);
        else scoped[scope] = (scoped[scope] || 0) + of(c);
      }
      const variants = Object.values(scoped);
      height = always + (variants.length ? Math.max(...variants) : 0);
    } else {
      for (const c of item.Childs || []) {
        if (c.Style.Visibility === "Gone") continue;
        const loc = c.Style.Location;
        const offset = has(loc.TopValue) && loc.TopUnit !== "Percentage" ? num(loc.TopValue) : 0;
        height = Math.max(height, offset + of(c));
      }
    }
  }
  return height + p.Top + p.Bottom;
}

/// Where an item that measured its own size sits in the room it was given - the echo
/// of LayoutResolver.AlignInside.
///
/// Only on an axis nothing pinned: a coordinate, an edge it is stuck to, or a size the
/// author stated has already decided, and an alignment must not argue with any of them.
/// "None" leaves it where it fell.
function alignPlace(origin, span, size, align) {
  if (align === "Center") return origin + (span - size) / 2;
  if (align === "End") return origin + span - size;
  return origin;
}

/// True when the author decided how wide the item is - a width outright, or both edges
/// pinned - rather than leaving it to be measured from its content.
function statedWidth(loc, docked) {
  if (has(loc.WidthValue)) return true;
  return (has(loc.LeftValue) || (docked & DOCK.Left) !== 0)
      && (has(loc.RightValue) || (docked & DOCK.Right) !== 0);
}

function computeFrame(item, area, report) {
  const loc = item.Style.Location;
  const a = dockOf(loc);
  const av = deflate(area, item.Style.Margin);
  const pad = item.Style.Padding;
  let left = NaN, right = NaN, top = NaN, bottom = NaN;

  if (a & DOCK.Left) left = av.left;
  else if (has(loc.LeftValue)) left = av.left + mm(num(loc.LeftValue), loc.LeftUnit, av.width);
  if (a & DOCK.Right) right = av.right;
  else if (has(loc.RightValue)) right = av.right - mm(num(loc.RightValue), loc.RightUnit, av.width);

  const w = has(loc.WidthValue) ? mm(num(loc.WidthValue), loc.WidthUnit, av.width) : NaN;
  if (Number.isFinite(w)) {
    if (Number.isFinite(left) && !Number.isFinite(right)) right = left + w;
    else if (!Number.isFinite(left) && Number.isFinite(right)) left = right - w;
    else if (!Number.isFinite(left)) { left = av.left; right = left + w; }
  }

  if (a & DOCK.Top) top = av.top;
  else if (has(loc.TopValue)) top = av.top + mm(num(loc.TopValue), loc.TopUnit, av.height);
  if (a & DOCK.Bottom) bottom = av.bottom;
  else if (has(loc.BottomValue)) bottom = av.bottom - mm(num(loc.BottomValue), loc.BottomUnit, av.height);

  const h = has(loc.HeightValue) ? mm(num(loc.HeightValue), loc.HeightUnit, av.height) : NaN;
  if (Number.isFinite(h)) {
    if (Number.isFinite(top) && !Number.isFinite(bottom)) bottom = top + h;
    else if (!Number.isFinite(top) && Number.isFinite(bottom)) top = bottom - h;
    else if (!Number.isFinite(top)) { top = av.top; bottom = top + h; }
  }

  if (item.$type === "TextItem") {
    if (![left, right, top, bottom].every(Number.isFinite)) {
      const available = (Number.isFinite(left) && Number.isFinite(right)) ? right - left - pad.Left - pad.Right : Infinity;
      const lines = breakLines(tokens(item.Text, report, 1, 1, item), item.Font, report, available);
      const bw = Math.max(...lines.map(l => textWidth(l, item.Font, report)), 0) + pad.Left + pad.Right;
      const bh = lines.length * lineHeight(item.Font, report) + pad.Top + pad.Bottom;
      if (!Number.isFinite(left) && Number.isFinite(right)) left = right - bw;
      else if (!Number.isFinite(right)) { if (!Number.isFinite(left)) left = av.left; right = left + bw; }
      if (!Number.isFinite(top) && Number.isFinite(bottom)) top = bottom - bh;
      else if (!Number.isFinite(bottom)) { if (!Number.isFinite(top)) top = av.top; bottom = top + bh; }
    }
  } else if (item.$type === "FieldItem") {
    if (!Number.isFinite(left)) left = av.left;
    if (!Number.isFinite(right)) right = av.right;
    const wanted = intrinsicHeight(item, right - left, report);
    if (!Number.isFinite(top) && Number.isFinite(bottom)) top = bottom - wanted;
    else if (!Number.isFinite(bottom)) { if (!Number.isFinite(top)) top = av.top; bottom = top + wanted; }
  } else if (item.$type === "LineItem" || item.$type === "ImageItem") {
    if (!Number.isFinite(left)) left = av.left;
    if (!Number.isFinite(right)) right = av.right;
    const thickness = intrinsicHeight(item, right - left, report);
    if (!Number.isFinite(top) && Number.isFinite(bottom)) top = bottom - thickness;
    else if (!Number.isFinite(bottom)) { if (!Number.isFinite(top)) top = av.top; bottom = top + thickness; }
  } else if (item.Childs && (!Number.isFinite(top) || !Number.isFinite(bottom))) {
    // A band that states no height takes the height of its content, like the engine.
    if (!Number.isFinite(left)) left = av.left;
    if (!Number.isFinite(right)) right = av.right;
    const needed = intrinsicHeight(item, right - left, report);
    if (!Number.isFinite(top)) top = bottom - needed;
    else bottom = top + needed;
  }

  // Only rewrite left/right (or top/bottom) when a constraint actually fired -
  // same guard as the engine's ApplyConstraints. Reconstructing an edge from
  // "the other edge plus this same width" when nothing clamped is a no-op in
  // theory and a few ULPs off in practice: an auto-sized box is sized to fit
  // its own text exactly, so that sliver of rounding error is enough to tip a
  // width comparison the wrong way and wrap a line that fit.
  const c = item.Style.Constraints;
  if (Number.isFinite(left) && Number.isFinite(right)) {
    const width = right - left;
    let clamped = width;
    if (c.MinWidth > 0 && clamped < c.MinWidth) clamped = c.MinWidth;
    if (c.MaxWidth > 0 && clamped > c.MaxWidth) clamped = c.MaxWidth;
    if (clamped !== width) {
      if (a & DOCK.Right) left = right - clamped; else right = left + clamped;
    }
  }
  if (Number.isFinite(top) && Number.isFinite(bottom)) {
    const height = bottom - top;
    let clamped = height;
    if (c.MinHeight > 0 && clamped < c.MinHeight) clamped = c.MinHeight;
    if (c.MaxHeight > 0 && clamped > c.MaxHeight) clamped = c.MaxHeight;
    if (clamped !== height) {
      if (a & DOCK.Bottom) top = bottom - clamped; else bottom = top + clamped;
    }
  }
  // The same move the engine makes in AlignInside, at the same moment: after the
  // constraints and after every sizing pass, never inside one of them.
  //
  // loc is item.Style.Location and never a working copy with the defaults filled in -
  // an item that stated nothing would come back from one stuck to all four edges, so
  // "did the author pin this?" would always answer yes and the alignment would never
  // fire. hydrate() fills the gaps with NaN and "None", which is why reading it here
  // still asks the author.
  //
  // This whole block was missing until 24 August 2026. Style.AlignVertical reached the
  // engine, the panel and the flow editor and never this file, so the preview put a
  // centred item against the top of its band while the engine centred it - the preview
  // lying about the page, which is the fifth column of the table in section 1.
  const across = item.Style.AlignHorizontal || "None";
  if (across !== "None" && !statedWidth(loc, a)
      && (a & (DOCK.Left | DOCK.Right)) === 0
      && !has(loc.LeftValue) && !has(loc.RightValue)
      && Number.isFinite(left) && Number.isFinite(right)) {
    const width = right - left;
    left = alignPlace(av.left, av.width, width, across);
    right = left + width;
  }

  const down = item.Style.AlignVertical || "None";
  if (down !== "None" && !has(loc.HeightValue)
      && (a & (DOCK.Top | DOCK.Bottom)) === 0
      && !has(loc.TopValue) && !has(loc.BottomValue)
      && Number.isFinite(top) && Number.isFinite(bottom)) {
    const height = bottom - top;
    top = alignPlace(av.top, av.height, height, down);
    bottom = top + height;
  }

  return rect(left, top, right, bottom);
}

function columnEdges(table, left, right) {
  const cols = table.Columns && table.Columns.length ? table.Columns : [{ Width: "NaN" }];
  const total = right - left;
  let fixed = 0, autos = 0;
  const widths = cols.map(c => {
    if (!has(c.Width)) { autos++; return NaN; }
    const w = mm(num(c.Width), c.WidthUnit, total);
    fixed += w; return w;
  });
  const share = autos ? Math.max(0, total - fixed) / autos : 0;
  const out = [left];
  widths.forEach(w => out.push(out[out.length - 1] + (Number.isFinite(w) ? w : share)));
  return out;
}

// A template only writes what it decides: anything left at its default is absent
// from the file. That keeps a template readable, and it means nothing downstream
// may assume a key is there. Filling the gaps in once, on the way in, is cheaper
// and far safer than guarding every read - a missed guard reads as undefined and
// lays the item out at NaN rather than failing.
/// Adds what is missing and touches nothing that is there.
function fill(target, defaults) {
  for (const key in defaults)
    if (target[key] === undefined) target[key] = defaults[key];
  return target;
}

/// True when the item says it only appears under a condition. The preview cannot
/// evaluate it - the designer holds no bound data - so it draws the element and the
/// panel says why it might not be there. Pretending it is absent would be worse:
/// somebody would move a rule that was never going to overlap.
function isConditional(item) {
  return !!(item.ShowWhen || item.HideWhen);
}

function hydrate(item) {
  if (!item || typeof item !== "object") return item;

  // Filled in place, never replaced. The properties panel holds a reference to the
  // very object it edits, so swapping in a merged copy on every layout leaves the
  // editor writing into an orphan: the value changes and the section does not.
  const s = item.Style ??= newStyle();
  fill(s, newStyle());
  fill(s.Location ??= {}, newStyle().Location);
  fill(s.Margin ??= {}, edges());
  fill(s.Padding ??= {}, edges());
  fill(s.Constraints ??= {}, { MinWidth: 0, MinHeight: 0, MaxWidth: 0, MaxHeight: 0 });

  // Rich text too. It was left out, and a template naming no font for one - legal, and what a
  // hand or an AI writing from the schema produces - stopped the designer at `item.Font.Wrap`
  // the moment it was loaded. Found on 20 October 2026 by the first browser test that handed
  // the designer a template it had not written itself.
  if (item.$type === "TextItem" || item.$type === "FieldItem" || item.$type === "BarcodeItem"
      || item.$type === "AdvancedTextItem")
    fill(item.Font ??= {}, newFont());
  if (item.$type === "AdvancedTextItem") item.Runs ??= [];

  // Item defaults that are the enum's zero are absent too, and those read as an
  // identity: a field with no Kind is a text field, not a field of no kind.
  const D = {
    LineItem:      { Orientation: "Horizontal" },
    ImageItem:     { Scaling: "Uniform" },
    ContainerItem: { Layout: "Absolute", ColumnCount: 1, ColumnGap: 6, BalanceColumns: true },
    TableRow:      { Layout: "Absolute" },
    TableItem:     { Layout: "Absolute", RepeatHeader: true },
    Header:        { ColumnCount: 1, ColumnGap: 6, BalanceColumns: true },
    Footer:        { ColumnCount: 1, ColumnGap: 6, BalanceColumns: true },
    Body:          { ColumnCount: 1, ColumnGap: 6, BalanceColumns: true },
    FieldItem:     { Kind: "Text", Signing: "ByHand", Multiline: false, ReadOnly: false,
                     Required: false, MaxLength: 0 },
    BarcodeItem:   { Value: "", Symbology: "Qr", Correction: "Medium",
                     QuietZone: 0, ShowText: true },
    SubTemplateItem: { Mode: "Splice", TemplatePath: "", TemplateData: "" },
    ImportedPageItem: { Data: "", SourcePath: "", Page: 1, Scaling: "Uniform" }
  }[item.$type];

  if (D) for (const k in D) if (item[k] === undefined) item[k] = D[k];

  (item.Columns || []).forEach(c => { if (c.WidthUnit === undefined) c.WidthUnit = "Millimeter"; });
  (item.Childs || []).forEach(hydrate);
  return item;
}

/// Splices in every sub-template that carries its own JSON.
///
/// Only that shape. A path needs a file this page does not have and a st:// reference
/// needs a program it is not - the engine draws exactly the same line, and the panel
/// says which is which rather than letting the preview pretend.
///
/// Before applyHeadingNumbers below, and that is not tidiness: a heading inside a
/// spliced template has to be counted with the rest of them, which is only possible
/// because expansion comes first. The engine says so in as many words - "4.1. Method
/// coming from the sub-template, possible only because expansion precedes everything
/// else" (CLAUDE.md section 15b).
function expandCarried(node) {
  for (const child of (node.Childs || [])) {
    if (child.$type === "SubTemplateItem" && (child.TemplateData || "").length) {
      // Append brings a document, not a block: its sections land after this one, and
      // this canvas shows one section. Splicing its body into the flow here would
      // draw a page that will never exist - which is the fault this function's own
      // comment warns about, met from the other end.
      if (child.Mode === "Append") { child.Childs = []; continue; }

      try {
        const inner = JSON.parse(decodeURIComponent(escape(atob(child.TemplateData))));
        const body = (inner.Sections || [])
          .flatMap(s => s.Childs || []).find(c => c.$type === "Body");

        // Replaced rather than appended: layout runs on every keystroke, and appending
        // would splice the same template in again on each one.
        child.Childs = (body && body.Childs)
          ? JSON.parse(JSON.stringify(body.Childs)) : [];
      } catch {
        // Not a template, or not base64. Empty is the honest drawing: the panel's
        // check says what is wrong, and a preview that guessed would be worse.
        child.Childs = [];
      }
      continue;
    }
    if (child.Childs) expandCarried(child);
  }
}

/// The report as the engine should receive it, and as a file should hold it.
///
/// Both the exact preview and Save had a copy of these lines, comment included, so a
/// third thing to strip would have gone into one of them and not the other.
function forEngine(report) {
  const copy = JSON.parse(JSON.stringify(report));

  // The engine writes no $type on a section - it is not polymorphic at that position -
  // and every section is dropped the same way, not only the first.
  (copy.Sections || []).forEach(d => delete d.$type);

  // A carried sub-template's children are derived from its bytes, so the file keeps
  // the bytes and not the expansion. Otherwise editing the other template would leave
  // this one showing what it used to say, and saying it with confidence.
  (function strip(node) {
    for (const child of (node.Childs || [])) {
      if (child.$type === "SubTemplateItem" && (child.TemplateData || "").length)
        child.Childs = [];
      else
        strip(child);
    }
  })({ Childs: copy.Sections || [] });

  return copy;
}

function layout(report, index = 0) {
  normaliseReport(report);
  expandCarried({ Childs: report.Sections || [] });

  // Every section, not just the one being composed: {HeadingNumber} counts across the
  // whole file, so the second section cannot know where it starts without the first
  // having been walked. Once per layout, never while drawing.
  applyHeadingNumbers(report);

  // Which section to compose, passed in rather than read off the editor: the harness
  // hands this function a report of its own, and reaching for the global index would
  // lay out a section belonging to a different document. Defaulting to the first is
  // what every caller wanted back when there was only one.
  const doc = report.Sections[Math.min(Math.max(index | 0, 0), report.Sections.length - 1)];
  hydrate(doc);
  const placed = [];
  const page = rect(0, 0, doc.Dimension.Width, doc.Dimension.Height);
  const emit = (item, frame, extra) => { placed.push(Object.assign({ item, frame }, extra)); return frame; };

  emit(doc, page);
  const content = deflate(page, doc.Style.Padding);
  const kids = doc.Childs || [];
  const header = kids.find(k => k.$type === "Header");
  const footer = kids.find(k => k.$type === "Footer");
  const body = kids.find(k => k.$type === "Body");

  let band = content;
  if (header && header.Style.Visibility !== "Gone") {
    const f = arrange(header, content, placed, report);
    band = rect(band.left, f.bottom, band.right, band.bottom);
  }
  if (footer && footer.Style.Visibility !== "Gone") {
    const f = arrange(footer, content, placed, report);
    band = rect(band.left, band.top, band.right, f.top);
  }
  for (const k of kids) {
    if (k === header || k === footer || k === body) continue;
    arrange(k, content, placed, report);
  }
  if (body && body.Style.Visibility !== "Gone") {
    emit(body, band);
    const area = deflate(band, body.Style.Padding);
    if (body.Layout === "Stack") flow(body, area, placed, report);
    else (body.Childs || []).forEach(c => arrange(c, area, placed, report));
  }
  return { placed, page, band, section: doc };
}

function arrange(item, area, placed, report) {
  // Gone, or hidden by a condition the loaded data does not satisfy. Absolute
  // children come through here, so this is where a pinned rule disappears.
  if (item.Style.Visibility === "Gone" || !showsWithData(item)) return rect(NaN, NaN, NaN, NaN);
  const frame = computeFrame(item, area, report);
  placed.push({ item, frame });
  if (item.$type === "TableItem") { table(item, frame, placed, report); return frame; }
  if (item.$type === "ImageGridItem") { grid(item, frame, placed, report); return frame; }
  if (item.$type === "AdvancedTextItem") { advancedText(item, frame, placed, report); return frame; }
  if (item.Childs) {
    const inner = deflate(frame, item.Style.Padding);
    if (item.Layout === "Stack") flow(item, inner, placed, report);
    else item.Childs.forEach(c => arrange(c, inner, placed, report));
  }
  return frame;
}

/// Lays a container's children out, in one column or several.
///
/// Columns fill in order: what one cannot take starts the next. The page here is a
/// single sheet - the preview does not paginate - so anything past the last column
/// is simply not drawn, and the panel says how many columns there are.
///
/// Unlike the engine, this does not split a paragraph across a column boundary: it
/// moves whole children. A long text therefore starts a column here where the engine
/// would have finished it on the one before. The exact preview is the answer when
/// that matters.
function flow(container, area, placed, report) {
  const columns = Math.max(1, container.ColumnCount || 1);
  if (columns === 1) { flowColumn(container, area, placed, report, 0, false); return; }

  const gap = Math.max(0, container.ColumnGap ?? 6);
  const width = (area.right - area.left - gap * (columns - 1)) / columns;
  if (width <= 0) { flowColumn(container, area, placed, report, 0, false); return; }

  const mark = placed.length;
  const lay = bottom => {
    let from = 0;
    for (let i = 0; i < columns && from < (container.Childs || []).length; i++) {
      const left = area.left + i * (width + gap);
      from = flowColumn(container, rect(left, area.top, left + width, bottom),
                        placed, report, from, true);
    }
    return from >= (container.Childs || []).length;
  };

  const fits = lay(area.bottom);
  if (!fits || container.BalanceColumns === false) return;

  // It all fitted, so the columns are lopsided - the first full, the last nearly
  // empty. The engine halves its way to the shallowest height that still holds
  // everything; so does this, or the preview shows a page the engine will not draw.
  let tooShort = area.top, enough = area.bottom;
  for (let step = 0; step < 8; step++) {
    const tryAt = (tooShort + enough) / 2;
    placed.length = mark;
    if (lay(tryAt)) enough = tryAt; else tooShort = tryAt;
  }

  placed.length = mark;
  lay(enough);
}

/// Places children from `start` onwards. Returns the index of the first one it did
/// not place, which is where the next column picks up.
function flowColumn(container, area, placed, report, start, stopAtBottom) {
  let cursor = area.top;
  let variantTop = NaN, variantBottom = NaN, currentScope = "All";

  const kids = container.Childs || [];
  for (let index = start; index < kids.length; index++) {
    const child = kids[index];
    if (child.Style.Visibility === "Gone" || !showsWithData(child)) continue;

    // Page-scoped children share the same room rather than stacking, so a footer
    // sized for its totals is not overrun by the notice that replaces them.
    const scope = child.Style.PageScope || "All";
    if (scope === "All") {
      if (Number.isFinite(variantBottom)) { cursor = variantBottom; variantTop = variantBottom = NaN; }
    } else if (!Number.isFinite(variantTop)) {
      variantTop = variantBottom = cursor;
      currentScope = scope;
    } else if (scope !== currentScope) {
      cursor = variantTop;
      currentScope = scope;
    }
    const m = child.Style.Margin;
    const column = rect(area.left + m.Left, area.top, area.right - m.Right, area.bottom);
    const top = cursor + m.Top;
    const probe = computeFrame(child, rect(column.left, top, column.right, area.bottom), report);
    const left = Number.isFinite(probe.left) ? probe.left : column.left;
    const right = Number.isFinite(probe.right) ? probe.right : column.right;
    const height = intrinsicHeight(child, right - left, report);
    const frame = rect(left, top, right, top + height);

    // Past the foot of the column, and not the first thing in it: hand it on. The
    // first is placed regardless, or a child taller than a column would never be
    // drawn anywhere at all.
    if (stopAtBottom && frame.bottom > area.bottom && index > start)
      return index;

    placed.push({ item: child, frame });
    if (child.$type === "TableItem") table(child, frame, placed, report);
    else if (child.$type === "ImageGridItem") grid(child, frame, placed, report);
    else if (child.$type === "AdvancedTextItem") advancedText(child, frame, placed, report);
    else if (child.Childs) {
      const inner = deflate(frame, child.Style.Padding);
      if (child.Layout === "Stack") flow(child, inner, placed, report);
      else child.Childs.forEach(c => arrange(c, inner, placed, report));
    }
    cursor = frame.bottom + m.Bottom;
    if (scope !== "All") variantBottom = Math.max(variantBottom, cursor);
  }

  return kids.length;
}

function table(item, frame, placed, report) {
  const inner = deflate(frame, item.Style.Padding);
  const edgesX = columnEdges(item, inner.left, inner.right);
  let cursor = inner.top;

  for (const row of item.Childs || []) {
    if (row.Style.Visibility === "Gone") continue;
    const pad = row.Style.Padding;
    let tallest = 0;
    (row.Childs || []).forEach((cell, i) => {
      if (i + 1 >= edgesX.length) return;
      const w = edgesX[i + 1] - edgesX[i] - pad.Left - pad.Right - cell.Style.Margin.Left - cell.Style.Margin.Right;
      tallest = Math.max(tallest, intrinsicHeight(cell, Math.max(0, w), report));
    });
    const height = tallest + pad.Top + pad.Bottom;
    const rowFrame = rect(edgesX[0], cursor, inner.right, cursor + height);
    placed.push({ item: row, frame: rowFrame, gridEdges: item.Grid ? edgesX : null, grid: item.Grid });

    (row.Childs || []).forEach((cell, i) => {
      if (i + 1 >= edgesX.length || cell.Style.Visibility === "Gone") return;
      const box = rect(edgesX[i] + pad.Left, cursor + pad.Top, edgesX[i + 1] - pad.Right, cursor + height - pad.Bottom);
      arrange(cell, box, placed, report);
    });
    cursor += height;
  }
  frame.bottom = Math.max(frame.bottom, cursor + item.Style.Padding.Bottom);
}

/// Lays a grid of pictures out row by row, GridColumns to a row, each row the same
/// height - simpler than a table, since every cell is the same size. The local
/// preview never paginates, so a row that would overflow the page is simply drawn
/// past the foot rather than moved to one: the exact preview is the answer once a
/// photo sheet spans several pages.
function grid(item, frame, placed, report) {
  const inner = deflate(frame, item.Style.Padding);
  const columns = Math.max(1, item.GridColumns || 1);
  const gap = Math.max(0, item.Gap ?? 0);
  const cellWidth = Math.max(0, (inner.right - inner.left - gap * (columns - 1)) / columns);
  const rowHeight = Math.max(0, item.RowHeight || 0);

  const visible = (item.Childs || []).filter(c => c.Style.Visibility !== "Gone" && showsWithData(c));
  let cursor = inner.top;

  for (let i = 0; i < visible.length; i += columns) {
    const end = Math.min(i + columns, visible.length);
    for (let k = i; k < end; k++) {
      const cell = visible[k];
      const col = k - i;
      const left = inner.left + col * (cellWidth + gap);
      const m = cell.Style.Margin;
      const box = rect(left + m.Left, cursor + m.Top, left + cellWidth - m.Right, cursor + rowHeight - m.Bottom);
      arrange(cell, box, placed, report);
    }
    cursor += rowHeight + gap;
  }

  const bottom = visible.length ? cursor - gap : inner.top;
  frame.bottom = Math.max(frame.bottom, bottom + item.Style.Padding.Bottom);
}

/// Consecutive pieces from the same run collapse into one - the word split was only
/// ever for deciding where a line breaks. Trims the trailing space a line otherwise
/// ends on, the same tidying the real wrap already does.
function mergeAdvancedPieces(pieces, report) {
  for (let i = pieces.length - 1; i > 0; i--) {
    if (pieces[i].font !== pieces[i - 1].font) continue;
    pieces[i - 1].text += pieces[i].text;
    pieces[i - 1].width += pieces[i].width;
    pieces.splice(i, 1);
  }
  if (pieces.length) {
    const last = pieces[pieces.length - 1];
    const trimmed = last.text.replace(/\s+$/, "");
    if (trimmed.length !== last.text.length) {
      last.text = trimmed;
      last.width = textWidth(trimmed, last.font, report);
    }
  }
}

/// Breaks a paragraph of runs into lines, word by word - "a word plus the spaces
/// that follow it", the same rule breakLines uses for a plain text, generalised
/// across runs: a word never carries two styles, but a line commonly does. Shared
/// by the height guess and the actual placement, so there is exactly one wrapping
/// algorithm rather than two that could disagree about how many lines it takes.
function breakAdvancedRuns(item, width, report) {
  const lines = [];
  const wrap = item.Font.Wrap !== false && Number.isFinite(width) && width > 0;

  let current = [];
  let currentWidth = 0;

  const closeLine = () => {
    mergeAdvancedPieces(current, report);
    let tallestLine = 0, tallestCap = 0, lineWidth = 0;
    for (const piece of current) {
      const lh = lineHeight(piece.font, report);
      if (lh >= tallestLine) { tallestLine = lh; tallestCap = emMm(piece.font, report) * CAP_RATIO; }
      lineWidth += piece.width;
    }
    lines.push({ pieces: current, height: tallestLine,
                baselineOffset: (tallestLine + tallestCap) / 2, width: lineWidth });
    current = [];
    currentWidth = 0;
  };

  for (const run of item.Runs || []) {
    if (!run.Text) continue;

    // Three levels, the run over the item's own base over the section default -
    // fontOf only cascades two, so the item's own base is resolved first and stood
    // in as the "section default" for the run's own cascade onto it.
    const itemBase = fontOf(item.Font, report);
    const runFont = run.Font ? fontOf(run.Font, { DefaultFont: itemBase }) : itemBase;
    runFont.HorizontalAlignment = "Left";   // this function places it, not text()

    const resolved = tokens(run.Text, report, 1, 1, item);
    const paragraphs = resolved.split("\n");

    for (let p = 0; p < paragraphs.length; p++) {
      if (p > 0) closeLine();   // a hard break always starts a fresh line

      const paragraph = paragraphs[p];
      let start = 0;
      while (start < paragraph.length) {
        let end = start;
        while (end < paragraph.length && !BREAKS_AT.test(paragraph[end])) end++;
        while (end < paragraph.length && BREAKS_AT.test(paragraph[end])) end++;

        const word = paragraph.slice(start, end);
        const wordWidth = textWidth(word, runFont, report);

        // Trailing spaces on the word being tested must not be what tips it over
        // the edge - only what a reader would see cross the margin counts.
        const trimmedWord = word.replace(/\s+$/, "");
        const checkWidth = trimmedWord.length === word.length
          ? wordWidth : textWidth(trimmedWord, runFont, report);

        if (wrap && current.length > 0 && currentWidth + checkWidth > width + WRAP_SLACK_MM)
          closeLine();

        current.push({ font: runFont, text: word, width: wordWidth });
        currentWidth += wordWidth;
        start = end;
      }
    }
  }

  closeLine();

  if (!lines.length || (lines.length === 1 && lines[0].pieces.length === 0)) {
    const font = fontOf(item.Font, report);
    const lh = lineHeight(font, report);
    return [{ pieces: [], height: lh, baselineOffset: (lh + emMm(font, report) * CAP_RATIO) / 2, width: 0 }];
  }

  return lines;
}

/// Lays a paragraph of runs out row by row, one row per broken line - the row
/// height is not fixed the way an image grid's is, it comes from whichever piece
/// on the row asked for the most room. The preview does not paginate, so unlike
/// the real engine a line past the foot of the page is simply drawn there instead
/// of moved to one; the exact preview is the answer once a paragraph spans pages.
function advancedText(item, frame, placed, report) {
  const inner = deflate(frame, item.Style.Padding);
  const width = Math.max(0, inner.right - inner.left);
  const lines = breakAdvancedRuns(item, width, report);

  let cursor = inner.top;
  for (const line of lines) {
    const startX = (item.Font.HorizontalAlignment || "Left") === "Center"
      ? inner.left + Math.max(0, (inner.right - inner.left - line.width) / 2)
      : (item.Font.HorizontalAlignment || "Left") === "Right"
      ? inner.right - line.width
      : inner.left;

    const baselineY = cursor + line.baselineOffset;
    let x = startX;

    for (const piece of line.pieces) {
      const lh = lineHeight(piece.font, report);
      const pieceFirst = (lh + emMm(piece.font, report) * CAP_RATIO) / 2;
      const pieceTop = baselineY - pieceFirst;
      const pieceFrame = rect(x, pieceTop, x + piece.width, pieceTop + lh);
      placed.push({ item: { $type: "TextItem", Text: piece.text, Font: piece.font,
                            Style: { Padding: edges(), Visibility: "Visible" } },
                    frame: pieceFrame });
      x += piece.width;
    }

    cursor += line.height;
  }

  frame.bottom = Math.max(frame.bottom, cursor + item.Style.Padding.Bottom);
}

// ---------------------------------------------------------------------------
// Rendering to SVG — mirrors ReportRenderer: background and border on the
// frame, content inside the padding.
// ---------------------------------------------------------------------------
// --- wording -----------------------------------------------------------------
// English is the source language and the fallback: a key with no translation shows
// the English, and a key with no entry at all shows itself, so a missing string is
// visible rather than blank.
//
// Kept as plain data on purpose. Lifting it into an Angular or React application
// means taking this object, not rewriting it.
//
// The enum groups below hold *labels* for values that are written into the template
// as they are. Translate the label, never the value: a Role saved as "Titre1" loads
// nowhere.
/// The rooms the interface can be in, and the only list of them.
///
/// It was written by hand in two faces and confronted with nothing - not with the other
/// face, not with the stylesheet, not with the wordings. Every way of getting it wrong
/// was silent: an option whose CSS rule has gone stamps `data-theme` on the root, no
/// rule matches, and the page falls through to the base palette. The picker then says
/// one room and shows another, with no error anywhere.
///
/// `scheme` is what the room IS, not what it is called, and it is here because the
/// detached preview needs it: that window is written as a string and has to declare
/// `color-scheme` for its own native controls. It used to ask `theme === "dark"`, which
/// stopped being true of anything the moment the dark room was named Graphite - dead
/// code handing the second monitor's controls to the machine's preference rather than
/// to the choice that was made.
///
/// `auto` carries none, because it is not a palette: it is the machine's own answer,
/// and it is the default for the reason section 2b gives - a preference stored
/// somewhere is still a preference, so the question worth answering is what this looks
/// like when nobody has said anything.
///
/// There were seven. Four dark rooms were proposed against stated directions and
/// measured rather than admired, which was the right way to build them and told nobody
/// which to live in - only using them did that. Graphite won and is now the dark room
/// itself, declared in the stylesheet's base block where `auto` reads it and again
/// under its own name where this list does.
const THEMES = [
  { name: "auto",     scheme: null    },
  { name: "graphite", scheme: "dark"  },
  { name: "light",    scheme: "light" },
  { name: "frost",    scheme: "light" },
];

/// A stored name that is no longer a room, answered rather than passed on.
///
/// The preference lives under one localStorage key shared by both faces and was handed
/// straight to setAttribute. Somebody who had once picked Slate got, after the cull, an
/// attribute no rule matches - so the base palette, silently - and a picker left blank,
/// because assigning a `value` with no matching option leaves a real select at
/// selectedIndex -1. The dead name then sat in the variable for the rest of the session
/// and was written back on every save.
const themeOrAuto = name => THEMES.some(t => t.name === name) ? name : "auto";

/// What `color-scheme` a room is, for a window that has to say it in a string.
/// `auto` and anything unknown answer "light dark", which is the honest answer: let
/// the machine decide, which is what those two mean.
const schemeOf = name => {
  const found = THEMES.find(t => t.name === name);
  return found && found.scheme ? found.scheme : "light dark";
};

/// The rooms as options, for whichever face is asking. Built rather than typed, so the
/// two faces cannot come to offer different lists - and carrying data-i18n, so the
/// wording machinery translates them like everything else in the markup.
function themeOptions() {
  // The English label as the text, not the bare name: applyStaticText replaces it a
  // line later, and what stands there in the meantime - or if that line ever stops
  // running - should be a word rather than an identifier. It is also what the markup
  // said before this list existed, so nothing about the page changed but where the
  // list is kept.
  return THEMES.map(t => `<option value="${t.name}" data-i18n="theme.${t.name}">`
                       + `${STRINGS.en["theme." + t.name] ?? t.name}</option>`).join("");
}

const STRINGS = {
  en: {
    "app.title": "Frisket — template designer",
    "app.name": "Frisket",

    "action.new": "New", "action.open": "Open JSON…", "action.save": "Save JSON",
    "action.load": "Load…", "action.saveShort": "Save", "action.clear": "Clear",
    "action.exact": "Exact preview", "action.pdf": "PDF", "action.delete": "Delete",

    "tab.section": "Section", "tab.back": "Back", "tab.data": "Data",
    "tab.watermark": "Watermark", "tab.background": "Background",

    "panel.add": "Add", "panel.addBack": "Add to the back", "panel.structure": "Structure",
    "panel.backStructure": "Structure of the back", "panel.properties": "Properties",
    "panel.section": "Section",
    "panel.back": "Back", "panel.data": "Data", "panel.watermark": "Watermark",
    "panel.background": "Background",

    "family.display": "Display", "family.input": "Input",
    "add.text": "Text", "add.rule": "Rule", "add.image": "Image", "add.table": "Table",
    "add.code": "Code", "add.group": "Group", "add.checkbox": "Checkbox",
    "add.signature": "Signature", "add.imageGrid": "Image grid", "add.subTemplate": "Sub-template",
    "add.importedPage": "PDF page",
    "field.importedPdf": "PDF file", "field.importedPage": "Page",
    "hint.importedPage": "A page of another PDF, copied in whole. This page cannot show it - reading a PDF needs a reader, and there is none in a browser - so the space it will take is drawn and nothing else. Embedding puts the file inside the template, which travels anywhere; a path is resolved beside the template, which only the CLI and the native library can do.",
    "hint.importedClaim": "Anything imported puts a conformance claim in question: nothing here can vouch for bytes it did not write. The engine says so and refuses nothing - a report meant to be read has no need of PDF/A at all.",
    "type.ImportedPageItem": "PDF page",
    "add.advancedText": "Advanced text",

    "page.bands": "bands", "page.frames": "frames",

    "field.font": "Font", "field.size": "Size", "field.style": "Style",
    "field.alignH": "Horizontal", "field.alignV": "Vertical", "field.colour": "Colour",
    "field.lineSpacing": "Line spacing", "field.wrap": "Wrap",
    "field.direction": "Direction", "field.thickness": "Thickness",
    "field.naturalWidth": "Natural width", "field.naturalHeight": "Natural height",
    "field.embed": "Embed a file", "field.carried": "Carried",
    "field.scaling": "Scaling", "field.file": "File",
    "richText.applyColour": "Apply this colour again",
    "richText.newColour": "New colour",
    "field.sourceField": "Data field",
    "hint.sourceField": "A path in the data holding the image, as base64 or a data: URI - for example Signatures.Client. Takes over from the file below once the data provides something; nothing at that path means no image at all, the same way an unsigned quote is left with no signature rather than an empty box.",
    "hint.subTemplate": "Another template, three ways. A path is resolved next to the file that names it, which only the CLI and the native library can do - this page has no file of its own, so a path shows here as an empty box. st:// names something the calling program looks up. Embedding it puts the other template inside this one, which travels anywhere and is what the preview below can actually splice in.",
    "hint.advancedJustify": "Justify does nothing on an advanced text - it stretches the gaps of a plain text's one line, and this places each run's own box instead. Drawn left-aligned.",
    "field.value": "Value", "field.kind": "Kind", "field.correction": "Correction",
    "field.showText": "Readable value", "field.quietZone": "Margin (modules)",
    "field.name": "Name", "field.tooltip": "Tooltip", "field.multiline": "Multiline",
    "field.maxLength": "Max length", "field.options": "Options", "field.outline": "Outline",
    "field.repeatHeader": "Repeat header", "field.column": "col",
    "field.layout": "Layout", "field.isHeader": "Header row",
    "field.gridColumns": "Columns per row", "field.rowHeight": "Row height", "field.gap": "Gap",
    "field.templatePath": "Template file",
    "field.subMode": "Brings across",
    "SubTemplateMode.Splice": "A block, into this flow",
    "SubTemplateMode.Append": "A document, after this section",
    "hint.appendMode": "The other template keeps its own page size, header and footer, and becomes sections after this one. Anything below this include stays in this section, so it comes out first.",
    "field.language": "Language",
    "field.languageSame": "the file\u2019s",
    "field.runs": "Runs", "field.addRun": "+ run", "field.runColor": "Colour",
    "field.runOwnColor": "own colour", "field.runOwnSize": "own size",
    "richText.title": "Rich text", "richText.open": "✎ Edit as rich text",
    "richText.noColor": "No colour", "richText.noSize": "Default size",
    "richText.hint": "Select a range of the text, then click a style, pick a colour or set a size - each applies as soon as you choose it.",
    "richText.apply": "Apply", "action.cancel": "Cancel",
    "field.title": "Title", "field.author": "Author", "field.subject": "Subject",
    "field.keywords": "Keywords", "field.language": "Language",
    "field.headingNumbering": "Heading numbers",
    "field.proofBackground": "Paper",
    "hint.proofBackground": "The stock this is meant to be printed on - letterhead, coloured paper - shown behind the page while you write. It is never printed: not by the PDF, not by the SVG proof, not by the mail. The engine cannot supply paper, and a proof that drew it would be claiming the printer does.",
    "hint.headingNumbering": "How a heading numbers itself, one symbol per level: 1 for figures, A or a for letters, I or i for roman. Anything between two symbols is written between the levels - \"1.1.1\", \"A.a.1\", \"A) a. 1\". Put {HeadingNumber} in the heading's own text, where you want the number to appear; the punctuation after it is yours to type. {SectionHeadingNumber} restarts at every section, and the two are the same until there is a second one.",
    "field.pageSize": "Page size", "field.rootFontSize": "Root font size",
    "field.role": "Role", "field.altText": "Alternative text",

    "zoom.fit": "Fit", "zoom.label": "Zoom",
    "action.detach": "Preview window", "action.attached": "Preview open",
    "preview.title": "Frisket — preview",
    "hint.popup": "The browser blocked the window. Allow pop-ups for this page and try again.",

    "section.columns": "Columns",
    "field.columnCount": "How many", "field.columnGap": "Gap (mm)",
    "field.balance": "Even them out",
    "hint.balance": "On the page where the text runs out, the columns end together instead of the first being full and the last nearly empty.",

    "section.condition": "Shown when",
    "field.showWhen": "Only if", "field.hideWhen": "Except if",
    "hint.condition": "A path in the data, such as Client.Vat. The element is dropped - it takes no room - when there is nothing at that path: absent, empty, zero or false.",

    "panel.issues": "Checks", "issues.none": "Nothing to report",
    "issues.count": "{errors} error(s), {warnings} warning(s)",
    "issues.onlyFaults": "only what is wrong",
    "theme.auto": "Auto", "theme.graphite": "Graphite",
    "theme.light": "Light", "theme.frost": "Frost",

    // The flow editor. It shares this dictionary rather than keeping one of its own:
    // two dictionaries drift, and a face is not a different product.
    "editor.title": "Frisket — editor",
    "size.normal": "Normal", "size.small": "Small", "size.large": "Large", "size.huge": "Huge",
    "editor.document": "Document",
    "editor.addText": "Text",
    "editor.addImage": "Image",
    "editor.removeBlock": "Remove block",
    "editor.resize": "Drag to resize",
    "editor.moveBlock": "Drag to move this block",
    "editor.moveHalf": "Drag to take this out of the pair",
    "editor.addText": "Add a paragraph",
    "editor.addRow": "Add a row", "editor.dropRow": "Remove this row",
    "editor.addColumn": "Add a column", "editor.dropColumn": "Remove this column",
    "editor.fonts": "Typefaces",
    "editor.openHtml": "HTML\u2026",
    "editor.openMail": "Mail\u2026",
    "editor.htmlWhat": "The pages themselves, drawn as SVG with the fonts embedded - "
                     + "what the printer will do.",
    "editor.mailWhat": "The same report as a message: HTML that reflows, no pages, "
                     + "nothing embedded. Run the checks with a mail target first to be "
                     + "told what it loses on the way.",
    "editor.asking": "Asking the engine\u2026",
    "editor.noServer": "The preview server is not answering. Start it with "
                     + "`dotnet run --project apps/server` and try again - this page "
                     + "cannot write the HTML itself, and showing its own approximation "
                     + "instead would be the one thing this button exists to avoid.",
    "fonts.library": "From the server",
    "fonts.websafe": "Web-safe (email)",
    "fonts.default": "Document default",
    "fonts.offline": "The server is not answering, so this lists what the document "
                   + "already uses. A family the engine has no file for is refused when "
                   + "the PDF is written - it is never quietly swapped for another, "
                   + "since another face would relay out every line.",
    "fonts.approximate": "Web-safe families are not embedded: whoever opens the file "
                       + "supplies them, which is right for an email and wrong for a PDF "
                       + "that has to look the same everywhere.",
    "editor.foreign": "this is edited in the designer; it is kept exactly as it is here.",
    "editor.hint": "Type and it becomes text; drop a picture in and it becomes a picture. "
                 + "Enter breaks the line; shift+Enter starts a new block. Drag a block "
                 + "to move it, or alt and an arrow; drag a picture's corner to resize "
                 + "it, and a table's rails to add rows and columns. Drop a block on "
                 + "the side of another to set the two beside each other; the shape "
                 + "shown while dragging is the shape you will get. This face does not "
                 + "paginate - the sheet grows.",
    "target.tagged": "tagged output",
    "target.interactive": "fields stay fillable",
    "hint.target": "What you are about to produce. Untick either one to see what the template loses on the way out - the page looks the same either way, which is what makes the loss easy to miss.",
    "name.row": "row", "name.headerRow": "header row",
    "issue.ua.language": "The section does not say what language it is written in, so a reader announces it with whatever voice it happens to have.",
    "issue.ua.title": "The section has no title, which is what a reader announces when the file opens.",
    "issue.ua.altText": "Shown rather than read, and with no alternative text: a screen reader announces nothing at all.",
    "issue.ua.formRole": "Only a field can be a form control. This will be drawn instead, and a drawing needs an alternative text.",
    "issue.ua.fieldName": "This field has no tooltip, so a reader announces a control with no idea what it is for.",
    "issue.ua.tableHeader": "This table has no header row, so values are announced with nothing to say what they are.",
    "issue.sound.emptyBarcode": "This barcode has nothing to encode.",
    "issue.sound.emptyImage": "This image has neither a picture of its own nor a path to one in the data, so nothing will ever be drawn here.",
    "issue.sound.emptyImportedPage": "This imported page names no PDF, so nothing will be drawn here.",
    "issue.sound.importedPageNumber": "Pages are counted from one.",
    "issue.sound.emptyImageGrid": "This image grid asks for fewer than one column, which draws as a single column rather than what was set here.",
    "issue.sound.repeatPageBreak": "This breaks once, before the whole repeated run. Repeating clones what is inside the container, so for one page per record the break belongs on the block being repeated.",
    "issue.sound.emptySubTemplate": "This sub-template names nothing and carries nothing, so nothing will ever be inserted here. Give it a path, a st:// reference, or the other template's own JSON.",
    "issue.sound.emptyAdvancedText": "This advanced text has no runs with anything in them, so nothing will ever be drawn here.",
    "issue.sound.justifiedBreak": "This text is justified and holds a line break of its own. A break is stretched to the margin like a wrapped line, which is not what it means - put each paragraph in its own text.",
    "issue.sound.nestedSection": "A section cannot sit inside another. Sections are a list on the report, never nested - a page size means nothing inside a page.",
    "issue.sound.headingNumberNoFormat": "This text asks for a heading number, and the report does not say how one is written. Set the numbering format - \"1.1.1\", \"A.a.1\" - or drop the token; until then it stays on the page as it was typed.",
    "issue.sound.headingNumberOutsideHeading": "This is not a heading, so there is no level for the number to be. Give it a heading role, or name the level in the token itself - {HeadingNumber[1]} for the chapter this sits under.",
    "issue.sound.headingNumberDepth": "This heading is deeper than the numbering format spells out, so the last symbol of the format is repeated for it. Add a level to the format, or the two deepest levels will look alike.",
    "issue.sound.columnsIgnored": "This asks for columns, but they will never appear here: an absolutely placed container has no flow to divide, and a table or an image grid already divides its rows on its own terms. Set it to a plain stacking container, or drop the columns.",
    "issue.sound.duplicateField": "Another field is also called '{name}'. Filling one fills the other, and only one value is kept.",
    "issue.sound.raggedTable": "This row has {count} cell(s) where the table has {width} column(s). A conformance checker refuses rows of unequal width.",

    "PdfRole.Table": "Table", "PdfRole.TableRow": "Table row",
    "PdfRole.TableHeader": "Heading cell", "PdfRole.TableCell": "Cell",
    "PdfRole.Form": "Control",
    "field.bookmark": "Bookmark", "field.level": "Level",
    "field.visible": "Visible", "field.pageBreak": "Break", "field.splitting": "Splitting",
    "field.pageScope": "Pages", "field.background": "Background",
    "field.signing": "Signing", "field.scope": "On", "field.padding": "Margins",
    "field.mode": "Placing", "field.layer": "Layer", "field.angle": "Angle",

    "style.bold": "B", "style.italic": "I", "style.underline": "U", "style.strikethrough": "S",
    "option.readOnly": "read only", "option.required": "required",

    "section.accessibility": "Accessibility", "section.outline": "Outline",
    "section.position": "Position", "section.dock": "Dock — sides held",
    "section.margin": "Margin", "section.padding": "Padding", "section.render": "Rendering",
    "section.section": "Section",

    "watermark.enable": "enable",
    "back.enable": "print a back",

    "hint.add": "Added inside the selection when it is a container, beside it otherwise. "
              + "Input elements become a form in the PDF; printed or flattened, they keep their value.",
    "hint.back": "Conditions of sale, typically. A back is neither numbered nor counted: "
               + "an invoice reading page 1 / 2 has four printed faces. {Page} on it gives "
               + "the sheet it belongs to.",
    "hint.data": "Applied by the exact preview and by the PDF. The local preview shows the "
               + "template as it stands, prototype row included.",
    "background.enable": "enable", "field.area": "Covers", "field.opacity": "Opacity",
    "BackdropArea.Sheet": "The whole sheet", "BackdropArea.Margins": "Inside the margins",
    "hint.background": "Printed stationery, drawn behind every page and marked as "
      + "decoration so a screen reader passes over it. Stretch fills the rectangle "
      + "chosen; keep proportions and none need the picture's own size, which is "
      + "measured when the file is chosen here and can be typed on an image. Fade it "
      + "here - a PNG carrying an alpha channel is refused outright.",

    "hint.watermark": "Drawn as content, never as a background: a browser drops backgrounds "
                    + "when printing, and a mark missing from the paper is missing from the "
                    + "very copy someone might use.",
    "hint.metadata": "Written into the file's properties and its XMP packet. Tokens are "
                   + "filled in as elsewhere, so {InvoiceNumber} belongs here.",
    "hint.altText": "Without it a screen reader announces nothing at all: the person is not "
                  + "even told something is there.",
    "hint.approximate": "Approximate preview: the browser measures with its own fonts, this "
                      + "page does not paginate and applies no repetition.",
    "hint.approximateEngineFonts": "Approximate preview: measured with the engine's own "
                      + "fonts, but this page does not paginate and applies no repetition.",

    "alert.noPreview": "Exact preview unavailable: ", "alert.noPdf": "PDF unavailable: ",
    "alert.unreadable": "Cannot read: ", "alert.badData": "Unreadable data: ",
    "alert.noData": "Nothing to save.", "alert.badDataKept": "Unreadable data, nothing written: ",
    "confirm.new": "Start again from an empty template?", "confirm.clearData": "Clear the data?",

    // Enum labels. The values themselves are never translated.
    "Units.Millimeter": "Millimetres", "Units.Percentage": "Percentage",
    "FontSizeUnit.Rem": "Rem", "FontSizeUnit.Millimeter": "Millimetres of cap",
    "FontSizeUnit.Point": "Points", "FontSizeUnit.PercentOfFrame": "% of frame",
    "HorizontalAlignment.Left": "Left", "HorizontalAlignment.Center": "Centre",
    "HorizontalAlignment.Right": "Right", "HorizontalAlignment.Justify": "Justified",
    "VerticalAlignment.Top": "Top", "VerticalAlignment.Middle": "Middle",
    "VerticalAlignment.Bottom": "Bottom",
    "LineOrientation.Horizontal": "Horizontal", "LineOrientation.Vertical": "Vertical",
    "LineOrientation.DiagonalDown": "Diagonal down", "LineOrientation.DiagonalUp": "Diagonal up",
    "DashStyle.Solid": "Solid", "DashStyle.Dash": "Dashed", "DashStyle.Dot": "Dotted",
    "DashStyle.DashDot": "Dash-dot",
    "ImageScaling.Uniform": "Keep proportions", "ImageScaling.Stretch": "Stretch",
    "ImageScaling.None": "None",
    "Symbology.Qr": "QR", "Symbology.Code128": "Code 128",
    "QrErrorCorrection.Low": "Low (7%)", "QrErrorCorrection.Medium": "Medium (15%)",
    "QrErrorCorrection.Quartile": "High (25%)", "QrErrorCorrection.High": "Highest (30%)",
    "FieldKind.Text": "Text", "FieldKind.Checkbox": "Checkbox", "FieldKind.Signature": "Signature",
    "Signing.ByHand": "By hand", "Signing.Digital": "Digital certificate",
    "LayoutMode.Absolute": "Absolute", "LayoutMode.Stack": "Stacked",
    "Visibility.Visible": "Visible", "Visibility.Invisible": "Invisible (space kept)",
    "Visibility.Gone": "Gone (no space)",
    "PageBreak.None": "None", "PageBreak.Before": "Before", "PageBreak.After": "After",
    "PageBreak.BeforeAndAfter": "Before and after",
    "Splitting.Auto": "May split", "Splitting.KeepTogether": "Keep together",
    "PageScope.All": "All", "PageScope.First": "First", "PageScope.Last": "Last",
    "PageScope.ExceptFirst": "Except the first", "PageScope.ExceptLast": "Except the last",
    "PageScope.Odd": "Odd", "PageScope.Even": "Even",
    "PdfRole.Auto": "From the kind", "PdfRole.Section": "Section",
    "PdfRole.Heading1": "Heading 1", "PdfRole.Heading2": "Heading 2",
    "PdfRole.Heading3": "Heading 3", "PdfRole.Heading4": "Heading 4",
    "PdfRole.Heading5": "Heading 5", "PdfRole.Heading6": "Heading 6",
    "PdfRole.Paragraph": "Paragraph", "PdfRole.Caption": "Caption",
    "PdfRole.List": "List", "PdfRole.ListItem": "List item",
    "PdfRole.Figure": "Figure", "PdfRole.Artifact": "Decoration",
    "BackPageScope.EveryPage": "Every sheet", "BackPageScope.LastPage": "The last",
    "BackPageScope.FirstPage": "The first",
    "WatermarkMode.Tiled": "Repeated", "WatermarkMode.Single": "Once, large",
    "WatermarkLayer.InFront": "Over the content (translucent)",
    "WatermarkLayer.Behind": "Under the content",
    "type.Section": "Section", "type.Header": "Header", "type.Footer": "Footer",
    "type.Body": "Body", "type.TextItem": "Text", "type.LineItem": "Rule",
    "type.ImageItem": "Image", "type.BarcodeItem": "Code", "type.TableItem": "Table",
    "type.TableRow": "Row", "type.ContainerItem": "Group", "type.FieldItem": "Field",
    "type.ImageGridItem": "Image grid", "type.SubTemplateItem": "Sub-template",
    "field.alignAcross": "Across the band",
    "ItemAlign.None": "Where it falls", "ItemAlign.Start": "Left",
    "ItemAlign.Center": "Centred", "ItemAlign.End": "Right",
    "ItemAlign.Top": "Top", "ItemAlign.Middle": "Middle", "ItemAlign.Bottom": "Bottom",
    "field.alignDown": "Down the band",
    "hint.align": "Moves the item inside the room it was given, and only on an axis nothing else decided: a coordinate, an edge it is stuck to or a stated size has already answered, and an alignment does not argue with any of them. On a pinned axis it is a legal value that does nothing.",
    "type.AdvancedTextItem": "Advanced text",
    "paper.a4p": "A4 portrait", "paper.a4l": "A4 landscape",
    "paper.a3p": "A3 portrait", "paper.a3l": "A3 landscape",
    "paper.a5p": "A5 portrait", "paper.a5l": "A5 landscape",
    "paper.a6p": "A6 portrait",
    "paper.letter": "Letter", "paper.letterl": "Letter landscape",
    "paper.legal": "Legal", "paper.tabloid": "Tabloid",

    "section.report": "Report — the whole file",
    "tree.report": "Report",
    "tree.section": "Section",
    "tree.untitled": "(untitled)",
    "field.pageLimit": "Page limit", "field.noLimit": "no limit",
    "hint.reportLevel": "These belong to the file, not to the section being edited: one title, one language, one author for the whole document.",
    "issue.can.untagged": "This template declares roles, alternative texts or a language, and none of it reaches an untagged file.",
    "issue.can.flattenedFields": "{n} field(s) will be drawn rather than left fillable. The page looks the same either way.",
    "issue.can.flattenedFieldAltText": "This field will be drawn rather than left fillable, so it is a figure in the structure tree - and a figure with nothing to say for it is refused by PDF/UA. Give it an alternative text, or leave the fields fillable.",
    "issue.can.flattenedSignature": "A signature field flattened is a picture of a place to sign. Nobody can sign it.",
    "issue.can.absoluteInFlow": "This group's children overlap one another across the band, and the output being aimed at has no pages to place them on. They will be written one under the next instead.",
    "panel.report": "Report",
    "sections.open": "Sections…", "sections.title": "Sections",
    "sections.hint": "A report is a list of sections, each with its own page size. This page edits one of them at a time; everything else stays exactly where it is.",
    "sections.one": "Section", "sections.bands": "band(s)",
    "sections.edit": "Edit", "sections.remove": "Remove",
    "sections.add": "Add a section",
    "sections.confirmRemove": "Remove section {n} and everything in it?",
    "sections.editing": "editing section {n} of {total}",
    "action.close": "Close"
  },

  fr: {
    "app.title": "Frisket — concepteur de gabarit",

    "action.new": "Nouveau", "action.open": "Ouvrir JSON…", "action.save": "Enregistrer JSON",
    "action.load": "Charger…", "action.saveShort": "Enregistrer", "action.clear": "Vider",
    "action.exact": "Aperçu exact", "action.delete": "Supprimer",

    "tab.back": "Verso", "tab.data": "Données", "tab.watermark": "Filigrane",
    "tab.background": "Fond",

    "panel.add": "Ajouter", "panel.addBack": "Ajouter au verso", "panel.structure": "Structure",
    "panel.backStructure": "Structure du verso", "panel.properties": "Propriétés",
    "panel.section": "Section",
    "panel.back": "Verso", "panel.data": "Données", "panel.watermark": "Filigrane",
    "panel.background": "Fond de page",

    "family.display": "Affichage", "family.input": "Saisie",
    "add.text": "Texte", "add.rule": "Trait", "add.image": "Image", "add.table": "Tableau",
    "add.code": "Code", "add.group": "Groupe", "add.checkbox": "Case",
    "add.signature": "Signature", "add.imageGrid": "Planche images", "add.subTemplate": "Sous-gabarit",
    "add.importedPage": "Page PDF",
    "field.importedPdf": "Fichier PDF", "field.importedPage": "Page",
    "hint.importedPage": "Une page d’un autre PDF, copiée telle quelle. Cette page ne sait pas l’afficher - lire un PDF demande un lecteur, et il n’y en a pas dans un navigateur - donc seule la place qu’elle prendra est dessinée. L’embarquer met le fichier dans le gabarit, ce qui voyage partout ; un chemin est résolu à côté du gabarit, ce que seuls le CLI et la bibliothèque native savent faire.",
    "hint.importedClaim": "Tout import met en cause une déclaration de conformité : rien ici ne peut répondre d’octets qu’il n’a pas écrits. Le moteur le dit et ne refuse rien - un rapport fait pour être lu n’a que faire de PDF/A.",
    "type.ImportedPageItem": "Page PDF",
    "add.advancedText": "Texte avancé",

    "page.bands": "bandes", "page.frames": "cadres",

    "field.font": "Police", "field.size": "Taille", "field.style": "Style",
    "field.alignH": "Horizontal", "field.alignV": "Vertical", "field.colour": "Couleur",
    "field.lineSpacing": "Interligne", "field.wrap": "Retour à la ligne",
    "field.direction": "Sens", "field.thickness": "Épaisseur",
    "field.naturalWidth": "Largeur d'origine", "field.naturalHeight": "Hauteur d'origine",
    "field.embed": "Embarquer un fichier", "field.carried": "Embarqué",
    "field.scaling": "Ajustement", "field.file": "Fichier",
    "richText.applyColour": "Réappliquer cette couleur",
    "richText.newColour": "Nouvelle couleur",
    "field.sourceField": "Champ de données",
    "hint.sourceField": "Un chemin dans les données contenant l’image, en base64 ou en URI data: — par exemple Signatures.Client. Prend le pas sur le fichier ci-dessous dès que les données fournissent quelque chose ; rien à ce chemin veut dire pas d’image du tout, comme un devis non signé reste sans signature plutôt qu’avec un cadre vide.",
    "hint.subTemplate": "Un autre gabarit, de trois façons. Un chemin est résolu à côté du fichier qui le nomme, ce que seuls le CLI et la bibliothèque native savent faire - cette page n'a pas de fichier propre, donc un chemin s'affiche ici en cadre vide. st:// nomme ce que le programme appelant va chercher. L'embarquer met l'autre gabarit dans celui-ci, ce qui voyage partout et ce que l'aperçu ci-dessous sait réellement insérer.",
    "hint.advancedJustify": "Justifié ne fait rien sur un texte avancé - cela étire les espaces de l'unique ligne d'un texte simple, alors qu'ici chaque segment place sa propre boîte. Dessiné aligné à gauche.",
    "field.value": "Valeur", "field.kind": "Type", "field.correction": "Correction",
    "field.showText": "Valeur lisible", "field.quietZone": "Marge (modules)",
    "field.name": "Nom", "field.tooltip": "Info-bulle", "field.multiline": "Multiligne",
    "field.maxLength": "Longueur max.", "field.options": "Options", "field.outline": "Contour",
    "field.repeatHeader": "Répéter l'en-tête", "field.column": "col",
    "field.layout": "Disposition", "field.isHeader": "Ligne d'en-tête",
    "field.gridColumns": "Colonnes par rangée", "field.rowHeight": "Hauteur de rangée",
    "field.gap": "Espacement", "field.templatePath": "Fichier gabarit",
    "field.subMode": "Apporte",
    "SubTemplateMode.Splice": "Un bloc, dans ce flux",
    "SubTemplateMode.Append": "Un document, apr\u00e8s cette section",
    "hint.appendMode": "L\u2019autre gabarit garde son format, son en-t\u00eate et son pied, et devient des sections apr\u00e8s celle-ci. Ce qui suit cet appel reste dans cette section, donc sort avant.",
    "field.language": "Langue",
    "field.languageSame": "celle du fichier",
    "field.runs": "Segments", "field.addRun": "+ segment", "field.runColor": "Couleur",
    "field.runOwnColor": "couleur propre", "field.runOwnSize": "taille propre",
    "richText.title": "Texte enrichi", "richText.open": "✎ Éditer en texte enrichi",
    "richText.noColor": "Sans couleur", "richText.noSize": "Taille par défaut",
    "richText.hint": "Sélectionnez une partie du texte, puis cliquez un style, choisissez une couleur ou une taille - chacun s'applique dès que vous le choisissez.",
    "richText.apply": "Appliquer", "action.cancel": "Annuler",
    "field.title": "Titre", "field.author": "Auteur", "field.subject": "Sujet",
    "field.keywords": "Mots-clés", "field.language": "Langue",
    "field.headingNumbering": "Numéros de titre",
    "field.proofBackground": "Papier",
    "hint.proofBackground": "Le support sur lequel ceci est censé être imprimé - papier à en-tête, papier de couleur - montré derrière la page pendant qu'on écrit. Il n'est jamais imprimé : ni par le PDF, ni par la preuve SVG, ni par le courriel. Le moteur ne fournit pas de papier, et une preuve qui le dessinerait prétendrait que l'imprimante le fait.",
    "hint.headingNumbering": "Comment un titre se numérote, un symbole par niveau : 1 pour les chiffres, A ou a pour les lettres, I ou i pour les romains. Ce qui sépare deux symboles s'écrit entre les niveaux - \"1.1.1\", \"A.a.1\", \"A) a. 1\". Mettre {HeadingNumber} dans le texte du titre, là où le numéro doit apparaître ; la ponctuation qui suit est à taper soi-même. {SectionHeadingNumber} repart à 1 à chaque section, et les deux valent la même chose tant qu'il n'y en a qu'une.",
    "field.pageSize": "Format de page", "field.rootFontSize": "Police racine",
    "field.role": "Rôle", "field.altText": "Texte de remplacement",
    "field.bookmark": "Signet", "field.level": "Niveau",
    "field.visible": "Visible", "field.pageBreak": "Saut", "field.splitting": "Coupe",
    "field.pageScope": "Pages", "field.background": "Fond",
    "field.signing": "Signature", "field.scope": "Sur", "field.padding": "Marges",
    "field.mode": "Pose", "field.layer": "Plan", "field.angle": "Angle",

    "style.bold": "G", "style.italic": "I", "style.underline": "S", "style.strikethrough": "B",
    "option.readOnly": "lecture seule", "option.required": "obligatoire",

    "section.accessibility": "Accessibilité", "section.outline": "Plan",
    "section.position": "Position", "section.dock": "Côtés collés",
    "section.margin": "Marge", "section.padding": "Remplissage",
    "section.render": "Rendu", "section.section": "Section",

    "watermark.enable": "activer",
    "back.enable": "imprimer un verso",

    "hint.add": "Ajouté dans la sélection si c'est un conteneur, sinon à côté. Les éléments "
              + "de saisie deviennent un formulaire dans le PDF ; à l'impression ou une fois "
              + "aplatis, ils gardent leur valeur.",
    "hint.back": "Les conditions de vente, classiquement. Le verso n'est ni numéroté ni "
               + "compté : une facture « page 1 / 2 » a quatre faces imprimées. {Page} y "
               + "donne le numéro de la feuille à laquelle il appartient.",
    "hint.data": "Appliquées par l'aperçu exact et par le PDF. L'aperçu local montre le "
               + "gabarit tel quel, ligne modèle comprise.",
    "background.enable": "activer", "field.area": "Couvre", "field.opacity": "Opacité",
    "BackdropArea.Sheet": "Toute la feuille", "BackdropArea.Margins": "Dans les marges",
    "hint.background": "Un papier à en-tête, dessiné derrière chaque page et marqué "
      + "comme décoration pour qu'un lecteur d'écran passe dessus. Étirer remplit le "
      + "rectangle choisi ; garder les proportions et aucun ont besoin de la taille "
      + "d'origine, mesurée à l'ouverture du fichier ici et saisissable sur une image. "
      + "L'atténuation se règle ici - un PNG à canal alpha est refusé net.",

    "hint.watermark": "Dessiné comme du contenu, jamais comme un fond : un navigateur "
                    + "supprime les fonds à l'impression, et un filigrane absent du papier "
                    + "manque justement sur la copie dont on pourrait se servir.",
    "hint.metadata": "Écrits dans les propriétés du fichier et son paquet XMP. Les jetons y "
                   + "sont remplacés comme ailleurs, donc {InvoiceNumber} y a sa place.",
    "hint.altText": "Sans lui, un lecteur d'écran n'annonce rien : la personne n'apprend même "
                  + "pas qu'il y a quelque chose.",
    "hint.approximate": "Aperçu approximatif : le navigateur mesure avec ses propres polices, "
                      + "cette page ne pagine pas et n'applique pas les répétitions.",
    "hint.approximateEngineFonts": "Aperçu approximatif : mesuré avec les polices du moteur, "
                      + "mais cette page ne pagine pas et n'applique pas les répétitions.",

    "alert.noPreview": "Aperçu exact indisponible : ", "alert.noPdf": "PDF indisponible : ",
    "alert.unreadable": "Lecture impossible : ", "alert.badData": "Données illisibles : ",
    "alert.noData": "Aucune donnée à enregistrer.",
    "alert.badDataKept": "Données illisibles, rien n'a été écrit : ",
    "confirm.new": "Repartir d'un gabarit vide ?", "confirm.clearData": "Vider les données ?",

    "Units.Millimeter": "Millimètres", "Units.Percentage": "Pourcentage",
    "FontSizeUnit.Rem": "Rem", "FontSizeUnit.Millimeter": "Millimètres de capitale",
    "FontSizeUnit.Point": "Points", "FontSizeUnit.PercentOfFrame": "% du cadre",
    "HorizontalAlignment.Left": "Gauche", "HorizontalAlignment.Center": "Centré",
    "HorizontalAlignment.Right": "Droite", "HorizontalAlignment.Justify": "Justifié",
    "VerticalAlignment.Top": "Haut", "VerticalAlignment.Middle": "Milieu",
    "VerticalAlignment.Bottom": "Bas",
    "LineOrientation.Horizontal": "Horizontal", "LineOrientation.Vertical": "Vertical",
    "LineOrientation.DiagonalDown": "Diagonale descendante",
    "LineOrientation.DiagonalUp": "Diagonale montante",
    "DashStyle.Solid": "Plein", "DashStyle.Dash": "Tirets", "DashStyle.Dot": "Pointillés",
    "DashStyle.DashDot": "Tiret-point",
    "ImageScaling.Uniform": "Garder les proportions", "ImageScaling.Stretch": "Étirer",
    "ImageScaling.None": "Aucun",
    "Symbology.Qr": "QR", "Symbology.Code128": "Code 128",
    "QrErrorCorrection.Low": "Faible (7 %)", "QrErrorCorrection.Medium": "Moyenne (15 %)",
    "QrErrorCorrection.Quartile": "Forte (25 %)", "QrErrorCorrection.High": "Maximale (30 %)",
    "FieldKind.Text": "Texte", "FieldKind.Checkbox": "Case", "FieldKind.Signature": "Signature",
    "Signing.ByHand": "À la main", "Signing.Digital": "Certificat numérique",
    "LayoutMode.Absolute": "Absolue", "LayoutMode.Stack": "Empilée",
    "Visibility.Visible": "Visible", "Visibility.Invisible": "Invisible (place gardée)",
    "Visibility.Gone": "Retiré (sans place)",
    "PageBreak.None": "Aucun", "PageBreak.Before": "Avant", "PageBreak.After": "Après",
    "PageBreak.BeforeAndAfter": "Avant et après",
    "Splitting.Auto": "Peut se couper", "Splitting.KeepTogether": "Garder d'un bloc",
    "PageScope.All": "Toutes", "PageScope.First": "La première", "PageScope.Last": "La dernière",
    "PageScope.ExceptFirst": "Sauf la première", "PageScope.ExceptLast": "Sauf la dernière",
    "PageScope.Odd": "Impaires", "PageScope.Even": "Paires",
    "zoom.fit": "Ajusté", "zoom.label": "Zoom",
    "action.detach": "Aperçu fenêtré", "action.attached": "Aperçu ouvert",
    "preview.title": "Frisket \u2014 aperçu",
    "hint.popup": "Le navigateur a bloqué la fenêtre. Autoriser les fenêtres surgissantes pour cette page, puis réessayer.",

    "section.columns": "Colonnes",
    "field.columnCount": "Combien", "field.columnGap": "Écart (mm)",
    "field.balance": "Équilibrer",
    "hint.balance": "Sur la page où le texte s’arrête, les colonnes se terminent ensemble au lieu d’avoir la première pleine et la dernière presque vide.",

    "section.condition": "Affiché quand",
    "field.showWhen": "Seulement si", "field.hideWhen": "Sauf si",
    "hint.condition": "Un chemin dans les données, par exemple Client.Vat. L\u2019élément dispara\u00eet \u2014 sans laisser de place \u2014 quand il n\u2019y a rien \u00e0 ce chemin\u202f: absent, vide, z\u00e9ro ou faux.",

    "panel.issues": "Contrôles", "issues.none": "Rien à signaler",
    "issues.count": "{errors} erreur(s), {warnings} avertissement(s)",
    "issues.onlyFaults": "seulement ce qui cloche",
    "theme.auto": "Auto", "theme.graphite": "Graphite",
    "theme.light": "Clair", "theme.frost": "Givr\u00e9",

    "editor.title": "Frisket — éditeur",
    "size.normal": "Normale", "size.small": "Petite", "size.large": "Grande", "size.huge": "Énorme",
    "editor.document": "Document",
    "editor.addText": "Texte",
    "editor.addImage": "Image",
    "editor.removeBlock": "Retirer le bloc",
    "editor.resize": "Glisser pour redimensionner",
    "editor.moveBlock": "Glisser pour déplacer ce bloc",
    "editor.moveHalf": "Glisser pour sortir ce bloc de la paire",
    "editor.addText": "Ajouter un paragraphe",
    "editor.addRow": "Ajouter une ligne", "editor.dropRow": "Retirer cette ligne",
    "editor.addColumn": "Ajouter une colonne", "editor.dropColumn": "Retirer cette colonne",
    "editor.fonts": "Polices",
    "editor.openHtml": "HTML\u2026",
    "editor.asking": "On demande au moteur\u2026",
    "editor.openMail": "Courriel\u2026",
    "editor.htmlWhat": "Les pages elles-m\u00eames, dessin\u00e9es en SVG avec les polices "
                     + "incorpor\u00e9es \u2014 ce que l'imprimante fera.",
    "editor.mailWhat": "Le m\u00eame rapport en message\u202f: du HTML qui coule, sans pages, "
                     + "sans rien d'incorpor\u00e9. Lancez les contr\u00f4les sur une cible "
                     + "courriel d'abord pour savoir ce qu'il perd en chemin.",
    "editor.noServer": "Le serveur d'aper\u00e7u ne r\u00e9pond pas. Lancez-le avec "
                     + "\u00ab dotnet run --project apps/server \u00bb et r\u00e9essayez \u2014 cette "
                     + "page ne sait pas \u00e9crire ce HTML elle-m\u00eame, et montrer son "
                     + "approximation \u00e0 la place serait exactement ce que ce bouton "
                     + "existe pour \u00e9viter.",
    "fonts.library": "Depuis le serveur",
    "fonts.websafe": "Standard web (courriel)",
    "fonts.default": "Police du document",
    "fonts.offline": "Le serveur ne r\u00e9pond pas\u202f: cette liste est ce que le document "
                   + "emploie d\u00e9j\u00e0. Une famille dont le moteur n'a pas le fichier est "
                   + "refus\u00e9e \u00e0 l'\u00e9criture du PDF \u2014 jamais remplac\u00e9e en silence, "
                   + "car une autre police recomposerait chaque ligne.",
    "fonts.approximate": "Les familles standard du web ne sont pas incorpor\u00e9es\u202f: "
                       + "c'est le lecteur qui les fournit. Juste pour un courriel, faux "
                       + "pour un PDF qui doit avoir la m\u00eame allure partout.",
    "editor.foreign": "cela se modifie dans le concepteur ; c'est gardé tel quel ici.",
    "editor.hint": "Tapez et cela devient du texte ; déposez une image et cela devient une "
                 + "image. Entrée commence un nouveau bloc plutôt qu'une nouvelle ligne, "
                 + "Entrée va à la ligne ; maj+Entrée crée un nouveau bloc. Glissez "
                 + "un bloc pour le déplacer, ou alt et une flèche ; tirez le coin "
                 + "d'une image pour la redimensionner, et les rails d'un tableau pour "
                 + "ajouter lignes et colonnes. Déposez un bloc sur le côté d'un "
                 + "autre pour les mettre l'un à côté de l'autre ; la forme "
                 + "montrée pendant le glisser est celle que vous obtiendrez. Ce "
                 + "visage ne pagine pas — la feuille s'allonge.",
    "target.tagged": "sortie balisée",
    "target.interactive": "champs remplissables",
    "hint.target": "Ce que vous allez produire. Décochez l'un ou l'autre pour voir ce que le gabarit perd en chemin — la page a la même allure dans les deux cas, et c'est ce qui rend la perte facile à manquer.",
    "name.row": "ligne", "name.headerRow": "ligne d\u2019en-tête",
    "issue.ua.language": "Le section ne dit pas dans quelle langue il est écrit\u202f: un lecteur l\u2019annonce avec la voix qu\u2019il a sous la main.",
    "issue.ua.title": "Le section n\u2019a pas de titre, or c\u2019est ce qu\u2019un lecteur annonce à l\u2019ouverture.",
    "issue.ua.altText": "Montré et non lu, sans texte de remplacement\u202f: un lecteur d\u2019écran n\u2019annonce rien du tout.",
    "issue.ua.formRole": "Seul un champ peut être un contrôle de formulaire. Celui-ci sera dessiné, et un dessin a besoin d\u2019un texte de remplacement.",
    "issue.ua.fieldName": "Ce champ n\u2019a pas d\u2019infobulle\u202f: un lecteur annonce un contrôle sans savoir à quoi il sert.",
    "issue.ua.tableHeader": "Ce tableau n\u2019a pas de ligne d\u2019en-tête\u202f: les valeurs sont annoncées sans rien qui dise ce qu\u2019elles sont.",
    "issue.sound.emptyBarcode": "Ce code-barres n\u2019a rien à encoder.",
    "issue.sound.emptyImage": "Cette image n’a ni image propre ni chemin vers une image dans les données : rien n’y sera jamais dessiné.",
    "issue.sound.emptyImportedPage": "Cette page importée ne nomme aucun PDF : rien n’y sera dessiné.",
    "issue.sound.importedPageNumber": "Les pages se comptent à partir de un.",
    "issue.sound.emptyImageGrid": "Cette planche d'images demande moins d'une colonne, ce qui se dessine en une seule colonne plutôt que ce qui est réglé ici.",
    "issue.sound.repeatPageBreak": "Ceci coupe une fois, avant toute la série répétée. La répétition clone ce qui est dans le conteneur, donc pour une page par enregistrement le saut se pose sur le bloc répété.",
    "issue.sound.emptySubTemplate": "Ce sous-gabarit ne nomme rien et ne porte rien, donc rien n'y sera jamais inséré. Lui donner un chemin, une référence st:// ou le JSON de l'autre gabarit.",
    "issue.sound.emptyAdvancedText": "Ce texte avancé n'a aucun segment avec quelque chose dedans, donc rien n'y sera jamais dessiné.",
    "issue.sound.justifiedBreak": "Ce texte est justifié et contient un saut de ligne. Un saut est étiré jusqu'à la marge comme une ligne repliée, ce qui n'est pas son sens - mettre chaque paragraphe dans son propre texte.",
    "issue.sound.nestedSection": "Une section ne peut pas se trouver dans une autre. Les sections sont une liste sur le rapport, jamais imbriquées - un format de page ne veut rien dire à l'intérieur d'une page.",
    "issue.sound.headingNumberNoFormat": "Ce texte demande un numéro de titre, et le rapport ne dit pas comment on l'écrit. Régler le format de numérotation - \"1.1.1\", \"A.a.1\" - ou retirer le jeton ; en attendant il reste sur la page tel qu'il a été tapé.",
    "issue.sound.headingNumberOutsideHeading": "Ce n'est pas un titre, donc le numéro n'a aucun niveau où se placer. Lui donner un rôle de titre, ou nommer le niveau dans le jeton lui-même - {HeadingNumber[1]} pour le chapitre auquel il appartient.",
    "issue.sound.headingNumberDepth": "Ce titre est plus profond que ce que le format de numérotation énonce, donc son dernier symbole est répété pour lui. Ajouter un niveau au format, sans quoi les deux niveaux les plus profonds se ressembleront.",
    "issue.sound.columnsIgnored": "Ceci demande des colonnes qui n'apparaîtront jamais ici : un conteneur qui place ses enfants par coordonnées n'a pas de flux à diviser, et un tableau ou une planche d'images divise déjà ses rangées à sa manière. Passer en conteneur empilé, ou retirer les colonnes.",
    "issue.sound.duplicateField": "Un autre champ s\u2019appelle aussi «\u202f{name}\u202f». Remplir l\u2019un remplit l\u2019autre, et une seule valeur survit.",
    "issue.sound.raggedTable": "Cette ligne a {count} cellule(s) là où le tableau en a {width}. Un validateur refuse les lignes de largeurs inégales.",

    "PdfRole.Table": "Tableau", "PdfRole.TableRow": "Ligne de tableau",
    "PdfRole.TableHeader": "Cellule d\u2019en-tête", "PdfRole.TableCell": "Cellule",
    "PdfRole.Form": "Contrôle",
    "PdfRole.Auto": "Selon le type", "PdfRole.Section": "Section",
    "PdfRole.Heading1": "Titre 1", "PdfRole.Heading2": "Titre 2", "PdfRole.Heading3": "Titre 3",
    "PdfRole.Heading4": "Titre 4", "PdfRole.Heading5": "Titre 5", "PdfRole.Heading6": "Titre 6",
    "PdfRole.Paragraph": "Paragraphe", "PdfRole.Caption": "Légende",
    "PdfRole.List": "Liste", "PdfRole.ListItem": "Élément de liste",
    "PdfRole.Figure": "Figure", "PdfRole.Artifact": "Décoration",
    "BackPageScope.EveryPage": "Chaque feuille", "BackPageScope.LastPage": "La dernière",
    "BackPageScope.FirstPage": "La première",
    "WatermarkMode.Tiled": "Répété", "WatermarkMode.Single": "Une fois, en grand",
    "WatermarkLayer.InFront": "Par-dessus (translucide)",
    "WatermarkLayer.Behind": "Sous le contenu",
    "type.Section": "Section", "type.Header": "En-tête", "type.Footer": "Pied de page",
    "type.Body": "Corps", "type.TextItem": "Texte", "type.LineItem": "Trait",
    "type.ImageItem": "Image", "type.BarcodeItem": "Code", "type.TableItem": "Tableau",
    "type.TableRow": "Ligne", "type.ContainerItem": "Groupe", "type.FieldItem": "Champ",
    "type.ImageGridItem": "Planche images", "type.SubTemplateItem": "Sous-gabarit",
    "field.alignAcross": "Dans la largeur",
    "ItemAlign.Top": "En haut", "ItemAlign.Middle": "Au milieu", "ItemAlign.Bottom": "En bas",
    "field.alignDown": "Dans la hauteur",
    "hint.align": "Déplace l'élément dans la place qu'on lui a donnée, et seulement sur un axe que rien d'autre n'a décidé : une coordonnée, un bord auquel il est collé ou une taille énoncée ont déjà répondu, et un alignement ne discute avec aucun des trois. Sur un axe épinglé, c'est une valeur légale qui ne fait rien.",
    "ItemAlign.None": "Là où il tombe", "ItemAlign.Start": "À gauche",
    "ItemAlign.Center": "Centré", "ItemAlign.End": "À droite",
    "type.AdvancedTextItem": "Texte avancé",
    "section.report": "Rapport — le fichier entier",
    "tree.report": "Rapport",
    "tree.section": "Section",
    "tree.untitled": "(sans titre)",
    "field.pageLimit": "Limite de pages", "field.noLimit": "sans limite",
    "hint.reportLevel": "Ceux-ci appartiennent au fichier, pas à la section modifiée : un seul titre, une seule langue, un seul auteur pour tout le document.",
    "issue.can.untagged": "Ce gabarit déclare des rôles, des textes alternatifs ou une langue, et rien n'en atteint un fichier non balisé.",
    "issue.can.flattenedFields": "{n} champ(s) seront dessinés au lieu de rester remplissables. La page a la même allure dans les deux cas.",
    "issue.can.flattenedFieldAltText": "Ce champ sera dessiné au lieu de rester remplissable, donc c'est une figure dans l'arbre de structure - et une figure qui n'a rien à dire d'elle-même est refusée par PDF/UA. Lui donner un texte alternatif, ou laisser les champs remplissables.",
    "issue.can.flattenedSignature": "Une signature aplatie est une image d'un endroit où signer. Personne ne peut la signer.",
    "issue.can.absoluteInFlow": "Les enfants de ce groupe se chevauchent dans la largeur, et la sortie visée n'a pas de pages où les poser. Ils seront écrits l'un sous l'autre.",
    "panel.report": "Rapport",
    "sections.open": "Sections…", "sections.title": "Sections",
    "sections.hint": "Un rapport est une liste de sections, chacun avec son propre format. Cette page en modifie un à la fois ; tout le reste ne bouge pas.",
    "sections.one": "Section", "sections.bands": "bande(s)",
    "sections.edit": "Modifier", "sections.remove": "Retirer",
    "sections.add": "Ajouter une section",
    "sections.confirmRemove": "Retirer la section {n} et tout ce qu'elle contient ?",
    "sections.editing": "section {n} sur {total}",
    "action.close": "Fermer",

    "paper.a4p": "A4 portrait", "paper.a4l": "A4 paysage",
    "paper.a3p": "A3 portrait", "paper.a3l": "A3 paysage",
    "paper.a5p": "A5 portrait", "paper.a5l": "A5 paysage",
    "paper.a6p": "A6 portrait",
    "paper.letter": "Letter", "paper.letterl": "Letter paysage",
    "paper.legal": "Legal", "paper.tabloid": "Tabloid"
  }
};

/// The language the wording is read in. The page decides how it is remembered and
/// what to repaint afterwards; this file only needs to know which one to look up.
let lang = "en";

function setLang(code) { lang = STRINGS[code] ? code : "en"; }

/// The wording for a key: the chosen language, then English, then the key itself -
/// so a string that was never written shows up rather than leaving a blank.
function t(key, args) {
  const chosen = STRINGS[lang] || {};
  const text = chosen[key] ?? STRINGS.en[key] ?? key;

  // Placeholders are named rather than positional: a sentence translated into
  // another language rarely keeps the same word order, and {count} survives
  // being moved where {0} does not.
  return args ? text.replace(/\{(\w+)\}/g, (m, k) => (k in args ? args[k] : m)) : text;
}

/// The label for an enum value. The value is what gets stored; this is only what
/// the person reading it sees.
function tv(group, value) {
  return t(group + "." + value);
}

const esc = s => String(s ?? "").replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
const hex = c => c ? "#" + [c.R, c.G, c.B].map(v => (v | 0).toString(16).padStart(2, "0")).join("") : ON_PAPER.defaultInk;
const hexToRgb = s => ({ R: parseInt(s.slice(1, 3), 16), G: parseInt(s.slice(3, 5), 16),
                         B: parseInt(s.slice(5, 7), 16), A: 255 });
const n3 = v => Number.isFinite(v) ? Math.round(v * 1000) / 1000 : 0;

// The same grid the engine lays down, so the preview shows where the marks fall
// rather than merely that there are some. Approximate in width only, like every
// other measurement here.
function watermarkMarks(report, page) {
  const w = report.Watermark;
  if (!w || !w.Text) return [];

  const font = Object.assign(newFont(), w.Font);
  const cap = (font.Size ?? 8) * (font.SizeUnit === "Millimeter" ? 1 : (report.RootFontSize ?? 3));
  const run = w.Text.length * cap * 0.62;          // no font file here; a ratio will do
  const rad = (w.Angle ?? 45) * Math.PI / 180;
  const cx = page.width / 2, cy = page.height / 2;

  if (w.Mode === "Single") {
    return [{ x: cx - Math.cos(rad) * run / 2 - Math.sin(rad) * cap / 2,
              y: cy + Math.sin(rad) * run / 2 - Math.cos(rad) * cap / 2, cap }];
  }

  const along = run + (w.Spacing > 0 ? w.Spacing : run / 2);
  const across = cap * 3 + (w.Spacing > 0 ? w.Spacing : 0);
  const reach = Math.hypot(page.width, page.height) / 2 + Math.max(along, across);
  const marks = [];

  for (let row = -Math.ceil(reach / across); row <= Math.ceil(reach / across); row++) {
    const shift = (row & 1) === 0 ? 0 : along / 2;
    for (let step = -Math.ceil(reach / along); step <= Math.ceil(reach / along); step++) {
      const u = step * along + shift, v = row * across;
      const x = cx + u * Math.cos(rad) + v * Math.sin(rad);
      const y = cy - u * Math.sin(rad) + v * Math.cos(rad);
      if (x < -along || x > page.width + along || y < -along || y > page.height + along) continue;
      marks.push({ x, y, cap });
    }
  }
  return marks;
}

/// The ink the engine gives a watermark that says nothing about its own colour.
///
/// One constant because the number lives in three places and one of them had already
/// moved without the others: the engine lowered it from 77 to 51 - measured too dark
/// over a paragraph - and the designer went on stamping 77 into every mark it made.
/// The harness held this file to the engine and never looked at what the panel wrote,
/// so it was guarding the fallback branch while the branch always taken drifted.
const WATERMARK_INK = { R: 128, G: 128, B: 128, A: 51 };

/// The rectangle a backdrop fills, mirroring BackdropArea in the engine.
///
/// The margin box is read off the bands rather than off the padding, exactly as
/// ReportRenderer.Margins does: a header takes its band out of the body, so a top
/// taken from the body band would start the picture below the letterhead.
function backdropArea(report, page, placed, band, section) {
  if (!report.Backdrop || report.Backdrop.Area !== "Margins")
    return { x: 0, y: 0, w: page.width, h: page.height };

  const kids = section.Childs || [];
  const head = placed.find(p => p.item === kids.find(k => k.$type === "Header"));
  const foot = placed.find(p => p.item === kids.find(k => k.$type === "Footer"));
  const top = head ? head.frame.top : band.top;
  const bottom = foot ? foot.frame.top + foot.frame.height : band.top + band.height;
  return { x: band.left, y: top, w: band.width, h: bottom - top };
}

/// The picture behind the page. Always behind, which is what the word means - a
/// picture over the content is a watermark, and that is a different panel.
function backdropSvg(report, area) {
  const b = report.Backdrop;
  if (!b || !b.Source || !b.Source.Data) return "";

  const opacity = b.Opacity == null ? 1 : Number(b.Opacity);
  if (!(opacity > 0)) return "";

  // The same fitting rule an ordinary image gets, asked of the same function: the
  // engine fits a backdrop through an ephemeral ImageItem for exactly this reason, and
  // a second copy of the rule here is a second rule waiting to disagree. Centred,
  // because that is what the engine does with a backdrop smaller than its rectangle.
  const fit = imageFit({ Scaling: b.Scaling || "Stretch",
                         Style: { AlignHorizontal: b.AlignHorizontal || "Center",
                                  AlignVertical: b.AlignVertical || "Center" } });

  return `<image x="${n3(area.x)}" y="${n3(area.y)}" width="${n3(area.w)}"`
       + ` height="${n3(area.h)}" preserveAspectRatio="${fit}"`
       + `${opacity < 1 ? ` opacity="${n3(opacity)}"` : ""}`
       + ` href="data:${b.Source.MediaType || "image/png"};base64,${b.Source.Data}"/>`;
}

function watermarkSvg(report, page) {
  const w = report.Watermark;
  if (!w || !w.Text) return "";
  const font = Object.assign(newFont(), w.Font);

  // A watermark's default ink is not a text's. newFont() hands out opaque black, so
  // `font.Color || grey` never once took the second branch - the fallback was dead
  // code, and this preview drew a solid black DRAFT across the page while the engine
  // drew grey at a fifth. Nothing compared the two, which is why it lasted.
  //
  // The number has to match Watermark.Font's default in the engine, and the harness
  // now reads that file and holds this to it.
  const ink = (w.Font && w.Font.Color) ? w.Font.Color : WATERMARK_INK;
  const fill = hex(ink);
  const op = (ink.A ?? 255) / 255;
  const em = ((font.Size ?? 8) * (font.SizeUnit === "Millimeter" ? 1 : (report.RootFontSize ?? 3))) / CAP_RATIO;

  return watermarkMarks(report, page).map(m =>
    `<text x="0" y="0" transform="translate(${n3(m.x)} ${n3(m.y)}) rotate(${n3(-(w.Angle ?? 45))})"` +
    ` font-family="${esc(font.Family || unnamedFace)}" font-size="${n3(em)}" fill="${fill}"` +
    `${op < 1 ? ` fill-opacity="${n3(op)}"` : ""}` +
    `${font.Bold ? ' font-weight="bold"' : ""}>${esc(w.Text)}</text>`).join("");
}

/// Draws one section as SVG. `show` carries what the viewer asked to see over the
/// page rather than what the page holds - band tints, element outlines - so this
/// stays answerable without a checkbox to read.
function render(report, selected, index = 0, show = {}) {
  const { placed, page, band, section } = layout(report, index);
  const out = [];
  // white-space:pre on every <text>, as the engine's SvgBackend writes it: a browser
  // collapses the space a piece of rich text starts with and draws the next character in
  // its place, touching the word in another style before it. A rule naming the element,
  // because the browser's own sheet sets nowrap on <text> and that beats anything inherited.
  // Kerning is left on here, unlike the engine's proof, because this page MEASURES with a
  // canvas that kerns - drawing without it would disagree with its own ruler.
  out.push(`<svg class="page" xmlns="http://www.w3.org/2000/svg" xml:space="preserve" width="${page.width}mm" height="${page.height}mm" viewBox="0 0 ${page.width} ${page.height}">`);
  out.push(`<style>text{white-space:pre}</style>`);

  // First of all, so everything else paints over it - it is the paper.
  if (report.Backdrop)
    out.push(backdropSvg(report, backdropArea(report, page, placed, band, section)));

  if (report.Watermark && report.Watermark.Layer === "Behind")
    out.push(watermarkSvg(report, page));

  if (show.bands) {
    const kids = section.Childs || [];   // the section laid out, not the one selected
    const h = placed.find(p => p.item === kids.find(k => k.$type === "Header"));
    const f = placed.find(p => p.item === kids.find(k => k.$type === "Footer"));
    if (h) out.push(bandRect(h.frame, ON_PAPER.headerBand));
    out.push(bandRect(band, ON_PAPER.bodyBand));
    if (f) out.push(bandRect(f.frame, ON_PAPER.footerBand));
  }

  for (const p of placed) {
    const { item, frame } = p;
    if (!["left", "top", "right", "bottom"].every(k => Number.isFinite(frame[k]))) continue;
    if (item.Style.Visibility !== "Visible") continue;

    if (item.Style.Background) out.push(box(frame, null, item.Style.Background));
    if (item.Style.Border) out.push(box(frame, item.Style.Border, null));
    if (p.gridEdges && p.grid) {
      for (let i = 0; i + 1 < p.gridEdges.length; i++)
        out.push(box(rect(p.gridEdges[i], frame.top, p.gridEdges[i + 1], frame.bottom), p.grid, null));
    }

    const content = deflate(frame, item.Style.Padding);
    if (item.$type === "TextItem") out.push(text(item, content, report));
    else if (item.$type === "LineItem") out.push(rule(item, content));
    else if (item.$type === "ImageItem") out.push(image(item, content));
    else if (item.$type === "FieldItem") out.push(field(item, content, report));
    else if (item.$type === "BarcodeItem") out.push(barcode(item, content));

    if (show.boxes && item.$type !== "Section")
      out.push(`<rect x="${n3(frame.left)}" y="${n3(frame.top)}" width="${n3(frame.width)}" height="${n3(frame.height)}" fill="none" stroke="${ON_PAPER.outline}" stroke-width=".1"/>`);
  }

  const sel = placed.find(p => p.item === selected);
  if (sel && Number.isFinite(sel.frame.left))
    // Drawn with its own attributes rather than a stylesheet rule. The rule lived
    // with the page and the markup did not, so anything that copied the SVG - the
    // window on the second screen, a saved file - lost it and fell back to the SVG
    // default fill, which is black: a selected band came out as a redaction.
    //
    // A wash of the accent colour rather than an outline alone: over a busy table
    // an outline is easy to lose, and at 12 % the text underneath still reads.
    out.push(`<rect class="sel-rect" x="${n3(sel.frame.left)}" y="${n3(sel.frame.top)}"`
           + ` width="${n3(sel.frame.width)}" height="${n3(sel.frame.height)}"`
           + ` fill="${ON_PAPER.selection}" fill-opacity=".12"`
           + ` stroke="${ON_PAPER.selection}" stroke-width=".4" stroke-dasharray="1.5 1"/>`);

  if (report.Watermark && report.Watermark.Layer !== "Behind")
    out.push(watermarkSvg(report, page));

  out.push("</svg>");
  return out.join("\n");
}

// A placeholder, on purpose. Encoding a QR here would mean a second implementation
// of Reed-Solomon and the mask scoring, in another language, drifting from the
// engine's - and a wrong one drawn confidently is worse than an honest box, since
// the whole point of the preview is to be believed. The exact preview draws the
// real code; this reserves its room and names what will land there.
function barcode(item, f) {
  const square = (item.Symbology || "Qr") === "Qr";
  const side = square ? Math.min(f.width, f.height) : 0;
  const box = square
    ? rect(f.left + (f.width - side) / 2, f.top + (f.height - side) / 2,
           f.left + (f.width + side) / 2, f.top + (f.height + side) / 2)
    : f;

  const label = square ? "QR" : "128";
  return `<rect x="${n3(box.left)}" y="${n3(box.top)}" width="${n3(box.width)}" height="${n3(box.height)}"` +
         ` fill="${ON_PAPER.placeholderFill}" stroke="${ON_PAPER.placeholderLine}" stroke-width=".2" stroke-dasharray="1 1"/>` +
         `<text x="${n3(box.left + box.width / 2)}" y="${n3(box.top + box.height / 2)}"` +
         ` text-anchor="middle" font-family="sans-serif" font-size="${n3(Math.min(6, box.height / 3))}"` +
         ` fill="${ON_PAPER.placeholderInk}">${esc(label)}</text>`;
}

const bandRect = (r, colour) => Number.isFinite(r.left)
  ? `<rect x="${n3(r.left)}" y="${n3(r.top)}" width="${n3(r.width)}" height="${n3(r.height)}" fill="${colour}" fill-opacity=".4"/>` : "";

function box(r, pen, fill) {
  const stroke = pen ? ` stroke="${hex(pen.Color)}" stroke-width="${n3(pen.Width)}"` : "";
  return `<rect x="${n3(r.left)}" y="${n3(r.top)}" width="${n3(r.width)}" height="${n3(r.height)}" fill="${fill ? hex(fill.Color) : "none"}"${stroke}/>`;
}

/// Both decorations share one SVG attribute - two of them would have the second
/// silently win, rather than drawing both lines.
function decoration(font) {
  const d = (font.Underline ? "underline " : "") + (font.Strikethrough ? "line-through" : "");
  return d.length ? ` text-decoration="${d.trimEnd()}"` : "";
}

function text(item, r, report) {
  const font = fontOf(item.Font, report);
  const resolved = tokens(item.Text, report, 1, 1, item);
  const lines = breakLines(resolved, font, report, r.width);
  const lh = lineHeight(font, report);
  // Same rule as the engine: the cap is centred in its line.
  const cap = emMm(font, report) * CAP_RATIO;
  const first = (lh + cap) / 2;
  const block = lines.length * lh;
  const offset = font.VerticalAlignment === "Middle" ? Math.max(0, (r.height - block) / 2)
               : font.VerticalAlignment === "Bottom" ? Math.max(0, r.height - block) : 0;
  const [x, anchor] = font.HorizontalAlignment === "Center" ? [(r.left + r.right) / 2, "middle"]
                    : font.HorizontalAlignment === "Right" ? [r.right, "end"] : [r.left, "start"];

  return lines.map((line, i) =>
    `<text x="${n3(x)}" y="${n3(r.top + offset + first + i * lh)}" font-family="${esc(font.Family || unnamedFace)}"` +
    ` font-size="${n3(emMm(font, report))}" fill="${hex(font.Color)}"` +
    (anchor !== "start" ? ` text-anchor="${anchor}"` : "") +
    justify(line, font, report, r.width, i === lines.length - 1) +
    (font.Bold ? ` font-weight="bold"` : "") + (font.Italic ? ` font-style="italic"` : "") +
    decoration(font) +
    `>${esc(line)}</text>`).join("\n");
}

/// What to add to each space so the line reaches the far margin, as an attribute.
///
/// The engine works out the same number and both widen every space by it - the PDF
/// by moving the gaps between words, this by naming a word spacing. The last line of
/// a block is never stretched: a closing line pulled out to the margin is what tells
/// a reader nothing follows.
function justify(line, font, report, width, isLast) {
  if (font.HorizontalAlignment !== "Justify" || isLast) return "";

  const spaces = (line.match(/ /g) || []).length;
  if (!spaces) return "";

  const slack = width - textWidth(line, font, report);
  return slack > 0 ? ` word-spacing="${n3(slack / spaces)}"` : "";
}

function rule(item, r) {
  const pen = item.Pen, midY = (r.top + r.bottom) / 2, midX = (r.left + r.right) / 2;
  const p = item.Orientation === "Vertical" ? [midX, r.top, midX, r.bottom]
          : item.Orientation === "DiagonalDown" ? [r.left, r.top, r.right, r.bottom]
          : item.Orientation === "DiagonalUp" ? [r.left, r.bottom, r.right, r.top]
          : [r.left, midY, r.right, midY];
  const dash = pen.DashStyle === "Dash" ? ` stroke-dasharray="2,1"`
             : pen.DashStyle === "Dot" ? ` stroke-dasharray="0.4,0.8"`
             : pen.DashStyle === "DashDot" ? ` stroke-dasharray="2,1,0.4,1"` : "";
  return `<line x1="${n3(p[0])}" y1="${n3(p[1])}" x2="${n3(p[2])}" y2="${n3(p[3])}" stroke="${hex(pen.Color)}" stroke-width="${n3(pen.Width)}"${dash}/>`;
}

// A field is drawn as what it will look like on paper, which is also what it looks
// like once the document is flattened.
function field(item, r, report) {
  const out = [];
  const lh = lineHeight(item.Font, report);
  const ticked = item.Value && !/^\{.*\}$/.test(item.Value)
                 && !["false", "Off", "0"].includes(item.Value);

  const box = item.Kind === "Checkbox"
    ? rect(r.left, r.top, r.left + Math.min(r.height, lh), r.top + Math.min(r.height, lh))
    : r;

  if (item.Fill) out.push(boxShape(box, null, item.Fill));
  if (item.Outline) out.push(boxShape(box, item.Outline, null));

  if (item.Kind === "Checkbox") {
    if (ticked) {
      const w = box.width, h = box.height, pen = { Color: item.Font.Color, Width: Math.max(.15, w * .12) };
      out.push(stroke(box.left + w * .22, box.top + h * .52, box.left + w * .42, box.top + h * .74, pen));
      out.push(stroke(box.left + w * .42, box.top + h * .74, box.left + w * .80, box.top + h * .26, pen));
    }
    return out.join("\n");
  }

  if (item.Kind === "Signature") {
    const rule = r.bottom - lh;
    out.push(stroke(r.left + 1, rule, r.right - 1, rule, { Color: { R: 150, G: 150, B: 150 }, Width: .15 }));
    if (item.Tooltip)
      out.push(text({ Text: item.Tooltip, Font: faint(item.Font) }, rect(r.left + 1, rule, r.right - 1, r.bottom), report));
    return out.join("\n");
  }

  const shown = ticked ? item.Value : item.Tooltip;
  if (shown)
    out.push(text({ Text: shown, Font: ticked ? item.Font : faint(item.Font) },
                  rect(r.left + 1, r.top + .4, r.right - 1, r.bottom), report));

  return out.join("\n");
}

function faint(font) { return Object.assign({}, font, { Color: { R: 150, G: 150, B: 150, A: 255 } }); }

const boxShape = (r, pen, fill) => box(r, pen, fill);

function stroke(x1, y1, x2, y2, pen) {
  return `<line x1="${n3(x1)}" y1="${n3(y1)}" x2="${n3(x2)}" y2="${n3(y2)}" ` +
         `stroke="${hex(pen.Color)}" stroke-width="${n3(pen.Width)}"/>`;
}

/// What ReportBinder.BindImage does, read back off the data: a bare base64 string
/// and a data: URI are both accepted, since a browser hands over the latter. Null
/// when there is nothing at that path, or no data loaded at all - the same
/// "nothing to show" that leaves an unsigned quote with no signature on it.
function resolvedImageSource(item) {
  if (!item.SourceField || !bound) return null;

  const raw = fromData(item.SourceField.trim());
  if (raw === undefined || raw === null) return null;

  const text = String(raw).trim();
  if (!text) return null;

  let mediaType = "image/png";
  let data = text;
  const comma = text.indexOf(",");

  if (/^data:/i.test(text) && comma > 0) {
    const header = text.slice(5, comma);
    const semicolon = header.indexOf(";");
    mediaType = semicolon > 0 ? header.slice(0, semicolon) : header;
    data = text.slice(comma + 1);
  }

  return { mediaType, data };
}

/// How the picture sits in the frame the layout granted, said the way SVG says it.
///
/// This echo cannot measure a picture - NaturalWidth and NaturalHeight are computed
/// from the decoded bytes and never travel in the template - so it cannot work out a
/// fitted rectangle the way ReportRenderer.Fit does. It does not have to:
/// preserveAspectRatio says both things at once, "meet" being the uniform fit and
/// xMin/xMid/xMax being the same three positions across the frame.
///
/// It used to say "none", which stretched every picture and so disagreed with the
/// engine's own default. Mirroring the alignment fixed the older divergence too.
function imageFit(item) {
  if (item.Scaling === "Stretch") return "none";
  const style = item.Style || {};
  const across = { Center: "xMid", End: "xMax" }[style.AlignHorizontal] || "xMin";
  // YMin unless something says otherwise, which is what an ImageItem wants: the flow
  // has already decided where its frame is and the engine puts it at the top of it.
  // A backdrop's rectangle is the whole sheet, so it says.
  const down = { Center: "YMid", End: "YMax" }[style.AlignVertical] || "YMin";
  return `${across}${down} meet`;
}

function image(item, r) {
  const fit = imageFit(item);
  const bytes = resolvedImageSource(item);
  if (bytes)
    return `<image x="${n3(r.left)}" y="${n3(r.top)}" width="${n3(r.width)}" height="${n3(r.height)}" preserveAspectRatio="${fit}" href="data:${bytes.mediaType};base64,${bytes.data}"/>`;

  if (item.Source && item.Source.Data)
    return `<image x="${n3(r.left)}" y="${n3(r.top)}" width="${n3(r.width)}" height="${n3(r.height)}" preserveAspectRatio="${fit}" href="data:${item.Source.MediaType};base64,${item.Source.Data}"/>`;

  return `<rect x="${n3(r.left)}" y="${n3(r.top)}" width="${n3(r.width)}" height="${n3(r.height)}" fill="${ON_PAPER.placeholderFill}" stroke="${ON_PAPER.placeholderLine}" stroke-width=".2"/>` +
         `<text x="${n3((r.left + r.right) / 2)}" y="${n3((r.top + r.bottom) / 2)}" text-anchor="middle" font-size="3" fill="${ON_PAPER.placeholderInk}">image</text>`;
}

// ---------------------------------------------------------------------------
// The template validator - the echo of engine/Model/TemplateValidator.cs.
//
// It reads a report and says what is wrong with it, in the same rules at the same
// severities as the engine, because a designer that clears a document veraPDF then
// refuses is worse than no check at all. What the page keeps is only the memory of
// the last pass and which elements to light up; deciding is here.
// ---------------------------------------------------------------------------

/// What each kind of item is called in the tree. Looked up rather than fixed, so it
/// follows the chosen language like everything else.
const labelOf = type => t("type." + type);
const nameOf = item =>
  item.$type === "FieldItem" ? (item.Name || "champ") + " · " + item.Kind :
  item.$type === "TextItem" ? (item.Text || "").slice(0, 26) || "(vide)" :
  item.$type === "TableRow" ? (item.IsHeader ? "en-tête" : "ligne") :
  labelOf(item.$type);

/// Whether an absolutely laid out group's children sit beside one another across the
/// band, rather than over one another. The echo of FlowingHtml.SideBySide, and it has
/// to be: the rule below reports what a message loses, and a message keeps a row of
/// cells perfectly well. Warning about a group the emitter handles is a rule people
/// learn to ignore.
function sideBySide(container, band) {
  const spans = [];

  for (const child of container.Childs || []) {
    if ((child.Style || {}).Visibility && child.Style.Visibility !== "Visible") continue;

    const loc = (child.Style || {}).Location || {};
    const num = v => (v === "NaN" || v == null ? NaN : Number(v));
    const dock = String(loc.Dock || "None");

    let left = dock.includes("Left") ? 0 : num(loc.LeftValue);
    let right = dock.includes("Right") ? band
              : Number.isNaN(num(loc.RightValue)) ? NaN : band - num(loc.RightValue);
    const width = num(loc.WidthValue);

    if (Number.isNaN(left) && !Number.isNaN(right) && !Number.isNaN(width)) left = right - width;
    else if (!Number.isNaN(left) && Number.isNaN(right) && !Number.isNaN(width)) right = left + width;

    // Nothing said where it is, so it takes the whole band - and something taking the
    // whole band is not one of a row.
    if (Number.isNaN(left) || Number.isNaN(right) || right <= left) return null;
    spans.push([left, right]);
  }

  if (spans.length < 2) return null;
  spans.sort((a, b) => a[0] - b[0]);

  for (let i = 1; i < spans.length; i++)
    if (spans[i][0] < spans[i - 1][1] - 0.01) return null;

  return spans;
}

/// What an element counts as when nothing was declared. Mirrors PdfRoles.Resolve.
function roleOf(item, parent) {
  if (item.Role && item.Role !== "Auto") return item.Role;
  if (parent && parent.$type === "TableRow") return parent.IsHeader ? "TableHeader" : "TableCell";
  if (item.$type === "TableItem") return "Table";
  if (item.$type === "TableRow") return "TableRow";
  if (item.$type === "LineItem") return "Artifact";
  if (item.$type === "ImageItem" || item.$type === "BarcodeItem") return "Figure";
  if (item.$type === "FieldItem")
    return item.Kind === "Signature" && item.Signing === "ByHand" ? "Figure" : "Form";
  if (item.$type === "TextItem") return "Paragraph";
  return "Section";
}

/// Reads the template and returns what will go wrong. `profile` is a set holding
/// "sound", "accessible", or both.
function validate(rep, profile) {
  profile = profile || { sound: true, accessible: true, capable: true };

  const found = [];
  const add = (rule, level, item, where, args) =>
    found.push({ rule, level, item, where, message: t("issue." + rule, args) });

  if (profile.accessible) {
    if (!(rep.Language || "").trim()) add("ua.language", "error", null, t("panel.section"));
    if (!(rep.Title || "").trim()) add("ua.title", "error", null, t("panel.section"));
  }

  // Every section, as the engine does, and named so a fault in the third does not
  // read as a fault in the first.
  const sections = Array.isArray(rep.Sections) && rep.Sections.length
    ? rep.Sections
    : (rep.Section ? [rep.Section] : []);

  // And every section hydrated, on the way in, exactly as layout() hydrates the one it
  // composes. Section 1 puts it plainly: nothing can assume a key is there, because a
  // forgotten read does not crash - it is undefined. Reading a MISSING key is worse
  // here than in layout, where it lands at NaN and is seen: this function's answer is
  // a list of faults, and an absent property reads as a value the author never wrote.
  //
  // Signing is the case that showed it. ByHand is the zero of the enum, so the engine
  // never writes it, so `f.Signing === "ByHand"` was false for every pen-signed
  // signature read from a file - and the page invented two errors against a template
  // the engine passes in silence, while losing the one real PDF/UA fault it did have.
  // The designer hydrated only the section being edited; validate walks them all.
  sections.forEach(hydrate);
  if (rep.BackPage && rep.BackPage.Content) hydrate(rep.BackPage.Content);

  sections.forEach((section, i) =>
    walkFor(section, sections.length === 1 ? t("panel.section") : `${t("panel.section")} ${i + 1}`,
            profile, add, rep.HeadingNumbering));
  if (rep.BackPage) walkFor(rep.BackPage.Content, t("panel.back"), profile, add, rep.HeadingNumbering);

  // The third profile, and the only one that has to be told what is about to be
  // produced: the same template is faultless for one output and half-lost in another.
  // Silent with no target, since with none there is nothing to compare against - which
  // is what the engine does too, and what keeps the preview from warning about a loss
  // that is not happening here.
  if (profile.capable && profile.target) {
    const items = [];
    const gather = node => {
      (node.Childs || []).forEach(child => { items.push(child); gather(child); });
    };
    sections.forEach(gather);          // the same reach as Report.AllItems

    if (!profile.target.Tagged) {
      const described = (rep.Language || "").trim()
        || items.some(i => (i.Role && i.Role !== "Auto") || (i.AltText || "").trim());
      if (described) add("can.untagged", "warning", null, t("panel.report"));
    }

    // Placed at coordinates, going somewhere with no coordinates. Its children can
    // only be written one under the next, which is not the arrangement that was drawn.
    if (profile.target.Paged === false) {
      const band = Math.max(1, ((rep.Sections || [])[0] || {}).Dimension?.Width || 210);
      const pinned = items.filter(i => i.$type === "ContainerItem"
                                    && i.Layout === "Absolute"
                                    && (i.Childs || []).length > 1
                                    && !sideBySide(i, band));
      // One per group: each is a different place to go and put right, and a single
      // finding carrying a count cannot be told from one carrying a different count.
      for (const group of pinned)
        add("can.absoluteInFlow", "warning", group, t("panel.report"));
    }

    if (!profile.target.Interactive) {
      // A signature given by hand is drawn in either output, so flattening takes
      // nothing from it: none of the rules here is about it, the count included. That
      // it is a figure owing an alternative text is ua.altText's business, asked
      // whatever the output.
      const losing = items.filter(i => i.$type === "FieldItem")
                          .filter(f => !(f.Kind === "Signature" && f.Signing === "ByHand"));

      if (losing.length)
        add("can.flattenedFields", "warning", losing[0], t("panel.report"), { n: losing.length });

      // Drawn rather than fillable means a figure in the structure tree, and a figure
      // with nothing to say for it is refused by PDF/UA. Only when tagged: untagged
      // there is no tree to be wrong about, and a rule that accuses a template that is
      // right is how people learn to stop reading the panel.
      if (profile.target.Tagged)
        for (const mute of losing.filter(f => !(f.AltText || "").trim()))
          add("can.flattenedFieldAltText", "error", mute, t("panel.report"));

      // One per signature: each is its own place to go and put right - globally by
      // leaving the fields fillable, or here by signing this one with a pen, which
      // survives being drawn. One finding standing for the rest hides the others.
      for (const signature of losing.filter(f => f.Kind === "Signature"))
        add("can.flattenedSignature", "error", signature, t("panel.report"));
    }
  }

  return found;
}

function walkFor(container, where, profile, add, numbering) {
  if (container.$type === "TableItem") tableFaults(container, where, profile, add);

  const names = new Set();

  for (const child of container.Childs || []) {
    const here = where + " › " + shortName(child);
    const role = roleOf(child, container);

    if (profile.accessible) {
      // A figure with nothing to say in its place is announced as nothing at all:
      // the person is not told anything is there.
      if (role === "Figure" && !(child.AltText || "").trim())
        add("ua.altText", "error", child, here);

      // A role naming a control, on something that will never be one. An authored
      // role is taken before anything else is asked, so this walks straight past the
      // guard roleOf has for exactly that - and the renderer then does the only sane
      // thing left and writes a figure, which owes an alternative text that nothing
      // asked the author for.
      if (role === "Form" && child.$type !== "FieldItem")
        add("ua.formRole", "error", child, here);

      // The tooltip is a field's accessible name.
      if (role === "Form" && child.$type === "FieldItem" && !(child.Tooltip || "").trim())
        add("ua.fieldName", "error", child, here);
    }

    if (profile.sound) {
      // Columns divide a flow; absolute children carry their own coordinates, and a
      // table or an image grid has its own dispatch that never consults ColumnCount
      // either, whatever Layout says - either way the setting does nothing, silently.
      if ((child.ColumnCount || 1) > 1 && child.Childs
          && (child.Layout !== "Stack" || child.$type === "TableItem" || child.$type === "ImageGridItem"))
        add("sound.columnsIgnored", "warning", child, here);

      // A hard break inside justified text is stretched like a wrapped line, which
      // is not what a break means. One paragraph per text item.
      if (child.$type === "TextItem" && (child.Font || {}).HorizontalAlignment === "Justify"
          && (child.Text || "").includes("\n"))
        add("sound.justifiedBreak", "warning", child, here);

      // A heading number asked for and not answerable. All three leave the token
      // visible on the page rather than resolving to something wrong, which is loud -
      // but loud once the document is out, which is too late.
      if (child.$type === "TextItem"
          && /\{(section)?headingnumber(\[\d+\])?[:}]/i.test(child.Text || "")) {
        const declared = headingDepth(numbering);
        const level = headingLevelOf(child);
        const named = /\{(section)?headingnumber\[\d+\]/i.test(child.Text || "");

        if (!declared) add("sound.headingNumberNoFormat", "warning", child, here);
        if (!level && !named) add("sound.headingNumberOutsideHeading", "warning", child, here);
        if (declared && level > declared) add("sound.headingNumberDepth", "warning", child, here);
      }

      // A section inside a section. Nothing in the model stops it - a Section is an
      // item like any other, which is what lets a report hold a list of them - and it
      // means nothing: a page size inside a flow already laid out on a page.
      //
      // This rule lived in the engine alone from the day it was written. frisket check
      // refused a template this page called sound, and had the echo ever raised it, it
      // would have printed the key: there was no label in either language either.
      if (child.$type === "Section") add("sound.nestedSection", "error", child, here);

      if (child.$type === "BarcodeItem" && !(child.Value || "").trim())
        add("sound.emptyBarcode", "error", child, here);

      // Neither a picture baked into the template nor a path to one in the data:
      // both backends draw nothing at all for an empty Source, silently.
      if (child.$type === "ImageItem" && !(child.Source && child.Source.Data)
          && !(child.SourceField || "").trim())
        add("sound.emptyImage", "error", child, here);

      // The same fault in a new element: neither a PDF of its own nor a path to one.
      if (child.$type === "ImportedPageItem") {
        if (!(child.Data || "").length && !(child.SourcePath || "").trim())
          add("sound.emptyImportedPage", "error", child, here);

        // Counted from one, because a template is written by a person. Zero is not a
        // smaller number here, it is a different question.
        if ((child.Page ?? 1) < 1)
          add("sound.importedPageNumber", "error", child, here);
      }

      // Fewer than one column draws as a single one - legal, but not what the
      // number on screen promised.
      if (child.$type === "ImageGridItem" && (child.GridColumns || 0) < 1)
        add("sound.emptyImageGrid", "error", child, here);

      // No path, no file: the engine has nothing to resolve and the item stays
      // empty forever, silently.
      if (child.$type === "SubTemplateItem" && !(child.TemplatePath || "").trim()
          && !(child.TemplateData || "").length)
        add("sound.emptySubTemplate", "error", child, here);

      // A break on a repeating container breaks once, before the whole run - which
      // somebody may well want, so this fires only where the break had somewhere
      // better to be: a run wrapping a single block that carries none of its own.
      // Repeating clones what is *inside* a container, never the container.
      {
        const breaks = ((child.Style || {}).PageBreak) || "None";
        const only = (child.Childs || []).length === 1 ? child.Childs[0] : null;
        if ((child.RepeatOver || "").trim()
            && (breaks === "Before" || breaks === "BeforeAndAfter")
            && only && only.$type === "ContainerItem"
            && (((only.Style || {}).PageBreak) || "None") === "None")
          add("sound.repeatPageBreak", "warning", child, here);
      }

      if (child.$type === "AdvancedTextItem") {
        // A blank cell is a blank cell, not a fault - see the engine's own note. The
        // parent is the container being walked, so the test is free here.
        if (container.$type !== "TableRow" && (child.Runs || []).every(r => !r.Text))
          add("sound.emptyAdvancedText", "error", child, here);

        // Justify was reported here until the resolver learnt to stretch a line
        // assembled from several runs. It does now, so the rule is gone rather than
        // kept as a reassuring no-op - and so is the one about {Pages} in a run, since
        // the resolver fills the real total into rich text as well (20 October 2026).
      }

      if (child.$type === "FieldItem" && (child.Name || "").trim()) {
        if (names.has(child.Name))
          add("sound.duplicateField", "warning", child, here, { name: child.Name });
        names.add(child.Name);
      }
    }

    if (child.Childs) walkFor(child, here, profile, add, numbering);
  }
}

function tableFaults(table, where, profile, add) {
  const rows = (table.Childs || []).filter(r => r.$type === "TableRow"
                                           && (r.Style || {}).Visibility !== "Gone");
  if (!rows.length) return;

  if (profile.accessible && !rows.some(r => r.IsHeader))
    add("ua.tableHeader", "warning", table, where);

  if (!profile.sound) return;

  // The declared columns are the authority; without them the widest row is the
  // best guess at what the table was meant to be.
  const width = (table.Columns || []).length
              || Math.max(...rows.map(r => (r.Childs || []).length));

  for (const row of rows.filter(r => (r.Childs || []).length !== width))
    add("sound.raggedTable", "error", row, where + " › " + shortName(row),
        { count: (row.Childs || []).length, width });
}

/// A name short enough for a path, and recognisable enough to find on screen.
function shortName(item) {
  if (item.$type === "TextItem" && (item.Text || "").trim()) {
    const clean = item.Text.replace(/\n/g, " ").trim();
    return clean.length <= 24 ? clean : clean.slice(0, 24) + "…";
  }
  if (item.$type === "FieldItem" && (item.Name || "").trim()) return item.Name;
  if (item.$type === "TableRow") return t(item.IsHeader ? "name.headerRow" : "name.row");
  return labelOf(item.$type);
}

// ---------------------------------------------------------------------------
// Run algebra - pure list surgery on a TextRun list, no surface in sight.
// Both faces need it and neither owns it.
// ---------------------------------------------------------------------------

/// Neighbouring runs whose style is identical become one, so selecting a range and
/// applying the same style twice does not fragment the list forever.
function mergeSameStyleRuns(runs) {
  const out = [];
  for (const run of runs) {
    const prev = out[out.length - 1];
    if (prev && JSON.stringify(prev.Font) === JSON.stringify(run.Font)) prev.Text += run.Text;
    else out.push({ Text: run.Text, Font: run.Font ? Object.assign({}, run.Font) : null });
  }
  return out;
}

/// Cuts runs at the given flat offsets without touching any text or style - pure
/// insertion of boundaries, so a style can then be toggled on exactly the pieces
/// that fall inside the selection.
function splitRunsAt(runs, offsets) {
  const result = [];
  let pos = 0;
  for (const run of runs) {
    const cuts = offsets.filter(o => o > pos && o < pos + run.Text.length).sort((a, b) => a - b);
    let last = 0;
    for (const cut of cuts) {
      const at = cut - pos;
      result.push({ Text: run.Text.slice(last, at), Font: run.Font ? Object.assign({}, run.Font) : null });
      last = at;
    }
    result.push({ Text: run.Text.slice(last), Font: run.Font ? Object.assign({}, run.Font) : null });
    pos += run.Text.length;
  }
  return result.filter(r => r.Text.length > 0);
}
