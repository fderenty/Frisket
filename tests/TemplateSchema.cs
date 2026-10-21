using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Frisket;

namespace test
{
    /// <summary>
    /// Builds a JSON Schema for a template by reading the model, never by describing it.
    ///
    /// A schema typed out beside the model would be a sixth place to keep in step - see
    /// the table at the top of CLAUDE.md - and it would drift within the week, silently,
    /// because nothing would compare the two. Reading the types means it cannot: a
    /// property added this morning is in the schema this afternoon, and a property
    /// removed takes its declaration with it.
    ///
    /// It lives in the test project on purpose. Reflection over the model is exactly
    /// what native AOT cannot keep, and the engine is kept AOT-clean; here it costs
    /// nothing and the test suite is what regenerates the file.
    ///
    /// What it cannot do is say anything a schema cannot say. Whether a figure has
    /// alternative text, whether a table's rows agree, whether columns were asked of a
    /// container that will ignore them - those are TemplateValidator's, and the schema
    /// says so in its own description rather than looking complete.
    /// </summary>
    internal static class TemplateSchema
    {
        private const string Dialect = "https://json-schema.org/draft/2020-12/schema";

        /// <summary>
        /// Enum properties this pass found writing numbers rather than names, as
        /// "Owner.Property (Enum)". Filled by <see cref="Build"/>; empty is the only
        /// acceptable answer.
        ///
        /// The schema declares every enum as a string with a list of names, and it can
        /// only do that because the model puts JsonStringEnumConverter on each one. That
        /// was an assumption written in a comment: an enum missing its converter writes a
        /// number, the schema keeps promising a name, and no test anywhere notices - the
        /// round trip still works, so only a human reading the file would ever find it.
        ///
        /// Collected here rather than thrown, so a failure can name all of them at once.
        /// </summary>
        internal static List<string> Numbered { get; } = new();

        /// <summary>
        /// Every type this walk reached, in the order it reached them, filled by
        /// <see cref="Build"/>.
        ///
        /// Here so that the reference page can be generated from the same walk rather
        /// than from a second one of its own. A second walk would be a second answer to
        /// "what is in the model", and the two would part company the first time one of
        /// them learned something - which is the whole reason the schema is generated at
        /// all. The page therefore cannot describe a type the schema does not know, and
        /// cannot miss one it does.
        /// </summary>
        internal static List<Type> Walked { get; } = new();

        /// <summary>
        /// Types and properties this pass found with no comment of their own to describe
        /// them, as "Type" or "Type.Property". Filled by <see cref="Build"/>.
        ///
        /// The descriptions are the engine's own /// comments, read from the documentation
        /// file the build writes beside it - never typed here, which would be the sixth place
        /// §1 refuses. So a property with no comment arrives in the schema with nothing to
        /// say, and somebody - or a model - writing a template from it has only the name to
        /// go on. Collected rather than thrown, so a failure names all of them at once.
        /// </summary>
        internal static List<string> Undescribed { get; } = new();

        internal static string Build()
        {
            var definitions = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var pending = new Queue<Type>();
            var seen = new HashSet<Type>();

            Numbered.Clear();
            Walked.Clear();
            Undescribed.Clear();

            Queue(typeof(Report));

            while (pending.Count > 0)
            {
                Type type = pending.Dequeue();
                Walked.Add(type);
                definitions[Name(type)] = Describe(type, Queue);
            }

            var json = new StringBuilder();
            json.Append("{\n  \"$schema\": ").Append(Text(Dialect)).Append(",\n")
                .Append("  \"$id\": ").Append(Text("https://frisket.invalid/schema/template.json")).Append(",\n")
                .Append("  \"title\": ").Append(Text("Frisket template")).Append(",\n")
                .Append("  \"description\": ").Append(Text(
                    "The shape of a template, generated from the model rather than written "
                    + "beside it. It carries structure only: what is required of a section "
                    + "beyond its shape - alternative text on a figure, rows of equal width, "
                    + "columns asked of a container that places its children by coordinates - "
                    + "is checked by TemplateValidator, in the engine and in the designer, and "
                    + "cannot be expressed here. A template this schema accepts may still have "
                    + "things to report.")).Append(",\n")
                .Append("  \"$ref\": ").Append(Text("#/$defs/Report")).Append(",\n")
                .Append("  \"$defs\": {\n");

            bool first = true;
            foreach ((string name, string body) in definitions)
            {
                if (!first) json.Append(",\n");
                first = false;
                json.Append("    ").Append(Text(name)).Append(": ").Append(Indent(body, "    "));
            }

            return json.Append("\n  }\n}\n").ToString();

            void Queue(Type type)
            {
                if (seen.Add(type))
                    pending.Enqueue(type);
            }
        }

