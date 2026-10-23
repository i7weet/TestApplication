using Microsoft.EntityFrameworkCore;

namespace TestApp;

public class TestDbContext : DbContext
{
    public DbSet<TaskItem> TaskItems => Set<TaskItem>();
    
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var task = modelBuilder.Entity<TaskItem>();

        task.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(200);

        task.Property(x => x.RowVersion)
            .IsRowVersion();
    }
}