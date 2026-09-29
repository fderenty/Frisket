using System.Globalization;
using System.Reflection;
using System.Text;
using Frisket;

namespace test
{
    /// <summary>
    /// The model as a page a person can read, beside the schema a machine can.
    ///
    /// Generated, never written - the same discipline and the same reason as the schema
    /// (CLAUDE.md section 1). It is produced from four things, and nothing else is
    /// allowed on the page:
    ///
    /// - the walk <see cref="TemplateSchema"/> already makes, borrowed rather than
    ///   repeated, so the two cannot part company about what is in the model;
    /// - the model's own /// comments, through <see cref="XmlDocs"/>;
    /// - the engine answering: the token table is built by handing real tokens to
    ///   <c>ReportTokens.Resolve</c> and printing what comes back, and every default is
    ///   read off a freshly constructed instance rather than described;
    /// - a fixed frame of prose that names no property, no count and no enum value, so
    ///   there is nothing in it the model could contradict.
    ///
    /// A sentence may say WHY. It may never say WHAT the code already states - that rule
    /// exists because two comments in this repository said the model gives a section A4
    /// and that Header stacks, and both were false for months with nothing able to
    /// disagree.
    /// </summary>
    internal static class TemplateReference
    {
        /// <summary>
        /// Blank cells tolerated on the page. Lowered by hand when the number falls, which
        /// is what records the improvement - a ceiling nobody lowers stops measuring
        /// anything, so test 71 also refuses one left more than ten above what it counts.
        ///
        /// It was set at 83 from a count over the source and the first run measured 57:
        /// the page counts what is on the page, which is the walked types, the properties
        /// each declares, the enum members and the tokens - not every public property in
        /// engine/Model.
        /// </summary>
        internal const int BlankCeiling = 57;

        internal static int Blanks { get; private set; }

        internal static string Build(XmlDocs docs)
        {
            Blanks = 0;
            _docs = docs;   // before Tokens(), which reads it - it ran first and counted
                            // every token as undescribed until this line existed

            // The schema's own walk. Build() fills Walked; calling it here is what makes
            // the page and the schema one answer rather than two.
            TemplateSchema.Build();
            List<Type> types = TemplateSchema.Walked.ToList();

            // What the page will carry, worked out before a word of it is rendered: a
            // cross-reference can only become a link once it is known to land.
            docs.OnPage.Clear();
            foreach (Type type in types)
            {
                docs.OnPage.Add("t-" + Name(type));
                if (type.IsEnum)
                    foreach (string member in Enum.GetNames(type))
                        docs.OnPage.Add("e-" + Name(type) + "." + member);
                else
                    foreach (PropertyInfo p in TemplateSchema.Serialized(type).Where(p => p.DeclaringType == type))
                        docs.OnPage.Add("p-" + Name(type) + "." + TemplateSchema.SerializedName(p));
            }

            var page = new StringBuilder();
            page.Append(Head());
            page.Append(Frame());
            page.Append(Tokens());
            page.Append(Catalogue(types, docs));
            page.Append(Enumerations(types, docs));
            page.Append("</main>\n</body>\n</html>\n");
            return page.ToString();
        }

        // ------------------------------------------------------------------
        // Tokens - the engine answering, not a table
        // ------------------------------------------------------------------

        private static string Tokens()
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Customer"] = "ACME",
            };

            var context = new TokenContext(3, "12", 1, "4", "Quarterly report",
                new DateTimeOffset(2026, 3, 9, 14, 5, 9, TimeSpan.Zero), fields);

            var reserved = new BindingOptions().Reserved;

            var rows = new StringBuilder();
            foreach ((string name, string owner) in TokenNames())
            {
                string bare = ReportTokens.Resolve("{" + name + "}", context);
                string dated = ReportTokens.Resolve("{" + name + ":yyyy-MM-dd}", context);
                string padded = ReportTokens.Resolve("{" + name + ":000}", context);
                string lowered = ReportTokens.Resolve("{" + name.ToLowerInvariant() + "}", context);

                string formats = padded != bare ? "a number format: <code>{" + name + ":000}</code> gives <code>" + Escape(padded) + "</code>"
                               : dated != bare ? "a date format: <code>{" + name + ":yyyy-MM-dd}</code> gives <code>" + Escape(dated) + "</code>"
                               : "a format is accepted and ignored";

                rows.Append("<tr><td><code>{").Append(Escape(name)).Append("}</code></td>")
                    .Append("<td><code>").Append(Escape(bare)).Append("</code></td>")
                    .Append("<td>").Append(formats).Append("</td>")
                    .Append("<td>").Append(lowered == bare ? "however it is spelt" : "exactly as spelt").Append("</td>")
                    .Append("<td>").Append(reserved.Contains(name) ? "left for the resolver" : "<b>the data may answer it</b>").Append("</td>")
                    .Append("<td>").Append(Doc(owner, name)).Append("</td></tr>\n");
            }

