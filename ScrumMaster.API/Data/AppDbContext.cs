using Microsoft.EntityFrameworkCore;
using ScrumMaster.API.Models;

namespace ScrumMaster.API.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Blocker> Blockers => Set<Blocker>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Blocker>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Status).HasConversion<string>();
            b.Property(x => x.Title).IsRequired().HasMaxLength(500);
            b.Property(x => x.Reporter).IsRequired().HasMaxLength(100);
        });
    }
}