        // ------------------------------------------------------------------

        private static string Describe(Type type, Action<Type> queue)
        {
            var properties = new List<string>();

            // The discriminator first: it is what tells one kind of item from another,
            // and an editor completes on it before anything else.
            string? discriminator = Discriminator(type);
            if (discriminator != null)
            {
                properties.Add("\"$type\": {\n  \"const\": " + Text(discriminator)
                               + ",\n  \"description\": " + Text("What kind of element this is.") + "\n}");
            }

            foreach (PropertyInfo property in Serialized(type))
            {
                string rule = Rule(property, queue);
                string? purpose = Docs.Of(property);
                if (purpose == null) Undescribed.Add($"{type.Name}.{SerializedName(property)}");
                properties.Add(Text(SerializedName(property)) + ": " + Described(rule, purpose));
            }

            var body = new StringBuilder("{\n  \"type\": \"object\"");

            // What the kind of thing is, in the engine's own words.
            if (Docs.Of(type) is { } said)
                body.Append(",\n  \"description\": ").Append(Text(said));
            else
                Undescribed.Add(type.Name);

            if (properties.Count > 0)
            {
                body.Append(",\n  \"properties\": {\n");
                for (int i = 0; i < properties.Count; i++)
                {
                    if (i > 0) body.Append(",\n");
                    body.Append("    ").Append(Indent(properties[i], "    "));
                }
                body.Append("\n  }");
            }

            // A template writes only what it decides, so nothing is required - and
            // anything unknown is a mistake worth showing rather than ignoring.
            body.Append(",\n  \"additionalProperties\": false\n}");
            return body.ToString();
        }

        /// <summary>
        /// The properties that actually reach the file: public, readable, writable or a
        /// collection, and not marked to be left out.
        /// </summary>
        internal static IEnumerable<PropertyInfo> Serialized(Type type)
            => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                   .Where(p => p.GetIndexParameters().Length == 0)
                   .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() is not { } ignore
                               || ignore.Condition != JsonIgnoreCondition.Always)
                   // Readable is enough. A get-only property is still written out - a
                   // colour's four channels have no setters and are in every template -
                   // and anything genuinely not serialized carries JsonIgnore.
                   .Where(p => p.CanRead)
                   .Where(p => p.Name != "Item")
                   .OrderBy(p => p.MetadataToken);

        /// <summary>
        /// Records a property whose enum will be written as a number. The converter counts
        /// whether it sits on the property or on the enum type itself - both work, and
        /// this model uses the first.
        /// </summary>
        private static void SpellsItsEnum(PropertyInfo property)
        {
            Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (!type.IsEnum)
                return;

            Type? converter = property.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType
                              ?? type.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType;

            bool spelled = converter is { IsGenericType: true }
                           && converter.GetGenericTypeDefinition() == typeof(JsonStringEnumConverter<>)
                           && converter.GetGenericArguments()[0] == type;

            if (!spelled)
                Numbered.Add($"{property.DeclaringType?.Name}.{property.Name} ({type.Name})");
        }

