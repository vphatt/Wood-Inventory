using Microsoft.EntityFrameworkCore;
using TimberFlowDesktop.Domain;

namespace TimberFlowDesktop.Data;

/// <summary>
/// Kho dữ liệu dùng chung của ứng dụng: nạp toàn bộ từ SQLite vào bộ nhớ
/// và cung cấp các nghiệp vụ ghi (thêm kiện, lập phiếu, báo giá...).
/// Sau mỗi thao tác ghi, gọi <see cref="Reload"/> rồi phát sự kiện <see cref="Changed"/>.
/// </summary>
public static class AppState
{
    public static List<Supplier> Suppliers { get; private set; } = new();
    public static List<WoodCategory> Categories { get; private set; } = new();
    public static List<WoodSubCategory> SubCategories { get; private set; } = new();
    public static List<WoodQuotation> Quotations { get; private set; } = new();
    public static List<WoodLot> Lots { get; private set; } = new();
    public static List<WarehouseReceipt> Receipts { get; private set; } = new();
    public static List<WarehouseIssue> Issues { get; private set; } = new();
    public static List<Order> Orders { get; private set; } = new();

    /// <summary>Bắn ra sau mỗi thay đổi dữ liệu để các màn hình tự làm mới.</summary>
    public static event Action Changed;

    public static int LowStockCount => Lots.Count(l => l.Quantity <= 30 && l.Quantity > 0);

    public static void Initialize()
    {
        using var db = new AppDbContext();
        DbSeeder.Seed(db);
        Reload();
    }

    public static void Reload()
    {
        using var db = new AppDbContext();
        Categories = db.WoodCategories.AsNoTracking().OrderBy(c => c.Name).ToList();
        SubCategories = db.WoodSubCategories.AsNoTracking().OrderBy(s => s.Name).ToList();
        Suppliers = db.Suppliers.AsNoTracking().OrderBy(s => s.Id).ToList();
        Quotations = db.WoodQuotations.AsNoTracking().Include(q => q.Items)
                       .OrderByDescending(q => q.EffectiveDate).ToList();
        Lots = db.WoodLots.AsNoTracking().OrderBy(l => l.ImportDate).ThenBy(l => l.Id).ToList();
        Receipts = db.WarehouseReceipts.AsNoTracking().Include(r => r.Lots)
                     .OrderByDescending(r => r.Date).ToList();
        Issues = db.WarehouseIssues.AsNoTracking().Include(i => i.Items)
                   .OrderByDescending(i => i.Date).ToList();
        Orders = db.Orders.AsNoTracking().OrderBy(o => o.Date).ToList();
    }

    private static void Commit()
    {
        Reload();
        Changed?.Invoke();
    }

    public static Supplier FindSupplier(string id) => Suppliers.FirstOrDefault(s => s.Id == id);

    /// <summary>Tên các loại gỗ (dùng cho dropdown thay cho danh sách hardcode).</summary>
    public static IEnumerable<string> CategoryNames => Categories.Select(c => c.Name);

