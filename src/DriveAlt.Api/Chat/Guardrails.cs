using System.Text.RegularExpressions;

namespace DriveAlt.Api.Chat;

public static class RefusalLines
{
    public static readonly string[] All =
    [
        "Sorry, I want to help, but Abdulaziz told me not to answer 🤐",
        "Take it up with Abdulaziz, I'm just following orders.",
        "Abdulaziz would unplug me if I solved that. The page is all yours though.",
        "Nope! Abdulaziz says answers live in your brain, not mine.",
        "I could tell you… but then Abdulaziz would have to rewrite me 🤐",
        "Abdulaziz made me pinky-promise: no answers, no hints, no peeking.",
        "Hints count as answers in Abdulaziz's rulebook. I don't make the rules (he does).",
    ];

    /// <summary>Round-robin per conversation, so the same line never shows twice in a row.</summary>
    public static string Next(int refusalCount) => All[refusalCount % All.Length];
}

/// <summary>Last line of defence on the bot's free text: it may only chat about the search, never leak content.</summary>
public static partial class Guardrails
{
    public const int MaxCommentary = 280;
    public const int ShingleSize = 5;

    public static readonly string[] FoundLines =
    [
        "Ta-da! Fished these out of the drive for you.",
        "Dug these up. Go get 'em!",
        "Fresh from the batch drive, still warm.",
    ];

    public const string NothingLine = "I dug everywhere and came back empty-pawed. Try other words, or check Browse?";

    [GeneratedRegex(@"\b(the\s+answer\s+is|answer\s*:|answers?\s+(?:are|would\s+be)|solution\s+is|solution\s*:|correct\s+(?:option|choice|answer)|the\s+result\s+is|step\s*1\b|first,?\s+you|you\s+(?:should|need\s+to|have\s+to)\s+(?:use|apply|compute|calculate|swap|insert|draw)|hint\s*:)|=\s*-?\d", RegexOptions.IgnoreCase)]
    private static partial Regex LeakPhrases();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Words();

    [GeneratedRegex(@"(?<=[.!?…])\s+")]
    private static partial Regex SentenceBreak();

    /// <summary>
    /// Returns safe commentary: the model's line if it passes, else a stock line.
    /// <paramref name="protectedTexts"/> holds every text the bot must not echo (question, slide and answer text of the pages involved).
    /// </summary>
    public static string Clean(string? commentary, IEnumerable<string> protectedTexts, bool hasResults, int seed)
    {
        var fallback = hasResults ? FoundLines[Math.Abs(seed) % FoundLines.Length] : NothingLine;
        var c = (commentary ?? "").Replace("```", "").Replace("**", "").Replace("`", "").Trim();
        if (c.Length == 0) return fallback;
        if (c.Contains('\n')) c = c.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim();

        var sentences = SentenceBreak().Split(c);
        if (sentences.Length > 2) c = string.Join(' ', sentences.Take(2));
        if (c.Length > MaxCommentary) return fallback;
        if (LeakPhrases().IsMatch(c)) return fallback;
        if (Overlaps(c, protectedTexts)) return fallback;
        return c;
    }

    public static bool Overlaps(string text, IEnumerable<string> protectedTexts)
    {
        var mine = Shingles(text);
        if (mine.Count == 0) return false;
        foreach (var t in protectedTexts)
        {
            if (string.IsNullOrWhiteSpace(t)) continue;
            if (Shingles(t).Overlaps(mine)) return true;
        }
        return false;
    }

    private static HashSet<string> Shingles(string text)
    {
        var w = Words().Matches(text.ToLowerInvariant()).Select(m => m.Value).ToArray();
        var set = new HashSet<string>();
        for (var i = 0; i + ShingleSize <= w.Length; i++) set.Add(string.Join(' ', w, i, ShingleSize));
        return set;
    }

    [GeneratedRegex(@"\b(answers?|solutions?|solve|solving|solved|hint|hints|explain|explanation|how\s+(?:do|to|would)\s+(?:i|you|we)\s+(?:solve|do|answer|approach)|check\s+(?:my|this)|is\s+(?:this|my\s+\w+|it)\s+(?:right|correct|wrong)|am\s+i\s+right|what'?s\s+the\s+(?:answer|result|output)|work\s+(?:it\s+)?out|walk\s+me\s+through)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HelpRequest();

    [GeneratedRegex(@"\b(?:with|incl(?:uding)?|and|has|have|got)\s+(?:the\s+)?(?:answers?|solutions?)\b|\b(?:answers?|solutions?)\s+(?:file|sheet|key|pdf)\b|\b(?:solved|solution)\s+(?:past\s+)?papers?\b", RegexOptions.IgnoreCase)]
    private static partial Regex WantsSolvedFile();

    /// <summary>Offline-mode intent check: asking for help with content (refuse) vs. asking for a file that contains solutions (fine).</summary>
    public static bool LooksLikeHelpRequest(string message) =>
        HelpRequest().IsMatch(message) && !WantsSolvedFile().IsMatch(message);
}