        internal static string SerializedName(PropertyInfo property)
            => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;

        internal static string? Discriminator(Type type)
            => type.BaseType is { } parent
               && Ancestry(type).SelectMany(t => t.GetCustomAttributes<JsonDerivedTypeAttribute>())
                                .FirstOrDefault(a => a.DerivedType == type) is { } derived
                   ? derived.TypeDiscriminator?.ToString()
                   : null;

        private static IEnumerable<Type> Ancestry(Type type)
        {
            for (Type? at = type; at != null; at = at.BaseType)
                yield return at;
        }

        // ------------------------------------------------------------------

        private static string Rule(PropertyInfo property, Action<Type> queue)
        {
            string rule = Rule(property.PropertyType, queue);
            SpellsItsEnum(property);

            // A property that may be absent may also be written as null, and a schema
            // that forbids that refuses files the engine itself produces.
            bool nullable = !property.PropertyType.IsValueType
                            || Nullable.GetUnderlyingType(property.PropertyType) != null;

            return nullable && rule.StartsWith("{\n  \"type\": \"", StringComparison.Ordinal)
                ? Nullify(rule)
                : rule;
        }

        /// <summary>
        /// Lets a rule accept null as well as its own type. The engine never writes one -
        /// anything at its default is left out entirely - but a template written by hand
        /// or by another tool may, and refusing it would be pedantry rather than help.
        /// </summary>
        private static string Nullify(string rule)
        {
            const string one = "\"type\": \"";
            int at = rule.IndexOf(one, StringComparison.Ordinal);
            if (at < 0)
                return rule;

            int start = at + one.Length;
            int end = rule.IndexOf('"', start);
            if (end < 0)
                return rule;

            string named = rule[start..end];
            return rule[..at] + "\"type\": [\"" + named + "\", \"null\"]" + rule[(end + 1)..];
        }

