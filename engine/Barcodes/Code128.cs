namespace Frisket
{
    /// <summary>
    /// Code 128: the general-purpose linear barcode, and the one a workshop or warehouse
    /// scanner expects. Encodes any ASCII, and packs pairs of digits into one symbol,
    /// which is what makes it compact for part and order numbers.
    ///
    /// Returns bar widths in modules, alternating dark and light, starting dark.
    /// </summary>
    public static class Code128
    {
        /// <summary>
        /// The 107 symbols, six digits each giving the widths of three bars and three
        /// spaces; the stop symbol has seven. Taken from the published table, not from
        /// memory: a table that is merely close produces codes that look entirely
        /// plausible and scan as nothing.
        ///
        /// Every entry is checked on first use - each must total 11 modules, 13 for the
        /// stop - because a single wrong digit is invisible by inspection and fatal in
        /// the field.
        /// </summary>
        private const string Patterns =
            "212222222122222221121223121322131222122213122312132212221213" +
            "221312231212112232122132122231113222123122123221223211221132" +
            "221231213212223112312131311222321122321221312212322112322211" +
            "212123212321232121111323131123131321112313132113132311211313" +
            "231113231311112133112331132131113213113321133121313121211133" +
            "231131213113213311213131311123311321331121312113312311332111" +
            "314111221411431111111224111422121124121421141122141221112214" +
            "112412122114122411142112142211241211221114413111241112134111" +
            "111242121142121241114212124112124211411212421112421211212141" +
            "214121412121111143111341131141114113114311411113411311113141" +
            "114131311141411131211412211214211232" +
            "2331112";

        private const int StartB = 104;
        private const int StartC = 105;
        private const int SwitchB = 100;
        private const int SwitchC = 99;
        private const int Stop = 106;

        static Code128()
        {
            for (int symbol = 0; symbol <= Stop; symbol++)
            {
                int width = 0;
                foreach (int module in Widths(symbol))
                    width += module;

                int expected = symbol == Stop ? 13 : 11;
                if (width != expected)
                    throw new InvalidOperationException(
                        $"Code 128 symbol {symbol} totals {width} modules where the standard says {expected}. " +
                        "The pattern table is wrong.");
            }
        }

        private static IEnumerable<int> Widths(int symbol)
        {
            int at = symbol * 6;
            int count = symbol == Stop ? 7 : 6;
            for (int i = 0; i < count; i++)
                yield return Patterns[at + i] - '0';
        }

        /// <summary>Bar widths in modules, dark first, for <paramref name="text"/>.</summary>
        public static int[] Encode(string text)
        {
            if (string.IsNullOrEmpty(text))
                throw new ArgumentException("A barcode needs something to encode.", nameof(text));

            foreach (char c in text)
            {
                if (c is < ' ' or > '~')
                    throw new ArgumentException(
                        $"Code 128 carries printable ASCII; '{c}' is outside it. Use a QR code for anything else.",
                        nameof(text));
            }

            List<int> symbols = Symbols(text);

            // The check symbol weights each position, so two misread symbols cannot
            // cancel out: it is the start value plus each symbol times its place.
            int sum = symbols[0];
            for (int i = 1; i < symbols.Count; i++)
                sum += symbols[i] * i;

            symbols.Add(sum % 103);
            symbols.Add(Stop);

            var widths = new List<int>(symbols.Count * 6 + 1);
            foreach (int symbol in symbols)
                widths.AddRange(Widths(symbol));

            return widths.ToArray();
        }

        /// <summary>
        /// Picks the code sets. C carries two digits in one symbol, so a long run of
        /// digits is worth switching for; B carries the printable ASCII that part
        /// numbers and names are made of.
        /// </summary>
        private static List<int> Symbols(string text)
        {
            var symbols = new List<int>();

            // Starting in C pays when the whole value is digits, or when it opens with a
            // long enough run: switching costs a symbol, and so does an odd digit left over.
            int leading = DigitsFrom(text, 0);
            bool numeric = leading == text.Length ? leading % 2 == 0 && leading >= 2 : leading >= 4;

            symbols.Add(numeric ? StartC : StartB);
            int at = 0;

            while (at < text.Length)
            {
                if (numeric)
                {
                    if (DigitsFrom(text, at) >= 2)
                    {
                        symbols.Add((text[at] - '0') * 10 + (text[at + 1] - '0'));
                        at += 2;
                        continue;
                    }

                    symbols.Add(SwitchB);
                    numeric = false;
                    continue;
                }

                int run = DigitsFrom(text, at);
                bool toEnd = at + run == text.Length;

                if (run >= 4 && run % 2 == 0 && (toEnd || run >= 6))
                {
                    symbols.Add(SwitchC);
                    numeric = true;
                    continue;
                }

                symbols.Add(text[at] - 32);
                at++;
            }

            return symbols;
        }

        private static int DigitsFrom(string text, int at)
        {
            int count = 0;
            while (at + count < text.Length && char.IsAsciiDigit(text[at + count]))
                count++;
            return count;
        }
    }
}
