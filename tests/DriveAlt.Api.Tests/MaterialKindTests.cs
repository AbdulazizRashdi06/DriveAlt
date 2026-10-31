using System.Reflection;
using System.Text.Json.Nodes;
using DriveAlt.Api.Chat;
using DriveAlt.Api.Files;
using DriveAlt.Api.Search;

namespace DriveAlt.Api.Tests;

public class MaterialKindTests
{
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
    public void Search_and_options_name_a_kind_and_replies_have_no_asked_for()
    {
        var tools = (JsonArray)typeof(ChatService).GetField("Tools", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var declarations = tools[0]!["functionDeclarations"]!.AsArray();
        JsonNode Parameters(string name) => declarations.Single(d => d!["name"]!.GetValue<string>() == name)!["parameters"]!;
        Assert.Contains("want", Parameters("search_material")["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        // No asked_for: what the model picks is what is shown.
        Assert.DoesNotContain("asked_for", tools.ToJsonString());
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
