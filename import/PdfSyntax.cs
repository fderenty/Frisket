using System.Globalization;
using System.Text;

namespace Frisket.Import
{
    /// <summary>
    /// A cursor over the bytes of a PDF, and the parser for one object at a time.
    ///
    /// Everything in here reads bytes somebody else chose, so it is written the way the
    /// font and image readers are (CLAUDE.md sections 8b and 8c): every index is guarded
    /// at <b>both</b> ends, every count is bounded by what the file could possibly hold,
    /// and nesting is bounded outright. A negative offset is not exotic - an offset in a
    /// cross-reference table is unsigned in the file and signed here, so anything past
    /// 0x7FFFFFFF arrives below zero, and a bound that guards only the top is perfectly
    /// true of it.
    /// </summary>
    internal sealed class PdfSyntax(byte[] data)
    {
        /// <summary>
        /// How deep an array or dictionary may nest.
        ///
        /// A hard guard, not a taste: the parser recurses, and a file of nothing but
        /// twenty thousand open brackets would walk the stack down as far as it is long.
        /// Real documents nest a handful deep; a hundred is far past anything a producer
        /// writes and far short of anything that hurts.
        /// </summary>
        public const int MaxDepth = 100;

        private readonly byte[] _data = data;
        private int _at;

        public byte[] Bytes => _data;
        public int Length => _data.Length;

        public int At
        {
            get => _at;
            set => _at = value < 0 ? 0 : value > _data.Length ? _data.Length : value;
        }

        public bool Done => _at >= _data.Length;

        private byte Peek(int ahead = 0)
        {
            int i = _at + ahead;
            return i >= 0 && i < _data.Length ? _data[i] : (byte)0;
        }

        public static bool IsSpace(byte b) => b is 0 or 9 or 10 or 12 or 13 or 32;

        public static bool IsDelimiter(byte b) =>
            b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'['
              or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

        public static bool IsRegular(byte b) => !IsSpace(b) && !IsDelimiter(b);

        /// <summary>
        /// Whitespace, and comments, which are whitespace that runs to the end of the
        /// line. A comment can hold anything at all, including what looks like an object.
        /// </summary>
        public void SkipSpace()
        {
            while (_at < _data.Length)
            {
                byte b = _data[_at];
                if (IsSpace(b)) { _at++; continue; }

                if (b == '%')
                {
                    while (_at < _data.Length && _data[_at] != 10 && _data[_at] != 13)
                        _at++;
                    continue;
                }

                return;
            }
        }

        /// <summary>Reads a run of regular characters - a keyword, a number, an operator.</summary>
        public string ReadToken()
        {
            SkipSpace();
            int start = _at;
            while (_at < _data.Length && IsRegular(_data[_at]))
                _at++;

            // A delimiter on its own is a token too, or a caller looking for "<<" would
            // sit on it forever asking for a word that never comes.
            if (_at == start && _at < _data.Length)
            {
                if (Peek() == '<' && Peek(1) == '<') { _at += 2; return "<<"; }
                if (Peek() == '>' && Peek(1) == '>') { _at += 2; return ">>"; }
                _at++;
                return ((char)_data[start]).ToString();
            }

            return Latin1(start, _at - start);
        }

        public string Latin1(int start, int count)
        {
            if (start < 0 || count <= 0 || start >= _data.Length)
                return string.Empty;

            count = Math.Min(count, _data.Length - start);
            var sb = new StringBuilder(count);
            for (int i = 0; i < count; i++)
                sb.Append((char)_data[start + i]);
            return sb.ToString();
        }

