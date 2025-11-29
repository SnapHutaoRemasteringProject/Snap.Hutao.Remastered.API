using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snap.Hutao.Remastered.API
{
    /// <summary>
    /// Generic response wrapper. JSON property names are determined by instance properties (set by constructor).
    /// A custom JsonConverterFactory handles serialization.
    /// </summary>
    [JsonConverter(typeof(ResponseJsonConverterFactory))]
    public class Response<T>
    {
        // Field name mappings (readonly)
        internal string ReturnCodeName { get; }
        internal string MessageName { get; }
        internal string DataName { get; }
        internal string L10nKeyName { get; }

        public int ReturnCode { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }
        public string? L10nKey { get; set; }

        public Response()
            : this("returnCode", "message", "data", "l10nKey")
        {
        }

        public Response(string returnCodeName, string messageName, string dataName, string l10nKeyName)
        {
            ReturnCodeName = returnCodeName ?? throw new ArgumentNullException(nameof(returnCodeName));
            MessageName = messageName ?? throw new ArgumentNullException(nameof(messageName));
            DataName = dataName ?? throw new ArgumentNullException(nameof(dataName));
            L10nKeyName = l10nKeyName ?? throw new ArgumentNullException(nameof(l10nKeyName));
        }
    }

    /// <summary>
    /// Non-generic response (data is null).
    /// </summary>
    public class Response : Response<object?>
    {
        public Response() : base() { }
        public Response(string returnCodeName, string messageName, string dataName, string l10nKeyName)
            : base(returnCodeName, messageName, dataName, l10nKeyName) { }
    }

    /// <summary>
    /// Hutao-specific response wrapper with the required field names by default.
    /// </summary>
    public class HutaoResponse<T> : Response<T>
    {
        public HutaoResponse()
            : base("returnCode", "message", "data", "l10nKey")
        {
        }

        public HutaoResponse(int returnCode, string? message = null, T? data = default, string? l10nKey = null)
            : this()
        {
            ReturnCode = returnCode;
            Message = message;
            Data = data;
            L10nKey = l10nKey;
        }
    }

    public class HutaoResponse : HutaoResponse<object?>
    {
        public HutaoResponse() : base() { }
        public HutaoResponse(int returnCode, string? message = null, object? data = null, string? l10nKey = null)
            : base(returnCode, message, data, l10nKey) { }
    }

    internal class ResponseJsonConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            if (!typeToConvert.IsGenericType) return false;
            var def = typeToConvert.GetGenericTypeDefinition();
            return def == typeof(Response<>);
        }

        public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            var itemType = typeToConvert.GetGenericArguments()[0];
            var converterType = typeof(ResponseJsonConverter<>).MakeGenericType(itemType);
            return (JsonConverter?)Activator.CreateInstance(converterType) ?? throw new InvalidOperationException("Unable to create converter");
        }
    }

    internal class ResponseJsonConverter<T> : JsonConverter<Response<T>>
    {
        public override Response<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // For simplicity, read into JsonDocument and try common property names.
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            var result = new Response<T>();

            // try common names
            if (root.TryGetProperty("returnCode", out var rc)) result.ReturnCode = rc.GetInt32();
            else if (root.TryGetProperty("code", out rc)) result.ReturnCode = rc.GetInt32();

            if (root.TryGetProperty("message", out var msg)) result.Message = msg.GetString();

            if (root.TryGetProperty("data", out var dataProp))
            {
                result.Data = dataProp.ValueKind == JsonValueKind.Null ? default : JsonSerializer.Deserialize<T>(dataProp.GetRawText(), options);
            }

            if (root.TryGetProperty("l10nKey", out var lk)) result.L10nKey = lk.GetString();

            return result;
        }

        public override void Write(Utf8JsonWriter writer, Response<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            // write returnCode
            writer.WriteNumber(value.ReturnCodeName, value.ReturnCode);

            // message
            if (value.Message is null)
            {
                writer.WriteNull(value.MessageName);
            }
            else
            {
                writer.WriteString(value.MessageName, value.Message);
            }

            // data
            writer.WritePropertyName(value.DataName);
            if (value.Data is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                JsonSerializer.Serialize(writer, value.Data, typeof(T), options);
            }

            // l10nKey
            if (value.L10nKey is null)
            {
                writer.WriteNull(value.L10nKeyName);
            }
            else
            {
                writer.WriteString(value.L10nKeyName, value.L10nKey);
            }

            writer.WriteEndObject();
        }
    }
}
