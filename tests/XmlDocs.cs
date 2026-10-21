using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace test
{
    /// <summary>
    /// The model's own /// comments, read back out of Frisket.xml.
    ///
    /// The compiler writes that file because engine/Frisket.csproj asks it to, and the
    /// project reference copies it beside the test binary. It is the only way the prose
    /// in the engine reaches anything at run time: reflection gives names and types and
    /// says nothing about meaning.
    ///
    /// A reference page built without it would repeat the schema in a nicer typeface.
    /// </summary>
    internal sealed class XmlDocs
    {
        private readonly Dictionary<string, XElement> _members = new(StringComparer.Ordinal);

        /// <summary>Where the file was found, for a failure that has to say what is missing.</summary>
        internal string Path { get; }

        /// <summary>
        /// The anchors the page will carry, so a cross-reference becomes a link only when
        /// it lands somewhere. The model refers to plenty the page has no place for - a
        /// static helper, a layout result, a method - and a link to nothing is worse than
        /// no link: it looks like somewhere to go.
        /// </summary>
        internal HashSet<string> OnPage { get; } = new(StringComparer.Ordinal);

        internal int Count => _members.Count;

        private XmlDocs(string path, IEnumerable<XElement> members)
        {
            Path = path;
            foreach (XElement member in members)
                if (member.Attribute("name")?.Value is { } name)
                    _members[name] = member;
        }

        /// <summary>
        /// Beside the test binary, where the project reference puts it. Null when the
        /// documentation file is switched off - a caller must say so rather than quietly
        /// producing a page with no prose on it.
        /// </summary>
        internal static XmlDocs? Beside()
        {
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Frisket.xml");
            if (!File.Exists(path))
                return null;

            XDocument document = XDocument.Load(path);
            return new XmlDocs(path, document.Descendants("member"));
        }

        // ------------------------------------------------------------------
        // Looking one up
        // ------------------------------------------------------------------

        /// <summary>The summary of a type, as HTML, or an empty string.</summary>
        internal string Type(Type type) => Summary("T:" + Key(type));

        /// <summary>The summary of a property, as HTML, or an empty string.</summary>
        internal string Property(PropertyInfo property)
        {
            // A mirror - LocationJson, written as Location - is described by the property it
            // mirrors, whose comment says what the value is for; its own says how it maps to
            // null. The schema reads it the same way (TemplateSchema.Docs): two readers of
            // one comment that disagree would describe one property twice, differently.
            string written = TemplateSchema.SerializedName(property);
            if (written != property.Name)
            {
                string mirrored = Summary("P:" + Key(property.DeclaringType!) + "." + written);
                if (mirrored.Length > 0)
                    return mirrored;
            }
            return Summary("P:" + Key(property.DeclaringType!) + "." + property.Name);
        }

        /// <summary>The summary of a constant or an enum member, as HTML, or an empty string.</summary>
        internal string Field(Type owner, string name) => Summary("F:" + Key(owner) + "." + name);

        /// <summary>
        /// True where the member exists in the file with a summary that says something.
        /// Distinguished from Type/Property returning "" so a caller can count what the
        /// model does not describe rather than what it describes emptily.
        /// </summary>
        internal bool Describes(string key) => _members.TryGetValue(key, out XElement? m)
                                               && !string.IsNullOrWhiteSpace(m.Element("summary")?.Value);

        internal static string Key(Type type) => type.FullName!.Replace('+', '.');

        private string Summary(string key)
            => _members.TryGetValue(key, out XElement? member) && member.Element("summary") is { } summary
                ? Html(summary)
                : "";

        // ------------------------------------------------------------------
        // Rendering it
        // ------------------------------------------------------------------

        /// <summary>
        /// The summary as HTML: blank lines become paragraphs, &lt;c&gt; becomes code, and a
        /// &lt;see cref&gt; becomes a link to that type or property on the page.
        ///
        /// The cross-references are the one thing this page does that the source cannot -
        /// 102 of them in the model, each a place a reader wanted to go next - so they are
        /// rendered rather than flattened to their bare name.
        /// </summary>
        private string Html(XElement summary)
        {
            var text = new StringBuilder();

            foreach (XNode node in summary.Nodes())
            {
                switch (node)
                {
                    case XText plain:
                        text.Append(Escape(Squeeze(plain.Value)));
                        break;

                    case XElement element when element.Name == "see" || element.Name == "seealso":
                        text.Append(Link(element.Attribute("cref")?.Value ?? ""));
                        break;

                    case XElement element when element.Name == "c" || element.Name == "code":
                        text.Append("<code>").Append(Escape(Squeeze(element.Value))).Append("</code>");
                        break;

                    case XElement element when element.Name == "paramref" || element.Name == "typeparamref":
                        text.Append("<code>").Append(Escape(element.Attribute("name")?.Value ?? "")).Append("</code>");
                        break;

                    case XElement element when element.Name == "para":
                        text.Append("\n\n").Append(Escape(Squeeze(element.Value)));
                        break;

                    default:
                        text.Append(Escape(Squeeze(node.ToString())));
                        break;
                }
            }

            return Paragraphs(text.ToString());
        }

        /// <summary>
        /// A cref as a link, when it names something the page has, and as code when it
        /// does not. "T:Frisket.Report" and "P:Frisket.Style.Splitting" both arrive here;
        /// so does "M:Frisket.Report.Resolve(...)", which the page has no anchor for.
        /// </summary>
        private string Link(string cref)
        {
            if (cref.Length < 3 || cref[1] != ':')
                return "<code>" + Escape(cref) + "</code>";

            char kind = cref[0];
            string full = cref[2..];
            string shown = full.StartsWith("Frisket.", StringComparison.Ordinal) ? full[8..] : full;

            // A method or an event has no place of its own on the page; it reads as code.
            if (kind is not ('T' or 'P' or 'F'))
                return "<code>" + Escape(Trimmed(shown)) + "</code>";

            string anchor = kind == 'T' ? "t-" + shown : (kind == 'P' ? "p-" : "e-") + shown;
            if (!OnPage.Contains(anchor))
                return "<code>" + Escape(Trimmed(shown)) + "</code>";

            return "<a href=\"#" + Escape(anchor) + "\"><code>"
                 + Escape(Trimmed(shown)) + "</code></a>";
        }

        private static string Trimmed(string shown)
        {
            int bracket = shown.IndexOf('(');
            if (bracket > 0)
                shown = shown[..bracket];
            return shown;
        }

        /// <summary>
        /// The /// prose is wrapped to a column and indented; a single newline is a wrap
        /// and means nothing, a blank line is a paragraph. Squeezing the first away here
        /// is what lets Paragraphs read the second.
        /// </summary>
        private static string Squeeze(string raw)
        {
            var lines = raw.Replace("\r\n", "\n").Split('\n');
            var text = new StringBuilder();

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    text.Append('\n');
                    continue;
                }

                if (text.Length > 0 && text[^1] != '\n' && text[^1] != ' ')
                    text.Append(' ');
                text.Append(trimmed);
            }

            return text.ToString();
        }

        private static string Paragraphs(string text)
        {
            var blocks = text.Split('\n')
                             .Select(b => b.Trim())
                             .Where(b => b.Length > 0)
                             .ToList();

            return blocks.Count == 0 ? "" : "<p>" + string.Join("</p><p>", blocks) + "</p>";
        }

        private static string Escape(string value)
            => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
