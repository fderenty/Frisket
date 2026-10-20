using System.Text.Json.Serialization;
using System.Globalization;
using System.Text;

namespace Frisket
{
    /// <summary>
    /// Renders to SVG, one document per page. No dependency: SVG is text, and its user
    /// units are set to millimeters here so the engine's coordinates go straight through.
    ///
    /// It is meant as the reference backend and the debugging one - open
    /// <see cref="ToHtml"/> in a browser and the whole report is there - not as the
    /// production output. Note that a browser measures text with its own fonts, so what
    /// you see matches the layout only as closely as the measurer matches the browser.
    /// </summary>
    public sealed class SvgBackend : IReportBackend
    {
        private readonly List<string> _pages = new();
        private readonly HashSet<string> _usedFamilies = new(StringComparer.OrdinalIgnoreCase);
        private StringBuilder _current = new();
        private string _title = string.Empty;

        /// <summary>Sheet size of the last page begun, used to set @page when printing.</summary>
        private double _pageWidth = 210;
        private double _pageHeight = 297;

        public ITextMeasurer Measurer { get; }

        /// <summary>The finished SVG sections, one per page.</summary>
        public IReadOnlyList<string> Pages => _pages;

        /// <summary>Sheet colour. Set it transparent to get a page with no background.</summary>
        public Color PageBackground { get; set; } = Color.White;

        /// <summary>Draws the header, body and footer bands, to see what the resolver reserved.</summary>
        public bool ShowBands { get; set; }

        /// <summary>
        /// Fonts to embed in <see cref="ToHtml"/>. Without this the browser draws with
        /// whatever it happens to have, while the layout was computed with the real
        /// metrics - so the preview drifts from the truth. Embedding removes the drift.
        /// The per-page SVG files are left lean and do not carry the fonts.
        /// </summary>
        public FontLibrary? EmbeddedFonts { get; set; }

        public SvgBackend(ITextMeasurer? measurer = null)
            => Measurer = measurer ?? FixedPitchTextMeasurer.Default;

        public void BeginDocument(Report report)
        {
            _pages.Clear();
            _usedFamilies.Clear();
            _title = report.Title ?? string.Empty;
        }

        public void BeginPage(Page page)
        {
            _pageWidth = page.Frame.Width;
            _pageHeight = page.Frame.Height;
            _current = new StringBuilder(4096);
            // xml:space="preserve" for viewers that read SVG 1.1; the <style> after this tag is
            // what a browser honours - see there.
            _current.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" ")
                    .Append("xml:space=\"preserve\" ")
                    .Append("width=\"").Append(N(page.Frame.Width)).Append("mm\" ")
                    .Append("height=\"").Append(N(page.Frame.Height)).Append("mm\" ")
                    .Append("viewBox=\"0 0 ").Append(N(page.Frame.Width)).Append(' ').Append(N(page.Frame.Height))
                    .Append("\">\n");

            _current.Append("  <title>").Append(Escape(_title)).Append(" - page ").Append(page.Number).Append("</title>\n");

            // The text drawn as the PDF draws it. Found on 20 October 2026 by putting a
            // sub-document's article on screen with the PDF beside it in a real reader; every
            // number the suite measures was right, and the proof was still wrong twice.
            //
            // - The spaces are the text's. A browser collapses the space a piece of rich text
            //   starts with - " - the wording", after a word in bold - and draws the dash where
            //   the space should be, touching the word; the PDF draws the space. Its own sheet
            //   gives every <text> white-space:nowrap, which beats anything inherited from the
            //   root - xml:space and a style attribute alike were ignored - so this is a rule
            //   that names the element.
            // - No kerning and no ligatures, because the engine measures neither and the PDF
            //   draws neither: one glyph per character, at its advance. A bold "Art. 1" came
            //   out 11.448mm kerned, against 11.246 in the engine and 11.249 without.
            _current.Append("  <style>text{white-space:pre;font-kerning:none;font-variant-ligatures:none}</style>\n");

            if (PageBackground.A > 0)
            {
                _current.Append("  <rect x=\"0\" y=\"0\" width=\"").Append(N(page.Frame.Width))
                        .Append("\" height=\"").Append(N(page.Frame.Height))
                        .Append("\" fill=\"").Append(Hex(PageBackground)).Append("\"/>\n");
            }

            if (ShowBands)
            {
                Band(page.HeaderBand, "#c8e6c9");
                Band(page.BodyBand, "#e3f2fd");
                Band(page.FooterBand, "#ffe0b2");
            }
        }