        private static string Rule(Type type, Action<Type> queue)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type.IsEnum)
            {
                // Written as names, every one of them: the model puts
                // JsonStringEnumConverter on each, and a template reading 3 where it
                // could read "TableHeader" is a template nobody can review.
                // Test 47 holds the model to that now instead of assuming it.
                string[] names = Enum.GetNames(type);

                // A [Flags] enum does not write one name. The converter joins the set
                // with a comma and a space, so an item docked to both sides writes
                // "Left, Right" - which is in no list of single names. A closed enum
                // here declared every real template invalid, 56 times over in the
                // samples alone, and nothing said so: the walk beside it read property
                // names and never once a value.
                if (type.GetCustomAttribute<FlagsAttribute>() != null)
                {
                    string one = "(?:" + string.Join("|", names) + ")";
                    return "{\n  \"type\": \"string\",\n  \"pattern\": "
                         + Text("^" + one + "(?:, " + one + ")*$")
                         + ",\n  \"description\": "
                         + Text("One of " + string.Join(", ", names)
                                + " - or several of them joined with a comma and a space.")
                         + "\n}";
                }

                return "{\n  \"type\": \"string\",\n  \"enum\": [" + string.Join(", ", names.Select(Text)) + "]\n}";
            }

            if (type == typeof(string))
                return "{\n  \"type\": \"string\"\n}";

            if (type == typeof(bool))
                return "{\n  \"type\": \"boolean\"\n}";

            if (type == typeof(int) || type == typeof(long) || type == typeof(short))
                return "{\n  \"type\": \"integer\"\n}";

            // A byte is a colour channel here, and its bounds are worth stating: an
            // editor can then say 300 is wrong before the file is ever loaded.
            if (type == typeof(byte))
                return "{\n  \"type\": \"integer\",\n  \"minimum\": 0,\n  \"maximum\": 255\n}";

            if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
            {
                // NaN is how the model says "undecided" for a coordinate, and JSON has
                // no such number - so those cross as the string "NaN".
                return "{\n  \"type\": [\"number\", \"string\"],\n"
                     + "  \"description\": " + Text("A number; the string \"NaN\" means undecided.") + "\n}";
            }

            if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
                return "{\n  \"type\": \"string\",\n  \"format\": \"date-time\"\n}";

            if (type == typeof(byte[]))
                return "{\n  \"type\": \"string\",\n  \"contentEncoding\": \"base64\"\n}";

            if (Element(type) is { } element)
            {
                // A dictionary keyed by string is an object of free names, not a list.
                if (element.IsGenericType && element.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                    return "{\n  \"type\": \"object\",\n  \"additionalProperties\": "
                         + Indent(Rule(element.GetGenericArguments()[1], queue), "  ") + "\n}";

                return "{\n  \"type\": \"array\",\n  \"items\": "
                     + Indent(Rule(element, queue), "  ") + "\n}";
            }

            if (Derived(type) is { Count: > 0 } family)
            {
                // One branch per kind, each pinned by its own $type. Without that a
                // schema would accept a table where a barcode was meant.
                foreach (Type kind in family)
                    queue(kind);

                return "{\n  \"oneOf\": [" + string.Join(", ", family.Select(k => Reference(k))) + "]\n}";
            }

            // Classes and structs alike. Colour is a struct, and describing it as
            // unconstrained let a schema accept anything at all where four channels
            // were meant - which a generator would take as permission.
            if (type.Namespace == typeof(Report).Namespace && !type.IsPrimitive)
            {
                queue(type);
                return Reference(type);
            }

            // Anything left is something this generator has not been taught. Saying so
            // is better than emitting a rule that quietly accepts everything.
            return "{\n  \"description\": " + Text("Unconstrained: " + type.Name) + "\n}";
        }

        /// <summary>Every concrete kind a polymorphic property may hold, itself included.</summary>
        private static List<Type> Derived(Type type)
            => type.GetCustomAttributes<JsonDerivedTypeAttribute>()
                   .Select(a => a.DerivedType)
                   .ToList();

        private static Type? Element(Type type)
        {
            if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
                return null;

            if (type.IsArray)
                return type.GetElementType();

            return type.IsGenericType ? type.GetGenericArguments().FirstOrDefault() : null;
        }

        private static string Reference(Type type)
            => "{ \"$ref\": " + Text("#/$defs/" + Name(type)) + " }";

        internal static string Name(Type type) => type.Name;

        private static string Text(string value)
            => JsonSerializer.Serialize(value, SourceGenerationContext.Default.String);

        /// <summary>
        /// A rule with the property's description put in it - first, where an editor shows
        /// it - and merged with a description the rule already carries, which says what the
        /// value's form means ("NaN means undecided") rather than what the property is for.
        /// </summary>
        private static string Described(string rule, string? said)
        {
            if (said == null)
                return rule;

            JsonObject shape = JsonNode.Parse(rule)!.AsObject();
            string? already = shape["description"]?.GetValue<string>();
            shape.Remove("description");

            var described = new JsonObject { ["description"] = already == null ? said : said + " " + already };
            foreach (var (key, value) in shape.ToList())
            {
                shape.Remove(key);
                described[key] = value;
            }

            var options = new JsonSerializerOptions(SourceGenerationContext.Default.Options) { WriteIndented = true };
            return described.ToJsonString(options).Replace("\r\n", "\n");
        }

        /// <summary>
        /// The engine's /// comments, read from the documentation file the compiler writes
        /// beside Frisket.dll, cut to what describes the thing: the first paragraphs, up to a
        /// length a description can carry. Much of a comment here is history - why a thing
        /// is the way it is, and what went wrong before - which is the right place for it and
        /// the wrong place for a schema.
        /// </summary>
        private static class Docs
        {
            private static readonly Dictionary<string, string> Summaries = Load();

            private const int Room = 600;

            internal static string? Of(Type type)
                => Find("T:" + XmlName(type));

            internal static string? Of(PropertyInfo property)
            {
                Type owner = property.DeclaringType!;

                // A mirror carries the JSON name for a property the serializer skips -
                // LocationJson for Location - and its own comment is about the mirroring
                // ("maps to null when empty"), not about what the value is for. The property
                // it mirrors says that, so it is asked first.
                string written = SerializedName(property);
                if (written != property.Name && Find("P:" + XmlName(owner) + "." + written) is { } mirrored)
                    return mirrored;

                return Find("P:" + XmlName(owner) + "." + property.Name);
            }

            private static string XmlName(Type type) => type.FullName!.Replace('+', '.');

            private static string? Find(string id)
                => Summaries.TryGetValue(id, out string? text) && text.Length > 0 ? text : null;

            private static Dictionary<string, string> Load()
            {
                string file = Path.Combine(AppContext.BaseDirectory, "Frisket.xml");
                var found = new Dictionary<string, string>(StringComparer.Ordinal);
                if (!File.Exists(file))
                    return found;

                foreach (XElement member in XDocument.Load(file).Descendants("member"))
                {
                    XElement? summary = member.Element("summary");
                    string? id = member.Attribute("name")?.Value;
                    if (summary != null && id != null)
                        found[id] = Cut(Plain(summary));
                }
                return found;
            }

            /// <summary>The summary as text: references by their last name, code as code.</summary>
            private static string Plain(XElement summary)
            {
                var text = new StringBuilder();
                foreach (XNode node in summary.Nodes())
                    Walk(node, text);
                return text.ToString();

                static void Walk(XNode node, StringBuilder into)
                {
                    switch (node)
                    {
                        case XText words:
                            into.Append(words.Value);
                            break;
                        case XElement { Name.LocalName: "see" or "seealso" } see:
                            string target = (string?)see.Attribute("cref") ?? (string?)see.Attribute("langword")
                                            ?? (string?)see.Attribute("href") ?? see.Value;
                            into.Append(see.IsEmpty ? Last(target) : see.Value);
                            break;
                        case XElement { Name.LocalName: "paramref" or "typeparamref" } named:
                            into.Append((string?)named.Attribute("name"));
                            break;
                        case XElement { Name.LocalName: "para" } para:
                            into.Append("\n\n");
                            foreach (XNode inner in para.Nodes()) Walk(inner, into);
                            into.Append("\n\n");
                            break;
                        case XElement element:
                            foreach (XNode inner in element.Nodes()) Walk(inner, into);
                            break;
                    }
                }

                // "T:Frisket.Model.TableItem" reads as TableItem, a member as Type.Member.
                static string Last(string cref)
                {
                    string bare = Regex.Replace(cref, @"^[A-Z]:", "");
                    bare = Regex.Replace(bare, @"\(.*\)$", "");
                    string[] parts = bare.Split('.');
                    return parts.Length >= 2 && char.IsUpper(parts[^2][0]) && cref.StartsWith("T:") is false
                        ? parts[^2] + "." + parts[^1]
                        : parts[^1];
                }
            }

            /// <summary>
            /// The first paragraphs, whole, while they fit in the room - at least the first,
            /// however long, since a description cut mid-sentence says less than none.
            /// </summary>
            private static string Cut(string text)
            {
                string[] paragraphs = Regex.Split(text.Trim(), @"\n\s*\n")
                    .Select(p => Regex.Replace(p, @"\s+", " ").Trim())
                    .Where(p => p.Length > 0)
                    .ToArray();

                var kept = new StringBuilder();
                foreach (string paragraph in paragraphs)
                {
                    if (kept.Length > 0 && kept.Length + paragraph.Length + 1 > Room)
                        break;
                    if (kept.Length > 0) kept.Append(' ');
                    kept.Append(paragraph);
                }
                return kept.ToString();
            }
        }

        private static string Indent(string block, string by)
            => block.Replace("\n", "\n" + by);
    }
}
