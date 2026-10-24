// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

namespace g0v0.Server.Realtime.Extensions;

/// <summary>
/// Provides LINQ extensions used by multiplayer queue ordering.
/// </summary>
internal static class EnumerableExtensions
{
    /// <summary>
    /// Interleaves sequential elements from one or more collections, returning groups of interleaved elements.
    /// </summary>
    /// <remarks>
    /// For the collections A = [A1, A2, A3], B = [B1, B2], this returns the groups
    /// [A1, B1], [A2, B2], [A3].
    /// </remarks>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="collections">The collections to interleave.</param>
    /// <returns>The interleaved element groups.</returns>
    public static IEnumerable<IEnumerable<T>> Interleave<T>(this IEnumerable<IEnumerable<T>> collections)
    {
        List<IEnumerator<T>> enumerators = new();

        try
        {
            enumerators.AddRange(collections.Select(collection => collection.GetEnumerator()));

            while (true)
            {
                T[] interleaved = enumerators
                    .Where(enumerator => enumerator.MoveNext())
                    .Select(enumerator => enumerator.Current)
                    .ToArray(); // The enumerators must be consumed immediately due to lazy evaluation.

                if (interleaved.Length == 0)
                {
                    break;
                }

                yield return interleaved;
            }
        }
        finally
        {
            foreach (IEnumerator<T> enumerator in enumerators)
            {
                enumerator.Dispose();
            }
        }
    }
}