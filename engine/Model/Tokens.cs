using System.Globalization;
using System.Text;

namespace Frisket
{
    /// <summary>
    /// Values available to the tokens found in a text: <c>{Page}</c>, <c>{Pages}</c>,
    /// <c>{Title}</c>, <c>{Date}</c>, <c>{Time}</c>, plus anything in
    /// <see cref="Report.Fields"/>. A token may carry a format: <c>{Page:000}</c>,
    /// <c>{Date:yyyy-MM-dd}</c>. Write <c>{{</c> for a literal brace.
    /// </summary>
    public readonly struct TokenContext
    {
        public int PageNumber { get; }

        /// <summary>
        /// Total page count, already rendered as text. It is a string because during
        /// layout the real value is not known yet and a same-width placeholder stands in.
        /// </summary>
        public string PageCount { get; }

        /// <summary>
        /// The page's number within its own section, counting from one. Equal to
        /// <see cref="PageNumber"/> in a report of one section, which is why a template
        /// can use it without knowing whether the report has sections.
        /// </summary>
        public int SectionPageNumber { get; }

        /// <summary>
        /// Pages in this page's own section, as text - a placeholder during layout, for
        /// the same reason <see cref="PageCount"/> is one.
        /// </summary>
        public string SectionPageCount { get; }

        public string Title { get; }
        public DateTimeOffset Timestamp { get; }
        public IReadOnlyDictionary<string, string>? Fields { get; }

        public TokenContext(int pageNumber, string pageCount, string title,
                            DateTimeOffset timestamp, IReadOnlyDictionary<string, string>? fields)
            : this(pageNumber, pageCount, pageNumber, pageCount, title, timestamp, fields)
        {
        }

        public TokenContext(int pageNumber, string pageCount,
                            int sectionPageNumber, string sectionPageCount, string title,
                            DateTimeOffset timestamp, IReadOnlyDictionary<string, string>? fields)
        {
            PageNumber = pageNumber;
            PageCount = pageCount;
            SectionPageNumber = sectionPageNumber;
            SectionPageCount = sectionPageCount;
            Title = title;
            Timestamp = timestamp;
            Fields = fields;
        }
    }

    /// <summary>Token substitution for text items.</summary>
    public static class ReportTokens
    {
        /// <summary>
        /// The sheet this text is being drawn on, counted across the whole file from one.
        /// Backs are not counted: an invoice reading page 1 of 2 has four printed faces,
        /// and the reader counts what is written on the front.
        /// </summary>
        public const string PageToken = "Page";

        /// <summary>
        /// Sheets in the whole file. Not known until pagination has finished, so a
        /// same-width placeholder stands in during layout and the real total is written
        /// into the finished fragments afterwards - which is why nothing moves and
        /// nothing rewraps, and why an advanced text cannot have it corrected.
        /// </summary>
        public const string PagesToken = "Pages";

        /// <summary>
        /// Counting within one section rather than across the file, from one at each
        /// section boundary. Equal to {Page} while a report has one section, so a
        /// template may use it without knowing whether the report will ever gain a
        /// second.
        ///
        /// This summary said the opposite until 24 August 2026 - that {Page} and {Pages}
        /// "keep meaning the whole section". They mean the whole file, and always have.
        /// It is what the Document-to-Section rename of 27 July did to a sentence where
        /// the word meant the file, which is the third thing CLAUDE.md warns a mass
        /// rename does and the last of them still standing.
        /// </summary>
        public const string SectionPageToken = "SectionPage";

        /// <summary>Sheets in this section, on the same terms as {SectionPage}.</summary>
        public const string SectionPagesToken = "SectionPages";

        /// <summary>
        /// Report.Title, already bound - it carries tokens of its own, and they are
        /// resolved before this one hands it over. One per file: it is the PDF /Title and
        /// the dc:title of the XMP packet, so it reads the same in every section.
        /// </summary>
        public const string TitleToken = "Title";

        /// <summary>
        /// When the document was made - Report.GeneratedAt, or the moment of layout if
        /// the template does not say. Takes a format ({Date:dd/MM/yyyy}); the default is
        /// yyyy-MM-dd, and every one of them is written invariant, because a report must
        /// not change shape with the machine that produced it.
        /// </summary>
        public const string DateToken = "Date";

        /// <summary>The same instant as {Date}, defaulting to HH:mm.</summary>
        public const string TimeToken = "Time";

        /// <summary>True when the text carries at least one token.</summary>
        public static bool HasTokens(string? text)
            => !string.IsNullOrEmpty(text) && text!.IndexOf('{') >= 0;

