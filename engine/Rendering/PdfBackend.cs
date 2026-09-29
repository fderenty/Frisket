using System.Globalization;
using System.Text;

namespace Frisket
{
    /// <summary>
    /// Writes a PDF, with the very fonts the layout was measured with embedded in it.
    /// No dependency: the container is written by hand and Deflate comes from the
    /// framework.
    ///
    /// Fonts go in as Type0/Identity-H with the TrueType file embedded whole, which is
    /// what makes accented text and the euro sign come out right anywhere, and a
    /// ToUnicode map so the text stays selectable and searchable. Subsetting is not done:
    /// the file carries each font in full.
    ///
    /// Coordinates are handed over in millimetres from the top-left, like everywhere else
    /// in the engine; the page matrix converts to PDF's points-from-the-bottom once.
    /// </summary>
    public sealed class PdfBackend : IReportBackend
    {
        private const double MillimetresToPoints = 72.0 / 25.4;

        private sealed class EmbeddedFont
        {
            public required FontFile File { get; init; }
            public required string Name { get; init; }              // /F1, /F2...
            public SortedDictionary<ushort, int> Glyphs { get; } = new();   // glyph id -> code point
            public int ObjectNumber { get; set; }
        }

        private sealed class EmbeddedImage
        {
            public required string Name { get; init; }              // /Im1, /Im2...
            public int ObjectNumber { get; set; }
        }

        private readonly PdfWriter _pdf = new();
        private readonly Dictionary<string, EmbeddedFont> _fonts = new();
        private readonly Dictionary<string, EmbeddedImage> _images = new();
        private readonly List<int> _pageObjects = new();
        private readonly List<string> _pageBodies = new();

        /// <summary>
        /// Each page's own sheet size, kept beside its body. /MediaBox belongs to the
        /// page object in PDF, not to the file - and the loop that writes them runs after
        /// every page has been drawn, so reading the two current-page fields there gave
        /// every page the size of the last one. Invisible while a report had one page
        /// size; wrong the moment it has two.
        /// </summary>
        private readonly List<(double Width, double Height)> _pageSizes = new();

        private StringBuilder _content = new();
        private double _pageHeight = 297;
        private double _pageWidth = 210;
        private string _title = string.Empty;
        private DateTimeOffset _now = DateTimeOffset.Now;

        public ITextMeasurer Measurer { get; }

        /// <summary>Compress page content streams. Off makes the output readable while debugging.</summary>
        public bool Compress { get; set; } = true;

        /// <summary>
        /// How strictly the file should conform. <see cref="PdfConformance.PdfA3b"/> adds
        /// the colour profile, the XMP packet and the file identifier an archivable
        /// section needs - and is what Factur-X is built on.
        /// </summary>
        public PdfConformance Conformance { get; set; } = PdfConformance.None;

        /// <summary>
        /// Overrides what the report says about itself. Left null, the document's own
        /// Author, Subject and Keywords are used - which is where they belong, since a
        /// template carries them and the data can fill them in.
        /// </summary>
        public string? Author { get; set; }

        public string? Subject { get; set; }
        public string? Keywords { get; set; }

        /// <summary>Overrides the document's language tag.</summary>
        public string? Language { get; set; }

        private string? _author, _subject, _keywords, _language;

        private readonly List<PdfField> _fields = new();

        /// <summary>
        /// Write the fields as a real form. Turn it off to flatten: the values are drawn
        /// exactly the same either way, so a flattened section and an interactive one
        /// read identically - only the ability to change them goes.
        /// </summary>
        public bool Interactive { get; set; } = true;

        /// <summary>
        /// Also draw an interactive field's box onto the page, under its widget. Off by
        /// default: a reader draws its own chrome around a field, and a second box just
        /// beside it reads as a misprint.
        ///
        /// Worth turning on when the section will be rasterised or flattened by
        /// something that does not carry annotations over, and only the page is left. If
        /// that is the intent from the start, <see cref="Interactive"/> set to false is
        /// the better answer: it draws the boxes and the values both, and needs no reader
        /// to cooperate. Ignored when Interactive is off, since the box is drawn anyway.
        /// </summary>
        public bool FieldBackdrop { get; set; }

        private readonly SortedSet<int> _missingGlyphs = new();

        /// <summary>
        /// Code points the fonts had no glyph for. They would be drawn as .notdef, an
        /// empty box - which PDF/A forbids outright, and which is a defect in any
        /// section. <see cref="ToArray"/> refuses to write an archivable file that
        /// contains one.
        /// </summary>
        public IReadOnlyCollection<int> MissingGlyphs => _missingGlyphs;

        private readonly List<PdfAttachment> _attachments = new();
        private string? _facturXFileName;
        private string? _facturXProfile;

        /// <summary>Carries a file inside the section. PDF/A-3 is what allows this.</summary>
        public void Attach(PdfAttachment attachment)
            => _attachments.Add(attachment ?? throw new ArgumentNullException(nameof(attachment)));

        /// <summary>
        /// Attaches the machine-readable invoice of a Factur-X section and declares it in
        /// the metadata. The conformance is set to PDF/A-3 at the same time: a hybrid
        /// invoice that is not archivable is not a Factur-X invoice.
        /// </summary>
        public void AttachFacturX(byte[] xml, string profile = "EN 16931", string fileName = "factur-x.xml")
        {
            Attach(new PdfAttachment
            {
                Name = fileName,
                Data = xml,
                MediaType = "text/xml",
                Relationship = "Data",
                Description = "Factur-X invoice"
            });

            _facturXFileName = fileName;
            _facturXProfile = profile;
            Conformance = PdfConformance.PdfA3b;
        }

        public PdfBackend(FontLibrary fonts)
            : this(new FontTextMeasurer(fonts ?? throw new ArgumentNullException(nameof(fonts)))) { }

        /// <summary>
        /// The measurer must be able to hand over the font files, so that what is
        /// embedded is exactly what was measured. Anything else and the text would be
        /// laid out with one set of widths and drawn with another.
        /// </summary>
        public PdfBackend(FontTextMeasurer measurer)
            => Measurer = measurer ?? throw new ArgumentNullException(nameof(measurer));

        private FontTextMeasurer Fonts => (FontTextMeasurer)Measurer;

        // ------------------------------------------------------------------

        public void BeginDocument(Report report)
        {
            _title = report.Title ?? string.Empty;
            _author = Author ?? report.Author;
            _subject = Subject ?? report.Subject;
            _keywords = Keywords ?? report.Keywords;
            _language = Language ?? report.Language;
            _now = report.GeneratedAt ?? DateTimeOffset.Now;
        }

        /// <summary>
        /// One tagged element: what it is, which page carries it, and the mark that ties
        /// the tag to the drawing.
        /// </summary>
        private sealed record Marked(BaseItem Item, string Tag, string? Alt, string? Lang,
                                    int Page, int Mcid)
        {
            /// <summary>Index into the field list, when this element wraps a control.</summary>
            public int Field { get; set; } = -1;

            /// <summary>
            /// The element that owns this one, named outright rather than found by
            /// walking up the item tree.
            ///
            /// Ownership is normally read off the items: an element belongs to the
            /// nearest ancestor of its item that was also tagged. That works because one
            /// item makes one element - and a control inside a table cell is the case
            /// where it does not. The cell must stay a cell (a row may hold nothing but
            /// TH and TD) and the widget must sit in a Form (7.18.4), so one item makes
            /// two elements, and the second cannot be found by looking at the item.
            /// </summary>
            public int Owner { get; init; } = -1;
        }

        private readonly List<Marked> _marked = new();
        private int _mcid;
        private bool _inElement;
        private int _openForm = -1;