        private void Band(Rect band, string colour)
        {
            if (band.IsEmpty || double.IsNaN(band.Left))
                return;

            _current.Append("  <rect x=\"").Append(N(band.Left)).Append("\" y=\"").Append(N(band.Top))
                    .Append("\" width=\"").Append(N(band.Width)).Append("\" height=\"").Append(N(band.Height))
                    .Append("\" fill=\"").Append(colour).Append("\" fill-opacity=\"0.4\"/>\n");
        }

        public void EndPage()
        {
            _current.Append("</svg>\n");
            _pages.Add(_current.ToString());
        }

        public void EndDocument() { }

        public void DrawRectangle(Rect frame, PenStyle? pen, BrushStyle? fill)
        {
            _current.Append("  <rect x=\"").Append(N(frame.Left)).Append("\" y=\"").Append(N(frame.Top))
                    .Append("\" width=\"").Append(N(frame.Width)).Append("\" height=\"").Append(N(frame.Height))
                    .Append("\" fill=\"");

            if (fill == null)
                _current.Append("none\"");
            else
                _current.Append(Hex(fill.Color)).Append('"').Append(Opacity("fill-opacity", fill.Color));

            if (pen != null)
            {
                _current.Append(" stroke=\"").Append(Hex(pen.Color)).Append('"')
                        .Append(Opacity("stroke-opacity", pen.Color))
                        .Append(" stroke-width=\"").Append(N(pen.Width)).Append('"')
                        .Append(Dashes(pen));
            }

            _current.Append("/>\n");
        }

        public void DrawLine(double x1, double y1, double x2, double y2, PenStyle pen)
        {
            _current.Append("  <line x1=\"").Append(N(x1)).Append("\" y1=\"").Append(N(y1))
                    .Append("\" x2=\"").Append(N(x2)).Append("\" y2=\"").Append(N(y2))
                    .Append("\" stroke=\"").Append(Hex(pen.Color)).Append('"')
                    .Append(Opacity("stroke-opacity", pen.Color))
                    .Append(" stroke-width=\"").Append(N(pen.Width)).Append('"')
                    .Append(Dashes(pen))
                    .Append("/>\n");
        }

        /// <summary>
        /// One line turned about its own start. Drawn as a real element rather than as a
        /// background, so it survives a browser printing the page: background graphics are
        /// dropped by default, and a watermark that disappears on the printout is missing
        /// from the one copy that could be passed off as an original.
        /// </summary>
        public void DrawRotatedText(double x, double y, string text, TextStyle style, double degrees)
        {
            if (string.IsNullOrEmpty(text))
                return;

            _usedFamilies.Add(Measurer.ResolveFamily(style));

            _current.Append("  <text x=\"0\" y=\"0\" transform=\"translate(")
                    .Append(N(x)).Append(' ').Append(N(y))
                    .Append(") rotate(").Append(N(-degrees)).Append(")\" font-family=\"")
                    .Append(Escape(string.IsNullOrWhiteSpace(style.Family) ? "sans-serif" : style.Family))
                    .Append('"')
                    .Append(" font-size=\"").Append(N(style.EmSizeInMillimeters)).Append('"')
                    .Append(" fill=\"").Append(Hex(style.Color ?? Frisket.Color.Black)).Append('"')
                    .Append(Opacity("fill-opacity", style.Color ?? Frisket.Color.Black));

            if (style.Bold == true)
                _current.Append(" font-weight=\"bold\"");
            if (style.Italic == true)
                _current.Append(" font-style=\"italic\"");

            _current.Append('>').Append(Escape(text)).Append("</text>\n");
        }

