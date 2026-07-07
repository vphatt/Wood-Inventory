namespace TimberFlowDesktop.Domain;

/// <summary>
/// Nguyên tắc tính thể tích m³ của một loại gỗ.
/// Quyết định trường bắt buộc khi khai báo kiện gỗ ở màn nhập/xuất:
///  - BySpecification: bắt buộc Dày + Rộng + Dài
///  - ByFootage:       bắt buộc Dày + Footage
/// </summary>
public enum VolumeRule
{
    BySpecification = 0, // Theo quy cách Dày x Rộng x Dài
    ByFootage = 1        // Theo Footage
}

/// <summary>
/// Phân loại gỗ (danh mục master). Thay cho danh sách loại gỗ hardcode trước đây.
/// Mỗi loại gỗ gắn một <see cref="VolumeRule"/> để hệ thống biết cách tính m³
/// và ràng buộc dữ liệu khi nhập/xuất.
/// </summary>
public class WoodCategory
{
    public string Id { get; set; }
    public string Name { get; set; }             // Tên loại gỗ, vd. "Gỗ Sồi"
    public VolumeRule VolumeRule { get; set; }

    /// <summary>Nhãn hiển thị của nguyên tắc tính m³.</summary>
    public string VolumeRuleLabel => VolumeRule == VolumeRule.ByFootage
        ? "Theo Footage"
        : "Theo quy cách (Dày x Rộng x Dài)";
}

/// <summary>Nhà cung cấp gỗ.</summary>
public class Supplier
{
    public string Id { get; set; }
    public string Code { get; set; }         // Tên gọi tắt (mã định danh ngắn, duy nhất)
    public string Name { get; set; }         // Tên nhà cung cấp
    public string TaxCode { get; set; }      // Mã số thuế
    public string Address { get; set; }      // Địa chỉ
    public string Phone { get; set; }        // Số điện thoại
    public string BankAccount { get; set; }  // Số tài khoản
}

/// <summary>Một dòng giá trong bảng báo giá.</summary>
public class QuotationItem
{
    public string Id { get; set; }
    public string QuotationId { get; set; }
    public string WoodType { get; set; }
    public double Thickness { get; set; }
    public string Grade { get; set; }
    public string Specification { get; set; }
    public decimal PriceUsd { get; set; }
}

/// <summary>Bảng báo giá gỗ của nhà cung cấp (quản lý theo phiên bản).</summary>
public class WoodQuotation
{
    public string Id { get; set; }
    public string SupplierId { get; set; }
    public Supplier Supplier { get; set; }
    public DateTime EffectiveDate { get; set; }
    public string Version { get; set; }
    public bool IsActive { get; set; }
    public List<QuotationItem> Items { get; set; } = new();
}

/// <summary>
/// Kiện gỗ (Lot) — aggregate root của phân hệ tồn kho.
/// </summary>
public class WoodLot
{
    public string Id { get; set; }                 // Mã kiện, vd. LOT-2601A
    public string SupplierId { get; set; }
    public Supplier Supplier { get; set; }
    public DateTime ImportDate { get; set; }
    public string ReceiptId { get; set; }          // Phiếu nhập gốc
    public string Invoice { get; set; }
    public string PackingList { get; set; }
    public string WoodType { get; set; }           // "Gỗ Dương", "Gỗ Sồi", ...
    public string WoodName { get; set; }
    public double ThicknessMm { get; set; }
    public double WidthMm { get; set; }
    public double LengthMm { get; set; }
    public int OriginalQuantity { get; set; }
    public int Quantity { get; set; }              // Số thanh còn tồn
    public double Footage { get; set; }            // BFT — chỉ dùng cho Gỗ Dương
    public double Cbm { get; set; }                // m³ ban đầu
    public double RemainingCbm { get; set; }       // m³ còn lại
    public decimal PriceUsd { get; set; }
    public decimal ExchangeRate { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal CostPriceVnd { get; set; }      // Giá vốn VND/m³ (đã gồm thuế)
    public decimal TotalValueVnd { get; set; }     // Giá trị tồn hiện tại
    public string Grade { get; set; }

    /// <summary>Khấu trừ tồn kho khi xuất kho.</summary>
    public void IssueInventory(int issueQty, double issueCbm)
    {
        if (issueQty > Quantity)
            throw new InvalidOperationException($"Không thể xuất {issueQty}. Kiện {Id} chỉ còn {Quantity}.");
        if (issueCbm > RemainingCbm + 0.0001)
            throw new InvalidOperationException($"Không thể xuất {issueCbm} m³. Kiện {Id} chỉ còn {RemainingCbm} m³.");

        Quantity -= issueQty;
        RemainingCbm = Math.Round(Math.Max(0, RemainingCbm - issueCbm), 4);
        TotalValueVnd = Math.Round(CostPriceVnd * (decimal)RemainingCbm, 0);
    }
}

/// <summary>Phiếu nhập kho gỗ.</summary>
public class WarehouseReceipt
{
    public string Id { get; set; }
    public string SupplierId { get; set; }
    public Supplier Supplier { get; set; }
    public DateTime Date { get; set; }
    public string Invoice { get; set; }
    public string PackingList { get; set; }
    public string Status { get; set; }             // draft | completed
    public List<WoodLot> Lots { get; set; } = new();
}

/// <summary>Một dòng xuất trong phiếu xuất kho (truy xuất đích danh theo kiện).</summary>
public class WarehouseIssueItem
{
    public string WarehouseIssueId { get; set; }
    public string WoodLotId { get; set; }
    public WoodLot WoodLot { get; set; }
    public int Quantity { get; set; }
    public double Cbm { get; set; }
    public decimal CostPriceVnd { get; set; }      // Giá vốn tại thời điểm xuất
}

/// <summary>Phiếu xuất kho gỗ cho đơn hàng sản xuất.</summary>
public class WarehouseIssue
{
    public string Id { get; set; }
    public string OrderId { get; set; }
    public Order Order { get; set; }
    public DateTime Date { get; set; }
    public List<WarehouseIssueItem> Items { get; set; } = new();
}

/// <summary>Đơn hàng sản xuất.</summary>
public class Order
{
    public string Id { get; set; }
    public string CustomerName { get; set; }
    public DateTime Date { get; set; }
    public string Status { get; set; }             // pending | processing | completed
}
