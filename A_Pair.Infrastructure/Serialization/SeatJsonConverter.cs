using System.Text.Json;
using System.Text.Json.Serialization;
using A_Pair.Core.Models;

namespace A_Pair.Infrastructure.Serialization
{
    /// <summary>
    /// 支持 <see cref="Seat"/> 抽象类多态序列化的 JSON 转换器。
    /// </summary>
    /// <remarks>
    /// 在反序列化时，根据 JSON 中的 "Type" 属性值决定实例化哪个派生类：
    /// <list type="bullet">
    ///   <item><c>"Grid"</c> → <see cref="GridSeat"/></item>
    ///   <item><c>"Polar"</c> → <see cref="PolarSeat"/></item>
    ///   <item><c>"Freeform"</c> → <see cref="FreeformSeat"/></item>
    /// </list>
    /// 在序列化时，按对象的实际运行时类型进行序列化，确保派生类属性不会丢失。
    /// </remarks>
    public class SeatJsonConverter : JsonConverter<Seat>
    {
        /// <summary>
        /// 根据 JSON 中的 "Type" 属性反序列化为对应的 <see cref="Seat"/> 派生类。
        /// </summary>
        /// <param name="reader">UTF-8 JSON 读取器。</param>
        /// <param name="typeToConvert">目标类型。</param>
        /// <param name="options">序列化选项。</param>
        /// <returns>反序列化后的 <see cref="Seat"/> 实例。</returns>
        /// <exception cref="JsonException">当缺少 "Type" 属性或类型不受支持时抛出。</exception>
        public override Seat? Read (ref Utf8JsonReader reader , Type typeToConvert , JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            if (!root.TryGetProperty("Type" , out var typeProp))
                throw new JsonException("Seat must contain 'Type' property");

            var type = typeProp.GetString();
            return type switch
            {
                "Grid" => Deserialize<GridSeat>(root , options),
                "Polar" => Deserialize<PolarSeat>(root , options),
                "Freeform" => Deserialize<FreeformSeat>(root , options),
                _ => throw new JsonException($"Unsupported Seat type: {type}")
            };
        }

        /// <summary>
        /// 按对象的实际运行时类型序列化，确保派生类属性完整输出。
        /// </summary>
        /// <param name="writer">UTF-8 JSON 写入器。</param>
        /// <param name="value">要序列化的 <see cref="Seat"/> 实例。</param>
        /// <param name="options">序列化选项。</param>
        public override void Write (Utf8JsonWriter writer , Seat value , JsonSerializerOptions options)
        {
            // 按实际运行时类型序列化
            JsonSerializer.Serialize(writer , value , value.GetType() , options);
        }

        /// <summary>
        /// 从 <see cref="JsonElement"/> 反序列化为指定的 <see cref="Seat"/> 派生类型。
        /// </summary>
        /// <typeparam name="T">目标 <see cref="Seat"/> 派生类型。</typeparam>
        /// <param name="element">包含 JSON 数据的 <see cref="JsonElement"/>。</param>
        /// <param name="options">序列化选项。</param>
        /// <returns>反序列化后的实例；如果失败则返回 <c>null</c>。</returns>
        private static T? Deserialize<T> (JsonElement element , JsonSerializerOptions options) where T : Seat
        {
            var json = element.GetRawText();
            return JsonSerializer.Deserialize<T>(json , options);
        }
    }
}