        public void DrawText(Rect frame, string text, TextStyle style)
        {
            if (string.IsNullOrEmpty(text))
                return;

            FontMetrics metrics = Measurer.GetMetrics(style);
            _usedFamilies.Add(Measurer.ResolveFamily(style));
            string[] lines = text.Split('\n');

            // The first baseline sits so that the cap is centred in its line, which puts
            // the top of the capitals the same distance from the frame in every face.
            // Taking it from the font's ascender instead lets that distance swing by a
            // millimetre and a half between typefaces, which is plainly visible against a
            // table border.
            double first = (metrics.LineHeight + metrics.CapHeight) / 2;
            double block = lines.Length * metrics.LineHeight;
            double offset = (style.VerticalAlignment ?? Frisket.VerticalAlignment.Top) switch
            {
                VerticalAlignment.Middle => Math.Max(0, (frame.Height - block) / 2),
                VerticalAlignment.Bottom => Math.Max(0, frame.Height - block),
                _ => 0
            };

            (double x, string anchor) = (style.HorizontalAlignment ?? Frisket.HorizontalAlignment.Left) switch
            {
                HorizontalAlignment.Center => ((frame.Left + frame.Right) / 2, "middle"),
                HorizontalAlignment.Right => (frame.Right, "end"),
                _ => (frame.Left, "start")
            };

            for (int i = 0; i < lines.Length; i++)
            {
                double baseline = frame.Top + offset + first + i * metrics.LineHeight;

                _current.Append("  <text x=\"").Append(N(x)).Append("\" y=\"").Append(N(baseline))
                        .Append("\" font-family=\"")
                        .Append(Escape(string.IsNullOrWhiteSpace(style.Family) ? "sans-serif" : style.Family))
                        .Append('"')
                        .Append(" font-size=\"").Append(N(style.EmSizeInMillimeters)).Append('"')
                        .Append(" fill=\"").Append(Hex(style.Color ?? Frisket.Color.Black)).Append('"')
                        .Append(Opacity("fill-opacity", style.Color ?? Frisket.Color.Black));

                if (anchor != "start")
                    _current.Append(" text-anchor=\"").Append(anchor).Append('"');

                // The same padding the PDF works out, said the way SVG says it. Both
                // widen every space by the same millimetres, which is what keeps the
                // two files describing one document.
                double padding = Justification.SpacePadding(lines[i],
                    Measurer.Measure(lines[i], style, double.PositiveInfinity).Width,
                    frame.Width, i == lines.Length - 1, style);

                if (padding > 0)
                    _current.Append(" word-spacing=\"").Append(N(padding)).Append('"');
                if (style.Bold == true)
                    _current.Append(" font-weight=\"bold\"");
                if (style.Italic == true)
                    _current.Append(" font-style=\"italic\"");

                // Both decorations share one attribute in SVG - two of them would have the
                // second silently win, rather than drawing both lines.
                string decoration = (style.Underline == true ? "underline " : "")
                                   + (style.Strikethrough == true ? "line-through" : "");
                if (decoration.Length > 0)
                    _current.Append(" text-decoration=\"").Append(decoration.TrimEnd()).Append('"');

                _current.Append('>').Append(Escape(lines[i])).Append("</text>\n");
            }
        }

        /// <summary>
        /// SVG has no forms, so a field is drawn as what it looks like: the box, its
        /// value, and a tick when it is ticked. That is also what the field will look
        /// like once the document is flattened, which makes this a fair preview.
        /// </summary>
        public void DrawField(Rect frame, FieldItem field, TextStyle font)
        {
            Rect box = field.Kind == FieldKind.Checkbox ? Square(frame, font) : frame;

            if (field.Fill != null || field.Outline != null)
                DrawRectangle(box, field.Outline, field.Fill);

            if (field.Kind == FieldKind.Checkbox)
            {
                if (field.IsChecked)
                    Tick(box, font.Color ?? Color.Black);
                return;
            }

            // A signature area is an empty box with its label under the rule, so it reads
            // the same whether it is signed on screen or with a pen.
            if (field.Kind == FieldKind.Signature)
            {
                double rule = frame.Bottom - Measurer.GetMetrics(font).LineHeight;
                DrawLine(frame.Left + 1, rule, frame.Right - 1, rule,
                         new PenStyle { Width = 0.15, Color = new(150, 150, 150) });

                if (!string.IsNullOrEmpty(field.Tooltip))
                {
                    DrawText(new Rect(frame.Left + 1, rule, frame.Right - 1, frame.Bottom),
                             field.Tooltip!, Faint(font));
                }
                return;
            }

            if (!string.IsNullOrEmpty(field.Value))
                DrawText(Inset(frame), field.Value!, font);
            else if (!string.IsNullOrEmpty(field.Tooltip))
                DrawText(Inset(frame), field.Tooltip!, Faint(font));
        }

        private static Rect Inset(Rect frame) => new(frame.Left + 1, frame.Top + 0.4, frame.Right - 1, frame.Bottom);

        private static TextStyle Faint(TextStyle font)
        {
            TextStyle copy = font.Clone();
            copy.Color = new(150, 150, 150);
            return copy;
        }

        /// <summary>A checkbox is square and sits at the top left of what it was given.</summary>
        private Rect Square(Rect frame, TextStyle font)
        {
            double side = Math.Min(frame.Height, Measurer.GetMetrics(font).LineHeight);
            return Rect.FromSize(frame.Left, frame.Top, side, side);
        }