        /// <summary>
        /// Reads one object.
        ///
        /// <paramref name="resolveLength"/> is how a stream finds out how long it is when
        /// its <c>/Length</c> is an indirect reference, which is legal and common - a
        /// producer writing sequentially does not know the length until the stream is
        /// finished. Null means "no document to ask", and then the length is found by
        /// looking for the <c>endstream</c> that must follow.
        /// </summary>
        public PdfObject ReadObject(int depth = 0, Func<int, PdfObject?>? resolveLength = null)
        {
            if (depth > MaxDepth)
                throw new InvalidDataException("Objects nested deeper than a PDF has any business being.");

            SkipSpace();
            if (Done)
                return PdfNull.Instance;

            byte b = Peek();

            switch (b)
            {
                case (byte)'/':
                    return ReadName();

                case (byte)'(':
                    return ReadLiteralString();

                case (byte)'[':
                {
                    _at++;
                    var items = new List<PdfObject>();
                    while (true)
                    {
                        SkipSpace();
                        if (Done) break;
                        if (Peek() == ']') { _at++; break; }

                        int before = _at;
                        items.Add(ReadObject(depth + 1, resolveLength));

                        // A shape the parser does not know would otherwise leave the
                        // cursor where it was and this loop would never end.
                        if (_at == before) { _at++; }
                    }
                    return new PdfArray(items);
                }

                case (byte)'<':
                    if (Peek(1) == '<')
                        return ReadDictionaryOrStream(depth, resolveLength);
                    return ReadHexString();
            }

            string token = ReadToken();
            switch (token)
            {
                case "true": return new PdfBool(true);
                case "false": return new PdfBool(false);
                case "null": return PdfNull.Instance;
                case "": return PdfNull.Instance;
            }

            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                // "12 0 R" is a reference and "12 0" is two numbers, and the only way to
                // tell is to look. Put back if it turns out not to be one.
                int save = _at;
                SkipSpace();
                int generationStart = _at;
                string generation = ReadToken();

                if (int.TryParse(generation, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    SkipSpace();
                    if (Peek() == 'R' && !IsRegular(Peek(1)))
                    {
                        _at++;
                        return new PdfReference((int)number);
                    }
                }

                _at = save;
                _ = generationStart;
                return new PdfNumber(number);
            }

            // An operator, or rubbish. Either way it is not an object, and saying so is
            // better than guessing at one.
            return PdfNull.Instance;
        }

        private PdfName ReadName()
        {
            _at++;                                   // the slash
            var sb = new StringBuilder();

            while (_at < _data.Length && IsRegular(_data[_at]))
            {
                byte c = _data[_at++];
                if (c == '#' && _at + 1 < _data.Length
                    && Hex(_data[_at]) >= 0 && Hex(_data[_at + 1]) >= 0)
                {
                    sb.Append((char)((Hex(_data[_at]) << 4) | Hex(_data[_at + 1])));
                    _at += 2;
                    continue;
                }

                sb.Append((char)c);
            }

            return new PdfName(sb.ToString());
        }

        private static int Hex(byte b) => b switch
        {
            >= (byte)'0' and <= (byte)'9' => b - '0',
            >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
            >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
            _ => -1
        };

        private PdfString ReadLiteralString()
        {
            _at++;                                   // the bracket
            var bytes = new List<byte>();
            int open = 1;

            while (_at < _data.Length)
            {
                byte c = _data[_at++];

                if (c == '\\')
                {
                    if (_at >= _data.Length) break;
                    byte e = _data[_at++];
                    switch (e)
                    {
                        case (byte)'n': bytes.Add(10); break;
                        case (byte)'r': bytes.Add(13); break;
                        case (byte)'t': bytes.Add(9); break;
                        case (byte)'b': bytes.Add(8); break;
                        case (byte)'f': bytes.Add(12); break;
                        case 10: break;                       // a line continued
                        case 13:
                            if (_at < _data.Length && _data[_at] == 10) _at++;
                            break;
                        default:
                            if (e >= '0' && e <= '7')
                            {
                                int value = e - '0';
                                for (int i = 0; i < 2 && _at < _data.Length
                                                && _data[_at] >= '0' && _data[_at] <= '7'; i++)
                                    value = value * 8 + (_data[_at++] - '0');
                                bytes.Add((byte)value);
                            }
                            else bytes.Add(e);
                            break;
                    }
                    continue;
                }

                if (c == '(') { open++; bytes.Add(c); continue; }
                if (c == ')')
                {
                    open--;
                    if (open == 0) break;
                    bytes.Add(c);
                    continue;
                }

                bytes.Add(c);
            }

            return new PdfString(bytes.ToArray(), hex: false);
        }

