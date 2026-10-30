using System.Reflection;
using System.Text.Json.Nodes;
using DriveAlt.Api.Chat;
using DriveAlt.Api.Files;
using DriveAlt.Api.Search;

namespace DriveAlt.Api.Tests;

public class MaterialKindTests
{
    [Theory]
    [InlineData("questions on circular queues", "questions")]
    [InlineData("slides about queues", "slides")]
    [InlineData("a lecture on hashing", "slides")]
    [InlineData("Q3", "questions")]
    [InlineData("exercise on trees", "questions")]
    [InlineData("Send me exercise sheet 6", null)]
    [InlineData("exercise 6 pdf", null)]
    [InlineData("download exercises on trees", null)]
    [InlineData("the exercise file", null)]
    [InlineData("exercise PDFs", null)]
    [InlineData("questions from exercise sheet 6", "questions")]
    [InlineData("lecture notes on hashing", null)]
    [InlineData("Lecture Notes on hashing", null)]
    [InlineData("lecture-notes on hashing", null)]
    [InlineData("problems about graphs", "questions")]
    [InlineData("queues", null)]
    [InlineData("midterm 2023 pdf", null)]
    [InlineData("the slide with Dijkstra's pseudocode", "slide_pages")]
    [InlineData("slide 12", "slide_pages")]
    [InlineData("slides and questions", null)]
    [InlineData("questions from the slides", null)]
    [InlineData("landslides and unquestionable facts", null)]
    public void Asked_kind_only_overrides_clear_vocabulary(string text, string? kind) =>
        Assert.Equal(kind, Heuristics.AskedKind(text));

    [Theory]
    [InlineData("[{\"files\":true}]", "files")]
    [InlineData("[{\"files\":false}]", "questions")]
    [InlineData("[{}]", "questions")]
    [InlineData("[{\"files\":true,\"want\":\"slides\"}]", "slides")]
    public void Legacy_options_map_to_the_new_contract(string json, string want)
    {
        var options = ChatOption.Read(json);
        Assert.Equal(want, Assert.Single(options).Want);
        var saved = ChatOption.Write(options)!;
        Assert.DoesNotContain("\"files\":", saved);
        Assert.Equal(want, Assert.Single(ChatOption.Read(saved)).Want);
    }

    [Fact]
    public void Tool_schemas_require_the_contract_on_search_options_and_reply()
    {
        var tools = (JsonArray)typeof(ChatService).GetField("Tools", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var declarations = tools[0]!["functionDeclarations"]!.AsArray();
        JsonNode Parameters(string name) => declarations.Single(d => d!["name"]!.GetValue<string>() == name)!["parameters"]!;
        Assert.Contains("want", Parameters("search_material")["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Contains("asked_for", Parameters("reply")["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        var option = Parameters("ask")["properties"]!["options"]!["items"]!;
        Assert.Contains("want", option["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.DoesNotContain("result_kind", tools.ToJsonString());
    }

    [Fact]
    public void File_refs_preserve_best_page_and_read_legacy_covers()
    {
        var id = Guid.NewGuid();
        Assert.True(ResultRefs.TryParseFile(ResultRefs.File(id, 9), out var parsed, out var page));
        Assert.Equal(id, parsed);
        Assert.Equal(9, page);
        Assert.True(ResultRefs.TryParseFile(ResultRefs.File(id), out parsed, out page));
        Assert.Null(page);
        Assert.False(ResultRefs.TryParseFile($"f:{id}?page=-1", out _));
        Assert.False(ResultRefs.TryParseFile($"f:{id}?page=2?other=x", out _));
    }
}
