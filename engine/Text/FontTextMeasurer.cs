namespace Frisket
{
    /// <summary>
    /// Measures with the real advance widths of a real font.
    ///
    /// Line breaking happens on word boundaries, falling back to breaking inside a word
    /// only when a single word is wider than the space available. Kerning and complex
    /// shaping are not applied: for the Latin text of an invoice the difference is under
    /// a percent, and applying it would mean a shaping engine and the dependency that
    /// comes with it.
    /// </summary>
    public sealed class FontTextMeasurer : ITextMeasurer
    {
        private readonly FontLibrary _library;

        /// <summary>
        /// Whether a missing family is an error. It is by default, on purpose: a report
        /// laid out with the wrong metrics is worse than one that refuses to build.
        /// </summary>
        public bool FailOnMissingFont { get; set; } = true;

        public FontTextMeasurer(FontLibrary library)
            => _library = library ?? throw new ArgumentNullException(nameof(library));

        public FontFile? FontFor(TextStyle style)
        {
            FontFile? font = _library.Resolve(style.Family ?? string.Empty, style.Bold ?? false, style.Italic ?? false);
            if (font == null && FailOnMissingFont)
                throw new FontNotFoundException(style.Family ?? string.Empty);
            return font;
        }

        public string ResolveFamily(TextStyle style) => FontFor(style)?.Family ?? style.Family ?? string.Empty;

        public FontMetrics GetMetrics(TextStyle style)
        {
            FontFile? font = FontFor(style);
            double em = style.EmSizeInMillimeters;

            if (font == null)
                return new FontMetrics(em, em * 0.70, em * 0.52, em * 0.80, em * 0.20,
                                       em * 0.70 * (style.LineSpacing ?? 1.5));

            // Leading is a multiple of the cap height - the size the author asked for -
            // not of the font's own line height, which swings by half between faces and
            // would make a row change height for no reason but a change of typeface.
            double lineHeight = em * font.CapHeight * (style.LineSpacing ?? 1.5);

            return new FontMetrics(
                emSize: em,
                capHeight: em * font.CapHeight,
                xHeight: em * font.XHeight,
                ascent: em * font.Ascender,
                descent: em * font.Descender,
                lineHeight: lineHeight);
        }

        public TextMetrics Measure(string text, TextStyle style, double availableWidth)
        {
            FontMetrics metrics = GetMetrics(style);
            IReadOnlyList<string> lines = BreakLines(text, style, availableWidth);

            double widest = 0;
            foreach (string line in lines)
                widest = Math.Max(widest, WidthOf(line, style));

            return new TextMetrics(widest, lines.Count * metrics.LineHeight, metrics.LineHeight, lines.Count);
        }

        public IReadOnlyList<string> BreakLines(string text, TextStyle style, double availableWidth)
        {
            var lines = new List<string>();

            if (string.IsNullOrEmpty(text))
            {
                lines.Add(string.Empty);
                return lines;
            }

            bool wrap = style.Wrap != false && !double.IsInfinity(availableWidth) && availableWidth > 0;

            foreach (string paragraph in text.Split('\n'))
            {
                if (!wrap || WidthOf(paragraph, style) <= availableWidth)
                {
                    lines.Add(paragraph);
                    continue;
                }

                WrapParagraph(paragraph, style, availableWidth, lines);
            }

            return lines;
        }

        /// <summary>Width of a string in millimeters, at the style's point size.</summary>
        public double WidthOf(string text, TextStyle style)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            FontFile? font = FontFor(style);
            double em = style.EmSizeInMillimeters;

            if (font == null)
                return text.Length * em * 0.5;

            double total = 0;
            for (int i = 0; i < text.Length; i++)
            {
                int codePoint = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                total += font.AdvanceOf(codePoint);
            }

            return total * em;
        }

        private void WrapParagraph(string paragraph, TextStyle style, double width, List<string> lines)
        {
            var line = new System.Text.StringBuilder();
            int start = 0;

            while (start < paragraph.Length)
            {
                // A word plus the spaces that follow it, so trailing spaces never push a
                // line over on their own.
                int end = start;
                while (end < paragraph.Length && !char.IsWhiteSpace(paragraph[end]))
                    end++;
                while (end < paragraph.Length && char.IsWhiteSpace(paragraph[end]))
                    end++;

                string word = paragraph[start..end];
                string candidate = line.Length == 0 ? word.TrimStart() : line + word;

                if (WidthOf(candidate.TrimEnd(), style) <= width)
                {
                    line.Clear();
                    line.Append(candidate);
                    start = end;
                    continue;
                }

                if (line.Length > 0)
                {
                    lines.Add(line.ToString().TrimEnd());
                    line.Clear();
                    continue;   // retry the same word on a fresh line
                }

                // A single word wider than the line: break it between characters.
                start += BreakWord(word, style, width, lines);
            }

            if (line.Length > 0)
                lines.Add(line.ToString().TrimEnd());
        }

        /// <summary>Splits an over-long word, emitting all but the tail. Returns characters consumed.</summary>
        private int BreakWord(string word, TextStyle style, double width, List<string> lines)
        {
            int consumed = 0;
            var piece = new System.Text.StringBuilder();

            foreach (char c in word)
            {
                piece.Append(c);
                if (WidthOf(piece.ToString(), style) > width && piece.Length > 1)
                {
                    piece.Length--;
                    lines.Add(piece.ToString());
                    return consumed;
                }
                consumed++;
            }

            if (piece.Length > 0)
                lines.Add(piece.ToString().TrimEnd());

            return Math.Max(1, consumed);
        }
    }
}
