namespace Frisket
{
    /// <summary>
    /// Vertical metrics of a font at a given size, in millimeters.
    /// They are linear in the size, so one probe yields the ratios for every size.
    /// </summary>
    public readonly struct FontMetrics
    {
        /// <summary>Nominal em size, i.e. the point size converted to millimeters.</summary>
        public double EmSize { get; }

        /// <summary>Baseline to the top of a capital. This is what "3 mm of text" means.</summary>
        public double CapHeight { get; }

        /// <summary>Baseline to the top of a lowercase x.</summary>
        public double XHeight { get; }

        public double Ascent { get; }
        public double Descent { get; }

        /// <summary>Baseline-to-baseline distance.</summary>
        public double LineHeight { get; }

        public FontMetrics(double emSize, double capHeight, double xHeight, double ascent, double descent, double lineHeight)
        {
            EmSize = emSize;
            CapHeight = capHeight;
            XHeight = xHeight;
            Ascent = ascent;
            Descent = descent;
            LineHeight = lineHeight;
        }
    }

    /// <summary>Result of measuring a piece of text, in millimeters.</summary>
    public readonly struct TextMetrics
    {
        public double Width { get; }
        public double Height { get; }
        public double LineHeight { get; }
        public int LineCount { get; }

        public TextMetrics(double width, double height, double lineHeight, int lineCount)
        {
            Width = width;
            Height = height;
            LineHeight = lineHeight;
            LineCount = lineCount;
        }
    }

    /// <summary>
    /// Font metrics source. The layout engine measures through this and nothing else,
    /// which keeps it free of any graphics dependency.
    /// </summary>
    public interface ITextMeasurer
    {
        /// <summary>
        /// Measures <paramref name="text"/> in millimeters. Pass <see cref="double.PositiveInfinity"/>
        /// as <paramref name="availableWidth"/> to measure on a single unconstrained line.
        /// <paramref name="style"/> always carries a point size here.
        /// </summary>
        TextMetrics Measure(string text, TextStyle style, double availableWidth);

        /// <summary>
        /// Vertical metrics of the font, at the point size carried by <paramref name="style"/>.
        /// This is what lets the resolver turn a cap height in millimeters into a point
        /// size, and the answer differs per typeface - that is the whole point.
        /// </summary>
        FontMetrics GetMetrics(TextStyle style);

        /// <summary>
        /// Breaks <paramref name="text"/> into the lines it occupies at
        /// <paramref name="availableWidth"/>. The resolver needs the actual lines, not
        /// just their count, to cut a paragraph across a page boundary; the backend then
        /// draws those very lines, so the two cannot disagree on where the breaks fall.
        /// </summary>
        IReadOnlyList<string> BreakLines(string text, TextStyle style, double availableWidth);

        /// <summary>
        /// The family actually used for this style. The resolver stamps it onto the
        /// placed text so a backend draws with the typeface that was measured, even when
        /// the report left the family unnamed.
        /// </summary>
        string ResolveFamily(TextStyle style) => style.Family ?? string.Empty;
    }

    /// <summary>
    /// Deterministic fallback measurer: every glyph is <see cref="WidthRatio"/> times the
    /// font size wide. Wrong for any real font, but it lets the engine and its tests run
    /// with no backend at all, and it never varies between machines.
    /// </summary>
    public sealed class FixedPitchTextMeasurer : ITextMeasurer
    {
        /// <summary>Glyph advance as a fraction of the font size.</summary>
        public double WidthRatio { get; set; } = 0.5;

        /// <summary>Cap height as a fraction of the em. Real fonts sit roughly between 0.62 and 0.75.</summary>
        public double CapHeightRatio { get; set; } = 0.70;

        /// <summary>X-height as a fraction of the em.</summary>
        public double XHeightRatio { get; set; } = 0.52;

        public static FixedPitchTextMeasurer Default { get; } = new();

        public FontMetrics GetMetrics(TextStyle style)
        {
            double em = style.EmSizeInMillimeters;
            return new FontMetrics(
                emSize: em,
                capHeight: em * CapHeightRatio,
                xHeight: em * XHeightRatio,
                ascent: em * 0.80,
                descent: em * 0.20,
                lineHeight: em * CapHeightRatio * (style.LineSpacing ?? 1.5));
        }

        public IReadOnlyList<string> BreakLines(string text, TextStyle style, double availableWidth)
        {
            var lines = new List<string>();
            double charWidth = style.EmSizeInMillimeters * WidthRatio;

            if (string.IsNullOrEmpty(text))
            {
                lines.Add(string.Empty);
                return lines;
            }

            foreach (string hardLine in text.Split('\n'))
            {
                if (style.Wrap == false || double.IsInfinity(availableWidth) || availableWidth <= 0
                    || hardLine.Length * charWidth <= availableWidth)
                {
                    lines.Add(hardLine);
                    continue;
                }

                // Character-count wrapping is enough for a stand-in measurer; a real one
                // wraps on word boundaries using the actual glyph advances.
                int charsPerLine = Math.Max(1, (int)(availableWidth / charWidth));
                for (int i = 0; i < hardLine.Length; i += charsPerLine)
                    lines.Add(hardLine.Substring(i, Math.Min(charsPerLine, hardLine.Length - i)));
            }

            return lines;
        }

        public TextMetrics Measure(string text, TextStyle style, double availableWidth)
        {
            double lineHeight = style.EmSizeInMillimeters * CapHeightRatio * (style.LineSpacing ?? 1.5);
            double charWidth = style.EmSizeInMillimeters * WidthRatio;

            IReadOnlyList<string> lines = BreakLines(text, style, availableWidth);
            double widest = 0;
            foreach (string line in lines)
                widest = Math.Max(widest, line.Length * charWidth);

            return new TextMetrics(widest, lines.Count * lineHeight, lineHeight, lines.Count);
        }
    }
}
