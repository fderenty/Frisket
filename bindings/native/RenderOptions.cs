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
        internal bool Interactive { get; private init; } = true;
        internal bool Bare { get; private init; }
        // Invariant to match the engine's own default. A narrow-space thousands
        // separator is a French convention, not a neutral one.
        internal NumberFormatInfo Numbers { get; private init; } = NumberFormats.Invariant;

        internal static RenderOptions Parse(string? json)
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
                    "interactive" => options with { Interactive = Flag(property) },
                    "bare" => options with { Bare = Flag(property) },
                    "numbers" => options with { Numbers = Numbering(Word(property)) },
                    _ => throw new ArgumentException($"unknown option '{property.Name}'")
                };
            }

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
