namespace Frisket
{
    /// <summary>
    /// How much to add to each space to make a line reach the far margin.
    ///
    /// Shared between the backends rather than worked out twice. The two draw
    /// justified text by different means - a PDF adjusts positions between words, an
    /// SVG names a word spacing and lets the viewer do it - but they must stretch the
    /// same line by the same amount, or the two outputs of one document disagree in a
    /// way neither file shows on its own.
    /// </summary>
    internal static class Justification
    {
        /// <summary>
        /// Extra millimetres per space, or zero when the line should be left alone.
        ///
        /// A line that ends a paragraph is never stretched: a closing line pulled out to
        /// the margin is the mark of a typesetter who was not paying attention, and it
        /// is what tells a reader the paragraph has ended. That is every paragraph's last
        /// line, and not only the text's - which is what the layout now says, line by line.
        /// </summary>
        internal static double SpacePadding(string line, double naturalWidth,
                                            double frameWidth, bool endsParagraph,
                                            TextStyle style)
        {
            if ((style.HorizontalAlignment ?? HorizontalAlignment.Left) != HorizontalAlignment.Justify)
                return 0;

            if (endsParagraph)
                return 0;

            int spaces = 0;
            foreach (char c in line)
            {
                if (c == ' ')
                    spaces++;
            }

            if (spaces == 0)
                return 0;

            // Already at or past the margin: a line that has to be squeezed is a line
            // that was broken wrongly, and closing the gaps would only hide it.
            double slack = frameWidth - naturalWidth;
            return slack <= 0 ? 0 : slack / spaces;
        }

        /// <summary>Whether line <paramref name="line"/> ends a paragraph; past the list, it does.</summary>
        internal static bool Ends(IReadOnlyList<bool> endsParagraph, int line)
            => line >= endsParagraph.Count || endsParagraph[line];

        /// <summary>
        /// The last line alone ends a paragraph: what a surface is told when nobody says, and
        /// what every text was drawn with before the layout said which line ends one.
        /// </summary>
        internal static bool[] LastOnly(string? text)
        {
            bool[] ends = new bool[LineCount(text)];
            ends[^1] = true;
            return ends;
        }

        /// <summary>
        /// Every line ends one: a text the layout never broke, so that each newline in it is
        /// one the author typed.
        /// </summary>
        internal static bool[] Every(string? text)
        {
            bool[] ends = new bool[LineCount(text)];
            Array.Fill(ends, true);
            return ends;
        }

        private static int LineCount(string? text)
        {
            int lines = 1;
            foreach (char c in text ?? string.Empty)
            {
                if (c == '\n')
                    lines++;
            }
            return lines;
        }
    }
}
