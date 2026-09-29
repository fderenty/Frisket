namespace Frisket
{
    /// <summary>
    /// Walks a <see cref="LayoutResult"/> and turns it into backend draw calls.
    ///
    /// It reads nothing but placed rectangles, so a backend never sees the element
    /// tree and never has to know how anything was positioned.
    /// </summary>
    public static class ReportRenderer
    {
        /// <param name="cancellation">
        /// Asked once a page, which is the grain a document has: a page either was drawn
        /// or was not. Finer would buy nothing, since abandoning mid-page and abandoning
        /// mid-document come to the same thing - nothing usable comes out either way.
        /// </param>
        public static void Render(Report report, LayoutResult layout, IReportBackend backend,
                                  CancellationToken cancellation = default)
        {
            if (report is null) throw new ArgumentNullException(nameof(report));
            if (layout is null) throw new ArgumentNullException(nameof(layout));
            if (backend is null) throw new ArgumentNullException(nameof(backend));

            backend.BeginDocument(report);

            if (layout.Outline.Count > 0)
                backend.SetOutline(layout.Outline);

            foreach (Page page in layout.Pages)
            {
                cancellation.ThrowIfCancellationRequested();
                backend.BeginPage(page);

                // First of all, so everything else paints over it - it is the paper.
                if (report.Backdrop is { } paper)
                    DrawBackdrop(paper, page, layout.PageCount, backend);

                if (report.Watermark is { } mark && mark.Layer == WatermarkLayer.Behind)
                    DrawWatermark(mark, layout.WatermarkFont ?? mark.Font, page, layout.PageCount, backend);

                // Placement order is paint order: a container was placed before its
                // children, so backgrounds land underneath.
                foreach (PlacedItem placed in page.Items)
                    Draw(placed, backend);

                if (report.Watermark is { } over && over.Layer == WatermarkLayer.InFront)
                    DrawWatermark(over, layout.WatermarkFont ?? over.Font, page, layout.PageCount, backend);

                backend.EndPage();
            }

            backend.EndDocument();
        }

        /// <summary>Renders straight from a report, resolving with the backend's own metrics.</summary>
        public static LayoutResult Render(Report report, IReportBackend backend,
                                         CancellationToken cancellation = default)
        {
            LayoutResult layout = report.Resolve(backend, cancellation);

            // The seam between composing and drawing. A caller holding both halves has
            // this for nothing; one that calls this overload would not, so it is taken
            // here on their behalf - and it is the cheapest cancellation there is, since
            // the expensive half has not started.
            cancellation.ThrowIfCancellationRequested();

            Render(report, layout, backend, cancellation);
            return layout;
        }

        /// <summary>
        /// Lays the mark over the page and hands each copy to the backend already turned.
        /// The arithmetic lives here so that every backend places it identically - a
        /// watermark that lands somewhere else in the PDF than in the preview is worse
        /// than none, because it is the preview people check.
        /// </summary>
        /// <summary>
        /// The picture behind the page, fitted into its rectangle the way any other
        /// image is fitted into its frame.
        ///
        /// Through an ephemeral <see cref="ImageItem"/> rather than a second copy of the
        /// arithmetic: <see cref="Fit"/> already knows what Stretch, Uniform and None
        /// mean and reads the alignment off the style, and two implementations of one
        /// rule are two rules waiting to disagree. The same move PlaceAdvancedLines
        /// makes for a run of styled text.
        /// </summary>
        private static void DrawBackdrop(Backdrop paper, Page page, int total, IReportBackend backend)
        {
            if (paper.Source.Data.Length == 0 || paper.Opacity <= 0
                || !InScope(paper.PageScope, page.Number, total))
                return;

            // Decoration, and it has to say so - the same reason as the watermark below.
            // A letterhead announced before every page is a nuisance, not information.
            backend.BeginElement(Decoration, page.Frame);
            try
            {
                Rect area = paper.Area == BackdropArea.Margins ? Margins(page) : page.Frame;
                backend.DrawImage(Down(Fit(AsImage(paper), area), area, paper.AlignVertical),
                                  paper.Source, paper.Opacity);
            }
            finally
            {
                backend.EndElement();
            }
        }

        /// <summary>
        /// A backdrop in the shape <see cref="Fit"/> reads. Centred, because a picture
        /// smaller than the sheet belongs in the middle of it - which is the one place a
        /// backdrop differs from an image given a frame by the flow.
        /// </summary>
        private static ImageItem AsImage(Backdrop paper) => new()
        {
            Source = paper.Source,
            Scaling = paper.Scaling,
            NaturalWidth = paper.NaturalWidth,
            NaturalHeight = paper.NaturalHeight,
            Style = { AlignHorizontal = paper.AlignHorizontal }
        };

        /// <summary>
        /// The vertical half of the placement, which <see cref="Fit"/> does not do.
        ///
        /// And should not: an image in the flow is put at the top of the frame the flow
        /// gave it, because the flow has already decided where that frame is. A
        /// backdrop's rectangle is the whole sheet, so nothing has decided anything and
        /// it has to be told.
        /// </summary>
        private static Rect Down(Rect fitted, Rect area, ItemAlign how)
        {
            double slack = area.Height - fitted.Height;
            if (slack <= 0)
                return fitted;

            double top = how switch
            {
                ItemAlign.Center => area.Top + slack / 2,
                ItemAlign.End => area.Bottom - fitted.Height,
                _ => area.Top
            };
            return Rect.FromSize(fitted.Left, top, fitted.Width, fitted.Height);
        }

        /// <summary>
        /// What the section's margins leave, read off the bands the layout reserved rather
        /// than off the section - which the renderer never sees, and should not: a page
        /// carries its own geometry precisely so a backend needs nothing else.
        ///
        /// An absent band is empty, so it cannot be taken for a rectangle at the origin.
        /// </summary>
        private static Rect Margins(Page page)
        {
            double top = page.HeaderBand.Height > 0 ? page.HeaderBand.Top : page.BodyBand.Top;
            double bottom = page.FooterBand.Height > 0 ? page.FooterBand.Bottom : page.BodyBand.Bottom;
            return new Rect(page.BodyBand.Left, top, page.BodyBand.Right, bottom);
        }

        private static void DrawWatermark(Watermark mark, TextStyle font, Page page, int total, IReportBackend backend)
        {
            if (string.IsNullOrWhiteSpace(mark.Text) || !InScope(mark.PageScope, page.Number, total))
                return;

            // Decoration, and it has to say so. PDF/UA asks that every piece of content be
            // either tagged or set aside as an artifact; a mark repeated eighty-seven
            // times and left untagged is both a violation and, read out, unbearable.
            backend.BeginElement(Decoration, page.Frame);
            try
            {
                Tile(mark, font, page, backend);
            }
            finally
            {
                backend.EndElement();
            }
        }

        private static void Tile(Watermark mark, TextStyle font, Page page, IReportBackend backend)
        {
            Rect sheet = page.Frame;
            double width = backend.Measurer.Measure(mark.Text, font, double.PositiveInfinity).Width;
            double height = backend.Measurer.GetMetrics(font).CapHeight;
            double radians = mark.Angle * Math.PI / 180.0;

            if (mark.Mode == WatermarkMode.Single)
            {
                // Centred: the midpoint of the text lands on the midpoint of the page,
                // so the anchor steps back half the word along its own direction.
                double cx = (sheet.Left + sheet.Right) / 2;
                double cy = (sheet.Top + sheet.Bottom) / 2;
                backend.DrawRotatedText(cx - Math.Cos(radians) * width / 2 - Math.Sin(radians) * height / 2,
                                        cy + Math.Sin(radians) * width / 2 - Math.Cos(radians) * height / 2,
                                        mark.Text, font, mark.Angle);
                return;
            }

            // Tiled. The grid is laid out along the diagonal rather than along the page,
            // so the copies line up with each other instead of forming horizontal rows
            // that a single band of paint could take out.
            double along = width + (mark.Spacing > 0 ? mark.Spacing : width / 2);
            // Rows are three cap heights apart. The gap between two diagonals is what
            // decides the worst case, and a point halfway between them is half of this
            // from either - at six the corners of a sheet sat 25mm from any mark.
            double across = height * 3 + (mark.Spacing > 0 ? mark.Spacing : 0);
            if (along <= 0 || across <= 0)
                return;

            // Far enough either way that a turned grid still covers the corners. Half the
            // diagonal reaches them from the middle, and one more step covers the fact
            // that a copy runs forward from its anchor rather than either side of it -
            // without it the far corner sat thirty millimetres from the nearest mark.
            double reach = Math.Sqrt(sheet.Width * sheet.Width + sheet.Height * sheet.Height) / 2
                         + Math.Max(along, across);
            double midX = (sheet.Left + sheet.Right) / 2;
            double midY = (sheet.Top + sheet.Bottom) / 2;

            int steps = (int)Math.Ceiling(reach / along);
            int rows = (int)Math.Ceiling(reach / across);

            for (int row = -rows; row <= rows; row++)
            {
                // Every other row is offset by half a step, so the copies interlock and
                // no straight corridor runs between them.
                double shift = (row & 1) == 0 ? 0 : along / 2;

                for (int step = -steps; step <= steps; step++)
                {
                    double u = step * along + shift;      // along the text's own direction
                    double v = row * across;              // perpendicular to it

                    double x = midX + u * Math.Cos(radians) + v * Math.Sin(radians);
                    double y = midY - u * Math.Sin(radians) + v * Math.Cos(radians);

                    // A copy whose anchor sits off the sheet still runs onto it, so the
                    // cull has to allow a whole word's reach on every side.
                    if (x < sheet.Left - along || x > sheet.Right + along ||
                        y < sheet.Top - along || y > sheet.Bottom + along)
                        continue;

                    backend.DrawRotatedText(x, y, mark.Text, font, mark.Angle);
                }
            }
        }

        /// <summary>A stand-in owner for what the renderer draws outside the item tree.</summary>
        private static readonly BaseItem Decoration = new() { Role = PdfRole.Artifact };

        private static bool InScope(PageScope scope, int number, int total) => scope switch
        {
            PageScope.All => true,
            PageScope.First => number == 1,
            PageScope.Last => number == total,
            PageScope.ExceptFirst => number != 1,
            PageScope.ExceptLast => number != total,
            PageScope.Odd => (number & 1) == 1,
            PageScope.Even => (number & 1) == 0,
            _ => true
        };

        private static void Draw(PlacedItem placed, IReportBackend backend)
        {
            BaseItem source = placed.Source;

            // Invisible is laid out but not drawn; Gone never got here.
            if (source.Style.Visibility != Visibility.Visible)
                return;

            Rect frame = placed.Frame;
            if (double.IsNaN(frame.Left) || double.IsNaN(frame.Top)
                || double.IsNaN(frame.Right) || double.IsNaN(frame.Bottom))
                return;

            backend.BeginElement(source, frame);
            try
            {
                DrawContent(placed, source, frame, backend);
            }
            finally
            {
                backend.EndElement();
            }
        }

        private static void DrawContent(PlacedItem placed, BaseItem source, Rect frame, IReportBackend backend)
        {

            if (source.Style.Background != null || source.Style.Border != null)
                backend.DrawRectangle(frame, source.Style.Border, source.Style.Background);

            // The grid belongs to the row, not to the cells: a cell is inset by the row
            // padding, so stroking cells would draw floating boxes instead of a table.
            if (source is TableRow gridRow && placed.GridEdges is { Length: > 1 } edges
                && gridRow.Parent is TableItem { Grid: not null } owner)
            {
                for (int i = 0; i + 1 < edges.Length; i++)
                    backend.DrawRectangle(new Rect(edges[i], frame.Top, edges[i + 1], frame.Bottom), owner.Grid, null);
            }

            // Background and border take the whole frame; the content sits inside the
            // padding, just as a container's children do.
            Rect content = frame.Deflate(source.Style.Padding);

            switch (source)
            {
                case TextItem text:
                    backend.DrawText(content, placed.Fragment ?? text.Text, placed.Font ?? text.Font);
                    break;

                case LineItem line:
                    DrawLine(line, content, backend);
                    break;

                case ImageItem image:
                    backend.DrawImage(Fit(image, content), image.Source);
                    break;

                case ImportedPageItem imported:
                    // Fitted through an ephemeral ImageItem, the same trick the backdrop
                    // uses, so there is one fit rule and not two that will disagree.
                    backend.DrawImportedPage(Fit(AsImage(imported), content), imported);
                    break;

                case FieldItem field:
                    backend.DrawField(content, field, placed.Font ?? field.Font);
                    break;

                case BarcodeItem code:
                    DrawBarcode(code, content, placed.Fragment ?? code.Value, backend);
                    break;
            }
        }

        /// <summary>
        /// Draws a barcode as rectangles. Nothing is rasterised: it stays sharp at any
        /// size, weighs a few hundred bytes rather than a bitmap, and asks nothing of a
        /// backend beyond what it already draws.
        /// </summary>
        private static void DrawBarcode(BarcodeItem code, Rect frame, string value, IReportBackend backend)
        {
            if (string.IsNullOrEmpty(value))
                return;

            var ink = new BrushStyle { Color = code.Ink };

            if (code.Symbology == Symbology.Code128)
            {
                int[] widths = Code128.Encode(value);
                int span = widths.Sum() + code.QuietZoneOrDefault * 2;
                double module = frame.Width / span;

                double bottom = frame.Bottom;
                if (code.ShowText)
                    bottom -= backend.Measurer.GetMetrics(code.Font).LineHeight;

                double at = frame.Left + code.QuietZoneOrDefault * module;
                for (int i = 0; i < widths.Length; i++)
                {
                    double run = widths[i] * module;
                    if (i % 2 == 0)                              // even runs are the dark ones
                        backend.DrawRectangle(new Rect(at, frame.Top, at + run, bottom), null, ink);
                    at += run;
                }

                if (code.ShowText)
                {
                    TextStyle centred = code.Font.Clone();
                    centred.HorizontalAlignment = HorizontalAlignment.Center;
                    backend.DrawText(new Rect(frame.Left, bottom, frame.Right, frame.Bottom), value, centred);
                }

                return;
            }

            bool[,] grid = QrCode.Encode(value, code.Correction);
            int modules = grid.GetLength(0) + code.QuietZoneOrDefault * 2;

            // Square, and whole modules: half a module of rounding across a code is what
            // makes one scanner read it and the next refuse.
            double side = Math.Min(frame.Width, frame.Height);
            double unit = side / modules;
            double left = frame.Left + (frame.Width - side) / 2 + code.QuietZoneOrDefault * unit;
            double top = frame.Top + (frame.Height - side) / 2 + code.QuietZoneOrDefault * unit;

            for (int x = 0; x < grid.GetLength(0); x++)
                for (int y = 0; y < grid.GetLength(1); y++)
                    if (grid[x, y])
                        backend.DrawRectangle(
                            new Rect(left + x * unit, top + y * unit,
                                     left + (x + 1) * unit, top + (y + 1) * unit), null, ink);
        }

        private static void DrawLine(LineItem line, Rect frame, IReportBackend backend)
        {
            double midY = (frame.Top + frame.Bottom) / 2;
            double midX = (frame.Left + frame.Right) / 2;

            switch (line.Orientation)
            {
                case LineOrientation.Horizontal:
                    backend.DrawLine(frame.Left, midY, frame.Right, midY, line.Pen);
                    break;
                case LineOrientation.Vertical:
                    backend.DrawLine(midX, frame.Top, midX, frame.Bottom, line.Pen);
                    break;
                case LineOrientation.DiagonalDown:
                    backend.DrawLine(frame.Left, frame.Top, frame.Right, frame.Bottom, line.Pen);
                    break;
                case LineOrientation.DiagonalUp:
                    backend.DrawLine(frame.Left, frame.Bottom, frame.Right, frame.Top, line.Pen);
                    break;
            }
        }

        /// <summary>
        /// Where a picture narrower than its frame starts.
        ///
        /// <see cref="Style.AlignHorizontal"/> rather than a property of its own: it
        /// already exists for exactly this - "how to centre an image, a table or a
        /// field" - and it already places a *frame* of stated width inside its band,
        /// through ComputeHorizontal. This is the other half of the same question, for
        /// a picture given the whole band and fitted inside it, and one property has to
        /// answer both or the two disagree the day an image has a width.
        /// </summary>
        private static double Across(ImageItem image, Rect frame, double width)
            => image.Style.AlignHorizontal switch
            {
                ItemAlign.Center => frame.Left + Math.Max(0, (frame.Width - width) / 2),
                ItemAlign.End => frame.Right - width,
                _ => frame.Left
            };

        /// <summary>Applies the scaling mode inside the frame the layout granted.</summary>
        /// <summary>
        /// An imported page dressed as a picture, so that fitting it is the question it
        /// already is rather than a second one.
        ///
        /// Its natural size is the page's own, which is better than a picture's: the
        /// engine decodes no image and so never knows how big one really is, while a page
        /// states its media box outright.
        /// </summary>
        private static ImageItem AsImage(ImportedPageItem page)
        {
            var stand = new ImageItem { Scaling = page.Scaling };
            stand.Style.AlignHorizontal = page.Style.AlignHorizontal;

            if (page.Resolved is { } read)
            {
                stand.NaturalWidth = read.Width;
                stand.NaturalHeight = read.Height;
            }

            return stand;
        }

        private static Rect Fit(ImageItem image, Rect frame)
        {
            switch (image.Scaling)
            {
                case ImageScaling.Stretch:
                    return frame;

                case ImageScaling.None:
                {
                    double natural = double.IsNaN(image.NaturalWidth) ? frame.Width : image.NaturalWidth;
                    return Rect.FromSize(Across(image, frame, natural), frame.Top, natural,
                        double.IsNaN(image.NaturalHeight) ? frame.Height : image.NaturalHeight);
                }

                default:
                {
                    double ratio = image.AspectRatio;
                    if (double.IsNaN(ratio) || ratio <= 0)
                        return frame;

                    double width = frame.Width;
                    double height = width / ratio;
                    if (height > frame.Height)
                    {
                        height = frame.Height;
                        width = height * ratio;
                    }
                    return Rect.FromSize(Across(image, frame, width), frame.Top, width, height);
                }
            }
        }
    }
}
