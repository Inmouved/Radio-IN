// Small adapter for the built-in .NET Framework JSON parser. No JSON runtime is bundled.
namespace System.Text.Json
{
    public sealed class JsonDocument : System.IDisposable
    {
        public JsonElement RootElement { get; private set; }
        public static JsonDocument Parse(string text) { return new JsonDocument { RootElement = new JsonElement(new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 8000000 }.DeserializeObject(text)) }; }
        public void Dispose() { }
    }
    public struct JsonElement
    {
        readonly object value;
        public JsonElement(object value) { this.value = value; }
        public JsonElement GetProperty(string key) { return new JsonElement(((System.Collections.Generic.Dictionary<string, object>)value)[key]); }
        public bool TryGetProperty(string key, out JsonElement result)
        {
            object item;
            var dict = value as System.Collections.Generic.Dictionary<string, object>;
            if (dict != null && dict.TryGetValue(key, out item)) { result = new JsonElement(item); return true; }
            result = default(JsonElement); return false;
        }
        public System.Collections.Generic.IEnumerable<JsonElement> EnumerateArray() { foreach (object item in (object[])value) yield return new JsonElement(item); }
        public bool GetBoolean() { return System.Convert.ToBoolean(value); }
        public int GetInt32() { return System.Convert.ToInt32(value); }
        public long GetInt64() { return System.Convert.ToInt64(value); }
        public override string ToString() { return value == null ? "" : value.ToString(); }
    }
}
