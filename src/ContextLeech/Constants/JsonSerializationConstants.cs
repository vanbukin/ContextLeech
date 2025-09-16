using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;

namespace ContextLeech.Constants;

public static class JsonSerializationConstants
{
    public static readonly JsonSerializerOptions CommonSerializerOptions =
        new(JsonSerializerDefaults.General)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

    public static readonly JsonSerializerOptions McpJsonOptions =
        new(McpJsonUtilities.DefaultOptions)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false
        };
}
