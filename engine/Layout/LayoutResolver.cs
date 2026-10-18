using System.Globalization;

namespace Frisket
{
    /// <summary>
    /// Turns a report definition into positioned rectangles, page by page.
    ///
    /// Passes, in order:
    ///   1. Measure   - intrinsic sizes. Text goes through the measurer; a stacked
    ///                  container measures as the sum of what it manages to place.
    ///   2. Reserve   - header and footer are resolved first, per page, and the body
    ///                  gets the band that is left.
    ///   3. Paginate  - the body's children are stacked into that band until it is
    ///                  full, then a new page opens and the flow resumes where it
    ///                  stopped. Splitting happens at line boundaries for text and at
    ///                  child boundaries for a stacked container.
    ///   4. Arrange   - absolute coordinates inside a band, top-down.
    ///   5. Fields    - page numbers and totals, not implemented yet.
    ///
    /// Arrange works top-down: a parent's rectangle is always known before its children
    /// are placed, so percentages resolve in one go.
    /// </summary>
    public sealed class LayoutResolver
    {
        /// <summary>
        /// Where to resume an item that did not fit on a page. Kept outside the element
        /// tree so nothing has to be cloned to be continued.
        /// </summary>
        private sealed class Continuation
        {
            /// <summary>Index of the first child not placed yet, for a container.</summary>
            public int ChildIndex;

            /// <summary>Index of the first line not placed yet, for text.</summary>
            public int LineIndex;

            /// <summary>Where the child at <see cref="ChildIndex"/> itself stopped.</summary>
            public Continuation? Inner;
        }

        /// <summary>Outcome of flowing one item.</summary>
        private readonly struct FlowOutcome
        {
            /// <summary>Vertical cursor once the item is placed, margin included.</summary>
            public double Bottom { get; }

            /// <summary>Null when the item was fully placed.</summary>
            public Continuation? Rest { get; }

            /// <summary>False when nothing at all could go on this page.</summary>
            public bool Placed { get; }

            public FlowOutcome(double bottom, Continuation? rest, bool placed)
            {
                Bottom = bottom;
                Rest = rest;
                Placed = placed;
            }
        }

        private readonly Report _report;
        private readonly ITextMeasurer _measurer;

        /// <summary>
        /// The section being laid out. Everything that used to read the report's one
        /// section - its padding, its bands, its header and footer - reads this, so a
        /// page belongs to its own section rather than to the first one.
        /// </summary>
        private Section _document;

        /// <summary>
        /// Page index the current section started at, so a page can say where it sits
        /// within its own section. Zero through the first section, which makes
        /// {SectionPage} equal {Page} for a report of one.
        /// </summary>
        private int _sectionFirstPage;

        /// <summary>Millimeters of cap height per point, per typeface. See <see cref="CapHeightPerPoint"/>.</summary>
        private readonly Dictionary<string, double> _capHeightPerPoint = new();

        /// <summary>
        /// Size the metrics are probed at. Font metrics are linear in the size, so any
        /// value works and a single probe covers every size of that typeface.
        /// </summary>
        private const double ProbeSizeInPoints = 100.0;

        /// <summary>
        /// Slack allowed when deciding whether something fits. A band is reserved from the
        /// very content it will hold, so the last item lands exactly on the edge; without
        /// this, a rounding error a millionth of a millimetre wide pushes it to the next
        /// page - or, in a band, drops it altogether.
        /// </summary>
        private const double FitTolerance = 1e-6;

        /// <summary>Runaway guard: a layout that keeps opening pages is a bug, not a long report.</summary>
        public int MaxPages { get; set; } = 5000;

        /// <summary>Stand-in for {Pages} during layout; see <see cref="Resolve"/>.</summary>
        private string _pageCountPlaceholder = "0";

        private DateTimeOffset _timestamp;

        public LayoutResolver(Report report, ITextMeasurer measurer)
        {
            _report = report ?? throw new ArgumentNullException(nameof(report));
            _measurer = measurer ?? throw new ArgumentNullException(nameof(measurer));
            _document = _report.Section;
        }

        /// <summary>
        /// Lays the report out.
        ///
        /// {Pages} is the one value that cannot be known while laying out, and it is also
        /// part of what gets measured. Rather than re-paginating until it settles - which
        /// can oscillate - the layout runs with a placeholder of a fixed digit count. If
        /// the real count needs more digits, the layout is redone with that many, which
        /// converges immediately since only the digit count matters, never the digits.
        /// The real value is then written into the placed fragments.
        /// </summary>
        /// <summary>
        /// Stops the work when the caller has stopped wanting it.
        ///
        /// Polled rather than awaited: none of this is waiting for anything, it is
        /// arithmetic, so there is no point at which a continuation could be resumed.
        /// Cancelling CPU-bound work means asking, in the loops that last, whether the
        /// answer is still wanted - which is why nothing here is async and nothing needs
        /// to be.
        ///
        /// It throws, and produces nothing. A layout abandoned half way is not half a
        /// layout; and a document abandoned half way is worse, because a PDF cut short
        /// still looks like a PDF.
        /// </summary>
        public CancellationToken Cancellation { get; set; } = CancellationToken.None;

        public LayoutResult Resolve()
        {
            _timestamp = _report.GeneratedAt ?? DateTimeOffset.Now;

            // Before anything is measured, because a number widens the heading that
            // carries it - unlike {Pages}, which is known only at the end and is patched
            // into fragments of a fixed width afterwards.
            //
            // Once, and not while placing: the loop below lays the report out up to three
            // times, BalanceColumns searches eight column heights and KeepWithNext rewinds
            // pages. A counter advanced during placement would count every one of those.
            HeadingNumbers.Apply(_report);

            int digits = 1;
            LayoutResult result;

            for (int attempt = 0; ; attempt++)
            {
                _pageCountPlaceholder = new string('0', digits);
                result = LayoutOnce();

                int needed = result.PageCount.ToString(CultureInfo.InvariantCulture).Length;
                if (needed == digits || attempt >= 2)
                    break;
                digits = needed;
            }

            ResolvePageCount(result);

            // After the numbering is settled, never before: a back is not counted, so
            // inserting one earlier would make {Pages} say four where the reader sees two.
            if (_report.BackPage is { } back)
                AddBackPages(result, back);

            return result;
        }

        /// <summary>
        /// Puts a back behind each sheet that asks for one. The content is laid out afresh
        /// per sheet rather than placed once and repeated: it may carry {Page}, and a set
        /// of conditions that names the page it belongs to is the whole reason a back is
        /// allowed to know its number.
        /// </summary>
        private void AddBackPages(LayoutResult result, BackPage back)
        {
            if (back.Content.Childs.Count == 0)
                return;

            var fronts = result.Pages.Where(p => !p.IsBack).ToList();
            int total = fronts.Count;

            // Backwards, so inserting does not shift the fronts still to be handled.
            for (int i = fronts.Count - 1; i >= 0; i--)
            {
                Page front = fronts[i];
                bool wanted = back.Scope switch
                {
                    BackPageScope.FirstPage => front.Number == 1,
                    BackPageScope.LastPage => front.Number == total,
                    _ => true
                };

                if (!wanted)
                    continue;

                // The back is its front turned over: an A3 landscape page cannot have
                // an A4 portrait back. Taken from the front, since the report no longer
                // has one size to give.
                Rect pageFrame = front.Frame;

                int at = result.Pages.ToList().IndexOf(front);
                Page sheet = result.InsertBack(at, pageFrame, front.Number);

                Reset(back.Content);
                foreach (BaseItem item in Descendants(back.Content))
                    Reset(item);

                // {Page} on a back reads as the front does, and {Pages} as the count of
                // fronts - the reader counts what is written on the front.
                LaidOutSection? owning = result.Sections
                    .FirstOrDefault(sec => front.Number - 1 >= sec.FirstPage
                                        && front.Number - 1 < sec.FirstPage + sec.PageCount);

                _backContext = ContextFor(
                    front.Number, total.ToString(CultureInfo.InvariantCulture),
                    owning == null ? front.Number : front.Number - owning.FirstPage,
                    (owning?.PageCount ?? total).ToString(CultureInfo.InvariantCulture));

                Rect area = pageFrame.Deflate(back.Padding);
                SetFrame(back.Content, area);
                sheet.Place(back.Content, area);
                // Stack has to flow, like the body does. Sent down the absolute path a
                // stacked container gives every child the position it never set, so all
                // of them land at the top of the sheet, printed over one another.
                Rect inner = area.Deflate(back.Content.Style.Padding);
                if (back.Content.Layout == LayoutMode.Stack)
                    FlowChildren(back.Content, inner, sheet, null);
                else
                    ArrangeChildren(back.Content, inner, sheet);

                _backContext = null;
            }
        }

        private TokenContext? _backContext;

        private static IEnumerable<BaseItem> Descendants(ContainerItem container)
        {
            foreach (BaseItem child in container.Childs)
            {
                yield return child;
                if (child is ContainerItem inner)
                    foreach (BaseItem deep in Descendants(inner))
                        yield return deep;
            }
        }

        private LayoutResult LayoutOnce()
        {
            var result = new LayoutResult();
            _result = result;
            _bookmarked.Clear();

            foreach (Section section in _report.Sections)
                Reset(section);
            foreach (BaseItem item in _report.AllItems)
                Reset(item);

            // One flow per section, and nothing crosses between them: what does not fit
            // adds pages to the section it belongs to. That is what makes a page size per
            // section tractable - a table can never be half in one size and half in
            // another, because there is no path by which it could get there.
            int safety = 0;

            foreach (Section section in _report.Sections)
            {
                _document = section;
                var pageFrame = new Rect(0, 0, section.Dimension.Width, section.Dimension.Height);

                Body? body = section.Childs.OfType<Body>().FirstOrDefault();
                Continuation? pending = null;
                int firstPage = result.Pages.Count;
                _sectionFirstPage = firstPage;

                do
                {
                    Page page = OpenPage(result, pageFrame);

                    if (body != null && body.Style.Visibility != Visibility.Gone)
                    {
                        Rect band = page.BodyBand;
                        SetFrame(body, band);
                        page.Place(body, band);
                        NoteOutline(body, page, band);

                        Rect area = band.Deflate(body.Style.Padding);
                        pending = body.Layout == LayoutMode.Stack
                            ? FlowChildren(body, area, page, pending)
                            : ArrangeChildren(body, area, page);
                    }
                    else
                    {
                        pending = null;
                    }

                    // Counted over the whole file rather than per section: a section that
                    // loops still drives this up, so one counter catches it wherever it
                    // happens and there is no second number to explain.
                    if (++safety > MaxPages)
                        throw new InvalidOperationException(
                            $"Layout did not converge: more than {MaxPages} pages. An item that never fits and never splits is the usual cause.");

                    // Beside the runaway guard, and for the same reason: this is the loop
                    // that can last. One is the engine deciding it has gone wrong, the
                    // other the caller deciding it no longer cares.
                    Cancellation.ThrowIfCancellationRequested();
                }
                while (pending != null);

                int taken = result.Pages.Count - firstPage;
                result.AddSection(section, firstPage, taken);

                // Declared, not enforced. Laying out is already separable from drawing,
                // so the caller looks at this and decides whether to go on; stating the
                // fact is the engine's part, and choosing is not.
                if (section.PageLimit is { } limit && taken > limit)
                    result.AddIssue(new TemplateIssue(
                        "fits.pageLimit", IssueLevel.Warning,
                        $"Laid out in {taken} pages, where {limit} were allowed for.",
                        section,
                        $"section {_report.Sections.IndexOf(section) + 1}"));
            }

            // Said once for the document, and said by the composer because that is where
            // an imported page is known to be in it - the caller has not chosen a format
            // yet, and this is true of every format they might choose.
            //
            // Declared and refused by nothing, which is the author's own rule: the engine
            // cannot vouch for bytes it did not write, and only the caller knows whether
            // this document was ever meant to claim conformance. A report being assembled
            // to be read has no need of PDF/A at all, and for that use this is noise to
            // be ignored - which is exactly what a Warning is for.
            foreach (BaseItem item in _report.AllItems)
            {
                if (item is not ImportedPageItem imported || imported.Resolved == null)
                    continue;

                result.AddIssue(new TemplateIssue(
                    "fits.importedPage", IssueLevel.Warning,
                    "This document carries a page copied out of another PDF. Nothing here "
                    + "can vouch for what is in it - an unembedded font, transparency a "
                    + "profile forbids - so a conformance claim about the result is yours "
                    + "to make. Validate it, or do not claim.",
                    imported, "Report"));
            }

            _document = _report.Section;
            ApplyPageScope(result);

            // The mark is text, so it goes through the same cascade and the same
            // millimetres-of-cap-height rule as everything else. Settled once here, not
            // per page, and not in the backend.
            if (_report.Watermark is { } mark)
                result.WatermarkFont = ResolveFont(mark.Font, double.NaN);

            return result;
        }

