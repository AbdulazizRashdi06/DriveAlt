using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace DriveAlt.Api.Data;

public static class Seeder
{
    private sealed record SeedFile(List<SeedCourse> Courses);
    private sealed record SeedCourse(string Slug, string Name, int Semester, string Group, string[] Aliases, string Color);

    /// <summary>Upserts the Year 2 course tree (mirrors the batch Google Drive). Existing courses keep their id.</summary>
    public static async Task SeedCoursesAsync(AppDb db, CancellationToken ct = default)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "Seed", "year2.json");
        var seed = JsonSerializer.Deserialize<SeedFile>(await File.ReadAllTextAsync(path, ct), JsonDefaults.Options)!;
        var existing = await db.Courses.ToDictionaryAsync(c => c.Slug, ct);
        var sort = 0;
        foreach (var c in seed.Courses)
        {
            if (!existing.TryGetValue(c.Slug, out var course))
            {
                course = new Course { Slug = c.Slug, Name = c.Name };
                db.Courses.Add(course);
            }
            course.Name = c.Name;
            course.Semester = c.Semester;
            course.Group = c.Group;
            course.Aliases = c.Aliases;
            course.Color = c.Color;
            course.Sort = sort++;
        }
        await db.SaveChangesAsync(ct);
    }
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
