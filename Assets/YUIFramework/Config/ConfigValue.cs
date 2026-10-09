using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace YUIFramework.Configuration
{
    public static class ConfigKey
    {
        public static string Compose(params object[] values)
        {
            var builder = new StringBuilder();
            foreach (object value in values)
            {
                string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                builder.Append(text.Length).Append(':').Append(text);
            }
            return builder.ToString();
        }

        public static string Display(params object[] values) =>
            string.Join(" -> ", Array.ConvertAll(values, value =>
                Convert.ToString(value, CultureInfo.InvariantCulture)));
    }

    public static class ConfigValue
    {
        public static JObject RequireObject(JToken token, string context)
        {
            if (token is JObject obj) return obj;
            throw new ConfigDataException($"配置 {context} 根节点必须是对象，实际为 {token?.Type.ToString() ?? "null"}");
        }

        public static int ReadInt32(JObject obj, string name, string context) =>
            ReadInt32(Required(obj, name, context), $"{context}.{name}");

        public static long ReadInt64(JObject obj, string name, string context) =>
            ReadInt64(Required(obj, name, context), $"{context}.{name}");

        public static float ReadSingle(JObject obj, string name, string context) =>
            ReadSingle(Required(obj, name, context), $"{context}.{name}");

        public static double ReadDouble(JObject obj, string name, string context) =>
            ReadDouble(Required(obj, name, context), $"{context}.{name}");

        public static string ReadString(JObject obj, string name, string context) =>
            ReadString(Required(obj, name, context), $"{context}.{name}");

        public static string ReadOptionalString(JObject obj, string name, string context)
        {
            if (!obj.TryGetValue(name, StringComparison.Ordinal, out JToken token) ||
                token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
                return null;
            return ReadString(token, $"{context}.{name}");
        }

        public static bool ReadBoolean(JObject obj, string name, string context) =>
            ReadBoolean(Required(obj, name, context), $"{context}.{name}");

        public static JToken ReadJson(JObject obj, string name, string context)
        {
            JToken token = Required(obj, name, context);
            if (token.Type != JTokenType.Object && token.Type != JTokenType.Array)
                throw new ConfigDataException($"{context}.{name} 必须是对象或数组");
            return token.DeepClone();
        }

        public static int ReadInt32(JToken token, string context)
        {
            if (token.Type == JTokenType.Integer && long.TryParse(token.ToString(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out long number) && number >= int.MinValue && number <= int.MaxValue)
                return (int)number;
            if (token.Type == JTokenType.String && int.TryParse(token.Value<string>(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int textNumber))
                return textNumber;
            throw new ConfigDataException($"{context} 必须是 int");
        }

        public static long ReadInt64(JToken token, string context)
        {
            if ((token.Type == JTokenType.Integer || token.Type == JTokenType.String) &&
                long.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
                return value;
            throw new ConfigDataException($"{context} 必须是 long 十进制整数");
        }

        public static float ReadSingle(JToken token, string context)
        {
            double value = ReadDouble(token, context);
            if (value < -float.MaxValue || value > float.MaxValue)
                throw new ConfigDataException($"{context} 超出 float 范围");
            return (float)value;
        }

        public static double ReadDouble(JToken token, string context)
        {
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                double value = (double)token;
                if (!double.IsNaN(value) && !double.IsInfinity(value))
                    return value;
            }
            throw new ConfigDataException($"{context} 必须是有限数字");
        }

        public static string ReadString(JToken token, string context)
        {
            if (token.Type == JTokenType.String) return token.Value<string>();
            throw new ConfigDataException($"{context} 必须是 string");
        }

        public static bool ReadBoolean(JToken token, string context)
        {
            if (token.Type == JTokenType.Boolean) return token.Value<bool>();
            throw new ConfigDataException($"{context} 必须是 bool");
        }

        public static IEnumerable<ConfigRowNode> EnumerateRows(JToken root, int keyCount, string context)
        {
            if (keyCount <= 0) throw new ConfigDataException($"配置 {context} 的 Key 层数无效：{keyCount}");
            foreach (ConfigRowNode node in Walk(RequireObject(root, context), keyCount, 0,
                         new JToken[keyCount], context))
                yield return node;
        }

        private static IEnumerable<ConfigRowNode> Walk(
            JObject current,
            int keyCount,
            int depth,
            JToken[] keys,
            string context)
        {
            foreach (JProperty property in current.Properties())
            {
                keys[depth] = new JValue(property.Name);
                if (depth == keyCount - 1)
                {
                    if (property.Value is not JObject row)
                        throw new ConfigDataException($"配置 {context} 的 Key {property.Path} 对应值必须是对象");
                    yield return new ConfigRowNode((JToken[])keys.Clone(), row);
                    continue;
                }

                if (property.Value is not JObject child)
                    throw new ConfigDataException($"配置 {context} 的索引节点 {property.Path} 必须是对象");
                foreach (ConfigRowNode node in Walk(child, keyCount, depth + 1, keys, context))
                    yield return node;
            }
        }

        private static JToken Required(JObject obj, string name, string context)
        {
            if (obj.TryGetValue(name, StringComparison.Ordinal, out JToken token) &&
                token.Type != JTokenType.Null && token.Type != JTokenType.Undefined)
                return token;
            throw new ConfigDataException($"配置 {context} 缺少字段 {name}");
        }
    }
}