        /// <summary>
        /// Write the structure a reader that cannot see the page needs: what each piece
        /// of content is, in reading order, with decoration set aside. Required by
        /// PDF/UA, and by the "a" levels of PDF/A.
        /// </summary>
        public bool Tagged { get; set; }

        public void BeginElement(BaseItem item, Rect frame)
        {
            if (!Tagged || _inElement)
                return;

            PdfRole role = PdfRoles.Resolve(item);

            // The tree and the drawing must answer ONE question - is a widget coming? -
            // and the role must follow the answer in both directions.
            //
            // A widget with no Form over it is refused as surely as a Form with no widget
            // under it: PDF/UA 7.18.1 wants every annotation in the tree, 7.18.4 wants a
            // widget nested in a Form. A field used as a table cell reached exactly that
            // state - PdfRoles.Resolve answers TableCell for anything whose parent is a
            // TableRow, before it ever looks at FieldItem - so the widget was written,
            // listed in /Annots and in /AcroForm /Fields, and pointed at by nothing.
            //
            // The other way round, three ways to open a Form no widget comes for: the
            // output is flattened; the item is a signature to be given by hand; or it is
            // not a field at all and an authored Role = Form walked past the guard
            // PdfRoles.Resolve already has, because an authored role is taken first.
            // It becomes what it is - something drawn - and a drawing is a Figure, which
            // needs AltText like any other.
            bool cell = role is PdfRole.TableCell or PdfRole.TableHeader;

            if (WillHaveWidget(item, Interactive, frame) && !cell)
                role = PdfRole.Form;
            else if (role == PdfRole.Form)
                role = PdfRole.Figure;

            string? altText = item.AltText;

            // Only where it says something. A tag equal to the file's own would put a
            // /Lang on the element that changes nothing and hides the ones that do -
            // and the comparison is on the tag as written, deliberately: "en-GB" and
            // "en" are not the same claim, and it is not this method's business to
            // decide that one implies the other.
            string? language =
                !string.IsNullOrWhiteSpace(item.Language)
                && !string.Equals(item.Language, _language, StringComparison.OrdinalIgnoreCase)
                    ? item.Language
                    : null;

            _inElement = true;

            // Decoration is marked as such rather than tagged. Assistive software skips
            // an artifact, which is the whole point: a tiled watermark read out
            // eighty-seven times is worse than no watermark at all.
            if (role == PdfRole.Artifact)
            {
                _content.Append("/Artifact BMC\n");
                return;
            }

            string tag = PdfRoles.Tag(role);

            // An interactive control is itself the content: the structure element points
            // at the widget, and whatever the page draws underneath is the box around it.
            // Marked as content it would give the Form a second child, which the standard
            // allows only with a Role attribute - and it would be describing chrome.
            // Form now implies a widget is coming: the demotion above has already
            // taken every case where one is not.
            if (role == PdfRole.Form)
            {
                _marked.Add(new Marked(item, tag, altText, language, _pageObjects.Count, -1));
                _openForm = _marked.Count - 1;
                _content.Append("/Artifact BMC\n");
                return;
            }

            // A control used as a table cell has to be both, and cannot be either alone.
            // Tagged as a cell, its widget is reachable from nothing - PDF/UA 7.18.1
            // wants every annotation in the tree, 7.18.4 wants it nested in a Form.
            // Tagged as a Form, veraPDF answers "TR element may contain only TH and TD
            // elements", and the row is then one cell short of the header, which is a
            // second rule broken by the first fix.
            //
            // Both were tried, in that order, on the conformance document; this is what
            // the validator was asking for. One item, two elements: the cell keeps its
            // place in the row, and the Form under it takes the widget.
            if (cell && WillHaveWidget(item, Interactive, frame))
            {
                _marked.Add(new Marked(item, tag, null, language, _pageObjects.Count, -1));
                int owner = _marked.Count - 1;

                _marked.Add(new Marked(item, PdfRoles.Tag(PdfRole.Form), altText, null,
                                       _pageObjects.Count, -1) { Owner = owner });
                _openForm = _marked.Count - 1;
                _content.Append("/Artifact BMC\n");
                return;
            }

            _marked.Add(new Marked(item, tag, altText, language, _pageObjects.Count, _mcid));
            _openForm = -1;
            _content.Append('/').Append(tag).Append(" <</MCID ").Append(_mcid).Append(">> BDC\n");
            _mcid++;
        }

        public void EndElement()
        {
            if (!Tagged || !_inElement)
                return;

            _content.Append("EMC\n");
            _inElement = false;
            _openForm = -1;
        }

