using System.Reflection;
using System.Text;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Formats the stock arrays our own window gets: the same <c>length[names]</c> shape the
/// diagnostics used while the vanilla window was traced.
/// </summary>
internal static class StockTrace
{
    /// <summary>Prefix of every line this class writes.</summary>
    internal const string Tag = "[STOCKTRACE]";

    private static readonly FieldInfo? StockField =
        typeof(ShopMenuStock).GetField("stock", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// First three item names of an array, so different stock arrays are told apart
    /// in the log. Null and empty arrays get their own marker.
    /// </summary>
    /// <param name="items">Stock array; may be null.</param>
    /// <returns>Comma-separated names, never null.</returns>
    internal static string Names(ShopItem[]? items)
    {
        if (items == null)
        {
            return "<null>";
        }

        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < items.Length && i < 3; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            ShopItem item = items[i];
            builder.Append(item != null ? item.name : "<none>");
        }

        return builder.Length == 0 ? "<empty>" : builder.ToString();
    }

    /// <summary>Real value of the private <c>stock</c> field: length and first names.</summary>
    /// <param name="stock">Component to read; must not be null.</param>
    /// <returns>Readable snapshot such as <c>6[A,B,C]</c>.</returns>
    internal static string Field(ShopMenuStock stock)
    {
        ShopItem[]? current = StockField?.GetValue(stock) as ShopItem[];
        return $"{(current != null ? current.Length : -1)}[{Names(current)}]";
    }
}

