using System.IO;
using Microsoft.EntityFrameworkCore;
using WoodInventory.Domain;

namespace WoodInventory.Data;

/// <summary>
/// EF Core DbContext cấu hình cho SQLite cục bộ (file trong %APPDATA%\WoodInventory).
/// </summary>
public class AppDbContext : DbContext
{
    /// <summary>Đường dẫn file DB — nằm trong thư mục ghi được của người dùng.</summary>
    public static string DbPath { get; } = InitDbPath();

    private static string InitDbPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WoodInventory");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "woodinventory.db");
    }

    public DbSet<WoodCategory> WoodCategories { get; set; }
    public DbSet<WoodSubCategory> WoodSubCategories { get; set; }
    public DbSet<WoodLot> WoodLots { get; set; }
    public DbSet<Supplier> Suppliers { get; set; }
    public DbSet<WoodQuotation> WoodQuotations { get; set; }
    public DbSet<QuotationItem> QuotationItems { get; set; }
    public DbSet<WarehouseReceipt> WarehouseReceipts { get; set; }
    public DbSet<WarehouseIssue> WarehouseIssues { get; set; }
    public DbSet<WarehouseIssueItem> WarehouseIssueItems { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<AppSettings> Settings { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={DbPath}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<WoodCategory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(50);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.VolumeRule).HasConversion<int>();
            entity.Ignore(e => e.VolumeRuleLabel);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<WoodSubCategory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(50);
            entity.Property(e => e.CategoryId).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => new { e.CategoryId, e.Name }).IsUnique();
            entity.HasOne<WoodCategory>().WithMany()
                  .HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WoodLot>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(50);
            entity.Property(e => e.WoodType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ReceiptId).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => e.WoodType);
            entity.HasIndex(e => e.SupplierId);
            entity.HasIndex(e => e.ReceiptId);
            entity.Property(e => e.Price).HasPrecision(18, 2);
            entity.Property(e => e.PriceCurrency).HasMaxLength(3);
            entity.Property(e => e.ExchangeRate).HasPrecision(18, 2);
            entity.Property(e => e.TaxPercent).HasPrecision(5, 2);
            entity.Property(e => e.CostPriceVnd).HasPrecision(18, 2);
            entity.Property(e => e.TotalValueVnd).HasPrecision(18, 2);
            entity.HasOne(e => e.Supplier).WithMany()
                  .HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(250);
            entity.Property(e => e.TaxCode).HasMaxLength(50);
            entity.Property(e => e.Address).HasMaxLength(400);
            entity.Property(e => e.Phone).HasMaxLength(50);
            entity.Property(e => e.BankAccount).HasMaxLength(50);
            entity.HasIndex(e => e.Code).IsUnique();
        });

        modelBuilder.Entity<WoodQuotation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Version).IsRequired().HasMaxLength(20);
            entity.HasIndex(e => new { e.SupplierId, e.Version }).IsUnique();
            entity.HasMany(e => e.Items).WithOne()
                  .HasForeignKey(e => e.QuotationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QuotationItem>(entity =>
        {
            entity.Property(e => e.Price).HasPrecision(18, 2);
            entity.Property(e => e.PriceCurrency).HasMaxLength(3);
        });

        modelBuilder.Entity<WarehouseReceipt>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasMany(e => e.Lots).WithOne()
                  .HasForeignKey(e => e.ReceiptId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WarehouseIssue>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasMany(e => e.Items).WithOne()
                  .HasForeignKey(e => e.WarehouseIssueId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Order>(entity => entity.HasKey(e => e.Id));

        modelBuilder.Entity<AppSettings>(entity =>
        {
            entity.ToTable("AppSettings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DefaultExchangeRate).HasPrecision(18, 2);
            entity.Property(e => e.DefaultTaxPercent).HasPrecision(5, 2);
        });

        modelBuilder.Entity<WarehouseIssueItem>(entity =>
        {
            entity.HasKey(e => new { e.WarehouseIssueId, e.WoodLotId });
            entity.Property(e => e.CostPriceVnd).HasPrecision(18, 2);
            entity.HasOne(e => e.WoodLot).WithMany()
                  .HasForeignKey(e => e.WoodLotId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
