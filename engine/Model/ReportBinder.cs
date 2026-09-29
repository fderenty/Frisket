using System.Text.Json.Serialization;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Frisket
{
    /// <summary>What to do about a token the data has no answer for.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<MissingField>))]
    public enum MissingField
    {
        /// <summary>Leave it on the page, so a typo shows instead of hiding.</summary>
        Show,

        /// <summary>Replace it with nothing.</summary>
        Blank,

        /// <summary>Refuse to bind.</summary>
        Fail
    }

    /// <summary>
    /// Number presentation, chosen by the section rather than by the machine it runs
    /// on. The engine builds these by hand instead of asking for a culture: a report
    /// that reads differently on a French laptop and an English server is a bug, and
    /// the library is compiled with invariant globalization anyway.
    /// </summary>
    public static class NumberFormats
    {
        public static NumberFormatInfo Invariant => CultureInfo.InvariantCulture.NumberFormat;

        /// <summary>Groups with a narrow no-break space, decimals after a comma.</summary>
        public static NumberFormatInfo French { get; } = Build(" ", ",");

        public static NumberFormatInfo Build(string group, string decimalPoint)
        {
            var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
            format.NumberGroupSeparator = group;
            format.NumberDecimalSeparator = decimalPoint;
            format.CurrencyGroupSeparator = group;
            format.CurrencyDecimalSeparator = decimalPoint;
            format.PercentGroupSeparator = group;
            format.PercentDecimalSeparator = decimalPoint;
            return format;
        }
    }

    /// <summary>
    /// What the binder is told that the template does not say: what to do with a token the
    /// data cannot answer, how numbers are written out, and which tokens are none of the
    /// data business. Bind makes a default one when none is passed - an unanswered token
    /// stays visible on the page, and numbers group with commas and take a decimal point.
    ///
    /// Numbers reaches only the values the binder writes, and only the ones the data holds
    /// as numbers. A figure typed into the template is untouched, an amount the data holds
    /// as a string is passed through as it stands, and the reserved tokens are left for the
    /// resolver, which writes them invariant whatever was asked for here. Ask for French
    /// and a single page can carry three conventions at once - bound, typed, passed through
    /// - each consistent with itself, with nothing to fail.
    /// </summary>
    public sealed class BindingOptions
    {
        public MissingField MissingField { get; set; } = MissingField.Show;

        /// <summary>How numbers are written out. Invariant unless the section says otherwise.</summary>
        public NumberFormatInfo Numbers { get; set; } = NumberFormats.Invariant;

        /// <summary>
        /// Tokens the binder must not touch, because something later answers them: the
        /// page number cannot be known before the document is paginated, and a heading
        /// number is worked out from the tree once it is bound.
        ///
        /// Anything NOT in here, the data answers. That is the whole weight of this set:
        /// a data file carrying a field called <c>HeadingNumber</c> silently replaces the
        /// system token, and nothing says a word.
        ///
        /// Spelled from the constants, not beside them. It held five bare literals -
        /// "Page", "Pages", "Title", "Date", "Time" - sitting next to
        /// <see cref="ReportTokens.PageToken"/>, which is that same string. It was a
        /// sixth place to keep in step, and it had already fallen behind by four:
        /// SectionPage and SectionPages since sections were added, HeadingNumber and
        /// SectionHeadingNumber the day they were. Test 68 holds this set to the tokens
        /// the engine actually declares.
        /// </summary>
        public HashSet<string> Reserved { get; } =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ReportTokens.PageToken, ReportTokens.PagesToken,
                ReportTokens.SectionPageToken, ReportTokens.SectionPagesToken,
                ReportTokens.TitleToken, ReportTokens.DateToken, ReportTokens.TimeToken,
                HeadingNumbers.Token, HeadingNumbers.SectionToken,
            };
    }

    /// <summary>Raised when the data cannot answer a token and the options say to refuse.</summary>
    public sealed class MissingDataException : Exception
    {
        public string Field { get; }

        public MissingDataException(string field)
            : base($"The data has no value for '{field}'.")
            => Field = field;
    }

    /// <summary>
    /// Fills a template from a data section, producing an ordinary report.
    ///
    /// Binding is a pass of its own, before any layout: every repetition is stamped out
    /// and every value written in, and what comes back is a plain <see cref="Report"/>.
    /// That buys three things. The resolver is untouched, so pagination - the delicate
    /// part - is not reopened. The result can be serialized and opened in the designer,
    /// which makes a filled template something you can look at. And each repeated row is
    /// its own object, which the placement and page-scope code relies on.
    ///
    /// The price is memory: a hundred thousand rows means a hundred thousand items. For
    /// an invoice that is nothing; for a listing of that size it would be the wrong
    /// design.
    /// </summary>
    public static class ReportBinder
    {
        public static Report Bind(Report template, ReportData data, BindingOptions? options = null)
        {
            if (template is null) throw new ArgumentNullException(nameof(template));
            if (data is null) throw new ArgumentNullException(nameof(data));

            options ??= new BindingOptions();

            // Work on a copy: a template is a section to be filled many times over.
            Report report = Report.FromJson(template.ToJson());

            // The title carries tokens too, and {Title} elsewhere resolves through it.
            report.Title = Fill(report.Title, data, null, options);

            // So do the rest of the document's own properties. An archive searches on
            // these, so a subject reading "Facture {Number}" would be indexed with the
            // brace in it - findable by nobody.
            if (ReportTokens.HasTokens(report.Author))
                report.Author = Fill(report.Author!, data, null, options);
            if (ReportTokens.HasTokens(report.Subject))
                report.Subject = Fill(report.Subject!, data, null, options);
            if (ReportTokens.HasTokens(report.Keywords))
                report.Keywords = Fill(report.Keywords!, data, null, options);

            BindContainer(report.Section, data, null, options);
            report.Reattach();
            return report;
        }

        public static Report Bind(Report template, string dataJson, BindingOptions? options = null)
            => Bind(template, ReportData.FromJson(dataJson), options);

        // ------------------------------------------------------------------

        private static void BindContainer(ContainerItem container, ReportData data,
                                          ReportData.Scope? scope, BindingOptions options)
        {
            if (container.RepeatOver != null)
            {
                Repeat(container, data, scope, options);
                return;
            }

            BindItem(container, data, scope, options);

            foreach (BaseItem child in container.Childs.ToArray())
                Bind(child, data, scope, options);
        }

        private static void Bind(BaseItem item, ReportData data, ReportData.Scope? scope, BindingOptions options)
        {
            // Decided before anything is filled in. An item that is not going to appear
            // has no reason to have its tokens resolved, and a container that goes takes
            // its children with it.
            if (!Shows(item, data, scope))
            {
                item.Style.Visibility = Visibility.Gone;
                return;
            }

            if (item is ContainerItem container)
                BindContainer(container, data, scope, options);
            else
                BindItem(item, data, scope, options);
        }

        private static void BindItem(BaseItem item, ReportData data, ReportData.Scope? scope, BindingOptions options)
        {
            // An outline entry is data too: one bookmark per invoice in a batch is the
            // whole reason the property exists.
            if (ReportTokens.HasTokens(item.Bookmark))
                item.Bookmark = Fill(item.Bookmark!, data, scope, options);

            // "Code-barres de la facture {Number}" is only useful once the number is in
            // it - and this is text nobody proofreads, because nobody sees it.
            if (ReportTokens.HasTokens(item.AltText))
                item.AltText = Fill(item.AltText!, data, scope, options);

            switch (item)
            {
                case TextItem text when ReportTokens.HasTokens(text.Text):
                    text.Text = Fill(text.Text, data, scope, options);
                    break;

                case AdvancedTextItem advanced:
                    // Each run keeps its own text filled independently, the same as a
                    // plain TextItem - {Page} and {Pages} are left for the resolver,
                    // Reserved sees to that regardless of which run asks for them.
                    foreach (TextRun run in advanced.Runs)
                    {
                        if (ReportTokens.HasTokens(run.Text))
                            run.Text = Fill(run.Text, data, scope, options);
                    }
                    break;

                case BarcodeItem code when ReportTokens.HasTokens(code.Value):
                    // The whole point of a barcode on an invoice: the template carries
                    // {InvoiceNumber} and the data decides what is encoded. Left out, the
                    // token is encoded literally and the scanner reads back the brace.
                    code.Value = Fill(code.Value, data, scope, options);
                    break;

                case FieldItem field:
                    // A field's starting state is data like anything else: it is what
                    // makes a ticked box and a typed name arrive from the form that was
                    // filled in, rather than being written into the template.
                    if (ReportTokens.HasTokens(field.Value))
                        field.Value = Fill(field.Value!, data, scope, options);
                    if (ReportTokens.HasTokens(field.Tooltip))
                        field.Tooltip = Fill(field.Tooltip!, data, scope, options);
                    if (field.Name.Length > 0 && ReportTokens.HasTokens(field.Name))
                        field.Name = Fill(field.Name, data, scope, options);
                    break;

                case ImageItem image when image.SourceField != null:
                    BindImage(image, data, scope);
                    break;
            }
        }

        /// <summary>
        /// Pulls an image's bytes out of the data. Both a bare base64 string and a
        /// data: URI are accepted, since a browser hands over the latter.
        /// </summary>
        private static void BindImage(ImageItem image, ReportData data, ReportData.Scope? scope)
        {
            string encoded = ReportData.Format(data.Resolve(image.SourceField!, scope), null,
                                               System.Globalization.CultureInfo.InvariantCulture.NumberFormat);

            if (string.IsNullOrWhiteSpace(encoded))
            {
                // Nothing to show and nothing to guess: an unsigned quote simply has no
                // signature on it.
                image.Style.Visibility = Visibility.Gone;
                return;
            }

            string mediaType = "image/png";
            int comma = encoded.IndexOf(',');

            if (encoded.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
            {
                string header = encoded[5..comma];
                int semicolon = header.IndexOf(';');

                // A token, or the default. With no semicolon the whole header used to
                // become the media type, so anything a caller cared to put there
                // travelled into the model and out into an SVG attribute. The backend
                // escapes it now, and this stops the nonsense arriving in the first
                // place: what is not a media type is not stored as one.
                mediaType = AsMediaType(semicolon > 0 ? header[..semicolon] : header)
                            ?? mediaType;

                encoded = encoded[(comma + 1)..];
            }

            try
            {
                // No SourcePath. It was set to the field's path and read by exactly
                // one thing - the PDF image cache - which used it as an identity, so
                // every record's picture answered to the same name and only the first
                // was ever embedded. The cache is keyed on the bytes now, and a field
                // path was never a path to a file anyway.
                image.Source = new ImageSource
                {
                    Data = Convert.FromBase64String(encoded.Trim()),
                    MediaType = mediaType
                };
            }
            catch (FormatException)
            {
                throw new MissingDataException(image.SourceField!);
            }
        }

        /// <summary>
        /// Stamps the prototype out once per record. A table keeps its header rows and
        /// cycles the rest, so two prototypes alternate; any other container repeats its
        /// whole set of children as one block.
        /// </summary>
        private static void Repeat(ContainerItem container, ReportData data,
                                   ReportData.Scope? outer, BindingOptions options)
        {
            string collection = container.RepeatOver!;

            // Looked up in the enclosing record first, which is what makes nesting work:
            // a band repeating over "Groups" holds a table repeating over its own "Lines".
            JsonArray? records = data.Collection(collection, outer);

            // Grouping a flat collection produces the same shape a pre-grouped section
            // would have had, so nothing downstream knows the difference.
            if (records != null && container.GroupBy != null)
                records = ReportData.GroupBy(records, container.GroupBy, container.GroupItems);

            // Spent once used. Binding the result again would otherwise take the rows it
            // just produced for prototypes and multiply them.
            container.RepeatOver = null;

            bool isTable = container is TableItem;
            var kept = new List<BaseItem>();
            var prototypes = new List<BaseItem>();

            foreach (BaseItem child in container.Childs)
            {
                if (isTable && child is TableRow { IsHeader: true })
                    kept.Add(child);
                else
                    prototypes.Add(child);
            }

            foreach (BaseItem child in container.Childs.ToArray())
                container.Remove(child);

            // Nothing to show: the container says so itself rather than leaving an empty
            // frame and a stranded header behind.
            if (records == null || records.Count == 0)
            {
                if (container.EmptyText == null)
                {
                    container.Style.Visibility = Visibility.Gone;
                    return;
                }

                foreach (BaseItem child in kept)
                    container.Add(child);

                container.Add(EmptyRow(container, prototypes, container.EmptyText));
                return;
            }

            foreach (BaseItem child in kept)
            {
                container.Add(child);
                Bind(child, data, outer, options);
            }

            if (prototypes.Count == 0)
                return;

            for (int i = 0; i < records.Count; i++)
            {
                // The record joins the chain rather than replacing it, so a nested group
                // still reaches the one around it.
                ReportData.Scope? inner = records[i] is JsonObject record
                    ? outer?.Inside(record) ?? new ReportData.Scope { Record = record }
                    : outer;

                if (isTable)
                {
                    // One prototype per record, cycling: two of them band the rows.
                    BaseItem copy = prototypes[i % prototypes.Count].Clone();
                    container.Add(copy);
                    Bind(copy, data, inner, options);
                    continue;
                }

                foreach (BaseItem prototype in prototypes)
                {
                    BaseItem copy = prototype.Clone();
                    container.Add(copy);
                    Bind(copy, data, inner, options);
                }
            }
        }

        /// <summary>Builds the stand-in shown when a collection turns out empty.</summary>
        private static BaseItem EmptyRow(ContainerItem container, List<BaseItem> prototypes, string message)
        {
            var label = new TextItem
            {
                Text = message,
                Font = new() { HorizontalAlignment = HorizontalAlignment.Center }
            };

            // In a table the message has to be a row, or it would not sit in the columns.
            if (container is not TableItem)
                return label;

            var row = new TableRow();
            if (prototypes.FirstOrDefault() is ContainerItem model)
                row.Style = model.Style.Clone();

            row.Add(label);
            return row;
        }

        /// <summary>
        /// Whether <paramref name="item"/> earns its place, given what the data holds at
        /// the paths it named. Silent when it named none, which is nearly every item.
        /// </summary>
        private static bool Shows(BaseItem item, ReportData data, ReportData.Scope? scope)
        {
            if (item.ShowWhen is { Length: > 0 } show && !HasSomething(show, data, scope))
                return false;

            return item.HideWhen is not { Length: > 0 } hide || !HasSomething(hide, data, scope);
        }

        /// <summary>
        /// <paramref name="candidate"/> if it is shaped like a media type, and null if it
        /// is anything else - in which case the caller keeps its own default rather than
        /// storing a string that only looks like one.
        ///
        /// The grammar is RFC 6838's, narrowed: one slash, and on either side of it the
        /// characters a registered name may use. Deliberately narrow - a media type with
        /// a quote or an angle bracket in it is not a media type that was mistyped, it is
        /// something else wearing the name.
        /// </summary>
        private static string? AsMediaType(string candidate)
        {
            candidate = candidate.Trim();
            int slash = candidate.IndexOf('/');
            if (slash <= 0 || slash == candidate.Length - 1 || candidate.Length > 127)
                return null;

            foreach (char c in candidate)
            {
                bool allowed = char.IsAsciiLetterOrDigit(c)
                            || c is '/' or '.' or '+' or '-' or '_';
                if (!allowed)
                    return null;
            }

            return candidate.IndexOf('/', slash + 1) < 0 ? candidate : null;
        }

        /// <summary>
        /// True when the data has something worth showing at <paramref name="path"/>.
        ///
        /// Empty is the useful reading of empty, not the strict one: an absent key, a
        /// null, a blank string, a zero and a false all mean there is nothing to print.
        /// A template says <c>ShowWhen: "Client.Vat"</c> and means "when there is a VAT
        /// number", which is false in every one of those cases.
        /// </summary>
        private static bool HasSomething(string path, ReportData data, ReportData.Scope? scope)
        {
            // Written with braces as often as not, since that is how the same path is
            // spelled in the text right beside it.
            path = path.Trim().Trim('{', '}').Trim();

            JsonNode? value = data.Resolve(path, scope);
            if (value is null)
                return false;

            if (value is JsonArray array)
                return array.Count > 0;

            if (value is not JsonValue leaf)
                return true;                        // an object with something in it

            if (leaf.TryGetValue(out bool flag))
                return flag;

            if (leaf.TryGetValue(out double number))
                return number != 0;

            return !string.IsNullOrWhiteSpace(leaf.ToString());
        }

        private static string Fill(string text, ReportData data, ReportData.Scope? scope, BindingOptions options)
            => ReportTokens.Resolve(text, token =>
            {
                int colon = token.IndexOf(':');
                string path = colon < 0 ? token : token[..colon];
                string? format = colon < 0 ? null : token[(colon + 1)..];

                // Left for the resolver: these are not the data's to answer.
                if (options.Reserved.Contains(path))
                    return null;

                if (data.Has(path, scope))
                    return ReportData.Format(data.Resolve(path, scope), format, options.Numbers);

                return options.MissingField switch
                {
                    MissingField.Blank => string.Empty,
                    MissingField.Fail => throw new MissingDataException(path),
                    _ => null       // Show: leave the token visible
                };
            },
            unescape: false);       // the resolver runs after us and needs the escapes intact
    }
}
