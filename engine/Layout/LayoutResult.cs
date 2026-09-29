using System.Text.Json.Serialization;
namespace Frisket
{
    /// <summary>Which pages an item appears on. Drives both overlays and header/footer variants.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<PageScope>))]
    public enum PageScope { All, First, Last, ExceptFirst, ExceptLast, Odd, Even, Range }

    /// <summary>Author-requested page break around an item.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<PageBreak>))]
    public enum PageBreak { None, Before, After, BeforeAndAfter }

    /// <summary>
    /// What happens when an item does not fit in the space left on the current page.
    /// <see cref="Auto"/> splits it if it is splittable at all (a table, a multi-line
    /// paragraph); <see cref="KeepTogether"/> always pushes it whole to the next page.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Splitting>))]
    public enum Splitting { Auto, KeepTogether }

    /// <summary>
    /// One element as placed on one page. An item of the tree can produce several of
    /// these - a repeated header produces one per page, a split table one per page it
    /// spans - which is why the layout result lives outside the element tree.
    /// </summary>
    public sealed class PlacedItem
    {
        /// <summary>The element of the definition tree this rectangle came from.</summary>
        public BaseItem Source { get; }

        /// <summary>Absolute frame on the page, in millimeters.</summary>
        public Rect Frame { get; internal set; }

        /// <summary>
        /// The text to draw here: tokens already substituted, and only the lines that
        /// belong to this page when the item was split. Null for non-text items.
        /// </summary>
        public string? Fragment { get; internal set; }

        /// <summary>Index of the first line of the source this fragment carries.</summary>
        internal int LineStart;

        /// <summary>Number of lines, or -1 when the item is not line-based.</summary>
        internal int LineCount = -1;

        /// <summary>Width the lines were broken at, needed to redo the break identically.</summary>
        internal double MeasureWidth = double.NaN;

        /// <summary>
        /// Column boundaries, set on a placed table row so the renderer can stroke a grid
        /// spanning the full row height - the cells themselves are inset by the padding.
        /// </summary>
        internal double[]? GridEdges;

        /// <summary>
        /// For text, the style with its size already resolved to points, so the backend
        /// draws with exactly the size the layout was computed with. Null for other items.
        /// </summary>
        public TextStyle? Font { get; }

        public PlacedItem(BaseItem source, Rect frame, string? fragment = null, TextStyle? font = null)
        {
            Source = source;
            Frame = frame;
            Fragment = fragment;
            Font = font;
        }

        public override string ToString() => $"{Source.GetType().Name} {Frame}";
    }

    /// <summary>
    /// A resolved page: the reserved bands plus the flat list of what to draw.
    /// A backend only ever needs this, never the element tree.
    /// </summary>
    public sealed class Page
    {
        private readonly List<PlacedItem> _items = new();

        /// <summary>
        /// 1-based page number. A back page carries the number of the sheet it belongs
        /// to rather than one of its own, so {Page} on a back reads as the front does.
        /// </summary>
        public int Number { get; internal set; }

        /// <summary>
        /// True for a sheet's back. Backs are drawn like any other page but are neither
        /// numbered nor counted: an invoice reading "page 1 / 2" has four printed faces,
        /// and the reader counts what is written on the front.
        /// </summary>
        public bool IsBack { get; internal set; }

        /// <summary>The whole page.</summary>
        public Rect Frame { get; internal set; }

        /// <summary>Band reserved for the header; empty when there is no header.</summary>
        public Rect HeaderBand { get; internal set; }

        /// <summary>What is left for the body once header and footer are reserved.</summary>
        public Rect BodyBand { get; internal set; }

        /// <summary>Band reserved for the footer; empty when there is no footer.</summary>
        public Rect FooterBand { get; internal set; }

        public IReadOnlyList<PlacedItem> Items => _items;

        public Page(int number, Rect frame)
        {
            Number = number;
            Frame = frame;
            BodyBand = frame;
        }

        internal PlacedItem Place(BaseItem source, Rect frame, string? fragment = null, TextStyle? font = null)
        {
            var placed = new PlacedItem(source, frame, fragment, font);
            _items.Add(placed);
            return placed;
        }

        internal void RemoveAll(Predicate<PlacedItem> match) => _items.RemoveAll(match);

