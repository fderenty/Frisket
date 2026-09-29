using System.Globalization;
using System.Text;

namespace Frisket
{
    /// <summary>
    /// One widget waiting to be written: what it is, where it is, and on which page.
    /// Collected while drawing and emitted once, because an AcroForm is a single list
    /// hanging off the catalog while the widgets themselves belong to pages.
    /// </summary>
    internal sealed class PdfField
    {
        public required FieldItem Item { get; init; }
        public required string Name { get; init; }
        public required Rect Frame { get; init; }
        public required TextStyle Font { get; init; }
        public required int Page { get; init; }

        /// <summary>Filled in when the widget object is created.</summary>
        public int ObjectNumber { get; set; }

        /// <summary>
        /// Key into the structure tree's number tree, so a reader can go from the
        /// control back to what it is. Negative when the document is not tagged.
        /// </summary>
        public int StructParent { get; set; } = -1;
    }

    /// <summary>
    /// Writes the interactive part of a PDF: the field widgets and the form that owns
    /// them.
    ///
    /// Appearance streams are drawn here rather than left to the reader. PDF/A forbids
    /// NeedAppearances outright, and a section whose fields only appear once a
    /// particular viewer has regenerated them is not a section you can archive - or
    /// print.
    /// </summary>
    internal static class PdfForm
    {
        private const double ToPoints = 72.0 / 25.4;

        /// <summary>
        /// Emits every widget and returns what the catalog needs to say. Field names are
        /// made unique as they go: a prototype repeated over a collection would otherwise
        /// leave every copy answering to the same name.
        /// </summary>
        /// <param name="pageHeights">
        /// The height of every page, in the order they were drawn - not one height for
        /// the whole file.
        ///
        /// It WAS one height, the backend's <c>_pageHeight</c>, which holds whatever the
        /// last page left in it by the time the file is assembled. A widget on an A4 page
        /// in a report whose last section is A5 was written 149 mm below the box that was
        /// laid out, and with a 420 mm last section entirely off the sheet it belongs to.
        /// Everything else about it resolved - <c>/P</c>, <c>/Annots</c>, <c>/Fields</c>,
        /// <c>/AP</c> - so every assertion about the parts reaching each other stayed
        /// green, and veraPDF passes a misplaced annotation because it is valid PDF.
        ///
        /// This is the fault CLAUDE.md section 16 records for <c>/MediaBox</c>, in the
        /// same file, fixed there and never carried the three lines to here.
        /// </param>
        public static string Emit(PdfWriter pdf, List<PdfField> fields, int[] pageObjects,
                                  int formFont, string formFontName,
                                  IReadOnlyList<double> pageHeights)
        {
            if (fields.Count == 0)
                return string.Empty;

            // Every name an author wrote, claimed before a single one is invented. An
            // invented name that takes one a later field asked for BY NAME does not
            // collide - the loop below sees to that - it renames the author's field, and
            // a form back end then receives the value under a key that is in no template.
            var claimed = new HashSet<string>(
                fields.Select(f => string.IsNullOrWhiteSpace(f.Name) ? "field" : f.Name),
                StringComparer.Ordinal);

            var taken = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (PdfField field in fields)
            {
                string name = Unique(field.Name, taken, claimed);
                if (field.ObjectNumber == 0)
                    field.ObjectNumber = pdf.Reserve();
                double pageHeight = field.Page >= 0 && field.Page < pageHeights.Count
                    ? pageHeights[field.Page]
                    : pageHeights.Count > 0 ? pageHeights[^1] : 297;

                pdf.Set(field.ObjectNumber, Widget(pdf, field, name, pageObjects[field.Page], formFontName, pageHeight));
            }

            string list = string.Join(" ", fields.Select(f => $"{f.ObjectNumber} 0 R"));

            // SignaturesExist, and it alone. A reader consults this before deciding
            // whether the document has anything to sign at all: without it, Acrobat
            // shows the box and offers no way to sign it, which is the same document
            // looking the same and doing nothing - the shape of fault this whole area
            // keeps producing.
            //
            // Not AppendOnly (2) beside it, which says the file holds signatures that a
            // full rewrite would invalidate. There are none yet: every signature field
            // written here is empty, waiting for someone. Claiming it would be claiming
            // something about a signature that does not exist.
            string signatures = fields.Any(f => f.Item.Kind == FieldKind.Signature)
                ? " /SigFlags 1"
                : string.Empty;

            // A default appearance is required even though every field carries its own:
            // it is what a reader uses if the value is edited.
            return $" /AcroForm << /Fields [{list}]{signatures} /DA (/{formFontName} 9 Tf 0 g) " +
                   $"/DR << /Font << /{formFontName} {formFont} 0 R >> >> >>";
        }

