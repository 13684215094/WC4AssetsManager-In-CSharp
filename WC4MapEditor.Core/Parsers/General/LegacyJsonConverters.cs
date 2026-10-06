using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WC4MapEditor.Core.Parsers.General;

/// <summary>Compatibility converters for the upstream JSON editors.</summary>
public sealed class SafeInt32Converter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var i)) return i;
                if (reader.TryGetDouble(out var d)) return Clamp(d);
                return 0;

            case JsonTokenType.String:
                var s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s)) return 0;
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var si)) return si;
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var sd)) return Clamp(sd);
                return 0;

            case JsonTokenType.True:
                return 1;
            case JsonTokenType.False:
                return 0;
            default:
                return 0;
        }
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);

    private static int Clamp(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return 0;
        if (d >= int.MaxValue) return int.MaxValue;
        if (d <= int.MinValue) return int.MinValue;
        return (int)Math.Round(d);
    }
}

/// <summary>
/// 宽松的 List&lt;int&gt; 读取转换器：容忍 null、单个数字、逗号分隔字符串等写法
/// </summary>
public sealed class SafeIntListConverter : JsonConverter<List<int>>
{
    private static readonly SafeInt32Converter IntReader = new();

    public override List<int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var list = new List<int>();
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                list.Add(IntReader.Read(ref reader, typeof(int), options));
                return list;

            case JsonTokenType.String:
                var s = reader.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    foreach (var part in s.Split(',', ';', '|', ' ', '\t'))
                    {
                        if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                            list.Add(v);
                    }
                }
                return list;

            case JsonTokenType.StartArray:
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    list.Add(IntReader.Read(ref reader, typeof(int), options));
                }
                return list;

            default:
                return list;
        }
    }

    public override void Write(Utf8JsonWriter writer, List<int> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var v in value) writer.WriteNumberValue(v);
        writer.WriteEndArray();
    }
}