        private void Tick(Rect box, Color colour)
        {
            var pen = new PenStyle { Width = Math.Max(0.15, box.Width * 0.12), Color = colour };
            double x = box.Left, y = box.Top, w = box.Width, h = box.Height;

            DrawLine(x + w * 0.22, y + h * 0.52, x + w * 0.42, y + h * 0.74, pen);
            DrawLine(x + w * 0.42, y + h * 0.74, x + w * 0.80, y + h * 0.26, pen);
        }

        public void DrawImage(Rect frame, ImageSource image) => DrawImage(frame, image, 1);

        public void DrawImage(Rect frame, ImageSource image, double opacity)
        {
            if (image.Data.Length == 0 || opacity <= 0)
                return;

            _current.Append("  <image x=\"").Append(N(frame.Left)).Append("\" y=\"").Append(N(frame.Top))
                    .Append("\" width=\"").Append(N(frame.Width)).Append("\" height=\"").Append(N(frame.Height));

            if (opacity < 1)
                _current.Append("\" opacity=\"").Append(N(opacity));

            // Escaped, like every other string this file writes. It was the only bare
            // Append of one, and the value is not ours: ReportBinder takes whatever sits
            // between "data:" and the first comma, so a canvas posting
            // `data:image/png" onerror="..."` put a second attribute into the tag - which
            // a real parser reads as a second attribute and a browser then runs. The
            // sibling consumer already knew: FlowingHtml wraps the same field in Escape.
            _current.Append("\" preserveAspectRatio=\"none\" xlink:href=\"data:")
                    .Append(Escape(image.MediaType)).Append(";base64,")
                    .Append(Convert.ToBase64String(image.Data))
                    .Append("\"/>\n");
        }

        public void Dispose() { }

        // ------------------------------------------------------------------

        /// <summary>
        /// The @font-face rules that carry these fonts inside an HTML page. Without them
        /// a browser substitutes whatever it has, and since the substitute has its own
        /// cap height the text comes out the wrong size - which makes a preview meant for
        /// comparing typefaces show anything but the typefaces.
        /// </summary>
        public static string FontFaceRules(IEnumerable<FontFile> fonts)
        {
            var sb = new StringBuilder();

            foreach (FontFile font in fonts)
            {
                if (font.Data.Length == 0)
                    continue;

                sb.Append("@font-face{font-family:'").Append(Escape(font.Family)).Append("';")
                  .Append("font-weight:").Append(font.Bold ? "bold" : "normal").Append(';')
                  .Append("font-style:").Append(font.Italic ? "italic" : "normal").Append(';')
                  .Append("src:url(data:font/ttf;base64,")
                  .Append(Convert.ToBase64String(font.Data))
                  .Append(") format('truetype')}\n");
            }

            return sb.ToString();
        }

        /// <summary>
        /// The rendered section in pieces, for dropping into a page you already have.
        ///
        /// <see cref="Pages"/> are plain &lt;svg&gt; elements, ready to place wherever they
        /// belong. <see cref="FontCss"/> goes in once, anywhere the page can see it:
        /// without it a browser substitutes a face of its own, and since the substitute
        /// has its own cap height the text comes out the wrong size - the document is
        /// still readable, and no longer the one that was laid out.
        ///
        /// Keeping them apart is the point. A page holding twenty invoices needs the
        /// fonts once, not twenty times.
        /// </summary>
        public sealed class SvgFragment
        {
            /// <summary>The @font-face rules, to write inside a &lt;style&gt; once.</summary>
            public required string FontCss { get; init; }

            /// <summary>One &lt;svg&gt; element per page.</summary>
            public required IReadOnlyList<string> Pages { get; init; }

            /// <summary>Sheet size in millimetres, for sizing whatever holds the pages.</summary>
            public double PageWidth { get; init; }
            public double PageHeight { get; init; }

            /// <summary>The section's title, already escaped for HTML.</summary>
            public required string Title { get; init; }
        }

        /// <summary>
        /// The section as pieces to assemble, rather than as a page of its own. Use this
        /// to put a report inside an application's own layout; <see cref="ToHtml"/> is
        /// the same pieces wrapped in a standalone file.
        /// </summary>
        public SvgFragment ToFragment() => new()
        {
            // Only the families the report actually drew with: embedding the whole
            // library would carry fonts nothing references.
            FontCss = EmbeddedFonts == null
                ? string.Empty
                : FontFaceRules(EmbeddedFonts.Fonts.Where(f => _usedFamilies.Contains(f.Family))),
            Pages = _pages.ToArray(),
            PageWidth = _pageWidth,
            PageHeight = _pageHeight,
            Title = Escape(_title)
        };