        /// <summary>
        /// Adds a page and reserves its bands. Header and footer are resolved first,
        /// which is what leaves the body a band instead of the whole sheet.
        /// </summary>
        private Page OpenPage(LayoutResult result, Rect pageFrame)
        {
            // The section being laid out, not the report's first one: its padding, its
            // bands, and its own header and footer.
            Section section = _document;
            Page page = result.AddPage(pageFrame);

            SetFrame(section, pageFrame);
            page.Place(section, pageFrame);

            Rect content = pageFrame.Deflate(section.Style.Padding);
            Rect bodyBand = content;

            Header? header = section.Childs.OfType<Header>().FirstOrDefault();
            if (header != null && Arrange(header, content, page))
            {
                page.HeaderBand = FrameOf(header);
                if (!double.IsNaN(page.HeaderBand.Bottom))
                    bodyBand = bodyBand.WithTop(page.HeaderBand.Bottom);
            }

            Footer? footer = section.Childs.OfType<Footer>().FirstOrDefault();
            if (footer != null && Arrange(footer, content, page))
            {
                page.FooterBand = FrameOf(footer);
                if (!double.IsNaN(page.FooterBand.Top))
                    bodyBand = bodyBand.WithBottom(page.FooterBand.Top);
            }

            page.BodyBand = bodyBand;

            // Anything else hung directly off the section - watermarks, frames - is
            // positioned against the whole page and repeated on every page.
            Body? ownBody = section.Childs.OfType<Body>().FirstOrDefault();
            foreach (BaseItem child in section.Childs)
            {
                if (ReferenceEquals(child, header) || ReferenceEquals(child, footer) || ReferenceEquals(child, ownBody))
                    continue;
                Arrange(child, content, page);
            }

            return page;
        }

        private static void Reset(BaseItem item)
        {
            item.Style.RenderedFrame.Reset();
            item.Style.RenderedFrame.Visibility = item.Style.Visibility;
            item.Style.SetDefault4Calc(item is ContainerItem { Childs.Count: > 0 });
        }

        // ------------------------------------------------------------------
        // Pass 3: flow and pagination
        // ------------------------------------------------------------------

        /// <summary>
        /// Stacks the children of <paramref name="container"/> into <paramref name="area"/>,
        /// resuming at <paramref name="resume"/>. Returns where to carry on next page,
        /// or null when everything fit.
        /// </summary>
        /// <summary>
        /// Flows a container's children into <paramref name="area"/>, in one column or
        /// several.
        ///
        /// Columns fall out of the machinery that was already there: what a column
        /// cannot take is a <see cref="Continuation"/>, and a continuation is precisely
        /// what the next column starts from. The last column's leftovers are handed back
        /// unchanged, so they land on the next page exactly as they always did.
        ///
        /// They fill in order rather than balancing. A page of terms and conditions
        /// wants its first column full before the second begins; balancing the last
        /// page of a long text is a nicety, and it would mean laying the whole thing out
        /// twice to find out how tall it is.
        /// </summary>
        private Continuation? FlowChildren(ContainerItem container, Rect area, Page page, Continuation? resume)
        {
            int columns = Math.Max(1, container.ColumnCount);
            if (columns == 1)
                return FlowColumn(container, area, page, resume);

            double gap = Math.Max(0, container.ColumnGap);
            double width = (area.Width - gap * (columns - 1)) / columns;

            // Too narrow to divide: one column is a poor result, a negative one is not a
            // result at all.
            if (width <= 0)
                return FlowColumn(container, area, page, resume);

            // One attempt at full height, which is the answer whenever the content
            // overflows: a page that continues has no spare room to even out.
            int mark = page.Items.Count;
            Continuation? pending = Lay(area.Bottom);

            if (pending != null || !container.BalanceColumns)
                return pending;

            // It all fitted, so this is the last page of the flow and the columns are
            // lopsided - the first full, the last nearly empty. Find the shallowest
            // height that still holds everything, by halving: eight tries settle a
            // page to well under a millimetre, and each is only a re-flow of what has
            // already been measured.
            double tooShort = area.Top;
            double enough = area.Bottom;

            for (int step = 0; step < 8; step++)
            {
                double tryAt = (tooShort + enough) / 2;
                page.Rewind(mark);

                if (Lay(tryAt) == null)
                    enough = tryAt;
                else
                    tooShort = tryAt;
            }

            // The last attempt was not necessarily the one that fitted, so lay it out
            // once more at the height that did.
            page.Rewind(mark);
            Lay(enough);
            return null;

            Continuation? Lay(double bottom)
            {
                Continuation? rest = resume;

                for (int i = 0; i < columns; i++)
                {
                    double left = area.Left + i * (width + gap);
                    rest = FlowColumn(container, new Rect(left, area.Top, left + width, bottom),
                                      page, rest);

                    if (rest == null)
                        return null;
                }

                return rest;
            }
        }

        private Continuation? FlowColumn(ContainerItem container, Rect area, Page page, Continuation? resume)
        {
            IReadOnlyList<BaseItem> children = container.Childs;
            int start = resume?.ChildIndex ?? 0;
            Continuation? inner = resume?.Inner;

            double cursor = area.Top;
            bool placedSomething = false;

            // Where each child's marks begin, so a child that has to follow the next one
            // onto the fresh page can be taken back off this one. Only children placed
            // on this page are recorded; anything resumed from before is not ours to
            // move.
            var placedAt = new Dictionary<int, int>();
            int firstOnPage = -1;

            // Carrying on from a previous page: the heading still open goes back at the
            // top, so the reader knows which group these rows belong to. Only the last
            // one before the resume point - the earlier headings belong to groups that
            // have already been closed off by their own subtotals.
            if (resume != null)
            {
                int open = -1;
                for (int i = 0; i < start && i < children.Count; i++)
                {
                    if (children[i].Style.RepeatOnBreak && children[i].Style.Visibility != Visibility.Gone)
                        open = i;
                }

                if (open >= 0)
                {
                    cursor = Flow(children[open], area, cursor, page, null, force: true).Bottom;
                    placedSomething = true;
                }
            }

            for (int i = start; i < children.Count; i++)
            {
                BaseItem child = children[i];

                if (child.Style.Visibility == Visibility.Gone)
                    continue;

                bool breakBefore = child.Style.PageBreak is PageBreak.Before or PageBreak.BeforeAndAfter;
                if (breakBefore && placedSomething)
                    return WithKept(i);

                // An item that fits on no page at all would loop forever, so the first
                // item of a page is placed even when it overflows the band.
                bool force = !placedSomething;

                placedAt[i] = page.Items.Count;
                if (firstOnPage < 0)
                    firstOnPage = i;

                FlowOutcome outcome = Flow(child, area, cursor, page, i == start ? inner : null, force);

                if (!outcome.Placed)
                {
                    placedAt.Remove(i);
                    return WithKept(i, i == start ? inner : null);
                }

                cursor = outcome.Bottom;
                placedSomething = true;

                // A child that continues onto the next page is not stranded - part of it
                // is already there - so nothing before it needs to follow.
                if (outcome.Rest != null)
                    return new Continuation { ChildIndex = i, Inner = outcome.Rest };

                bool breakAfter = child.Style.PageBreak is PageBreak.After or PageBreak.BeforeAndAfter;
                if (breakAfter && i + 1 < children.Count)
                    return new Continuation { ChildIndex = i + 1 };
            }

            return null;

            // Moves the break earlier while the item before it asked not to be left at
            // the foot of the page, and takes back what was drawn for the ones that go
            // with it.
            //
            // Walks back rather than stepping once: a heading, a rule and a caption in a
            // row all ask for it, and moving only the last would strand the other two -
            // which is the same fault one item further up.
            //
            // Never past the first item placed on this page. Moving what is already at
            // the top gains nothing, and without that floor a run of items all asking
            // for it would push one another off the page forever.
            Continuation WithKept(int breakAt, Continuation? within = null)
            {
                int at = breakAt;

                while (at - 1 > firstOnPage
                       && placedAt.ContainsKey(at - 1)
                       && children[at - 1].Style.KeepWithNext
                       && children[at - 1].Style.Visibility != Visibility.Gone)
                {
                    at--;
                }

                if (at == breakAt)
                    return new Continuation { ChildIndex = breakAt, Inner = within };

                page.Rewind(placedAt[at]);
                return new Continuation { ChildIndex = at };
            }
        }

        /// <summary>
        /// Places one item at <paramref name="top"/> inside <paramref name="area"/>.
        /// <paramref name="force"/> places it even if it overflows, which is how the
        /// flow guarantees progress when nothing fits.
        /// </summary>
        private FlowOutcome Flow(BaseItem item, Rect area, double top, Page page, Continuation? resume, bool force)
        {
            item.Style.RenderedFrame.Visibility = item.Style.Visibility;

            EdgeSize margin = item.Style.Margin;
            Rect column = new(area.Left + margin.Left, area.Top, area.Right - margin.Right, area.Bottom);
            double itemTop = top + margin.Top;

            ComputeHorizontal(item, column, out double left, out double right);
            if (double.IsNaN(left))
                left = column.Left;
            if (double.IsNaN(right))
                right = column.Right;

            double available = area.Bottom - itemTop;

            // An explicit height wins over any intrinsic measurement.
            double requested = double.IsNaN(item.Style.Location4Calc.HeightValue)
                ? double.NaN
                : ToMillimeters(item.Style.Location4Calc.HeightValue, item.Style.Location4Calc.HeightUnit, area.Height);

            switch (item)
            {
                case TextItem text:
                    return FlowText(text, area, column, left, right, itemTop, available, requested, page, resume, force);

                case TableItem table:
                    return FlowTable(table, area, left, right, itemTop, page, resume, force);

                case ImageGridItem grid:
                    return FlowImageGrid(grid, area, left, right, itemTop, page, resume, force);

                case AdvancedTextItem advanced:
                    return FlowAdvancedText(advanced, area, left, right, itemTop, page, resume, force);

                case ContainerItem { Layout: LayoutMode.Stack } stack:
                    return FlowStack(stack, area, left, right, itemTop, requested, page, resume, force);

                default:
                    return FlowBlock(item, area, left, right, itemTop, available, requested, page, force);
            }
        }

