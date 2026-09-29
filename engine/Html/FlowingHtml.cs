using System.Text;

namespace Frisket
{
    /// <summary>
    /// The report as HTML that reflows: a message, not a photograph of one.
    ///
    /// This is not a backend, and it deliberately cannot be one.
    /// <see cref="IReportBackend"/> is a drawing surface - <c>BeginPage(Page)</c>,
    /// <c>DrawText(Rect, …)</c> - so everything reaches it already laid out, in
    /// millimetres, on a page. Any implementation of it therefore produces a picture
    /// of a page, which is exactly what an email must not be: an email has no pages
    /// and rewraps to whatever width the reader's phone happens to be.
    ///
    /// So this is a second consumer of the model, parallel to
    /// <see cref="LayoutResolver"/> rather than downstream of it. Binding is shared -
    /// tokens are filled the same way - and the fork is after it:
    ///
    ///     Report ─ ReportBinder ─┬─ LayoutResolver ─ IReportBackend   (paged)
    ///                            └─ FlowingHtml                       (reflows)
    ///
    /// Not to be confused with <see cref="SvgBackend.ToHtml"/>, which is also HTML and
    /// is the other thing entirely: SVG pages with the fonts embedded, for opening in
    /// a browser and seeing exactly what the printer will do. That one is a proof; this
    /// one is a delivery.
    ///
    /// The rules it writes to are email's, which are older and narrower than the web's:
    /// tables for layout, styles inline rather than in a sheet, widths in per cent, and
    /// no embedded fonts at all - the reader supplies the face. Gmail strips
    /// <c>&lt;svg&gt;</c>, Outlook renders with Word's engine, and both discard a
    /// <c>&lt;style&gt;</c> block often enough not to rely on one.
    /// </summary>
    public static class FlowingHtml
    {
        /// <summary>
        /// Writes the whole report as one HTML document.
        ///
        /// Every section runs into the next: there are no pages here, so there is
        /// nothing for a section break to break. A header and a footer appear once each,
        /// at the top and the bottom of their section, rather than on every page - the
        /// repetition they exist for has no meaning without pages, and repeating them
        /// down the message would be a page's habit worn where it means nothing.
        /// </summary>
        public static string Write(Report report)
        {
            if (report is null) throw new ArgumentNullException(nameof(report));

            var html = new StringBuilder();
            html.Append("<!doctype html>\n<html");
            if (!string.IsNullOrWhiteSpace(report.Language))
                html.Append(" lang=\"").Append(Escape(report.Language!)).Append('"');
            html.Append(">\n<head>\n<meta charset=\"utf-8\">\n");
            html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");

            if (!string.IsNullOrWhiteSpace(report.Title))
                html.Append("<title>").Append(Escape(report.Title)).Append("</title>\n");

            html.Append("</head>\n<body style=\"margin:0;padding:0;\">\n");

            // One table, the width of the reader's window up to a measure that stays
            // readable. This is the shape every mail template has had for twenty years,
            // and it is the shape because nothing else survives Outlook.
            html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" ")
                .Append("width=\"100%\" style=\"border-collapse:collapse;\">\n<tr><td align=\"center\">\n")
                // width="640" for Outlook, which lays tables out with Word and ignores
                // max-width; margin:0 auto for everything else, because align="center"
                // on the cell centres inline content and a table is not inline - without
                // it the whole message sat against one edge of the window.
                .Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" ")
                .Append("width=\"640\" style=\"border-collapse:collapse;width:100%;max-width:640px;")
                .Append("margin:0 auto;")
                .Append(BodyFont(report)).Append("\">\n");

