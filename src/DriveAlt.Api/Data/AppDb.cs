using Microsoft.EntityFrameworkCore;

namespace DriveAlt.Api.Data;

public sealed class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public const int EmbeddingDimensions = 768;

    public DbSet<User> Users => Set<User>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<DocFile> Files => Set<DocFile>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("vector");

        // Guid ids are assigned in code (new() => Guid.NewGuid()), so EF must treat unknown ones as inserts.
        foreach (var entity in b.Model.GetEntityTypes())
        {
            var key = entity.FindPrimaryKey()?.Properties.SingleOrDefault();
            if (key?.ClrType == typeof(Guid)) b.Entity(entity.ClrType).Property(key.Name).ValueGeneratedNever();
        }

        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.UniId).IsUnique();
        });

        b.Entity<OtpCode>(e => e.HasIndex(x => new { x.UniId, x.CreatedAt }));

        b.Entity<Course>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
        });

        b.Entity<DocFile>(e =>
        {
            e.ToTable("files");
            e.HasIndex(x => x.Sha256).IsUnique();
            e.HasIndex(x => x.SourceRef);
            e.HasIndex(x => new { x.CourseId, x.MaterialType });
            e.HasIndex(x => x.UploadedById);
            e.HasOne(x => x.Course).WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.UploadedBy).WithMany().HasForeignKey(x => x.UploadedById).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Pages).WithOne(x => x.File).HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Page>(e =>
        {
            e.HasIndex(x => new { x.FileId, x.Number }).IsUnique();
            e.Property(x => x.RawExtraction).HasColumnType("jsonb");
            e.HasGeneratedTsVectorColumn(x => x.Search!, "english", x => new { x.SearchText })
                .HasIndex(x => x.Search).HasMethod("GIN");
            e.Property(x => x.Embedding).HasColumnType($"vector({EmbeddingDimensions})");
            e.HasIndex(x => x.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
            e.HasMany(x => x.Regions).WithOne(x => x.Page).HasForeignKey(x => x.PageId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Region>(e =>
        {
            e.HasIndex(x => x.PageId);
            e.HasGeneratedTsVectorColumn(x => x.Search!, "english", x => new { x.SearchText })
                .HasIndex(x => x.Search).HasMethod("GIN");
            e.Property(x => x.Embedding).HasColumnType($"vector({EmbeddingDimensions})");
            e.HasIndex(x => x.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
        });

        b.Entity<Job>(e =>
        {
            e.HasIndex(x => new { x.State, x.RunAfter });
            e.HasIndex(x => x.FileId);
        });

        b.Entity<Conversation>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.UpdatedAt });
            e.HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Message>(e => e.HasIndex(x => new { x.ConversationId, x.CreatedAt }));

        b.Entity<Bookmark>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.PageId, x.RegionId }).IsUnique().AreNullsDistinct(false);
            e.HasOne(x => x.Page).WithMany().HasForeignKey(x => x.PageId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Region).WithMany().HasForeignKey(x => x.RegionId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
