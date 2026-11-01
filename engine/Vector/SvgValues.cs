using System.Globalization;

namespace Frisket
{
    /// <summary>
    /// The values inside an SVG's attributes: lengths, colours, transforms, points and path
    /// data. Each answers null, or false, on anything it does not read, and the caller says so
    /// by name: nothing here guesses.
    /// </summary>
    internal static class SvgValues
    {
        /// <summary>
        /// The largest number taken from a file. Anything past it is a mistake or an attack,
        /// and letting it through would hand a PDF reader coordinates it is free to choke on.
        /// </summary>
        public const double Largest = 1e9;

        /// <summary>A plain number, within reason.</summary>
        public static double? Number(string? value)
        {
            if (value == null)
                return null;

            string trimmed = value.Trim();
            int at = 0;
            double? read = ReadNumber(trimmed, ref at);
            return read != null && at == trimmed.Length ? read : null;
        }

        /// <summary>
        /// A length in the drawing's user units. Percentages and em are refused: the first
        /// asks what it is a percentage of, which this reader does not model, and the second
        /// belongs to <see cref="LengthOrEm"/>, where a font size is at hand.
        /// </summary>
        public static double? Length(string? value)
        {
            if (value == null)
                return null;

            string trimmed = value.Trim();
            int at = 0;
            double? read = ReadNumber(trimmed, ref at);
            if (read == null)
                return null;

            double? scale = trimmed[at..] switch
            {
                "" or "px" => 1,
                "pt" => 96.0 / 72,
                "pc" => 16,
                "mm" => 96 / 25.4,
                "cm" => 96 / 2.54,
                "in" => 96,
                _ => null
            };

            return scale == null ? null : read * scale;
        }

        /// <summary>A shift along a text, which is where em earns its place: dy="0.35em" centres a label.</summary>
        public static double? LengthOrEm(string? value, double fontSize)
        {
            if (value == null)
                return null;

            string trimmed = value.Trim();
            if (trimmed.EndsWith("em", StringComparison.Ordinal))
            {
                double? ems = Number(trimmed[..^2]);
                return ems * fontSize;
            }

            return Length(trimmed);
        }

        /// <summary>A number or a percentage, as opacity takes them, held between 0 and 1 as CSS holds them.</summary>
        public static double? Opacity(string value)
        {
            string trimmed = value.Trim();
            bool percent = trimmed.EndsWith('%');
            double? read = Number(percent ? trimmed[..^1] : trimmed);
            if (read == null)
                return null;

            return Math.Clamp(percent ? read.Value / 100 : read.Value, 0, 1);
        }

        /// <summary>A list of numbers separated by spaces or commas. Null if anything else is in it.</summary>
        public static List<double>? Numbers(string value)
        {
            var numbers = new List<double>();
            int at = 0;

            SkipSeparators(value, ref at);
            while (at < value.Length)
            {
                double? read = ReadNumber(value, ref at);
                if (read == null)
                    return null;

                numbers.Add(read.Value);
                SkipSeparators(value, ref at);
            }

            return numbers;
        }

        private static void SkipSeparators(string text, ref int at)
        {
            while (at < text.Length && (text[at] is ' ' or '\t' or '\n' or '\r' or ','))
                at++;
        }

