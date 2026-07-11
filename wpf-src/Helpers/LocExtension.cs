using System.Windows.Data;
using System.Windows.Markup;

namespace WoodInventory.Helpers;

/// <summary>
/// MarkupExtension dịch chuỗi trong XAML: {helpers:Loc Some.Key}. Trả về một Binding trỏ vào indexer
/// của LanguageService.Instance (không trả string thẳng) — nhờ vậy khi đổi ngôn ngữ (hot-swap), WPF tự
/// đọc lại giá trị mới mà không cần rebuild lại cây XAML.
/// </summary>
public class LocExtension : MarkupExtension
{
    public string Key { get; set; }

    public LocExtension() { }
    public LocExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LanguageService.Instance,
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}
