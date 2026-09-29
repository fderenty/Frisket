using System.Globalization;
using System.Text;

namespace Frisket
{
    /// <summary>
    /// Numbers the headings of a report, so a template can write
    /// <c>{HeadingNumber}. Introduction</c> and get <c>1.2. Introduction</c>.
    ///
    /// The number lives in the text rather than being painted to the left of it, which
    /// is the whole difference from a word processor's linked lists. The author decides
    /// where it goes, what comes before it, and - because the separator between two
    /// levels is the only thing this class writes - what punctuation follows it. A
    /// template asking for "1.1." types the second dot itself.
    ///
    /// Two counters, exactly as the page tokens have two: <c>{HeadingNumber}</c> runs
    /// across the whole file, <c>{SectionHeadingNumber}</c> restarts at every section,
    /// and they are equal while a report has one section - so a template may use either
    /// without knowing whether the report will ever gain a second.
    ///
    /// <para>
    /// The numbers are worked out in one walk of the tree in declaration order, before
    /// any layout, and stored on the items. That is what "as it prints" has to mean
    /// here: a counter advanced while items are being placed would be advanced again by
    /// every re-placement, and the engine re-places constantly - BalanceColumns searches
    /// eight column heights, each one placing and rewinding, and KeepWithNext rewinds to
    /// pull a heading onto the next page. Filling a token from a stored number is a pure
    /// function of the item, so it survives all of that, and the two backends cannot
    /// disagree about it either.
    /// </para>
    /// </summary>
    public static class HeadingNumbers
    {
        /// <summary>Runs across the whole file.</summary>
        public const string Token = "HeadingNumber";

        /// <summary>Restarts at every section.</summary>
        public const string SectionToken = "SectionHeadingNumber";

        /// <summary>Levels a heading role can name: Heading1 through Heading6.</summary>
        public const int MaxLevel = 6;

        // ------------------------------------------------------------------
        // The walk
        // ------------------------------------------------------------------

        /// <summary>
        /// Gives every heading its number. Safe to call more than once: it writes only
        /// the counters held beside each item, never the template's own text.
        /// </summary>
        public static void Apply(Report report)
        {
            if (report == null)
                return;

            var file = new int[MaxLevel];

            foreach (Section section in report.Sections)
            {
                // A section starts its own count from one. The file's does not stop.
                var own = new int[MaxLevel];
                Walk(section, file, own);
            }
        }

        private static void Walk(BaseItem item, int[] file, int[] own)
        {
            // An item the data hid is not in the document, so it takes no number - and
            // neither does anything under it.
            if (item.Style.Visibility == Visibility.Gone)
                return;

            int level = LevelOf(item);
            if (level > 0)
            {
                Advance(file, level);
                Advance(own, level);
            }

            // Stored whatever the item is, because a token may sit on something that is
            // not itself a heading - the validator has an opinion about that, the engine
            // does not need one.
            item.HeadingCounters = (int[])file.Clone();
            item.SectionHeadingCounters = (int[])own.Clone();

            if (item is ContainerItem container)
                foreach (BaseItem child in container.Childs)
                    Walk(child, file, own);
        }

        private static void Advance(int[] counters, int level)
        {
            counters[level - 1]++;
            for (int deeper = level; deeper < counters.Length; deeper++)
                counters[deeper] = 0;
        }

        /// <summary>
        /// One to six for a heading, zero for anything else. The level comes from the
        /// role the model already carries - PDF/UA needs it anyway - rather than from a
        /// second place that could disagree with it.
        /// </summary>
        public static int LevelOf(BaseItem item)
            => item.Role switch
            {
                PdfRole.Heading1 => 1,
                PdfRole.Heading2 => 2,
                PdfRole.Heading3 => 3,
                PdfRole.Heading4 => 4,
                PdfRole.Heading5 => 5,
                PdfRole.Heading6 => 6,
                _ => 0
            };

        // ------------------------------------------------------------------
        // Filling a token
        // ------------------------------------------------------------------

