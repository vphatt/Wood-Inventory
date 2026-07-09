using Microsoft.EntityFrameworkCore;
using WoodInventory.Domain;

namespace WoodInventory.Data;

/// <summary>
/// Khởi tạo cơ sở dữ liệu và nạp dữ liệu mẫu ngành gỗ.
/// </summary>
public static class DbSeeder
{
    public static void Seed(AppDbContext context)
    {
        context.Database.EnsureCreated();

        // Nâng cấp nhẹ cho DB cũ: EnsureCreated không thêm bảng/cột mới vào DB đã tồn tại,
        // nên tự tạo bảng danh mục loại gỗ + thêm cột NCC nếu thiếu (không dùng Migrations).
        EnsureWoodCategoriesTable(context);
        SeedWoodCategories(context);
        EnsureWoodSubCategoriesTable(context);
        EnsureWoodSubTypeColumns(context);
        SeedWoodSubCategories(context);
        EnsureSupplierColumns(context);
        EnsureQuotationItemColumns(context);
        EnsureWoodLotColumns(context);
        MergeQuotationsPerSupplier(context);
        EnsureAppSettingsTable(context);
        SeedAppSettings(context);

        if (context.Suppliers.Any())
            return; // Đã có dữ liệu

        // 1. Nhà cung cấp
        var suppliers = new List<Supplier>
        {
            new() { Id = "SUP-001", Code = "NAH", Name = "North American Hardwoods Inc.", TaxCode = "US-5540192", Phone = "+1-555-0192", Address = "102 Dunlap St, Memphis, TN, USA", BankAccount = "0011-2233-4455" },
            new() { Id = "SUP-002", Code = "HPL", Name = "Công ty Cổ phần Lâm nghiệp Hòa Phát", TaxCode = "2400398211", Phone = "024-3982-1144", Address = "KCN Phùng Chi Lăng, Lạng Sơn, Việt Nam", BankAccount = "1902-8899-0011" },
            new() { Id = "SUP-003", Code = "GTH", Name = "Gia Thanh Wood Trading Corp", TaxCode = "0312844998", Phone = "028-3844-9988", Address = "45 Đường số 2, P. Thảo Điền, TP. Thủ Đức, TP.HCM", BankAccount = "0601-3344-5566" }
        };
        context.Suppliers.AddRange(suppliers);
        context.SaveChanges();

        // 2. Báo giá — mỗi NCC đúng 1 danh sách (không phiên bản)
        var quotations = new List<WoodQuotation>
        {
            new()
            {
                Id = "QT-001", SupplierId = "SUP-001", EffectiveDate = DateTime.Parse("2026-05-15"),
                Version = "", IsActive = true,
                Items =
                {
                    // Gỗ Dương: chỉ cần Grade + Thickness là biết giá — không giới hạn Rộng/Dài/Xuất xứ.
                    new QuotationItem { Id = "QI-101", WoodType = "Gỗ Dương", ThicknessMin = 25, ThicknessMax = 25, Grade = "FAS", PriceUsd = 680 },
                    // Gỗ Sồi: đủ Dày (giá trị đơn) + Rộng từ 150mm trở lên (range mở, không giới hạn trên).
                    new QuotationItem { Id = "QI-102", WoodType = "Gỗ Sồi", ThicknessMin = 26, ThicknessMax = 26, WidthMin = 150, Grade = "FAS", PriceUsd = 1150 },
                    new QuotationItem { Id = "QI-103", WoodType = "Gỗ Sồi", ThicknessMin = 38, ThicknessMax = 38, WidthMin = 150, Grade = "FAS", PriceUsd = 1250 },
                    // Gỗ Tần Bì: cần thêm Xuất xứ để phân biệt giá.
                    new QuotationItem { Id = "QI-104", WoodType = "Gỗ Tần Bì", ThicknessMin = 32, ThicknessMax = 32, WidthMin = 120, Grade = "1C", Origin = "Mỹ", PriceUsd = 850 }
                }
            },
            new()
            {
                Id = "QT-002", SupplierId = "SUP-002", EffectiveDate = DateTime.Parse("2026-01-15"),
                Version = "", IsActive = true,
                Items =
                {
                    new QuotationItem { Id = "QI-201", WoodType = "Gỗ Sồi", ThicknessMin = 26, ThicknessMax = 26, WidthMin = 120, Grade = "AB", PriceUsd = 1000 },
                    // Gỗ Thông: chỉ cần độ dày — không set Grade/Rộng/Dài/Xuất xứ.
                    new QuotationItem { Id = "QI-202", WoodType = "Gỗ Thông", ThicknessMin = 20, ThicknessMax = 20, PriceUsd = 420 },
                    new QuotationItem { Id = "QI-203", WoodType = "Gỗ Cao Su", ThicknessMin = 18, ThicknessMax = 18, Grade = "AA", Specification = "Standard Joint", PriceUsd = 380 }
                }
            }
        };
        foreach (var q in quotations)
            foreach (var it in q.Items)
                it.UpdatedAt = q.EffectiveDate;   // mục giá seed: mốc "tạo" = ngày áp dụng báo giá
        context.WoodQuotations.AddRange(quotations);
        context.SaveChanges();

        // 3. Đơn hàng
        context.Orders.AddRange(
            new Order { Id = "ORD-26001", CustomerName = "Nội thất Minh Mỹ (Bàn ghế ăn)", Date = DateTime.Parse("2026-06-20"), Status = "processing" },
            new Order { Id = "ORD-26002", CustomerName = "Mộc Mỹ Nghệ Việt (Tủ bếp cao cấp)", Date = DateTime.Parse("2026-06-22"), Status = "completed" },
            new Order { Id = "ORD-26003", CustomerName = "Ngoại Thất Xuất Khẩu Âu Việt", Date = DateTime.Parse("2026-06-25"), Status = "pending" });
        context.SaveChanges();

        // 4. Phiếu nhập
        context.WarehouseReceipts.AddRange(
            new WarehouseReceipt { Id = "REC-26001", SupplierId = "SUP-001", Date = DateTime.Parse("2026-05-20"), Invoice = "INV-7721A", PackingList = "PL-7721A", Status = "completed" },
            new WarehouseReceipt { Id = "REC-26002", SupplierId = "SUP-002", Date = DateTime.Parse("2026-06-02"), Invoice = "INV-HP-9901", PackingList = "PL-HP-9901", Status = "completed" },
            new WarehouseReceipt { Id = "REC-26003", SupplierId = "SUP-003", Date = DateTime.Parse("2026-06-10"), Invoice = "INV-GT-550", PackingList = "PL-GT-550", Status = "completed" });
        context.SaveChanges();

        // 5. Kiện gỗ
        static WoodLot Configure(WoodLot lot)
        {
            lot.Cbm = WoodVolumeCalculator.CalculateVolume(lot.WoodType, lot.ThicknessMm, lot.WidthMm, lot.LengthMm, lot.OriginalQuantity, lot.Footage);
            lot.RemainingCbm = lot.Cbm;
            lot.CostPriceVnd = WoodVolumeCalculator.CalculateCostPricePerM3(lot.PriceUsd, lot.ExchangeRate, lot.TaxPercent);
            lot.TotalValueVnd = WoodVolumeCalculator.CalculateTotalValue(lot.CostPriceVnd, lot.Cbm);
            return lot;
        }

        var lot2 = Configure(new WoodLot
        {
            Id = "LOT-2601B", SupplierId = "SUP-001", ImportDate = DateTime.Parse("2026-05-20"),
            ReceiptId = "REC-26001", Invoice = "INV-7721A", PackingList = "PL-7721A",
            WoodType = "Gỗ Sồi", WoodName = "Gỗ Sồi Mỹ FAS 26mm (Red Oak)",
            ThicknessMm = 26, WidthMm = 150, LengthMm = 2400,
            OriginalQuantity = 180, Quantity = 180, Footage = 0,
            PriceUsd = 1150, ExchangeRate = 25450, TaxPercent = 10, Grade = "FAS"
        });
        var lot3 = Configure(new WoodLot
        {
            Id = "LOT-2602A", SupplierId = "SUP-002", ImportDate = DateTime.Parse("2026-06-02"),
            ReceiptId = "REC-26002", Invoice = "INV-HP-9901", PackingList = "PL-HP-9901",
            WoodType = "Gỗ Sồi", WoodName = "Gỗ Sồi AB 26mm",
            ThicknessMm = 26, WidthMm = 120, LengthMm = 2000,
            OriginalQuantity = 300, Quantity = 300, Footage = 0,
            PriceUsd = 1000, ExchangeRate = 25400, TaxPercent = 5, Grade = "AB"
        });

        context.WoodLots.AddRange(
            Configure(new WoodLot
            {
                Id = "LOT-2601A", SupplierId = "SUP-001", ImportDate = DateTime.Parse("2026-05-20"),
                ReceiptId = "REC-26001", Invoice = "INV-7721A", PackingList = "PL-7721A",
                WoodType = "Gỗ Dương", WoodName = "Gỗ Dương FAS 25mm (Poplar)",
                ThicknessMm = 25, WidthMm = 0, LengthMm = 0, LengthNote = "96\"108\"120\"",
                OriginalQuantity = 120, Quantity = 120, Footage = 2450,
                PriceUsd = 680, ExchangeRate = 25450, TaxPercent = 10, Grade = "FAS"
            }),
            lot2, lot3,
            Configure(new WoodLot
            {
                Id = "LOT-2603A", SupplierId = "SUP-003", ImportDate = DateTime.Parse("2026-06-10"),
                ReceiptId = "REC-26003", Invoice = "INV-GT-550", PackingList = "PL-GT-550",
                WoodType = "Gỗ Tràm", WoodName = "Gỗ Tràm ghép thanh 18mm",
                ThicknessMm = 18, WidthMm = 1220, LengthMm = 2440,
                OriginalQuantity = 400, Quantity = 400, Footage = 0,
                PriceUsd = 350, ExchangeRate = 25420, TaxPercent = 10, Grade = "AA"
            }));
        context.SaveChanges();

        // 6. Phiếu xuất
        var issues = new List<WarehouseIssue>
        {
            new()
            {
                Id = "ISS-26001", OrderId = "ORD-26001", Date = DateTime.Parse("2026-06-21"),
                Items = { new WarehouseIssueItem { WoodLotId = "LOT-2601B", Quantity = 50, Cbm = Math.Round(50 * 26 * 150 * 2400 / 1_000_000_000.0, 4), CostPriceVnd = lot2.CostPriceVnd } }
            },
            new()
            {
                Id = "ISS-26002", OrderId = "ORD-26002", Date = DateTime.Parse("2026-06-23"),
                Items = { new WarehouseIssueItem { WoodLotId = "LOT-2602A", Quantity = 100, Cbm = Math.Round(100 * 26 * 120 * 2000 / 1_000_000_000.0, 4), CostPriceVnd = lot3.CostPriceVnd } }
            }
        };
        context.WarehouseIssues.AddRange(issues);
        context.SaveChanges();

        // Khấu trừ tồn kho theo phiếu xuất mẫu
        foreach (var issue in issues)
            foreach (var item in issue.Items)
                context.WoodLots.Find(item.WoodLotId)?.IssueInventory(item.Quantity, item.Cbm);
        context.SaveChanges();
    }

