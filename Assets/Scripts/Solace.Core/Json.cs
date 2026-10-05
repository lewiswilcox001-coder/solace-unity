// Solace.Core — minimal hand-rolled JSON DOM, writer, and parser.
// Supports objects, arrays, strings, numbers, booleans, null.
// No external library. Number literals are preserved verbatim so that
// Save → Load → Save produces byte-identical JSON (round-trip safe).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Solace.Core
{
    public abstract class JsonValue
    {
        public abstract void WriteTo(StringBuilder sb);

        public string ToJson()
        {
            var sb = new StringBuilder();
            WriteTo(sb);
            return sb.ToString();
        }

        public static JsonValue Parse(string text)
        {
            var parser = new JsonParser(text);
            JsonValue v = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd)
                throw new JsonParseException("Trailing characters after JSON value.");
            return v;
        }

        public virtual JsonObject AsObject() { throw new JsonParseException("Expected JSON object."); }
        public virtual JsonArray AsArray() { throw new JsonParseException("Expected JSON array."); }
        public virtual string AsString() { throw new JsonParseException("Expected JSON string."); }
        public virtual bool AsBool() { throw new JsonParseException("Expected JSON boolean."); }
        public virtual bool IsNull { get { return false; } }
    }

    public sealed class JsonObject : JsonValue
    {
        private readonly List<KeyValuePair<string, JsonValue>> _members =
            new List<KeyValuePair<string, JsonValue>>();

        public void Add(string key, JsonValue value)
        {
            _members.Add(new KeyValuePair<string, JsonValue>(key, value ?? JsonNull.Instance));
        }

        public void Add(string key, string value) { Add(key, (JsonValue)new JsonString(value)); }
        public void Add(string key, int value) { Add(key, JsonNumber.From(value)); }
        public void Add(string key, long value) { Add(key, JsonNumber.From(value)); }
        public void Add(string key, float value) { Add(key, JsonNumber.From(value)); }
        public void Add(string key, double value) { Add(key, JsonNumber.From(value)); }
        public void Add(string key, bool value) { Add(key, (JsonValue)new JsonBool(value)); }

        public int Count { get { return _members.Count; } }

        public IEnumerable<KeyValuePair<string, JsonValue>> Members { get { return _members; } }

        public bool TryGet(string key, out JsonValue value)
        {
            for (int i = 0; i < _members.Count; i++)
            {
                if (_members[i].Key == key)
                {
                    value = _members[i].Value;
                    return true;
                }
            }
            value = null;
            return false;
        }

        public JsonValue this[string key]
        {
            get
            {
                JsonValue v;
                if (TryGet(key, out v)) return v;
                throw new JsonParseException("Missing JSON key: '" + key + "'.");
            }
        }

        public override JsonObject AsObject() { return this; }

        public override void WriteTo(StringBuilder sb)
        {
            sb.Append('{');
            for (int i = 0; i < _members.Count; i++)
            {
                if (i > 0) sb.Append(',');
                JsonString.WriteEscaped(sb, _members[i].Key);
                sb.Append(':');
                _members[i].Value.WriteTo(sb);
            }
            sb.Append('}');
        }
    }

    public sealed class JsonArray : JsonValue
    {
        private readonly List<JsonValue> _items = new List<JsonValue>();

        public void Add(JsonValue value) { _items.Add(value ?? JsonNull.Instance); }
        public void Add(string value) { Add((JsonValue)new JsonString(value)); }
        public void Add(int value) { Add(JsonNumber.From(value)); }
        public void Add(float value) { Add(JsonNumber.From(value)); }
        public void Add(bool value) { Add((JsonValue)new JsonBool(value)); }

        public int Count { get { return _items.Count; } }
        public JsonValue this[int index] { get { return _items[index]; } }
        public IEnumerable<JsonValue> Items { get { return _items; } }

        public override JsonArray AsArray() { return this; }

        public override void WriteTo(StringBuilder sb)
        {
            sb.Append('[');
            for (int i = 0; i < _items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                _items[i].WriteTo(sb);
            }
            sb.Append(']');
        }
    }

    public sealed class JsonString : JsonValue
    {
        public readonly string Value;
        public JsonString(string value) { Value = value ?? ""; }

        public override string AsString() { return Value; }

        public override void WriteTo(StringBuilder sb) { WriteEscaped(sb, Value); }

        public static void WriteEscaped(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4"));
                        }
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    /// <summary>
    /// A JSON number. The original literal text is preserved so re-serializing
    /// after a parse yields the identical string (round-trip guarantee).
    /// </summary>
    public sealed class JsonNumber : JsonValue
    {
        private readonly string _literal;

        public JsonNumber(string literal)
        {
            if (string.IsNullOrEmpty(literal))
                throw new JsonParseException("Empty number literal.");
            _literal = literal;
        }

        public static JsonNumber From(int v) { return new JsonNumber(v.ToString(CultureInfo.InvariantCulture)); }
        public static JsonNumber From(long v) { return new JsonNumber(v.ToString(CultureInfo.InvariantCulture)); }
        public static JsonNumber From(uint v) { return new JsonNumber(v.ToString(CultureInfo.InvariantCulture)); }

        public static JsonNumber From(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v))
                throw new JsonParseException("Cannot serialize NaN/Infinity.");
            return new JsonNumber(v.ToString("R", CultureInfo.InvariantCulture));
        }

        public static JsonNumber From(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
                throw new JsonParseException("Cannot serialize NaN/Infinity.");
            return new JsonNumber(v.ToString("R", CultureInfo.InvariantCulture));
        }

        public string Literal { get { return _literal; } }

        public int AsInt() { return int.Parse(_literal, CultureInfo.InvariantCulture); }
        public long AsLong() { return long.Parse(_literal, CultureInfo.InvariantCulture); }
        public uint AsUInt() { return uint.Parse(_literal, CultureInfo.InvariantCulture); }
        public float AsFloat() { return float.Parse(_literal, CultureInfo.InvariantCulture); }
        public double AsDouble() { return double.Parse(_literal, CultureInfo.InvariantCulture); }

        public bool IsIntegral
        {
            get
            {
                return _literal.IndexOf('.') < 0 && _literal.IndexOf('e') < 0 && _literal.IndexOf('E') < 0;
            }
        }

        public override void WriteTo(StringBuilder sb) { sb.Append(_literal); }
    }

    public sealed class JsonBool : JsonValue
    {
        public readonly bool Value;
        public JsonBool(bool value) { Value = value; }
        public override bool AsBool() { return Value; }
        public override void WriteTo(StringBuilder sb) { sb.Append(Value ? "true" : "false"); }
    }

    public sealed class JsonNull : JsonValue
    {
        public static readonly JsonNull Instance = new JsonNull();
        private JsonNull() { }
        public override bool IsNull { get { return true; } }
        public override void WriteTo(StringBuilder sb) { sb.Append("null"); }
    }

    public sealed class JsonParseException : Exception
    {
        public JsonParseException(string message) : base(message) { }
    }

    internal sealed class JsonParser
    {
        private readonly string _text;
        private int _pos;

        public JsonParser(string text) { _text = text ?? ""; _pos = 0; }

        public bool AtEnd { get { return _pos >= _text.Length; } }

        public void SkipWhitespace()
        {
            while (_pos < _text.Length)
            {
                char c = _text[_pos];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _pos++;
                else break;
            }
        }

        public JsonValue ParseValue()
        {
            SkipWhitespace();
            if (AtEnd) throw new JsonParseException("Unexpected end of JSON.");
            char c = _text[_pos];
            if (c == '{') return ParseObject();
            if (c == '[') return ParseArray();
            if (c == '"') return new JsonString(ParseString());
            if (c == 't') { Expect("true"); return new JsonBool(true); }
            if (c == 'f') { Expect("false"); return new JsonBool(false); }
            if (c == 'n') { Expect("null"); return JsonNull.Instance; }
            if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
            throw new JsonParseException("Unexpected character '" + c + "' at position " + _pos + ".");
        }

        private JsonObject ParseObject()
        {
            var obj = new JsonObject();
            _pos++; // {
            SkipWhitespace();
            if (!AtEnd && _text[_pos] == '}') { _pos++; return obj; }
            while (true)
            {
                SkipWhitespace();
                if (AtEnd || _text[_pos] != '"')
                    throw new JsonParseException("Expected string key at position " + _pos + ".");
                string key = ParseString();
                SkipWhitespace();
                if (AtEnd || _text[_pos] != ':')
                    throw new JsonParseException("Expected ':' at position " + _pos + ".");
                _pos++;
                obj.Add(key, ParseValue());
                SkipWhitespace();
                if (AtEnd) throw new JsonParseException("Unterminated object.");
                char c = _text[_pos];
                if (c == ',') { _pos++; continue; }
                if (c == '}') { _pos++; break; }
                throw new JsonParseException("Expected ',' or '}' at position " + _pos + ".");
            }
            return obj;
        }

        private JsonArray ParseArray()
        {
            var arr = new JsonArray();
            _pos++; // [
            SkipWhitespace();
            if (!AtEnd && _text[_pos] == ']') { _pos++; return arr; }
            while (true)
            {
                arr.Add(ParseValue());
                SkipWhitespace();
                if (AtEnd) throw new JsonParseException("Unterminated array.");
                char c = _text[_pos];
                if (c == ',') { _pos++; continue; }
                if (c == ']') { _pos++; break; }
                throw new JsonParseException("Expected ',' or ']' at position " + _pos + ".");
            }
            return arr;
        }

        private string ParseString()
        {
            _pos++; // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (_pos >= _text.Length) throw new JsonParseException("Unterminated string.");
                char c = _text[_pos++];
                if (c == '"') break;
                if (c == '\\')
                {
                    if (_pos >= _text.Length) throw new JsonParseException("Unterminated escape.");
                    char e = _text[_pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (_pos + 4 > _text.Length)
                                throw new JsonParseException("Bad \\u escape.");
                            string hex = _text.Substring(_pos, 4);
                            _pos += 4;
                            int code;
                            try { code = Convert.ToInt32(hex, 16); }
                            catch { throw new JsonParseException("Bad \\u escape: " + hex); }
                            sb.Append((char)code);
                            break;
                        default:
                            throw new JsonParseException("Bad escape '\\" + e + "'.");
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private JsonNumber ParseNumber()
        {
            int start = _pos;
            if (!AtEnd && _text[_pos] == '-') _pos++;
            while (!AtEnd && _text[_pos] >= '0' && _text[_pos] <= '9') _pos++;
            if (!AtEnd && _text[_pos] == '.')
            {
                _pos++;
                while (!AtEnd && _text[_pos] >= '0' && _text[_pos] <= '9') _pos++;
            }
            if (!AtEnd && (_text[_pos] == 'e' || _text[_pos] == 'E'))
            {
                _pos++;
                if (!AtEnd && (_text[_pos] == '+' || _text[_pos] == '-')) _pos++;
                while (!AtEnd && _text[_pos] >= '0' && _text[_pos] <= '9') _pos++;
            }
            string lit = _text.Substring(start, _pos - start);
            if (lit.Length == 0 || lit == "-")
                throw new JsonParseException("Bad number at position " + start + ".");
            return new JsonNumber(lit);
        }

        private void Expect(string word)
        {
            if (_pos + word.Length > _text.Length || _text.Substring(_pos, word.Length) != word)
                throw new JsonParseException("Expected '" + word + "' at position " + _pos + ".");
            _pos += word.Length;
        }
    }

    /// <summary>Small helpers for reading typed values with defaults.</summary>
    public static class JsonHelpers
    {
        public static string GetString(JsonObject o, string key, string def)
        {
            JsonValue v;
            if (o.TryGet(key, out v) && !v.IsNull) return v.AsString();
            return def;
        }

        public static int GetInt(JsonObject o, string key, int def)
        {
            JsonValue v;
            if (o.TryGet(key, out v) && !v.IsNull) return ((JsonNumber)v).AsInt();
            return def;
        }

        public static uint GetUInt(JsonObject o, string key, uint def)
        {
            JsonValue v;
            if (o.TryGet(key, out v) && !v.IsNull) return ((JsonNumber)v).AsUInt();
            return def;
        }

        public static float GetFloat(JsonObject o, string key, float def)
        {
            JsonValue v;
            if (o.TryGet(key, out v) && !v.IsNull) return ((JsonNumber)v).AsFloat();
            return def;
        }

        public static bool GetBool(JsonObject o, string key, bool def)
        {
            JsonValue v;
            if (o.TryGet(key, out v) && !v.IsNull) return v.AsBool();
            return def;
        }
    }
}