        /// <summary>
        /// Writes the structure tree and returns its root, or zero when nothing was
        /// tagged.
        ///
        /// Two things have to agree: the tree, which says what each element is, and the
        /// number tree, which maps a page's marks back to those elements. A reader uses
        /// the second to answer "what is under my cursor", so a tree that is right and a
        /// map that is wrong reads as a section with no structure at all.
        /// </summary>
        private int EmitStructure()
        {
            if (!Tagged || _marked.Count == 0)
                return 0;

            int root = _pdf.Reserve();
            int section = _pdf.Reserve();

            var elements = new int[_marked.Count];
            for (int i = 0; i < _marked.Count; i++)
                elements[i] = _pdf.Reserve();

            // Who owns whom. An element belongs to the nearest ancestor of its item that
            // was also tagged, on the same page - a container split over two pages has
            // one element per page, and a row must join the one it was drawn on.
            var owner = new Dictionary<(BaseItem, int), int>();
            for (int i = 0; i < _marked.Count; i++)
            {
                // An element that names its owner is a second element for an item that
                // already has one - it is not what that item resolves to, and registering
                // it here would hand the item's children to the wrong half of the pair.
                if (_marked[i].Owner < 0)
                    owner[(_marked[i].Item, _marked[i].Page)] = i;
            }

            var parentOf = new int[_marked.Count];
            var children = new List<int>[_marked.Count];
            var roots = new List<int>();

            for (int i = 0; i < _marked.Count; i++)
            {
                if (_marked[i].Owner >= 0)
                {
                    parentOf[i] = _marked[i].Owner;
                    continue;
                }

                parentOf[i] = -1;
                for (BaseItem? up = _marked[i].Item.Parent; up != null; up = up.Parent)
                {
                    if (owner.TryGetValue((up, _marked[i].Page), out int found) && found != i)
                    {
                        parentOf[i] = found;
                        break;
                    }
                }

                if (parentOf[i] < 0)
                    roots.Add(i);
            }

            for (int i = 0; i < _marked.Count; i++)
            {
                if (parentOf[i] >= 0)
                    (children[parentOf[i]] ??= new List<int>()).Add(i);
            }

            for (int i = 0; i < _marked.Count; i++)
            {
                Marked mark = _marked[i];
                var sb = new StringBuilder("<< /Type /StructElem /S /").Append(mark.Tag)
                         .Append(" /P ").Append(parentOf[i] >= 0 ? elements[parentOf[i]] : section)
                         .Append(" 0 R");

                if (mark.Page < _pageObjects.Count)
                    sb.Append(" /Pg ").Append(_pageObjects[mark.Page]).Append(" 0 R");

                // A control is reached through an object reference, not a mark: there
                // are no glyphs of ours to point at, and a checker looks for exactly
                // this to decide the widget belongs to a Form.
                if (mark.Field >= 0 && mark.Field < _fields.Count)
                {
                    PdfField field = _fields[mark.Field];
                    field.StructParent = _pageObjects.Count + mark.Field;

                    sb.Append(" /K << /Type /OBJR /Obj ").Append(field.ObjectNumber).Append(" 0 R >>");
                }
                else if (children[i] is { Count: > 0 } inside)
                {
                    // A container's own marked content is usually empty - it draws a
                    // background at most - so what it holds is its children.
                    sb.Append(" /K [");
                    foreach (int kid in inside)
                        sb.Append(elements[kid]).Append(" 0 R ");
                    sb.Append(']');
                }
                else if (mark.Mcid >= 0)
                {
                    sb.Append(" /K ").Append(mark.Mcid);
                }

                // Nothing to point at: no widget, no children, and no mark of its own.
                // An element with no /K at all is a legal empty element; one saying
                // /K -1 points at a mark that is in no content stream, which veraPDF
                // reads as a broken tree.
                //
                // Unreachable today - -1 is written only for a Form, and a Form is now
                // opened only where a widget is coming, which sets Field just above.
                // That is a property of two other methods, and this one cannot see it:
                // the guard costs a comparison and survives their next change.

                // A heading cell says which way it heads, or a reader cannot tell whether
                // it labels the column below or the row beside it.
                if (mark.Tag == "TH")
                    sb.Append(" /A << /O /Table /Scope /Column >>");

                // What to announce in place of something that cannot be read.
                if (!string.IsNullOrWhiteSpace(mark.Alt))
                    sb.Append(" /Alt ").Append(PdfWriter.Text(mark.Alt));

                // And what language to announce it in. PDF/UA 7.2 asks that a passage in
                // a language other than the file's be marked, because a reader given
                // German words and English phonemes produces noise rather than an
                // accent. /Lang belongs to the file and may also sit on any element,
                // which is what makes a dossier of pieces in several languages sayable.
                if (!string.IsNullOrWhiteSpace(mark.Lang))
                    sb.Append(" /Lang ").Append(PdfWriter.Text(mark.Lang));

                _pdf.Set(elements[i], sb.Append(" >>").ToString());
            }

            string kids = string.Join(" ", roots.Select(i => elements[i] + " 0 R"));
            // /Document, and not the engine's word for a section: this is the PDF
            // structure type at the top of the tree, one of the standard set ISO 32000-1
            // defines. "Section" is not in that set - the standard division type is
            // /Sect - so writing it made every reader treat the whole tree as a
            // non-standard type with no role map, which PDF/UA 7.1 refuses.
            _pdf.Set(section, $"<< /Type /StructElem /S /Document /P {root} 0 R /K [{kids}] >>");

            // The number tree: for each page, its marks in order, so mark n on that page
            // is found at position n.
            //
            // Its marks, and nothing else. A Form owns no marked content - it is reached
            // through the OBJR under its own key below - and it carries Mcid = -1 for
            // exactly that reason. Listed here it took a slot no MCID claims and shifted
            // every element after it by one, so on any tagged interactive page holding a
            // field, the reader asking "what is under my cursor" over a heading was told
            // it was the signature box. The tree was right and the map was wrong, which
            // this method's own doc comment names as the thing it exists to prevent.
            var numbers = new StringBuilder();
            for (int page = 0; page < _pageObjects.Count; page++)
            {
                var onPage = new List<string>();
                for (int i = 0; i < _marked.Count; i++)
                    if (_marked[i].Page == page && _marked[i].Mcid >= 0)
                        onPage.Add(elements[i] + " 0 R");

                if (onPage.Count == 0)
                    continue;

                numbers.Append(page).Append(" [").Append(string.Join(" ", onPage)).Append("] ");
            }

            // Then one key per control, so a reader going from the widget finds the Form
            // that owns it. Keys must climb, hence the pages first.
            int next = _pageObjects.Count;
            for (int i = 0; i < _marked.Count; i++)
            {
                if (_marked[i].Field < 0)
                    continue;

                numbers.Append(next + _marked[i].Field).Append(' ')
                       .Append(elements[i]).Append(" 0 R ");
            }

            int parentTree = _pdf.Add($"<< /Nums [{numbers}] >>");
            _pdf.Set(root, $"<< /Type /StructTreeRoot /K {section} 0 R /ParentTree {parentTree} 0 R " +
                           $"/ParentTreeNextKey {next + _fields.Count} >>");
            return root;
        }

        private IReadOnlyList<OutlineEntry> _outline = Array.Empty<OutlineEntry>();

        public void SetOutline(IReadOnlyList<OutlineEntry> entries)
            => _outline = entries ?? Array.Empty<OutlineEntry>();

        /// <summary>
        /// Writes the outline as a tree of linked dictionaries and returns the root, or
        /// zero when there is nothing to write.
        ///
        /// Every entry needs its parent, its neighbours and its children, and each of
        /// those is an object number - so the numbers are all claimed first and the
        /// dictionaries filled in afterwards.
        /// </summary>
        private int EmitOutline()
        {
            if (_outline.Count == 0)
                return 0;

            int root = _pdf.Reserve();
            var objects = new int[_outline.Count];
            for (int i = 0; i < _outline.Count; i++)
                objects[i] = _pdf.Reserve();

            // A level deeper than one step below its predecessor is pulled back: an
            // outline with a hole in it is not a tree, and a reader shown one either
            // flattens it or gives up.
            var depth = new int[_outline.Count];
            for (int i = 0; i < _outline.Count; i++)
            {
                int asked = Math.Max(0, _outline[i].Level);
                depth[i] = i == 0 ? 0 : Math.Min(asked, depth[i - 1] + 1);
            }

            int Parent(int i)
            {
                for (int j = i - 1; j >= 0; j--)
                    if (depth[j] < depth[i])
                        return j;
                return -1;
            }

            bool Sibling(int a, int b) => depth[a] == depth[b] && Parent(a) == Parent(b);

            for (int i = 0; i < _outline.Count; i++)
            {
                OutlineEntry entry = _outline[i];
                int parent = Parent(i);

                var children = new List<int>();
                for (int j = i + 1; j < _outline.Count && depth[j] > depth[i]; j++)
                    if (depth[j] == depth[i] + 1)
                        children.Add(j);

                int previous = -1, next = -1;
                for (int j = i - 1; j >= 0; j--)
                    if (Sibling(i, j)) { previous = j; break; }
                for (int j = i + 1; j < _outline.Count; j++)
                {
                    if (depth[j] < depth[i]) break;
                    if (Sibling(i, j)) { next = j; break; }
                }

                var sb = new StringBuilder("<< /Title ").Append(PdfWriter.Text(entry.Title))
                         .Append(" /Parent ").Append(parent < 0 ? root : objects[parent]).Append(" 0 R");

                if (previous >= 0) sb.Append(" /Prev ").Append(objects[previous]).Append(" 0 R");
                if (next >= 0) sb.Append(" /Next ").Append(objects[next]).Append(" 0 R");

                if (children.Count > 0)
                {
                    sb.Append(" /First ").Append(objects[children[0]]).Append(" 0 R")
                      .Append(" /Last ").Append(objects[children[^1]]).Append(" 0 R")
                      // Negative: the branch opens closed, so a long section does not
                      // greet the reader with every heading it has.
                      .Append(" /Count -").Append(children.Count);
                }

                // Destination: the page, and the height on it, measured from the bottom
                // like everything a PDF says about position.
                if (entry.Sheet >= 0 && entry.Sheet < _pageObjects.Count)
                {
                    // That page's height, not the last one's - the same correction the
                    // widgets needed and for the same reason. A bookmark at 100 mm on an
                    // A4 page pointed at 136 pt instead of 558 when the file ended on a
                    // shorter sheet, which is a heading the reader scrolls past.
                    double sheetHeight = entry.Sheet < _pageSizes.Count
                        ? _pageSizes[entry.Sheet].Height : _pageHeight;
                    double top = (sheetHeight - entry.Top) * MillimetresToPoints;
                    sb.Append(" /Dest [").Append(_pageObjects[entry.Sheet]).Append(" 0 R /XYZ null ")
                      .Append(PdfWriter.N(top)).Append(" null]");
                }

                _pdf.Set(objects[i], sb.Append(" >>").ToString());
            }

            var top0 = new List<int>();
            for (int i = 0; i < _outline.Count; i++)
                if (depth[i] == 0)
                    top0.Add(i);

            _pdf.Set(root, $"<< /Type /Outlines /First {objects[top0[0]]} 0 R " +
                           $"/Last {objects[top0[^1]]} 0 R /Count {top0.Count} >>");
            return root;
        }

