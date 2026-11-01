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
  else if (item.$type === "ImageItem") {
    const ratio = pictureRatio(item);
    height = ratio > 0 ? content / ratio : 0;
  }
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
  //
  // And a Layout that is absent is whatever the engine's constructor says, which is Stack for
  // the four that flow - Body, TableItem, ImageGridItem, SubTemplateItem (CLAUDE.md 7). The
  // engine writes that Stack, so a file it wrote never showed the difference; a template a
  // host or an AI writes by hand leaves it out, and its body or its sub-document was laid out
  // here as Absolute - every child on top of the first - while the engine stacked them.
  // Found on 20 October 2026 by looking at a sub-document drawn both ways side by side.
  const D = {
    LineItem:      { Orientation: "Horizontal" },
    ImageItem:     { Scaling: "Uniform" },
    ContainerItem: { Layout: "Absolute", ColumnCount: 1, ColumnGap: 6, BalanceColumns: true },
    TableRow:      { Layout: "Absolute" },
    TableItem:     { Layout: "Stack", RepeatHeader: true },
    ImageGridItem: { Layout: "Stack" },
    Header:        { ColumnCount: 1, ColumnGap: 6, BalanceColumns: true },
    Footer:        { ColumnCount: 1, ColumnGap: 6, BalanceColumns: true },
    Body:          { Layout: "Stack", ColumnCount: 1, ColumnGap: 6, BalanceColumns: true },
    FieldItem:     { Kind: "Text", Signing: "ByHand", Multiline: false, ReadOnly: false,
                     Required: false, MaxLength: 0 },
    BarcodeItem:   { Value: "", Symbology: "Qr", Correction: "Medium",
                     QuietZone: 0, ShowText: true },
    SubTemplateItem: { Layout: "Stack", Mode: "Splice", TemplatePath: "", TemplateData: "" },
    ImportedPageItem: { Data: "", SourcePath: "", Page: 1, Scaling: "Uniform" }
  }[item.$type];

  if (D) for (const k in D) if (item[k] === undefined) item[k] = D[k];

  (item.Columns || []).forEach(c => { if (c.WidthUnit === undefined) c.WidthUnit = "Millimeter"; });
  (item.Childs || []).forEach(hydrate);
  return item;
}

/// Splices in every sub-template that carries its own JSON, and every st:// reference the
/// caller can answer for.
///
/// A path needs a file this page does not have, so it stays empty - the engine draws the same
/// line, and the panel says which is which rather than letting the preview pretend. A
/// reference is a program's to answer, and since 20 October 2026 the program can be asked:
/// `lookup(reference)` returns the referenced template - JSON text or an object - or
/// undefined while it is not known yet, which draws the block empty until it is. That is
/// ISubTemplates.Find on the engine's side, answered by the same host from the same store.
///
/// What a piece holds is expanded in turn, as the engine expands it, and a reference already
/// being expanded higher up draws empty rather than looping - the engine's cycle guard,
/// which throws; a preview has nothing to throw to.
///
/// Before applyHeadingNumbers below, and that is not tidiness: a heading inside a
/// spliced template has to be counted with the rest of them, which is only possible
/// because expansion comes first. The engine says so in as many words - "4.1. Method
/// coming from the sub-template, possible only because expansion precedes everything
/// else" (CLAUDE.md section 15b).
function expandCarried(node, lookup = null, chain = new Set()) {
  for (const child of (node.Childs || [])) {
    const carried = child.$type === "SubTemplateItem" && (child.TemplateData || "").length > 0;
    const reference = child.$type === "SubTemplateItem" ? referenceOf(child) : null;

    if (carried || reference !== null) {
      // Append brings a document, not a block: its sections land after this one, and
      // this canvas shows one section. Splicing its body into the flow here would
      // draw a page that will never exist - which is the fault this function's own
      // comment warns about, met from the other end.
      if (child.Mode === "Append") { child.Childs = []; continue; }

      let inner = null;
      try {
        inner = carried
          ? JSON.parse(decodeURIComponent(escape(atob(child.TemplateData))))
          : chain.has(reference) ? null : asTemplate(lookup ? lookup(reference) : undefined);
      } catch {
        // Not a template, or not base64. Empty is the honest drawing: the panel's
        // check says what is wrong, and a preview that guessed would be worse.
        inner = null;
      }

      const body = inner && (inner.Sections || [])
        .flatMap(s => s.Childs || []).find(c => c.$type === "Body");

      // Replaced rather than appended: layout runs on every keystroke, and appending
      // would splice the same template in again on each one.
      child.Childs = (body && body.Childs) ? JSON.parse(JSON.stringify(body.Childs)) : [];

      const deeper = new Set(chain);
      if (reference !== null) deeper.add(reference);
      expandCarried(child, lookup, deeper);
      continue;
    }
    if (child.Childs) expandCarried(child, lookup, chain);
  }
}

/// A sub-template whose children come from somewhere else - its own bytes, or the host's
/// store - and are therefore drawn here and never edited: they are copied in afresh on
/// every layout, and dropped again on saving.
function holdsPiece(item) {
  return item.$type === "SubTemplateItem"
      && ((item.TemplateData || "").length > 0 || referenceOf(item) !== null);
}

/// What a sub-template's st:// reference names - the text after the scheme, trimmed - or null
/// when it names a file, or nothing. The scheme is looked for at the very start, untrimmed, as
/// the engine looks for it: " st://x" is a path there, and must not be a reference here.
function referenceOf(item) {
  const path = item.TemplatePath || "";
  return /^st:\/\//i.test(path) ? path.slice(5).trim() : null;
}

/// A template as a host may hand it over: JSON text, an object, or nothing yet.
function asTemplate(value) {
  if (typeof value === "string") return JSON.parse(value);
  return value && typeof value === "object" ? value : null;
}

/// The template the exact preview is sent, with every reference it can answer for carried as
/// bytes instead of named. The engine behind the preview is not the host and cannot resolve a
/// name; carried, the pieces reach it exactly as this page shows them. What is SAVED keeps the
/// references - see forEngine - so changing a piece in the host's store changes every document
/// that names it, which is the whole point of naming it.
function carryReferences(template, lookup) {
  const copy = JSON.parse(JSON.stringify(template));
  const encode = text => btoa(unescape(encodeURIComponent(text)));

  (function carry(node, chain) {
    for (const child of (node.Childs || [])) {
      // A piece embedded from a file may itself name pieces of the library.
      if (child.$type === "SubTemplateItem" && (child.TemplateData || "").length) {
        try {
          const inner = JSON.parse(decodeURIComponent(escape(atob(child.TemplateData))));
          carry({ Childs: inner.Sections || [] }, chain);
          child.TemplateData = encode(JSON.stringify(inner));
        } catch { /* not a template: the engine says so, as it would have */ }
        continue;
      }
      const reference = child.$type === "SubTemplateItem" ? referenceOf(child) : null;
      if (reference !== null && !chain.has(reference)) {
        let inner = null;
        try { inner = asTemplate(lookup(reference)); } catch { inner = null; }
        if (inner) {
          const deeper = new Set(chain).add(reference);
          const nested = JSON.parse(JSON.stringify(inner));
          carry({ Childs: nested.Sections || [] }, deeper);
          child.TemplateData = encode(JSON.stringify(nested));
          child.TemplatePath = "";
          child.Childs = [];
        }
        continue;
      }
      carry(child, chain);
    }
  })({ Childs: copy.Sections || [] }, new Set());

  return copy;
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
  // this one showing what it used to say, and saying it with confidence. A referenced one's
  // are derived from the host's store, for the same reason and more so: keeping them would
  // freeze the piece into the file, and a changed article would change nothing.
  (function strip(node) {
    for (const child of (node.Childs || [])) {
      if (holdsPiece(child))
        child.Childs = [];
      else
        strip(child);
    }
  })({ Childs: copy.Sections || [] });

  return copy;
}

