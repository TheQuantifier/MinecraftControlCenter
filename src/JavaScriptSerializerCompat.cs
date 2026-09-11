using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;

namespace System.Web.Script.Serialization
{
    internal sealed class JavaScriptSerializer
    {
        internal string Serialize(object value)
        {
            return JsonSerializer.Serialize(value);
        }

        internal T Deserialize<T>(string json)
        {
            object converted = ConvertElement(JsonDocument.Parse(json).RootElement);
            if (converted is T typed) return typed;
            return JsonSerializer.Deserialize<T>(json);
        }

        internal object DeserializeObject(string json)
        {
            return ConvertElement(JsonDocument.Parse(json).RootElement);
        }

        private static object ConvertElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (JsonProperty property in element.EnumerateObject()) values[property.Name] = ConvertElement(property.Value);
                    return values;
                case JsonValueKind.Array:
                    List<object> items = new List<object>();
                    foreach (JsonElement item in element.EnumerateArray()) items.Add(ConvertElement(item));
                    return items.ToArray();
                case JsonValueKind.String: return element.GetString();
                case JsonValueKind.Number:
                    if (element.TryGetInt32(out int integer)) return integer;
                    if (element.TryGetInt64(out long longInteger)) return longInteger;
                    return element.GetDouble();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                default: return null;
            }
        }
    }
}
