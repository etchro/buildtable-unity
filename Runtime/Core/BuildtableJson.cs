using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BuildtableSdk
{
    /// <summary>
    /// Minimal JSON writer. The SDK controls every payload it sends, so a
    /// hand-rolled writer is enough and avoids a dependency — JsonUtility can't
    /// express the arbitrary nested objects the API accepts for deviceInfo, and
    /// Newtonsoft isn't present in a stock project.
    ///
    /// Responses are read with UnityEngine.JsonUtility against small
    /// [Serializable] envelope types instead; their shapes are fixed.
    /// </summary>
    public sealed class BuildtableJson
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly Stack<bool> _needsComma = new Stack<bool>();
        private readonly Stack<char> _closers = new Stack<char>();

        public static BuildtableJson Object()
        {
            var json = new BuildtableJson();
            json.BeginObject();
            return json;
        }

        public BuildtableJson BeginObject()
        {
            Separate();
            Open('{', '}');
            return this;
        }

        public BuildtableJson BeginObject(string key)
        {
            WriteKey(key);
            Open('{', '}');
            return this;
        }

        public BuildtableJson EndObject() => Close('}');

        public BuildtableJson BeginArray(string key)
        {
            WriteKey(key);
            Open('[', ']');
            return this;
        }

        public BuildtableJson EndArray() => Close(']');

        /// <summary>Writes a string element inside an array.</summary>
        public BuildtableJson Element(string value)
        {
            Separate();
            AppendString(value);
            MarkWritten();
            return this;
        }

        /// <summary>
        /// Splices an already-serialized fragment in as an array element. Used to
        /// assemble an events request from event objects that were serialized
        /// when they happened, not when they're sent. The caller owns validity.
        /// </summary>
        public BuildtableJson RawElement(string json)
        {
            if (string.IsNullOrEmpty(json)) return this;
            Separate();
            _sb.Append(json);
            MarkWritten();
            return this;
        }

        /// <summary>Writes a string field, skipping it entirely when null or empty.</summary>
        public BuildtableJson StringIfSet(string key, string value)
        {
            if (string.IsNullOrEmpty(value)) return this;
            return String(key, value);
        }

        public BuildtableJson String(string key, string value)
        {
            WriteKey(key);
            if (value == null) _sb.Append("null");
            else AppendString(value);
            MarkWritten();
            return this;
        }

        public BuildtableJson Number(string key, long value)
        {
            WriteKey(key);
            _sb.Append(value.ToString(CultureInfo.InvariantCulture));
            MarkWritten();
            return this;
        }

        public BuildtableJson Number(string key, float value)
        {
            WriteKey(key);
            // "R" keeps the value round-trippable; invariant culture keeps a
            // comma-decimal locale from producing invalid JSON.
            _sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
            MarkWritten();
            return this;
        }

        /// <summary>
        /// Splices an already-serialized JSON fragment in as a value. Used for
        /// deviceInfo, which is captured when the report is written and stored
        /// verbatim in the offline queue. The caller owns its validity.
        /// </summary>
        public BuildtableJson Raw(string key, string json)
        {
            if (string.IsNullOrEmpty(json)) return this;
            WriteKey(key);
            _sb.Append(json);
            MarkWritten();
            return this;
        }

        public BuildtableJson Bool(string key, bool value)
        {
            WriteKey(key);
            _sb.Append(value ? "true" : "false");
            MarkWritten();
            return this;
        }

        /// <summary>Closes any still-open containers and returns the document.</summary>
        public string Build()
        {
            while (_closers.Count > 0) Close(_closers.Peek());
            return _sb.ToString();
        }

        public override string ToString() => _sb.ToString();

        private void Open(char open, char close)
        {
            _sb.Append(open);
            _closers.Push(close);
            _needsComma.Push(false);
        }

        private BuildtableJson Close(char close)
        {
            _sb.Append(close);
            _closers.Pop();
            _needsComma.Pop();
            MarkWritten();
            return this;
        }

        private void WriteKey(string key)
        {
            Separate();
            AppendString(key);
            _sb.Append(':');
        }

        private void Separate()
        {
            if (_needsComma.Count > 0 && _needsComma.Peek()) _sb.Append(',');
        }

        private void MarkWritten()
        {
            if (_needsComma.Count == 0) return;
            _needsComma.Pop();
            _needsComma.Push(true);
        }

        private void AppendString(string value)
        {
            _sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\b': _sb.Append("\\b"); break;
                    case '\f': _sb.Append("\\f"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        // Control characters and lone surrogates would make the
                        // payload invalid JSON; escape everything below 0x20.
                        if (c < ' ') _sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else _sb.Append(c);
                        break;
                }
            }
            _sb.Append('"');
        }
    }
}
