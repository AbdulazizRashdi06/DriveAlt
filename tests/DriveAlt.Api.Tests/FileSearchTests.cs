using DriveAlt.Api.Data;
using DriveAlt.Api.Search;

namespace DriveAlt.Api.Tests;

public class FileSearchTests
{
    [Theory]
    [InlineData("ok i want full ds past papers 2026", "")]
    [InlineData("can you send the whole Data Structures PDF", "")]
    [InlineData("DS pastpapers about circular queues", "circular queues")]
    [InlineData("DS chapter 3 slides", "chapter 3")]
    public void File_search_keeps_topic_and_chapter_numbers_but_not_request_words(string request, string topic)
    {
        Course[] courses = [new() { Id = 1, Slug = "data-structures", Name = "Data Structures", Aliases = ["DS"] }];
        Assert.Equal(topic, SearchService.FileTopic(request, courses));
    }
}