        public void BeginPage(Page page)
        {
            _pageWidth = page.Frame.Width;
            _pageHeight = page.Frame.Height;
            _content = new StringBuilder(8192);
            _mcid = 0;                                   // marks are numbered within a page

            // One matrix for the whole page: millimetres in, points out, and the y axis
            // turned over so the engine's top-left origin holds all the way down.
            _content.Append(PdfWriter.N(MillimetresToPoints)).Append(" 0 0 ")
                    .Append(PdfWriter.N(-MillimetresToPoints)).Append(" 0 ")
                    .Append(PdfWriter.N(_pageHeight * MillimetresToPoints)).Append(" cm\n");
        }

        public void EndPage()
        {
            _pageBodies.Add(_content.ToString());
            _pageSizes.Add((_pageWidth, _pageHeight));
            _pageObjects.Add(_pdf.Reserve());
        }

        public void EndDocument() { }

        public void Dispose() { }

        // ------------------------------------------------------------------

        public void DrawRectangle(Rect frame, PenStyle? pen, BrushStyle? fill)
        {
            if (pen == null && fill == null)
                return;

            if (fill != null)
                _content.Append(Rgb(fill.Color)).Append(" rg\n");
            if (pen != null)
                _content.Append(Rgb(pen.Color)).Append(" RG\n")
                        .Append(PdfWriter.N(pen.Width)).Append(" w\n")
                        .Append(Dash(pen));

            _content.Append(PdfWriter.N(frame.Left)).Append(' ').Append(PdfWriter.N(frame.Top)).Append(' ')
                    .Append(PdfWriter.N(frame.Width)).Append(' ').Append(PdfWriter.N(frame.Height)).Append(" re\n")
                    .Append(pen != null && fill != null ? "B\n" : fill != null ? "f\n" : "S\n");
        }

        public void DrawLine(double x1, double y1, double x2, double y2, PenStyle pen)
        {
            _content.Append(Rgb(pen.Color)).Append(" RG\n")
                    .Append(PdfWriter.N(pen.Width)).Append(" w\n")
                    .Append(Dash(pen))
                    .Append(PdfWriter.N(x1)).Append(' ').Append(PdfWriter.N(y1)).Append(" m\n")
                    .Append(PdfWriter.N(x2)).Append(' ').Append(PdfWriter.N(y2)).Append(" l\nS\n");
        }

        /// <summary>
        /// One line turned about its own start, written into the page's content stream.
        /// Not an annotation and not a background: it belongs to the page, so it prints
        /// and it flattens with everything else.
        /// </summary>
        public void DrawRotatedText(double x, double y, string text, TextStyle style, double degrees)
        {
            if (string.IsNullOrEmpty(text))
                return;

            FontFile? face = Fonts.FontFor(style);
            if (face == null)
                return;

            EmbeddedFont embedded = Use(face);

            // The page matrix already flipped y, so unrotated text needs [1 0 0 -1] and
            // the rotation composes onto that. The first column is where the text's own
            // left-to-right goes, the second where its up goes:
            //
            //     [ cos  -sin ]
            //     [ -sin -cos ]
            //
            // Getting the sign of b and c wrong turns the text the other way while the
            // tiling still marches the first way, so the PDF and the preview disagree -
            // which is what a reader notices and no assertion here did.
            double radians = degrees * Math.PI / 180.0;
            double cos = Math.Cos(radians), sin = Math.Sin(radians);

            Color ink = style.Color ?? Frisket.Color.Black;

            // q/Q around it: the opacity is the mark's own business and must not leak
            // into whatever the page draws next.
            _content.Append("q\n").Append(AlphaState(ink))
                    .Append(Rgb(ink)).Append(" rg\nBT\n")
                    .Append('/').Append(embedded.Name).Append(' ')
                    .Append(PdfWriter.N(style.EmSizeInMillimeters)).Append(" Tf\n")
                    .Append(PdfWriter.N(cos)).Append(' ').Append(PdfWriter.N(-sin)).Append(' ')
                    .Append(PdfWriter.N(-sin)).Append(' ').Append(PdfWriter.N(-cos)).Append(' ')
                    .Append(PdfWriter.N(x)).Append(' ').Append(PdfWriter.N(y)).Append(" Tm\n")
                    .Append('<').Append(GlyphString(text, embedded, _missingGlyphs)).Append("> Tj\nET\nQ\n");
        }

        public void DrawText(Rect frame, string text, TextStyle style)
        {
            if (string.IsNullOrEmpty(text))
                return;

            FontFile? file = Fonts.FontFor(style);
            if (file == null)
                return;

            EmbeddedFont font = Use(file);
            FontMetrics metrics = Measurer.GetMetrics(style);
            string[] lines = text.Split('\n');

            // The cap sits centred in its line, so the top of the capitals lands the same
            // distance from the frame whichever face is used.
            double first = (metrics.LineHeight + metrics.CapHeight) / 2;
            double block = lines.Length * metrics.LineHeight;
            double offset = (style.VerticalAlignment ?? Frisket.VerticalAlignment.Top) switch
            {
                VerticalAlignment.Middle => Math.Max(0, (frame.Height - block) / 2),
                VerticalAlignment.Bottom => Math.Max(0, frame.Height - block),
                _ => 0
            };

            _content.Append(Rgb(style.Color ?? Frisket.Color.Black)).Append(" rg\nBT\n")
                    .Append('/').Append(font.Name).Append(' ')
                    .Append(PdfWriter.N(style.EmSizeInMillimeters)).Append(" Tf\n");

            for (int i = 0; i < lines.Length; i++)
            {
                double width = Fonts.WidthOf(lines[i], style);
                double x = (style.HorizontalAlignment ?? Frisket.HorizontalAlignment.Left) switch
                {
                    HorizontalAlignment.Center => (frame.Left + frame.Right) / 2 - width / 2,
                    HorizontalAlignment.Right => frame.Right - width,
                    _ => frame.Left
                };
                double baseline = frame.Top + offset + first + i * metrics.LineHeight;

                // The page matrix flipped the y axis; the text matrix flips it back so
                // the glyphs stand up.
                _content.Append("1 0 0 -1 ").Append(PdfWriter.N(x)).Append(' ')
                        .Append(PdfWriter.N(baseline)).Append(" Tm\n");

                double padding = Justification.SpacePadding(lines[i], width, frame.Width,
                                                            i == lines.Length - 1, style);

                if (padding > 0)
                {
                    ShowJustified(lines[i], font, padding, style.EmSizeInMillimeters);
                    width = frame.Width;              // it reaches the margin now
                }
                else
                {
                    _content.Append('<').Append(GlyphString(lines[i], font, _missingGlyphs))
                            .Append("> Tj\n");
                }

                if (style.Underline == true)
                {
                    double thickness = Math.Max(0.05, style.EmSizeInMillimeters * 0.05);
                    double y = baseline + metrics.Descent * 0.35;
                    _content.Append("ET\n")
                            .Append(Rgb(style.Color ?? Frisket.Color.Black)).Append(" RG\n")
                            .Append(PdfWriter.N(thickness)).Append(" w\n[] 0 d\n")
                            .Append(PdfWriter.N(x)).Append(' ').Append(PdfWriter.N(y)).Append(" m\n")
                            .Append(PdfWriter.N(x + width)).Append(' ').Append(PdfWriter.N(y)).Append(" l\nS\nBT\n")
                            .Append('/').Append(font.Name).Append(' ')
                            .Append(PdfWriter.N(style.EmSizeInMillimeters)).Append(" Tf\n");
                }

                if (style.Strikethrough == true)
                {
                    // Through the middle of a lowercase letter, not the middle of the whole
                    // line - half the x-height above the baseline is where a strike reads as
                    // crossing the word rather than floating above or sitting under it.
                    double thickness = Math.Max(0.05, style.EmSizeInMillimeters * 0.05);
                    double y = baseline - metrics.XHeight * 0.5;
                    _content.Append("ET\n")
                            .Append(Rgb(style.Color ?? Frisket.Color.Black)).Append(" RG\n")
                            .Append(PdfWriter.N(thickness)).Append(" w\n[] 0 d\n")
                            .Append(PdfWriter.N(x)).Append(' ').Append(PdfWriter.N(y)).Append(" m\n")
                            .Append(PdfWriter.N(x + width)).Append(' ').Append(PdfWriter.N(y)).Append(" l\nS\nBT\n")
                            .Append('/').Append(font.Name).Append(' ')
                            .Append(PdfWriter.N(style.EmSizeInMillimeters)).Append(" Tf\n");
                }
            }

            _content.Append("ET\n");
        }