        private FlowOutcome FlowText(TextItem text, Rect area, Rect column, double left, double right,
                                     double top, double available, double requestedHeight,
                                     Page page, Continuation? resume, bool force)
        {
            TextStyle font = ResolveFont(text.Font, requestedHeight);

            // Padding is part of the box: the lines break inside it, and every fragment
            // of a split paragraph carries it on both edges.
            EdgeSize padding = text.Style.Padding;
            double frame = padding.Top + padding.Bottom;
            double width = Math.Max(0, right - left - padding.Left - padding.Right);

            string resolved = TextOf(text, page);
            IReadOnlyList<string> lines = _measurer.BreakLines(resolved, font, width);
            double lineHeight = _measurer.GetMetrics(font).LineHeight;

            int first = resume?.LineIndex ?? 0;
            int remaining = Math.Max(0, lines.Count - first);

            double full = !double.IsNaN(requestedHeight) ? requestedHeight : remaining * lineHeight + frame;

            // A fixed height is a promise about the frame; splitting it would break it.
            bool splittable = CanSplit(text) && double.IsNaN(requestedHeight);

            if (full <= available + FitTolerance || !splittable)
            {
                if (full > available + FitTolerance && !force)
                    return new FlowOutcome(top, resume, false);

                PlaceLines(text, page, left, top, right, top + full, font, lines, first, remaining, width);
                return new FlowOutcome(top + full + text.Style.Margin.Bottom, null, true);
            }

            int fitting = lineHeight > 0 ? (int)Math.Floor((available - frame + FitTolerance) / lineHeight) : 0;
            if (fitting <= 0)
            {
                if (!force)
                    return new FlowOutcome(top, resume, false);
                fitting = 1;
            }

            if (fitting >= remaining)
            {
                double height = remaining * lineHeight + frame;
                PlaceLines(text, page, left, top, right, top + height, font, lines, first, remaining, width);
                return new FlowOutcome(top + height + text.Style.Margin.Bottom, null, true);
            }

            double part = fitting * lineHeight + frame;
            PlaceLines(text, page, left, top, right, top + part, font, lines, first, fitting, width);
            return new FlowOutcome(top + part,
                                   new Continuation { LineIndex = first + fitting },
                                   true);
        }

        // ------------------------------------------------------------------
        // Advanced (multi-style) text
        // ------------------------------------------------------------------

        /// <summary>One already-measured, already-styled piece of a line.</summary>
        private sealed class AdvancedPiece
        {
            public TextStyle Font = null!;
            public string Text = "";
            public double Width;
        }

        /// <summary>
        /// A visual line, built from pieces of one or more runs. Unlike a plain
        /// <see cref="TextItem"/>'s lines, which all share one font and so one height,
        /// each of these can be a different height - whichever piece on it asked for
        /// the most room - which is why pagination sums heights rather than
        /// multiplying by a constant.
        /// </summary>
        private sealed class AdvancedLine
        {
            public readonly List<AdvancedPiece> Pieces = new();

            /// <summary>Tallest line height among this line's pieces.</summary>
            public double Height;

            /// <summary>
            /// Baseline, measured from the top of the line - "the cap is centred in its
            /// line" applied to the *tallest* piece, so every other piece on the line
            /// sits on that same baseline rather than being centred in its own right.
            /// </summary>
            public double BaselineOffset;

            public double Width;
        }

        /// <summary>
        /// Lays a paragraph of runs out row by row, <see cref="ImageGridItem.RowHeight"/>-
        /// style except the row height is not fixed: it comes from whichever piece on
        /// the row asked for the most room. A row is never split - the same rule a
        /// table row and an image-grid row already follow - so it either fits whole or
        /// moves to the next page entire.
        /// </summary>
        private FlowOutcome FlowAdvancedText(AdvancedTextItem item, Rect area, double left, double right,
                                             double top, Page page, Continuation? resume, bool force)
        {
            EdgeSize padding = item.Style.Padding;
            double frame = padding.Top + padding.Bottom;
            double width = Math.Max(0, right - left - padding.Left - padding.Right);
            double available = area.Bottom - top;

            List<AdvancedLine> lines = BreakAdvancedText(item, width, page);

            int first = resume?.LineIndex ?? 0;
            int remaining = Math.Max(0, lines.Count - first);

            double RemainingHeight(int from, int count)
            {
                double sum = 0;
                for (int i = from; i < from + count && i < lines.Count; i++)
                    sum += lines[i].Height;
                return sum;
            }

            double full = RemainingHeight(first, remaining) + frame;
            bool splittable = CanSplit(item);

            if (full <= available + FitTolerance || !splittable)
            {
                if (full > available + FitTolerance && !force)
                    return new FlowOutcome(top, resume, false);

                PlaceAdvancedLines(item, page, left, top, right, lines, first, remaining);
                return new FlowOutcome(top + full + item.Style.Margin.Bottom, null, true);
            }

            int fitting = 0;
            double used = 0;
            for (int i = first; i < lines.Count; i++)
            {
                if (used + lines[i].Height > available - frame + FitTolerance)
                    break;
                used += lines[i].Height;
                fitting++;
            }

            if (fitting <= 0)
            {
                if (!force)
                    return new FlowOutcome(top, resume, false);
                fitting = 1;
                used = lines[first].Height;
            }

            if (fitting >= remaining)
            {
                double height = used + frame;
                PlaceAdvancedLines(item, page, left, top, right, lines, first, remaining);
                return new FlowOutcome(top + height + item.Style.Margin.Bottom, null, true);
            }

            double part = used + frame;
            PlaceAdvancedLines(item, page, left, top, right, lines, first, fitting);
            return new FlowOutcome(top + part, new Continuation { LineIndex = first + fitting }, true);
        }

        /// <summary>
        /// Breaks a paragraph of runs into lines. Reused across the pages the
        /// paragraph spans - the width does not change from one page to the next, so
        /// there is exactly one way to break it, computed once.
        ///
        /// Wraps at word boundaries, the same "a word plus the spaces that follow it"
        /// rule <see cref="FontTextMeasurer"/> uses for a plain <see cref="TextItem"/>,
        /// generalised across runs: a word never carries two styles, but a line
        /// commonly does. A single word wider than the whole line is placed on its own
        /// line rather than broken inside - the exact preview a plain TextItem gives a
        /// word like that too, since only <see cref="FontTextMeasurer.BreakWord"/>
        /// (private to the measurer) goes further, and reaching into it here would
        /// mean measuring the same word twice for a case that does not come up in
        /// ordinary body text.
        /// </summary>
        private List<AdvancedLine> BreakAdvancedText(AdvancedTextItem item, double width, Page page)
        {
            var lines = new List<AdvancedLine>();
            bool wrap = item.Font.Wrap != false && width > 0 && !double.IsInfinity(width);

            var current = new AdvancedLine();
            double currentWidth = 0;

            void CloseLine()
            {
                MergeAdjacentPieces(current);
                double tallestLine = 0, tallestCap = 0;
                foreach (AdvancedPiece piece in current.Pieces)
                {
                    FontMetrics metrics = _measurer.GetMetrics(piece.Font);
                    if (metrics.LineHeight >= tallestLine)
                    {
                        tallestLine = metrics.LineHeight;
                        tallestCap = metrics.CapHeight;
                    }
                }
                current.Height = tallestLine;
                current.BaselineOffset = (tallestLine + tallestCap) / 2;
                foreach (AdvancedPiece piece in current.Pieces)
                    current.Width += piece.Width;
                lines.Add(current);
                current = new AdvancedLine();
                currentWidth = 0;
            }

            foreach (TextRun run in item.Runs)
            {
                if (string.IsNullOrEmpty(run.Text))
                    continue;

                TextStyle font = ResolveFont(run.Font?.Over(item.Font) ?? item.Font, double.NaN);
                font = font.Clone();
                font.HorizontalAlignment = HorizontalAlignment.Left;   // this item places it, not DrawText
                string text = TextOf(run.Text, page, item);

                string[] paragraphs = text.Split('\n');
                for (int p = 0; p < paragraphs.Length; p++)
                {
                    if (p > 0)
                        CloseLine();          // a hard break always starts a fresh line

                    string paragraph = paragraphs[p];
                    int start = 0;

                    while (start < paragraph.Length)
                    {
                        int end = start;
                        while (end < paragraph.Length && !LineBreaks.At(paragraph[end]))
                            end++;
                        while (end < paragraph.Length && LineBreaks.At(paragraph[end]))
                            end++;

                        string word = paragraph[start..end];
                        double wordWidth = _measurer.Measure(word, font, double.PositiveInfinity).Width;

                        // Trailing spaces on the word being tested must not be what tips
                        // it over the edge - only what a reader would see cross the margin
                        // counts, the same allowance FontTextMeasurer.WrapParagraph makes.
                        string trimmedWord = word.TrimEnd();
                        double checkWidth = trimmedWord.Length == word.Length
                            ? wordWidth
                            : _measurer.Measure(trimmedWord, font, double.PositiveInfinity).Width;

                        if (wrap && current.Pieces.Count > 0 && currentWidth + checkWidth > width + FitTolerance)
                            CloseLine();

                        current.Pieces.Add(new AdvancedPiece { Font = font, Text = word, Width = wordWidth });
                        currentWidth += wordWidth;
                        start = end;
                    }
                }
            }

            CloseLine();

            // An empty paragraph is still one line - a blank AdvancedTextItem takes the
            // same one line of room an empty plain TextItem would.
            if (lines.Count == 0)
            {
                TextStyle font = ResolveFont(item.Font, double.NaN);
                FontMetrics metrics = _measurer.GetMetrics(font);
                lines.Add(new AdvancedLine { Height = metrics.LineHeight,
                                             BaselineOffset = (metrics.LineHeight + metrics.CapHeight) / 2 });
            }

            return lines;
        }

        /// <summary>
        /// Consecutive pieces from the same run collapse into one - the split into
        /// words was only ever for deciding where a line breaks, not something a
        /// reader or a PDF operator needs to see. Trims the trailing space a line
        /// otherwise ends on, the same tidying <see cref="FontTextMeasurer"/> already
        /// does for a plain wrapped line.
        /// </summary>
        private void MergeAdjacentPieces(AdvancedLine line)
        {
            for (int i = line.Pieces.Count - 1; i > 0; i--)
            {
                if (!ReferenceEquals(line.Pieces[i].Font, line.Pieces[i - 1].Font))
                    continue;

                line.Pieces[i - 1].Text += line.Pieces[i].Text;
                line.Pieces[i - 1].Width += line.Pieces[i].Width;
                line.Pieces.RemoveAt(i);
            }

            if (line.Pieces.Count > 0)
            {
                AdvancedPiece last = line.Pieces[^1];
                string trimmed = last.Text.TrimEnd();
                if (trimmed.Length != last.Text.Length)
                {
                    last.Text = trimmed;
                    last.Width = _measurer.Measure(trimmed, last.Font, double.PositiveInfinity).Width;
                }
            }
        }

        /// <summary>Places the given range of already-broken lines, one draw per piece.</summary>
        private void PlaceAdvancedLines(AdvancedTextItem item, Page page, double left, double top,
                                        double right, List<AdvancedLine> lines, int first, int count)
        {
            EdgeSize padding = item.Style.Padding;
            double lineLeft = left + padding.Left;
            double lineRight = right - padding.Right;
            double cursor = top + padding.Top;

            for (int i = first; i < first + count && i < lines.Count; i++)
            {
                AdvancedLine line = lines[i];

                HorizontalAlignment alignment = item.Font.HorizontalAlignment ?? HorizontalAlignment.Left;

                // The last line of a block is never stretched - a closing line pulled
                // out to the margin is what tells a reader the paragraph is over. Same
                // rule, same reason, as a plain text's.
                bool justify = alignment == HorizontalAlignment.Justify
                            && i < lines.Count - 1
                            && lineRight > lineLeft;

                double startX = alignment switch
                {
                    HorizontalAlignment.Center => lineLeft + Math.Max(0, (lineRight - lineLeft - line.Width) / 2),
                    HorizontalAlignment.Right => lineRight - line.Width,
                    _ => lineLeft
                };

                double baselineY = cursor + line.BaselineOffset;
                double x = startX;

                // What each space on this line grows by. Counted across the whole line
                // rather than per piece: the gaps a reader sees do not stop at a run
                // boundary, and stretching each piece to its own share would open the
                // gaps unevenly wherever a word changed style mid-sentence.
                double spacePad = justify ? SpacePadOf(line, lineRight - lineLeft) : 0;

                foreach (AdvancedPiece piece in line.Pieces)
                {
                    FontMetrics metrics = _measurer.GetMetrics(piece.Font);
                    double pieceFirst = (metrics.LineHeight + metrics.CapHeight) / 2;
                    double pieceTop = baselineY - pieceFirst;

                    if (spacePad > 0 && piece.Text.Contains(' '))
                    {
                        // A justified piece is placed word by word, and its spaces are
                        // gaps rather than glyphs. That keeps the stretching entirely in
                        // the geometry: no backend learns anything, and the two outputs
                        // cannot disagree about a line the way they could if each worked
                        // out its own spacing.
                        x = PlaceJustifiedPiece(piece, page, x, pieceTop, metrics, spacePad);
                        continue;
                    }

                    var synthetic = new TextItem { Text = piece.Text, Font = piece.Font };
                    Place(synthetic, page, x, pieceTop, x + piece.Width, pieceTop + metrics.LineHeight,
                         piece.Text, piece.Font);
                    x += piece.Width;
                }

                cursor += line.Height;
            }
        }