        /// <summary>
        /// Claims each widget's object number ahead of time. A page has to name its
        /// widgets under /Annots, and pages are written before the widgets themselves;
        /// without this the page pointed at object 0 for every one of them, and a reader
        /// following /Annots found nothing at all - no field to fill, and nothing drawn.
        /// </summary>
        public static void Reserve(PdfWriter pdf, List<PdfField> fields)
        {
            foreach (PdfField field in fields)
                field.ObjectNumber = pdf.Reserve();
        }

        /// <summary>The widgets each page has to list under /Annots.</summary>
        public static string Annotations(List<PdfField> fields, int page)
        {
            var mine = fields.Where(f => f.Page == page).ToList();
            if (mine.Count == 0)
                return string.Empty;

            // "0 0 R" is a reference to nothing, and a reader following it finds nothing:
            // it reads as a page with no widgets rather than as a broken file, so it fails
            // silently and looks exactly like a form that was never there.
            if (mine.Any(f => f.ObjectNumber == 0))
                throw new InvalidOperationException("Widget object numbers must be reserved before the page is written.");

            return $" /Annots [{string.Join(" ", mine.Select(f => $"{f.ObjectNumber} 0 R"))}]";
        }

        /// <summary>
        /// A name no other widget in this file is using.
        ///
        /// Two widgets sharing one name are not two fields in a PDF: they are one field
        /// with two appearances, so filling either fills both. That is the point of this
        /// method, and it used to hand out a derived name without recording it - so a
        /// field named for one the engine had already invented was given it a second
        /// time, and the very collision being avoided came back through the door the fix
        /// walked out of. Hence the loop, and hence recording every name handed out
        /// rather than only the ones asked for.
        ///
        /// And hence <paramref name="claimed"/>. Not colliding is half the job: an
        /// invented name that a later field asks for by name must not be invented at
        /// all, or that field is the one renamed - the engine quietly taking a name its
        /// author chose and handing it to a duplicate.
        ///
        /// The separator is not a period: a period separates a parent field from its
        /// child (ISO 32000-1, 12.7.3.2), so "a.1" claims a field "a" with a child "1" -
        /// a hierarchy nothing here writes, hung under a parent that is also a terminal
        /// field. Author-supplied names are left exactly as they are; this is only about
        /// what the engine itself invents.
        /// </summary>
        private static string Unique(string name, Dictionary<string, int> taken,
                                     HashSet<string> claimed)
        {
            if (string.IsNullOrWhiteSpace(name))
                name = "field";

            if (taken.TryAdd(name, 1))
                return name;

            for (int seen = taken[name] + 1; ; seen++)
            {
                string derived = $"{name}-{seen}";
                if (claimed.Contains(derived) || !taken.TryAdd(derived, 1))
                    continue;

                taken[name] = seen;
                return derived;
            }
        }