        /// <summary>
        /// True when the text uses <c>{Pages}</c> or <c>{SectionPages}</c> - the tokens
        /// whose value is unknown until every page has been laid out, and which the
        /// resolver therefore has to come back and settle afterwards.
        /// </summary>
        public static bool UsesPageCount(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            for (int i = 0; i + 1 < text!.Length; i++)
            {
                if (text[i] != '{')
                    continue;
                if (text[i + 1] == '{')
                {
                    i++;
                    continue;
                }
                int end = text.IndexOf('}', i + 1);
                if (end < 0)
                    return false;
                ReadOnlySpan<char> name = NameOf(text.AsSpan(i + 1, end - i - 1));
                if (name.Equals(PagesToken, StringComparison.OrdinalIgnoreCase)
                    || name.Equals(SectionPagesToken, StringComparison.OrdinalIgnoreCase))
                    return true;
                i = end;
            }
            return false;
        }

        public static string Resolve(string? text, in TokenContext context)
        {
            TokenContext captured = context;
            return Resolve(text, token => ResolveToken(token, captured), unescape: true);
        }

        /// <summary>
        /// Scans the tokens and hands each one, format and all, to <paramref name="lookup"/>.
        /// Returning null leaves that token where it is, untouched - which is how the
        /// binder can fill in the data and still leave <c>{Page}</c> for the resolver.
        /// </summary>
        /// <param name="unescape">
        /// True on the final pass, where <c>{{</c> becomes a literal brace. An earlier
        /// pass leaves the escape intact so the later one still sees it.
        /// </param>
        public static string Resolve(string? text, Func<string, string?> lookup, bool unescape = true)
        {
            if (string.IsNullOrEmpty(text) || text!.IndexOf('{') < 0)
                return text ?? string.Empty;

            var sb = new StringBuilder(text.Length + 16);

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '{')
                {
                    if (i + 1 < text.Length && text[i + 1] == '{')
                    {
                        sb.Append(unescape ? "{" : "{{");
                        i++;
                        continue;
                    }

                    int end = text.IndexOf('}', i + 1);
                    if (end < 0)
                    {
                        sb.Append(c);
                        continue;
                    }

                    string token = text.Substring(i + 1, end - i - 1);
                    string? replacement = lookup(token);
                    sb.Append(replacement ?? string.Concat("{", token, "}"));
                    i = end;
                    continue;
                }

                if (c == '}' && i + 1 < text.Length && text[i + 1] == '}')
                {
                    sb.Append(unescape ? "}" : "}}");
                    i++;
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        private static ReadOnlySpan<char> NameOf(ReadOnlySpan<char> token)
        {
            int colon = token.IndexOf(':');
            return colon < 0 ? token : token[..colon];
        }

        private static string ResolveToken(string token, in TokenContext context)
        {
            int colon = token.IndexOf(':');
            string name = colon < 0 ? token : token[..colon];
            string? format = colon < 0 ? null : token[(colon + 1)..];

            // Formatting is invariant on purpose: a report must not change shape with
            // the machine's locale.
            CultureInfo culture = CultureInfo.InvariantCulture;

            if (name.Equals(SectionPageToken, StringComparison.OrdinalIgnoreCase))
                return context.SectionPageNumber.ToString(format ?? "0", culture);

            if (name.Equals(SectionPagesToken, StringComparison.OrdinalIgnoreCase))
            {
                if (format != null && int.TryParse(context.SectionPageCount, NumberStyles.Integer,
                                                   culture, out int sectionCount))
                    return sectionCount.ToString(format, culture);
                return context.SectionPageCount;
            }

            if (name.Equals(PageToken, StringComparison.OrdinalIgnoreCase))
                return context.PageNumber.ToString(format ?? "0", culture);

            if (name.Equals(PagesToken, StringComparison.OrdinalIgnoreCase))
            {
                if (format != null && int.TryParse(context.PageCount, NumberStyles.Integer, culture, out int count))
                    return count.ToString(format, culture);
                return context.PageCount;
            }

            if (name.Equals(TitleToken, StringComparison.OrdinalIgnoreCase))
                return context.Title;

            if (name.Equals(DateToken, StringComparison.OrdinalIgnoreCase))
                return context.Timestamp.ToString(format ?? "yyyy-MM-dd", culture);

            if (name.Equals(TimeToken, StringComparison.OrdinalIgnoreCase))
                return context.Timestamp.ToString(format ?? "HH:mm", culture);

            if (context.Fields != null && context.Fields.TryGetValue(name, out string? value))
                return value;

            // Unknown token: leave it visible rather than silently blank, so a typo
            // shows up on the page instead of hiding.
            return string.Concat("{", token, "}");
        }
    }
}
