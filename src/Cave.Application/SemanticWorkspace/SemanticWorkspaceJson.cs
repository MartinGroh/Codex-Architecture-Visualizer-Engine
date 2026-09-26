using System.Text.Json;
using System.Text.Json.Serialization;
using Cave.Domain;

namespace Cave.Application;

/// <summary>
/// Owns the canonical JSON contract shared by semantic-workspace HTTP, MCP, and file adapters.
/// </summary>
public static class SemanticWorkspaceJson
{
    /// <summary>Creates the canonical camel-case, enum-string, polymorphic JSON options.</summary>
    /// <param name="writeIndented">Whether durable or diagnostic output should be indented.</param>
    /// <returns>Independent serializer options safe for adapter customization by cloning.</returns>
    public static JsonSerializerOptions CreateOptions(bool writeIndented = false)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = writeIndented,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new SemanticContentBlockJsonConverter());
        return options;
    }

    private sealed class SemanticContentBlockJsonConverter : JsonConverter<SemanticContentBlock>
    {
        public override SemanticContentBlock Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var id = RequiredString(root, "id");
            var kind = RequiredString(root, "kind");
            return kind switch
            {
                "text" => new SemanticTextBlock(id, RequiredString(root, "text", allowEmpty: true)),
                "checklist" => new SemanticChecklistBlock(
                    id,
                    DeserializeRequired<SemanticChecklistItem[]>(root, "items", options)),
                "code" => new SemanticCodeBlock(
                    id,
                    RequiredString(root, "source", allowEmpty: true),
                    RequiredString(root, "language"),
                    OptionalString(root, "filename")),
                "math" => new SemanticMathBlock(id, RequiredString(root, "source", allowEmpty: true)),
                "image" => new SemanticImageBlock(
                    id,
                    RequiredString(root, "assetId"),
                    OptionalString(root, "caption"),
                    RequiredString(root, "altText", allowEmpty: true)),
                "drawing" => new SemanticDrawingBlock(
                    id,
                    DeserializeRequired<SemanticDrawingStroke[]>(root, "strokes", options)),
                _ => throw new JsonException($"Semantic content kind '{kind}' is unsupported."),
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            SemanticContentBlock value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id);
            writer.WriteString("kind", value.Kind);
            switch (value)
            {
                case SemanticTextBlock text:
                    writer.WriteString("text", text.Text);
                    break;
                case SemanticChecklistBlock checklist:
                    writer.WritePropertyName("items");
                    JsonSerializer.Serialize(writer, checklist.Items, options);
                    break;
                case SemanticCodeBlock code:
                    writer.WriteString("source", code.Source);
                    writer.WriteString("language", code.Language);
                    WriteOptionalString(writer, "filename", code.Filename);
                    break;
                case SemanticMathBlock math:
                    writer.WriteString("source", math.Source);
                    break;
                case SemanticImageBlock image:
                    writer.WriteString("assetId", image.AssetId);
                    WriteOptionalString(writer, "caption", image.Caption);
                    writer.WriteString("altText", image.AltText);
                    break;
                case SemanticDrawingBlock drawing:
                    writer.WritePropertyName("strokes");
                    JsonSerializer.Serialize(writer, drawing.Strokes, options);
                    break;
                default:
                    throw new JsonException($"Semantic content type '{value.GetType().Name}' is unsupported.");
            }

            writer.WriteEndObject();
        }

        private static T DeserializeRequired<T>(
            JsonElement root,
            string propertyName,
            JsonSerializerOptions options) =>
            root.TryGetProperty(propertyName, out var property)
                ? property.Deserialize<T>(options)
                    ?? throw new JsonException($"Property '{propertyName}' cannot be null.")
                : throw new JsonException($"Property '{propertyName}' is required.");

        private static string RequiredString(
            JsonElement root,
            string propertyName,
            bool allowEmpty = false)
        {
            if (!root.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.String)
            {
                throw new JsonException($"String property '{propertyName}' is required.");
            }

            var value = property.GetString()!;
            if (!allowEmpty && string.IsNullOrWhiteSpace(value))
            {
                throw new JsonException($"String property '{propertyName}' cannot be empty.");
            }

            return value;
        }

        private static string? OptionalString(JsonElement root, string propertyName) =>
            !root.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null
                ? null
                : property.ValueKind == JsonValueKind.String
                    ? property.GetString()
                    : throw new JsonException($"Property '{propertyName}' must be a string or null.");

        private static void WriteOptionalString(
            Utf8JsonWriter writer,
            string propertyName,
            string? value)
        {
            if (value is null)
            {
                writer.WriteNull(propertyName);
            }
            else
            {
                writer.WriteString(propertyName, value);
            }
        }
    }
}
