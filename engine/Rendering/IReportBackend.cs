namespace Frisket
{
    /// <summary>
    /// Drawing surface. One implementation per output: PDF, Gnostice, SVG, printer.
    /// The backend also owns the <see cref="Measurer"/> so that layout and drawing
    /// always agree on font metrics - if they disagree, text overflows its frame.
    /// </summary>
    public interface IReportBackend : IDisposable
    {
        ITextMeasurer Measurer { get; }

        void BeginDocument(Report report);
        void BeginPage(Page page);
        void DrawText(Rect frame, string text, TextStyle style);
        void DrawLine(double x1, double y1, double x2, double y2, PenStyle pen);
        void DrawRectangle(Rect frame, PenStyle? pen, BrushStyle? fill);
        void DrawImage(Rect frame, ImageSource image);

        /// <summary>
        /// Draws a picture faded, for a backdrop sitting behind text.
        ///
        /// Required rather than defaulted, unlike the optional members below, because
        /// there is no honest fallback: drawing a picture asked for at a fifth at full
        /// strength buries the page it was meant to sit behind. A surface that cannot
        /// fade should say so by not compiling.
        /// </summary>
        void DrawImage(Rect frame, ImageSource image, double opacity);

        /// <summary>
        /// Draws a field the reader can fill in. <paramref name="font"/> carries the size
        /// already resolved to points, like text. A backend with no notion of interaction
        /// should draw what the field looks like rather than ignore it.
        /// </summary>
        void DrawField(Rect frame, FieldItem field, TextStyle font);

        /// <summary>
        /// Draws a page copied out of another PDF, already fitted to
        /// <paramref name="frame"/>.
        ///
        /// Ignored by default, and that default is the honest one for every surface but
        /// PDF: the page is a set of PDF marking instructions, and there is no rendering
        /// of them here to put anywhere else. An SVG proof therefore shows the space and
        /// not the page, which the layout says in as many words rather than leaving the
        /// proof to lie quietly.
        /// </summary>
        void DrawImportedPage(Rect frame, ImportedPageItem page) { }

        /// <summary>
        /// Draws one line turned about a point, used for watermarks. The point is where
        /// the text starts on its baseline, so a caller places the anchor and lets the
        /// text run from there rather than trying to fit a turned frame.
        ///
        /// The default ignores the angle, which is the honest fallback for a surface that
        /// cannot turn text: the mark still lands, and still cannot be missed.
        /// </summary>
        void DrawRotatedText(double x, double y, string text, TextStyle style, double degrees)
            => DrawText(new Rect(x, y, x + 1000, y + 1000), text, style);

        /// <summary>
        /// Declares the document's outline, once, before the pages are drawn. Ignored by
        /// default: a surface with no notion of navigation loses nothing by it, and the
        /// pages are the same either way.
        /// </summary>
        void SetOutline(IReadOnlyList<OutlineEntry> entries) { }

        /// <summary>
        /// Opens the element about to be drawn, so a backend that records structure knows
        /// what the marks belong to. Ignored by default.
        ///
        /// Everything between this and <see cref="EndElement"/> is that element: a
        /// heading, a paragraph, a figure - or decoration, which assistive software is
        /// meant to skip rather than read out.
        /// </summary>
        /// <param name="frame">
        /// Where it is about to be drawn. The same item can be placed twice - a split
        /// table row lays its non-text cells again, at zero height, so the row keeps its
        /// full count of cells - and what an element is depends on which placement this
        /// is: a field placed for real gets a control, the placeholder gets an empty
        /// cell. Told only the item, a backend cannot tell those two apart.
        /// </param>
        void BeginElement(BaseItem item, Rect frame) { }

        void EndElement() { }

        void EndPage();
        void EndDocument();
    }
}
