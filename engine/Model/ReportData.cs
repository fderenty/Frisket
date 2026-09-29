using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Frisket
{
    /// <summary>
    /// The values a template is filled with: the other half of the pair, kept in its own
    /// file so a template can be designed, reviewed and versioned without any data in it.
    ///
    /// Scalars are read by path - <c>Customer</c>, <c>Totals.HT</c> - and collections by
    /// name, for the containers that repeat over them. Values keep their JSON type, so
    /// how a number or a date is presented stays a decision of the template:
    /// <c>{Total:0.00}</c> rather than a string formatted upstream.
    /// </summary>
    public sealed class ReportData
    {
        private readonly JsonObject _root;

        private ReportData(JsonObject root) => _root = root;

        public static ReportData Empty => new(new JsonObject());

        public static ReportData FromJson(string json)
        {
            JsonNode? node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            return node is JsonObject root
                ? new ReportData(root)
                : throw new JsonException("A data section must be a JSON object.");
        }

        public static ReportData FromFile(string path) => FromJson(File.ReadAllText(path));

        public string ToJson() => _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        /// <summary>
        /// Where a token is read from: the record being repeated over, and the records
        /// around it. Nesting a group inside a group makes this a chain, so a line can
        /// still reach the family it belongs to and the invoice around that.
        /// </summary>
        internal sealed class Scope
        {
            public required JsonObject Record { get; init; }
            public Scope? Outer { get; init; }

            public Scope Inside(JsonObject record) => new() { Record = record, Outer = this };
        }

        /// <summary>
        /// Follows a dotted path, innermost record first and the section last. The
        /// nearest scope wins, which is what lets a line read {Ref} from itself and
        /// {Currency} from the invoice without either having to say where to look.
        /// </summary>
        internal JsonNode? Resolve(string path, Scope? scope)
        {
            for (Scope? at = scope; at != null; at = at.Outer)
            {
                if (TryWalk(at.Record, path, out JsonNode? found))
                    return found;
            }

            return TryWalk(_root, path, out JsonNode? outer) ? outer : null;
        }

        internal bool Has(string path, Scope? scope)
        {
            for (Scope? at = scope; at != null; at = at.Outer)
            {
                if (TryWalk(at.Record, path, out _))
                    return true;
            }

            return TryWalk(_root, path, out _);
        }

        /// <summary>
        /// A collection by path, looked up through the same chain. That is what lets a
        /// band repeat over "Groups" and the table inside it over its own "Items".
        /// </summary>
        internal JsonArray? Collection(string name, Scope? scope)
        {
            for (Scope? at = scope; at != null; at = at.Outer)
            {
                if (TryWalk(at.Record, name, out JsonNode? found))
                    return found as JsonArray;
            }

            return TryWalk(_root, name, out JsonNode? outer) ? outer as JsonArray : null;
        }

        /// <summary>
        /// Buckets a flat collection by the value of one field, in first-seen order, and
        /// hands back one synthetic record per bucket carrying <c>Key</c>, <c>Count</c>,
        /// the grouped rows under <c>Items</c>, and a <c>Sum</c> of every numeric field.
        /// Subtotals without an expression language, and enough for an invoice.
        /// </summary>
        internal static JsonArray GroupBy(JsonArray records, string field, string itemsName)
        {
            var order = new List<string>();
            var buckets = new Dictionary<string, JsonArray>(StringComparer.Ordinal);
            var keys = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);

            foreach (JsonNode? node in records)
            {
                JsonNode? value = node is JsonObject row && TryWalk(row, field, out JsonNode? found) ? found : null;
                string key = value?.ToJsonString() ?? "null";

                if (!buckets.TryGetValue(key, out JsonArray? bucket))
                {
                    buckets[key] = bucket = new JsonArray();
                    keys[key] = value;
                    order.Add(key);
                }

                bucket.Add((JsonNode?)node?.DeepClone());   // the typed overload is not AOT friendly
            }

            var groups = new JsonArray();

            foreach (string key in order)
            {
                JsonArray items = buckets[key];
                var sums = new JsonObject();

                foreach (JsonNode? node in items)
                {
                    if (node is not JsonObject row)
                        continue;

                    foreach (KeyValuePair<string, JsonNode?> property in row)
                    {
                        if (property.Value is not JsonValue leaf
                            || leaf.GetValueKind() != JsonValueKind.Number
                            || !leaf.TryGetValue(out double number))
                            continue;

                        double running = sums.TryGetPropertyValue(property.Key, out JsonNode? had)
                                         && had is JsonValue previous && previous.TryGetValue(out double before)
                            ? before
                            : 0;

                        sums[property.Key] = JsonValue.Create(running + number);
                    }
                }

                var group = new JsonObject
                {
                    ["Key"] = keys[key]?.DeepClone(),
                    ["Count"] = JsonValue.Create(items.Count),
                    ["Sum"] = sums,
                    [itemsName] = items
                };

                // The grouping field is republished under its own name, so a nested group
                // can still say {Family} rather than having to know it became a {Key}.
                if (!field.Contains('.') && field is not ("Key" or "Count" or "Sum") && field != itemsName)
                    group[field] = keys[key]?.DeepClone();

                groups.Add((JsonNode?)group);
            }

            return groups;
        }

        private static bool TryWalk(JsonNode from, string path, out JsonNode? found)
        {
            found = null;
            JsonNode? at = from;

            foreach (string step in path.Split('.'))
            {
                if (at is not JsonObject obj || !obj.TryGetPropertyValue(step, out JsonNode? next))
                    return false;
                at = next;
            }

            found = at;
            return true;
        }

        /// <summary>
        /// Renders a value the way the template asked for. Formatting lives here rather
        /// than in the data, so changing how a total is presented never touches what the
        /// total is. Everything is invariant: a report must not shift with the machine.
        /// </summary>
        internal static string Format(JsonNode? value, string? format, NumberFormatInfo numbers)
        {
            if (value == null)
                return string.Empty;

            if (value is JsonArray array)
                return string.Join(", ", array.Select(item => Format(item, format, numbers)));

            if (value is not JsonValue leaf)
                return value.ToJsonString();

            CultureInfo culture = CultureInfo.InvariantCulture;

            if (leaf.TryGetValue(out bool flag))
                return flag ? "true" : "false";

            if (leaf.TryGetValue(out double number) && leaf.GetValueKind() == JsonValueKind.Number)
                return number.ToString(format ?? "0.##########", numbers);

            string text = leaf.TryGetValue(out string? s) ? s ?? string.Empty : leaf.ToString();

            // A date arrives as a string; a format on the token means it is meant as one.
            if (format != null && DateTimeOffset.TryParse(text, culture, DateTimeStyles.RoundtripKind, out DateTimeOffset when))
                return when.ToString(format, culture);

            return text;
        }
    }
}
