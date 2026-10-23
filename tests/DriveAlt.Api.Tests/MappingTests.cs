using DriveAlt.Api.Files;
using DriveAlt.Api.Ai;
using DriveAlt.Api.Data;
using DriveAlt.Api.Import;
using DriveAlt.Api.Search;

namespace DriveAlt.Api.Tests;

public class MappingTests
{
    private static readonly List<Course> Courses =
    [
        new() { Id = 1, Slug = "data-structures", Name = "Data Structures", Semester = 1, Group = "ALL", Aliases = ["DS", "data structure"] },
        new() { Id = 2, Slug = "database-systems", Name = "Database Systems", Semester = 1, Group = "ALL", Aliases = ["DB", "SQL"] },
        new() { Id = 3, Slug = "its", Name = "Intelligent Transportation Systems", Semester = 1, Group = "AI", Aliases = ["ITS", "ITS 2024"] },
        new() { Id = 4, Slug = "software-engineering", Name = "Software Engineering", Semester = 2, Group = "", Aliases = ["SWE"] },
    ];

    [Theory]
    [InlineData("ds_midterm_2024.pdf", "data-structures", MaterialTypes.PastPaper, 2024)]
    [InlineData("Lecture 10 - Queues.pdf", null, MaterialTypes.Slides, null)]
    [InlineData("SQL Quiz_ANSWERS.pdf", "database-systems", MaterialTypes.Quiz, null)]
    public void Guesses_from_file_names(string name, string? course, string type, int? year)
    {
        var g = Heuristics.FromPath(name, Courses);
        Assert.Equal(course, g.Course?.Slug);
        Assert.Equal(type, g.MaterialType);
        Assert.Equal(year, g.Year);
    }

    [Theory]
    [InlineData("Quiz 3 revision", "Quiz 3")]
    [InlineData("midterm 1", "Midterm 1")]
    [InlineData("Lecture notes", null)]
    public void Reads_assessment_labels(string text, string? label) => Assert.Equal(label, Heuristics.Label(text));

    [Fact]
    public void Maps_drive_paths_like_the_batch_drive()
    {
        var m = DriveImporter.Map(["semester 1", "ALL", "data structures", "past papers", "midterm", "midterm 1"], "WhatsApp Image.jpeg", Courses);
        Assert.Equal("data-structures", m.Course?.Slug);
        Assert.Equal(MaterialTypes.PastPaper, m.Type);
        Assert.Equal("midterm/midterm 1", m.Folder);
        Assert.Equal("Midterm 1", m.Label);

        var q = DriveImporter.Map(["semester 1", "ALL", "data structures", "past papers", "Quiz 3"], "x.JPG", Courses);
        Assert.Equal(MaterialTypes.Quiz, q.Type);
        Assert.Equal("Quiz 3", q.Folder);

        var s = DriveImporter.Map(["semester 1", "ALL", "data structures", "slides", "2024"], "Lecture 9 - Stacks.pdf", Courses);
        Assert.Equal(MaterialTypes.Slides, s.Type);
        Assert.Equal("2024", s.Folder);
        Assert.Equal(2024, s.Year);

        var its = DriveImporter.Map(["semester 1", "AI", "ITS 2024"], "VANET.pptx", Courses);
        Assert.Equal("its", its.Course?.Slug);
    }

    [Fact]
    public void Converts_gemini_boxes_to_normalized_regions()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse("""
            {"page_kind":"assessment","text":"Q1 ...","summary":"s","topic":"queues","tags":["Queues","queues","rear"],
             "material_type":"quiz","academic_year":2024,"regions":[
               {"label":"question","box_2d":[100,50,300,950],"question_number":"1","text":"Q1"},
               {"label":"answer","box_2d":[300,50,400,950],"text":"5"},
               {"label":"other","box_2d":[0,0,0,0],"text":"too small"}]}
            """)!;
        var x = MaterialAi.FromJson(json, json.ToJsonString());
        Assert.Single(x.Regions);
        var q = x.Regions[0];
        Assert.Equal(RegionLabels.Question, q.Label);
        Assert.Equal(0.05f, q.X, 3); Assert.Equal(0.1f, q.Y, 3); Assert.Equal(0.9f, q.W, 3); Assert.Equal(0.2f, q.H, 3);
        Assert.Equal(["queues", "rear"], x.Tags);
        Assert.Equal(MaterialTypes.Quiz, x.MaterialType);
    }

    [Fact]
    public void Builds_or_queries_without_stop_words()
    {
        Assert.Equal("past | paper | queue | rear | counting | ds", SearchService.OrTsQuery("past paper questions on queue rear counting in DS"));
        Assert.Equal("quiz | 3", SearchService.OrTsQuery("Quiz 3 from last year"));
        Assert.Null(SearchService.OrTsQuery("the of and"));
    }
}

public class TypeWordTests
{
    [Theory]
    [InlineData("topic examples", null)]
    [InlineData("the latest syllabus", null)]
    [InlineData("midterm 1", "past_paper")]
    [InlineData("quiz 3 revision", "quiz")]
    [InlineData("lab 2 sheet", "lab")]
    [InlineData("practice exercises #7", "exercise")]
    public void Matches_type_words_as_words(string text, string? type) => Assert.Equal(type, Heuristics.MaterialType(text));
}
