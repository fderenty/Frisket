using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Frisket
{
    /// <summary>
    /// What the text of a template says that the model will not read: a property no element
    /// of that kind carries, and a text the reader refuses outright.
    ///
    /// <see cref="Report.FromJson"/> skips a property it does not know, as System.Text.Json
    /// does by default - so "Txt" where "Text" was meant, or "text" in the wrong case, gives a
    /// document that comes out perfectly well without the words it was meant to hold, and
    /// nothing says so. A person typing a template meets that rarely; a model writing one
    /// meets it constantly, and has nothing but the output to learn from. Hence this, which
    /// a host runs on the text before anything else, beside <see cref="TemplateValidator"/>.
    ///
    /// Separate from the validator on purpose. The validator reads a Report and is mirrored
    /// by the designer's echo; what is reported here exists only in the text, and is gone
    /// once the text has been read.
    ///
    /// The walk follows the serializer's own metadata - the names it reads, the kinds behind
    /// each "$type" - rather than reflection, so it stays as native AOT builds it.
    /// </summary>
    public static class TemplateJson
    {
        /// <summary>
        /// Every property the model does not read, each with its JSON Pointer and, when one
        /// is close, the name that was probably meant; and the reader's own refusal, when it
        /// refuses the text.
        /// </summary>
        public static IReadOnlyList<TemplateIssue> Check(string json)
        {
            if (json is null) throw new ArgumentNullException(nameof(json));

            var found = new List<TemplateIssue>();

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(json);
            }
            catch (JsonException e)
            {
                found.Add(Invalid(e));
                return found;
            }

            Walk(root, typeof(Report), "", found);

            // And what the reader itself turns down: a value of the wrong shape, a kind it
            // has never heard of. It says where, and that is passed on.
            try
            {
                Report.FromJson(json);
            }
            catch (Exception e) when (e is JsonException or NotSupportedException
                                        or InvalidOperationException or FormatException)
            {
                found.Add(Invalid(e));
            }

            return found;
        }

        private static void Walk(JsonNode? node, Type declared, string pointer, List<TemplateIssue> found)
        {
            if (node is null)
                return;

            declared = Nullable.GetUnderlyingType(declared) ?? declared;
            JsonTypeInfo? info = SourceGenerationContext.Default.GetTypeInfo(declared);
            if (info is null)
                return;

            switch (node)
            {
                case JsonObject map when info.Kind == JsonTypeInfoKind.Dictionary && info.ElementType != null:
                    foreach (var (key, value) in map)
                        Walk(value, info.ElementType, pointer + "/" + Escape(key), found);
                    break;

                case JsonObject shape when info.Kind == JsonTypeInfoKind.Object:
                    JsonTypeInfo kind = Concrete(info, shape) ?? info;
                    var known = new Dictionary<string, JsonPropertyInfo>(StringComparer.Ordinal);
                    foreach (JsonPropertyInfo property in kind.Properties)
                        known[property.Name] = property;

                    foreach (var (key, value) in shape)
                    {
                        // The discriminator, and the schema's own pointer, which an editor
                        // writes at the top of a file and the reader rightly passes over.
                        if (key == "$type" || key == "$schema")
                            continue;

                        string at = pointer + "/" + Escape(key);
                        if (known.TryGetValue(key, out JsonPropertyInfo? property))
                            Walk(value, property.PropertyType, at, found);
                        else
                            found.Add(Unknown(key, kind.Type, known.Keys, at));
                    }
                    break;

                case JsonArray list when info.Kind == JsonTypeInfoKind.Enumerable && info.ElementType != null:
                    for (int i = 0; i < list.Count; i++)
                        Walk(list[i], info.ElementType, pointer + "/" + i.ToString(CultureInfo.InvariantCulture), found);
                    break;
            }
        }

        /// <summary>
        /// The kind an object says it is, for a property that may hold several. Null when it
        /// says nothing, or names a kind nobody knows - which the reader refuses, and that
        /// refusal is reported on its own.
        /// </summary>
        private static JsonTypeInfo? Concrete(JsonTypeInfo info, JsonObject shape)
        {
            JsonPolymorphismOptions? options = info.PolymorphismOptions;
            if (options is null)
                return null;

            string name = options.TypeDiscriminatorPropertyName;
            if (shape[name] is not JsonValue said || !said.TryGetValue(out string? kind))
                return null;

            foreach (JsonDerivedType derived in options.DerivedTypes)
            {
                if (derived.TypeDiscriminator is string id && id == kind)
                    return SourceGenerationContext.Default.GetTypeInfo(derived.DerivedType);
            }
            return null;
        }

        private static TemplateIssue Unknown(string key, Type owner, IEnumerable<string> known, string pointer)
        {
            string? meant = Nearest(key, known);
            string message = $"\"{key}\" is not something a {owner.Name} carries, so it is ignored "
                           + "and the document comes out without whatever it was meant to say."
                           + (meant != null ? $" Did you mean \"{meant}\"?" : "");
            return new TemplateIssue("read.unknownProperty", IssueLevel.Warning, message, null,
                                     owner.Name + " › " + key) { Pointer = pointer };
        }

        private static TemplateIssue Invalid(Exception e)
        {
            string? pointer = e is JsonException { Path: { } path } ? PointerFrom(path) : null;
            return new TemplateIssue("read.invalid", IssueLevel.Error,
                                     "The template cannot be read: " + e.Message, null,
                                     pointer ?? "") { Pointer = pointer };
        }

        /// <summary>
        /// The name most probably meant: the same name in another case - the reader is
        /// case-sensitive - or one within two edits of it, when the name is long enough for two
        /// edits to be a slip rather than another word.
        /// </summary>
        internal static string? Nearest(string written, IEnumerable<string> known)
        {
            string? best = null;
            int bestDistance = int.MaxValue;
            foreach (string name in known)
            {
                if (string.Equals(name, written, StringComparison.OrdinalIgnoreCase))
                    return name;

                int distance = Distance(written.ToLowerInvariant(), name.ToLowerInvariant());
                int allowed = Math.Min(2, Math.Max(1, name.Length / 4));
                if (distance <= allowed && distance < bestDistance)
                {
                    best = name;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private static int Distance(string a, string b)
        {
            var row = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) row[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                int diagonal = row[0];
                row[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int above = row[j];
                    row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1),
                                      diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                    diagonal = above;
                }
            }
            return row[b.Length];
        }

        /// <summary>RFC 6901: a tilde and a slash inside a name are written ~0 and ~1.</summary>
        internal static string Escape(string name) => name.Replace("~", "~0").Replace("/", "~1");

        /// <summary>
        /// The reader's own path - $.Sections[0].Childs[1]['$type'] - as a JSON Pointer.
        /// </summary>
        internal static string PointerFrom(string path)
        {
            var pointer = new StringBuilder();
            int i = path.StartsWith('$') ? 1 : 0;
            while (i < path.Length)
            {
                if (path[i] == '.')
                {
                    int end = i + 1;
                    while (end < path.Length && path[end] != '.' && path[end] != '[') end++;
                    pointer.Append('/').Append(Escape(path[(i + 1)..end]));
                    i = end;
                }
                else if (path[i] == '[')
                {
                    int close = path.IndexOf(']', i);
                    if (close < 0) break;
                    string inside = path[(i + 1)..close];
                    if (inside.Length >= 2 && inside[0] == '\'' && inside[^1] == '\'')
                        inside = inside[1..^1];
                    pointer.Append('/').Append(Escape(inside));
                    i = close + 1;
                }
                else
                {
                    i++;
                }
            }
            return pointer.ToString();
        }
    }
}
