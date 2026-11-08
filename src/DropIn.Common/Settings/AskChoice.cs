using System.Text.Json;
using System.Text.Json.Serialization;

namespace DropIn.Common;

/// <summary>A yes/no option that can also ask the user; written to the config file as true, false or "Ask".</summary>
[JsonConverter(typeof(AskChoiceConverter))]
public enum AskChoice
{
    /// <summary>Never do it.</summary>
    No,
    /// <summary>Always do it.</summary>
    Yes,
    /// <summary>Ask the user each time.</summary>
    Ask
}

/// <summary>Reads true, false or "Ask" (and the older text values such as Always, Never, Overwrite and Skip).</summary>
sealed class AskChoiceConverter : JsonConverter<AskChoice>
{
    /// <inheritdoc />
    public override AskChoice Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True: return AskChoice.Yes;
            case JsonTokenType.False: return AskChoice.No;
            case JsonTokenType.String:
                return reader.GetString()?.Trim().ToLowerInvariant() switch
                {
                    "true" or "yes" or "always" or "overwrite" => AskChoice.Yes,
                    "false" or "no" or "never" or "skip" => AskChoice.No,
                    _ => AskChoice.Ask
                };
            default:
                reader.Skip();
                return AskChoice.Ask;
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, AskChoice value, JsonSerializerOptions options)
    {
        if (value == AskChoice.Ask) writer.WriteStringValue("Ask");
        else writer.WriteBooleanValue(value == AskChoice.Yes);
    }
}

/// <summary>Reads a boolean, also accepting the older text values (Interactive is true, Fail is false).</summary>
sealed class LenientBoolConverter : JsonConverter<bool>
{
    /// <inheritdoc />
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True: return true;
            case JsonTokenType.False: return false;
            case JsonTokenType.String:
                return !(reader.GetString()?.Trim().ToLowerInvariant() is "false" or "no" or "fail");
            default:
                reader.Skip();
                return true;
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
}