        /// <summary>
        /// Drops everything placed after <paramref name="count"/>. What lets a pass be
        /// tried and thrown away - balancing columns needs several attempts, and an
        /// attempt that leaves its items behind is a page drawn twice over.
        /// </summary>
        internal void Rewind(int count)
        {
            if (count >= 0 && count < _items.Count)
                _items.RemoveRange(count, _items.Count - count);
        }

        public override string ToString() => $"Page {Number} {Frame} ({_items.Count} items)";
    }

    /// <summary>
    /// Outcome of resolving a report: the pages to draw, in order.
    /// </summary>
    /// <summary>One entry of the section outline, and where it points.</summary>
    /// <param name="Title">What the reader sees in the panel.</param>
    /// <param name="Level">Zero at the top; deeper entries nest under the last shallower one.</param>
    /// <param name="Sheet">Index into <see cref="LayoutResult.Pages"/>, backs included.</param>
    /// <param name="Top">Millimetres from the top of that sheet.</param>
    public sealed record OutlineEntry(string Title, int Level, int Sheet, double Top);

    /// <summary>
    /// Where one section landed: the index of its first page among all of them, and how
    /// many it took. What <c>{SectionPage}</c> counts from, and what a page limit is
    /// measured against.
    /// </summary>
    public sealed record LaidOutSection(Section Section, int FirstPage, int PageCount);

    public sealed class LayoutResult
    {
        private readonly List<Page> _pages = new();

        public IReadOnlyList<Page> Pages => _pages;

        private readonly List<OutlineEntry> _outline = new();

        /// <summary>
        /// The whole report's outline, in the order the entries were laid down. Empty when
        /// nothing asked for one.
        /// </summary>
        public IReadOnlyList<OutlineEntry> Outline => _outline;

        internal void AddOutline(string title, int level, int sheetIndex, double top)
            => _outline.Add(new OutlineEntry(title, level, sheetIndex, top));

        private readonly List<LaidOutSection> _sections = new();

        /// <summary>
        /// Where each section ended up, in order. One entry for a report of one, which
        /// is what makes <c>{SectionPage}</c> answerable without asking whether the
        /// report has sections at all.
        /// </summary>
        public IReadOnlyList<LaidOutSection> Sections => _sections;

        internal void AddSection(Section section, int firstPage, int pageCount)
            => _sections.Add(new LaidOutSection(section, firstPage, pageCount));

        private readonly List<TemplateIssue> _issues = new();

        /// <summary>
        /// What laying out found, and could not have been found before: a section longer
        /// than the <see cref="Section.PageLimit"/> it declared, and whatever else comes
        /// to be checked here.
        ///
        /// Nothing here refuses anything. Laying out is already separable from drawing -
        /// <see cref="Report.Resolve(ITextMeasurer, CancellationToken)"/> composes and returns this while
        /// producing no file - so the caller can look, then decide whether to go on. The
        /// engine states the fact; the choice is not its to make.
        ///
        /// These are *not* what <c>frisket check</c> reports. That reads the template and
        /// can speak before any section exists; this one had to compose first. The two
        /// use the same words on purpose, and are not interchangeable.
        /// </summary>
        public IReadOnlyList<TemplateIssue> Issues => _issues;

        internal void AddIssue(TemplateIssue issue) => _issues.Add(issue);

        /// <summary>Pages the reader counts: fronts only.</summary>
        public int PageCount => _pages.Count(p => !p.IsBack);

        /// <summary>Faces to print, backs included. What a backend iterates.</summary>
        public int SheetCount => _pages.Count;

        /// <summary>
        /// The watermark's text style with its size already settled in points, the same
        /// way every other run of text is settled. Null when the report carries no mark.
        /// Resolved here rather than at drawing time so the mark obeys the document's
        /// font cascade and its size in millimetres of cap height like anything else.
        /// </summary>
        public TextStyle? WatermarkFont { get; internal set; }

        internal Page AddPage(Rect frame)
        {
            var page = new Page(_pages.Count + 1, frame);
            _pages.Add(page);
            return page;
        }

        /// <summary>Slips a sheet's back in directly after it.</summary>
        internal Page InsertBack(int afterIndex, Rect frame, int number)
        {
            var page = new Page(number, frame) { IsBack = true };
            _pages.Insert(afterIndex + 1, page);
            return page;
        }
    }
}
