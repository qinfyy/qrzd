using System.Collections;
using System.Globalization;
using System.Text;

namespace Sv.Resources;

public sealed class ResourceCondition
{
    private readonly int _kind;
    private readonly object? _expected;
    private readonly double _lower;
    private readonly double _upper;
    private readonly bool _lowerInclusive;
    private readonly bool _upperInclusive;

    public ResourceCondition(int kind, string expression)
    {
        _kind = kind;
        Expression = expression.Trim();
        if (IsInterval(kind))
        {
            if (Expression.Length < 3 || Expression[0] is not ('[' or '(') || Expression[^1] is not (']' or ')'))
                throw new FormatException($"Invalid interval: {expression}");
            string[] limits = Expression[1..^1].Split(',');
            if (limits.Length != 2) throw new FormatException($"Invalid interval: {expression}");
            _lower = string.IsNullOrWhiteSpace(limits[0]) ? double.NegativeInfinity : double.Parse(limits[0], CultureInfo.InvariantCulture);
            _upper = string.IsNullOrWhiteSpace(limits[1]) ? double.PositiveInfinity : double.Parse(limits[1], CultureInfo.InvariantCulture);
            _lowerInclusive = Expression[0] == '[';
            _upperInclusive = Expression[^1] == ']';
        }
        else
        {
            _expected = new LiteralReader(Expression).Read();
        }
    }

    public string Expression { get; }
    public int[] Integers => _expected is List<object?> values ? values.Select(value => Convert.ToInt32(value, CultureInfo.InvariantCulture)).ToArray() : [];

    public bool Matches(object? actual)
    {
        if (actual is null) return false;
        if (IsInterval(_kind))
        {
            double value = Convert.ToDouble(actual, CultureInfo.InvariantCulture);
            return (value > _lower || _lowerInclusive && value == _lower) && (value < _upper || _upperInclusive && value == _upper);
        }
        string[] expected = Values(_expected);
        string[] values = Values(actual);
        return _kind switch
        {
            1 or 9 or 10 or 24 or 34 or 35 or 36 or 42 or 43 or 102 => expected.All(values.Contains),
            11 or 25 or 44 => !expected.Any(values.Contains),
            17 or 29 or 41 or 48 => expected.Any(values.Contains),
            3 or 100 or 52 => expected.Contains(Key(actual)),
            101 => Key(_expected) == Key(actual),
            12 or 13 or 14 or 15 or 33 or 38 or 39 or 40 => Number(actual) >= Number(_expected),
            5 or 6 or 19 or 23 or 31 or 37 => MatchMap(actual, (value, limit) => Range(value, limit)),
            20 or 22 or 32 => MatchMap(actual, (value, limit) => Number(value) >= Number(limit)),
            8 => MatchMap(actual, (value, limit) => Values(limit).Contains(Key(value)), any: true),
            47 => MatchMap(actual, (value, limit) => Values(limit).Contains(Key(value))),
            18 => MatchMap(actual, (value, limit) => Values(limit).Intersect(Values(value)).Any(), any: true),
            _ => false,
        };
    }

    private bool MatchMap(object actual, Func<object?, object?, bool> check, bool any = false)
    {
        if (_expected is not Dictionary<string, object?> expected || actual is not IDictionary values) return false;
        bool Match(KeyValuePair<string, object?> entry)
        {
            object key = entry.Key;
            if (!values.Contains(key) && int.TryParse(entry.Key, out int integer)) key = integer;
            return values.Contains(key) && check(values[key], entry.Value);
        }
        return any ? expected.Any(Match) : expected.All(Match);
    }

    private static bool Range(object? value, object? limit)
    {
        if (limit is not List<object?> range || range.Count != 2) return false;
        return Number(value) >= Number(range[0]) && Number(value) <= Number(range[1]);
    }

    private static bool IsInterval(int kind) => kind is 4 or 7 or 16 or 21 or 26 or 27 or 28 or 30 or 45 or 46 or 49 or 50 or 51 or 53 or 54;
    private static double Number(object? value) => Convert.ToDouble(value, CultureInfo.InvariantCulture);
    private static string Key(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    private static string[] Values(object? value) => value is IEnumerable list and not string ? list.Cast<object?>().Select(Key).ToArray() : [Key(value)];

    // Resource conditions contain Python literals, not executable Python. Reject everything outside this small grammar.
    private sealed class LiteralReader(string source)
    {
        private int _position;

        public object? Read()
        {
            if (source.Length > 8192) throw new FormatException("Condition literal is too long");
            object? value = Value(0);
            Space();
            if (_position != source.Length) throw new FormatException($"Unexpected condition token at {_position}");
            return value;
        }

        private object? Value(int depth)
        {
            Space();
            if (depth > 16 || _position >= source.Length) throw new FormatException("Incomplete condition literal");
            char c = source[_position];
            if (c is '\'' or '"') return Text();
            if (c is '[' or '(') return Sequence(c == '[' ? ']' : ')', depth + 1);
            if (c == '{')
            {
                _position++;
                Dictionary<string, object?> map = [];
                while (!Take('}'))
                {
                    string key = Key(Value(depth + 1));
                    Require(':');
                    map.Add(key, Value(depth + 1));
                    if (Take('}')) break;
                    Require(',');
                }
                return map;
            }
            if (source.AsSpan(_position).StartsWith("set(", StringComparison.Ordinal))
            {
                _position += 4;
                if (Take(')')) return new List<object?>();
                object? values = Value(depth + 1);
                Require(')');
                return values is List<object?> ? values : throw new FormatException("set requires a literal sequence");
            }
            int start = _position;
            while (_position < source.Length && (char.IsAsciiDigit(source[_position]) || source[_position] is '-' or '+' or '.')) _position++;
            if (start == _position) throw new FormatException($"Unsupported condition token at {_position}");
            return double.Parse(source[start.._position], CultureInfo.InvariantCulture);
        }

        private List<object?> Sequence(char end, int depth)
        {
            _position++;
            List<object?> values = [];
            while (!Take(end))
            {
                values.Add(Value(depth));
                if (Take(end)) break;
                Require(',');
            }
            return values;
        }

        private string Text()
        {
            char quote = source[_position++];
            StringBuilder value = new();
            while (_position < source.Length)
            {
                char c = source[_position++];
                if (c == quote) return value.ToString();
                if (c == '\\')
                {
                    if (_position == source.Length) break;
                    c = source[_position++];
                    if (c is not ('\\' or '\'' or '"')) throw new FormatException("Unsupported escape in condition");
                }
                value.Append(c);
            }
            throw new FormatException("Unterminated condition string");
        }

        private void Space()
        {
            while (_position < source.Length && char.IsWhiteSpace(source[_position])) _position++;
        }

        private bool Take(char token)
        {
            Space();
            if (_position >= source.Length || source[_position] != token) return false;
            _position++;
            return true;
        }

        private void Require(char token)
        {
            if (!Take(token)) throw new FormatException($"Expected {token} at {_position}");
        }
    }
}