function layout(report, index = 0, lookup = null) {
  normaliseReport(report);
  expandCarried({ Childs: report.Sections || [] }, lookup);

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

/// Where an item's left and right edges fall from what it states - a dock, an edge, a width -
/// and NaN for an edge nothing decided: the engine's ComputeHorizontal, which is all its Flow
/// asks before giving the column to whatever is left undecided.
///
/// Not computeFrame, which sizes a text to its content. That is right for a text at a
/// coordinate and wrong in a flow, where the engine breaks the lines at the column and lines
/// them up against its edges. Until 20 October 2026 the flow took computeFrame's answer, so a
/// paragraph in a stacked body never wrapped here and ran off the page, and a text aligned
/// right sat against the end of its own words - while the engine wrapped the one and put the
/// other against the margin. Found by setting a sub-document's two previews side by side.
function horizontalOf(item, area) {
  const loc = item.Style.Location;
  const a = dockOf(loc);
  let left = NaN, right = NaN;
  if (a & DOCK.Left) left = area.left;
  else if (has(loc.LeftValue)) left = area.left + mm(num(loc.LeftValue), loc.LeftUnit, area.width);
  if (a & DOCK.Right) right = area.right;
  else if (has(loc.RightValue)) right = area.right - mm(num(loc.RightValue), loc.RightUnit, area.width);

  const w = has(loc.WidthValue) ? mm(num(loc.WidthValue), loc.WidthUnit, area.width) : NaN;
  if (!Number.isFinite(w)) return { left, right };
  if (Number.isFinite(left) && !Number.isFinite(right)) right = left + w;
  else if (!Number.isFinite(left) && Number.isFinite(right)) left = right - w;
  else if (!Number.isFinite(left)) {
    // Nothing pinned it, so the alignment decides - against the left without one.
    left = alignPlace(area.left, area.width, w, item.Style.AlignHorizontal || "None");
    right = left + w;
  }
  return { left, right };
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
    // A text asks only what it states, as the engine's Flow asks it; everything else keeps
    // computeFrame, which agrees with that for anything not sized from its content and also
    // clamps a width to its constraints, as the engine does when it places the item.
    const probe = child.$type === "TextItem"
      ? horizontalOf(child, column)
      : computeFrame(child, rect(column.left, top, column.right, area.bottom), report);
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

    "family.display": "Display", "family.input": "Input", "family.library": "Library",
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
    "hint.subTemplate": "Another template, three ways. A path is resolved next to the file that names it, which only the CLI and the native library can do - this page has no file of its own, so a path shows here as an empty box. st:// names something the calling program looks up, and is drawn here when the program offers its library to this page. Embedding it puts the other template inside this one, which travels anywhere.",
    "field.libraryPiece": "From the library",
    "hint.pieceReady": "Drawn from the library: {title}. It is edited there, not here - the file keeps the reference only, so changing the piece changes every document that names it.",
    "hint.pieceLoading": "Asking the library for {reference}…",
    "hint.pieceMissing": "The library does not know {reference}, so nothing is drawn here. Whatever composes the document will have to.",
    "hint.pieceNoLibrary": "This page has no library to ask, so {reference} is drawn as an empty box. The program that composes the document looks it up.",
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
    "section.repeat": "Repeat",
    "field.repeatOver": "Over the list", "field.emptyText": "When it is empty",
    "hint.repeatOver": "A list in the data, such as Lines: what this holds is drawn once per entry - a table keeps its header rows and repeats the others, two of them alternating. A token inside reads the entry first, then the whole of the data. With nothing in the list the element goes, header and all, unless it says what to show instead. This page draws it once; the exact preview draws every entry.",
    "editor.addField": "Insert a merge field",
    "editor.fromData": "Picture from the data: {path}",
    "fieldType.text": "Text", "fieldType.number": "Number", "fieldType.decimal": "Decimal",
    "fieldType.currency": "Amount", "fieldType.date": "Date", "fieldType.datetime": "Date and time",
    "fieldType.image": "Picture", "fieldType.list": "Lists",
    "fields.eachLine": "In each line of {list}",
    "editor.repeats": "\u21BB Repeated for each entry of {list}",
    "live.off": "Exact preview: off", "live.canvas": "Exact preview: on the canvas",
    "live.beside": "Exact preview: beside", "live.window": "Exact preview: in a window",
    "live.asking": "The engine is drawing…",
    "live.ready": "The engine's pages, up to date ({count})",
    "live.failed": "The engine did not answer: {message}",
    "live.windowClosed": "The window is closed - Preview window opens it",
    "live.page": "page {page} / {pages} - engine",
    "live.noServer": "This page was given no engine to ask.",
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
    "issue.sound.noPageSize": "This section has no page size, so it is laid out on a sheet of nothing: every column too narrow to divide, one item to a page and the rest thrown away. Give it a page size - 210 by 297 is A4.",
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

    "alert.noPdf": "PDF unavailable: ",
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
    "issue.can.drawingInFlow": "This picture is a drawing, and a mail reader does not draw SVG. Its alternative text will be written in its place; a bitmap of it, if the reader should see it, is the host's to provide.",
    "issue.sound.unreadablePicture": "This picture cannot be drawn. {reason}",
    "canvas.refusedPicture": "picture refused",
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

    "family.display": "Affichage", "family.input": "Saisie", "family.library": "Bibliothèque",
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
    "hint.subTemplate": "Un autre gabarit, de trois façons. Un chemin est résolu à côté du fichier qui le nomme, ce que seuls le CLI et la bibliothèque native savent faire - cette page n'a pas de fichier propre, donc un chemin s'affiche ici en cadre vide. st:// nomme ce que le programme appelant va chercher, et s'affiche ici quand ce programme ouvre sa bibliothèque à la page. L'embarquer met l'autre gabarit dans celui-ci, ce qui voyage partout.",
    "field.libraryPiece": "Depuis la bibliothèque",
    "hint.pieceReady": "Dessiné depuis la bibliothèque : {title}. Il se modifie là-bas, pas ici - le fichier ne garde que la référence, donc changer la pièce change chaque document qui la nomme.",
    "hint.pieceLoading": "Demande de {reference} à la bibliothèque…",
    "hint.pieceMissing": "La bibliothèque ne connaît pas {reference}, donc rien n'est dessiné ici. Ce qui compose le document devra le connaître.",
    "hint.pieceNoLibrary": "Cette page n'a pas de bibliothèque à interroger, donc {reference} s'affiche en cadre vide. Le programme qui compose le document va le chercher.",
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

    "alert.noPdf": "PDF indisponible : ",
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
    "section.repeat": "Répétition",
    "field.repeatOver": "Sur la liste", "field.emptyText": "Si elle est vide",
    "hint.repeatOver": "Une liste des données, par exemple Lignes : ce que contient l'élément est dessiné une fois par entrée - un tableau garde ses lignes d'en-tête et répète les autres, deux d'entre elles en alternance. Un jeton lit d'abord l'entrée, puis l'ensemble des données. Si la liste est vide, l'élément disparaît, en-tête compris, sauf s'il dit quoi montrer à la place. Cette page le dessine une fois ; l'aperçu exact dessine chaque entrée.",
    "editor.addField": "Insérer un champ de fusion",
    "editor.fromData": "Image issue des données : {path}",
    "fieldType.text": "Texte", "fieldType.number": "Nombre", "fieldType.decimal": "Décimal",
    "fieldType.currency": "Montant", "fieldType.date": "Date", "fieldType.datetime": "Date et heure",
    "fieldType.image": "Image", "fieldType.list": "Listes",
    "fields.eachLine": "Dans chaque ligne de {list}",
    "editor.repeats": "\u21BB Répété pour chaque élément de {list}",
    "live.off": "Aperçu exact : non", "live.canvas": "Aperçu exact : sur le canevas",
    "live.beside": "Aperçu exact : à côté", "live.window": "Aperçu exact : dans une fenêtre",
    "live.asking": "Le moteur dessine…",
    "live.ready": "Les pages du moteur, à jour ({count})",
    "live.failed": "Le moteur n'a pas répondu : {message}",
    "live.windowClosed": "La fenêtre est fermée - Aperçu fenêtré l'ouvre",
    "live.page": "page {page} / {pages} - moteur",
    "live.noServer": "Cette page n'a reçu aucun moteur à interroger.",
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
    "issue.sound.noPageSize": "Cette section n'a pas de format de page : elle est mise en page sur une feuille de rien, chaque colonne trop étroite pour être divisée, un élément par page et le reste perdu. Lui donner un format - 210 par 297 pour l'A4.",
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
    "issue.can.drawingInFlow": "Cette image est un dessin, et un lecteur de courrier ne dessine pas le SVG. Son texte de remplacement sera écrit à sa place ; une version bitmap, si le lecteur doit la voir, revient à l'hôte.",
    "issue.sound.unreadablePicture": "Cette image ne peut pas être dessinée. {reason}",
    "canvas.refusedPicture": "image refusée",
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

  const reading = readingOf(b.Source.Data);
  if (reading) {
    const r = rect(area.x, area.y, area.x + area.w, area.y + area.h);
    return reading.drawing ? drawingSvg(reading.drawing, r, fit, opacity, (report.DefaultFont || {}).Family) : refusedPicture(r);
  }

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
function render(report, selected, index = 0, show = {}, lookup = null) {
  drawingClips = 0;
  drawingFamily = (report.DefaultFont || {}).Family;
  const { placed, page, band, section } = layout(report, index, lookup);
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

  // What repeats over a list is drawn once here, as its prototype: the engine stamps it out
  // per entry, and the exact preview shows the run. Said on the page, at the corner of what
  // repeats - above it when there is room - so the one row on screen does not read as the
  // only one there will be.
  for (const { item, frame } of placed) {
    const list = (item.RepeatOver || "").trim();
    if (!list || !Number.isFinite(frame.left) || !Number.isFinite(frame.top)) continue;
    const words = "\u21BB " + list;
    const size = 2.4;
    const width = words.length * size * 0.55 + 1.6;
    const top = frame.top >= size + 1.4 ? frame.top - size - 1.4 : frame.top + 0.4;
    out.push(`<g class="repeat-mark"><rect x="${n3(frame.right - width)}" y="${n3(top)}"`
           + ` width="${n3(width)}" height="${n3(size + 1)}" rx=".6"`
           + ` fill="${ON_PAPER.placeholderFill}" stroke="${ON_PAPER.placeholderLine}" stroke-width=".15"/>`
           + `<text x="${n3(frame.right - width + 0.8)}" y="${n3(top + size)}" font-size="${size}"`
           + ` font-family="sans-serif" fill="${ON_PAPER.placeholderInk}">${esc(words)}</text></g>`);
  }

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

  // A drawing is read here and drawn from the reading, in the page's own typefaces: handed to
  // the browser in an <image> it would use neither, and would draw what the PDF refuses.
  const reading = readingOf(pictureData(item));
  if (reading)
    return reading.drawing
      ? drawingSvg(reading.drawing, r, item.Scaling === "Stretch" ? "none" : fit, 1, drawingFamily)
      : refusedPicture(r);

  const bytes = resolvedImageSource(item);
  if (bytes)
    return `<image x="${n3(r.left)}" y="${n3(r.top)}" width="${n3(r.width)}" height="${n3(r.height)}" preserveAspectRatio="${fit}" href="data:${bytes.mediaType};base64,${bytes.data}"/>`;

  if (item.Source && item.Source.Data)
    return `<image x="${n3(r.left)}" y="${n3(r.top)}" width="${n3(r.width)}" height="${n3(r.height)}" preserveAspectRatio="${fit}" href="data:${item.Source.MediaType};base64,${item.Source.Data}"/>`;

  return `<rect x="${n3(r.left)}" y="${n3(r.top)}" width="${n3(r.width)}" height="${n3(r.height)}" fill="${ON_PAPER.placeholderFill}" stroke="${ON_PAPER.placeholderLine}" stroke-width=".2"/>` +
         `<text x="${n3((r.left + r.right) / 2)}" y="${n3((r.top + r.bottom) / 2)}" text-anchor="middle" font-size="3" fill="${ON_PAPER.placeholderInk}">image</text>`;
}

// ---------------------------------------------------------------------------
// Pictures and drawings - the echo of engine/Vector and of Pictures.
//
// A browser would draw any SVG it is handed, and that is exactly why this page cannot simply
// hand it one: it would show a chart the PDF refuses, in a typeface the PDF does not use. So
// the bytes are read here by the same rules, to the same list of shapes, and drawn from that.
// check-against-engine.js holds this reader to the engine's on every sample test 100 writes,
// to the rule and to the point.
//
// The words of a refusal are this page's own; what has to agree is that it is refused.
// ---------------------------------------------------------------------------

const SVG_LARGEST = 1e9;
const SVG_MAX_DEPTH = 64;
const SVG_MOST_ISSUES = 20;

const SVG_NOT_DRAWN = ["title", "desc", "metadata"];
const SVG_SHAPES = ["path", "rect", "circle", "ellipse", "line", "polyline", "polygon"];
const SVG_PROPERTIES = [
  "color", "fill", "fill-opacity", "fill-rule", "stroke", "stroke-opacity", "stroke-width",
  "stroke-dasharray", "stroke-dashoffset", "stroke-linecap", "stroke-linejoin", "stroke-miterlimit",
  "opacity", "clip-path", "clip-rule", "font-family", "font-size", "font-weight", "font-style",
  "text-anchor", "display", "visibility"
];
const SVG_IGNORED = [
  "id", "class", "lang", "tabindex", "role", "focusable", "xmlns", "shape-rendering",
  "text-rendering", "color-rendering", "image-rendering", "color-interpolation",
  "pointer-events", "cursor", "font-kerning", "font-variant-ligatures"
];
const SVG_OWN = {
  svg: ["viewBox", "width", "height", "x", "y", "preserveAspectRatio", "version", "baseProfile"],
  g: [], defs: [],
  path: ["d"],
  rect: ["x", "y", "width", "height", "rx", "ry"],
  circle: ["cx", "cy", "r"],
  ellipse: ["cx", "cy", "rx", "ry"],
  line: ["x1", "y1", "x2", "y2"],
  polyline: ["points"], polygon: ["points"],
  text: ["x", "y", "dx", "dy"], tspan: ["x", "y", "dx", "dy"],
  clipPath: ["clipPathUnits"]
};
const SVG_GENERIC_FAMILIES = ["serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui",
                              "ui-sans-serif", "ui-serif", "ui-monospace", "ui-rounded"];

// The names as numbers: this file carries no colour of a chart's choosing, only the means of
// reading one. The palette audit is about what the chrome wears.
const SVG_COLOUR_NAMES = {
  aliceblue: 0xF0F8FF, antiquewhite: 0xFAEBD7, aqua: 0x00FFFF, aquamarine: 0x7FFFD4, azure: 0xF0FFFF,
  beige: 0xF5F5DC, bisque: 0xFFE4C4, black: 0x000000, blanchedalmond: 0xFFEBCD, blue: 0x0000FF,
  blueviolet: 0x8A2BE2, brown: 0xA52A2A, burlywood: 0xDEB887, cadetblue: 0x5F9EA0, chartreuse: 0x7FFF00,
  chocolate: 0xD2691E, coral: 0xFF7F50, cornflowerblue: 0x6495ED, cornsilk: 0xFFF8DC, crimson: 0xDC143C,
  cyan: 0x00FFFF, darkblue: 0x00008B, darkcyan: 0x008B8B, darkgoldenrod: 0xB8860B, darkgray: 0xA9A9A9,
  darkgreen: 0x006400, darkgrey: 0xA9A9A9, darkkhaki: 0xBDB76B, darkmagenta: 0x8B008B, darkolivegreen: 0x556B2F,
  darkorange: 0xFF8C00, darkorchid: 0x9932CC, darkred: 0x8B0000, darksalmon: 0xE9967A, darkseagreen: 0x8FBC8F,
  darkslateblue: 0x483D8B, darkslategray: 0x2F4F4F, darkslategrey: 0x2F4F4F, darkturquoise: 0x00CED1, darkviolet: 0x9400D3,
  deeppink: 0xFF1493, deepskyblue: 0x00BFFF, dimgray: 0x696969, dimgrey: 0x696969, dodgerblue: 0x1E90FF,
  firebrick: 0xB22222, floralwhite: 0xFFFAF0, forestgreen: 0x228B22, fuchsia: 0xFF00FF, gainsboro: 0xDCDCDC,
  ghostwhite: 0xF8F8FF, gold: 0xFFD700, goldenrod: 0xDAA520, gray: 0x808080, green: 0x008000,
  greenyellow: 0xADFF2F, grey: 0x808080, honeydew: 0xF0FFF0, hotpink: 0xFF69B4, indianred: 0xCD5C5C,
  indigo: 0x4B0082, ivory: 0xFFFFF0, khaki: 0xF0E68C, lavender: 0xE6E6FA, lavenderblush: 0xFFF0F5,
  lawngreen: 0x7CFC00, lemonchiffon: 0xFFFACD, lightblue: 0xADD8E6, lightcoral: 0xF08080, lightcyan: 0xE0FFFF,
  lightgoldenrodyellow: 0xFAFAD2, lightgray: 0xD3D3D3, lightgreen: 0x90EE90, lightgrey: 0xD3D3D3, lightpink: 0xFFB6C1,
  lightsalmon: 0xFFA07A, lightseagreen: 0x20B2AA, lightskyblue: 0x87CEFA, lightslategray: 0x778899, lightslategrey: 0x778899,
  lightsteelblue: 0xB0C4DE, lightyellow: 0xFFFFE0, lime: 0x00FF00, limegreen: 0x32CD32, linen: 0xFAF0E6,
  magenta: 0xFF00FF, maroon: 0x800000, mediumaquamarine: 0x66CDAA, mediumblue: 0x0000CD, mediumorchid: 0xBA55D3,
  mediumpurple: 0x9370DB, mediumseagreen: 0x3CB371, mediumslateblue: 0x7B68EE, mediumspringgreen: 0x00FA9A, mediumturquoise: 0x48D1CC,
  mediumvioletred: 0xC71585, midnightblue: 0x191970, mintcream: 0xF5FFFA, mistyrose: 0xFFE4E1, moccasin: 0xFFE4B5,
  navajowhite: 0xFFDEAD, navy: 0x000080, oldlace: 0xFDF5E6, olive: 0x808000, olivedrab: 0x6B8E23,
  orange: 0xFFA500, orangered: 0xFF4500, orchid: 0xDA70D6, palegoldenrod: 0xEEE8AA, palegreen: 0x98FB98,
  paleturquoise: 0xAFEEEE, palevioletred: 0xDB7093, papayawhip: 0xFFEFD5, peachpuff: 0xFFDAB9, peru: 0xCD853F,
  pink: 0xFFC0CB, plum: 0xDDA0DD, powderblue: 0xB0E0E6, purple: 0x800080, rebeccapurple: 0x663399,
  red: 0xFF0000, rosybrown: 0xBC8F8F, royalblue: 0x4169E1, saddlebrown: 0x8B4513, salmon: 0xFA8072,
  sandybrown: 0xF4A460, seagreen: 0x2E8B57, seashell: 0xFFF5EE, sienna: 0xA0522D, silver: 0xC0C0C0,
  skyblue: 0x87CEEB, slateblue: 0x6A5ACD, slategray: 0x708090, slategrey: 0x708090, snow: 0xFFFAFA,
  springgreen: 0x00FF7F, steelblue: 0x4682B4, tan: 0xD2B48C, teal: 0x008080, thistle: 0xD8BFD8,
  tomato: 0xFF6347, turquoise: 0x40E0D0, violet: 0xEE82EE, wheat: 0xF5DEB3, white: 0xFFFFFF,
  whitesmoke: 0xF5F5F5, yellow: 0xFFFF00, yellowgreen: 0x9ACD32
};

// ---- bytes -----------------------------------------------------------------

/// A template carries a picture as base64, and this file has no atob: the core runs where
/// there is no browser (section 5c). Null when it is not base64, which reads as "not a picture".
function bytesOfBase64(text) {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
  const clean = String(text).replace(/[\s=]+/g, "");
  const out = new Uint8Array(Math.floor(clean.length * 3 / 4));
  let bits = 0, held = 0, at = 0;

  for (let i = 0; i < clean.length; i++) {
    const value = alphabet.indexOf(clean[i]);
    if (value < 0) return null;
    bits = (bits << 6) | value;
    held += 6;
    if (held >= 8) {
      held -= 8;
      out[at++] = (bits >> held) & 255;
    }
  }

  return out.subarray(0, at);
}

/// Strict UTF-8, as the engine reads it. Null on anything malformed: no replacement characters
/// quietly standing in for bytes that were never text.
function textOfUtf8(bytes) {
  const units = [];
  let out = "";

  for (let i = 0; i < bytes.length;) {
    const lead = bytes[i++];
    let code, more;
    if (lead < 0x80) { code = lead; more = 0; }
    else if (lead >= 0xC2 && lead <= 0xDF) { code = lead & 0x1F; more = 1; }
    else if (lead >= 0xE0 && lead <= 0xEF) { code = lead & 0x0F; more = 2; }
    else if (lead >= 0xF0 && lead <= 0xF4) { code = lead & 0x07; more = 3; }
    else return null;

    for (let k = 0; k < more; k++) {
      const next = bytes[i++];
      if (next === undefined || (next & 0xC0) !== 0x80) return null;
      code = (code << 6) | (next & 0x3F);
    }

    if ((more === 2 && code < 0x800) || (more === 3 && code < 0x10000)
        || (code >= 0xD800 && code <= 0xDFFF) || code > 0x10FFFF) return null;

    units.push(code);
    if (units.length >= 4096) { out += String.fromCodePoint(...units); units.length = 0; }
  }

  return out + String.fromCodePoint(...units);
}

/// What the bytes open with, as the engine asks it: a tag, after a mark and white space.
function looksLikeDrawing(bytes) {
  let at = bytes.length >= 3 && bytes[0] === 0xEF && bytes[1] === 0xBB && bytes[2] === 0xBF ? 3 : 0;
  while (at < bytes.length && (bytes[at] === 32 || bytes[at] === 9 || bytes[at] === 10 || bytes[at] === 13)) at++;
  return at < bytes.length && bytes[at] === 60;
}

// ---- markup ----------------------------------------------------------------

function svgFail(text, at, what) {
  let line = 1;
  for (let i = 0; i < at && i < text.length; i++) if (text[i] === "\n") line++;
  throw { svgMalformed: `Line ${line}: ${what}.` };
}

function svgDecode(run, text, at) {
  let amp = run.indexOf("&");
  if (amp < 0) return run;

  let decoded = "", from = 0;
  while (amp >= 0) {
    decoded += run.slice(from, amp);
    const semi = run.indexOf(";", amp);
    if (semi < 0) svgFail(text, at, "an & that starts no entity");

    const name = run.slice(amp + 1, semi);
    const known = { amp: "&", lt: "<", gt: ">", quot: '"', apos: "'" }[name];
    if (known !== undefined) {
      decoded += known;
    } else {
      const hexa = name.startsWith("#x");
      const digits = hexa ? name.slice(2) : name.startsWith("#") ? name.slice(1) : "";
      const code = (hexa ? /^[0-9a-fA-F]+$/ : /^[0-9]+$/).test(digits) ? parseInt(digits, hexa ? 16 : 10) : 0;
      if (!(code > 0) || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF) || code === 0xFFFE || code === 0xFFFF
          || (code < 0x20 && code !== 9 && code !== 10 && code !== 13))
        svgFail(text, at, `the entity &${name}; which SVG does not know`);
      decoded += String.fromCodePoint(code);
    }

    from = semi + 1;
    amp = run.indexOf("&", from);
  }

  return decoded + run.slice(from);
}

/// The markup into elements, by the rules of engine/Vector/SvgMarkup.cs: nothing is ever
/// fetched, an internal subset is refused, and so no entity exists but the five XML knows.
function svgParse(text) {
  for (let i = 0; i < text.length; i++) {
    const c = text.charCodeAt(i);
    if ((c < 32 && c !== 9 && c !== 10 && c !== 13) || c === 0xFFFE || c === 0xFFFF)
      svgFail(text, i, "a character XML does not allow");
  }

  const isSpace = c => c === " " || c === "\t" || c === "\n" || c === "\r";
  const nameChar = /[\p{L}\p{Nd}_\-.:]/u;
  let at = text.charCodeAt(0) === 0xFEFF ? 1 : 0;
  const open = [];
  let root = null;

  const after = (from, closer, fault) => {
    const found = text.indexOf(closer, from);
    if (found < 0) svgFail(text, from, fault);
    return found + closer.length;
  };
  const skipSpace = () => { while (at < text.length && isSpace(text[at])) at++; };
  const readName = () => {
    const start = at;
    while (at < text.length && nameChar.test(text[at])) at++;
    if (at === start) svgFail(text, at, "a name was expected");
    return text.slice(start, at);
  };

  while (at < text.length) {
    if (text[at] !== "<") {
      let stop = text.indexOf("<", at);
      if (stop < 0) stop = text.length;
      const run = text.slice(at, stop);
      if (open.length) open[open.length - 1].children.push(svgDecode(run, text, at));
      else if (run.trim()) svgFail(text, at, "text outside the root element");
      at = stop;
      continue;
    }

    if (text.startsWith("<!--", at)) { at = after(at + 4, "-->", "a comment never closed"); continue; }
    if (text.startsWith("<?", at)) { at = after(at + 2, "?>", "a processing instruction never closed"); continue; }

    if (text.startsWith("<![CDATA[", at)) {
      if (!open.length) svgFail(text, at, "CDATA outside the root element");
      const end = after(at + 9, "]]>", "a CDATA section never closed");
      open[open.length - 1].children.push(text.slice(at + 9, end - 3));
      at = end;
      continue;
    }

    if (text.startsWith("<!DOCTYPE", at)) {
      if (root) svgFail(text, at, "a DOCTYPE after the root element");
      let quote = "", i = at + 9, done = false;
      for (; i < text.length; i++) {
        const c = text[i];
        if (quote) { if (c === quote) quote = ""; }
        else if (c === '"' || c === "'") quote = c;
        else if (c === "[") svgFail(text, i, "a DOCTYPE with an internal subset, which is where entities are declared");
        else if (c === ">") { done = true; break; }
      }
      if (!done) svgFail(text, at, "a DOCTYPE never closed");
      at = i + 1;
      continue;
    }

    if (text.startsWith("<!", at)) svgFail(text, at, "a declaration SVG does not need");

    if (text.startsWith("</", at)) {
      at += 2;
      const closing = readName();
      skipSpace();
      if (at >= text.length || text[at] !== ">") svgFail(text, at, "an end tag not closed");
      if (!open.length || open[open.length - 1].name !== closing) svgFail(text, at, `</${closing}> with no matching start tag`);
      open.pop();
      at++;
      continue;
    }

    at++;
    const element = { name: readName(), attributes: [], children: [] };
    let shut = false;

    for (;;) {
      const before = at;
      skipSpace();
      if (at >= text.length) svgFail(text, at, `<${element.name}> is never finished`);
      if (text[at] === ">") { at++; break; }
      if (text[at] === "/") {
        if (text[at + 1] !== ">") svgFail(text, at, `<${element.name}> is badly closed`);
        at += 2;
        shut = true;
        break;
      }
      if (at === before) svgFail(text, at, `<${element.name}> needs a space before its next attribute`);

      const name = readName();
      skipSpace();
      if (text[at] !== "=") svgFail(text, at, `the attribute ${name} has no value`);
      at++;
      skipSpace();
      if (text[at] !== '"' && text[at] !== "'") svgFail(text, at, `the value of ${name} is not in quotes`);
      const quote = text[at++];
      const stop = text.indexOf(quote, at);
      if (stop < 0) svgFail(text, at, `the value of ${name} is never closed`);
      const raw = text.slice(at, stop);
      if (raw.includes("<")) svgFail(text, at, `a < inside the value of ${name}`);
      if (element.attributes.some(pair => pair[0] === name)) svgFail(text, at, `the attribute ${name} given twice`);
      element.attributes.push([name, svgDecode(raw, text, at)]);
      at = stop + 1;
    }

    if (open.length >= SVG_MAX_DEPTH) svgFail(text, at, `elements nested deeper than ${SVG_MAX_DEPTH}`);

    if (open.length) open[open.length - 1].children.push(element);
    else if (!root) root = element;
    else svgFail(text, at, "a second root element");

    if (!shut) open.push(element);
  }

  if (open.length) throw { svgMalformed: `<${open[open.length - 1].name}> is never closed.` };
  if (!root) throw { svgMalformed: "There is no element in this file." };
  return root;
}

const svgAttr = (element, name) => {
  const pair = element.attributes.find(p => p[0] === name);
  return pair ? pair[1] : null;
};

// ---- values ----------------------------------------------------------------

const SVG_SEPARATORS = " \t\n\r,";

function svgSkip(text, cursor) {
  while (cursor.at < text.length && SVG_SEPARATORS.includes(text[cursor.at])) cursor.at++;
}

/// One number off the front, stopping where it stops: "1.5.5-2" is three.
function svgReadNumber(text, cursor) {
  const digit = c => c >= "0" && c <= "9";
  let i = cursor.at;
  if (text[i] === "+" || text[i] === "-") i++;

  let digits = 0;
  while (i < text.length && digit(text[i])) { i++; digits++; }
  if (text[i] === ".") {
    i++;
    while (i < text.length && digit(text[i])) { i++; digits++; }
  }
  if (!digits) return null;

  if (text[i] === "e" || text[i] === "E") {
    let j = i + 1;
    if (text[j] === "+" || text[j] === "-") j++;
    if (j < text.length && digit(text[j])) {
      while (j < text.length && digit(text[j])) j++;
      i = j;
    }
  }

  const value = Number(text.slice(cursor.at, i));
  if (!Number.isFinite(value) || Math.abs(value) > SVG_LARGEST) return null;
  cursor.at = i;
  return value;
}

function svgNumber(value) {
  if (value === null || value === undefined) return null;
  const trimmed = value.trim(), cursor = { at: 0 };
  const read = svgReadNumber(trimmed, cursor);
  return read !== null && cursor.at === trimmed.length ? read : null;
}

function svgLength(value) {
  if (value === null || value === undefined) return null;
  const trimmed = value.trim(), cursor = { at: 0 };
  const read = svgReadNumber(trimmed, cursor);
  if (read === null) return null;
  const scale = { "": 1, px: 1, pt: 96 / 72, pc: 16, mm: 96 / 25.4, cm: 96 / 2.54, in: 96 }[trimmed.slice(cursor.at)];
  return scale === undefined ? null : read * scale;
}

function svgLengthOrEm(value, fontSize) {
  if (value === null || value === undefined) return null;
  const trimmed = value.trim();
  if (trimmed.endsWith("em")) {
    const ems = svgNumber(trimmed.slice(0, -2));
    return ems === null ? null : ems * fontSize;
  }
  return svgLength(trimmed);
}

function svgOpacity(value) {
  const trimmed = value.trim(), percent = trimmed.endsWith("%");
  const read = svgNumber(percent ? trimmed.slice(0, -1) : trimmed);
  return read === null ? null : Math.min(1, Math.max(0, percent ? read / 100 : read));
}

function svgNumbers(value) {
  const numbers = [], cursor = { at: 0 };
  svgSkip(value, cursor);
  while (cursor.at < value.length) {
    const read = svgReadNumber(value, cursor);
    if (read === null) return null;
    numbers.push(read);
    svgSkip(value, cursor);
  }
  return numbers;
}

/// Rounds a half to the even neighbour, as Math.Round does in the engine: 76.5 is a channel of
/// 76 there, and Math.round here would make it 77.
function svgRound(value) {
  const down = Math.floor(value), rest = value - down;
  if (rest < 0.5) return down;
  if (rest > 0.5) return down + 1;
  return down % 2 === 0 ? down : down + 1;
}

function svgColour(value) {
  const text = value.trim().toLowerCase();

  if (text.startsWith("#")) {
    const h = text.slice(1);
    if (!/^[0-9a-f]*$/.test(h)) return null;
    const short = i => parseInt(h[i], 16) * 17;
    const long = i => parseInt(h.slice(i, i + 2), 16);
    if (h.length === 3) return { r: short(0), g: short(1), b: short(2), a: 255 };
    if (h.length === 4) return { r: short(0), g: short(1), b: short(2), a: short(3) };
    if (h.length === 6) return { r: long(0), g: long(2), b: long(4), a: 255 };
    if (h.length === 8) return { r: long(0), g: long(2), b: long(4), a: long(6) };
    return null;
  }

  if (text.startsWith("rgb(") || text.startsWith("rgba(")) {
    if (!text.endsWith(")")) return null;
    const parts = text.slice(text.indexOf("(") + 1, -1).split(/[ ,\/\t\n\r]+/).filter(Boolean);
    if (parts.length !== 3 && parts.length !== 4) return null;

    const channels = [];
    for (let i = 0; i < 3; i++) {
      const percent = parts[i].endsWith("%");
      const read = svgNumber(percent ? parts[i].slice(0, -1) : parts[i]);
      if (read === null) return null;
      channels.push(svgRound(Math.min(255, Math.max(0, percent ? read * 2.55 : read))));
    }

    const alpha = parts.length === 4 ? svgOpacity(parts[3]) : 1;
    if (alpha === null) return null;
    return { r: channels[0], g: channels[1], b: channels[2], a: svgRound(alpha * 255) };
  }

  const packed = Object.prototype.hasOwnProperty.call(SVG_COLOUR_NAMES, text) ? SVG_COLOUR_NAMES[text] : null;
  return packed === null ? null : { r: (packed >> 16) & 255, g: (packed >> 8) & 255, b: packed & 255, a: 255 };
}

const svgTimes = (m, n) => [
  m[0] * n[0] + m[2] * n[1], m[1] * n[0] + m[3] * n[1],
  m[0] * n[2] + m[2] * n[3], m[1] * n[2] + m[3] * n[3],
  m[0] * n[4] + m[2] * n[5] + m[4], m[1] * n[4] + m[3] * n[5] + m[5]
];
const SVG_IDENTITY = [1, 0, 0, 1, 0, 0];

function svgTransform(value) {
  let matrix = SVG_IDENTITY;
  const cursor = { at: 0 };
  svgSkip(value, cursor);

  while (cursor.at < value.length) {
    const open = value.indexOf("(", cursor.at);
    const shut = open < 0 ? -1 : value.indexOf(")", open);
    if (shut < 0) return null;
    const a = svgNumbers(value.slice(open + 1, shut));
    if (!a) return null;

    const name = value.slice(cursor.at, open).trim();
    const radians = (a[0] || 0) * Math.PI / 180;
    let step = null;

    if (name === "matrix" && a.length === 6) step = a;
    else if (name === "translate" && (a.length === 1 || a.length === 2)) step = [1, 0, 0, 1, a[0], a.length === 2 ? a[1] : 0];
    else if (name === "scale" && (a.length === 1 || a.length === 2)) step = [a[0], 0, 0, a.length === 2 ? a[1] : a[0], 0, 0];
    else if (name === "rotate" && (a.length === 1 || a.length === 3)) {
      const turn = [Math.cos(radians), Math.sin(radians), -Math.sin(radians), Math.cos(radians), 0, 0];
      step = a.length === 1 ? turn : svgTimes(svgTimes([1, 0, 0, 1, a[1], a[2]], turn), [1, 0, 0, 1, -a[1], -a[2]]);
    }
    else if (name === "skewX" && a.length === 1) step = [1, 0, Math.tan(radians), 1, 0, 0];
    else if (name === "skewY" && a.length === 1) step = [1, Math.tan(radians), 0, 1, 0, 0];

    if (!step) return null;
    matrix = svgTimes(matrix, step);
    cursor.at = shut + 1;
    svgSkip(value, cursor);
  }

  return matrix;
}

// A path is [verb, numbers...] pieces, verbs M L C Z in absolute coordinates.
const svgPath = () => ({ verbs: [], points: [] });
const svgMove = (p, x, y) => { p.verbs.push("M"); p.points.push(x, y); };
const svgLine = (p, x, y) => { p.verbs.push("L"); p.points.push(x, y); };
const svgCubic = (p, a, b, c, d, e, f) => { p.verbs.push("C"); p.points.push(a, b, c, d, e, f); };
const svgClose = p => p.verbs.push("Z");

function svgTransformed(path, m) {
  const moved = { verbs: path.verbs.slice(), points: [] };
  for (let i = 0; i + 1 < path.points.length; i += 2) {
    moved.points.push(m[0] * path.points[i] + m[2] * path.points[i + 1] + m[4],
                      m[1] * path.points[i] + m[3] * path.points[i + 1] + m[5]);
  }
  return moved;
}

function svgArc(path, x1, y1, rx, ry, turn, large, sweep, x2, y2) {
  if (x1 === x2 && y1 === y2) return;
  rx = Math.abs(rx);
  ry = Math.abs(ry);
  if (rx === 0 || ry === 0) { svgLine(path, x2, y2); return; }

  const phi = turn * Math.PI / 180, cos = Math.cos(phi), sin = Math.sin(phi);
  const dx = (x1 - x2) / 2, dy = (y1 - y2) / 2;
  const xp = cos * dx + sin * dy, yp = -sin * dx + cos * dy;

  const lambda = xp * xp / (rx * rx) + yp * yp / (ry * ry);
  if (lambda > 1) { rx *= Math.sqrt(lambda); ry *= Math.sqrt(lambda); }

  const top = rx * rx * ry * ry - rx * rx * yp * yp - ry * ry * xp * xp;
  const bottom = rx * rx * yp * yp + ry * ry * xp * xp;
  const factor = (large === sweep ? -1 : 1) * Math.sqrt(Math.max(0, top / bottom));
  const cxp = factor * rx * yp / ry, cyp = -factor * ry * xp / rx;
  const cx = cos * cxp - sin * cyp + (x1 + x2) / 2, cy = sin * cxp + cos * cyp + (y1 + y2) / 2;

  const from = Math.atan2((yp - cyp) / ry, (xp - cxp) / rx);
  const to = Math.atan2((-yp - cyp) / ry, (-xp - cxp) / rx);
  let span = to - from;
  if (!sweep && span > 0) span -= 2 * Math.PI;
  else if (sweep && span < 0) span += 2 * Math.PI;

  const pieces = Math.max(1, Math.ceil(Math.abs(span) / (Math.PI / 2) - 1e-9));
  const step = span / pieces, k = 4 / 3 * Math.tan(step / 4);
  const out = (px, py) => [cx + rx * cos * px - ry * sin * py, cy + rx * sin * px + ry * cos * py];

  for (let i = 0; i < pieces; i++) {
    const a = from + i * step, b = a + step;
    const [c1x, c1y] = out(Math.cos(a) - k * Math.sin(a), Math.sin(a) + k * Math.cos(a));
    const [c2x, c2y] = out(Math.cos(b) + k * Math.sin(b), Math.sin(b) - k * Math.cos(b));
    const [ex, ey] = i === pieces - 1 ? [x2, y2] : out(Math.cos(b), Math.sin(b));
    svgCubic(path, c1x, c1y, c2x, c2y, ex, ey);
  }
}

/// Path data down to moves, lines, cubics and closes; null at the first thing wrong.
function svgPathData(d) {
  const path = svgPath(), cursor = { at: 0 };
  let x = 0, y = 0, startX = 0, startY = 0, lastX = 0, lastY = 0;
  let command = "", previous = "";

  const read = () => { svgSkip(d, cursor); return svgReadNumber(d, cursor); };
  const readMany = n => {
    const out = [];
    for (let i = 0; i < n; i++) {
      const value = read();
      if (value === null) return null;
      out.push(value);
    }
    return out;
  };
  const flag = () => {
    svgSkip(d, cursor);
    const c = d[cursor.at];
    if (c !== "0" && c !== "1") return null;
    cursor.at++;
    return c === "1";
  };
  const quadratic = (x0, y0, qx, qy, ex, ey) =>
    svgCubic(path, x0 + 2 / 3 * (qx - x0), y0 + 2 / 3 * (qy - y0), ex + 2 / 3 * (qx - ex), ey + 2 / 3 * (qy - ey), ex, ey);

  svgSkip(d, cursor);
  while (cursor.at < d.length) {
    if (/[A-Za-z]/.test(d[cursor.at])) command = d[cursor.at++];
    else if (!command || command === "Z" || command === "z") return null;

    if (!path.verbs.length && command !== "M" && command !== "m") return null;

    const relative = command === command.toLowerCase();
    const ox = relative ? x : 0, oy = relative ? y : 0;
    const upper = command.toUpperCase();
    let v;

    switch (upper) {
      case "M":
        if (!(v = readMany(2))) return null;
        x = startX = ox + v[0];
        y = startY = oy + v[1];
        svgMove(path, x, y);
        command = relative ? "l" : "L";
        previous = "M";
        svgSkip(d, cursor);
        continue;
      case "L":
        if (!(v = readMany(2))) return null;
        x = ox + v[0]; y = oy + v[1];
        svgLine(path, x, y);
        break;
      case "H":
        if (!(v = readMany(1))) return null;
        x = ox + v[0];
        svgLine(path, x, y);
        break;
      case "V":
        if (!(v = readMany(1))) return null;
        y = oy + v[0];
        svgLine(path, x, y);
        break;
      case "C":
        if (!(v = readMany(6))) return null;
        lastX = ox + v[2]; lastY = oy + v[3];
        svgCubic(path, ox + v[0], oy + v[1], lastX, lastY, ox + v[4], oy + v[5]);
        x = ox + v[4]; y = oy + v[5];
        break;
      case "S": {
        if (!(v = readMany(4))) return null;
        const after = previous === "C" || previous === "S";
        const x1 = after ? 2 * x - lastX : x, y1 = after ? 2 * y - lastY : y;
        lastX = ox + v[0]; lastY = oy + v[1];
        svgCubic(path, x1, y1, lastX, lastY, ox + v[2], oy + v[3]);
        x = ox + v[2]; y = oy + v[3];
        break;
      }
      case "Q":
        if (!(v = readMany(4))) return null;
        lastX = ox + v[0]; lastY = oy + v[1];
        quadratic(x, y, lastX, lastY, ox + v[2], oy + v[3]);
        x = ox + v[2]; y = oy + v[3];
        break;
      case "T": {
        if (!(v = readMany(2))) return null;
        const after = previous === "Q" || previous === "T";
        lastX = after ? 2 * x - lastX : x;
        lastY = after ? 2 * y - lastY : y;
        quadratic(x, y, lastX, lastY, ox + v[0], oy + v[1]);
        x = ox + v[0]; y = oy + v[1];
        break;
      }
      case "A": {
        if (!(v = readMany(3))) return null;
        const large = flag(), sweep = flag();
        if (large === null || sweep === null) return null;
        const end = readMany(2);
        if (!end) return null;
        svgArc(path, x, y, v[0], v[1], v[2], large, sweep, ox + end[0], oy + end[1]);
        x = ox + end[0]; y = oy + end[1];
        break;
      }
      case "Z":
        svgClose(path);
        x = startX; y = startY;
        break;
      default:
        return null;
    }

    previous = upper;
    svgSkip(d, cursor);
  }

  return path;
}

// ---- the reader ------------------------------------------------------------

const SVG_KAPPA = 0.5522847498307936;

function svgRectangle(path, x, y, w, h, rx, ry) {
  if (rx <= 0 || ry <= 0) {
    svgMove(path, x, y);
    svgLine(path, x + w, y);
    svgLine(path, x + w, y + h);
    svgLine(path, x, y + h);
    svgClose(path);
    return;
  }

  const kx = rx * SVG_KAPPA, ky = ry * SVG_KAPPA, right = x + w, bottom = y + h;
  svgMove(path, x + rx, y);
  svgLine(path, right - rx, y);
  svgCubic(path, right - rx + kx, y, right, y + ry - ky, right, y + ry);
  svgLine(path, right, bottom - ry);
  svgCubic(path, right, bottom - ry + ky, right - rx + kx, bottom, right - rx, bottom);
  svgLine(path, x + rx, bottom);
  svgCubic(path, x + rx - kx, bottom, x, bottom - ry + ky, x, bottom - ry);
  svgLine(path, x, y + ry);
  svgCubic(path, x, y + ry - ky, x + rx - kx, y, x + rx, y);
  svgClose(path);
}

function svgEllipse(path, cx, cy, rx, ry) {
  const kx = rx * SVG_KAPPA, ky = ry * SVG_KAPPA;
  svgMove(path, cx + rx, cy);
  svgCubic(path, cx + rx, cy + ky, cx + kx, cy + ry, cx, cy + ry);
  svgCubic(path, cx - kx, cy + ry, cx - rx, cy + ky, cx - rx, cy);
  svgCubic(path, cx - rx, cy - ky, cx - kx, cy - ry, cx, cy - ry);
  svgCubic(path, cx + kx, cy - ry, cx + rx, cy - ky, cx + rx, cy);
  svgClose(path);
}

const svgInitialPaint = () => ({
  fill: { r: 0, g: 0, b: 0, a: 255 }, fillIsCurrent: false, fillOpacity: 1, fillEvenOdd: false,
  stroke: null, strokeIsCurrent: false, strokeOpacity: 1, strokeWidth: 1, dash: [], dashOffset: 0,
  cap: 0, join: 0, miter: 4, current: { r: 0, g: 0, b: 0, a: 255 }, clipEvenOdd: false,
  family: null, fontSize: 16, bold: false, italic: false, anchor: 0, visible: true, preserve: false, opacity: 1
});

/// What the engine's SvgReader makes of text, as { drawing, rules, reasons }.
function readSvgText(text) {
  const rules = [], reasons = [], clips = new Map();

  const refuse = (where, what) => {
    if (rules.length < SVG_MOST_ISSUES) {
      rules.push("svg.unsupported");
      reasons.push(`${where}: ${what}`);
    }
  };
  const ignored = name => {
    if (SVG_IGNORED.includes(name) || name.startsWith("aria-") || name.startsWith("data-")) return true;
    const colon = name.indexOf(":");
    if (colon <= 0) return false;
    const prefix = name.slice(0, colon);
    return prefix === "xmlns" || name === "xml:lang" || (prefix !== "xml" && prefix !== "xlink");
  };

  const declare = (element, paint, where) => {
    const said = new Map();
    const own = SVG_OWN[element.name];

    for (const [name, value] of element.attributes) {
      if (SVG_PROPERTIES.includes(name)) said.set(name, value);
      else if (name === "xml:space") paint.preserve = value === "preserve";
      else if (name === "transform" || name === "style") continue;
      else if (own && own.includes(name)) continue;
      else if (!ignored(name)) refuse(where, `the attribute ${name} is not taken`);
    }

    const style = svgAttr(element, "style");
    if (style !== null) {
      for (const declaration of style.split(";").filter(s => s.length)) {
        const colon = declaration.indexOf(":");
        if (colon <= 0) {
          if (declaration.trim()) refuse(where, `"${declaration.trim()}" in a style attribute is not a property and a value`);
          continue;
        }
        const name = declaration.slice(0, colon).trim();
        const value = declaration.slice(colon + 1).split("!important").join("").trim();
        if (SVG_PROPERTIES.includes(name)) said.set(name, value);
        else if (!ignored(name)) refuse(where, `the style property ${name} is not taken`);
      }
    }

    const get = name => said.has(name) ? said.get(name).trim() : null;
    apply(get, paint, where);

    return {
      get,
      has: name => { const v = get(name); return v !== null && v !== "none"; },
      isNone: name => get(name) === "none"
    };
  };

  const apply = (get, paint, where) => {
    const bad = (name, value, takes) => refuse(where, `${name}="${value}" is not taken: ${takes}`);
    const stated = name => { const v = get(name); return v !== null && v !== "inherit" ? v : null; };
    const isCurrent = v => v.toLowerCase() === "currentcolor";
    let v, read;

    if ((v = stated("color")) !== null) {
      if ((read = svgColour(v))) paint.current = read;
      else if (!isCurrent(v)) bad("color", v, "a colour");
    }

    for (const [name, is, slot] of [["fill", "fillIsCurrent", "fill"], ["stroke", "strokeIsCurrent", "stroke"]]) {
      if ((v = stated(name)) === null) continue;
      paint[is] = isCurrent(v);
      if (paint[is]) continue;
      if (v === "none" || v === "transparent") paint[slot] = null;
      else if ((read = svgColour(v))) paint[slot] = read;
      else bad(name, v, "none, or a colour");
    }

    for (const [name, set] of [["fill-opacity", x => { paint.fillOpacity = x; }],
                               ["stroke-opacity", x => { paint.strokeOpacity = x; }],
                               ["opacity", x => { paint.opacity *= x; }]]) {
      if ((v = stated(name)) === null) continue;
      if ((read = svgOpacity(v)) !== null) set(read);
      else bad(name, v, "a number or a percentage");
    }

    if ((v = stated("fill-rule")) !== null) {
      if (v === "nonzero" || v === "evenodd") paint.fillEvenOdd = v === "evenodd";
      else bad("fill-rule", v, "nonzero or evenodd");
    }

    if ((v = stated("clip-rule")) !== null) {
      if (v === "nonzero" || v === "evenodd") paint.clipEvenOdd = v === "evenodd";
      else bad("clip-rule", v, "nonzero or evenodd");
    }

    if ((v = stated("stroke-width")) !== null) {
      read = svgLength(v);
      if (read !== null && read >= 0) paint.strokeWidth = read;
      else bad("stroke-width", v, "a length of zero or more");
    }

    if ((v = stated("stroke-dasharray")) !== null) {
      if (v === "none") paint.dash = [];
      else {
        read = svgNumbers(v);
        if (!read || !read.length || read.some(n => n < 0)) bad("stroke-dasharray", v, "none, or plain lengths");
        else paint.dash = read.reduce((sum, n) => sum + n, 0) > 0 ? read : [];
      }
    }

    if ((v = stated("stroke-dashoffset")) !== null) {
      if ((read = svgLength(v)) !== null) paint.dashOffset = read;
      else bad("stroke-dashoffset", v, "a plain length");
    }

    for (const [name, slot, words] of [["stroke-linecap", "cap", ["butt", "round", "square"]],
                                       ["stroke-linejoin", "join", ["miter", "round", "bevel"]],
                                       ["text-anchor", "anchor", ["start", "middle", "end"]]]) {
      if ((v = stated(name)) === null) continue;
      const found = words.indexOf(v);
      if (found >= 0) paint[slot] = found;
      else bad(name, v, words.join(", "));
    }

    if ((v = stated("stroke-miterlimit")) !== null) {
      read = svgNumber(v);
      if (read !== null && read >= 1) paint.miter = read;
      else bad("stroke-miterlimit", v, "a number of one or more");
    }

    if ((v = stated("font-family")) !== null) {
      const first = v.split(",")[0].trim().replace(/^["']+|["']+$/g, "").trim();
      paint.family = !first || SVG_GENERIC_FAMILIES.includes(first.toLowerCase()) ? null : first;
    }

    if ((v = stated("font-size")) !== null) {
      read = svgLength(v);
      if (read !== null && read >= 0) paint.fontSize = read;
      else bad("font-size", v, "a plain length");
    }

    if ((v = stated("font-weight")) !== null) {
      read = svgNumber(v);
      if (v === "bold" || (read !== null && read >= 600 && read <= 1000)) paint.bold = true;
      else if (v === "normal" || (read !== null && read >= 1 && read < 600)) paint.bold = false;
      else bad("font-weight", v, "normal, bold or a number");
    }

    if ((v = stated("font-style")) !== null) {
      if (v === "italic" || v === "oblique") paint.italic = true;
      else if (v === "normal") paint.italic = false;
      else bad("font-style", v, "normal, italic or oblique");
    }

    if ((v = stated("visibility")) !== null) {
      if (v === "visible" || v === "hidden" || v === "collapse") paint.visible = v === "visible";
      else bad("visibility", v, "visible or hidden");
    }

    v = get("display");
    if (v !== null && !["inline", "block", "none", "inherit"].includes(v)) bad("display", v, "inline or none");
  };

  const matrixOf = (element, where) => {
    const value = svgAttr(element, "transform");
    if (value === null) return SVG_IDENTITY;
    const read = svgTransform(value);
    if (!read) refuse(where, `the transform "${value}" is not one this reader takes`);
    return read || SVG_IDENTITY;
  };

  const frame = (node, element, said, where) => {
    node.m = matrixOf(element, where);
    node.clip = null;

    const clip = said.get("clip-path");
    if (clip === null || clip === "none" || clip === "inherit") return;

    const inner = clip.trim();
    if (inner.startsWith("url(") && inner.endsWith(")")) {
      const reference = inner.slice(4, -1).trim().replace(/^["']+|["']+$/g, "");
      if (reference.startsWith("#")) {
        if (clips.has(reference.slice(1))) node.clip = clips.get(reference.slice(1));
        else refuse(where, `clip-path names "${reference}", and no usable clipPath has that id`);
        return;
      }
    }

    refuse(where, `clip-path="${clip}" is not taken: only url(#id)`);
  };

  const geometry = (element, where) => {
    const length = name => {
      const value = svgAttr(element, name);
      if (value === null) return 0;
      const read = svgLength(value);
      if (read === null) refuse(where, `${name}="${value}" is not a length this reader takes`);
      return read;
    };
    const radius = name => {
      const read = length(name);
      if (read !== null && read < 0) { refuse(where, `${name} is negative`); return null; }
      return read;
    };

    const path = svgPath();

    switch (element.name) {
      case "path": {
        const d = svgAttr(element, "d") ?? "";
        const read = svgPathData(d);
        if (!read) refuse(where, `the path data "${d.slice(0, 40)}" is not well formed`);
        return read;
      }
      case "rect": {
        const x = length("x"), y = length("y"), w = radius("width"), h = radius("height"), rx = radius("rx"), ry = radius("ry");
        if (x === null || y === null || w === null || h === null || rx === null || ry === null) return null;
        if (w > 0 && h > 0) {
          const across = svgAttr(element, "rx") === null ? ry : rx;
          const down = svgAttr(element, "ry") === null ? rx : ry;
          svgRectangle(path, x, y, w, h, Math.min(across, w / 2), Math.min(down, h / 2));
        }
        return path;
      }
      case "circle": {
        const cx = length("cx"), cy = length("cy"), r = radius("r");
        if (cx === null || cy === null || r === null) return null;
        if (r > 0) svgEllipse(path, cx, cy, r, r);
        return path;
      }
      case "ellipse": {
        const cx = length("cx"), cy = length("cy"), rx = radius("rx"), ry = radius("ry");
        if (cx === null || cy === null || rx === null || ry === null) return null;
        if (rx > 0 && ry > 0) svgEllipse(path, cx, cy, rx, ry);
        return path;
      }
      case "line": {
        const x1 = length("x1"), y1 = length("y1"), x2 = length("x2"), y2 = length("y2");
        if (x1 === null || y1 === null || x2 === null || y2 === null) return null;
        svgMove(path, x1, y1);
        svgLine(path, x2, y2);
        return path;
      }
      default: {
        const points = svgAttr(element, "points") ?? "";
        const read = svgNumbers(points);
        if (!read || read.length % 2 !== 0) {
          refuse(where, `the points "${points.slice(0, 40)}" are not pairs of numbers`);
          return null;
        }
        for (let i = 0; i + 1 < read.length; i += 2) {
          if (i === 0) svgMove(path, read[i], read[i + 1]);
          else svgLine(path, read[i], read[i + 1]);
        }
        if (element.name === "polygon" && path.verbs.length) svgClose(path);
        return path;
      }
    }
  };

  const collectClip = (clip, where) => {
    const id = svgAttr(clip, "id") ?? "";
    const units = svgAttr(clip, "clipPathUnits");
    if (units !== null && units !== "userSpaceOnUse") refuse(where, `clipPathUnits="${units}" is not taken`);

    const paint = svgInitialPaint();
    const said = declare(clip, paint, where);
    if (said.has("clip-path")) refuse(where, "a clip on a clipPath is not taken");

    const own = matrixOf(clip, where);
    const shapes = [];
    for (const child of clip.children) {
      if (typeof child === "string") { if (child.trim()) refuse(where, "a clipPath holds shapes, and this one holds text"); }
      else if (!SVG_NOT_DRAWN.includes(child.name)) shapes.push(child);
    }

    if (shapes.length !== 1) {
      refuse(where, `a clipPath must hold exactly one shape, and this one holds ${shapes.length}`);
      return;
    }

    const shape = shapes[0], inside = where + " › " + shape.name;
    if (!SVG_SHAPES.includes(shape.name)) {
      refuse(inside, `<${shape.name}> in a clipPath is not taken`);
      return;
    }

    const shapePaint = Object.assign({}, paint);
    const shapeSaid = declare(shape, shapePaint, inside);
    if (shapeSaid.has("clip-path")) refuse(inside, "a clip on a clipPath's shape is not taken");

    const path = geometry(shape, inside);
    if (!path) return;

    const whole = svgTimes(own, matrixOf(shape, inside));
    if (id.length && !clips.has(id)) clips.set(id, { path: svgTransformed(path, whole), evenOdd: shapePaint.clipEvenOdd });
  };

  const collectClips = (element, where) => {
    for (const child of element.children) {
      if (typeof child === "string") continue;
      const here = where + " › " + child.name;
      if (child.name === "clipPath") collectClip(child, here);
      else if (child.name !== "metadata") collectClips(child, here);
    }
  };

  const hex2 = c => "#" + [c.r, c.g, c.b].map(v => v.toString(16).toUpperCase().padStart(2, "0")).join("");

  const shapeNode = (element, inherited, into, where) => {
    const paint = Object.assign({}, inherited);
    const said = declare(element, paint, where);
    const holder = {};
    frame(holder, element, said, where);

    const path = geometry(element, where);
    if (!path || !path.verbs.length || said.isNone("display") || !paint.visible) return;

    let fill = element.name === "line" ? null : paint.fillIsCurrent ? paint.current : paint.fill;
    let stroke = paint.strokeWidth > 0 ? (paint.strokeIsCurrent ? paint.current : paint.stroke) : null;
    const fillAlpha = (fill ? fill.a : 255) / 255 * paint.fillOpacity * paint.opacity;
    const strokeAlpha = (stroke ? stroke.a : 255) / 255 * paint.strokeOpacity * paint.opacity;
    if (fillAlpha <= 0) fill = null;
    if (strokeAlpha <= 0) stroke = null;
    if (!fill && !stroke) return;

    into.push({
      kind: "shape", m: holder.m, clip: holder.clip, path,
      fill: fill ? hex2(fill) : null, fillAlpha: fill ? fillAlpha : 0, evenOdd: paint.fillEvenOdd,
      stroke: stroke ? hex2(stroke) : null, strokeAlpha: stroke ? strokeAlpha : 0,
      strokeWidth: paint.strokeWidth, dash: paint.dash, dashOffset: paint.dashOffset,
      cap: paint.cap, join: paint.join, miter: paint.miter
    });
  };

  const textNode = (element, inherited, into, where) => {
    const paint = Object.assign({}, inherited);
    const said = declare(element, paint, where);
    const node = { kind: "text", runs: [] };
    frame(node, element, said, where);

    // White space across the pieces, and where the next piece starts: engine's Collapser.
    let pending = null, preserve = false, words = null, wordsPaint = null, offsets = null;
    let seen = false, lastSpace = false, wordsWhere = "";
    const isDefault = o => o.x === null && o.y === null && o.dx === 0 && o.dy === 0;

    const flush = () => {
      if (words === null) return;
      const written = words;
      words = null;
      if (!written.length) return;

      const p = wordsPaint;
      if ((p.strokeIsCurrent ? p.current : p.stroke) && p.strokeWidth > 0)
        refuse(wordsWhere, "a stroked text is not taken: a text is filled");

      const fill = p.fillIsCurrent ? p.current : p.fill;
      const alpha = (fill ? fill.a : 255) / 255 * p.fillOpacity * p.opacity;
      if (!fill || alpha <= 0 || p.fontSize <= 0 || !p.visible) return;

      node.runs.push({
        text: written, x: offsets.x, y: offsets.y, dx: offsets.dx, dy: offsets.dy, anchor: p.anchor,
        family: p.family, size: p.fontSize, bold: p.bold, italic: p.italic, fill: hex2(fill), alpha
      });
    };

    const addWords = (text, p, here) => {
      if (words !== null && ((pending && !isDefault(pending)) || p !== wordsPaint)) flush();
      preserve = p.preserve;

      for (let i = 0; i < text.length; i++) {
        const raw = text[i];
        const c = raw === "\n" || raw === "\r" || raw === "\t" ? " " : raw;

        if (c === " " && !preserve) {
          if (lastSpace || !seen) continue;
          lastSpace = true;
        } else {
          lastSpace = false;
        }

        if (words === null) {
          words = "";
          wordsPaint = p;
          offsets = pending || { x: null, y: null, dx: 0, dy: 0 };
          pending = null;
          wordsWhere = here;
        }

        words += c;
        seen = true;
      }
    };

    const position = (span, p, here) => {
      const absolute = name => {
        const value = svgAttr(span, name);
        if (value === null) return null;
        const read = svgLength(value);
        if (read === null) refuse(here, `${name}="${value}" is not taken: one position, as a plain length`);
        return read;
      };
      const relative = name => {
        const value = svgAttr(span, name);
        if (value === null) return 0;
        const read = svgLengthOrEm(value, p.fontSize);
        if (read === null) refuse(here, `${name}="${value}" is not taken: one shift`);
        return read ?? 0;
      };
      return { x: absolute("x"), y: absolute("y"), dx: relative("dx"), dy: relative("dy") };
    };

    const spans = (span, p, here) => {
      const at = position(span, p, here);
      const held = pending || { x: null, y: null, dx: 0, dy: 0 };
      pending = { x: at.x ?? held.x, y: at.y ?? held.y, dx: held.dx + at.dx, dy: held.dy + at.dy };

      for (const child of span.children) {
        if (typeof child === "string") {
          addWords(child, p, here);
        } else {
          const inner = here + " › " + child.name;
          if (child.name === "tspan") {
            const own = Object.assign({}, p);
            const ownSaid = declare(child, own, inner);
            if (ownSaid.has("clip-path") || svgAttr(child, "transform") !== null)
              refuse(inner, "a clip or a transform on a tspan is not taken");
            spans(child, own, inner);
          } else if (!SVG_NOT_DRAWN.includes(child.name)) {
            refuse(inner, `<${child.name}> is not taken`);
          }
        }
      }
    };

    spans(element, paint, where);

    if (words !== null && lastSpace && !preserve && words.endsWith(" ")) words = words.slice(0, -1);
    flush();

    if (node.runs.length && !said.isNone("display")) into.push(node);
  };

  const children = (parent, paint, into, where) => {
    for (const child of parent.children) {
      if (typeof child === "string") {
        if (child.trim()) refuse(where, `<${parent.name}> holds text, which only a <text> draws`);
      } else {
        element(child, paint, into, where + " › " + child.name);
      }
    }
  };

  const element = (node, inherited, into, where) => {
    const name = node.name;
    if (SVG_NOT_DRAWN.includes(name) || name === "clipPath") return;

    if (name === "defs") {
      declare(node, Object.assign({}, inherited), where);
      for (const child of node.children) {
        if (typeof child !== "string" && child.name !== "clipPath" && !SVG_NOT_DRAWN.includes(child.name))
          refuse(where + " › " + child.name, `<${child.name}> is not taken: only a clipPath in defs`);
      }
      return;
    }

    if (name === "g") {
      const paint = Object.assign({}, inherited);
      const said = declare(node, paint, where);
      const group = { kind: "group", children: [] };
      frame(group, node, said, where);
      children(node, paint, group.children, where);
      if (!said.isNone("display")) into.push(group);
      return;
    }

    if (SVG_SHAPES.includes(name)) { shapeNode(node, inherited, into, where); return; }
    if (name === "text") { textNode(node, inherited, into, where); return; }

    refuse(where, `<${name}> is not taken`);
  };

  try {
    const root = svgParse(text);
    if (root.name !== "svg") throw { svgMalformed: `The root element is <${root.name}>, not <svg>.` };

    collectClips(root, "svg");

    let box = null;
    const stated = svgAttr(root, "viewBox");
    if (stated !== null) {
      const read = svgNumbers(stated);
      if (read && read.length === 4 && read[2] > 0 && read[3] > 0) box = read;
      else refuse("svg", `the viewBox "${stated}" is not four numbers with a width and a height above zero`);
    } else {
      const w = svgLength(svgAttr(root, "width")), h = svgLength(svgAttr(root, "height"));
      if (w > 0 && h > 0) box = [0, 0, w, h];
      else refuse("svg", "this drawing has no viewBox and no width and height in plain lengths");
    }

    const paint = svgInitialPaint();
    const said = declare(root, paint, "svg");
    const group = { kind: "group", children: [] };
    frame(group, root, said, "svg");
    children(root, paint, group.children, "svg");

    const drawing = box && !rules.length ? { box, nodes: [group], ratio: box[2] / box[3] } : null;
    return { drawing, rules, reasons };
  } catch (e) {
    if (e && e.svgMalformed) return { drawing: null, rules: ["svg.invalid"], reasons: [e.svgMalformed] };
    throw e;
  }
}

// A redraw comes at every keystroke and a chart does not change with it. Keyed on the text
// itself; a handful, because a report holds a handful and the base64 is a long key.
const drawingsRead = new Map();

/// A picture's base64 as a drawing's reading; null when the bytes are not a drawing at all.
function readingOf(base64) {
  if (!base64) return null;
  if (drawingsRead.has(base64)) return drawingsRead.get(base64);

  const bytes = bytesOfBase64(base64);
  let reading = null;
  if (bytes && looksLikeDrawing(bytes)) {
    const text = textOfUtf8(bytes);
    reading = text === null
      ? { drawing: null, rules: ["svg.invalid"], reasons: ["This file is not UTF-8."] }
      : readSvgText(text);
  }

  if (drawingsRead.size >= 16) drawingsRead.clear();
  drawingsRead.set(base64, reading);
  return reading;
}

/// The echo of Pictures.Refusal and of PngImage's header: why a picture cannot be drawn, or
/// null if it can. Nothing at all is not a refusal.
function pictureRefusal(base64) {
  if (!base64) return null;

  const reading = readingOf(base64);
  if (reading) return reading.rules.length ? reading.reasons.join(" ") : null;

  const b = bytesOfBase64(base64);
  if (!b || !b.length) return "These bytes are not a JPEG, a PNG or an SVG drawing.";

  if (b.length > 3 && b[0] === 0xFF && b[1] === 0xD8 && b[2] === 0xFF) return null;

  if (!(b.length >= 8 && b[0] === 137 && b[1] === 80 && b[2] === 78 && b[3] === 71))
    return "These bytes are not a JPEG, a PNG or an SVG drawing.";

  const unreadable = "This PNG could not be read.";
  const signature = [137, 80, 78, 71, 13, 10, 26, 10];
  if (b.length < 33 || signature.some((v, i) => b[i] !== v)) return unreadable;

  const word = at => ((b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3]) >> 0;
  let width = 0, height = 0, depth = 0, colour = -1, data = 0;

  for (let at = 8; at + 8 <= b.length;) {
    const length = word(at);
    if (length < 0 || length > b.length - at - 12) return unreadable;
    const type = String.fromCharCode(b[at + 4], b[at + 5], b[at + 6], b[at + 7]);
    const body = at + 8;

    if (type === "IHDR") {
      if (length < 13) return unreadable;
      width = word(body);
      height = word(body + 4);
      depth = b[body + 8];
      colour = b[body + 9];
      if (b[body + 10] !== 0) return unreadable;
      if (b[body + 12] !== 0) return "This PNG is interlaced: save it without interlacing.";
    } else if (type === "IDAT") {
      data += length;
    } else if (type === "IEND") {
      break;
    }

    at += 12 + length;
  }

  if (width <= 0 || height <= 0 || data === 0) return unreadable;
  if (![0, 2, 4, 6].includes(colour)) return "This PNG has a palette: save it as RGB, with or without alpha.";
  if (depth !== 8) return `This PNG has ${depth} bits to a channel, and 8 are taken.`;

  if (colour === 4 || colour === 6) {
    const decoded = height * (1 + width * (colour === 4 ? 2 : 4));
    if (decoded > 512 * 1024 * 1024) return "This PNG is too large to take apart.";
    if (decoded > data * 1032) return "This PNG claims to be far larger than its data could hold.";
  }

  return null;
}

const pictureIsDrawing = base64 => !!readingOf(base64);

/// A picture's bytes, whichever way they come: the data's if it brings them, the template's.
function pictureData(item) {
  const bytes = resolvedImageSource(item);
  return bytes ? bytes.data : (item.Source && item.Source.Data) || null;
}

/// Width over height, as ImageItem.AspectRatio answers: what is declared, then what a drawing
/// says of itself. Zero when there is neither.
function pictureRatio(item) {
  if (item.NaturalWidth > 0 && item.NaturalHeight > 0) return item.NaturalWidth / item.NaturalHeight;
  const reading = readingOf(pictureData(item));
  return reading && reading.drawing ? reading.drawing.ratio : 0;
}

// ---- drawing it ------------------------------------------------------------

const n5 = v => Number.isFinite(v) ? Math.round(v * 100000) / 100000 : 0;

function drawnPathData(path) {
  let d = "", at = 0;
  for (const verb of path.verbs) {
    const count = verb === "C" ? 6 : verb === "Z" ? 0 : 2;
    d += verb;
    for (let i = 0; i < count; i++) d += (i ? " " : "") + n5(path.points[at++]);
  }
  return d;
}

/// Where each piece of a text goes, as VectorText.Place works it out, with this page's ruler.
function placedRuns(text, documentFamily) {
  const placed = [];
  let penX = 0, penY = 0;

  const widthOf = (run, family) => {
    const font = `${run.italic ? "italic " : ""}${run.bold ? "700 " : ""}100px ${family || unnamedFace}`;
    return measureRun(run.text, font) * run.size / 100;
  };

  for (let start = 0; start < text.runs.length;) {
    let end = start + 1;
    while (end < text.runs.length && text.runs[end].x === null && text.runs[end].y === null) end++;

    const widths = [];
    let total = 0;
    for (let i = start; i < end; i++) {
      const width = widthOf(text.runs[i], text.runs[i].family || documentFamily);
      widths.push(width);
      total += text.runs[i].dx + width;
    }

    const first = text.runs[start];
    let x = (first.x ?? penX) + (first.anchor === 1 ? -total / 2 : first.anchor === 2 ? -total : 0);
    let y = first.y ?? penY;

    for (let i = start; i < end; i++) {
      const run = text.runs[i];
      x += run.dx;
      y += run.dy;
      placed.push({ x, y, run, family: run.family || documentFamily || "" });
      x += widths[i - start];
    }

    penX = x;
    penY = y;
    start = end;
  }

  return placed;
}

/// A drawing as markup, from the list and never from the file, as SvgBackend writes it. The
/// clip ids are made here: every page of a designer sits in one document, and a file's own
/// "plot" would be the first such id's, for every chart after it.
let drawingClips = 0;
let drawingFamily = null;

function drawingSvg(drawing, r, preserve, opacity, documentFamily) {
  const nodes = list => list.map(node => {
    let around = "", before = "";

    if (node.m.some((v, i) => v !== SVG_IDENTITY[i])) around += ` transform="matrix(${node.m.map(n5).join(" ")})"`;
    if (node.clip) {
      const id = "fk-clip-" + ++drawingClips;
      before = `<clipPath id="${id}"><path d="${drawnPathData(node.clip.path)}"${node.clip.evenOdd ? ' clip-rule="evenodd"' : ""}/></clipPath>`;
      around += ` clip-path="url(#${id})"`;
    }

    let inner = "";
    if (node.kind === "group") {
      inner = nodes(node.children);
    } else if (node.kind === "shape") {
      inner = `<path d="${drawnPathData(node.path)}" fill="${node.fill || "none"}"`;
      if (node.fill && node.fillAlpha * opacity < 1) inner += ` fill-opacity="${n3(node.fillAlpha * opacity)}"`;
      if (node.fill && node.evenOdd) inner += ' fill-rule="evenodd"';
      if (node.stroke) {
        inner += ` stroke="${node.stroke}" stroke-width="${n5(node.strokeWidth)}"`;
        if (node.strokeAlpha * opacity < 1) inner += ` stroke-opacity="${n3(node.strokeAlpha * opacity)}"`;
        if (node.cap) inner += ` stroke-linecap="${node.cap === 1 ? "round" : "square"}"`;
        if (node.join) inner += ` stroke-linejoin="${node.join === 1 ? "round" : "bevel"}"`;
        if (node.miter !== 4) inner += ` stroke-miterlimit="${n5(node.miter)}"`;
        if (node.dash.length) {
          inner += ` stroke-dasharray="${node.dash.map(n5).join(" ")}"`;
          if (node.dashOffset) inner += ` stroke-dashoffset="${n5(node.dashOffset)}"`;
        }
      }
      inner += "/>";
    } else {
      inner = placedRuns(node, documentFamily).map(({ x, y, run, family }) =>
        `<text x="${n5(x)}" y="${n5(y)}" font-family="${esc(family || unnamedFace)}" font-size="${n5(run.size)}" fill="${run.fill}"`
        + (run.alpha * opacity < 1 ? ` fill-opacity="${n3(run.alpha * opacity)}"` : "")
        + (run.bold ? ' font-weight="bold"' : "") + (run.italic ? ' font-style="italic"' : "")
        + `>${esc(run.text)}</text>`).join("");
    }

    return before + (around ? `<g${around}>${inner}</g>` : inner);
  }).join("");

  return `<svg x="${n3(r.left)}" y="${n3(r.top)}" width="${n3(r.width)}" height="${n3(r.height)}"`
       + ` viewBox="${drawing.box.map(n5).join(" ")}" preserveAspectRatio="${preserve}">${nodes(drawing.nodes)}</svg>`;
}

/// A rectangle saying a picture was refused, where the PDF would stop. What the rule says is
/// in the panel; this is so the place is not mistaken for an empty frame.
function refusedPicture(r) {
  return `<rect x="${n3(r.left)}" y="${n3(r.top)}" width="${n3(r.width)}" height="${n3(r.height)}" fill="${ON_PAPER.placeholderFill}" stroke="${ON_PAPER.placeholderLine}" stroke-width=".2"/>` +
         `<text x="${n3((r.left + r.right) / 2)}" y="${n3((r.top + r.bottom) / 2)}" text-anchor="middle" font-size="3" fill="${ON_PAPER.placeholderInk}">${esc(t("canvas.refusedPicture"))}</text>`;
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
  item.$type === "SubTemplateItem" && referenceOf(item) !== null ? "st://" + referenceOf(item) :
  (item.RepeatOver || "").trim() ? "\u21BB " + item.RepeatOver.trim() :
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

  sections.forEach((section, i) => {
    const named = sections.length === 1 ? t("panel.section") : `${t("panel.section")} ${i + 1}`;

    // A section with no page size is laid out on a sheet of nothing. This page writes A4
    // into one that has no Dimension at all, but an empty one - which is what the engine
    // writes for a section built without a size - stays empty, and so does a file read
    // straight from somewhere else.
    const size = section.Dimension;
    if (profile.sound && !(size && Number(size.Width) > 0 && Number(size.Height) > 0))
      add("sound.noPageSize", "error", section, named);

    walkFor(section, named, profile, add, rep.HeadingNumbering);
  });
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

      // One per drawing, for the same reason. Only what the template carries can be seen
      // from here: a chart the data brings is known when it is bound.
      for (const drawing of items.filter(i => i.$type === "ImageItem" && i.Source && pictureIsDrawing(i.Source.Data)))
        add("can.drawingInFlow", "warning", drawing, t("panel.report"));
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

      // A picture no output could draw: without this the panel said nothing, the proof
      // drew it and the PDF stopped at the end.
      if (child.$type === "ImageItem" && child.Source && child.Source.Data) {
        const reason = pictureRefusal(child.Source.Data);
        if (reason) add("sound.unreadablePicture", "error", child, here, { reason });
      }

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