        /// <summary>
        /// Draws one justified line, widening every space by <paramref name="padding"/>.
        ///
        /// Not the Tw operator, which would be the obvious way: word spacing applies to
        /// the single-byte code 32, and these fonts are composite with two-byte codes,
        /// so Tw does nothing at all here - silently. The positions between words are
        /// adjusted instead, which is what TJ is for.
        ///
        /// The numbers in a TJ array are thousandths of a text-space unit and are
        /// subtracted from the position, so widening a gap means a negative one.
        /// </summary>
        private void ShowJustified(string line, EmbeddedFont font, double padding, double emSize)
        {
            double adjustment = emSize > 0 ? -padding * 1000.0 / emSize : 0;

            _content.Append('[');

            int start = 0;
            for (int i = 0; i <= line.Length; i++)
            {
                bool end = i == line.Length;
                if (!end && line[i] != ' ')
                    continue;

                // The space stays with the words before it; only the gap after it moves.
                string chunk = line[start..(end ? i : i + 1)];
                if (chunk.Length > 0)
                    _content.Append('<').Append(GlyphString(chunk, font, _missingGlyphs)).Append('>');

                if (!end)
                    _content.Append(PdfWriter.N(adjustment));

                start = i + 1;
            }

            _content.Append("] TJ\n");
        }

        /// <summary>
        /// Whether a widget will really be written for this item.
        ///
        /// One expression, asked by the drawing and by the structure tree alike. They
        /// used to answer it separately - the tree asked only whether the output was
        /// interactive - so a Form element was opened for things no widget ever came
        /// for, and PDF/UA refuses a Form with no widget under it.
        ///
        /// A signature to be given by hand is not a form field at all: written as one,
        /// readers that support signing offer a certificate dialog instead - which asks
        /// the customer of a garage for something they do not have, in place of the
        /// ruled box they were meant to sign.
        /// </summary>
        private static bool WillHaveWidget(BaseItem item, bool interactive, Rect frame) =>
            interactive
            && item is FieldItem field
            && !(field.Kind == FieldKind.Signature && field.Signing == Signing.ByHand)

            // A cell that a split row places again on the continuation page is laid at
            // zero height, so the row keeps its full count of cells (section 11). For
            // anything drawn that placeholder is an empty rectangle; for a field it was
            // a SECOND widget - unfillable, invisible, and advertised in /AcroForm
            // /Fields under an invented name that is in no template.
            && !frame.IsEmpty;

        /// <summary>
        /// Draws a field. The box goes in the widget's appearance stream when the form is
        /// interactive, and straight onto the page when it is not; the value is always
        /// drawn on the page, which is what keeps the two identical.
        /// </summary>
        public void DrawField(Rect frame, FieldItem field, TextStyle font)
        {
            Rect box = field.Kind == FieldKind.Checkbox ? Square(frame, font) : frame;

            // A signature to be given by hand is not a form field at all. Written as one,
            // readers that support signing offer a certificate dialog instead - which asks
            // the customer of a garage for something they do not have, in place of the
            // ruled box they were meant to sign.
            bool widget = WillHaveWidget(field, Interactive, frame);

            if (widget)
            {
                if (_openForm >= 0)
                    _marked[_openForm].Field = _fields.Count;

                _fields.Add(new PdfField
                {
                    Item = field,
                    Name = string.IsNullOrWhiteSpace(field.Name) ? "field" : field.Name,
                    Frame = frame,
                    Font = font,
                    Page = _pageObjects.Count           // the page being drawn
                });
            }

            // The widget's appearance stream is the only thing drawing an interactive
            // field, so anything that rasterises or flattens without annotation support
            // drops the whole form - not just its interactivity, but every trace that
            // there was something to fill in. The same box on the page underneath costs
            // nothing: it sits exactly where the widget does, so a viewer that shows the
            // widget shows no difference.
            if ((!widget || FieldBackdrop) && (field.Fill != null || field.Outline != null))
                DrawRectangle(box, field.Outline, field.Fill);

            switch (field.Kind)
            {
                case FieldKind.Checkbox:
                    if (field.IsChecked && !widget)
                        Tick(Square(frame, font), font.Color ?? Color.Black);
                    break;

                case FieldKind.Signature:
                    if (!widget)
                    {
                        double rule = frame.Bottom - Measurer.GetMetrics(font).LineHeight;
                        DrawLine(frame.Left + 1, rule, frame.Right - 1, rule,
                                 new PenStyle { Width = 0.15, Color = new(150, 150, 150) });
                    }
                    break;

                default:
                    if (!string.IsNullOrEmpty(field.Value))
                    {
                        DrawText(new Rect(frame.Left + 1, frame.Top + 0.4, frame.Right - 1, frame.Bottom),
                                 field.Value!, font);
                    }
                    break;
            }
        }

        private Rect Square(Rect frame, TextStyle font)
        {
            double side = Math.Min(frame.Height, Measurer.GetMetrics(font).LineHeight);
            return Rect.FromSize(frame.Left, frame.Top, side, side);
        }

        private void Tick(Rect box, Color colour)
        {
            var pen = new PenStyle { Width = Math.Max(0.15, box.Width * 0.12), Color = colour };
            DrawLine(box.Left + box.Width * 0.22, box.Top + box.Height * 0.52,
                     box.Left + box.Width * 0.42, box.Top + box.Height * 0.74, pen);
            DrawLine(box.Left + box.Width * 0.42, box.Top + box.Height * 0.74,
                     box.Left + box.Width * 0.80, box.Top + box.Height * 0.26, pen);
        }

        public void DrawImage(Rect frame, ImageSource image) => DrawImage(frame, image, 1);

        public void DrawImage(Rect frame, ImageSource image, double opacity)
        {
            if (image.Data.Length == 0 || opacity <= 0)
                return;

            EmbeddedImage embedded = Use(image);

            // The unit square is placed with its top-left at the frame's top-left; the
            // negative height undoes the page's flipped y axis for the bitmap.
            //
            // q/Q around it: the opacity is this picture's own business and must not leak
            // into whatever the page draws next - the same care the rotated text takes.
            _content.Append("q\n").Append(AlphaState(opacity))
                    .Append(PdfWriter.N(frame.Width)).Append(" 0 0 ").Append(PdfWriter.N(-frame.Height)).Append(' ')
                    .Append(PdfWriter.N(frame.Left)).Append(' ').Append(PdfWriter.N(frame.Bottom)).Append(" cm\n")
                    .Append('/').Append(embedded.Name).Append(" Do\nQ\n");
        }

