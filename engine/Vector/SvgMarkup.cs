using System.Text;

namespace Frisket
{
    /// <summary>An element of an SVG file, its children being elements and strings of text.</summary>
    internal sealed class SvgElement
    {
        public required string Name { get; init; }
        public List<KeyValuePair<string, string>> Attributes { get; } = new();
        public List<object> Children { get; } = new();

        public string? Attribute(string name)
        {
            foreach (KeyValuePair<string, string> pair in Attributes)
            {
                if (pair.Key == name)
                    return pair.Value;
            }

            return null;
        }
    }

    /// <summary>The bytes are not a well-formed file, which is said once and ends the reading.</summary>
    internal sealed class SvgMalformedException(string message) : Exception(message);

    /// <summary>
    /// Reads the markup of an SVG into elements, and no further.
    ///
    /// Written here rather than taken from System.Xml, for what it cannot do rather than what
    /// it can. An XML parser is built to fetch things and to expand things: external entities
    /// read a file off the machine, nested internal ones turn a kilobyte into a gigabyte, and
    /// the defence is a set of options somebody must remember to set. This one has neither to
    /// switch off - a DOCTYPE with an internal subset is refused, an entity it was not born
    /// knowing is refused, and nothing is ever loaded. The engine holds no reference to
    /// System.Xml anywhere, and the native library is compiled ahead of time from these sources.
    ///
    /// The file comes from somebody else, so the rule is section 8c's: a bad one is refused,
    /// never read past, and the depth is bounded because the reader above it recurses and a
    /// stack overflow is the one failure no catch can answer (section 8g).
    /// </summary>
    internal static class SvgMarkup
    {
        /// <summary>Far beyond any drawing, and well within a thread's stack.</summary>
        public const int MaxDepth = 64;

        public static SvgElement Parse(string text)
        {
            RefuseIllegalCharacters(text);

            int at = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
            var open = new List<SvgElement>();
            SvgElement? root = null;

            while (at < text.Length)
            {
                if (text[at] != '<')
                {
                    int stop = text.IndexOf('<', at);
                    if (stop < 0)
                        stop = text.Length;

                    string run = text[at..stop];
                    if (open.Count > 0)
                        open[^1].Children.Add(Decode(run, text, at));
                    else if (!string.IsNullOrWhiteSpace(run))
                        Fail(text, at, "text outside the root element");

                    at = stop;
                    continue;
                }

                if (Starts(text, at, "<!--"))
                {
                    at = After(text, at + 4, "-->", "a comment never closed");
                    continue;
                }

                if (Starts(text, at, "<?"))
                {
                    at = After(text, at + 2, "?>", "a processing instruction never closed");
                    continue;
                }

                if (Starts(text, at, "<![CDATA["))
                {
                    if (open.Count == 0)
                        Fail(text, at, "CDATA outside the root element");

                    int end = After(text, at + 9, "]]>", "a CDATA section never closed");
                    open[^1].Children.Add(text[(at + 9)..(end - 3)]);
                    at = end;
                    continue;
                }

                if (Starts(text, at, "<!DOCTYPE"))
                {
                    at = SkipDoctype(text, at, root != null);
                    continue;
                }

                if (Starts(text, at, "<!"))
                    Fail(text, at, "a declaration SVG does not need");

                if (Starts(text, at, "</"))
                {
                    at += 2;
                    string closing = ReadName(text, ref at);
                    SkipSpace(text, ref at);
                    if (at >= text.Length || text[at] != '>')
                        Fail(text, at, "an end tag not closed");

                    if (open.Count == 0 || open[^1].Name != closing)
                        Fail(text, at, $"</{closing}> with no matching start tag");

                    open.RemoveAt(open.Count - 1);
                    at++;
                    continue;
                }

                at++;
                var element = new SvgElement { Name = ReadName(text, ref at) };
                bool shut = ReadAttributes(text, ref at, element);

                if (open.Count >= MaxDepth)
                    Fail(text, at, $"elements nested deeper than {MaxDepth}");

                if (open.Count > 0)
                    open[^1].Children.Add(element);
                else if (root == null)
                    root = element;
                else
                    Fail(text, at, "a second root element");

                if (!shut)
                    open.Add(element);
            }

            if (open.Count > 0)
                throw new SvgMalformedException($"<{open[^1].Name}> is never closed.");

            return root ?? throw new SvgMalformedException("There is no element in this file.");
        }