        /// <summary>
        /// Extra millimetres per space so this line reaches the far margin, or zero when
        /// it should be left alone. The same arithmetic <see cref="Justification"/> does
        /// for a plain text, over a line assembled from several runs.
        /// </summary>
        private static double SpacePadOf(AdvancedLine line, double available)
        {
            int spaces = 0;
            foreach (AdvancedPiece piece in line.Pieces)
            {
                foreach (char c in piece.Text)
                {
                    if (c == ' ') spaces++;
                }
            }

            if (spaces == 0) return 0;

            // Already at or past the margin: a line that would have to be squeezed was
            // broken wrongly, and closing its gaps would only hide that.
            double slack = available - line.Width;
            return slack <= 0 ? 0 : slack / spaces;
        }

        /// <summary>
        /// Places one piece of a justified line, a word at a time, and answers where the
        /// next piece starts. The spaces are not drawn - a space draws nothing anyway -
        /// so widening them is a matter of where the next word is put.
        /// </summary>
        private double PlaceJustifiedPiece(AdvancedPiece piece, Page page, double x, double top,
                                           FontMetrics metrics, double spacePad)
        {
            double spaceWidth = _measurer.Measure(" ", piece.Font, double.PositiveInfinity).Width;

            int at = 0;
            while (at < piece.Text.Length)
            {
                int space = piece.Text.IndexOf(' ', at);
                string word = space < 0 ? piece.Text[at..] : piece.Text[at..space];

                if (word.Length > 0)
                {
                    double width = _measurer.Measure(word, piece.Font, double.PositiveInfinity).Width;
                    var synthetic = new TextItem { Text = word, Font = piece.Font };
                    Place(synthetic, page, x, top, x + width, top + metrics.LineHeight,
                          word, piece.Font);
                    x += width;
                }

                if (space < 0) break;

                // Every run of spaces counts, so a double space stays a double space -
                // wider, but still twice the single one.
                int run = 0;
                while (space + run < piece.Text.Length && piece.Text[space + run] == ' ') run++;
                x += run * (spaceWidth + spacePad);
                at = space + run;
            }

            return x;
        }

        private FlowOutcome FlowStack(ContainerItem stack, Rect area, double left, double right,
                                      double top, double requestedHeight, Page page,
                                      Continuation? resume, bool force)
        {
            double bottomLimit = double.IsNaN(requestedHeight)
                ? area.Bottom
                : Math.Min(area.Bottom, top + requestedHeight);

            // A container told to stay whole stays whole.
            //
            // Splitting.KeepTogether was consulted in the two text paths and nowhere
            // else, so on a stacked container it was a legal value with no effect: the
            // block split exactly as it would have anyway. Found by asking a heading and
            // its caption to travel with what they describe, and watching them not.
            //
            // Measured first: a block that Auto splits across a page split identically
            // with KeepTogether set. CLAUDE.md §10 in a new place - a value the model
            // accepts and the engine ignores in silence.
            if (!CanSplit(stack) && !force)
            {
                double wanted = ChildHeight(stack, right - left, page);
                if (wanted > bottomLimit - top + FitTolerance)
                    return new FlowOutcome(top, resume, false);
            }

            // The frame is only known once the children have been placed, so reserve the
            // slot now to keep the container behind its children in draw order.
            int before = page.Items.Count;
            var provisional = new Rect(left, top, right, top);
            PlacedItem slot = page.Place(stack, provisional);
            NoteOutline(stack, page, provisional);

            Rect inner = new Rect(left, top, right, bottomLimit).Deflate(stack.Style.Padding);
            Continuation? rest = FlowChildren(stack, inner, page, resume);

            // Not even one child fit: take the slot back rather than leave an empty
            // rectangle behind, and let the caller open a page.
            if (page.Items.Count == before + 1 && rest != null && !force)
            {
                page.RemoveAll(placed => ReferenceEquals(placed, slot));
                return new FlowOutcome(top, resume, false);
            }

            double bottom = double.IsNaN(requestedHeight)
                ? Math.Max(top, LowestBottom(page, slot) + stack.Style.Padding.Bottom)
                : top + requestedHeight;

            slot.Frame = new Rect(left, top, right, bottom);
            SetFrame(stack, slot.Frame);

            return new FlowOutcome(bottom + stack.Style.Margin.Bottom, rest, true);
        }

        private FlowOutcome FlowBlock(BaseItem item, Rect area, double left, double right,
                                      double top, double available, double requestedHeight,
                                      Page page, bool force)
        {
            // A stated height wins; otherwise the item is measured. Containers measure
            // through ContainerHeight, so an absolute group is as tall as the lowest
            // edge its children reach.
            double height = double.IsNaN(requestedHeight)
                ? IntrinsicHeight(item, right - left, page)
                : requestedHeight;

            if (height > available + FitTolerance && !force)
                return new FlowOutcome(top, null, false);

            PlacedItem placed = Place(item, page, left, top, right, top + height, null, null);

            // Placing a container is only half the job: its children still have to be
            // laid out inside it. Forgetting this left every group with a stated height
            // drawn as an empty box.
            if (item is ContainerItem container)
                ArrangeChildren(container, placed.Frame.Deflate(item.Style.Padding), page);

            return new FlowOutcome(placed.Frame.Bottom + item.Style.Margin.Bottom, null, true);
        }

        // ------------------------------------------------------------------
        // Tables
        // ------------------------------------------------------------------

        /// <summary>
        /// Lays a table out row by row. Rows are never cut in half: the table stops at
        /// the last row that fits and continues on the next page, where the header rows
        /// are re-emitted first if <see cref="TableItem.RepeatHeader"/> is set.
        /// </summary>
        private FlowOutcome FlowTable(TableItem table, Rect area, double left, double right,
                                      double top, Page page, Continuation? resume, bool force)
        {
            int before = page.Items.Count;
            PlacedItem slot = page.Place(table, new Rect(left, top, right, top));

            Rect inner = new Rect(left, top, right, area.Bottom).Deflate(table.Style.Padding);
            double[] edges = ColumnEdges(table, inner.Left, inner.Right, page);

            var header = new List<TableRow>();
            var body = new List<TableRow>();
            foreach (BaseItem child in table.Childs)
            {
                if (child is not TableRow row || row.Style.Visibility == Visibility.Gone)
                    continue;
                (row.IsHeader ? header : body).Add(row);
            }

            double cursor = inner.Top;
            int start = resume?.ChildIndex ?? 0;

            // The header goes on the first page always, and on the following ones only
            // when asked for.
            if (header.Count > 0 && (start == 0 || table.RepeatHeader))
            {
                foreach (TableRow row in header)
                    cursor = PlaceRow(row, edges, cursor, inner.Right, page);
            }

            int placed = 0;
            int index = start;
            int skip = resume?.LineIndex ?? 0;
            int carry = 0;                                   // lines the next page resumes at

            for (; index < body.Count; index++)
            {
                TableRow row = body[index];
                double height = RowHeight(row, edges, page, skip);

                if (cursor + height <= area.Bottom + FitTolerance || (force && placed == 0 && skip == 0))
                {
                    cursor = PlaceRow(row, edges, cursor, inner.Right, page, skip, int.MaxValue, out _);
                    placed++;
                    skip = 0;
                    continue;
                }

                // The row does not fit whole. A long description should not push the
                // whole line to the next page and leave a hole at the bottom, so the
                // cells are cut at a line boundary and the row carries on overleaf.
                int lines = RowLineCount(row, edges, page, skip);
                int fits = lines > 0 ? LinesThatFit(row, edges, page, skip, area.Bottom - cursor) : 0;

                // Not CanSplit: that asks whether a container flows, and a row is laid
                // out absolutely by nature. What decides here is whether the author asked
                // for the row to be kept whole.
                if (row.Style.Splitting == Splitting.KeepTogether || fits <= 0 || fits >= lines)
                    break;

                cursor = PlaceRow(row, edges, cursor, inner.Right, page, skip, fits, out int shown);
                placed++;
                carry = skip + shown;
                break;
            }

            // Not one row fit: give the slot back and let the caller open a page.
            if (placed == 0 && index < body.Count && !force)
            {
                page.RemoveAll(p => ReferenceEquals(p, slot));
                while (page.Items.Count > before)
                    page.RemoveAll(p => ReferenceEquals(p, page.Items[page.Items.Count - 1]));
                return new FlowOutcome(top, resume, false);
            }

            double bottom = cursor + table.Style.Padding.Bottom;
            slot.Frame = new Rect(left, top, right, bottom);
            SetFrame(table, slot.Frame);
            NoteOutline(table, page, slot.Frame);

            Continuation? rest = index < body.Count
                ? new Continuation { ChildIndex = index, LineIndex = carry }
                : null;
            return new FlowOutcome(bottom + table.Style.Margin.Bottom, rest, true);
        }

        // ------------------------------------------------------------------
        // Image grids
        // ------------------------------------------------------------------

        /// <summary>
        /// Lays a grid of pictures out row by row, <see cref="ImageGridItem.GridColumns"/>
        /// to a row, each row <see cref="ImageGridItem.RowHeight"/> tall. Simpler than a
        /// table's: every row is the same height, and nothing inside one can be cut - a
        /// picture is not text, so there is no line to break at - which means a row
        /// either fits whole or moves to the next page entire.
        /// </summary>
        private FlowOutcome FlowImageGrid(ImageGridItem grid, Rect area, double left, double right,
                                          double top, Page page, Continuation? resume, bool force)
        {
            int before = page.Items.Count;
            PlacedItem slot = page.Place(grid, new Rect(left, top, right, top));

            Rect inner = new Rect(left, top, right, area.Bottom).Deflate(grid.Style.Padding);

            int columns = Math.Max(1, grid.GridColumns);
            double gap = Math.Max(0, grid.Gap);
            double cellWidth = Math.Max(0, (inner.Right - inner.Left - gap * (columns - 1)) / columns);
            double rowHeight = Math.Max(0, grid.RowHeight);

            var visible = new List<BaseItem>();
            foreach (BaseItem child in grid.Childs)
            {
                if (child.Style.Visibility != Visibility.Gone)
                    visible.Add(child);
            }

            int index = resume?.ChildIndex ?? 0;
            int placedRows = 0;
            double cursor = inner.Top;          // top of the row about to be tried
            double contentBottom = inner.Top;   // bottom of the last row actually placed

            for (; index < visible.Count; index += columns)
            {
                double rowTop = cursor;
                double rowBottom = rowTop + rowHeight;

                if (!(rowBottom <= area.Bottom + FitTolerance || (force && placedRows == 0)))
                    break;

                int end = Math.Min(index + columns, visible.Count);
                for (int i = index; i < end; i++)
                {
                    BaseItem cell = visible[i];
                    int column = i - index;
                    double cellLeft = inner.Left + column * (cellWidth + gap);
                    double cellRight = cellLeft + cellWidth;
                    EdgeSize margin = cell.Style.Margin;

                    Place(cell, page, cellLeft + margin.Left, rowTop + margin.Top,
                          cellRight - margin.Right, rowBottom - margin.Bottom, null, null);
                }

                contentBottom = rowBottom;
                cursor = rowBottom + gap;
                placedRows++;
            }

            // Not even one row fit: give the slot back and let the caller open a page.
            if (placedRows == 0 && index < visible.Count && !force)
            {
                page.RemoveAll(p => ReferenceEquals(p, slot));
                while (page.Items.Count > before)
                    page.RemoveAll(p => ReferenceEquals(p, page.Items[page.Items.Count - 1]));
                return new FlowOutcome(top, resume, false);
            }

            double bottom = contentBottom + grid.Style.Padding.Bottom;
            slot.Frame = new Rect(left, top, right, bottom);
            SetFrame(grid, slot.Frame);
            NoteOutline(grid, page, slot.Frame);

            Continuation? rest = index < visible.Count
                ? new Continuation { ChildIndex = index }
                : null;
            return new FlowOutcome(bottom + grid.Style.Margin.Bottom, rest, true);
        }

