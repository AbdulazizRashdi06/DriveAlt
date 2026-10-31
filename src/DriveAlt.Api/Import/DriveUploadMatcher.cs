using DriveAlt.Api.Data;
using DriveAlt.Api.Files;
using Microsoft.EntityFrameworkCore;

namespace DriveAlt.Api.Import;

/// <summary>
/// Finds the app file a Drive file became: by its Drive ID (sync records or the original import's source_ref), else
/// by the same name in the folder's course, however it got there. Built once per request.
/// </summary>
public sealed class DriveUploadMatcher
{
    public sealed record AppFile(Guid Id, int? CourseId, string Status);

    private readonly Dictionary<string, Guid> _byDriveId;
    private readonly Dictionary<Guid, AppFile> _files;
    private readonly List<(AppFile File, string Title, string Original)> _names;
    private readonly IReadOnlyList<Course> _courses;
    private readonly Dictionary<string, int?> _folderCourse = [];
    private readonly Dictionary<int, Dictionary<string, Guid>> _byName = [];

    private DriveUploadMatcher(Dictionary<string, Guid> byDriveId, List<(AppFile, string, string)> names, IReadOnlyList<Course> courses)
    {
        _byDriveId = byDriveId;
        _names = names;
        _files = names.ToDictionary(n => n.Item1.Id, n => n.Item1);
        _courses = courses;
    }

    public static async Task<DriveUploadMatcher> LoadAsync(AppDb db, CancellationToken ct)
    {
        var byDriveId = await db.DriveSyncItems.AsNoTracking().GroupBy(i => i.Id).Select(g => new { g.Key, FileId = g.First().FileId })
            .ToDictionaryAsync(i => i.Key, i => i.FileId, ct);
        // Files from the original Drive import are only recorded in source_ref ("gdrive:id1+id2").
        foreach (var f in await db.Files.AsNoTracking().Where(f => f.SourceRef != null && f.SourceRef.StartsWith("gdrive:"))
                     .Select(f => new { f.Id, f.SourceRef }).ToListAsync(ct))
            foreach (var driveId in f.SourceRef![7..].Split('+'))
                byDriveId.TryAdd(driveId, f.Id);
        var files = await db.Files.AsNoTracking().OrderByDescending(f => f.CreatedAt)
            .Select(f => new { f.Id, f.CourseId, f.Status, f.Title, f.OriginalName }).ToListAsync(ct);
        var courses = await db.Courses.AsNoTracking().ToListAsync(ct);
        return new DriveUploadMatcher(byDriveId,
            files.Select(f => (new AppFile(f.Id, f.CourseId, f.Status), Heuristics.CanonicalTitle(f.Title), Heuristics.CanonicalTitle(f.OriginalName))).ToList(),
            courses);
    }

    /// <summary>The app file for a Drive file in a folder, or null when it isn't in the app.</summary>
    public AppFile? Find(string[] folderSegments, string driveId, string name)
    {
        // A Drive ID whose app file was deleted since counts as not uploaded.
        if (_byDriveId.TryGetValue(driveId, out var id) && _files.TryGetValue(id, out var byId)) return byId;
        var key = Heuristics.CanonicalTitle(name);
        if (key.Length == 0) return null;
        var path = string.Join('/', folderSegments);
        if (!_folderCourse.TryGetValue(path, out var courseId))
            _folderCourse[path] = courseId = Heuristics.FromPath(path, _courses).Course?.Id;
        // Same rule as the upload duplicate check: same canonical name, in the folder's course (any course if unknown).
        var bucket = courseId ?? -1;
        if (!_byName.TryGetValue(bucket, out var names))
        {
            names = [];
            foreach (var (file, title, original) in _names.Where(n => courseId is null || n.File.CourseId == courseId))
                foreach (var n in new[] { title, original })
                    if (n.Length > 0) names.TryAdd(n, file.Id);
            _byName[bucket] = names;
        }
        return names.TryGetValue(key, out var match) ? _files[match] : null;
    }
}
