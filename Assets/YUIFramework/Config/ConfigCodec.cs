using System;
using System.IO;
using System.Text;
using MessagePack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace YUIFramework.Configuration
{
    public sealed class ConfigCodec : IConfigCodec
    {
        public JToken Decode(byte[] bytes, ConfigFormat format)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 16 * 1024 * 1024)
                throw new ConfigDataException("Config asset must contain 1..16777216 bytes.");
            string json;
            if (format == ConfigFormat.Json) json = new UTF8Encoding(false, true).GetString(bytes);
            else if (format == ConfigFormat.MessagePack)
            {
                var reader = new MessagePackReader(bytes);
                var nodes = 0;
                Validate(ref reader, 0, ref nodes);
                if (!reader.End) throw new ConfigDataException("Trailing MessagePack data.");
                json = MessagePackSerializer.ConvertToJson(bytes,
                    MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData));
            }
            else throw new ArgumentOutOfRangeException(nameof(format));
            using var text = new StringReader(json);
            using var jsonReader = new JsonTextReader(text)
            { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double, MaxDepth = 64 };
            var value = JToken.Load(jsonReader, new JsonLoadSettings
            { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (jsonReader.Read()) throw new ConfigDataException("Trailing JSON data.");
            ValidateJson(value);
            return value;
        }
        private static void ValidateJson(JToken token)
        {
            if (token.Type == JTokenType.Float)
            {
                var number = token.Value<double>();
                if (double.IsNaN(number) || double.IsInfinity(number))
                    throw new ConfigDataException("Config numbers must be finite.");
            }
            foreach (var child in token.Children()) ValidateJson(child);
        }

        private static void Validate(ref MessagePackReader reader, int depth, ref int nodes)
        {
            if (depth > 64 || ++nodes > 1000000) throw new ConfigDataException("Config complexity limit exceeded.");
            switch (reader.NextMessagePackType)
            {
                case MessagePackType.Map:
                    var entries = reader.ReadMapHeader();
                    for (var index = 0; index < entries; index++)
                    {
                        if (reader.NextMessagePackType != MessagePackType.String)
                            throw new ConfigDataException("Config map keys must be strings.");
                        reader.Skip();
                        Validate(ref reader, depth + 1, ref nodes);
                    }
                    break;
                case MessagePackType.Array:
                    var count = reader.ReadArrayHeader();
                    for (var index = 0; index < count; index++) Validate(ref reader, depth + 1, ref nodes);
                    break;
                case MessagePackType.Extension:
                case MessagePackType.Binary:
                    throw new ConfigDataException("Config protocol does not include binary or extension values.");
                default: reader.Skip(); break;
            }
        }
    }
}