        /// <summary>
        /// Draws a page copied out of another PDF.
        ///
        /// The page arrives as a Form XObject whose own space is the page's, measured in
        /// points and with y upwards; this sits it in a frame measured in millimetres
        /// with y downwards, which is what the page-level transform has established. Two
        /// scales and a flip, and it is the very same shape as placing a picture - whose
        /// unit square is the special case where both extents are one.
        /// </summary>
        public void DrawImportedPage(Rect frame, ImportedPageItem page)
        {
            // Nothing resolved it. Drawing an outline to stand for it was considered and
            // turned down: an outline is a mark this document did not ask for, and the
            // absence is already said out loud in LayoutResult.Issues and by the panel.
            if (page.Resolved is not { } read || frame.Width <= 0 || frame.Height <= 0)
                return;

            double wide = read.Width * MillimetresToPoints;
            double tall = read.Height * MillimetresToPoints;
            if (wide <= 0 || tall <= 0)
                return;

            string name = Use(read);

            _content.Append("q\n")
                    .Append(PdfWriter.N(frame.Width / wide)).Append(" 0 0 ")
                    .Append(PdfWriter.N(-frame.Height / tall)).Append(' ')
                    .Append(PdfWriter.N(frame.Left)).Append(' ')
                    .Append(PdfWriter.N(frame.Bottom)).Append(" cm\n")
                    .Append('/').Append(name).Append(" Do\nQ\n");
        }

        private readonly Dictionary<IImportedPage, string> _forms = new();
        private readonly List<(string Name, int Number)> _formObjects = new();

        /// <summary>
        /// Copies a page into this file once, however many times it is drawn.
        ///
        /// Keyed on the page that was read rather than on the item or on the bytes: two
        /// items naming the same file share one <see cref="IImportedPage"/> already, since
        /// resolution caches by file and page number - so the same terms on the back of
        /// nine sections cost one copy of them.
        /// </summary>
        private string Use(IImportedPage page)
        {
            if (_forms.TryGetValue(page, out string? known))
                return known;

            string name = "Fx" + (_forms.Count + 1);
            _forms[page] = name;
            _formObjects.Add((name, page.WriteInto(new Sink(_pdf))));
            return name;
        }

        /// <summary>
        /// The file being written, seen through the three holes something outside this
        /// assembly needs - and no wider. A backend that exposed Reserve and Set on its
        /// own face would be inviting anybody to put anything in the document.
        /// </summary>
        private sealed class Sink(PdfWriter writer) : IPdfSink
        {
            public int Reserve() => writer.Reserve();

            public void Set(int number, string body) => writer.Set(number, body);

            public void SetStream(int number, string dictionary, byte[] data, bool compress = false)
                => writer.SetStream(number, dictionary, data, compress);
        }

        // ------------------------------------------------------------------

        /// <summary>Assembles the section. Call once the last page has been drawn.</summary>
        public byte[] ToArray()
        {
            if (Conformance == PdfConformance.PdfA3b && _missingGlyphs.Count > 0)
            {
                string characters = string.Join(", ", _missingGlyphs.Take(12)
                    .Select(c => $"U+{c:X4} '{char.ConvertFromUtf32(c)}'"));

                throw new InvalidOperationException(
                    $"The report uses {_missingGlyphs.Count} character(s) the fonts have no glyph for: {characters}. " +
                    "PDF/A forbids a reference to .notdef, so the file would not be archivable. " +
                    "Use a font that covers them, or change the text.");
            }

            int pagesObject = _pdf.Reserve();

            foreach (EmbeddedFont font in _fonts.Values)
                EmitFont(font);
            foreach (var (key, image) in _images)
                EmitImage(key, image);

            string resources = BuildResources();

            // Before any page is written, since each one has to name its widgets.
            //
            // A form needs a font for its default appearance, and took whichever one the
            // file happened to have embedded - so a document whose only content is fields
            // had none, the AcroForm was skipped, the widgets were never reserved, and
            // the page that already listed them threw. A signature page with a rule, a
            // box and no words is an entirely ordinary thing to ask for.
            //
            // Asked for rather than hoped for: the same FontFor the text path uses, so
            // what the form names is a font this file really embeds. A standard-14 name
            // would be shorter and PDF/A forbids it.
            EmbeddedFont? formFont = _fonts.Values.FirstOrDefault();

            if (formFont == null && _fields.Count > 0
                && Fonts.FontFor(new TextStyle()) is { } fallback)
            {
                formFont = Use(fallback);
                EmitFont(formFont);
            }

            bool hasForm = _fields.Count > 0 && formFont != null;
            if (hasForm)
                PdfForm.Reserve(_pdf, _fields);

            for (int i = 0; i < _pageObjects.Count; i++)
            {
                int contents = _pdf.AddStream("", PdfWriter.Latin1(_pageBodies[i]), Compress);
                (double width, double height) = i < _pageSizes.Count
                    ? _pageSizes[i]
                    : (_pageWidth, _pageHeight);

                _pdf.Set(_pageObjects[i],
                    $"<< /Type /Page /Parent {pagesObject} 0 R " +
                    $"/MediaBox [0 0 {PdfWriter.N(width * MillimetresToPoints)} {PdfWriter.N(height * MillimetresToPoints)}] " +
                    $"/Resources << {resources} >> /Contents {contents} 0 R" +
                    (Tagged ? $" /StructParents {i} /Tabs /S" : string.Empty) +
                    PdfForm.Annotations(_fields, i) + " >>");
            }

            string kids = string.Join(" ", _pageObjects.Select(p => $"{p} 0 R"));
            _pdf.Set(pagesObject, $"<< /Type /Pages /Count {_pageObjects.Count} /Kids [{kids}] >>");

            var catalogEntries = new StringBuilder($"/Type /Catalog /Pages {pagesObject} 0 R");

            int outline = EmitOutline();
            if (outline > 0)
                catalogEntries.Append($" /Outlines {outline} 0 R /PageMode /UseOutlines");

            // What the document is written in. A reader announcing French with an English
            // voice is unintelligible, and this is the one line that prevents it.
            if (!string.IsNullOrWhiteSpace(_language))
                catalogEntries.Append(" /Lang ").Append(PdfWriter.Text(_language));

            // Show the title rather than the file name in the window. The file name of an
            // invoice is a serial number; the title says what it is.
            if (!string.IsNullOrEmpty(_title))
                catalogEntries.Append(" /ViewerPreferences << /DisplayDocTitle true >>");

            int structure = EmitStructure();
            if (structure > 0)
                catalogEntries.Append($" /StructTreeRoot {structure} 0 R /MarkInfo << /Marked true >>");

            catalogEntries.Append(EmitAttachments());

            if (hasForm)
            {
                catalogEntries.Append(PdfForm.Emit(_pdf, _fields, _pageObjects.ToArray(),
                                                   formFont!.ObjectNumber, formFont.Name,
                                                   _pageSizes.Select(p => p.Height).ToList()));
            }

            if (Conformance == PdfConformance.PdfA3b)
            {
                int profile = _pdf.AddStream("/N 3", IccProfile.CreateSRgb());
                int intent = _pdf.Add(
                    "<< /Type /OutputIntent /S /GTS_PDFA1 /OutputConditionIdentifier (sRGB) " +
                    $"/Info (sRGB IEC61966-2.1) /DestOutputProfile {profile} 0 R >>");
                catalogEntries.Append($" /OutputIntents [{intent} 0 R]");
            }

            string xmp = PdfMetadata.Build(_title, _author, _now, "Frisket", Conformance,
                                           _facturXFileName, _facturXProfile,
                                           _subject, _keywords, Tagged);

            // The packet must stay readable in the file: a conformance checker reads it
            // without parsing the PDF, so it is never compressed.
            int metadata = _pdf.AddRawStream("/Type /Metadata /Subtype /XML",
                                             Encoding.UTF8.GetBytes(xmp));
            catalogEntries.Append($" /Metadata {metadata} 0 R");

            int catalog = _pdf.Add($"<< {catalogEntries} >>");
            return _pdf.Build(catalog, "Frisket", _now, _title, _author, DocumentId(),
                              _subject, _keywords);
        }