        /// <summary>What a standalone file should look like around the pages.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter<HtmlChrome>))]
        public enum HtmlChrome
        {
            /// <summary>
            /// A viewer: grey surround, the sheets centred and shadowed. For opening the
            /// file and looking at it.
            /// </summary>
            Preview,

            /// <summary>
            /// The pages and nothing else - white, flush, no shadow. For an iframe, where
            /// the surrounding page provides its own frame and a grey band inside it looks
            /// like a mistake.
            /// </summary>
            Bare
        }

        /// <summary>
        /// Wraps every page in one standalone HTML file.
        ///
        /// This is the form to put in an iframe, through src or srcdoc: a frame needs a
        /// section of its own, which a fragment is not. Inside a page you already own,
        /// <see cref="ToFragment"/> is the better answer - it adds no section and lets
        /// the fonts be written once.
        /// </summary>
        public string ToHtml(HtmlChrome chrome = HtmlChrome.Preview)
        {
            SvgFragment fragment = ToFragment();

            var sb = new StringBuilder();
            sb.Append("<!doctype html>\n<html><head><meta charset=\"utf-8\">\n<title>")
              .Append(fragment.Title).Append("</title>\n<style>\n")
              .Append(fragment.FontCss);

            sb.Append(chrome == HtmlChrome.Bare
                      ? "body{background:#fff;margin:0;padding:0;font-family:sans-serif}\n"
                      : "body{background:#525659;margin:0;padding:24px;font-family:sans-serif}\n")
              // The sheet must be as wide as the sheet. A plain block would stretch to the
              // window and spill its white background right across it, which looks like
              // the page itself has no size.
              .Append(chrome == HtmlChrome.Bare
                      ? ".page{background:#fff;margin:0;display:block;width:fit-content;max-width:100%}\n"
                      : ".page{background:#fff;margin:0 auto 24px;box-shadow:0 2px 12px rgba(0,0,0,.5);"
                        + "display:block;width:fit-content;max-width:100%;overflow:auto}\n")
              .Append(".page svg{display:block}\n")
              .Append(".num{color:#bbb;text-align:center;font-size:12px;margin-bottom:24px}\n")

              // The viewer chrome belongs to the screen. On paper the sheet is the sheet:
              // no shadow, no grey surround, and no page counter of ours competing with
              // the one the report prints in its own footer.
              .Append("@page{size:").Append(N(_pageWidth)).Append("mm ").Append(N(_pageHeight)).Append("mm;margin:0}\n")
              .Append("@media print{")
              .Append("body{background:#fff;margin:0;padding:0}")
              .Append(".page{margin:0;box-shadow:none;break-after:page;page-break-after:always}")
              .Append(".page:last-of-type{break-after:auto;page-break-after:auto}")
              .Append(".num{display:none}")
              .Append("}\n")
              .Append("</style></head><body>\n");

            for (int i = 0; i < _pages.Count; i++)
            {
                sb.Append("<div class=\"page\">").Append(_pages[i]).Append("</div>\n")
                  .Append("<div class=\"num\">page ").Append(i + 1).Append(" / ").Append(_pages.Count).Append("</div>\n");
            }

            sb.Append("</body></html>\n");
            return sb.ToString();
        }

        /// <summary>Writes one SVG file per page plus an HTML index, and returns the index path.</summary>
        public string SaveTo(string directory, string baseName = "page")
        {
            Directory.CreateDirectory(directory);

            for (int i = 0; i < _pages.Count; i++)
                File.WriteAllText(Path.Combine(directory, $"{baseName}{i + 1}.svg"), _pages[i]);

            string index = Path.Combine(directory, $"{baseName}.html");
            File.WriteAllText(index, ToHtml());
            return index;
        }

        // ------------------------------------------------------------------

        private static string N(double value)
            => double.IsNaN(value) || double.IsInfinity(value)
                ? "0"
                : value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Hex(Color c)
            => string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);

        private static string Opacity(string attribute, Color c)
            => c.A == 255 ? string.Empty
                          : string.Concat(" ", attribute, "=\"", N(c.A / 255.0), "\"");

        private static string Dashes(PenStyle pen) => pen.DashStyle switch
        {
            DashStyle.Dash => " stroke-dasharray=\"2,1\"",
            DashStyle.Dot => " stroke-dasharray=\"0.4,0.8\"",
            DashStyle.DashDot => " stroke-dasharray=\"2,1,0.4,1\"",
            _ => string.Empty
        };

        private static string Escape(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var sb = new StringBuilder(text!.Length + 8);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
