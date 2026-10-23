using System.Text.Json.Nodes;
using DriveAlt.Api.Ai;

namespace DriveAlt.Api.Tests;

public class DeepSeekTests
{
    [Fact]
    public void Converts_gemini_function_declarations_to_openai_tools()
    {
        var gemini = (JsonArray)JsonNode.Parse("""
            [{ "functionDeclarations": [{
              "name": "search_material",
              "description": "Search the drive.",
              "parameters": {
                "type": "OBJECT",
                "properties": {
                  "query": { "type": "STRING" },
                  "year": { "type": "INTEGER" },
                  "refs": { "type": "ARRAY", "items": { "type": "STRING" } }
                },
                "required": ["query"]
              }
            }] }]
            """)!;

        var tools = DeepSeekClient.ToolsFromGemini(gemini);

        var fn = Assert.Single(tools)!["function"]!;
        Assert.Equal("function", tools[0]!["type"]!.GetValue<string>());
        Assert.Equal("search_material", fn["name"]!.GetValue<string>());
        var p = fn["parameters"]!;
        Assert.Equal("object", p["type"]!.GetValue<string>());
        Assert.Equal("string", p["properties"]!["query"]!["type"]!.GetValue<string>());
        Assert.Equal("integer", p["properties"]!["year"]!["type"]!.GetValue<string>());
        Assert.Equal("string", p["properties"]!["refs"]!["items"]!["type"]!.GetValue<string>());
        Assert.Equal("query", p["required"]![0]!.GetValue<string>());
        // The source declarations are left untouched (the Gemini loop still uses them).
        Assert.Equal("OBJECT", gemini[0]!["functionDeclarations"]![0]!["parameters"]!["type"]!.GetValue<string>());
    }
}