            return "<h2 id=\"tokens\">Tokens</h2>\n"
                 + "<p class=\"lede\">What a token in a text resolves to. Every cell below is the "
                 + "engine answering, on one made-up document: sheet 3 of 12, page 1 of 4 in its "
                 + "section, titled <i>Quarterly report</i>, generated on 2026-03-09 at 14:05, "
                 + "with one field called <code>Customer</code>.</p>\n"
                 + "<table><thead><tr><th>Token</th><th>Gives</th><th>Format</th><th>Case</th>"
                 + "<th>Binding</th><th>What it is</th></tr></thead><tbody>\n"
                 + rows + "</tbody></table>\n";
        }

        private static IEnumerable<(string Name, string Owner)> TokenNames()
        {
            foreach (Type owner in new[] { typeof(ReportTokens), typeof(HeadingNumbers) })
                foreach (FieldInfo f in owner.GetFields(BindingFlags.Public | BindingFlags.Static)
                                             .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                                             .OrderBy(f => f.MetadataToken))
                    yield return ((string)f.GetRawConstantValue()!, owner.FullName + "." + f.Name);
        }

        private static string Doc(string key, string fallback)
        {
            string text = _docs?.Describes("F:" + key) == true ? _docs.Field(Owner(key), Member(key)) : "";
            if (text.Length != 0)
                return text;
            Blanks++;
            return "";
        }

        private static Type Owner(string key)
        {
            string type = key[..key.LastIndexOf('.')];
            return typeof(Report).Assembly.GetType(type)!;
        }

        private static string Member(string key) => key[(key.LastIndexOf('.') + 1)..];

        private static XmlDocs? _docs;

        // ------------------------------------------------------------------
        // The catalogue
        // ------------------------------------------------------------------

        private static string Catalogue(List<Type> types, XmlDocs docs)
        {
            _docs = docs;
            var body = new StringBuilder("<h2 id=\"elements\">Elements</h2>\n"
                + "<p class=\"lede\">Every type a template can hold, in the order the model "
                + "reaches them from the report. A property is described where it is declared "
                + "and linked from every kind that inherits it - more than half of what the "
                + "schema states is that repetition.</p>\n");

            foreach (Type type in types.Where(t => !t.IsEnum))
            {
                string summary = docs.Type(type);
                if (summary.Length == 0) Blanks++;

                body.Append("<section class=\"type\" id=\"t-").Append(Escape(Name(type))).Append("\">\n")
                    .Append("<h3>").Append(Escape(Name(type)));
                if (TemplateSchema.Discriminator(type) is { } tag)
                    body.Append(" <span class=\"tag\">\"$type\": \"").Append(Escape(tag)).Append("\"</span>");
                body.Append("</h3>\n").Append(summary);

                PropertyInfo[] own = TemplateSchema.Serialized(type)
                    .Where(p => p.DeclaringType == type)
                    .ToArray();

                PropertyInfo[] inherited = TemplateSchema.Serialized(type)
                    .Where(p => p.DeclaringType != type)
                    .ToArray();

                if (own.Length > 0)
                {
                    body.Append("<table><thead><tr><th>Property</th><th>Type</th>"
                              + "<th>Default</th><th>What it is</th></tr></thead><tbody>\n");
                    foreach (PropertyInfo p in own)
                        body.Append(Row(type, p, docs));
                    body.Append("</tbody></table>\n");
                }

                if (inherited.Length > 0)
                {
                    body.Append("<p class=\"inherited\">Also carries ")
                        .Append(string.Join(", ", inherited
                            .GroupBy(p => p.DeclaringType!)
                            .Select(g => "<a href=\"#t-" + Escape(Name(g.Key)) + "\">"
                                       + Escape(Name(g.Key)) + "</a>'s " + g.Count())))
                        .Append(".</p>\n");
                }

                body.Append("</section>\n");
            }

            return body.ToString();
        }

        private static string Row(Type owner, PropertyInfo property, XmlDocs docs)
        {
            string summary = docs.Property(property);
            if (summary.Length == 0) Blanks++;

            return "<tr id=\"p-" + Escape(Name(owner) + "." + TemplateSchema.SerializedName(property)) + "\">"
                 + "<td><code>" + Escape(TemplateSchema.SerializedName(property)) + "</code></td>"
                 + "<td><code>" + Escape(Shape(property.PropertyType)) + "</code></td>"
                 + "<td>" + Default(owner, property) + "</td>"
                 + "<td>" + summary + "</td></tr>\n";
        }

        /// <summary>
        /// The value a freshly built instance actually has - measured, never described.
        ///
        /// This column is what stops a sentence being needed for the things sentences
        /// kept getting wrong: <c>Report.Dimension.Width</c> prints 0, and nobody has to
        /// write that there is no default page size for a reader to see it.
        /// </summary>
        private static string Default(Type owner, PropertyInfo property)
        {
            object? made;
            try
            {
                made = owner.GetConstructor(Type.EmptyTypes) == null
                    ? null
                    : Activator.CreateInstance(owner);
            }
            catch
            {
                return "";
            }

            if (made == null)
                return "";

            object? value;
            try { value = property.GetValue(made); }
            catch { return ""; }

            return value switch
            {
                null => "<i>null</i>",
                string s when s.Length == 0 => "<i>empty</i>",
                string s => "<code>" + Escape(s) + "</code>",
                bool b => "<code>" + (b ? "true" : "false") + "</code>",
                double d when double.IsNaN(d) => "<i>unset</i>",
                double d => "<code>" + d.ToString("0.###", CultureInfo.InvariantCulture) + "</code>",
                Enum e => "<code>" + Escape(e.ToString()) + "</code>",
                System.Collections.ICollection c => c.Count == 0 ? "<i>empty</i>" : "<code>" + c.Count + "</code>",
                _ => value.GetType().IsValueType
                     ? "<code>" + Escape(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "") + "</code>"
                     : "<i>an object</i>",
            };
        }

        // ------------------------------------------------------------------
        // Enumerations
        // ------------------------------------------------------------------

        private static string Enumerations(List<Type> types, XmlDocs docs)
        {
            var body = new StringBuilder("<h2 id=\"enums\">Enumerations</h2>\n"
                + "<p class=\"lede\">Each is written into the file as a name. A "
                + "<code>[Flags]</code> one joins several with a comma and a space.</p>\n");

            foreach (Type type in types.Where(t => t.IsEnum))
            {
                string summary = docs.Type(type);
                if (summary.Length == 0) Blanks++;

                bool flags = type.GetCustomAttribute<FlagsAttribute>() != null;
                body.Append("<section class=\"type\" id=\"t-").Append(Escape(Name(type))).Append("\">\n")
                    .Append("<h3>").Append(Escape(Name(type)));
                if (flags) body.Append(" <span class=\"tag\">several at once</span>");
                body.Append("</h3>\n").Append(summary)
                    .Append("<table><tbody>\n");

                foreach (string member in Enum.GetNames(type))
                {
                    string said = docs.Field(type, member);
                    // A member the type's own summary already names is not a blank: a
                    // reader has been told, and a line saying so again would be noise.
                    if (said.Length == 0 && !summary.Contains(">" + member + "<", StringComparison.Ordinal))
                        Blanks++;

                    body.Append("<tr id=\"e-").Append(Escape(Name(type) + "." + member)).Append("\">")
                        .Append("<td><code>").Append(Escape(member)).Append("</code></td>")
                        .Append("<td>").Append(said).Append("</td></tr>\n");
                }

                body.Append("</tbody></table>\n</section>\n");
            }

            return body.ToString();
        }

        // ------------------------------------------------------------------
        // The frame, which names nothing the model could contradict
        // ------------------------------------------------------------------

        private static string Frame()
            => "<main>\n<h1>The Frisket template</h1>\n"
             + "<p class=\"lede\">What a template may hold and what each part does. This page "
             + "is generated from the model itself - its types, its own comments, and the "
             + "engine answering - so it cannot say something the code does not. It is the "
             + "readable face of <a href=\"frisket-template.schema.json\">the JSON schema</a> "
             + "beside it, which says the same things in a form an editor can check as you "
             + "type.</p>\n"
             + "<p class=\"lede\">It describes shape and meaning, not soundness. Whether a "
             + "figure has alternative text, whether a table's rows agree in width, whether a "
             + "template will survive being flattened - those are questions for "
             + "<code>frisket check</code>, which reads a template and answers them.</p>\n"
             + "<p class=\"lede\">The word <i>field</i> means three unrelated things in this "
             + "model, and the page keeps them apart: a form control on the page, a path into "
             + "the data, and a constant declared on the report.</p>\n";

        private static string Name(Type type) => TemplateSchema.Name(type);

        private static string Shape(Type type)
        {
            Type? nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null)
                return Shape(nullable) + "?";

            if (type == typeof(string)) return "text";
            if (type == typeof(bool)) return "true or false";
            if (type == typeof(double) || type == typeof(int)) return "a number";
            if (type.IsEnum) return Name(type);

            if (type.IsArray) return Shape(type.GetElementType()!) + "[]";
            if (type.IsGenericType && type.GetGenericArguments().Length == 1)
                return "a list of " + Shape(type.GetGenericArguments()[0]);

            return Name(type);
        }

        private static string Escape(string value)
            => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        private static string Head()
            => "<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n"
             + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n"
             + "<title>The Frisket template</title>\n<style>\n" + Style() + "</style>\n</head>\n<body>\n";

        /// <summary>
        /// Its own small stylesheet, deliberately not the designer's palette: a tweak to
        /// the tool's chrome has no business rewriting a committed reference page, and
        /// wiring the two together would drag this file under the colour harness for no
        /// benefit to either.
        /// </summary>
        private static string Style()
            => ":root{--ink:#1a1c20;--dim:#5b6068;--line:#d9dde3;--pale:#f5f7fa;--link:#0b5cad;--bg:#fff}\n"
             + "@media (prefers-color-scheme:dark){:root{--ink:#e7e9ec;--dim:#9aa1ab;--line:#2c313a;--pale:#171a1f;--link:#7fb2f0;--bg:#101215}}\n"
             + "*{box-sizing:border-box}\n"
             + "body{margin:0;background:var(--bg);color:var(--ink);"
             + "font:16px/1.55 ui-sans-serif,-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif}\n"
             + "main{max-width:62rem;margin:0 auto;padding:2.5rem 1.25rem 6rem}\n"
             + "h1{font-size:2rem;margin:0 0 1rem;letter-spacing:-.01em}\n"
             + "h2{font-size:1.4rem;margin:3rem 0 .5rem;padding-bottom:.35rem;border-bottom:2px solid var(--line)}\n"
             + "h3{font-size:1.05rem;margin:2rem 0 .35rem}\n"
             + "p{margin:.5rem 0}\n"
             + "p.lede{color:var(--dim);max-width:44rem}\n"
             + "p.inherited{color:var(--dim);font-size:.9rem}\n"
             + "code{font:.88em ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;"
             + "background:var(--pale);padding:.1em .3em;border-radius:3px}\n"
             + "a{color:var(--link)}\na code{background:none;padding:0}\n"
             + "table{width:100%;border-collapse:collapse;margin:.6rem 0 1.2rem;font-size:.93rem}\n"
             + "th{text-align:left;font-weight:600;color:var(--dim);font-size:.82rem;"
             + "text-transform:uppercase;letter-spacing:.04em;padding:.35rem .5rem;border-bottom:1px solid var(--line)}\n"
             + "td{padding:.45rem .5rem;border-bottom:1px solid var(--line);vertical-align:top}\n"
             + "td:first-child{white-space:nowrap}\n"
             + "td p{margin:0 0 .35rem}\ntd p:last-child{margin:0}\n"
             + "section.type{scroll-margin-top:1rem}\n"
             + ".tag{font:.72rem ui-monospace,monospace;color:var(--dim);background:var(--pale);"
             + "padding:.15em .45em;border-radius:3px;font-weight:400;white-space:nowrap}\n"
             + "@media(max-width:40rem){table{display:block;overflow-x:auto}}\n";
    }
}