        /// <summary>
        /// Column boundaries.
        ///
        /// Fixed and percentage widths are honoured as given. A fitted column is measured
        /// against its own cells and takes what it needs, within its bounds. Whatever is
        /// left over is shared equally among the plain auto columns - and if the fitted
        /// columns alone would overrun the table, they are scaled back together rather
        /// than one of them being starved.
        /// </summary>
        private double[] ColumnEdges(TableItem table, double left, double right, Page page)
        {
            int count = Math.Max(1, table.Columns.Count);
            double total = right - left;
            var widths = new double[count];
            double fixedTotal = 0;
            double fitTotal = 0;
            int autos = 0;

            for (int i = 0; i < count; i++)
            {
                TableColumn? column = i < table.Columns.Count ? table.Columns[i] : null;

                if (column != null && !column.Auto)
                {
                    widths[i] = ToMillimeters(column.Width, column.WidthUnit, total);
                    fixedTotal += widths[i];
                    continue;
                }

                if (column is { Fit: true })
                {
                    double needed = FittedWidth(table, i, page);
                    if (column.MinWidth > 0)
                        needed = Math.Max(needed, column.MinWidth);
                    if (column.MaxWidth > 0)
                        needed = Math.Min(needed, column.MaxWidth);

                    widths[i] = needed;
                    fitTotal += needed;
                    continue;
                }

                widths[i] = double.NaN;
                autos++;
            }

            // Not enough room for what the fitted columns asked: they share what there is,
            // in proportion, so the table still ends where it should.
            double spare = total - fixedTotal - fitTotal;
            if (spare < 0 && fitTotal > 0)
            {
                double scale = Math.Max(0, total - fixedTotal) / fitTotal;
                for (int i = 0; i < count; i++)
                {
                    if (i < table.Columns.Count && table.Columns[i] is { Fit: true })
                        widths[i] *= scale;
                }
                spare = 0;
            }

            if (autos > 0)
            {
                double share = Math.Max(0, spare) / autos;
                for (int i = 0; i < count; i++)
                {
                    if (double.IsNaN(widths[i]))
                        widths[i] = share;
                }
            }

            var edges = new double[count + 1];
            edges[0] = left;
            for (int i = 0; i < count; i++)
                edges[i + 1] = edges[i] + widths[i];
            return edges;
        }

        /// <summary>Width the widest cell of a column would like, insets included.</summary>
        private double FittedWidth(TableItem table, int column, Page page)
        {
            double widest = 0;

            foreach (BaseItem child in table.Childs)
            {
                if (child is not TableRow row || row.Style.Visibility == Visibility.Gone
                    || column >= row.Childs.Count)
                    continue;

                BaseItem cell = row.Childs[column];
                if (cell.Style.Visibility == Visibility.Gone)
                    continue;

                widest = Math.Max(widest,
                    IntrinsicWidth(cell, page)
                    + cell.Style.Margin.Left + cell.Style.Margin.Right
                    + row.Style.Padding.Left + row.Style.Padding.Right);
            }

            return widest;
        }

        /// <summary>
        /// Width an item wants when nothing constrains it: text on one line, an image at
        /// its natural size. A percentage is skipped, being a share of the very width
        /// this is working out.
        /// </summary>
        private double IntrinsicWidth(BaseItem item, Page page)
        {
            FrameDimension loc = item.Style.Location;
            if (!double.IsNaN(loc.WidthValue) && loc.WidthUnit == Units.Millimeter)
                return loc.WidthValue;

            EdgeSize padding = item.Style.Padding;
            double width;

            switch (item)
            {
                case TextItem text:
                {
                    TextStyle font = ResolveFont(text.Font, double.NaN);
                    width = _measurer.Measure(TextOf(text, page), font, double.PositiveInfinity).Width;
                    break;
                }

                case ImageItem image:
                    width = double.IsNaN(image.NaturalWidth) ? 0 : image.NaturalWidth;
                    break;

                // A page states its own size, which a picture never does here - the
                // engine decodes no image, so it only ever knows what a template told it.
                // Nothing resolved means nothing read, and nothing to size by.
                case ImportedPageItem imported:
                    width = imported.Resolved?.Width ?? 0;
                    break;

                case FieldItem field:
                    // Nothing is typed yet, so there is nothing to measure: a fitted
                    // column sizes a field from what it was given, or a sensible box.
                    width = field.Kind == FieldKind.Checkbox
                        ? _measurer.GetMetrics(ResolveFont(field.Font, double.NaN)).LineHeight
                        : 40;
                    break;

                case ContainerItem container:
                {
                    width = 0;
                    foreach (BaseItem child in container.Childs)
                    {
                        double offset = !double.IsNaN(child.Style.Location.LeftValue)
                                        && child.Style.Location.LeftUnit == Units.Millimeter
                            ? child.Style.Location.LeftValue
                            : 0;

                        width = Math.Max(width, offset + IntrinsicWidth(child, page)
                                                + child.Style.Margin.Left + child.Style.Margin.Right);
                    }
                    break;
                }

                default:
                    width = 0;
                    break;
            }

            return width + padding.Left + padding.Right;
        }

        /// <summary>Lays rows out one under the other, and returns where they end.</summary>
        private double LayRows(TableItem table, Rect inner, Page page, IEnumerable<TableRow> rows)
        {
            double[] edges = ColumnEdges(table, inner.Left, inner.Right, page);
            double cursor = inner.Top;

            foreach (TableRow row in rows)
            {
                if (row.Style.Visibility == Visibility.Gone)
                    continue;
                cursor = PlaceRow(row, edges, cursor, inner.Right, page);
            }

            return cursor;
        }

        /// <summary>Tallest cell decides, unless the row states its own height.</summary>
        /// <summary>
        /// How many lines of a row fit in the room left. Binary search would be overkill:
        /// a row has few lines, and the height is not linear in them because cells differ.
        /// </summary>
        private int LinesThatFit(TableRow row, double[] edges, Page page, int skip, double room)
        {
            int lines = RowLineCount(row, edges, page, skip);
            for (int count = lines; count > 0; count--)
                if (PartHeight(row, edges, page, skip, count) <= room + FitTolerance)
                    return count;

            return 0;
        }

        private double RowHeight(TableRow row, double[] edges, Page page) => RowHeight(row, edges, page, 0);

        /// <summary>
        /// Height of a row, counting each text cell from <paramref name="skip"/> lines in.
        /// A row that has already shown its first lines on the previous page is measured
        /// on what is left, which is what lets the remainder be sized honestly rather
        /// than reserving room for text that is no longer there.
        /// </summary>
        private double RowHeight(TableRow row, double[] edges, Page page, int skip)
        {
            if (!double.IsNaN(row.Style.Location4Calc.HeightValue))
                return ToMillimeters(row.Style.Location4Calc.HeightValue, row.Style.Location4Calc.HeightUnit, 0);

            double tallest = 0;
            IReadOnlyList<BaseItem> cells = row.Childs;
            EdgeSize inset = row.Style.Padding;

            for (int i = 0; i < cells.Count && i + 1 < edges.Length; i++)
            {
                BaseItem cell = cells[i];
                if (cell.Style.Visibility == Visibility.Gone)
                    continue;

                // The row padding insets every cell, on all four sides.
                double width = edges[i + 1] - edges[i] - inset.Left - inset.Right
                               - cell.Style.Margin.Left - cell.Style.Margin.Right;

                double height;
                if (skip > 0 && cell is TextItem text)
                {
                    // Only text carries on to the next page. Anything else - an image, a
                    // barcode - was drawn whole in the first part and leaves nothing.
                    (IReadOnlyList<string> lines, TextStyle font) = CellLines(text, Math.Max(0, width), page);
                    int left = Math.Max(0, lines.Count - skip);
                    height = left * _measurer.GetMetrics(font).LineHeight
                           + text.Style.Padding.Top + text.Style.Padding.Bottom;
                }
                else if (skip > 0)
                {
                    continue;
                }
                else
                {
                    height = IntrinsicHeight(cell, Math.Max(0, width), page);
                }

                tallest = Math.Max(tallest, height + cell.Style.Margin.Top + cell.Style.Margin.Bottom);
            }

            return tallest + inset.Top + inset.Bottom;
        }

        /// <summary>A text cell broken to its column width, with the font it was broken at.</summary>
        private (IReadOnlyList<string> Lines, TextStyle Font) CellLines(TextItem text, double width, Page page)
        {
            TextStyle font = ResolveFont(text.Font, double.NaN);
            double inner = Math.Max(0, width - text.Style.Padding.Left - text.Style.Padding.Right);
            return (_measurer.BreakLines(TextOf(text, page), font, inner), font);
        }

        /// <summary>
        /// How many lines of the tallest text cell a row would need to show, from
        /// <paramref name="skip"/> onwards. Zero when nothing in the row is text.
        /// </summary>
        private int RowLineCount(TableRow row, double[] edges, Page page, int skip)
        {
            IReadOnlyList<BaseItem> cells = row.Childs;
            EdgeSize inset = row.Style.Padding;
            int most = 0;

            for (int i = 0; i < cells.Count && i + 1 < edges.Length; i++)
            {
                if (cells[i] is not TextItem text || cells[i].Style.Visibility == Visibility.Gone)
                    continue;

                double width = edges[i + 1] - edges[i] - inset.Left - inset.Right
                               - text.Style.Margin.Left - text.Style.Margin.Right;

                (IReadOnlyList<string> lines, _) = CellLines(text, Math.Max(0, width), page);
                most = Math.Max(most, Math.Max(0, lines.Count - skip));
            }

            return most;
        }

        private double PlaceRow(TableRow row, double[] edges, double top, double right, Page page)
            => PlaceRow(row, edges, top, right, page, 0, int.MaxValue, out _);

        /// <summary>
        /// Places a row, or as much of it as <paramref name="maxLines"/> allows, starting
        /// <paramref name="skip"/> lines into each text cell. Reports the bottom edge, and
        /// through <paramref name="shown"/> how many lines went down - which is where the
        /// next page picks the row up.
        /// </summary>
        private double PlaceRow(TableRow row, double[] edges, double top, double right, Page page,
                               int skip, int maxLines, out int shown)
        {
            shown = Math.Min(maxLines, RowLineCount(row, edges, page, skip));

            double height = maxLines == int.MaxValue
                ? RowHeight(row, edges, page, skip)
                : PartHeight(row, edges, page, skip, shown);

            var frame = new Rect(edges[0], top, right, top + height);

            SetFrame(row, frame);
            PlacedItem placedRow = page.Place(row, frame);
            NoteOutline(row, page, frame);
            placedRow.GridEdges = edges;

            EdgeSize inset = row.Style.Padding;
            double cellTop = top + inset.Top;
            double cellBottom = top + height - inset.Bottom;
            IReadOnlyList<BaseItem> cells = row.Childs;

            for (int i = 0; i < cells.Count && i + 1 < edges.Length; i++)
            {
                BaseItem cell = cells[i];
                if (cell.Style.Visibility == Visibility.Gone)
                    continue;

                // Arrange subtracts the cell's own margin, so hand it the padded box.
                var box = new Rect(edges[i] + inset.Left, cellTop, edges[i + 1] - inset.Right, cellBottom);

                if (cell is TextItem text && (skip > 0 || maxLines != int.MaxValue))
                {
                    double width = box.Width - text.Style.Margin.Left - text.Style.Margin.Right;
                    (IReadOnlyList<string> lines, TextStyle font) = CellLines(text, Math.Max(0, width), page);

                    // A cell whose lines all went down on an earlier page has nothing
                    // left to draw - but it is still one of this row's cells, and a row
                    // that hands back fewer cells than it has columns is a row a
                    // conformance checker refuses and a reader cannot navigate. Placed
                    // empty, it draws nothing and keeps the row its own width.
                    int count = Math.Max(0, Math.Min(shown, lines.Count - skip));

                    double part = count * _measurer.GetMetrics(font).LineHeight
                                + text.Style.Padding.Top + text.Style.Padding.Bottom;

                    PlaceLines(text, page, box.Left + text.Style.Margin.Left, cellTop,
                               box.Right - text.Style.Margin.Right, cellTop + part,
                               font, lines, skip, count, Math.Max(0, width));
                    continue;
                }

                // The same for anything that is not text: drawn whole in the first
                // part, and present but empty in the rest.
                if (skip > 0)
                {
                    SetFrame(cell, new Rect(box.Left, cellTop, box.Right, cellTop));
                    page.Place(cell, new Rect(box.Left, cellTop, box.Right, cellTop), string.Empty);
                    continue;
                }

                Arrange(cell, box, page);
            }

            return top + height;
        }

