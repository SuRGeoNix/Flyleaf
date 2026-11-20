namespace Flyleaf.Controls.WPF;

/// <summary>Numeric name ordering shared by the queue and application importers.</summary>
public static class FlyleafNameSorting
{
    static readonly IComparer<string> names = new Utils.NaturalStringComparer();

    /// <summary>Orders full names, comparing digit runs by their numeric value.</summary>
    public static IOrderedEnumerable<T> OrderByNumber<T>(IEnumerable<T> items, Func<T, string?> name, bool reverse)
        => reverse ? items.OrderByDescending(item => name(item) ?? "", names) : items.OrderBy(item => name(item) ?? "", names);
}
