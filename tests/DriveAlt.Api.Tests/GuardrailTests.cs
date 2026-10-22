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
}