        /// <summary>Height of a row showing <paramref name="count"/> lines from <paramref name="skip"/>.</summary>
        private double PartHeight(TableRow row, double[] edges, Page page, int skip, int count)
        {
            IReadOnlyList<BaseItem> cells = row.Childs;
            EdgeSize inset = row.Style.Padding;
            double tallest = 0;

            for (int i = 0; i < cells.Count && i + 1 < edges.Length; i++)
            {
                if (cells[i] is not TextItem text || cells[i].Style.Visibility == Visibility.Gone)
                    continue;

                double width = edges[i + 1] - edges[i] - inset.Left - inset.Right
                               - text.Style.Margin.Left - text.Style.Margin.Right;

                (IReadOnlyList<string> lines, TextStyle font) = CellLines(text, Math.Max(0, width), page);
                int here = Math.Max(0, Math.Min(count, lines.Count - skip));

                tallest = Math.Max(tallest, here * _measurer.GetMetrics(font).LineHeight
                                          + text.Style.Padding.Top + text.Style.Padding.Bottom
                                          + text.Style.Margin.Top + text.Style.Margin.Bottom);
            }

            return tallest + inset.Top + inset.Bottom;
        }

        /// <summary>
        /// Height of the box an item wants at a given box width, padding included, used
        /// to size table rows. Containers are approximated: a stack sums its children,
        /// anything else takes the tallest.
        /// </summary>
        private double IntrinsicHeight(BaseItem item, double boxWidth, Page page)
        {
            FrameDimension loc = item.Style.Location4Calc;
            if (!double.IsNaN(loc.HeightValue))
                return ToMillimeters(loc.HeightValue, loc.HeightUnit, 0);

            EdgeSize padding = item.Style.Padding;
            double content = Math.Max(0, boxWidth - padding.Left - padding.Right);
            double height;

            switch (item)
            {
                case TextItem text:
                {
                    TextStyle font = ResolveFont(text.Font, double.NaN);
                    height = _measurer.Measure(TextOf(text, page), font, content).Height;
                    break;
                }

                case LineItem line:
                    height = line.Pen.Width;
                    break;

                case FieldItem field:
                    height = FieldHeight(field, ResolveFont(field.Font, double.NaN));
                    break;

                case ImageItem image:
                    height = ImageHeight(image, content);
                    break;

                case ImportedPageItem imported:
                    height = ImportedHeight(imported, content);
                    break;

                case ContainerItem container:
                    height = ContainerHeight(container, content, page);
                    break;

                // It fell to the default and measured zero. In the flow that never
                // showed, because FlowAdvancedText breaks its own lines and never asks;
                // anywhere else - a group sizing itself around one, an alignment needing
                // to know how tall it is - it was a paragraph of no height at all.
                case AdvancedTextItem advanced:
                {
                    double sum = 0;
                    foreach (AdvancedLine line in BreakAdvancedText(advanced, content, page))
                        sum += line.Height;
                    height = sum;
                    break;
                }

                default:
                    height = 0;
                    break;
            }

            return height + padding.Top + padding.Bottom;
        }

        /// <summary>
        /// Height a container needs for its children, at a given content width.
        ///
        /// The page a band will end up on is not known when its height is reserved, so
        /// the answer must hold for every page: it is the height of the *tallest* page
        /// the container can produce. Children scoped to All are always there; the
        /// scoped ones are alternatives, so only the heaviest scope counts. Sizing a
        /// footer from page one would otherwise reserve room for the one-line
        /// "totals on the last page" notice and let the totals themselves overflow.
        /// </summary>
        private double ContainerHeight(ContainerItem container, double content, Page page)
        {
            if (container.Layout == LayoutMode.Stack)
            {
                double always = 0;
                Dictionary<PageScope, double>? scoped = null;

                foreach (BaseItem child in container.Childs)
                {
                    if (child.Style.Visibility == Visibility.Gone)
                        continue;

                    double h = ChildHeight(child, content, page);

                    if (child.Style.PageScope == PageScope.All)
                    {
                        always += h;
                        continue;
                    }

                    scoped ??= new Dictionary<PageScope, double>();
                    scoped[child.Style.PageScope] = scoped.GetValueOrDefault(child.Style.PageScope) + h;
                }

                return always + (scoped == null ? 0 : scoped.Values.Max());
            }

            // Absolute: the lowest edge any child reaches. A child pinned to the bottom
            // is measured from an edge this very computation is trying to find, so it
            // cannot contribute and is left out.
            double tallest = 0;

            foreach (BaseItem child in container.Childs)
            {
                if (child.Style.Visibility == Visibility.Gone)
                    continue;

                // The author's own words, not the defaults filled in above: a child that
                // was merely defaulted to "fill" still has a height of its own to give.
                FrameDimension loc = child.Style.Location;
                if ((loc.Dock & Dock.Bottom) != 0 || !double.IsNaN(loc.BottomValue))
                    continue;

                double offset = double.IsNaN(loc.TopValue) || loc.TopUnit == Units.Percentage
                    ? 0
                    : loc.TopValue;

                // Measured at the width this child will actually get, not at the whole
                // container's. A picture 60mm wide beside a paragraph was being measured
                // as if it spanned the container, so its height came back at the ratio of
                // a 140mm picture and the group was twice as tall as anything in it.
                ComputeHorizontal(child, new Rect(0, 0, content, 0),
                                  out double childLeft, out double childRight);
                double width = double.IsNaN(childLeft) || double.IsNaN(childRight)
                    ? content
                    : Math.Max(0, childRight - childLeft);

                tallest = Math.Max(tallest, offset + ChildHeight(child, width, page));
            }

            return tallest;
        }

        private double ChildHeight(BaseItem child, double content, Page page)
            => IntrinsicHeight(child, content - child.Style.Margin.Left - child.Style.Margin.Right, page)
               + child.Style.Margin.Top + child.Style.Margin.Bottom;

        /// <summary>
        /// A field is at least a line tall, with a little air so the box does not sit on
        /// the text. A multi-line one gets three lines unless told otherwise.
        /// </summary>
        private double FieldHeight(FieldItem field, TextStyle font)
        {
            double line = _measurer.GetMetrics(font).LineHeight;
            double lines = field.Kind == FieldKind.Signature ? 3
                         : field.Multiline ? 3
                         : 1;

            return lines * line + 1.0
                   + field.Style.Padding.Top + field.Style.Padding.Bottom;
        }

        /// <summary>
        /// How tall an imported page is in the width it was given - the same question a
        /// picture answers, and answered better: a page states its media box, while a
        /// picture here has only what the template claimed about it.
        /// </summary>
        private static double ImportedHeight(ImportedPageItem page, double width)
        {
            if (page.Resolved is not { } read || read.Width <= 0 || read.Height <= 0)
                return 0;

            return page.Scaling == ImageScaling.None
                ? read.Height
                : width * read.Height / read.Width;
        }

        private static double ImageHeight(ImageItem image, double width)
        {
            double ratio = image.AspectRatio;
            if (image.Scaling == ImageScaling.None || double.IsNaN(ratio) || ratio <= 0)
                return double.IsNaN(image.NaturalHeight) ? 0 : image.NaturalHeight;
            return width / ratio;
        }

        /// <summary>
        /// Only text, an advanced text, stacked containers and tables know how to
        /// continue on the next page.
        /// </summary>
        private static bool CanSplit(BaseItem item)
        {
            if (item.Style.Splitting == Splitting.KeepTogether)
                return false;
            return item is TextItem or TableItem or AdvancedTextItem or ContainerItem { Layout: LayoutMode.Stack };
        }

        private static string Join(IReadOnlyList<string> lines, int start, int count)
        {
            if (count <= 0)
                return string.Empty;
            var parts = new string[count];
            for (int i = 0; i < count; i++)
                parts[i] = lines[start + i];
            return string.Join("\n", parts);
        }

        /// <summary>Lowest edge among the items placed after <paramref name="after"/>.</summary>
        private static double LowestBottom(Page page, PlacedItem after)
        {
            double lowest = double.NegativeInfinity;
            bool seen = false;
            foreach (PlacedItem placed in page.Items)
            {
                if (!seen)
                {
                    seen = ReferenceEquals(placed, after);
                    continue;
                }
                if (!double.IsNaN(placed.Frame.Bottom))
                    lowest = Math.Max(lowest, placed.Frame.Bottom);
            }
            return double.IsNegativeInfinity(lowest) ? after.Frame.Top : lowest;
        }

        private PlacedItem Place(BaseItem item, Page page, double left, double top, double right, double bottom,
                                 string? fragment, TextStyle? font)
        {
            ApplyConstraints(item, ref left, ref top, ref right, ref bottom);
            var frame = new Rect(left, top, right, bottom);
            SetFrame(item, frame);
            PlacedItem placed = page.Place(item, frame, fragment, font);
            NoteOutline(item, page, frame);
            return placed;
        }

        /// <summary>
        /// Records an outline entry where the item landed. Only the first placement
        /// counts: an item split across pages is one heading, and it belongs where the
        /// reader first meets it.
        /// </summary>
        private void NoteOutline(BaseItem item, Page page, Rect frame)
        {
            if (string.IsNullOrWhiteSpace(item.Bookmark) || _result == null || double.IsNaN(frame.Top))
                return;

            if (!_bookmarked.Add(item))
                return;

            int sheet = _result.Pages.ToList().IndexOf(page);
            if (sheet >= 0)
                _result.AddOutline(item.Bookmark!, Math.Max(0, item.BookmarkLevel), sheet, frame.Top);
        }

        private LayoutResult? _result;
        private readonly HashSet<BaseItem> _bookmarked = new();

        /// <summary>
        /// Places a slice of broken lines, remembering which slice it was so that a
        /// {Pages} token can be rewritten once the page count is known.
        /// </summary>
        private void PlaceLines(TextItem text, Page page, double left, double top, double right, double bottom,
                                TextStyle font, IReadOnlyList<string> lines, int first, int count, double width)
        {
            PlacedItem placed = Place(text, page, left, top, right, bottom, Join(lines, first, count), font);
            placed.LineStart = first;
            placed.LineCount = count;
            placed.MeasureWidth = width;
        }

        // ------------------------------------------------------------------
        // Pass 4: absolute arrangement
        // ------------------------------------------------------------------