        /// <summary>
        /// Control characters XML forbids, refused in one pass over the whole file so that no
        /// later line has to think about what one would do inside a PDF string or a proof.
        /// </summary>
        private static void RefuseIllegalCharacters(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c < ' ' && c != '\t' && c != '\n' && c != '\r') || c == '￾' || c == '￿')
                    Fail(text, i, "a character XML does not allow");
            }
        }

        private static bool Starts(string text, int at, string what)
            => string.CompareOrdinal(text, at, what, 0, what.Length) == 0;

        private static int After(string text, int from, string closer, string fault)
        {
            int found = text.IndexOf(closer, from, StringComparison.Ordinal);
            if (found < 0)
                Fail(text, from, fault);
            return found + closer.Length;
        }

        /// <summary>
        /// A DOCTYPE naming a DTD is harmless, since nothing here will ever fetch it, and older
        /// generators all write one. An internal subset is another matter: it is where entities
        /// are declared, so it is what every entity attack is written in.
        /// </summary>
        private static int SkipDoctype(string text, int at, bool afterRoot)
        {
            if (afterRoot)
                Fail(text, at, "a DOCTYPE after the root element");

            char quote = '\0';
            for (int i = at + 9; i < text.Length; i++)
            {
                char c = text[i];
                if (quote != '\0')
                {
                    if (c == quote)
                        quote = '\0';
                }
                else if (c is '"' or '\'')
                {
                    quote = c;
                }
                else if (c == '[')
                {
                    Fail(text, i, "a DOCTYPE with an internal subset, which is where entities are declared");
                }
                else if (c == '>')
                {
                    return i + 1;
                }
            }

            Fail(text, at, "a DOCTYPE never closed");
            return text.Length;
        }

        private static void SkipSpace(string text, ref int at)
        {
            while (at < text.Length && text[at] is ' ' or '\t' or '\n' or '\r')
                at++;
        }

        private static string ReadName(string text, ref int at)
        {
            int start = at;
            while (at < text.Length)
            {
                char c = text[at];
                if (char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or ':')
                    at++;
                else
                    break;
            }

            if (at == start)
                Fail(text, at, "a name was expected");

            return text[start..at];
        }

        /// <summary>Reads to the end of a start tag. True when it closed itself.</summary>
        private static bool ReadAttributes(string text, ref int at, SvgElement element)
        {
            while (true)
            {
                int before = at;
                SkipSpace(text, ref at);

                if (at >= text.Length)
                    Fail(text, at, $"<{element.Name}> is never finished");

                if (text[at] == '>')
                {
                    at++;
                    return false;
                }

                if (text[at] == '/')
                {
                    if (at + 1 >= text.Length || text[at + 1] != '>')
                        Fail(text, at, $"<{element.Name}> is badly closed");
                    at += 2;
                    return true;
                }

                if (at == before)
                    Fail(text, at, $"<{element.Name}> needs a space before its next attribute");

                string name = ReadName(text, ref at);
                SkipSpace(text, ref at);
                if (at >= text.Length || text[at] != '=')
                    Fail(text, at, $"the attribute {name} has no value");

                at++;
                SkipSpace(text, ref at);
                if (at >= text.Length || text[at] is not ('"' or '\''))
                    Fail(text, at, $"the value of {name} is not in quotes");

                char quote = text[at++];
                int stop = text.IndexOf(quote, at);
                if (stop < 0)
                    Fail(text, at, $"the value of {name} is never closed");

                string raw = text[at..stop];
                if (raw.Contains('<'))
                    Fail(text, at, $"a < inside the value of {name}");

                if (element.Attribute(name) != null)
                    Fail(text, at, $"the attribute {name} given twice");

                element.Attributes.Add(new(name, Decode(raw, text, at)));
                at = stop + 1;
            }
        }

        /// <summary>
        /// The five entities XML is born knowing, and numbered characters. There is no way to
        /// declare another - the internal subset is refused - so anything else is a mistake.
        /// </summary>
        private static string Decode(string run, string text, int at)
        {
            int amp = run.IndexOf('&');
            if (amp < 0)
                return run;

            var decoded = new StringBuilder(run.Length);
            int from = 0;

            while (amp >= 0)
            {
                decoded.Append(run, from, amp - from);

                int semi = run.IndexOf(';', amp);
                if (semi < 0)
                    Fail(text, at, "an & that starts no entity");

                string name = run[(amp + 1)..semi];
                switch (name)
                {
                    case "amp": decoded.Append('&'); break;
                    case "lt": decoded.Append('<'); break;
                    case "gt": decoded.Append('>'); break;
                    case "quot": decoded.Append('"'); break;
                    case "apos": decoded.Append('\''); break;
                    default:
                        decoded.Append(Numbered(name, text, at));
                        break;
                }

                from = semi + 1;
                amp = run.IndexOf('&', from);
            }

            decoded.Append(run, from, run.Length - from);
            return decoded.ToString();
        }

        private static string Numbered(string name, string text, int at)
        {
            bool hex = name.StartsWith("#x", StringComparison.Ordinal);
            string digits = hex ? name[2..] : name.StartsWith('#') ? name[1..] : string.Empty;

            if (digits.Length == 0
                || !int.TryParse(digits, hex ? System.Globalization.NumberStyles.AllowHexSpecifier
                                             : System.Globalization.NumberStyles.None,
                                 System.Globalization.CultureInfo.InvariantCulture, out int code)
                || code <= 0 || code > 0x10FFFF || code is >= 0xD800 and <= 0xDFFF or 0xFFFE or 0xFFFF
                || (code < 0x20 && code is not (9 or 10 or 13)))
            {
                Fail(text, at, $"the entity &{name}; which SVG does not know");
                return string.Empty;
            }

            return char.ConvertFromUtf32(code);
        }

        [System.Diagnostics.CodeAnalysis.DoesNotReturn]
        private static void Fail(string text, int at, string what)
        {
            int line = 1;
            for (int i = 0; i < at && i < text.Length; i++)
            {
                if (text[i] == '\n')
                    line++;
            }

            throw new SvgMalformedException($"Line {line}: {what}.");
        }
    }
}