        /// <summary>True if this text asks for a heading number at all.</summary>
        public static bool Mentions(string? text)
            => text != null
               && (text.Contains("{" + Token, StringComparison.OrdinalIgnoreCase)
                   || text.Contains("{" + SectionToken, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Replaces the heading tokens in <paramref name="source"/> and leaves every
        /// other token alone, the way the binder leaves <c>{Page}</c> for the resolver.
        ///
        /// With no format declared the token is left visible rather than resolved to
        /// nothing: a stray separator in the middle of a heading is a defect nobody
        /// notices, and <c>{HeadingNumber}</c> printed on the page is one nobody misses.
        /// </summary>
        public static string Fill(string source, BaseItem? owner, string? format)
        {
            if (!Mentions(source) || owner?.HeadingCounters == null || string.IsNullOrEmpty(format))
                return source;

            Symbol[] shape = Parse(format!);
            if (shape.Length == 0)
                return source;

            BaseItem item = owner;

            return ReportTokens.Resolve(source, token =>
            {
                int colon = token.IndexOf(':');
                string name = colon < 0 ? token : token[..colon];

                int[] counters;
                if (Named(name, Token))
                    counters = item.HeadingCounters!;
                else if (Named(name, SectionToken))
                    counters = item.SectionHeadingCounters!;
                else
                    return null;                       // not ours; leave it for the next pass

                int level = LevelIn(name) is int stated and > 0 ? stated : LevelOf(item);

                // Two ways of having no answer, and neither is a number. The item is not
                // a heading and named no level; or nothing at that level has been reached
                // yet, which is what a page header asking for a chapter number gets,
                // being laid out before the body it heads.
                //
                // The token is left visible in both, the way an unresolved field is: "0."
                // in front of a title is a defect nobody notices, and {HeadingNumber} on
                // the page is one nobody misses. The validator names both cases.
                if (level <= 0 || counters[level - 1] == 0)
                    return null;

                return Render(counters, level, shape);
            }, unescape: false);
        }

        private static bool Named(string name, string token)
            => name.StartsWith(token, StringComparison.OrdinalIgnoreCase)
               && (name.Length == token.Length || name[token.Length] == '[');

        /// <summary>The level written inside the brackets, or zero for none.</summary>
        public static int LevelIn(string? name)
        {
            if (name == null)
                return 0;

            int open = name.IndexOf('[');
            int close = name.IndexOf(']');
            if (open < 0 || close < open + 2)
                return 0;

            return int.TryParse(name.AsSpan(open + 1, close - open - 1), NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out int level)
                   && level >= 1 && level <= MaxLevel
                ? level
                : 0;
        }

        // ------------------------------------------------------------------
        // The format
        // ------------------------------------------------------------------

        /// <summary>
        /// How many levels the format spells out. Zero for none, which is also what an
        /// empty or symbol-less format gives.
        /// </summary>
        public static int Depth(string? format)
            => string.IsNullOrEmpty(format) ? 0 : Parse(format!).Length;

        /// <summary>One level of the format: what comes before it, and how it is spelled.</summary>
        private readonly record struct Symbol(string Lead, char Kind);

        /// <summary>
        /// Reads "1.1.1", "A.a.1" or "A) a. 1". The five symbol characters are the
        /// alphabets; everything between them is literal and belongs to the level that
        /// follows it, which is why truncating to level two of "1.1.1" gives "1.1" and
        /// not "1.1." - the closing punctuation is the author's, typed in the text.
        ///
        /// A separator therefore cannot contain a symbol character. In practice the
        /// literal words go around the token rather than inside the format, and the
        /// separators people actually write are ".", "-", ")" and a space.
        /// </summary>
        private static Symbol[] Parse(string format)
        {
            var shape = new List<Symbol>();
            var lead = new StringBuilder();

            foreach (char c in format)
            {
                if (c is '1' or 'A' or 'a' or 'I' or 'i')
                {
                    shape.Add(new Symbol(lead.ToString(), c));
                    lead.Clear();
                }
                else
                {
                    lead.Append(c);
                }
            }

            return shape.ToArray();
        }

        /// <summary>
        /// Writes levels one to <paramref name="level"/>. A level deeper than the format
        /// declares repeats the last symbol rather than stopping: stopping would give two
        /// sibling headings the same number, which is worse than a number the author did
        /// not spell out. The validator says so.
        /// </summary>
        private static string Render(int[] counters, int level, Symbol[] shape)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < level && i < counters.Length; i++)
            {
                Symbol symbol = shape[Math.Min(i, shape.Length - 1)];
                sb.Append(i == 0 ? "" : symbol.Lead).Append(Spell(counters[i], symbol.Kind));
            }

            return sb.ToString();
        }

        private static string Spell(int value, char kind) => kind switch
        {
            'A' => Letters(value, 'A'),
            'a' => Letters(value, 'a'),
            'I' => Roman(value, upper: true),
            'i' => Roman(value, upper: false),
            _ => value.ToString(CultureInfo.InvariantCulture)
        };

        /// <summary>
        /// A, B ... Z, then AA, AB - the way a spreadsheet names its columns. Twenty-seven
        /// subsections in a contract is ordinary, so what happens after Z is decided here
        /// rather than left to happen.
        /// </summary>
        private static string Letters(int value, char first)
        {
            if (value <= 0)
                return "";

            var sb = new StringBuilder();
            while (value > 0)
            {
                value--;
                sb.Insert(0, (char)(first + value % 26));
                value /= 26;
            }
            return sb.ToString();
        }

        private static readonly (int Value, string Sign)[] RomanSigns =
        {
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
        };

        /// <summary>
        /// I, II, III ... Above 3999 there is no standard roman numeral, so it falls back
        /// to arabic. Nobody will write four thousand chapters; an empty string or a
        /// hanging loop there would still be a defect that only showed up once.
        /// </summary>
        private static string Roman(int value, bool upper)
        {
            if (value <= 0 || value > 3999)
                return value.ToString(CultureInfo.InvariantCulture);

            var sb = new StringBuilder();
            foreach ((int amount, string sign) in RomanSigns)
                while (value >= amount)
                {
                    sb.Append(sign);
                    value -= amount;
                }

            string roman = sb.ToString();
            return upper ? roman : roman.ToLowerInvariant();
        }
    }
}