        /// <summary>
        /// One number off the front of what is left, or null with the position untouched. It
        /// stops where the number stops, because SVG lets them touch: "1.5.5-2" is three.
        /// </summary>
        private static double? ReadNumber(string text, ref int at)
        {
            int i = at;
            if (i < text.Length && text[i] is '+' or '-')
                i++;

            int digits = 0;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
            {
                i++;
                digits++;
            }

            if (i < text.Length && text[i] == '.')
            {
                i++;
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                {
                    i++;
                    digits++;
                }
            }

            if (digits == 0)
                return null;

            // An e only belongs to the number when digits follow it; "em" is a unit.
            if (i < text.Length && text[i] is 'e' or 'E')
            {
                int j = i + 1;
                if (j < text.Length && text[j] is '+' or '-')
                    j++;

                if (j < text.Length && char.IsAsciiDigit(text[j]))
                {
                    while (j < text.Length && char.IsAsciiDigit(text[j]))
                        j++;
                    i = j;
                }
            }

            if (!double.TryParse(text.AsSpan(at, i - at), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || !double.IsFinite(value) || Math.Abs(value) > Largest)
                return null;

            at = i;
            return value;
        }

        // ---- colours ------------------------------------------------------------------------

        /// <summary>
        /// A colour value, its alpha carried in the colour. Null when it is none of the forms
        /// taken here: a hash, rgb(), rgba() or one of the names CSS gives.
        /// </summary>
        public static Color? Colour(string value)
        {
            string text = value.Trim().ToLowerInvariant();

            if (text.StartsWith('#'))
                return Hash(text);

            if (text.StartsWith("rgb(", StringComparison.Ordinal) || text.StartsWith("rgba(", StringComparison.Ordinal))
                return Functional(text);

            return Names.TryGetValue(text, out int packed)
                ? new Color((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed)
                : null;
        }

        private static Color? Hash(string text)
        {
            string hex = text[1..];
            foreach (char c in hex)
            {
                if (!char.IsAsciiHexDigit(c))
                    return null;
            }

            byte Short(int i) => (byte)(Convert.ToInt32(hex.Substring(i, 1), 16) * 17);
            byte Long(int i) => Convert.ToByte(hex.Substring(i, 2), 16);

            return hex.Length switch
            {
                3 => new Color(Short(0), Short(1), Short(2)),
                4 => new Color(Short(0), Short(1), Short(2), Short(3)),
                6 => new Color(Long(0), Long(2), Long(4)),
                8 => new Color(Long(0), Long(2), Long(4), Long(6)),
                _ => null
            };
        }

        /// <summary>rgb(1,2,3), rgb(1 2 3), rgb(1 2 3 / .5), rgba(1,2,3,.5), and percentages.</summary>
        private static Color? Functional(string text)
        {
            int open = text.IndexOf('(');
            if (!text.EndsWith(')'))
                return null;

            string[] parts = text[(open + 1)..^1]
                .Split(new[] { ' ', ',', '/', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is not (3 or 4))
                return null;

            var channels = new byte[3];
            for (int i = 0; i < 3; i++)
            {
                bool percent = parts[i].EndsWith('%');
                double? read = Number(percent ? parts[i][..^1] : parts[i]);
                if (read == null)
                    return null;

                channels[i] = (byte)Math.Round(Math.Clamp(percent ? read.Value * 2.55 : read.Value, 0, 255));
            }

            double? alpha = parts.Length == 4 ? Opacity(parts[3]) : 1;
            if (alpha == null)
                return null;

            return new Color(channels[0], channels[1], channels[2], (byte)Math.Round(alpha.Value * 255));
        }

        /// <summary>The CSS names, which generators do use: steelblue is the colour of a thousand charts.</summary>
        internal static readonly Dictionary<string, int> Names = new()
        {
            ["aliceblue"] = 0xF0F8FF, ["antiquewhite"] = 0xFAEBD7, ["aqua"] = 0x00FFFF, ["aquamarine"] = 0x7FFFD4,
            ["azure"] = 0xF0FFFF, ["beige"] = 0xF5F5DC, ["bisque"] = 0xFFE4C4, ["black"] = 0x000000,
            ["blanchedalmond"] = 0xFFEBCD, ["blue"] = 0x0000FF, ["blueviolet"] = 0x8A2BE2, ["brown"] = 0xA52A2A,
            ["burlywood"] = 0xDEB887, ["cadetblue"] = 0x5F9EA0, ["chartreuse"] = 0x7FFF00, ["chocolate"] = 0xD2691E,
            ["coral"] = 0xFF7F50, ["cornflowerblue"] = 0x6495ED, ["cornsilk"] = 0xFFF8DC, ["crimson"] = 0xDC143C,
            ["cyan"] = 0x00FFFF, ["darkblue"] = 0x00008B, ["darkcyan"] = 0x008B8B, ["darkgoldenrod"] = 0xB8860B,
            ["darkgray"] = 0xA9A9A9, ["darkgreen"] = 0x006400, ["darkgrey"] = 0xA9A9A9, ["darkkhaki"] = 0xBDB76B,
            ["darkmagenta"] = 0x8B008B, ["darkolivegreen"] = 0x556B2F, ["darkorange"] = 0xFF8C00, ["darkorchid"] = 0x9932CC,
            ["darkred"] = 0x8B0000, ["darksalmon"] = 0xE9967A, ["darkseagreen"] = 0x8FBC8F, ["darkslateblue"] = 0x483D8B,
            ["darkslategray"] = 0x2F4F4F, ["darkslategrey"] = 0x2F4F4F, ["darkturquoise"] = 0x00CED1, ["darkviolet"] = 0x9400D3,
            ["deeppink"] = 0xFF1493, ["deepskyblue"] = 0x00BFFF, ["dimgray"] = 0x696969, ["dimgrey"] = 0x696969,
            ["dodgerblue"] = 0x1E90FF, ["firebrick"] = 0xB22222, ["floralwhite"] = 0xFFFAF0, ["forestgreen"] = 0x228B22,
            ["fuchsia"] = 0xFF00FF, ["gainsboro"] = 0xDCDCDC, ["ghostwhite"] = 0xF8F8FF, ["gold"] = 0xFFD700,
            ["goldenrod"] = 0xDAA520, ["gray"] = 0x808080, ["green"] = 0x008000, ["greenyellow"] = 0xADFF2F,
            ["grey"] = 0x808080, ["honeydew"] = 0xF0FFF0, ["hotpink"] = 0xFF69B4, ["indianred"] = 0xCD5C5C,
            ["indigo"] = 0x4B0082, ["ivory"] = 0xFFFFF0, ["khaki"] = 0xF0E68C, ["lavender"] = 0xE6E6FA,
            ["lavenderblush"] = 0xFFF0F5, ["lawngreen"] = 0x7CFC00, ["lemonchiffon"] = 0xFFFACD, ["lightblue"] = 0xADD8E6,
            ["lightcoral"] = 0xF08080, ["lightcyan"] = 0xE0FFFF, ["lightgoldenrodyellow"] = 0xFAFAD2, ["lightgray"] = 0xD3D3D3,
            ["lightgreen"] = 0x90EE90, ["lightgrey"] = 0xD3D3D3, ["lightpink"] = 0xFFB6C1, ["lightsalmon"] = 0xFFA07A,
            ["lightseagreen"] = 0x20B2AA, ["lightskyblue"] = 0x87CEFA, ["lightslategray"] = 0x778899, ["lightslategrey"] = 0x778899,
            ["lightsteelblue"] = 0xB0C4DE, ["lightyellow"] = 0xFFFFE0, ["lime"] = 0x00FF00, ["limegreen"] = 0x32CD32,
            ["linen"] = 0xFAF0E6, ["magenta"] = 0xFF00FF, ["maroon"] = 0x800000, ["mediumaquamarine"] = 0x66CDAA,
            ["mediumblue"] = 0x0000CD, ["mediumorchid"] = 0xBA55D3, ["mediumpurple"] = 0x9370DB, ["mediumseagreen"] = 0x3CB371,
            ["mediumslateblue"] = 0x7B68EE, ["mediumspringgreen"] = 0x00FA9A, ["mediumturquoise"] = 0x48D1CC, ["mediumvioletred"] = 0xC71585,
            ["midnightblue"] = 0x191970, ["mintcream"] = 0xF5FFFA, ["mistyrose"] = 0xFFE4E1, ["moccasin"] = 0xFFE4B5,
            ["navajowhite"] = 0xFFDEAD, ["navy"] = 0x000080, ["oldlace"] = 0xFDF5E6, ["olive"] = 0x808000,
            ["olivedrab"] = 0x6B8E23, ["orange"] = 0xFFA500, ["orangered"] = 0xFF4500, ["orchid"] = 0xDA70D6,
            ["palegoldenrod"] = 0xEEE8AA, ["palegreen"] = 0x98FB98, ["paleturquoise"] = 0xAFEEEE, ["palevioletred"] = 0xDB7093,
            ["papayawhip"] = 0xFFEFD5, ["peachpuff"] = 0xFFDAB9, ["peru"] = 0xCD853F, ["pink"] = 0xFFC0CB,
            ["plum"] = 0xDDA0DD, ["powderblue"] = 0xB0E0E6, ["purple"] = 0x800080, ["rebeccapurple"] = 0x663399,
            ["red"] = 0xFF0000, ["rosybrown"] = 0xBC8F8F, ["royalblue"] = 0x4169E1, ["saddlebrown"] = 0x8B4513,
            ["salmon"] = 0xFA8072, ["sandybrown"] = 0xF4A460, ["seagreen"] = 0x2E8B57, ["seashell"] = 0xFFF5EE,
            ["sienna"] = 0xA0522D, ["silver"] = 0xC0C0C0, ["skyblue"] = 0x87CEEB, ["slateblue"] = 0x6A5ACD,
            ["slategray"] = 0x708090, ["slategrey"] = 0x708090, ["snow"] = 0xFFFAFA, ["springgreen"] = 0x00FF7F,
            ["steelblue"] = 0x4682B4, ["tan"] = 0xD2B48C, ["teal"] = 0x008080, ["thistle"] = 0xD8BFD8,
            ["tomato"] = 0xFF6347, ["turquoise"] = 0x40E0D0, ["violet"] = 0xEE82EE, ["wheat"] = 0xF5DEB3,
            ["white"] = 0xFFFFFF, ["whitesmoke"] = 0xF5F5F5, ["yellow"] = 0xFFFF00, ["yellowgreen"] = 0x9ACD32,
        };

        // ---- transforms ---------------------------------------------------------------------

        /// <summary>A transform list, the leftmost applied last. Null if it names or counts anything wrongly.</summary>
        public static VectorMatrix? Transform(string value)
        {
            VectorMatrix matrix = VectorMatrix.Identity;
            int at = 0;

            SkipSeparators(value, ref at);
            while (at < value.Length)
            {
                int open = value.IndexOf('(', at);
                int shut = open < 0 ? -1 : value.IndexOf(')', open);
                if (shut < 0)
                    return null;

                List<double>? args = Numbers(value[(open + 1)..shut]);
                if (args == null)
                    return null;

                VectorMatrix? step = TransformStep(value[at..open].Trim(), args);
                if (step == null)
                    return null;

                matrix = matrix.Times(step.Value);
                at = shut + 1;
                SkipSeparators(value, ref at);
            }

            return matrix;
        }

        private static VectorMatrix? TransformStep(string name, List<double> a)
        {
            switch (name)
            {
                case "matrix" when a.Count == 6:
                    return new VectorMatrix(a[0], a[1], a[2], a[3], a[4], a[5]);

                case "translate" when a.Count is 1 or 2:
                    return new VectorMatrix(1, 0, 0, 1, a[0], a.Count == 2 ? a[1] : 0);

                case "scale" when a.Count is 1 or 2:
                    return new VectorMatrix(a[0], 0, 0, a.Count == 2 ? a[1] : a[0], 0, 0);

                case "rotate" when a.Count is 1 or 3:
                {
                    double radians = a[0] * Math.PI / 180;
                    var turn = new VectorMatrix(Math.Cos(radians), Math.Sin(radians),
                                                -Math.Sin(radians), Math.Cos(radians), 0, 0);
                    return a.Count == 1
                        ? turn
                        : new VectorMatrix(1, 0, 0, 1, a[1], a[2]).Times(turn).Times(new VectorMatrix(1, 0, 0, 1, -a[1], -a[2]));
                }

                case "skewX" when a.Count == 1:
                    return new VectorMatrix(1, 0, Math.Tan(a[0] * Math.PI / 180), 1, 0, 0);

                case "skewY" when a.Count == 1:
                    return new VectorMatrix(1, Math.Tan(a[0] * Math.PI / 180), 0, 1, 0, 0);

                default:
                    return null;
            }
        }

        // ---- path data ----------------------------------------------------------------------

        /// <summary>
        /// Path data brought down to moves, lines, cubics and closes. Null at the first thing
        /// wrong: SVG itself draws up to an error and stops, which here would be half a curve
        /// on the page and nobody told.
        /// </summary>
        public static VectorPath? Path(string d)
        {
            var path = new VectorPath();
            int at = 0;
            double x = 0, y = 0, startX = 0, startY = 0;
            double lastControlX = 0, lastControlY = 0;
            char command = '\0', previous = '\0';

            SkipSeparators(d, ref at);
            while (at < d.Length)
            {
                if (char.IsAsciiLetter(d[at]))
                {
                    command = d[at++];
                }
                else if (command == '\0' || command is 'Z' or 'z')
                {
                    return null;                                  // numbers with nothing to belong to
                }

                if (path.IsEmpty && command is not ('M' or 'm'))
                    return null;

                bool relative = char.IsLower(command);
                double ox = relative ? x : 0, oy = relative ? y : 0;

                bool Read(out double value)
                {
                    SkipSeparators(d, ref at);
                    double? read = ReadNumber(d, ref at);
                    value = read ?? 0;
                    return read != null;
                }

                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                        if (!Read(out double mx) || !Read(out double my))
                            return null;

                        x = startX = ox + mx;
                        y = startY = oy + my;
                        path.MoveTo(x, y);
                        // Further pairs after a move are lines, of the same relativity.
                        command = relative ? 'l' : 'L';
                        previous = 'M';
                        SkipSeparators(d, ref at);
                        continue;

                    case 'L':
                        if (!Read(out double lx) || !Read(out double ly))
                            return null;

                        x = ox + lx;
                        y = oy + ly;
                        path.LineTo(x, y);
                        break;

                    case 'H':
                        if (!Read(out double hx))
                            return null;

                        x = ox + hx;
                        path.LineTo(x, y);
                        break;

                    case 'V':
                        if (!Read(out double vy))
                            return null;

                        y = oy + vy;
                        path.LineTo(x, y);
                        break;

                    case 'C':
                    {
                        if (!Read(out double x1) || !Read(out double y1) || !Read(out double x2)
                            || !Read(out double y2) || !Read(out double cx) || !Read(out double cy))
                            return null;

                        lastControlX = ox + x2;
                        lastControlY = oy + y2;
                        path.CubicTo(ox + x1, oy + y1, lastControlX, lastControlY, ox + cx, oy + cy);
                        x = ox + cx;
                        y = oy + cy;
                        break;
                    }

                    case 'S':
                    {
                        if (!Read(out double x2) || !Read(out double y2) || !Read(out double cx) || !Read(out double cy))
                            return null;

                        // The first control mirrors the last one, if the command before was a cubic.
                        bool afterCubic = previous is 'C' or 'S';
                        double x1 = afterCubic ? 2 * x - lastControlX : x;
                        double y1 = afterCubic ? 2 * y - lastControlY : y;
                        lastControlX = ox + x2;
                        lastControlY = oy + y2;
                        path.CubicTo(x1, y1, lastControlX, lastControlY, ox + cx, oy + cy);
                        x = ox + cx;
                        y = oy + cy;
                        break;
                    }

                    case 'Q':
                    {
                        if (!Read(out double qx) || !Read(out double qy) || !Read(out double cx) || !Read(out double cy))
                            return null;

                        lastControlX = ox + qx;
                        lastControlY = oy + qy;
                        Quadratic(path, x, y, lastControlX, lastControlY, ox + cx, oy + cy);
                        x = ox + cx;
                        y = oy + cy;
                        break;
                    }

                    case 'T':
                    {
                        if (!Read(out double cx) || !Read(out double cy))
                            return null;

                        bool afterQuadratic = previous is 'Q' or 'T';
                        lastControlX = afterQuadratic ? 2 * x - lastControlX : x;
                        lastControlY = afterQuadratic ? 2 * y - lastControlY : y;
                        Quadratic(path, x, y, lastControlX, lastControlY, ox + cx, oy + cy);
                        x = ox + cx;
                        y = oy + cy;
                        break;
                    }

                    case 'A':
                    {
                        if (!Read(out double rx) || !Read(out double ry) || !Read(out double turn))
                            return null;

                        if (!ReadFlag(d, ref at, out bool large) || !ReadFlag(d, ref at, out bool sweep))
                            return null;

                        if (!Read(out double ax) || !Read(out double ay))
                            return null;

                        Arc(path, x, y, rx, ry, turn, large, sweep, ox + ax, oy + ay);
                        x = ox + ax;
                        y = oy + ay;
                        break;
                    }

                    case 'Z':
                        path.Close();
                        x = startX;
                        y = startY;
                        break;

                    default:
                        return null;
                }

                previous = char.ToUpperInvariant(command);
                SkipSeparators(d, ref at);
            }

            return path;
        }

        /// <summary>An arc's flags are one digit each, and may touch what follows: "a1 1 0 00.5.5".</summary>
        private static bool ReadFlag(string d, ref int at, out bool flag)
        {
            SkipSeparators(d, ref at);
            flag = at < d.Length && d[at] == '1';
            if (at >= d.Length || d[at] is not ('0' or '1'))
                return false;

            at++;
            return true;
        }

        private static void Quadratic(VectorPath path, double x0, double y0, double qx, double qy, double x, double y)
            => path.CubicTo(x0 + 2.0 / 3 * (qx - x0), y0 + 2.0 / 3 * (qy - y0),
                            x + 2.0 / 3 * (qx - x), y + 2.0 / 3 * (qy - y), x, y);

        /// <summary>
        /// An elliptical arc as cubics of a quarter turn at most, by the conversion the SVG
        /// specification gives in its implementation notes.
        /// </summary>
        private static void Arc(VectorPath path, double x1, double y1, double rx, double ry, double turn,
                                bool large, bool sweep, double x2, double y2)
        {
            if (x1 == x2 && y1 == y2)
                return;

            rx = Math.Abs(rx);
            ry = Math.Abs(ry);
            if (rx == 0 || ry == 0)
            {
                path.LineTo(x2, y2);
                return;
            }

            double phi = turn * Math.PI / 180;
            double cos = Math.Cos(phi), sin = Math.Sin(phi);

            double dx = (x1 - x2) / 2, dy = (y1 - y2) / 2;
            double xp = cos * dx + sin * dy;
            double yp = -sin * dx + cos * dy;

            // Radii too small to reach are scaled up until they just do, as SVG says.
            double lambda = xp * xp / (rx * rx) + yp * yp / (ry * ry);
            if (lambda > 1)
            {
                rx *= Math.Sqrt(lambda);
                ry *= Math.Sqrt(lambda);
            }

            double top = rx * rx * ry * ry - rx * rx * yp * yp - ry * ry * xp * xp;
            double bottom = rx * rx * yp * yp + ry * ry * xp * xp;
            double factor = (large == sweep ? -1 : 1) * Math.Sqrt(Math.Max(0, top / bottom));
            double cxp = factor * rx * yp / ry;
            double cyp = -factor * ry * xp / rx;

            double cx = cos * cxp - sin * cyp + (x1 + x2) / 2;
            double cy = sin * cxp + cos * cyp + (y1 + y2) / 2;

            double from = Math.Atan2((yp - cyp) / ry, (xp - cxp) / rx);
            double to = Math.Atan2((-yp - cyp) / ry, (-xp - cxp) / rx);
            double span = to - from;
            if (!sweep && span > 0)
                span -= 2 * Math.PI;
            else if (sweep && span < 0)
                span += 2 * Math.PI;

            int pieces = Math.Max(1, (int)Math.Ceiling(Math.Abs(span) / (Math.PI / 2) - 1e-9));
            double step = span / pieces;
            double k = 4.0 / 3 * Math.Tan(step / 4);

            (double X, double Y) Out(double px, double py)
                => (cx + rx * cos * px - ry * sin * py, cy + rx * sin * px + ry * cos * py);

            for (int i = 0; i < pieces; i++)
            {
                double a = from + i * step, b = a + step;
                (double c1x, double c1y) = Out(Math.Cos(a) - k * Math.Sin(a), Math.Sin(a) + k * Math.Cos(a));
                (double c2x, double c2y) = Out(Math.Cos(b) + k * Math.Sin(b), Math.Sin(b) - k * Math.Cos(b));
                (double ex, double ey) = i == pieces - 1 ? (x2, y2) : Out(Math.Cos(b), Math.Sin(b));
                path.CubicTo(c1x, c1y, c2x, c2y, ex, ey);
            }
        }
    }
}