        /// <summary>
        /// Places <paramref name="item"/> inside <paramref name="available"/>, then recurses.
        /// Returns false when the item is <see cref="Visibility.Gone"/>, in which case
        /// neither it nor its children get any geometry.
        /// </summary>
        private bool Arrange(BaseItem item, Rect available, Page page)
        {
            item.Style.RenderedFrame.Visibility = item.Style.Visibility;
            if (item.Style.Visibility == Visibility.Gone)
                return false;

            Rect frame = ComputeFrame(item, available, page, out TextStyle? font, out string? fragment);
            SetFrame(item, frame);
            page.Place(item, frame, fragment, font);
            NoteOutline(item, page, frame);

            if (item is TableItem fixedTable)
            {
                // A table sitting in a band rather than in the flow: its rows are laid
                // out where it was put, with no pagination to worry about.
                LayRows(fixedTable, frame.Deflate(item.Style.Padding), page,
                        fixedTable.Childs.OfType<TableRow>());
                return true;
            }

            if (item is AdvancedTextItem advanced)
            {
                // Placed by coordinates rather than flowed. The lines are broken at the
                // width the frame grants and laid down where it starts; there is no
                // pagination here, the same way a table in a band has none.
                //
                // Without this an advanced text placed absolutely produced a rectangle
                // and no marks at all - Place() had nothing to draw, since this item is
                // drawn through the synthetic texts PlaceAdvancedLines makes and never
                // through its own fragment. Silent, and only reachable off the flow,
                // which is the corner CLAUDE.md §7 is about.
                Rect inner = frame.Deflate(item.Style.Padding);
                List<AdvancedLine> lines = BreakAdvancedText(advanced, inner.Width, page);
                PlaceAdvancedLines(advanced, page, frame.Left, frame.Top, frame.Right,
                                   lines, 0, lines.Count);
                return true;
            }

            if (item is ContainerItem container)
            {
                Rect inner = frame.Deflate(item.Style.Padding);

                if (container.Layout == LayoutMode.Stack)
                    FlowBand(container, inner, page);
                else
                    ArrangeChildren(container, inner, page);
            }

            return true;
        }

        /// <summary>
        /// Stacks the children of a band. A band never paginates - its height was
        /// reserved from this very content - so this is the flow without the
        /// continuations, plus the one rule bands need:
        ///
        /// children scoped to a page are <em>alternatives</em>, not a sequence. Each new
        /// scope restarts at the same cursor, so a totals block scoped to the last page
        /// and a notice scoped to the others occupy the same room rather than stacking
        /// into a band sized for only one of them. It mirrors <see cref="ContainerHeight"/>,
        /// which reserved the space the same way.
        /// </summary>
        private void FlowBand(ContainerItem container, Rect area, Page page)
        {
            double cursor = area.Top;
            double variantTop = double.NaN;
            double variantBottom = double.NaN;
            PageScope currentScope = PageScope.All;

            foreach (BaseItem child in container.Childs)
            {
                if (child.Style.Visibility == Visibility.Gone)
                    continue;

                PageScope scope = child.Style.PageScope;

                if (scope == PageScope.All)
                {
                    if (!double.IsNaN(variantBottom))
                    {
                        cursor = variantBottom;                    // past the tallest variant
                        variantTop = variantBottom = double.NaN;
                    }
                }
                else if (double.IsNaN(variantTop))
                {
                    variantTop = variantBottom = cursor;
                    currentScope = scope;
                }
                else if (scope != currentScope)
                {
                    cursor = variantTop;                           // a different variant, same room
                    currentScope = scope;
                }

                cursor = Flow(child, area, cursor, page, null, force: true).Bottom;

                if (scope != PageScope.All)
                    variantBottom = Math.Max(variantBottom, cursor);
            }
        }

        private Continuation? ArrangeChildren(ContainerItem container, Rect childArea, Page page)
        {
            foreach (BaseItem child in container.Childs)
                Arrange(child, childArea, page);
            return null;
        }

        /// <summary>
        /// Moves an item inside the space it was given, once its own size is known.
        ///
        /// Only where nothing pinned it on that axis: a coordinate, an edge it is stuck
        /// to, or a size the author stated has already decided, and an alignment must not
        /// argue with any of them. <see cref="ItemAlign.None"/> leaves it where it fell,
        /// which is what everything did before there was a choice.
        /// </summary>
        private static void AlignInside(BaseItem item, Rect available,
                                        ref double left, ref double right,
                                        ref double top, ref double bottom,
                                        FrameDimension loc)
        {
            if (item.Style.AlignHorizontal != ItemAlign.None
                && !StatedWidth(loc)
                && (loc.Dock & (Dock.Left | Dock.Right)) == 0
                && double.IsNaN(loc.LeftValue) && double.IsNaN(loc.RightValue)
                && !double.IsNaN(left) && !double.IsNaN(right))
            {
                double width = right - left;
                left = Place(available.Left, available.Width, width, item.Style.AlignHorizontal);
                right = left + width;
            }

            if (item.Style.AlignVertical != ItemAlign.None
                && double.IsNaN(loc.HeightValue)
                && (loc.Dock & (Dock.Top | Dock.Bottom)) == 0
                && double.IsNaN(loc.TopValue) && double.IsNaN(loc.BottomValue)
                && !double.IsNaN(top) && !double.IsNaN(bottom))
            {
                double height = bottom - top;
                top = Place(available.Top, available.Height, height, item.Style.AlignVertical);
                bottom = top + height;
            }
        }

        private void ComputeHorizontal(BaseItem item, Rect available, out double left, out double right)
        {
            FrameDimension loc = item.Style.Location4Calc;
            left = double.NaN;
            right = double.NaN;

            if ((loc.Dock & Dock.Left) != 0)
                left = available.Left;
            else if (!double.IsNaN(loc.LeftValue))
                left = available.Left + ToMillimeters(loc.LeftValue, loc.LeftUnit, available.Width);

            if ((loc.Dock & Dock.Right) != 0)
                right = available.Right;
            else if (!double.IsNaN(loc.RightValue))
                right = available.Right - ToMillimeters(loc.RightValue, loc.RightUnit, available.Width);

            double width = double.IsNaN(loc.WidthValue)
                ? double.NaN
                : ToMillimeters(loc.WidthValue, loc.WidthUnit, available.Width);

            if (double.IsNaN(width))
                return;

            if (!double.IsNaN(left) && double.IsNaN(right))
                right = left + width;
            else if (double.IsNaN(left) && !double.IsNaN(right))
                left = right - width;
            else if (double.IsNaN(left) && double.IsNaN(right))
            {
                // Nothing pinned it, so the alignment decides. Without one it goes
                // against the left, which is where it has always gone.
                left = Place(available.Left, available.Width, width, item.Style.AlignHorizontal);
                right = left + width;
            }
            // Left and right both pinned: they win, the width request is ignored.
        }

        private Rect ComputeFrame(BaseItem item, Rect available, Page page,
                                  out TextStyle? resolvedFont, out string? resolvedText)
        {
            resolvedFont = null;
            resolvedText = null;
            FrameDimension loc = item.Style.Location4Calc;

            // Margin sits outside the frame: it shrinks what the item may occupy. The
            // flow does this itself, so only the absolute path goes through here.
            available = available.Deflate(item.Style.Margin);

            // Padding sits inside: an auto-sized item is its content plus its padding.
            EdgeSize padding = item.Style.Padding;

            ComputeHorizontal(item, available, out double left, out double right);

            // --- vertical ------------------------------------------------------
            double top = double.NaN;
            double bottom = double.NaN;

            if ((loc.Dock & Dock.Top) != 0)
                top = available.Top;
            else if (!double.IsNaN(loc.TopValue))
                top = available.Top + ToMillimeters(loc.TopValue, loc.TopUnit, available.Height);

            if ((loc.Dock & Dock.Bottom) != 0)
                bottom = available.Bottom;
            else if (!double.IsNaN(loc.BottomValue))
                bottom = available.Bottom - ToMillimeters(loc.BottomValue, loc.BottomUnit, available.Height);

            double height = double.IsNaN(loc.HeightValue)
                ? double.NaN
                : ToMillimeters(loc.HeightValue, loc.HeightUnit, available.Height);

            if (!double.IsNaN(height))
            {
                if (!double.IsNaN(top) && double.IsNaN(bottom))
                    bottom = top + height;
                else if (double.IsNaN(top) && !double.IsNaN(bottom))
                    top = bottom - height;
                else if (double.IsNaN(top) && double.IsNaN(bottom))
                {
                    top = Place(available.Top, available.Height, height, item.Style.AlignVertical);
                    bottom = top + height;
                }
            }

            // A barcode's value takes the page tokens too, so {Page} on a delivery note
            // encodes the sheet it is on rather than the brace.
            if (item is BarcodeItem coded && ReportTokens.HasTokens(coded.Value))
                resolvedText = TextOf(coded.Value, page, item);

            // --- Pass 1: font size, then intrinsic size for what is undetermined -
            if (item is TextItem text)
            {
                // PercentOfFrame needs the frame height, which only exists if it was
                // pinned above; ResolveFont falls back to the root size otherwise.
                double frameHeight = !double.IsNaN(top) && !double.IsNaN(bottom) ? bottom - top : double.NaN;
                resolvedFont = ResolveFont(text.Font, frameHeight);
                resolvedText = TextOf(text, page);

                if (double.IsNaN(left) || double.IsNaN(right) || double.IsNaN(bottom) || double.IsNaN(top))
                {
                    double measureWidth = double.IsNaN(left) || double.IsNaN(right)
                        ? double.PositiveInfinity
                        : Math.Max(0, right - left - padding.Left - padding.Right);

                    TextMetrics metrics = _measurer.Measure(resolvedText, resolvedFont, measureWidth);
                    double boxWidth = metrics.Width + padding.Left + padding.Right;
                    double boxHeight = metrics.Height + padding.Top + padding.Bottom;

                    // Whichever edge is pinned, the measured size supplies the other one:
                    // text pinned to the right grows leftwards, text pinned to the bottom
                    // grows upwards.
                    if (double.IsNaN(left) && !double.IsNaN(right))
                        left = right - boxWidth;
                    else if (double.IsNaN(right))
                    {
                        if (double.IsNaN(left))
                            left = available.Left;
                        right = left + boxWidth;
                    }

                    if (double.IsNaN(top) && !double.IsNaN(bottom))
                        top = bottom - boxHeight;
                    else if (double.IsNaN(bottom))
                    {
                        if (double.IsNaN(top))
                            top = available.Top;
                        bottom = top + boxHeight;
                    }
                }
            }
            else if (item is FieldItem field)
            {
                // A field is a box the reader writes in, so it takes a line of the height
                // its own font asks for unless it was given one.
                resolvedFont = ResolveFont(field.Font, double.NaN);

                if (double.IsNaN(left))
                    left = available.Left;
                if (double.IsNaN(right))
                    right = available.Right;

                if (double.IsNaN(top) && double.IsNaN(bottom))
                    top = available.Top;

                double wanted = FieldHeight(field, resolvedFont);
                if (double.IsNaN(bottom))
                    bottom = top + wanted;
                else if (double.IsNaN(top))
                    top = bottom - wanted;
            }
            else if (item is LineItem or ImageItem or AdvancedTextItem)
            {
                if (double.IsNaN(left))
                    left = available.Left;
                if (double.IsNaN(right))
                    right = available.Right;

                double thickness = IntrinsicHeight(item, right - left, page);

                if (double.IsNaN(top) && !double.IsNaN(bottom))
                    top = bottom - thickness;
                else if (double.IsNaN(bottom))
                {
                    if (double.IsNaN(top))
                        top = available.Top;
                    bottom = top + thickness;
                }
            }
            else if (item is ContainerItem && (double.IsNaN(top) || double.IsNaN(bottom)))
            {
                // A band with no stated height takes the height of what it holds. This is
                // what lets a footer grow with the invoice while staying the same on every
                // page of it: the height is settled once, before any page is opened.
                if (double.IsNaN(left))
                    left = available.Left;
                if (double.IsNaN(right))
                    right = available.Right;

                double needed = IntrinsicHeight(item, right - left, page);

                if (double.IsNaN(top))
                    top = bottom - needed;
                else
                    bottom = top + needed;
            }

            ApplyConstraints(item, ref left, ref top, ref right, ref bottom);

            // Where an item that measured its own size sits in the room it was given.
            //
            // AlignHorizontal and AlignVertical were consulted a hundred lines above, and
            // only where the author had *stated* a size: with none, the sizing passes in
            // between put the item against the top-left corner and the alignment was a
            // legal value that did nothing. That is the fault §10 is about, and it is the
            // one a picture beside a paragraph meets first - a paragraph never states a
            // height, so nothing could centre it against the picture.
            //
            // Applied here rather than inside each sizing pass: there is one of those per
            // kind of item, and a rule written five times is a rule four of them will
            // eventually disagree with.
            // item.Style.Location, never Location4Calc: the latter is the resolver's
            // working copy with the defaults already filled in - an item that stated
            // nothing comes back from it stuck to all four edges, so asking it "did the
            // author pin this?" always answers yes and the alignment never fired.
            // ContainerHeight learnt the same thing, and says so in its own comment.
            AlignInside(item, available, ref left, ref right, ref top, ref bottom,
                        item.Style.Location);

            // A width means a width. An absolutely placed text is broken to it here, the
            // way a table cell is: backends split on newlines and wrap nothing, so
            // without this a text given a width was measured for three lines - the box
            // was the right height - and then drawn as one long line straight across
            // whatever sat beside it.
            //
            // Only a width the author stated counts. Where the width came from measuring
            // the text itself, breaking to it again is a coin toss on the last word: the
            // two numbers are equal to the last bit, and a total that fitted perfectly
            // well lost its currency to the line below.
            if (item is TextItem && resolvedText is { Length: > 0 } && resolvedFont?.Wrap != false
                && StatedWidth(loc))
            {
                double content = right - left - padding.Left - padding.Right;
                if (!double.IsNaN(content) && content > 0)
                    resolvedText = string.Join("\n",
                        _measurer.BreakLines(resolvedText, resolvedFont!, content));
            }

            return new Rect(left, top, right, bottom);
        }

