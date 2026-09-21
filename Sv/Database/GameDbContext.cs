using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sv.Utility;

namespace Sv.Database;

public sealed class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options)
{
    /// <summary>SQLite 里 DateTimeOffset 的存法（与 EF 默认格式一致），一律带配置时区偏移。</summary>
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss.fffffffzzz";

    /// <summary>写库前换算到配置时区，读库后再换算一次：库里偏移稳定，换时区配置也能正确还原。</summary>
    private static readonly ValueConverter<DateTimeOffset, string> TimeZoneConverter = new(
        value => Time.InTimeZone(value).ToString(TimeFormat, CultureInfo.InvariantCulture),
        text => Time.InTimeZone(DateTimeOffset.Parse(text, CultureInfo.InvariantCulture)));

    public DbSet<PlayerEntity> Players => Set<PlayerEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlayerEntity>(entity =>
        {
            entity.ToTable("Players");
            entity.HasKey(value => value.Uid);
            entity.Property(value => value.Uid).ValueGeneratedOnAdd();
            entity.HasIndex(nameof(PlayerEntity.Name), nameof(PlayerEntity.ServerId)).IsUnique();
            entity.Property(value => value.Name).HasMaxLength(64);
            entity.Property(value => value.CreatedAt).HasConversion(TimeZoneConverter);
            entity.Property(value => value.UpdatedAt).HasConversion(TimeZoneConverter);
        });
    }
}