        public void Save(string path) => File.WriteAllBytes(path, ToArray());

        /// <summary>
        /// Emits the embedded files and returns what the catalog must say about them:
        /// the name tree a reader shows in its attachment pane, and the /AF array that
        /// says what each file is to the section.
        /// </summary>
        private string EmitAttachments()
        {
            if (_attachments.Count == 0)
                return string.Empty;

            var specs = new List<int>();
            var names = new StringBuilder();

            foreach (PdfAttachment attachment in _attachments)
            {
                int file = _pdf.AddStream(
                    $"/Type /EmbeddedFile /Subtype /{PdfName(attachment.MediaType)} " +
                    $"/Params << /Size {attachment.Data.Length} /ModDate ({Moment(_now)}) >>",
                    attachment.Data);

                var spec = new StringBuilder("<< /Type /Filespec /F (")
                    .Append(PdfWriter.Escape(attachment.Name)).Append(") /UF (")
                    .Append(PdfWriter.Escape(attachment.Name)).Append(')')
                    .Append(" /AFRelationship /").Append(attachment.Relationship);

                if (!string.IsNullOrEmpty(attachment.Description))
                    spec.Append(" /Desc ").Append(PdfWriter.Text(attachment.Description));

                spec.Append($" /EF << /F {file} 0 R /UF {file} 0 R >> >>");

                int specObject = _pdf.Add(spec.ToString());
                specs.Add(specObject);
                names.Append('(').Append(PdfWriter.Escape(attachment.Name)).Append(") ").Append(specObject).Append(" 0 R ");
            }

            int tree = _pdf.Add($"<< /Names [{names}] >>");
            string references = string.Join(" ", specs.Select(s => $"{s} 0 R"));

            return $" /AF [{references}] /Names << /EmbeddedFiles {tree} 0 R >>";
        }

        /// <summary>A media type as a PDF name: the slash has to be escaped.</summary>
        private static string PdfName(string mediaType) => mediaType.Replace("/", "#2F");

        private static string Moment(DateTimeOffset when)
            => string.Format(CultureInfo.InvariantCulture, "D:{0:yyyyMMddHHmmss}{1}{2:00}'{3:00}'",
                             when.DateTime, when.Offset < TimeSpan.Zero ? '-' : '+',
                             Math.Abs(when.Offset.Hours), Math.Abs(when.Offset.Minutes));

        /// <summary>
        /// A file identifier, required of an archivable PDF. Derived from the section
        /// rather than random, so the same report renders to the same bytes twice.
        /// </summary>
        private byte[] DocumentId()
        {
            byte[] seed = Encoding.UTF8.GetBytes($"{_title}|{_author}|{_now:O}|{_pageObjects.Count}");
            return System.Security.Cryptography.SHA256.HashData(seed)[..16];
        }

        private string BuildResources()
        {
            var sb = new StringBuilder();

            if (_fonts.Count > 0)
            {
                sb.Append("/Font << ");
                foreach (EmbeddedFont font in _fonts.Values)
                    sb.Append('/').Append(font.Name).Append(' ').Append(font.ObjectNumber).Append(" 0 R ");
                sb.Append(">> ");
            }

            if (_images.Count > 0 || _formObjects.Count > 0)
            {
                // One dictionary for both: a picture and an imported page are the same
                // kind of thing to a content stream, and a second /XObject entry beside
                // the first is a resources dictionary with a duplicate key.
                sb.Append("/XObject << ");
                foreach (EmbeddedImage image in _images.Values)
                    sb.Append('/').Append(image.Name).Append(' ').Append(image.ObjectNumber).Append(" 0 R ");
                foreach ((string name, int number) in _formObjects)
                    sb.Append('/').Append(name).Append(' ').Append(number).Append(" 0 R ");
                sb.Append(">> ");
            }

            if (_alphas.Count > 0)
            {
                sb.Append("/ExtGState << ");
                foreach (var (alpha, name) in _alphas)
                {
                    // Normal blending stated explicitly: PDF/A wants to know, and an
                    // inherited mode would make the mark look different per reader.
                    sb.Append('/').Append(name)
                      .Append(" << /Type /ExtGState /BM /Normal /ca ").Append(PdfWriter.N(alpha))
                      .Append(" /CA ").Append(PdfWriter.N(alpha)).Append(" >> ");
                }
                sb.Append(">> ");
            }

            return sb.ToString();
        }

        private readonly Dictionary<double, string> _alphas = new();

        /// <summary>
        /// Names a graphics state for this opacity, so the content stream can select it.
        /// Returns an empty string for a fully opaque colour, which needs no state at all.
        /// </summary>
        private string AlphaState(Color colour) => AlphaState(colour.A / 255.0);

        /// <summary>The same, for a picture, which has an opacity rather than a colour.</summary>
        private string AlphaState(double opacity)
        {
            if (opacity >= 1)
                return string.Empty;

            double alpha = Math.Round(Math.Max(0, opacity), 3);
            if (!_alphas.TryGetValue(alpha, out string? name))
                _alphas[alpha] = name = "GS" + (_alphas.Count + 1);

            return "/" + name + " gs\n";
        }

        // ------------------------------------------------------------------
        // Fonts
        // ------------------------------------------------------------------

        private EmbeddedFont Use(FontFile file)
        {
            string key = $"{file.Family}|{file.Subfamily}|{file.Bold}|{file.Italic}";
            if (_fonts.TryGetValue(key, out EmbeddedFont? found))
                return found;

            var font = new EmbeddedFont { File = file, Name = "F" + (_fonts.Count + 1) };
            _fonts[key] = font;
            return font;
        }

