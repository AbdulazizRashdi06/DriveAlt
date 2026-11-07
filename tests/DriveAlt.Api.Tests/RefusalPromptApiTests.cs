using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace DriveAlt.Api.Tests;

public partial class ApiTests
{
    /// <summary>
    /// Owner, 2026-11-07: when the bot won't solve something it should say Abdulaziz told it not to. The model writes that
    /// line itself (the owner rejected fixed refusal lines on 2026-10-28), so the prompt carries the instruction and the
    /// server passes the model's words through unchanged.
    /// </summary>
    [Fact]
    public async Task Refusals_are_told_to_name_abdulaziz_in_the_models_own_words()
    {
        var student = await app.SignInAsync("25-8894");
        var chat = (await (await student.PostAsync("/api/chats", null)).Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
        string? system = null;
        app.ChatModel.Enqueue(request =>
        {
            system = request["messages"]![0]!["content"]!.GetValue<string>();
            return ScriptedChatModel.Tool("reply", new JsonObject
            {
                ["commentary"] = "Abdulaziz made me promise not to do sums, sorry! Want something from the drive?",
                ["result_refs"] = new JsonArray(), ["refused"] = true, ["more_commentary"] = new JsonArray(),
            });
        });
        var reply = await SendAsync(student, chat, "1 + 1", prepared: true);

        Assert.Contains("that Abdulaziz told you not to", system);
        Assert.DoesNotContain("blaming the owner", system);
        Assert.True(reply["refused"]!.GetValue<bool>());
        Assert.Equal("Abdulaziz made me promise not to do sums, sorry! Want something from the drive?", reply["text"]!.GetValue<string>());
    }
}