            foreach (Section section in report.Sections)
            {
                Header? header = section.Childs.OfType<Header>().FirstOrDefault();
                Body? body = section.Childs.OfType<Body>().FirstOrDefault();
                Footer? footer = section.Childs.OfType<Footer>().FirstOrDefault();

                // How wide the sheet's own band is, so a width in millimetres can be
                // written as the fraction of the message it was a fraction of.
                EdgeSize pad = body?.Style.Padding ?? new EdgeSize();
                double band = Math.Max(1, section.Dimension.Width - pad.Left - pad.Right);

                if (header != null) WriteChildren(html, header, report, band);
                if (body != null) WriteChildren(html, body, report, band);
                if (footer != null) WriteChildren(html, footer, report, band);
            }

            html.Append("</table>\n</td></tr>\n</table>\n</body>\n</html>\n");
            return html.ToString();
        }

        private static void WriteChildren(StringBuilder html, ContainerItem container, Report report, double band)
        {
            foreach (BaseItem child in container.Childs)
            {
                if (child.Style.Visibility != Visibility.Visible)
                    continue;

                html.Append("<tr><td>\n");
                WriteItem(html, child, report, band);
                html.Append("</td></tr>\n");
            }
        }

        private static void WriteItem(StringBuilder html, BaseItem item, Report report, double band)
        {
            switch (item)
            {
                case AdvancedTextItem advanced:
                    WriteAdvanced(html, advanced, report);
                    break;

                case TextItem text:
                    html.Append("<p style=\"margin:0 0 .6em;")
                        .Append(TextCss(text.Font, report)).Append("\">")
                        .Append(Escape(text.Text).Replace("\n", "<br>"))
                        .Append("</p>\n");
                    break;

                case ImageItem image:
                    WriteImage(html, image, band);
                    break;

                case LineItem:
                    html.Append("<hr style=\"border:none;border-top:1px solid #ccc;margin:.8em 0;\">\n");
                    break;

                case TableItem table:
                    WriteTable(html, table, report, band);
                    break;

                case ImageGridItem grid:
                    WriteGrid(html, grid, report, band);
                    break;

                // A control the reader could have filled in, drawn as what it holds.
                // Nothing here is interactive: an email cannot carry a form field, which
                // is what TemplateProfile.Capable reports through can.flattenedFields
                // before a single byte is written.
                case FieldItem field:
                    html.Append("<p style=\"margin:0 0 .6em;\">")
                        .Append(Escape(field.Value ?? field.Tooltip ?? ""))
                        .Append("</p>\n");
                    break;

                // Shown, not read: without a renderer there is no barcode to draw here,
                // and its alternative text is the honest stand-in - the same text a
                // screen reader would have been given.
                case BarcodeItem barcode:
                    if (!string.IsNullOrWhiteSpace(barcode.AltText))
                        html.Append("<p style=\"margin:0 0 .6em;color:#666;\">")
                            .Append(Escape(barcode.AltText!)).Append("</p>\n");
                    break;

                case ContainerItem container:
                    WriteContainer(html, container, report, band);
                    break;
            }
        }

        /// <summary>
        /// A container's children.
        ///
        /// Side by side where they were side by side, one under the next otherwise.
        ///
        /// The first version of this stacked everything absolutely laid out and left a
        /// rule to apologise for it, on the grounds that coordinates mean nothing without
        /// a page. That was too quick: a picture beside a paragraph has a perfectly good
        /// flowing form, and it is the oldest one there is - a row of cells. What has no
        /// flowing form is an arrangement whose pieces *overlap*, because a table cell
        /// cannot be half over its neighbour.
        ///
        /// So the question asked of a group is not "was it placed at coordinates" but
        /// "do its pieces sit beside one another", and <see cref="SideBySide"/> answers
        /// it for the validator too, so the warning and the output cannot disagree about
        /// which groups survive.
        /// </summary>
        private static void WriteContainer(StringBuilder html, ContainerItem container, Report report, double band)
        {
            List<(BaseItem Item, double Left, double Right)>? row =
                container.Layout == LayoutMode.Absolute ? SideBySide(container, band) : null;

            html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" ")
                .Append("width=\"100%\" style=\"border-collapse:collapse;\">\n");

            if (row != null)
            {
                html.Append("<tr>");
                foreach ((BaseItem child, double left, double right) in row)
                {
                    double share = band <= 0 ? 0 : Math.Round((right - left) / band * 100, 1);
                    // valign for Word, the style for everything else - and only one
                    // of each: writing the default and then the choice leaves both in
                    // the attribute, which reads as an argument the browser has to
                    // settle.
                    string down = VerticalIn(child);
                    html.Append("<td valign=\"").Append(down.Length > 0 ? WordValign(child) : "top")
                        .Append("\" style=\"")
                        .Append(down.Length > 0 ? down : "vertical-align:top;")
                        .Append(share > 0 ? $"width:{share.ToString(System.Globalization.CultureInfo.InvariantCulture)}%;" : "")
                        .Append("\">\n");
                    // The cell is now the band this child sits in, so anything measured
                    // against a width inside it is measured against that.
                    WriteItem(html, child, report, Math.Max(1, right - left));
                    html.Append("</td>");
                }
                html.Append("</tr>\n</table>\n");
                return;
            }

            foreach (BaseItem child in container.Childs)
            {
                if (child.Style.Visibility != Visibility.Visible)
                    continue;

                html.Append("<tr><td>\n");
                WriteItem(html, child, report, band);
                html.Append("</td></tr>\n");
            }

            html.Append("</table>\n");
        }

        /// <summary>
        /// The children of an absolutely laid out group, in reading order, when none of
        /// them overlaps another across the band - or null when they do.
        ///
        /// This is what decides whether a group survives being written as a message, and
        /// it is deliberately the same answer <c>can.absoluteInFlow</c> gives: a rule that
        /// warns about a group the emitter handles perfectly well is a rule people learn
        /// to ignore, and one that stays quiet about a group it mangles is worse.
        ///
        /// Two or more children required, and every one of them has to say where it is:
        /// a child with no horizontal position of its own spans the whole band by default,
        /// which overlaps everything.
        /// </summary>
        internal static List<(BaseItem Item, double Left, double Right)>? SideBySide(
            ContainerItem container, double band)
        {
            var spans = new List<(BaseItem Item, double Left, double Right)>();

            foreach (BaseItem child in container.Childs)
            {
                if (child.Style.Visibility != Visibility.Visible)
                    continue;

                FrameDimension loc = child.Style.Location;

                double left = (loc.Dock & Dock.Left) != 0 ? 0
                            : !double.IsNaN(loc.LeftValue) ? loc.LeftValue
                            : double.NaN;

                double right = (loc.Dock & Dock.Right) != 0 ? band
                             : !double.IsNaN(loc.RightValue) ? band - loc.RightValue
                             : double.NaN;

                double width = loc.WidthValue;

                if (double.IsNaN(left) && !double.IsNaN(right) && !double.IsNaN(width))
                    left = right - width;
                else if (!double.IsNaN(left) && double.IsNaN(right) && !double.IsNaN(width))
                    right = left + width;

                // Nothing said where it is across the band, so it takes all of it - and
                // an arrangement where something takes all of it is not a row.
                if (double.IsNaN(left) || double.IsNaN(right) || right <= left)
                    return null;

                spans.Add((child, left, right));
            }

            if (spans.Count < 2)
                return null;

            spans.Sort((a, b) => a.Left.CompareTo(b.Left));

            for (int i = 1; i < spans.Count; i++)
            {
                // A hair of tolerance: two cells that touch are still two cells, and the
                // millimetre arithmetic that produced them rarely lands on the bit.
                if (spans[i].Left < spans[i - 1].Right - 0.01)
                    return null;
            }

            return spans;
        }

        /// <summary>Where a cell's content sits down the row, when it was told.</summary>
        private static string VerticalIn(BaseItem item) => item.Style.AlignVertical switch
        {
            ItemAlign.Center => "vertical-align:middle;",
            ItemAlign.End => "vertical-align:bottom;",
            _ => ""
        };

        /// <summary>The same thing as an attribute, which is what Word reads.</summary>
        private static string WordValign(BaseItem item) => item.Style.AlignVertical switch
        {
            ItemAlign.Center => "middle",
            ItemAlign.End => "bottom",
            _ => "top"
        };

        private static void WriteAdvanced(StringBuilder html, AdvancedTextItem item, Report report)
        {
            html.Append("<p style=\"margin:0 0 .6em;")
                .Append(TextCss(item.Font, report))
                .Append(Align(item.Font.HorizontalAlignment))
                .Append("\">");

            foreach (TextRun run in item.Runs)
            {
                if (string.IsNullOrEmpty(run.Text))
                    continue;

                string body = Escape(run.Text).Replace("\n", "<br>");
                string css = run.Font == null ? "" : RunCss(run.Font, report);

                if (css.Length == 0) html.Append(body);
                else html.Append("<span style=\"").Append(css).Append("\">").Append(body).Append("</span>");
            }

            html.Append("</p>\n");
        }

        private static void WriteImage(StringBuilder html, ImageItem image, double band)
        {
            if (image.Source?.Data is not { Length: > 0 })
                return;

            // Encoded here, not interpolated. ImageSource.Data is a byte[] that the
            // serializer happens to write as base64, so it *looks* like a base64 string
            // everywhere a template is read - and appending it to a string gives
            // "System.Byte[]", which is a data: URI a browser accepts and draws nothing
            // for. Found by opening the file, which no assertion about its length would
            // have found.
            string encoded = Convert.ToBase64String(image.Source.Data);

            // Per cent rather than millimetres: the width the template asked for is a
            // fraction of a sheet that does not exist here. A picture wider than the
            // reader's window is the commonest way an email arrives broken, so the
            // ceiling is the important half of this.
            // The width the template asked for, as a fraction of the band it asked for
            // it in. Dropping it is what made a 60mm picture arrive as wide as the
            // message - the ceiling was there, the size was not, and "no wider than the
            // window" is not the same instruction as "this wide".
            double wanted = image.Style.Location.WidthValue;
            string width = double.IsNaN(wanted) || wanted <= 0 || band <= 0
                ? ""
                : $"width:{Math.Min(100, Math.Round(wanted / band * 100, 1))
                        .ToString(System.Globalization.CultureInfo.InvariantCulture)}%;";

            html.Append("<img src=\"data:")
                .Append(Escape(image.Source.MediaType ?? "image/png"))
                .Append(";base64,").Append(encoded)
                .Append("\" alt=\"").Append(Escape(image.AltText ?? ""))
                .Append("\" style=\"display:block;").Append(width).Append("max-width:100%;height:auto;")
                .Append(ImageAcross(image.Style.AlignHorizontal))
                .Append("\">\n");
        }

        private static void WriteTable(StringBuilder html, TableItem table, Report report, double band)
        {
            html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" ")
                .Append("width=\"100%\" style=\"border-collapse:collapse;margin:0 0 .8em;\">\n");

            foreach (BaseItem child in table.Childs)
            {
                if (child is not TableRow row || row.Style.Visibility != Visibility.Visible)
                    continue;

                html.Append("<tr>");
                foreach (BaseItem cell in row.Childs)
                {
                    string tag = row.IsHeader ? "th" : "td";
                    html.Append('<').Append(tag)
                        .Append(" style=\"padding:.3em .5em;vertical-align:top;")
                        .Append(row.IsHeader ? "text-align:left;border-bottom:1px solid #ddd;" : "")
                        .Append("\">");
                    WriteItem(html, cell, report, band);
                    html.Append("</").Append(tag).Append('>');
                }
                html.Append("</tr>\n");
            }

            html.Append("</table>\n");
        }

        private static void WriteGrid(StringBuilder html, ImageGridItem grid, Report report, double band)
        {
            int columns = Math.Max(1, grid.GridColumns);
            html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" ")
                .Append("width=\"100%\" style=\"border-collapse:collapse;\">\n");

            int at = 0;
            foreach (BaseItem child in grid.Childs)
            {
                if (child.Style.Visibility != Visibility.Visible)
                    continue;

                if (at % columns == 0) html.Append("<tr>");
                html.Append("<td style=\"padding:2px;width:")
                    .Append((100 / columns).ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append("%;\">");
                WriteItem(html, child, report, band);
                html.Append("</td>");
                if (at % columns == columns - 1) html.Append("</tr>\n");
                at++;
            }

            if (at % columns != 0) html.Append("</tr>\n");
            html.Append("</table>\n");
        }

        // ------------------------------------------------------------------
        // Styling. Inline, always: a <style> block is discarded by enough clients
        // that relying on one is relying on luck.
        // ------------------------------------------------------------------

        /// <summary>
        /// The document's own face, as a stack the reader can actually satisfy.
        ///
        /// Nothing is embedded here, so naming a family the engine happens to hold means
        /// nothing to whoever opens the message: the family is a request, and the
        /// fallbacks are what it is answered with. That is the opposite of the PDF's
        /// rule, where a family the engine has no file for is refused outright.
        /// </summary>
        private static string BodyFont(Report report)
        {
            string family = report.DefaultFont?.Family ?? "";
            string stack = string.IsNullOrWhiteSpace(family)
                ? "Helvetica, Arial, sans-serif"
                : $"'{family}', Helvetica, Arial, sans-serif";

            return $"font-family:{stack};font-size:15px;line-height:1.5;color:#111;";
        }

        private static string TextCss(TextStyle? style, Report report)
        {
            if (style == null) return "";
            var css = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(style.Family))
                css.Append("font-family:'").Append(Escape(style.Family)).Append("', Helvetica, Arial, sans-serif;");

            if (style.Bold == true) css.Append("font-weight:bold;");
            if (style.Italic == true) css.Append("font-style:italic;");

            return css.ToString();
        }

        private static string RunCss(TextStyle style, Report report)
        {
            var css = new StringBuilder(TextCss(style, report));

            var decorations = new List<string>();
            if (style.Underline == true) decorations.Add("underline");
            if (style.Strikethrough == true) decorations.Add("line-through");
            if (decorations.Count > 0)
                css.Append("text-decoration:").Append(string.Join(' ', decorations)).Append(';');

            if (style.Color is { } colour && !(colour.R == 0 && colour.G == 0 && colour.B == 0))
                css.Append("color:#").Append($"{colour.R:x2}{colour.G:x2}{colour.B:x2}").Append(';');

            // A size in the template is a cap height in millimetres or a multiple of the
            // root size; neither means anything to a screen. It becomes a proportion of
            // the reader's own text, which is the only honest translation.
            if (style.Size is { } size && Math.Abs(size - 1) > 0.001)
                css.Append("font-size:")
                   .Append((size).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
                   .Append("em;");

            return css.ToString();
        }

        private static string Align(HorizontalAlignment? how) => how switch
        {
            HorizontalAlignment.Center => "text-align:center;",
            HorizontalAlignment.Right => "text-align:right;",
            HorizontalAlignment.Justify => "text-align:justify;",
            _ => ""
        };

        private static string ImageAcross(ItemAlign how) => how switch
        {
            ItemAlign.Center => "margin-left:auto;margin-right:auto;",
            ItemAlign.End => "margin-left:auto;",
            _ => ""
        };

        private static string Escape(string text)
        {
            var escaped = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '&': escaped.Append("&amp;"); break;
                    case '<': escaped.Append("&lt;"); break;
                    case '>': escaped.Append("&gt;"); break;
                    case '"': escaped.Append("&quot;"); break;
                    default: escaped.Append(c); break;
                }
            }
            return escaped.ToString();
        }
    }
}