        private static string Widget(PdfWriter pdf, PdfField field, string name, int page,
                                     string formFontName, double pageHeight)
        {
            FieldItem item = field.Item;

            // PDF measures from the bottom of the page; the engine from the top.
            Rect box = field.Frame;
            double bottom = (pageHeight - box.Bottom) * ToPoints;
            double top = (pageHeight - box.Top) * ToPoints;
            double left = box.Left * ToPoints;
            double right = box.Right * ToPoints;

            double width = right - left;
            double height = top - bottom;

            int flags = 0;
            if (item.ReadOnly) flags |= 1;
            if (item.Required) flags |= 2;
            if (item.Kind == FieldKind.Text && item.Multiline) flags |= 1 << 12;

            var sb = new StringBuilder();
            sb.Append("<< /Type /Annot /Subtype /Widget /P ").Append(page).Append(" 0 R")
              .Append(" /T ").Append(PdfWriter.Text(name))
              .Append(" /Rect [").Append(PdfWriter.N(left)).Append(' ').Append(PdfWriter.N(bottom)).Append(' ')
              .Append(PdfWriter.N(right)).Append(' ').Append(PdfWriter.N(top)).Append(']')
              .Append(" /F 4");                                  // print, as PDF/A requires

            // Which structure element owns this control. Without it a checker finds a
            // widget belonging to nothing, and a screen reader announces a box with no
            // idea what it is for.
            if (field.StructParent >= 0)
                sb.Append(" /StructParent ").Append(field.StructParent);

            if (flags != 0)
                sb.Append(" /Ff ").Append(flags);

            if (!string.IsNullOrEmpty(item.Tooltip))
                sb.Append(" /TU ").Append(PdfWriter.Text(item.Tooltip));

            sb.Append(Appearance(item, field.Font, formFontName));

            // Some readers - Edge and Chrome among them - take a form field over and draw
            // their own control rather than the appearance stream supplied here. They read
            // its border from /MK and /BS, so a widget that names neither comes out as a
            // plain opaque box: no border of its own, and hiding whatever the page drew
            // underneath. Both keys carry the field's own outline.
            if (item.Outline != null)
            {
                sb.Append(" /MK << /BC [").Append(Grey(item.Outline.Color)).Append("] >>")
                  .Append(" /BS << /W ").Append(PdfWriter.N(item.Outline.Width * ToPoints))
                  .Append(" /S /S >>");
            }

            switch (item.Kind)
            {
                case FieldKind.Checkbox:
                {
                    int off = pdf.AddStream(Form(width, height), Latin1(TickBox(item, field.Font, width, height, false)));
                    int on = pdf.AddStream(Form(width, height), Latin1(TickBox(item, field.Font, width, height, true)));
                    string state = item.IsChecked ? "/Yes" : "/Off";

                    sb.Append(" /FT /Btn")
                      .Append(" /V ").Append(state).Append(" /AS ").Append(state)
                      .Append($" /AP << /N << /Yes {on} 0 R /Off {off} 0 R >> >>");
                    break;
                }

                case FieldKind.Signature:
                    // Left unsigned on purpose. It prints as an empty box to sign by hand,
                    // and a reader can still sign it electronically later.
                    sb.Append(" /FT /Sig")
                      .Append($" /AP << /N {pdf.AddStream(Form(width, height), Latin1(SignatureBox(item, width, height)))} 0 R >>");
                    break;

                default:
                {
                    int appearance = pdf.AddStream(Form(width, height),
                                                   Latin1(TextBox(item, field.Font, width, height)));

                    sb.Append(" /FT /Tx");
                    if (item.MaxLength > 0)
                        sb.Append(" /MaxLen ").Append(item.MaxLength);
                    if (!string.IsNullOrEmpty(item.Value))
                        sb.Append(" /V ").Append(PdfWriter.Text(item.Value));
                    sb.Append($" /AP << /N {appearance} 0 R >>");
                    break;
                }
            }

            sb.Append(" >>");
            return sb.ToString();
        }

        /// <summary>The default appearance string, in points and greyscale.</summary>
        private static string Appearance(FieldItem item, TextStyle font, string formFontName)
        {
            Color ink = font.Color ?? Color.Black;
            double size = font.SizeUnit == FontSizeUnit.Point ? font.Size ?? 9 : 9;

            // The font named here must be in /DR, and everything in /DR is embedded -
            // PDF/A allows nothing else.
            return $" /DA (/{formFontName} {PdfWriter.N(Math.Max(6, Math.Min(size, 24)))} Tf " +
                   $"{PdfWriter.N(ink.R / 255.0)} {PdfWriter.N(ink.G / 255.0)} {PdfWriter.N(ink.B / 255.0)} rg)";
        }

        /// <summary>
        /// An appearance stream holds the box and nothing else - no text, so no font
        /// resource. The value itself is drawn by the page, which is what makes a
        /// flattened section and an interactive one read identically.
        /// </summary>
        private static string Form(double width, double height)
            => $"/Type /XObject /Subtype /Form /BBox [0 0 {PdfWriter.N(width)} {PdfWriter.N(height)}] "
               + "/Resources << /ProcSet [/PDF] >>";

