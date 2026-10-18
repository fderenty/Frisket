namespace Frisket
{
    /// <summary>
    /// Where a line of text may break. Both line breakers ask here - the measurer for
    /// plain text and the layout resolver for runs of mixed style - so the two cannot
    /// come to disagree about it.
    /// </summary>
    internal static class LineBreaks
    {
        /// <summary>
        /// The white space that holds its neighbours together, what Unicode's line
        /// breaking rules (UAX #14) call glue: the no-break space, the figure space, and
        /// the narrow no-break space that <see cref="NumberFormats.French"/> groups
        /// thousands with. Breaking at any white space at all put "12" at the end of one
        /// line and "480,00" at the start of the next.
        ///
        /// The designer's echo reads this line rather than keeping a copy of it.
        /// </summary>
        internal const string Glue = "\u00A0\u2007\u202F";

        /// <summary>Whether a line may break at this character.</summary>
        internal static bool At(char c) => char.IsWhiteSpace(c) && Glue.IndexOf(c) < 0;
    }
}