        /// <summary>
        /// True when the author decided how wide the item is - a width outright, or both
        /// edges pinned - rather than leaving it to be measured from its content.
        /// </summary>
        private static bool StatedWidth(FrameDimension loc)
        {
            if (!double.IsNaN(loc.WidthValue))
                return true;

            bool leftPinned = !double.IsNaN(loc.LeftValue) || (loc.Dock & Dock.Left) != 0;
            bool rightPinned = !double.IsNaN(loc.RightValue) || (loc.Dock & Dock.Right) != 0;
            return leftPinned && rightPinned;
        }

        // ------------------------------------------------------------------
        // Pass 5: fields
        // ------------------------------------------------------------------

        private TokenContext ContextFor(int pageNumber, string pageCount)
            => ContextFor(pageNumber, pageCount, pageNumber - _sectionFirstPage, pageCount);

        private TokenContext ContextFor(int pageNumber, string pageCount,
                                        int sectionPageNumber, string sectionPageCount)
            => new(pageNumber, pageCount, sectionPageNumber, sectionPageCount,
                   _report.Title, _timestamp, _report.Fields);

        private string TextOf(TextItem item, Page page) => TextOf(item.Text, page, item);

        private string TextOf(string source, Page page, BaseItem? owner = null)
        {
            if (!ReportTokens.HasTokens(source))
                return source;

            // The heading number first, and from what the walk stored on the item rather
            // than from a counter running here - so eight column trials of the same
            // heading all produce the same number, and the two backends cannot differ.
            string text = HeadingNumbers.Fill(source, owner, _report.HeadingNumbering);

            // A back is laid out after the count is known, so it gets the real total
            // rather than the placeholder the main pass has to iterate on.
            return ReportTokens.Resolve(text,
                _backContext ?? ContextFor(page.Number, _pageCountPlaceholder));
        }

        /// <summary>
        /// Writes the real page count into the fragments that asked for it. The digit
        /// count already matches the placeholder, so nothing moves and nothing rewraps.
        /// </summary>
        private void ResolvePageCount(LayoutResult result)
        {
            string total = result.PageCount.ToString(CultureInfo.InvariantCulture);

            foreach (Page page in result.Pages)
            {
                // Which section this page belongs to, so {SectionPage} counts from its
                // own start and {SectionPages} reports its own length.
                LaidOutSection? section = result.Sections
                    .FirstOrDefault(sec => page.Number - 1 >= sec.FirstPage
                                        && page.Number - 1 < sec.FirstPage + sec.PageCount);

                int sectionPage = section == null ? page.Number : page.Number - section.FirstPage;
                string sectionTotal = section == null
                    ? total
                    : section.PageCount.ToString(CultureInfo.InvariantCulture);

                TokenContext context = ContextFor(page.Number, total, sectionPage, sectionTotal);

                foreach (PlacedItem placed in page.Items)
                {
                    if (placed.Source is not TextItem text || !ReportTokens.UsesPageCount(text.Text))
                        continue;

                    string full = ReportTokens.Resolve(text.Text, context);

                    if (placed.LineCount < 0 || placed.Font == null)
                    {
                        placed.Fragment = full;
                        continue;
                    }

                    IReadOnlyList<string> lines = _measurer.BreakLines(full, placed.Font, placed.MeasureWidth);
                    int available = Math.Max(0, lines.Count - placed.LineStart);
                    placed.Fragment = Join(lines, placed.LineStart, Math.Min(placed.LineCount, available));
                }
            }
        }

        // ------------------------------------------------------------------
        // Page scope
        // ------------------------------------------------------------------

        /// <summary>
        /// Drops the items that do not belong on a page. Only applied to what does not
        /// take part in the flow - overlays, header, footer - because removing a flow
        /// item after the fact would invalidate the pagination that placed it.
        /// Last and ExceptLast are resolved here, since the page count is only known now.
        /// </summary>
        private void ApplyPageScope(LayoutResult result)
        {
            int total = result.PageCount;
            var flowed = new HashSet<BaseItem>();
            if (_report.Body != null)
                CollectFlowItems(_report.Body, flowed);

            foreach (Page page in result.Pages)
                page.RemoveAll(placed => !flowed.Contains(placed.Source)
                                         && !IsOnPage(placed.Source, page.Number, total));
        }

        private static void CollectFlowItems(ContainerItem container, HashSet<BaseItem> into)
        {
            into.Add(container);
            foreach (BaseItem child in container.Childs)
            {
                into.Add(child);
                if (child is ContainerItem inner)
                    CollectFlowItems(inner, into);
            }
        }

        /// <summary>
        /// Scope is inherited: an item shows only if it and every container above it do.
        /// Without this, hiding a totals block would leave its contents on the page.
        /// </summary>
        private static bool IsOnPage(BaseItem item, int number, int total)
        {
            for (BaseItem? at = item; at != null; at = at.Parent)
            {
                if (!Matches(at.Style, number, total))
                    return false;
            }
            return true;
        }

        private static bool Matches(Style style, int number, int total) => style.PageScope switch
        {
            PageScope.All => true,
            PageScope.First => number == 1,
            PageScope.Last => number == total,
            PageScope.ExceptFirst => number != 1,
            PageScope.ExceptLast => number != total,
            PageScope.Odd => (number & 1) == 1,
            PageScope.Even => (number & 1) == 0,
            PageScope.Range => style.PageRange != null && style.PageRange.Contains(number),
            _ => true
        };

        // ------------------------------------------------------------------
        // Fonts, constraints, units
        // ------------------------------------------------------------------

        /// <summary>
        /// Turns an author-facing size into a point size the measurer and the backend
        /// understand. Sizing in millimeters pins the cap height, which is what makes a
        /// section look consistent across typefaces: the same point size renders a
        /// visibly different capital from one font to the next, the same cap height does not.
        /// </summary>
        private TextStyle ResolveFont(TextStyle style, double frameHeight)
        {
            // Anything the text left undecided comes from the document's own default,
            // which is why a template names its typeface once rather than on every line.
            style = style.Over(_report.DefaultFont);

            double size = style.Size ?? 1;

            if (style.SizeUnit is null or FontSizeUnit.Rem && style.Size is null)
                size = 1;

            if (style.SizeUnit == FontSizeUnit.Point)
                return WithResolvedFamily(style, size);

            double capHeightMm = style.SizeUnit switch
            {
                FontSizeUnit.Millimeter => size,
                FontSizeUnit.PercentOfFrame => double.IsNaN(frameHeight) || frameHeight <= 0
                    ? _report.RootFontSize                     // circular request: fall back
                    : frameHeight * size / 100.0,
                _ => size * _report.RootFontSize               // Rem, and the default
            };

            double perPoint = CapHeightPerPoint(style);
            double points = perPoint > 0
                ? capHeightMm / perPoint
                : capHeightMm / TextStyle.PointsToMillimeters;   // measurer reports nothing usable

            return WithResolvedFamily(style, points);
        }

        /// <summary>Pins both the point size and the typeface the measurer settled on.</summary>
        private TextStyle WithResolvedFamily(TextStyle style, double points)
        {
            TextStyle resolved = style.WithPointSize(points);
            resolved.Family = _measurer.ResolveFamily(style);
            return resolved;
        }

        private double CapHeightPerPoint(TextStyle style)
        {
            string key = string.Concat(style.Family, style.Bold == true ? "|b" : "|",
                                       style.Italic == true ? "i" : "");
            if (_capHeightPerPoint.TryGetValue(key, out double ratio))
                return ratio;

            FontMetrics probe = _measurer.GetMetrics(style.WithPointSize(ProbeSizeInPoints));
            ratio = probe.CapHeight / ProbeSizeInPoints;
            _capHeightPerPoint[key] = ratio;
            return ratio;
        }

        private static void ApplyConstraints(BaseItem item, ref double left, ref double top, ref double right, ref double bottom)
        {
            Constraints c = item.Style.Constraints;
            bool anchoredRight = (item.Style.Location4Calc.Dock & Dock.Right) != 0;
            bool anchoredBottom = (item.Style.Location4Calc.Dock & Dock.Bottom) != 0;

            if (!double.IsNaN(left) && !double.IsNaN(right))
            {
                double clamped = Clamp(right - left, c.MinWidth, c.MaxWidth);
                if (clamped != right - left)
                {
                    if (anchoredRight)
                        left = right - clamped;
                    else
                        right = left + clamped;
                }
            }

            if (!double.IsNaN(top) && !double.IsNaN(bottom))
            {
                double clamped = Clamp(bottom - top, c.MinHeight, c.MaxHeight);
                if (clamped != bottom - top)
                {
                    if (anchoredBottom)
                        top = bottom - clamped;
                    else
                        bottom = top + clamped;
                }
            }
        }

        /// <summary>A limit of zero or less means "no limit", so a default Constraints is inert.</summary>
        private static double Clamp(double value, double min, double max)
        {
            if (min > 0 && value < min)
                return min;
            if (max > 0 && value > max)
                return max;
            return value;
        }

        /// <summary>Slides a span of a known size along the room it was given.</summary>
        private static double Place(double start, double room, double size, ItemAlign align) => align switch
        {
            ItemAlign.Center => start + (room - size) / 2,
            ItemAlign.End => start + room - size,
            _ => start
        };

        private static double ToMillimeters(double value, Units unit, double reference)
            => unit == Units.Percentage ? reference * value / 100.0 : value;

        private static void SetFrame(BaseItem item, Rect frame)
        {
            ReadOnlyFrameDimension rendered = item.Style.RenderedFrame;

            if (!double.IsNaN(frame.Left))
                rendered.Left = frame.Left;
            if (!double.IsNaN(frame.Top))
                rendered.Top = frame.Top;
            if (!double.IsNaN(frame.Right))
                rendered.Right = frame.Right;
            if (!double.IsNaN(frame.Bottom))
                rendered.Bottom = frame.Bottom;
        }

        private static Rect FrameOf(BaseItem item)
        {
            ReadOnlyFrameDimension f = item.Style.RenderedFrame;
            return new Rect(f.Left, f.Top, f.Right, f.Bottom);
        }
    }
}