    /// <summary>Tạo bảng WoodCategories nếu DB (cũ) chưa có — vì đang dùng EnsureCreated, không Migrations.</summary>
    private static void EnsureWoodCategoriesTable(AppDbContext context)
    {
        context.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "WoodCategories" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_WoodCategories" PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "VolumeRule" INTEGER NOT NULL
            );
            """);
        context.Database.ExecuteSqlRaw(
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_WoodCategories_Name" ON "WoodCategories" ("Name");""");
    }

    /// <summary>Tạo bảng AppSettings (cài đặt chung, luôn đúng 1 dòng) cho DB cũ nếu chưa có.</summary>
    private static void EnsureAppSettingsTable(AppDbContext context)
    {
        context.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "AppSettings" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_AppSettings" PRIMARY KEY,
                "CompanyName" TEXT NULL,
                "CompanyTaxCode" TEXT NULL,
                "CompanyAddress" TEXT NULL,
                "CompanyPhone" TEXT NULL,
                "DefaultExchangeRate" TEXT NOT NULL,
                "DefaultTaxPercent" TEXT NOT NULL,
                "DefaultVolumeDecimals" INTEGER NOT NULL,
                "LowStockThreshold" INTEGER NOT NULL
            );
            """);
    }

    /// <summary>Mồi đúng 1 dòng cài đặt mặc định (chỉ khi bảng rỗng) — khớp các giá trị hardcode cũ.</summary>
    private static void SeedAppSettings(AppDbContext context)
    {
        if (context.Settings.Any()) return;

        context.Settings.Add(new AppSettings
        {
            Id = "default",
            CompanyName = "CÔNG TY TNHH MTV HƯNG DIỆU",
            CompanyTaxCode = null,
            CompanyAddress = null,
            CompanyPhone = null,
            DefaultExchangeRate = 25400,
            DefaultTaxPercent = 10,
            DefaultVolumeDecimals = 5,
            LowStockThreshold = 30
        });
        context.SaveChanges();
    }

    /// <summary>Tạo bảng WoodSubCategories (phân loại con) cho DB cũ nếu chưa có.</summary>
    private static void EnsureWoodSubCategoriesTable(AppDbContext context)
    {
        context.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "WoodSubCategories" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_WoodSubCategories" PRIMARY KEY,
                "CategoryId" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                CONSTRAINT "FK_WoodSubCategories_WoodCategories_CategoryId"
                    FOREIGN KEY ("CategoryId") REFERENCES "WoodCategories" ("Id") ON DELETE CASCADE
            );
            """);
        context.Database.ExecuteSqlRaw(
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_WoodSubCategories_CategoryId_Name" ON "WoodSubCategories" ("CategoryId", "Name");""");
    }

    /// <summary>Thêm cột WoodSubType (phân loại con) vào WoodLots + QuotationItems cũ nếu thiếu.</summary>
    private static void EnsureWoodSubTypeColumns(AppDbContext context)
    {
        AddColumnIfMissing(context, "WoodLots", "WoodSubType", "TEXT");
        AddColumnIfMissing(context, "QuotationItems", "WoodSubType", "TEXT");
    }

    /// <summary>Thêm 1 cột vào bảng nếu chưa có (SQLite không hỗ trợ ADD COLUMN IF NOT EXISTS).</summary>
    private static void AddColumnIfMissing(AppDbContext context, string table, string column, string sqlType)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conn = context.Database.GetDbConnection();
        var openedHere = conn.State != System.Data.ConnectionState.Open;
        if (openedHere) conn.Open();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info('{table}');";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                existing.Add(reader.GetString(1)); // cột index 1 = tên cột
        }
        finally
        {
            if (openedHere) conn.Close();
        }

        if (existing.Count > 0 && !existing.Contains(column))
            // table/column/sqlType là hằng số nội bộ (không phải input người dùng) nên an toàn.