        /// <summary>The box, and the tick when it is ticked. Drawn, not lifted from a font.</summary>
        private static string TickBox(FieldItem item, TextStyle font, double width, double height, bool ticked)
        {
            var sb = new StringBuilder();
            double inset = 0.5;

            if (item.Fill != null)
            {
                sb.Append(Grey(item.Fill.Color)).Append(" rg 0 0 ")
                  .Append(PdfWriter.N(width)).Append(' ').Append(PdfWriter.N(height)).Append(" re f\n");
            }

            if (item.Outline != null)
            {
                sb.Append(Grey(item.Outline.Color)).Append(" RG ")
                  .Append(PdfWriter.N(item.Outline.Width * ToPoints)).Append(" w ")
                  .Append(PdfWriter.N(inset)).Append(' ').Append(PdfWriter.N(inset)).Append(' ')
                  .Append(PdfWriter.N(width - 2 * inset)).Append(' ').Append(PdfWriter.N(height - 2 * inset))
                  .Append(" re S\n");
            }

            if (!ticked)
                return sb.ToString();

            Color ink = font.Color ?? Color.Black;
            sb.Append(Grey(ink)).Append(" RG ")
              .Append(PdfWriter.N(Math.Max(0.6, width * 0.12))).Append(" w 1 J 1 j\n")
              .Append(PdfWriter.N(width * 0.22)).Append(' ').Append(PdfWriter.N(height * 0.48)).Append(" m ")
              .Append(PdfWriter.N(width * 0.42)).Append(' ').Append(PdfWriter.N(height * 0.26)).Append(" l ")
              .Append(PdfWriter.N(width * 0.80)).Append(' ').Append(PdfWriter.N(height * 0.74)).Append(" l S\n");

            return sb.ToString();
        }

        private static string SignatureBox(FieldItem item, double width, double height)
        {
            var sb = new StringBuilder();

            if (item.Fill != null)
                sb.Append(Grey(item.Fill.Color)).Append(" rg 0 0 ")
                  .Append(PdfWriter.N(width)).Append(' ').Append(PdfWriter.N(height)).Append(" re f\n");

            if (item.Outline != null)
                sb.Append(Grey(item.Outline.Color)).Append(" RG ")
                  .Append(PdfWriter.N(item.Outline.Width * ToPoints)).Append(" w 0.5 0.5 ")
                  .Append(PdfWriter.N(width - 1)).Append(' ').Append(PdfWriter.N(height - 1)).Append(" re S\n");

            // The rule to sign on.
            sb.Append("0.6 0.6 0.6 RG 0.4 w ")
              .Append("4 ").Append(PdfWriter.N(height * 0.28)).Append(" m ")
              .Append(PdfWriter.N(width - 4)).Append(' ').Append(PdfWriter.N(height * 0.28)).Append(" l S\n");

            return sb.ToString();
        }

        /// <summary>
        /// A text field's appearance holds only its box: the value is drawn by the page,
        /// so that a flattened section and an interactive one read identically.
        /// </summary>
        private static string TextBox(FieldItem item, TextStyle font, double width, double height)
        {
            var sb = new StringBuilder();

            if (item.Fill != null)
                sb.Append(Grey(item.Fill.Color)).Append(" rg 0 0 ")
                  .Append(PdfWriter.N(width)).Append(' ').Append(PdfWriter.N(height)).Append(" re f\n");

            if (item.Outline != null)
                sb.Append(Grey(item.Outline.Color)).Append(" RG ")
                  .Append(PdfWriter.N(item.Outline.Width * ToPoints)).Append(" w 0.5 0.5 ")
                  .Append(PdfWriter.N(width - 1)).Append(' ').Append(PdfWriter.N(height - 1)).Append(" re S\n");

            return sb.ToString();
        }

        private static string Grey(Color c)
            => $"{PdfWriter.N(c.R / 255.0)} {PdfWriter.N(c.G / 255.0)} {PdfWriter.N(c.B / 255.0)}";

        private static byte[] Latin1(string text) => PdfWriter.Latin1(text);
    }
}
