namespace WoodInventory.Domain;

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
    public string VolumeRuleLabel => Helpers.Lang.T(VolumeRule == VolumeRule.ByFootage
        ? "WoodCategories.Rule.ByFootage"
        : "WoodCategories.Rule.BySpec");
}

/// <summary>
/// Phân loại con (cấp 2) của một loại gỗ cha. Ví dụ: "Gỗ Thông" → "Thông trắng"/"Thông vàng",
/// "Gỗ Dương" → "1 com"/"2 com". Nguyên tắc tính m³ kế thừa từ loại cha (không lưu ở đây).
/// </summary>
public class WoodSubCategory
{
    public string Id { get; set; }
    public string CategoryId { get; set; }        // FK tới WoodCategory (loại cha)
    public string Name { get; set; }              // Tên phân loại con, vd. "Thông trắng"
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

/// <summary>
/// Một dòng giá trong bảng báo giá. Mọi field ngoài WoodType/PriceUsd đều optional —
/// NULL nghĩa là khớp mọi giá trị (wildcard). Field range chỉ set Min = "từ Min trở lên",
/// chỉ set Max = "đến Max", set cả hai = khoảng đóng (bằng nhau = giá trị đơn).
/// </summary>
public class QuotationItem
{
    public string Id { get; set; }
    public string QuotationId { get; set; }
    public string WoodType { get; set; }
    public string WoodSubType { get; set; }    // phân loại con (cấp 2); null = áp cho mọi phân loại con của loại cha
    public string Grade { get; set; }          // null = mọi grade
    public double? ThicknessMin { get; set; }
    public double? ThicknessMax { get; set; }
    public string ThicknessMinNote { get; set; } // ký hiệu gốc gỗ Footage, vd "4/4\"" — chỉ hiển thị, ThicknessMin (mm) vẫn dùng để khớp giá
    public string ThicknessMaxNote { get; set; }
    // Danh sách giá trị RỜI RẠC tương đương, vd "1220/2440/3000" (khớp ĐÚNG 1 trong các giá trị, không phải khoảng liên tục).
    // Khi field này có giá trị thì ƯU TIÊN dùng thay cho Min/Max — xem QuotationPriceMatcher. Chỉ áp dụng cho gỗ KHÔNG
    // phải Footage (gỗ Footage đã dùng "/" cho ký hiệu phân số inch như "4/4\"" nên không hỗ trợ danh sách ở Thickness).
    public string ThicknessValues { get; set; }
    // Cờ khoảng MỞ cho Min/Max: false = ĐOẠN đóng [a;b] (a ≤ x ≤ b, mặc định, tương thích dữ liệu cũ);
    // true = KHOẢNG mở (a;b) (a < x < b). Áp cho từng bound đang set — xem QuotationPriceMatcher.RangeMatches.
    public bool ThicknessOpen { get; set; }
    public double? WidthMin { get; set; }
    public double? WidthMax { get; set; }
    public string WidthValues { get; set; }
    public bool WidthOpen { get; set; }
    public double? LengthMin { get; set; }
    public double? LengthMax { get; set; }
    public string LengthValues { get; set; }
    public bool LengthOpen { get; set; }
    public string Origin { get; set; }         // null = mọi xuất xứ
    public string Specification { get; set; }  // ghi chú tự do, không dùng để khớp giá
    public decimal Price { get; set; }
    public string PriceCurrency { get; set; } = "USD";  // "USD" | "VND" — VND thì nhập kho không nhân tỷ giá
    public DateTime? UpdatedAt { get; set; }    // thời điểm chỉnh sửa gần nhất (ban đầu = lúc tạo dòng)
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
    public string DeliveryNote { get; set; }       // Phiếu giao hàng riêng của kiện (khác Invoice/PackingList chung phiếu)
    public string ForestList { get; set; }         // Bảng kê lâm sản (chứng từ cấp phiếu — copy vào từng kiện như Invoice/PackingList)
    public string WoodType { get; set; }           // "Gỗ Dương", "Gỗ Sồi", ... (loại cha)
    public string WoodSubType { get; set; }        // phân loại con (cấp 2), vd "1 com" / "Thông trắng"; null = chưa phân loại
    public string WoodName { get; set; }
    public double ThicknessMm { get; set; }
    public double WidthMm { get; set; }
    public double LengthMm { get; set; }
    public string LengthNote { get; set; }         // Mô tả chiều dài dạng inch cho gỗ nhóm Footage, vd 132"144" — chỉ hiển thị, không dùng tính toán
    public string ThicknessNote { get; set; }      // Ký hiệu độ dày dạng inch cho gỗ nhóm Footage, vd 4/4" / 5/4" — chỉ hiển thị, không dùng tính toán
    public int OriginalQuantity { get; set; }
    public int Quantity { get; set; }              // Số thanh còn tồn
    public double Footage { get; set; }            // BFT — chỉ dùng cho Gỗ Dương
    public double Cbm { get; set; }                // m³ ban đầu
    public double RemainingCbm { get; set; }       // m³ còn lại
    public int? VolumeDecimals { get; set; }       // số chữ số thập phân làm tròn m³ riêng của kiện (null = mặc định 5)
    public double? VolumeAdjustment { get; set; }  // điều chỉnh tay +/- cộng vào m³ sau khi làm tròn (null = 0)
    public decimal Price { get; set; }
    public string PriceCurrency { get; set; } = "USD";  // "USD" | "VND" — theo báo giá NCC tại thời điểm nhập
    public decimal ExchangeRate { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal CostPriceVnd { get; set; }      // Giá vốn VND/m³ (đã gồm thuế)
    public decimal TotalValueVnd { get; set; }     // Giá trị tồn hiện tại
    public string Grade { get; set; }
    public string Origin { get; set; }             // Xuất xứ kiện gỗ (vd "Mỹ", "Nga") — dùng khai báo + khớp báo giá

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

    /// <summary>Hoàn trả tồn kho khi sửa/xóa một dòng đã xuất trước đó (nghịch đảo <see cref="IssueInventory"/>).</summary>
    public void ReturnInventory(int returnQty, double returnCbm)
    {
        Quantity += returnQty;
        RemainingCbm = Math.Round(Math.Min(Cbm, RemainingCbm + returnCbm), 4);
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

/// <summary>
/// Cài đặt chung của app — LUÔN đúng 1 dòng duy nhất (Id cố định "default").
/// Thông tin công ty hiển thị ở thanh trạng thái + giá trị mặc định mồi sẵn cho form Nhập Kho.
/// </summary>
public class AppSettings
{
    public string Id { get; set; } = "default";
    public string CompanyName { get; set; }
    public string CompanyTaxCode { get; set; }
    public string CompanyAddress { get; set; }
    public string CompanyPhone { get; set; }
    public decimal DefaultExchangeRate { get; set; }
    public decimal DefaultTaxPercent { get; set; }
    public int DefaultVolumeDecimals { get; set; }
    public int LowStockThreshold { get; set; }
    public string Language { get; set; } = "vi";   // "vi" | "zh-Hans"
}