        /// <summary>Encodes a run as Identity-H: two bytes of glyph id per character.</summary>
        private static string GlyphString(string text, EmbeddedFont font, ICollection<int> missing)
        {
            var sb = new StringBuilder(text.Length * 4);

            for (int i = 0; i < text.Length; i++)
            {
                int codePoint = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }

                ushort glyph = font.File.GlyphOf(codePoint);
                if (glyph == 0)
                    missing.Add(codePoint);

                font.Glyphs[glyph] = codePoint;
                sb.Append(glyph.ToString("X4", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Embed only the glyphs the section drew. On by default; turn it off to embed
        /// each face whole, which is what to try first if a reader ever refuses a font.
        /// </summary>
        public bool Subset { get; set; } = true;

        /// <summary>Six uppercase letters standing for this particular set of glyphs.</summary>
        private static string SubsetTag(IEnumerable<ushort> glyphs)
        {
            uint hash = 2166136261;
            foreach (ushort g in glyphs)
                hash = unchecked((hash ^ g) * 16777619);

            var tag = new char[6];
            for (int i = 0; i < 6; i++)
            {
                tag[i] = (char)('A' + hash % 26);
                hash /= 26;
            }

            return new string(tag);
        }

        private void EmitFont(EmbeddedFont font)
        {
            FontFile file = font.File;
            string name = Sanitise(file.Family) + (file.Bold ? ",Bold" : "") + (file.Italic ? ",Italic" : "");

            // Only the glyphs this section drew. A whole face is most of an invoice:
            // sixty kilobytes of font against twelve of section, for thirty-seven glyphs
            // out of six hundred.
            byte[] embedded = Subset ? FontSubset.Build(file.Data, font.Glyphs.Keys) : file.Data;

            // A partial font must say so, and the tag has to differ per subset: two
            // sections embedding different cuts under one name are why the six letters
            // exist. Derived from the glyphs themselves, so the same section twice gives
            // the same file.
            if (embedded.Length < file.Data.Length)
                name = SubsetTag(font.Glyphs.Keys) + "+" + name;

            int fontFile = _pdf.AddStream($"/Length1 {embedded.Length}", embedded, Compress);

            int flags = 4;                                   // symbolic: Identity-H bypasses the encoding
            if (file.Italic) flags |= 64;
            if (file.Bold) flags |= 1 << 18;

            int descriptor = _pdf.Add(
                $"<< /Type /FontDescriptor /FontName /{name} /Flags {flags} " +
                $"/FontBBox [{Thousandths(file.XMin)} {Thousandths(file.YMin)} {Thousandths(file.XMax)} {Thousandths(file.YMax)}] " +
                $"/ItalicAngle {(file.Italic ? -12 : 0)} /Ascent {Thousandths(file.Ascender)} " +
                $"/Descent {-Thousandths(file.Descender)} /CapHeight {Thousandths(file.CapHeight)} " +
                $"/StemV {(file.Bold ? 160 : 80)} /FontFile2 {fontFile} 0 R >>");

            var widths = new StringBuilder();
            foreach (ushort glyph in font.Glyphs.Keys)
                widths.Append(glyph).Append(" [").Append(Thousandths(file.AdvanceOfGlyph(glyph))).Append("] ");

            int descendant = _pdf.Add(
                "<< /Type /Font /Subtype /CIDFontType2 " +
                $"/BaseFont /{name} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
                $"/FontDescriptor {descriptor} 0 R /DW 1000 /W [{widths}] /CIDToGIDMap /Identity >>");

            int toUnicode = _pdf.AddStream("", PdfWriter.Latin1(ToUnicodeCMap(font)));

            font.ObjectNumber = _pdf.Add(
                "<< /Type /Font /Subtype /Type0 " +
                $"/BaseFont /{name} /Encoding /Identity-H /DescendantFonts [{descendant} 0 R] " +
                $"/ToUnicode {toUnicode} 0 R >>");
        }

        /// <summary>
        /// Maps glyph ids back to characters, so the text can still be selected, searched
        /// and copied out of the finished section.
        /// </summary>
        private static string ToUnicodeCMap(EmbeddedFont font)
        {
            var sb = new StringBuilder();
            sb.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n")
              .Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n")
              .Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n")
              .Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");

            var entries = font.Glyphs.ToList();
            for (int start = 0; start < entries.Count; start += 100)
            {
                var chunk = entries.Skip(start).Take(100).ToList();
                sb.Append(chunk.Count).Append(" beginbfchar\n");
                foreach (var (glyph, codePoint) in chunk)
                {
                    sb.Append('<').Append(glyph.ToString("X4", CultureInfo.InvariantCulture)).Append("> <");
                    foreach (char c in char.ConvertFromUtf32(codePoint))
                        sb.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    sb.Append(">\n");
                }
                sb.Append("endbfchar\n");
            }

            sb.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend");
            return sb.ToString();
        }

        private static int Thousandths(double emFraction) => (int)Math.Round(emFraction * 1000);

        /// <summary>PDF colours run from 0 to 1, one component at a time.</summary>
        private static string Rgb(Color c)
            => $"{PdfWriter.N(c.R / 255.0)} {PdfWriter.N(c.G / 255.0)} {PdfWriter.N(c.B / 255.0)}";

        /// <summary>Dash pattern, in millimetres like every other length here.</summary>
        private static string Dash(PenStyle pen) => pen.DashStyle switch
        {
            DashStyle.Dash => "[2 1] 0 d\n",
            DashStyle.Dot => "[0.4 0.8] 0 d\n",
            DashStyle.DashDot => "[2 1 0.4 1] 0 d\n",
            _ => "[] 0 d\n"
        };

        private static string Sanitise(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' ? c : '_');
            return sb.Length == 0 ? "Font" : sb.ToString();
        }

        // ------------------------------------------------------------------
        // Images
        // ------------------------------------------------------------------

        /// <summary>
        /// The XObject for these bytes, made once and drawn as often as asked.
        ///
        /// Keyed on what the bytes ARE. It was keyed on where they came from -
        /// <c>SourcePath</c> first, and that is the template's field path for anything
        /// bound from data, the same string for every record. So sixty signatures drew
        /// the first person's, and the conformance document's six photographs were one
        /// photograph carrying six different alternative texts. It shipped: the file in
        /// tests/output held two image objects for seven pictures while the SVG beside
        /// it held seven, which is the divergence section 2 exists to catch.
        ///
        /// The fallback it fell through to was no better - sixty-four bytes and a length.
        /// For a PNG those bytes are the signature and the IHDR, so two pictures of the
        /// same size and the same length collided, and a collision here draws the wrong
        /// picture rather than failing. The whole content, hashed: SHA256.HashData is
        /// already how this file names an embedded font's subset.
        /// </summary>
        private EmbeddedImage Use(ImageSource source)
        {
            string key = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(source.Data));

            if (_images.TryGetValue(key, out EmbeddedImage? found))
                return found;

            var image = new EmbeddedImage { Name = "Im" + (_images.Count + 1) };
            _images[key] = image;
            _pending[key] = source;
            return image;
        }

        private readonly Dictionary<string, ImageSource> _pending = new();

        private void EmitImage(string key, EmbeddedImage image)
        {
            ImageSource source = _pending[key];

            if (LooksLikeJpeg(source.Data))
            {
                (int width, int height, int components) = JpegSize(source.Data);
                string space = components == 1 ? "/DeviceGray" : components == 4 ? "/DeviceCMYK" : "/DeviceRGB";

                // A JPEG needs no conversion at all: PDF reads the very same bytes.
                image.ObjectNumber = _pdf.AddRawStream(
                    $"/Type /XObject /Subtype /Image /Width {width} /Height {height} " +
                    $"/ColorSpace {space} /BitsPerComponent 8 /Filter /DCTDecode", source.Data);
                return;
            }

            if (PngImage.TryRead(source.Data, out PngImage png))
            {
                // PNG's own filtering is PDF's Predictor 15, so the compressed data goes
                // in untouched here too.
                image.ObjectNumber = _pdf.AddRawStream(
                    $"/Type /XObject /Subtype /Image /Width {png.Width} /Height {png.Height} " +
                    $"/ColorSpace {png.ColorSpace} /BitsPerComponent {png.BitDepth} /Filter /FlateDecode " +
                    $"/DecodeParms << /Predictor 15 /Colors {png.Colors} /BitsPerComponent {png.BitDepth} " +
                    $"/Columns {png.Width} >>", png.Data);
                return;
            }

            throw new NotSupportedException(
                "Only JPEG and non-interlaced 8-bit grayscale or RGB PNG images can be embedded; " +
                "this one has a palette, an alpha channel, or is interlaced. A canvas exports " +
                "RGBA: fill it with an opaque colour before exporting and the PNG comes out " +
                "without an alpha channel.");
        }

        private static bool LooksLikeJpeg(byte[] data)
            => data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

        /// <summary>Reads a JPEG's size and component count from its start-of-frame marker.</summary>
        private static (int Width, int Height, int Components) JpegSize(byte[] data)
        {
            for (int i = 2; i + 9 < data.Length;)
            {
                if (data[i] != 0xFF) { i++; continue; }

                byte marker = data[i + 1];
                int length = (data[i + 2] << 8) | data[i + 3];

                // Any SOFn but the arithmetic and lossless ones carries the dimensions.
                if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                    return ((data[i + 7] << 8) | data[i + 8], (data[i + 5] << 8) | data[i + 6], data[i + 9]);

                i += 2 + length;
            }

            throw new NotSupportedException("This JPEG has no readable frame header.");
        }
    }
}
