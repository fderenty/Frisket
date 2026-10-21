using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Frisket
{
    /// <summary>
    /// Every token and every path into the data that the host's data will not answer,
    /// checked against the host's own catalogue of what it can fill in.
    ///
    /// The engine prints a token it cannot fill as it was written - "{Recipient.Nmae}", braces
    /// and all - which is honest and arrives late: on the page, once the document exists. A
    /// host that knows what its data holds can say so beforehand, and a model writing a
    /// template from that catalogue can be told which name it got wrong.
    ///
    /// The catalogue is the one the content editor is handed as `fields`: a JSON array of
    /// entries with a `path` - a bare string will do - and, for a list, the `fields` of one
    /// entry. Paths are matched as the binder resolves them: inside a container repeating over
    /// a list, an entry's own fields first and everything outside after; a header row of a
    /// repeated table is printed once, outside the list.
    ///
    /// The engine's own tokens - {Page}, {Title}, {HeadingNumber} and the rest - and the
    /// template's own <see cref="Report.Fields"/> are always known.
    /// </summary>
    public static class TemplateFields
    {
        public static IReadOnlyList<TemplateIssue> Check(Report report, string catalogueJson)
        {
            if (report is null) throw new ArgumentNullException(nameof(report));
            if (catalogueJson is null) throw new ArgumentNullException(nameof(catalogueJson));

            var root = Scope.Parse(JsonNode.Parse(catalogueJson));
            var found = new List<TemplateIssue>();
            var engine = new BindingOptions().Reserved;

            bool Known(string path, IReadOnlyList<Scope> scopes)
            {
                string bare = path.Trim();
                int bracket = bare.IndexOf('[');
                string name = bracket > 0 ? bare[..bracket] : bare;
                if (engine.Contains(name)) return true;
                if (report.Fields.Keys.Any(k => string.Equals(k, bare, StringComparison.OrdinalIgnoreCase))) return true;
                return scopes.Any(scope => scope.Holds(bare));
            }

            void Flag(string path, string pointer, string where, BaseItem? item, IReadOnlyList<Scope> scopes)
            {
                string? meant = TemplateJson.Nearest(path, scopes.SelectMany(s => s.Paths));
                string list = scopes.Count > 1 ? " in an entry of " + scopes[0].Name + " or" : "";
                found.Add(new TemplateIssue("fields.unknown", IssueLevel.Warning,
                    $"\"{path}\" names nothing{list} in the data the host fills in, so it is printed as written, "
                    + "braces and all." + (meant != null ? $" Did you mean \"{meant}\"?" : ""),
                    item, where) { Pointer = pointer });
            }

            void Tokens(string? text, string pointer, string where, BaseItem? item, IReadOnlyList<Scope> scopes)
            {
                if (!ReportTokens.HasTokens(text))
                    return;
                ReportTokens.Resolve(text, token =>
                {
                    int colon = token.IndexOf(':');
                    string path = (colon < 0 ? token : token[..colon]).Trim();
                    if (path.Length > 0 && !Known(path, scopes))
                        Flag(path, pointer, where, item, scopes);
                    return null;
                }, unescape: false);
            }

            void Path(string? path, string pointer, string where, BaseItem item, IReadOnlyList<Scope> scopes)
            {
                string bare = (path ?? "").Trim().TrimStart('{').TrimEnd('}').Trim();
                if (bare.Length > 0 && !Known(bare, scopes))
                    Flag(bare, pointer, where, item, scopes);
            }

            void Walk(BaseItem item, string pointer, IReadOnlyList<Scope> scopes)
            {
                string name = item.GetType().Name;
                Tokens(item.AltText, pointer + "/AltText", name + " › AltText", item, scopes);
                Tokens(item.Bookmark, pointer + "/Bookmark", name + " › Bookmark", item, scopes);
                Path(item.ShowWhen, pointer + "/ShowWhen", name + " › ShowWhen", item, scopes);
                Path(item.HideWhen, pointer + "/HideWhen", name + " › HideWhen", item, scopes);

                switch (item)
                {
                    case TextItem text:
                        Tokens(text.Text, pointer + "/Text", name + " › Text", item, scopes);
                        break;
                    case AdvancedTextItem advanced:
                        for (int i = 0; i < advanced.Runs.Count; i++)
                            Tokens(advanced.Runs[i].Text, $"{pointer}/Runs/{i}/Text", name + " › Runs", item, scopes);
                        break;
                    case BarcodeItem code:
                        Tokens(code.Value, pointer + "/Value", name + " › Value", item, scopes);
                        break;
                    case FieldItem field:
                        Tokens(field.Value, pointer + "/Value", name + " › Value", item, scopes);
                        Tokens(field.Tooltip, pointer + "/Tooltip", name + " › Tooltip", item, scopes);
                        Tokens(field.Name, pointer + "/Name", name + " › Name", item, scopes);
                        break;
                    case ImageItem image:
                        Path(image.SourceField, pointer + "/SourceField", name + " › SourceField", item, scopes);
                        break;
                }

                if (item is not ContainerItem container)
                    return;

                // A repeated container reads an entry first. Its list is looked for where the
                // binder looks: in the entries already entered, then at the root.
                IReadOnlyList<Scope> inside = scopes;
                string? over = container.RepeatOver?.Trim();
                if (!string.IsNullOrEmpty(over))
                {
                    Scope? list = scopes.Select(s => s.List(over)).FirstOrDefault(l => l != null);
                    if (list == null && !Known(over, scopes))
                        Flag(over, pointer + "/RepeatOver", name + " › RepeatOver", item, scopes);
                    inside = list == null ? scopes : new[] { list }.Concat(scopes).ToList();
                }

                for (int i = 0; i < container.Childs.Count; i++)
                {
                    BaseItem child = container.Childs[i];
                    bool printedOnce = container is TableItem && child is TableRow { IsHeader: true };
                    Walk(child, pointer + "/Childs/" + i.ToString(CultureInfo.InvariantCulture),
                         printedOnce ? scopes : inside);
                }
            }

            var top = new[] { root };
            Tokens(report.Title, "/Title", "Report › Title", null, top);
            Tokens(report.Author, "/Author", "Report › Author", null, top);
            Tokens(report.Subject, "/Subject", "Report › Subject", null, top);
            Tokens(report.Keywords, "/Keywords", "Report › Keywords", null, top);

            for (int i = 0; i < report.Sections.Count; i++)
                Walk(report.Sections[i], "/Sections/" + i.ToString(CultureInfo.InvariantCulture), top);
            if (report.BackPage is { } back)
                Walk(back.Content, "/BackPage/Content", top);

            return found;
        }

        /// <summary>What one level of the data holds: its paths, and the lists among them.</summary>
        private sealed class Scope
        {
            private readonly Dictionary<string, Scope?> _fields = new(StringComparer.Ordinal);

            internal string Name { get; private init; } = "";

            internal IEnumerable<string> Paths => _fields.Keys;

            /// <summary>A path the data answers - itself, or the object that holds one.</summary>
            internal bool Holds(string path)
                => _fields.ContainsKey(path) || _fields.Keys.Any(p => p.StartsWith(path + ".", StringComparison.Ordinal));

            /// <summary>The fields of one entry of a list, when this level has that list.</summary>
            internal Scope? List(string path) => _fields.TryGetValue(path, out Scope? entry) ? entry : null;

            internal static Scope Parse(JsonNode? node, string name = "")
            {
                var scope = new Scope { Name = name };
                if (node is not JsonArray entries)
                    return scope;

                foreach (JsonNode? entry in entries)
                {
                    string path = entry switch
                    {
                        JsonValue v when v.TryGetValue(out string? s) => s,
                        JsonObject o when o["path"] is JsonValue p && p.TryGetValue(out string? s) => s,
                        _ => "",
                    };
                    path = path.Trim().TrimStart('{').TrimEnd('}').Trim();
                    if (path.Length == 0)
                        continue;

                    bool isList = entry is JsonObject shape && shape["type"] is JsonValue t
                                  && t.TryGetValue(out string? kind)
                                  && string.Equals(kind, "list", StringComparison.OrdinalIgnoreCase);
                    string label = entry is JsonObject named && named["label"] is JsonValue l
                                   && l.TryGetValue(out string? said) ? said : path;
                    scope._fields[path] = isList ? Parse(((JsonObject)entry!)["fields"], label) : null;
                }
                return scope;
            }
        }
    }
}
