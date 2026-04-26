using System.Text.Json;
using System.Text.Json.Serialization;
using A_Pair.Core.Models;

namespace A_Pair.Infrastructure.Serialization
{
    public class SeatJsonConverter : JsonConverter<Seat>
    {
        public override Seat? Read (ref Utf8JsonReader reader , Type typeToConvert , JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            if (!root.TryGetProperty("Type" , out var typeProp))
                throw new JsonException("Seat must contain 'Type' property");

            string typeString;
            if (typeProp.ValueKind == JsonValueKind.Number)
            {
                int typeInt = typeProp.GetInt32();
                typeString = ((SeatType)typeInt).ToString();
            }
            else if (typeProp.ValueKind == JsonValueKind.String)
            {
                typeString = typeProp.GetString()!;
            }
            else
            {
                throw new JsonException($"Invalid Type value kind: {typeProp.ValueKind}");
            }

            return typeString switch
            {
                "Grid" => Deserialize<GridSeat>(root , options),
                "Polar" => Deserialize<PolarSeat>(root , options),
                "Freeform" => Deserialize<FreeformSeat>(root , options),
                _ => throw new JsonException($"Unsupported Seat type: {typeString}")
            };
        }

        public override void Write (Utf8JsonWriter writer , Seat value , JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            // 1. 手动写入 Type 字段（用字符串形式，Read 方法已兼容两种格式）
            writer.WriteString("Type" , value.Type.ToString());

            // 2. 写入其余属性（通过反射或直接按具体类型序列化后合并）
            // 最简单的方法：序列化整个对象到一个临时文档，然后复制其属性（除 Type 外）
            var tempJson = JsonSerializer.Serialize(value , value.GetType() , options);
            using var tempDoc = JsonDocument.Parse(tempJson);
            foreach (var prop in tempDoc.RootElement.EnumerateObject())
            {
                if (prop.Name == "Type") continue; // 已手动写入
                prop.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        private static T? Deserialize<T> (JsonElement element , JsonSerializerOptions options) where T : Seat
        {
            var json = element.GetRawText();
            return JsonSerializer.Deserialize<T>(json , options);
        }
    }
}