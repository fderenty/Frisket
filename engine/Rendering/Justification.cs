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
        /// The last line of a block is never stretched: a closing line pulled out to
        /// the margin is the mark of a typesetter who was not paying attention, and it
        /// is what tells a reader the paragraph has ended.
        /// </summary>
        internal static double SpacePadding(string line, double naturalWidth,
                                            double frameWidth, bool isLastLine,
                                            TextStyle style)
        {
            if ((style.HorizontalAlignment ?? HorizontalAlignment.Left) != HorizontalAlignment.Justify)
                return 0;

            if (isLastLine)
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
    }
}