    /// <summary>Tìm loại gỗ cha theo tên (không phân biệt hoa thường).</summary>
    public static WoodCategory FindCategoryByName(string name) =>
        Categories.FirstOrDefault(c =>
            string.Equals(c.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Các phân loại con của một loại gỗ cha (theo Id).</summary>
    public static IEnumerable<WoodSubCategory> SubCategoriesOf(string categoryId) =>
        SubCategories.Where(s => s.CategoryId == categoryId).OrderBy(s => s.Name);

    /// <summary>Tên các phân loại con của một loại gỗ cha (theo tên loại cha) — cho dropdown nối tầng.</summary>
    public static IEnumerable<string> SubNamesOf(string categoryName)
    {
        var cat = FindCategoryByName(categoryName);
        return cat == null ? Enumerable.Empty<string>() : SubCategoriesOf(cat.Id).Select(s => s.Name);
    }

    /// <summary>Loại gỗ cha (theo tên) có ít nhất một phân loại con hay không.</summary>
    public static bool CategoryHasSubs(string categoryName) => SubNamesOf(categoryName).Any();

    /// <summary>
    /// Nguyên tắc tính m³ của một loại gỗ theo tên. Nếu chưa có trong danh mục thì
    /// suy đoán tạm theo tên (Gỗ Dương → Footage) để không vỡ dữ liệu cũ.
    /// </summary>
    public static VolumeRule GetVolumeRule(string woodTypeName)
    {
        var cat = Categories.FirstOrDefault(c =>
            string.Equals(c.Name, woodTypeName?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (cat != null) return cat.VolumeRule;

        var n = (woodTypeName ?? "").Trim().ToLowerInvariant();
        return n.Contains("dương") || n.Contains("duong") || n.Contains("poplar")
            ? VolumeRule.ByFootage
            : VolumeRule.BySpecification;
    }

    // ---------------- Nghiệp vụ: Nhà cung cấp ----------------

    private static void ValidateSupplier(Supplier s)
    {
        if (string.IsNullOrWhiteSpace(s.Name))
            throw new InvalidOperationException("Vui lòng nhập Tên nhà cung cấp.");
        if (string.IsNullOrWhiteSpace(s.Code))
            throw new InvalidOperationException("Vui lòng nhập Tên gọi tắt.");
        if (string.IsNullOrWhiteSpace(s.TaxCode))
            throw new InvalidOperationException("Vui lòng nhập Mã số thuế.");
    }

    /// <summary>Thêm nhà cung cấp mới. Chặn trùng Tên gọi tắt (Code).</summary>
    public static void AddSupplier(Supplier supplier)
    {
        ValidateSupplier(supplier);
        using var db = new AppDbContext();
        var code = supplier.Code.Trim();
        if (db.Suppliers.Any(x => x.Code.ToLower() == code.ToLower()))
            throw new InvalidOperationException($"Tên gọi tắt \"{code}\" đã tồn tại. Vui lòng dùng tên khác.");

        supplier.Code = code;
        supplier.Name = supplier.Name.Trim();
        supplier.TaxCode = supplier.TaxCode.Trim();
        db.Suppliers.Add(supplier);
        db.SaveChanges();
        Commit();
    }

    /// <summary>Sửa nhà cung cấp. Chặn trùng Tên gọi tắt với NCC khác.</summary>
    public static void UpdateSupplier(Supplier supplier)
    {
        ValidateSupplier(supplier);
        using var db = new AppDbContext();
        var existing = db.Suppliers.Find(supplier.Id);
        if (existing == null) return;

        var code = supplier.Code.Trim();
        if (db.Suppliers.Any(x => x.Id != supplier.Id && x.Code.ToLower() == code.ToLower()))
            throw new InvalidOperationException($"Tên gọi tắt \"{code}\" đã tồn tại. Vui lòng dùng tên khác.");

        existing.Code = code;
        existing.Name = supplier.Name.Trim();
        existing.TaxCode = supplier.TaxCode.Trim();
        existing.Address = supplier.Address?.Trim();
        existing.Phone = supplier.Phone?.Trim();
        existing.BankAccount = supplier.BankAccount?.Trim();
        db.SaveChanges();
        Commit();
    }

    /// <summary>Xóa nhà cung cấp. Chặn nếu đang được dùng bởi kiện gỗ / báo giá / phiếu nhập.</summary>
    public static void DeleteSupplier(string id)
    {
        using var db = new AppDbContext();
        var s = db.Suppliers.Find(id);
        if (s == null) return;

        if (db.WoodLots.Any(l => l.SupplierId == id)
            || db.WoodQuotations.Any(q => q.SupplierId == id)
            || db.WarehouseReceipts.Any(r => r.SupplierId == id))
            throw new InvalidOperationException(
                $"Nhà cung cấp \"{s.Name}\" đang được dùng bởi kiện gỗ / báo giá / phiếu nhập nên không thể xóa.");

        db.Suppliers.Remove(s);
        db.SaveChanges();
        Commit();
    }

    // ---------------- Nghiệp vụ: Phân loại gỗ ----------------

    /// <summary>Thêm loại gỗ mới. Chặn trùng tên (không phân biệt hoa thường).</summary>
    public static void AddCategory(WoodCategory category)
    {
        using var db = new AppDbContext();
        var name = category.Name?.Trim() ?? "";
        if (db.WoodCategories.Any(c => c.Name.ToLower() == name.ToLower()))
            throw new InvalidOperationException($"Loại gỗ \"{name}\" đã tồn tại trong danh mục.");
        category.Name = name;
        db.WoodCategories.Add(category);
        db.SaveChanges();
        Commit();
    }

    /// <summary>
    /// Sửa loại gỗ có sẵn. Nếu đổi tên → cascade cập nhật WoodType của mọi kiện gỗ và
    /// dòng báo giá đang dùng tên cũ (giữ toàn vẹn dữ liệu). Chặn trùng tên với loại khác.
    /// </summary>
    public static void UpdateCategory(string id, string newName, VolumeRule newRule)
    {
        using var db = new AppDbContext();
        var cat = db.WoodCategories.Find(id);
        if (cat == null) return;

        newName = newName?.Trim() ?? "";
        if (newName.Length == 0)
            throw new InvalidOperationException("Tên loại gỗ không được để trống.");
        if (db.WoodCategories.Any(c => c.Id != id && c.Name.ToLower() == newName.ToLower()))
            throw new InvalidOperationException($"Loại gỗ \"{newName}\" đã tồn tại trong danh mục.");

        var oldName = cat.Name;
        if (!string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            foreach (var lot in db.WoodLots.Where(l => l.WoodType == oldName))
                lot.WoodType = newName;
            foreach (var qi in db.QuotationItems.Where(i => i.WoodType == oldName))
                qi.WoodType = newName;
        }

        cat.Name = newName;
        cat.VolumeRule = newRule;
        db.SaveChanges();
        Commit();
    }

    /// <summary>Xóa loại gỗ. Chặn nếu đang có kiện gỗ dùng loại này (bảo vệ toàn vẹn).</summary>
    public static void DeleteCategory(string id)
    {
        using var db = new AppDbContext();
        var cat = db.WoodCategories.Find(id);
        if (cat == null) return;
        if (db.WoodLots.Any(l => l.WoodType == cat.Name))
            throw new InvalidOperationException(
                $"Loại gỗ \"{cat.Name}\" đang được dùng bởi các kiện gỗ trong kho nên không thể xóa.");
        db.WoodCategories.Remove(cat);
        db.SaveChanges();
        Commit();
    }

    // ---------------- Nghiệp vụ: Phân loại con (cấp 2) ----------------

    /// <summary>Thêm phân loại con vào một loại gỗ cha. Chặn trùng tên trong cùng loại cha.</summary>
    public static void AddSubCategory(string categoryId, string name)
    {
        using var db = new AppDbContext();
        name = name?.Trim() ?? "";
        if (name.Length == 0)
            throw new InvalidOperationException("Tên phân loại không được để trống.");
        if (db.WoodSubCategories.Any(s => s.CategoryId == categoryId && s.Name.ToLower() == name.ToLower()))
            throw new InvalidOperationException($"Phân loại \"{name}\" đã tồn tại trong loại gỗ này.");

        db.WoodSubCategories.Add(new WoodSubCategory
        {
            Id = $"SUB-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            CategoryId = categoryId,
            Name = name
        });
        db.SaveChanges();
        Commit();
    }

    /// <summary>
    /// Sửa tên phân loại con. Nếu đổi tên → cascade cập nhật WoodSubType của mọi kiện gỗ và dòng
    /// báo giá đang dùng tên cũ (trong phạm vi loại cha). Chặn trùng tên với phân loại khác cùng cha.
    /// </summary>
    public static void UpdateSubCategory(string id, string newName)
    {
        using var db = new AppDbContext();
        var sub = db.WoodSubCategories.Find(id);
        if (sub == null) return;

        newName = newName?.Trim() ?? "";
        if (newName.Length == 0)
            throw new InvalidOperationException("Tên phân loại không được để trống.");
        if (db.WoodSubCategories.Any(s => s.Id != id && s.CategoryId == sub.CategoryId
                                          && s.Name.ToLower() == newName.ToLower()))
            throw new InvalidOperationException($"Phân loại \"{newName}\" đã tồn tại trong loại gỗ này.");

        var oldName = sub.Name;
        if (!string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            var parentName = db.WoodCategories.Find(sub.CategoryId)?.Name;
            foreach (var lot in db.WoodLots.Where(l => l.WoodType == parentName && l.WoodSubType == oldName))
                lot.WoodSubType = newName;
            foreach (var qi in db.QuotationItems.Where(i => i.WoodType == parentName && i.WoodSubType == oldName))
                qi.WoodSubType = newName;
        }

        sub.Name = newName;
        db.SaveChanges();
        Commit();
    }

    /// <summary>Xóa phân loại con. Chặn nếu đang có kiện gỗ dùng phân loại này.</summary>
    public static void DeleteSubCategory(string id)
    {
        using var db = new AppDbContext();
        var sub = db.WoodSubCategories.Find(id);
        if (sub == null) return;
        var parentName = db.WoodCategories.Find(sub.CategoryId)?.Name;
        if (db.WoodLots.Any(l => l.WoodType == parentName && l.WoodSubType == sub.Name))
            throw new InvalidOperationException(
                $"Phân loại \"{sub.Name}\" đang được dùng bởi các kiện gỗ trong kho nên không thể xóa.");
        db.WoodSubCategories.Remove(sub);
        db.SaveChanges();
        Commit();
    }

    // ---------------- Nghiệp vụ ----------------

    public static void DeleteLot(string id)
    {
        using var db = new AppDbContext();
        var lot = db.WoodLots.Find(id);
        if (lot == null) return;
        if (db.WarehouseIssueItems.Any(i => i.WoodLotId == id))
            throw new InvalidOperationException(
                $"Kiện {id} đã có lịch sử xuất kho (truy xuất nguồn gốc) nên không thể xóa.");
        db.WoodLots.Remove(lot);
        db.SaveChanges();
        Commit();
    }

    // ---------------- Nghiệp vụ: Báo giá (mỗi NCC 1 danh sách, không phiên bản) ----------------

    /// <summary>Báo giá (duy nhất) của một NCC — null nếu chưa có mục nào.</summary>
    public static WoodQuotation FindQuotation(string supplierId) =>
        Quotations.FirstOrDefault(q => q.SupplierId == supplierId);

    /// <summary>Số mục giá của một NCC.</summary>
    public static int QuotationItemCount(string supplierId) =>
        FindQuotation(supplierId)?.Items.Count ?? 0;

    /// <summary>Lấy báo giá của NCC trong DB, tạo mới (rỗng) nếu chưa có.</summary>
    private static WoodQuotation GetOrCreate(AppDbContext db, string supplierId)
    {
        var q = db.WoodQuotations.Include(x => x.Items).FirstOrDefault(x => x.SupplierId == supplierId);
        if (q == null)
        {
            q = new WoodQuotation
            {
                Id = $"QT-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                SupplierId = supplierId,
                EffectiveDate = DateTime.Today,
                Version = "",       // không dùng phiên bản nữa; "" đảm bảo unique (SupplierId,Version) = 1/NCC
                IsActive = true
            };
            db.WoodQuotations.Add(q);
        }
        return q;
    }

    /// <summary>Thêm một mục giá vào báo giá của NCC (tạo báo giá nếu chưa có).</summary>
    public static void AddQuotationItem(string supplierId, QuotationItem item)
    {
        using var db = new AppDbContext();
        var q = GetOrCreate(db, supplierId);
        item.Id = $"QI-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        item.QuotationId = q.Id;
        q.Items.Add(item);
        q.EffectiveDate = DateTime.Today;
        db.SaveChanges();
        Commit();
    }

    /// <summary>Sửa một mục giá.</summary>
    public static void UpdateQuotationItem(QuotationItem item)
    {
        using var db = new AppDbContext();
        var existing = db.QuotationItems.Find(item.Id);
        if (existing == null) return;
        existing.WoodType = item.WoodType;
        existing.WoodSubType = item.WoodSubType;
        existing.Grade = item.Grade;
        existing.ThicknessMin = item.ThicknessMin;
        existing.ThicknessMax = item.ThicknessMax;
        existing.WidthMin = item.WidthMin;
        existing.WidthMax = item.WidthMax;
        existing.LengthMin = item.LengthMin;
        existing.LengthMax = item.LengthMax;
        existing.Origin = item.Origin;
        existing.Specification = item.Specification;
        existing.PriceUsd = item.PriceUsd;
        var q = db.WoodQuotations.Find(existing.QuotationId);
        if (q != null) q.EffectiveDate = DateTime.Today;
        db.SaveChanges();
        Commit();
    }

    /// <summary>Xóa một mục giá.</summary>
    public static void DeleteQuotationItem(string itemId)
    {
        using var db = new AppDbContext();
        var item = db.QuotationItems.Find(itemId);
        if (item == null) return;
        db.QuotationItems.Remove(item);
        db.SaveChanges();
        Commit();
    }

    /// <summary>Xóa toàn bộ báo giá của một NCC (cascade các mục giá).</summary>
    public static void DeleteQuotation(string supplierId)
    {
        using var db = new AppDbContext();
        foreach (var q in db.WoodQuotations.Where(q => q.SupplierId == supplierId).ToList())
            db.WoodQuotations.Remove(q);
        db.SaveChanges();
        Commit();
    }

    /// <summary>Lập phiếu nhập kho kèm danh sách kiện gỗ mới.</summary>
    public static void AddReceipt(WarehouseReceipt receipt)
    {
        using var db = new AppDbContext();
        var duplicated = receipt.Lots.Select(l => l.Id).FirstOrDefault(id => db.WoodLots.Any(l => l.Id == id));
        if (duplicated != null)
            throw new InvalidOperationException($"Mã kiện {duplicated} đã tồn tại trong hệ thống.");
        db.WarehouseReceipts.Add(receipt);
        db.SaveChanges();
        Commit();
    }

    /// <summary>
    /// Cập nhật phiếu nhập: sửa header + thay toàn bộ danh sách kiện theo khai báo mới.
    /// Chặn nếu phiếu có kiện đã phát sinh xuất kho (giữ nhất quán tồn kho), và chặn
    /// mã kiện mới trùng với kiện của phiếu khác.
    /// </summary>
    public static void UpdateReceipt(WarehouseReceipt updated)
    {
        using var db = new AppDbContext();
        var existing = db.WarehouseReceipts.Include(r => r.Lots).FirstOrDefault(r => r.Id == updated.Id)
            ?? throw new InvalidOperationException("Không tìm thấy phiếu nhập cần cập nhật.");

        var oldLotIds = existing.Lots.Select(l => l.Id).ToList();

        var issued = existing.Lots.Where(l => l.Quantity != l.OriginalQuantity).Select(l => l.Id).ToList();
        issued.AddRange(db.WarehouseIssueItems.Where(i => oldLotIds.Contains(i.WoodLotId)).Select(i => i.WoodLotId));
        issued = issued.Distinct().ToList();
        if (issued.Count > 0)
            throw new InvalidOperationException(
                $"Phiếu có kiện đã phát sinh xuất kho ({string.Join(", ", issued)}) nên không thể chỉnh sửa.");

        foreach (var lot in updated.Lots)
            if (!oldLotIds.Contains(lot.Id) && db.WoodLots.Any(l => l.Id == lot.Id))
                throw new InvalidOperationException($"Mã kiện {lot.Id} đã tồn tại trong hệ thống.");

        // Xóa toàn bộ kiện cũ + cập nhật header (lưu trước để tránh trùng khóa khi tái sử dụng mã kiện)
        db.WoodLots.RemoveRange(existing.Lots);
        existing.SupplierId = updated.SupplierId;
        existing.Date = updated.Date;
        existing.Invoice = updated.Invoice;
        existing.PackingList = updated.PackingList;
        existing.Status = updated.Status;
        db.SaveChanges();

        foreach (var lot in updated.Lots)
        {
            lot.ReceiptId = existing.Id;
            db.WoodLots.Add(lot);
        }
        db.SaveChanges();
        Commit();
    }

    /// <summary>Xóa phiếu nhập (cascade xóa các kiện). Chặn nếu có kiện đã phát sinh xuất kho.</summary>
    public static void DeleteReceipt(string id)
    {
        using var db = new AppDbContext();
        var receipt = db.WarehouseReceipts.Include(r => r.Lots).FirstOrDefault(r => r.Id == id);
        if (receipt == null) return;

        var lotIds = receipt.Lots.Select(l => l.Id).ToList();
        var issued = db.WarehouseIssueItems.Where(i => lotIds.Contains(i.WoodLotId))
                       .Select(i => i.WoodLotId).Distinct().ToList();
        if (issued.Count > 0)
            throw new InvalidOperationException(
                $"Phiếu có kiện đã xuất kho ({string.Join(", ", issued)}) nên không thể xóa.");

        db.WarehouseReceipts.Remove(receipt);
        db.SaveChanges();
        Commit();
    }

    /// <summary>Lập phiếu xuất kho và khấu trừ tồn kho các kiện tương ứng.</summary>
    public static void AddIssue(WarehouseIssue issue)
    {
        using var db = new AppDbContext();
        foreach (var item in issue.Items)
        {
            var lot = db.WoodLots.Find(item.WoodLotId)
                ?? throw new InvalidOperationException($"Không tìm thấy kiện {item.WoodLotId}.");
            lot.IssueInventory(item.Quantity, item.Cbm);
        }
        db.WarehouseIssues.Add(issue);
        db.SaveChanges();
        Commit();
    }
}
