using System.Text;

namespace Frisket
{
    /// <summary>
    /// Asks whether an SVG can be drawn, before it is handed to a document.
    ///
    /// For whoever generates one. A chart usually reaches the engine through the data, where
    /// <see cref="TemplateValidator"/>, reading the template, cannot see it; it would then be
    /// refused as the document is drawn, in the same words as here. The generator's own tests
    /// are a better place to hear them; <see cref="TemplateData"/> asks the same of a template and
    /// the data that fills it. What is taken is written out in https://github.com/fderenty/Frisket/tree/main/examples/charts.
    /// </summary>
    public static class SvgDrawing
    {
        /// <summary>
        /// What stops this file being drawn, one finding per place, or nothing. Families are
        /// asked of <paramref name="fonts"/> if it is given: a name the library does not hold is
        /// refused as anywhere else, and only the caller knows which library will draw it.
        /// </summary>
        public static IReadOnlyList<TemplateIssue> Check(byte[] svg, FontLibrary? fonts = null)
        {
            if (svg is null) throw new ArgumentNullException(nameof(svg));

            SvgReading reading = SvgReader.Read(svg);
            if (fonts == null || reading.Drawing == null)
                return reading.Issues;

            var found = new List<TemplateIssue>(reading.Issues);
            var asked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Walk(VectorNode node)
            {
                if (node is VectorGroup group)
                {
                    foreach (VectorNode child in group.Children)
                        Walk(child);
                }
                else if (node is VectorText text)
                {
                    foreach (VectorRun run in text.Runs)
                    {
                        string family = run.Style.Family ?? string.Empty;
                        if (family.Length > 0 && asked.Add(family)
                            && fonts.Resolve(family, run.Style.Bold ?? false, run.Style.Italic ?? false) == null)
                        {
                            found.Add(new TemplateIssue("svg.unknownFamily", IssueLevel.Error,
                                $"font-family names \"{family}\", which this library does not hold: "
                                + string.Join(", ", fonts.Families()) + ".", null, "svg › text"));
                        }
                    }
                }
            }

            foreach (VectorNode node in reading.Drawing.Nodes)
                Walk(node);

            return found;
        }

        /// <summary>The same, for a file held as text.</summary>
        public static IReadOnlyList<TemplateIssue> Check(string svg, FontLibrary? fonts = null)
            => Check(Encoding.UTF8.GetBytes(svg ?? throw new ArgumentNullException(nameof(svg))), fonts);
    }

    /// <summary>
    /// What the bytes of a picture are, and whether anything here can draw them. One answer for
    /// the PDF, the proof and the validator, because they disagreeing is the fault this exists
    /// to end: a picture the proof drew, the validator passed and the PDF refused at the end.
    /// </summary>
    internal static class Pictures
    {
        /// <summary>
        /// Starts with a tag, after an optional byte order mark and white space. Told by the
        /// bytes and not by the media type, as a JPEG is: a bare base64 string from the data
        /// says nothing of what it is, and no JPEG or PNG starts this way.
        /// </summary>
        public static bool LooksLikeDrawing(byte[] data)
        {
            int at = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
            while (at < data.Length && data[at] is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r')
                at++;

            return at < data.Length && data[at] == (byte)'<';
        }

        public static bool LooksLikeJpeg(byte[] data)
            => data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

        /// <summary>Why this picture cannot be drawn, in a sentence someone can act on; null if it can. Nothing at all is not a refusal.</summary>
        public static string? Refusal(ImageSource source)
        {
            byte[] data = source.Data;
            if (data.Length == 0 || LooksLikeJpeg(data))
                return null;

            if (source.Reading() is { } reading)
                return reading.Issues.Count == 0 ? null : "This drawing cannot be drawn. " + Say(reading.Issues);

            if (data.Length >= 8 && data[0] == 137 && data[1] == 80 && data[2] == 78 && data[3] == 71)
                return PngImage.TryRead(data, out _, out string reason) ? null : reason;

            return "These bytes are not a JPEG, a PNG or an SVG drawing.";
        }

        /// <summary>A reading's findings as one message, for an exception or a panel.</summary>
        public static string Say(IReadOnlyList<TemplateIssue> issues)
            => string.Join(" ", issues.Select(i => i.Where + ": " + i.Message));
    }
}
