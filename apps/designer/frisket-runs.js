// Frisket - a list of TextRun, bound to an editable surface, and back.
//
// Shared between the faces rather than written twice. The full designer edits runs in
// a dialog; the flow editor edits them in the block itself. Same conversion, same
// pitfalls - and two copies would answer differently the first time one of them
// learned a new style property, which has already happened once: Strikethrough went
// missing from three places at the same time (CLAUDE.md §15).
//
// Not part of frisket-core.js because it is made of DOM, and the core carries a hard
// promise not to be. It takes its surface as an argument for the same reason: an
// element found by id belongs to one face, and this belongs to neither.

/// Renders a Runs list as spans (one per run, styled inline) plus bare text nodes
/// for a run with no override - so a plain read of textContent, run by run, is
/// already what saving needs.
/// One run as the element the surface holds it in.
///
/// Its own function because two things build it now: laying out a whole paragraph, and
/// arming the next keystroke with a style the caret is only pending. A second copy of
/// these nine lines is the shape of mistake section 15 records - Strikethrough went
/// missing from three places at once for want of one of them.
function runElement(run) {
  const span = document.createElement("span");
  span.textContent = run.Text;
  span.dataset.font = JSON.stringify(run.Font);

  // Drawn as well as stored: what is written into data-font is what comes back out,
  // and what is written into style is what a person sees while typing.
  if (run.Font.Family) span.style.fontFamily = faceOf(run.Font.Family);
  if (run.Font.Bold) span.style.fontWeight = "bold";
  if (run.Font.Italic) span.style.fontStyle = "italic";
  const deco = [];
  if (run.Font.Underline) deco.push("underline");
  if (run.Font.Strikethrough) deco.push("line-through");
  if (deco.length) span.style.textDecoration = deco.join(" ");
  if (run.Font.Color) span.style.color = hex(run.Font.Color);
  if (run.Font.Size != null) span.style.fontSize = (0.8 + Number(run.Font.Size) * 0.5) + "em";
  return span;
}

function runsToSurface(runs, surface) {
  surface.innerHTML = "";
  for (const run of runs.length ? runs : [{ Text: "", Font: null }]) {
    if (!run.Font || !Object.keys(run.Font).some(k => run.Font[k] != null)) {
      surface.appendChild(document.createTextNode(run.Text));
      continue;
    }
    surface.appendChild(runElement(run));
  }
}

/// The inverse: walks the surface's direct children, a run per span or bare text
/// node, then merges neighbours whose style is identical so selecting and
/// re-applying the same style twice does not fragment the list forever.
function surfaceToRuns(surface) {
  const runs = [];
  for (const node of surface.childNodes) {
    if (node.nodeType === Node.TEXT_NODE) {
      if (node.textContent.length) runs.push({ Text: node.textContent, Font: null });
    } else if (node.nodeType === Node.ELEMENT_NODE) {
      // A break carries no text, so the test below dropped it and every hard line
      // break in a paragraph vanished on the next redraw - the whole thing came
      // back as one run. The engine has always understood a newline inside a run
      // (BreakAdvancedText splits on it and closes the line); it was this reader
      // that lost them on the way in.
      if (node.tagName === "BR") { runs.push({ Text: "\n", Font: null }); continue; }

      const text = node.textContent;
      if (!text.length) continue;
      let font = null;
      try { font = JSON.parse(node.dataset.font || "null"); } catch { font = null; }
      runs.push({ Text: text, Font: font });
    }
  }
  return mergeSameStyleRuns(runs);
}
