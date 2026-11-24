namespace Flyleaf.Controls.WPF;

/// <summary>Shared search rules for the queue panel and the quick navigation popup.</summary>
internal static class FlyleafQueueSearch
{
    static bool Contains(string? value, string query) => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
    internal static bool Matches(FlyleafQueueSource source, string query) => query.Length == 0 || Contains(source.Title, query) || Contains(source.Input, query);
    internal static bool Matches(FlyleafQueueChild child, string query) => query.Length == 0 || Contains(child.Title, query) || Contains(child.DirectUrl, query);

    // Discovered children replace their parent in the flattened playback sequence.
    internal static IEnumerable<object> FindPlayable(FlyleafQueue queue, string query)
    {
        foreach (var source in queue.Sources)
        {
            bool parentMatches = Matches(source, query);
            if (source.Children.Count == 0)
            {
                if (parentMatches) yield return source;
            }
            else
                foreach (var child in source.Children)
                    if (parentMatches || Matches(child, query)) yield return child;
        }
    }
}
