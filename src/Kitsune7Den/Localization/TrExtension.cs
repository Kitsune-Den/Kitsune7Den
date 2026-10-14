using System.Windows.Data;
using System.Windows.Markup;

namespace Kitsune7Den.Localization;

/// <summary>
/// <c>Text="{l:Tr Nav_Dashboard}"</c> — a one-way binding to <see cref="Loc"/>'s
/// indexer, so the text follows the language setting without reloading the view.
/// Works anywhere a binding does, including setters and templates.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