#pragma warning disable EF1002
            context.Database.ExecuteSqlRaw($"""ALTER TABLE "{table}" ADD COLUMN "{column}" {sqlType};""");
#pragma warning restore EF1002
    }

    /// <summary>Seed vài phân loại con mẫu (chỉ khi bảng rỗng).</summary>
    private static void SeedWoodSubCategories(AppDbContext context)
    {
        if (context.WoodSubCategories.Any()) return;

        context.WoodSubCategories.AddRange(
            // Gỗ Thông
            new WoodSubCategory { Id = "SUB-001", CategoryId = "CAT-004", Name = "Thông trắng" },
            new WoodSubCategory { Id = "SUB-002", CategoryId = "CAT-004", Name = "Thông vàng" },
            // Gỗ Dương
            new WoodSubCategory { Id = "SUB-003", CategoryId = "CAT-002", Name = "1 com" },
            new WoodSubCategory { Id = "SUB-004", CategoryId = "CAT-002", Name = "2 com" },
            // Gỗ Sồi
            new WoodSubCategory { Id = "SUB-005", CategoryId = "CAT-001", Name = "Sồi trắng" },
            new WoodSubCategory { Id = "SUB-006", CategoryId = "CAT-001", Name = "Sồi đỏ" });
        context.SaveChanges();
    }

    /// <summary>Gộp báo giá về 1/NCC cho DB cũ (từng chia phiên bản): giữ bản active/mới nhất, xóa phần thừa.</summary>
    private static void MergeQuotationsPerSupplier(AppDbContext context)
    {
        var dupSuppliers = context.WoodQuotations
            .GroupBy(q => q.SupplierId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (dupSuppliers.Count == 0) return;

        foreach (var sup in dupSuppliers)
        {
            var list = context.WoodQuotations.Where(q => q.SupplierId == sup).ToList();
            var keep = list.FirstOrDefault(q => q.IsActive) ?? list.OrderByDescending(q => q.EffectiveDate).First();
            foreach (var q in list.Where(q => q.Id != keep.Id))
                context.WoodQuotations.Remove(q);   // cascade xóa Items của bản thừa
        }
        context.SaveChanges();
    }

    /// <summary>Thêm cột TaxCode/BankAccount vào bảng Suppliers cũ nếu thiếu (SQLite không có ADD COLUMN IF NOT EXISTS).</summary>
    private static void EnsureSupplierColumns(AppDbContext context)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conn = context.Database.GetDbConnection();
        var openedHere = conn.State != System.Data.ConnectionState.Open;
        if (openedHere) conn.Open();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA table_info('Suppliers');";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                existing.Add(reader.GetString(1)); // cột index 1 = tên cột
        }
        finally
        {
            if (openedHere) conn.Close();
        }

        if (existing.Count > 0 && !existing.Contains("TaxCode"))
            context.Database.ExecuteSqlRaw("""ALTER TABLE "Suppliers" ADD COLUMN "TaxCode" TEXT;""");
        if (existing.Count > 0 && !existing.Contains("BankAccount"))
            context.Database.ExecuteSqlRaw("""ALTER TABLE "Suppliers" ADD COLUMN "BankAccount" TEXT;""");
    }

    /// <summary>
    /// Thêm các cột range/optional mới vào bảng QuotationItems cũ nếu thiếu (SQLite không có
    /// ADD COLUMN IF NOT EXISTS). Nếu cột "Thickness" (giá trị đơn) cũ còn tồn tại, backfill
    /// sang ThicknessMin/Max để giữ đúng ngữ nghĩa dữ liệu cũ (giá trị đơn = khoảng đóng bằng chính nó).
    /// </summary>
    private static void EnsureQuotationItemColumns(AppDbContext context)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conn = context.Database.GetDbConnection();
        var openedHere = conn.State != System.Data.ConnectionState.Open;
        if (openedHere) conn.Open();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA table_info('QuotationItems');";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                existing.Add(reader.GetString(1)); // cột index 1 = tên cột
        }
        finally
        {
            if (openedHere) conn.Close();
        }

        if (existing.Count == 0) return; // bảng chưa tồn tại (DB mới) — EnsureCreated đã lo đủ cột

        if (!existing.Contains("ThicknessMin"))
            context.Database.ExecuteSqlRaw("""ALTER TABLE "QuotationItems" ADD COLUMN "ThicknessMin" REAL;""");
        if (!existing.Contains("ThicknessMax"))
            context.Database.ExecuteSqlRaw("""ALTER TABLE "QuotationItems" ADD COLUMN "ThicknessMax" REAL;""");
        if (!existing.Contains("WidthMin"))
            context.Database.ExecuteSqlRaw("""ALTER TABLE "QuotationItems" ADD COLUMN "WidthMin" REAL;""");
        if (!existing.Contains("WidthMax"))
            context.Database.ExecuteSqlRaw("""ALTER TABLE "QuotationItems" ADD COLUMN "WidthMax" REAL;""");
        if (!existing.Contains("LengthMin"))
            context.Database.ExecuteSqlRaw("""ALTER TABLE "QuotationItems" ADD COLUMN "LengthMin" REAL;""");
        if (!existing.Contains("LengthMax"))
            context.Database.ExecuteSqlRaw("""ALTER TABLE "QuotationItems" ADD COLUMN "LengthMax" REAL;""");
        if (!existing.Contains("Origin"))
            context.Database.ExecuteSqlRaw("""ALTER TABLE "QuotationItems" ADD COLUMN "Origin" TEXT;""");
        if (!existing.Contains("ThicknessMinNote"))
            context.Database.ExecuteSqlRaw("""ALTER TABLE "QuotationItems" ADD COLUMN "ThicknessMinNote" TEXT;""");
        if (!existing.Contains("ThicknessMaxNote"))
            context.Database.ExecuteSqlRaw("""ALTER TABLE "QuotationItems" ADD COLUMN "ThicknessMaxNote" TEXT;""");
        if (!existing.Contains("UpdatedAt"))
        {
            context.Database.ExecuteSqlRaw("""ALTER TABLE "QuotationItems" ADD COLUMN "UpdatedAt" TEXT;""");
            // Dòng cũ: lấy ngày áp dụng của báo giá làm "thời điểm tạo" ban đầu.
            context.Database.ExecuteSqlRaw(
                """UPDATE "QuotationItems" SET "UpdatedAt" = (SELECT "EffectiveDate" FROM "WoodQuotations" WHERE "WoodQuotations"."Id" = "QuotationItems"."QuotationId") WHERE "UpdatedAt" IS NULL;""");
        }

        if (existing.Contains("Thickness"))
        {
            context.Database.ExecuteSqlRaw(
                """UPDATE "QuotationItems" SET "ThicknessMin" = "Thickness", "ThicknessMax" = "Thickness" WHERE "ThicknessMin" IS NULL AND "Thickness" IS NOT NULL;""");
            // Cột "Thickness" cũ (single value, REAL NOT NULL) không còn dùng nhưng vẫn NOT NULL →
            // mọi INSERT mục giá mới sẽ vỡ ràng buộc. Bỏ hẳn cột (SQLite 3.35+, bundle của EF hỗ trợ DROP COLUMN).
            try { context.Database.ExecuteSqlRaw("""ALTER TABLE "QuotationItems" DROP COLUMN "Thickness";"""); }
            catch { /* DB quá cũ không hỗ trợ DROP COLUMN — bỏ qua, khôi phục thủ công nếu cần */ }
        }
    }

    /// <summary>Thêm cột LengthNote vào bảng WoodLots cũ nếu thiếu (SQLite không có ADD COLUMN IF NOT EXISTS).</summary>
    private static void EnsureWoodLotColumns(AppDbContext context)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conn = context.Database.GetDbConnection();
        var openedHere = conn.State != System.Data.ConnectionState.Open;
        if (openedHere) conn.Open();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA table_info('WoodLots');";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                existing.Add(reader.GetString(1)); // cột index 1 = tên cột
        }
        finally
        {
            if (openedHere) conn.Close();
        }

        if (existing.Count > 0 && !existing.Contains("LengthNote"))
            context.Database.ExecuteSqlRaw("""ALTER TABLE "WoodLots" ADD COLUMN "LengthNote" TEXT;""");
        if (existing.Count > 0 && !existing.Contains("ThicknessNote"))
            context.Database.ExecuteSqlRaw("""ALTER TABLE "WoodLots" ADD COLUMN "ThicknessNote" TEXT;""");
        if (existing.Count > 0 && !existing.Contains("Origin"))
            context.Database.ExecuteSqlRaw("""ALTER TABLE "WoodLots" ADD COLUMN "Origin" TEXT;""");
        if (existing.Count > 0 && !existing.Contains("DeliveryNote"))
            context.Database.ExecuteSqlRaw("""ALTER TABLE "WoodLots" ADD COLUMN "DeliveryNote" TEXT;""");
        if (existing.Count > 0 && !existing.Contains("VolumeDecimals"))
            context.Database.ExecuteSqlRaw("""ALTER TABLE "WoodLots" ADD COLUMN "VolumeDecimals" INTEGER;""");
        if (existing.Count > 0 && !existing.Contains("VolumeAdjustment"))
            context.Database.ExecuteSqlRaw("""ALTER TABLE "WoodLots" ADD COLUMN "VolumeAdjustment" REAL;""");
    }

    /// <summary>Seed các loại gỗ mặc định (chỉ khi bảng rỗng). Gỗ Dương tính theo Footage, còn lại theo quy cách.</summary>
    private static void SeedWoodCategories(AppDbContext context)
    {
        if (context.WoodCategories.Any()) return;

        context.WoodCategories.AddRange(
            new WoodCategory { Id = "CAT-001", Name = "Gỗ Sồi", VolumeRule = VolumeRule.BySpecification },
            new WoodCategory { Id = "CAT-002", Name = "Gỗ Dương", VolumeRule = VolumeRule.ByFootage },
            new WoodCategory { Id = "CAT-003", Name = "Gỗ Tần Bì", VolumeRule = VolumeRule.BySpecification },
            new WoodCategory { Id = "CAT-004", Name = "Gỗ Thông", VolumeRule = VolumeRule.BySpecification },
            new WoodCategory { Id = "CAT-005", Name = "Gỗ Tràm", VolumeRule = VolumeRule.BySpecification },
            new WoodCategory { Id = "CAT-006", Name = "Gỗ Cao Su", VolumeRule = VolumeRule.BySpecification });
        context.SaveChanges();
    }
}
