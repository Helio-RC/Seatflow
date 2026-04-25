using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using A_Pair.Core.Models;

namespace A_Pair.Infrastructure.Serialization
{
    /// <summary>
    /// 多态序列化 Seat 及其派生类（GridSeat、PolarSeat）
    /// </summary>
    public class SeatJsonConverter : JsonConverter<Seat>
    {
        public override Seat? Read (ref Utf8JsonReader reader , Type typeToConvert , JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            // 根据 "Type" 属性判断具体类型
            if (!root.TryGetProperty("Type" , out var typeProp))
                throw new JsonException("Seat 必须包含 'Type' 属性");

            var type = typeProp.GetString();
            return type switch
            {
                "Grid" => Deserialize<GridSeat>(root , options),
                "Polar" => Deserialize<PolarSeat>(root , options),
                _ => throw new JsonException($"不支持的 Seat 类型: {type}")
            };
        }

        public override void Write (Utf8JsonWriter writer , Seat value , JsonSerializerOptions options)
        {
            // 按实际运行时类型序列化
            JsonSerializer.Serialize(writer , value , value.GetType() , options);
        }

        private static T? Deserialize<T> (JsonElement element , JsonSerializerOptions options) where T : Seat
        {
            var json = element.GetRawText();
            return JsonSerializer.Deserialize<T>(json , options);
        }
    }
}