        private PdfString ReadHexString()
        {
            _at++;                                   // the angle
            var bytes = new List<byte>();
            int high = -1;

            while (_at < _data.Length)
            {
                byte c = _data[_at++];
                if (c == '>') break;

                int v = Hex(c);
                if (v < 0) continue;                 // whitespace and rubbish alike

                if (high < 0) high = v;
                else { bytes.Add((byte)((high << 4) | v)); high = -1; }
            }

            // An odd number of digits: the last one is a high nibble and the low one is
            // zero. Said by the standard, and a file that ends mid-byte is common enough.
            if (high >= 0) bytes.Add((byte)(high << 4));

            return new PdfString(bytes.ToArray(), hex: true);
        }

        private PdfObject ReadDictionaryOrStream(int depth, Func<int, PdfObject?>? resolveLength)
        {
            _at += 2;                                // <<
            var entries = new Dictionary<string, PdfObject>(StringComparer.Ordinal);

            while (true)
            {
                SkipSpace();
                if (Done) break;

                if (Peek() == '>' && Peek(1) == '>') { _at += 2; break; }

                if (Peek() != '/')
                {
                    // Not a key. Step over one byte rather than spinning, and let the
                    // dictionary end where it ends: a malformed one is still worth what
                    // can be read out of it.
                    _at++;
                    continue;
                }

                string key = ReadName().Value;
                PdfObject value = ReadObject(depth + 1, resolveLength);
                entries[key] = value;
            }

            SkipSpace();
            if (!Looking("stream"))
                return new PdfDictionary(entries);

            _at += "stream".Length;

            // The keyword is followed by CRLF or LF, and by neither CR alone nor spaces.
            if (Peek() == 13) _at++;
            if (Peek() == 10) _at++;

            int start = _at;
            int length = -1;

            if (entries.TryGetValue("Length", out PdfObject? declared))
            {
                PdfObject? resolved = declared is PdfReference reference
                    ? resolveLength?.Invoke(reference.Number)
                    : declared;

                if (resolved is PdfNumber n && n.Value >= 0 && n.Value <= _data.Length - start)
                    length = (int)n.Value;
            }

            // Either the length was not to be had, or it does not lead to the end of a
            // stream - a length is a number in a file somebody else wrote, and it is
            // exactly the kind of number section 8c is about. Look for the keyword that
            // has to follow instead.
            if (length < 0 || !EndsStream(start + length))
                length = FindEndstream(start);

            _at = start + length;
            SkipSpace();
            if (Looking("endstream")) _at += "endstream".Length;

            var raw = new byte[length];
            Buffer.BlockCopy(_data, start, raw, 0, length);
            return new PdfStream(entries, raw);
        }

        private bool EndsStream(int at)
        {
            int i = at;
            while (i < _data.Length && IsSpace(_data[i])) i++;
            return Matches(i, "endstream");
        }

        private int FindEndstream(int start)
        {
            for (int i = start; i + 9 <= _data.Length; i++)
            {
                if (_data[i] != 'e' || !Matches(i, "endstream"))
                    continue;

                int end = i;
                // The end-of-line before the keyword belongs to the syntax, not to the
                // stream, so a copied stream that keeps it is one or two bytes longer
                // than it was - which a /Length elsewhere would then contradict.
                if (end > start && _data[end - 1] == 10) end--;
                if (end > start && _data[end - 1] == 13) end--;
                return end - start;
            }

            return Math.Max(0, _data.Length - start);
        }

        public bool Looking(string word) => Matches(_at, word);

        private bool Matches(int at, string word)
        {
            if (at < 0 || at + word.Length > _data.Length)
                return false;

            for (int i = 0; i < word.Length; i++)
                if (_data[at + i] != word[i])
                    return false;

            return true;
        }

        /// <summary>
        /// Where <paramref name="word"/> last appears at or before <paramref name="from"/>.
        /// How the trailer is found: a PDF is read from its end.
        /// </summary>
        public int LastIndexOf(string word, int from)
        {
            int at = Math.Min(from, _data.Length - word.Length);
            for (; at >= 0; at--)
                if (Matches(at, word))
                    return at;

            return -1;
        }
    }
}
