using System.Globalization;
using System.Text;

namespace Frisket.Import
{
    /// <summary>
    /// What a PDF object is, for the purpose of copying one.
    ///
    /// Eight shapes and no more, because that is all ISO 32000-1 has: null, a boolean, a
    /// number, a string, a name, an array, a dictionary and a reference - plus a stream,
    /// which is a dictionary with bytes attached.
    ///
    /// It carries no meaning. Nothing here knows what <c>/Font</c> is for, and that is
    /// the point: a page is copied, not understood, so the model has to be able to hold
    /// anything and interpret nothing.
    /// </summary>
    public abstract class PdfObject
    {
        /// <summary>
        /// Writes this out, sending every reference through <paramref name="renumber"/>.
        ///
        /// Renumbering is why this is a model at all rather than a slice of the original
        /// bytes. A copied object names other objects by number, those numbers belong to
        /// the file it came from, and rewriting "12 0 R" in raw text would also rewrite a
        /// string that happens to read "12 0 R" - a caption, a title, a bookmark. The
        /// only safe rewrite is one that knows which of those it is looking at.
        /// </summary>
        public abstract void Write(StringBuilder into, Func<int, int> renumber);

        /// <summary>Every object this one names, directly. Used to walk the closure.</summary>
        public virtual IEnumerable<int> References() => Array.Empty<int>();

        public sealed override string ToString()
        {
            var sb = new StringBuilder();
            Write(sb, n => n);
            return sb.ToString();
        }
    }

    public sealed class PdfNull : PdfObject
    {
        public static readonly PdfNull Instance = new();
        public override void Write(StringBuilder into, Func<int, int> renumber) => into.Append("null");
    }

    public sealed class PdfBool(bool value) : PdfObject
    {
        public bool Value { get; } = value;
        public override void Write(StringBuilder into, Func<int, int> renumber)
            => into.Append(Value ? "true" : "false");
    }

    public sealed class PdfNumber(double value) : PdfObject
    {
        public double Value { get; } = value;

        public override void Write(StringBuilder into, Func<int, int> renumber)
        {
            // Written back the way it is meant to be read: invariant, and without an
            // exponent, which PDF has no syntax for. A number that came in as 1e-5 and
            // went out as "1E-05" would be a file that no longer parses.
            if (Value == Math.Floor(Value) && Math.Abs(Value) < 1e15)
                into.Append(((long)Value).ToString(CultureInfo.InvariantCulture));
            else
                into.Append(Value.ToString("0.######", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// A string, held as the bytes it was written with.
    ///
    /// Bytes rather than text on purpose. A PDF string is either PDFDocEncoded or
    /// UTF-16BE, it may be a date, a piece of binary in a signature, or a font's own
    /// private data - and deciding which is interpretation this has no business doing.
    /// What comes in goes out.
    /// </summary>
    public sealed class PdfString(byte[] bytes, bool hex) : PdfObject
    {
        public byte[] Bytes { get; } = bytes;
        public bool Hex { get; } = hex;

        public override void Write(StringBuilder into, Func<int, int> renumber)
        {
            // Written as hex whenever it holds anything a literal string would have to
            // escape. It is always legal, it never needs balancing, and it removes the
            // one shape of fault that matters here: a byte that closes the string early.
            if (Hex || Bytes.Any(b => b < 32 || b > 126 || b == '(' || b == ')' || b == '\\'))
            {
                into.Append('<');
                foreach (byte b in Bytes)
                    into.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                into.Append('>');
                return;
            }

            into.Append('(');
            foreach (byte b in Bytes)
                into.Append((char)b);
            into.Append(')');
        }
    }

    /// <summary>A name, held without its slash and with <c>#xx</c> escapes resolved.</summary>
    public sealed class PdfName(string value) : PdfObject
    {
        public string Value { get; } = value;

        public override void Write(StringBuilder into, Func<int, int> renumber)
        {
            into.Append('/');
            foreach (char c in Value)
            {
                // Everything outside the regular characters goes back as #xx. A name is
                // the one place a stray delimiter cannot be recovered from, since there
                // is no terminator to look for.
                if (c is > (char)32 and < (char)127 and not '/' and not '#' and not '('
                    and not ')' and not '<' and not '>' and not '[' and not ']'
                    and not '{' and not '}' and not '%')
                    into.Append(c);
                else
                    into.Append('#').Append(((int)c & 0xFF).ToString("x2", CultureInfo.InvariantCulture));
            }
        }
    }

    public sealed class PdfArray(List<PdfObject> items) : PdfObject
    {
        public List<PdfObject> Items { get; } = items;

        public override void Write(StringBuilder into, Func<int, int> renumber)
        {
            into.Append('[');
            for (int i = 0; i < Items.Count; i++)
            {
                if (i > 0) into.Append(' ');
                Items[i].Write(into, renumber);
            }
            into.Append(']');
        }

        public override IEnumerable<int> References() => Items.SelectMany(i => i.References());
    }

    public class PdfDictionary(Dictionary<string, PdfObject> entries) : PdfObject
    {
        public Dictionary<string, PdfObject> Entries { get; } = entries;

        public PdfObject? this[string key] => Entries.GetValueOrDefault(key);

        public override void Write(StringBuilder into, Func<int, int> renumber)
        {
            into.Append("<<");
            foreach ((string key, PdfObject value) in Entries)
            {
                into.Append(' ');
                new PdfName(key).Write(into, renumber);
                into.Append(' ');
                value.Write(into, renumber);
            }
            into.Append(" >>");
        }

        public override IEnumerable<int> References() => Entries.Values.SelectMany(v => v.References());
    }

    /// <summary>
    /// A stream: its dictionary, and its bytes as they sit in the file.
    ///
    /// Raw, still filtered, and copied across that way. Decoding a stream to re-encode it
    /// is work that can only lose - a JPEG re-deflated is bigger and no clearer - and it
    /// would mean this assembly understanding every filter PDF has rather than the one it
    /// needs to find its way about (Flate, for cross-reference and object streams).
    /// </summary>
    public sealed class PdfStream(Dictionary<string, PdfObject> entries, byte[] raw)
        : PdfDictionary(entries)
    {
        public byte[] Raw { get; } = raw;

        /// <summary>
        /// The dictionary alone, without <c>/Length</c>, which whoever writes the bytes
        /// computes for itself.
        /// </summary>
        public string DictionaryText(Func<int, int> renumber)
        {
            var sb = new StringBuilder();
            foreach ((string key, PdfObject value) in Entries)
            {
                if (key == "Length")
                    continue;

                new PdfName(key).Write(sb, renumber);
                sb.Append(' ');
                value.Write(sb, renumber);
                sb.Append(' ');
            }
            return sb.ToString();
        }
    }

    public sealed class PdfReference(int number) : PdfObject
    {
        public int Number { get; } = number;

        public override void Write(StringBuilder into, Func<int, int> renumber)
            => into.Append(renumber(Number)).Append(" 0 R");

        public override IEnumerable<int> References() => new[] { Number };
    }
}
