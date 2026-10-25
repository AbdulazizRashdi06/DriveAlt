using DriveAlt.Api.Chat;

namespace DriveAlt.Api.Tests;

public class GuardrailTests
{
    private static readonly string[] PageTexts =
    [
        "Question 1. A circular queue of size 8 has front = 5 and rear = 2. How many elements are in the queue?",
        "Answer: 5 elements, because (rear - front + size) mod size = 5.",
    ];

    [Fact]
    public void Keeps_short_playful_commentary()
    {
        var line = Guardrails.Clean("Ta-da! Two queue questions from the 2024 midterm.", PageTexts, hasResults: true, seed: 0);
        Assert.Equal("Ta-da! Two queue questions from the 2024 midterm.", line);
    }

    [Theory]
    [InlineData("The answer is 5 elements.")]
    [InlineData("Hint: think about wrapping around.")]
    [InlineData("You should use modular arithmetic here.")]
    [InlineData("So rear - front + size = 5, easy!")]
    public void Replaces_lines_that_leak_answers_or_hints(string commentary)
    {
        var line = Guardrails.Clean(commentary, PageTexts, hasResults: true, seed: 0);
        Assert.Contains(line, Guardrails.FoundLines);
    }

    [Fact]
    public void Replaces_lines_that_quote_the_page()
    {
        var quote = "Here you go: a circular queue of size 8 has front = 5 and rear = 2!";
        Assert.True(Guardrails.Overlaps(quote, PageTexts));
        Assert.Contains(Guardrails.Clean(quote, PageTexts, true, 1), Guardrails.FoundLines);
    }

    [Fact]
    public void Trims_to_two_sentences_and_drops_long_rambles()
    {
        Assert.Equal("One. Two!", Guardrails.Clean("One. Two! Three. Four.", [], true, 0));
        Assert.Equal(Guardrails.NothingLine, Guardrails.Clean(new string('a', 400), [], false, 0));
    }

    [Fact]
    public void Refusal_lines_rotate_and_blame_abdulaziz()
    {
        var lines = Enumerable.Range(0, RefusalLines.All.Length).Select(i => RefusalLines.Next(i)).ToList();
        Assert.Equal(lines.Count, lines.Distinct().Count());
        Assert.All(lines, l => Assert.Contains("Abdulaziz", l));
        Assert.NotEqual(RefusalLines.Next(0), RefusalLines.Next(1));
        Assert.All(RefusalLines.WithScreenshot, l => Assert.Contains("Abdulaziz", l));
        Assert.NotEqual(RefusalLines.Next(0, withScreenshot: true), RefusalLines.Next(0));
    }

    [Theory]
    [InlineData("what's the answer to Q2", true)]
    [InlineData("can you give me a hint for question 3", true)]
    [InlineData("is my answer right? I got 5", true)]
    [InlineData("explain how to solve the heap question", true)]
    [InlineData("quiz 3 with solutions", false)]
    [InlineData("past paper questions on heaps", false)]
    [InlineData("slides on hashing", false)]
    public void Offline_intent_check(string message, bool refuse)
    {
        Assert.Equal(refuse, Guardrails.LooksLikeHelpRequest(message));
    }

    [Theory]
    [InlineData("any question regarding circular queue deon or end calculation", false)]
    [InlineData("find questions on calculating queue length", false)]
    [InlineData("quiz 3 with solutions", false)]
    [InlineData("what's the answer to Q2", true)]
    [InlineData("give me a hint for Q3", true)]
    public void Model_refusal_does_not_turn_a_question_search_into_an_answer_request(string message, bool refuse)
    {
        Assert.Equal(refuse, ChatService.ShouldRefuse(message, modelRefused: true));
    }

    [Theory]
    [InlineData("Data Structures", "Data Structures")]
    [InlineData("  Efficient Algorithms ", "Efficient Algorithms")]
    [InlineData("", "Fallback")]
    [InlineData("The answer is 5 elements", "Fallback")]
    [InlineData("A label that is far too long to fit on one small chip", "Fallback")]
    [InlineData("circular queue of size 8 has front", "Fallback")]
    public void Option_labels_are_short_and_leak_nothing(string label, string expected)
    {
        Assert.Equal(expected, Guardrails.CleanLabel(label, PageTexts, "Fallback"));
    }

    [Fact]
    public void A_bad_question_falls_back_to_the_given_line()
    {
        Assert.Equal(ChatService.AskFallback, Guardrails.Clean("The answer is 5.", PageTexts, false, 0, ChatService.AskFallback));
        Assert.Equal("Which course, DS or algorithms?", Guardrails.Clean("Which course, DS or algorithms?", PageTexts, false, 0, ChatService.AskFallback));
    }

    [Theory]
    [InlineData("Ta-da!", 0, "Ta-da!")]
    [InlineData("Ta-da!", 9, "Ta-da! I've got 9 more when you want them.")]
    [InlineData("Best 3 here, plenty more where that came from.", 9, "Best 3 here, plenty more where that came from.")]
    public void Held_back_results_are_always_mentioned(string line, int held, string expected)
    {
        Assert.Equal(expected, ChatService.WithMoreNote(line, held));
    }

    private static readonly List<DriveAlt.Api.Data.Course> PickCourses =
    [
        new() { Id = 1, Slug = "data-structures", Name = "Data Structures", Aliases = ["DS", "data structures"] },
        new() { Id = 2, Slug = "database-systems", Name = "Database Systems", Aliases = ["DB", "database", "SQL"] },
        new() { Id = 3, Slug = "probability-statistics", Name = "Probability and Statistics", Aliases = ["probability", "stats"] },
    ];

    [Theory]
    [InlineData("data structures", "data-structures", "Data Structures")]
    [InlineData("the ds one", "data-structures", "Data Structures")]
    [InlineData("database please", "database-systems", "DB quiz")]
    [InlineData("the first one", "database-systems", "DB quiz")]
    public void A_typed_pick_follows_the_course_the_student_names(string text, string course, string label)
    {
        var chosen = new DriveAlt.Api.Chat.ChatOption { Label = "DB quiz", Query = "quiz 3", Course = "database-systems", Type = "quiz", Refs = ["r:x"], Done = true };
        var pick = ChatService.TypedPick(text, chosen, PickCourses);
        Assert.Equal(course, pick.Course);
        Assert.Equal(label, pick.Label);
        Assert.Equal("quiz 3", pick.Query);
        Assert.Equal(pick.Course == chosen.Course, pick.Done);
    }
}
