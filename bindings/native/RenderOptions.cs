using System.Globalization;
using System.Text.Json;
using Frisket;

namespace Frisket.Native
{
    /// <summary>
    /// What the caller asked for, read out of a JSON object.
    ///
    /// Options travel as JSON rather than as a struct on purpose. A struct means every
    /// wrapper in every language has to agree on field order, padding and enum width,
    /// and it can never gain a field without breaking all of them at once. A JSON object
    /// costs one parse per section - nothing, beside laying out a page - and a caller
    /// written today still works when a field is added tomorrow.
    ///
    /// Anything absent keeps its default, and an unknown key is refused rather than
    /// ignored: a misspelt option that silently changes nothing is the kind of thing
    /// somebody chases for an afternoon.
    /// </summary>
    internal sealed record RenderOptions
    {
        internal string Format { get; private init; } = "pdf";
        internal PdfConformance Conformance { get; private init; } = PdfConformance.None;
        internal bool Tagged { get; private init; }
        internal bool Subset { get; private init; } = true;
        internal bool Hinting { get; private init; } = true;
        internal bool Interactive { get; private init; } = true;
        internal bool Bare { get; private init; }
        // Invariant to match the engine's own default. A narrow-space thousands
        // separator is a French convention, not a neutral one.
        internal NumberFormatInfo Numbers { get; private init; } = NumberFormats.Invariant;

        // How the PDF's streams are deflated: the engine's zlib unless Zopfli is asked for,
        // and ZopfliSharp's own number of passes unless one is given. Threads: how many
        // streams at once - one, since a library takes no threads its caller did not offer.
        internal bool Zopfli { get; private init; }
        internal int? Iterations { get; private init; }
        internal int Threads { get; private init; } = 1;

        // A check's own two, read only by fk_template_check_for: the same object asks what
        // an output would lose, and these say how hard to look. A render is not asked them,
        // and is told so rather than let one through that changes nothing.
        internal bool Accessible { get; private init; }
        internal string? Fields { get; private init; }

        internal static RenderOptions Parse(string? json, bool check = false)
        {
            if (json is not { Length: > 0 })
                return new RenderOptions();

            using JsonDocument section = JsonDocument.Parse(json);
            if (section.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("options must be a JSON object");

            var options = new RenderOptions();

            foreach (JsonProperty property in section.RootElement.EnumerateObject())
            {
                options = property.Name switch
                {
                    "format" => options with { Format = Word(property) },
                    "conformance" => options with { Conformance = Conform(Word(property)) },
                    "tagged" => options with { Tagged = Flag(property) },
                    "subset" => options with { Subset = Flag(property) },
                    "hinting" => options with { Hinting = Flag(property) },
                    "interactive" => options with { Interactive = Flag(property) },
                    "bare" => options with { Bare = Flag(property) },
                    "numbers" => options with { Numbers = Numbering(Word(property)) },
                    "compression" => options with { Zopfli = Compression(Word(property)) },
                    "iterations" => options with { Iterations = Count(property, "at least 1", n => n >= 1) },
                    "threads" => options with { Threads = Count(property, "-1 or at least 1", n => n is -1 or >= 1) },
                    "accessible" when check => options with { Accessible = Flag(property) },
                    "fields" when check => options with { Fields = Catalogue(property) },
                    "accessible" or "fields" => throw new ArgumentException(
                        $"option '{property.Name}' belongs to fk_template_check_for, not to a render"),
                    _ => throw new ArgumentException($"unknown option '{property.Name}'")
                };
            }

            // After every key, since a JSON object has no order: passes for an encoder nobody
            // asked for would change nothing, which is the misspelt option this refuses above.
            if (options.Iterations != null && !options.Zopfli)
                throw new ArgumentException("option 'iterations' says how hard Zopfli works, so it goes with \"compression\": \"zopfli\"");

            return options;
        }

        private static string Word(JsonProperty property) => property.Value.ValueKind == JsonValueKind.String
            ? property.Value.GetString()!.Trim().ToLowerInvariant()
            : throw new ArgumentException($"option '{property.Name}' wants a string");

        private static bool Flag(JsonProperty property) => property.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ArgumentException($"option '{property.Name}' wants true or false")
        };

        private static string Catalogue(JsonProperty property) => property.Value.ValueKind == JsonValueKind.Array
            ? property.Value.GetRawText()
            : throw new ArgumentException($"option '{property.Name}' wants the catalogue, a JSON array");

        private static int Count(JsonProperty property, string wanted, Func<int, bool> allowed)
            => property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out int n) && allowed(n)
                ? n
                : throw new ArgumentException($"option '{property.Name}' wants a whole number, {wanted}");

        private static bool Compression(string value) => value switch
        {
            "zlib" => false,
            "zopfli" => true,
            _ => throw new ArgumentException($"unknown compression '{value}'. Try zlib or zopfli.")
        };

        private static PdfConformance Conform(string value) => value switch
        {
            "none" => PdfConformance.None,
            "pdfa3b" or "pdf/a-3b" or "3b" => PdfConformance.PdfA3b,
            _ => throw new ArgumentException($"unknown conformance '{value}'. Try none or pdfa3b.")
        };

        private static NumberFormatInfo Numbering(string value) => value switch
        {
            "french" => NumberFormats.French,
            "invariant" => NumberFormats.Invariant,
            _ => throw new ArgumentException($"unknown number format '{value}'. Try french or invariant.")
        };
    }
}
