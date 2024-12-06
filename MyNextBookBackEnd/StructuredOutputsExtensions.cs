using System.Diagnostics;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

using Microsoft.Extensions.Logging;

using Newtonsoft.Json.Schema;
using Newtonsoft.Json.Schema.Generation;

using OpenAI.Chat;

namespace MyNextBookBackEnd;

internal static class StructuredOutputsExtensions
{

    public static Func<JsonSchemaExporterContext?, JsonNode, JsonNode> StructuredOutputsTransform = new((context, node) =>
    {
        static void ProcessJsonObject(JsonObject jsonObject)
        {
            if (jsonObject.ContainsKey("properties"))
            {
                jsonObject["additionalProperties"] = false;

                var properties = jsonObject["properties"] as JsonObject;
                if (properties != null)
                {
                    foreach (var property in properties)
                    {
                        if (property.Value is JsonObject nestedObject)
                        {
                            ProcessJsonObject(nestedObject);
                        }
                    }
                }
            }

            // Check for nested objects in arrays
            if (jsonObject.ContainsKey("items"))
            {
                var items = jsonObject["items"] as JsonObject;
                if (items != null)
                {
                    ProcessJsonObject(items);
                }
            }
        }

        if (node is JsonObject rootObject)
        {
            ProcessJsonObject(rootObject);
        }

        return node;
    });

    public static T? CompleteChat<T>(this ChatClient chatClient, List<ChatMessage> messages, ChatCompletionOptions options)
    {
        ChatCompletion completion = chatClient.CompleteChat(messages, options);
        return JsonSerializer.Deserialize<T>(completion.Content[0].Text);
    }

    public static ChatResponseFormat CreateJsonSchemaFormat<T>(
           string jsonSchemaFormatName,
           string? jsonSchemaFormatDescription = null,
           bool? jsonSchemaIsStrict = null,
           ILogger? logger = null)
    {
        JSchemaGenerator generator = new JSchemaGenerator
        {
            SchemaReferenceHandling = SchemaReferenceHandling.None,
        };
        JSchema schema = generator.Generate(typeof(T));
        JsonNode? schemaNode = JsonNode.Parse(schema.ToString());
        JsonSchemaExporterContext context = new JsonSchemaExporterContext(); // Create an empty context
        schemaNode = StructuredOutputsTransform(context, schemaNode);

        // Serialize the modified schema node to a formatted JSON string
        string jsonString = schemaNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        return ChatResponseFormat.CreateJsonSchemaFormat(
            jsonSchemaFormatName,
            jsonSchema: BinaryData.FromString(jsonString),
            jsonSchemaFormatDescription: jsonSchemaFormatDescription,
            jsonSchemaIsStrict: jsonSchemaIsStrict
        );
    }



}
