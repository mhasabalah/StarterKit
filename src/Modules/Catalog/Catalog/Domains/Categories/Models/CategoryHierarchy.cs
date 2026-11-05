namespace Catalog.Domains.Categories.Models;

/// <summary>Culture-neutral snapshot of one category — what the distributed cache stores.</summary>
public sealed record CategoryNodeSnapshot(
    Guid Id,
    Guid? ParentId,
    string NameEn,
    string NameAr,
    string Slug,
    int DisplayOrder);

/// <summary>Pure tree algorithms over (Id, ParentId) pairs. No I/O, so they are unit-tested directly.</summary>
public static class CategoryHierarchy
{
    /// <summary>
    /// True when making <paramref name="newParentId"/> the parent of <paramref name="categoryId"/> would create
    /// a loop (the new parent is the category itself or one of its descendants).
    /// </summary>
    public static bool WouldCreateCycle(
        Guid categoryId, Guid? newParentId, IReadOnlyDictionary<Guid, Guid?> parentById)
    {
        var visited = new HashSet<Guid>();
        for (var current = newParentId; current is { } id; current = parentById.GetValueOrDefault(id))
        {
            if (id == categoryId)
                return true;
            if (!visited.Add(id))
                return true; // existing corrupt loop — refuse rather than spin
        }

        return false;
    }

    /// <summary>The root plus every descendant id (breadth-first).</summary>
    public static HashSet<Guid> GetDescendantIds(Guid rootId, IEnumerable<(Guid Id, Guid? ParentId)> categories)
    {
        var childrenByParent = categories
            .Where(c => c.ParentId.HasValue)
            .ToLookup(c => c.ParentId!.Value, c => c.Id);

        var result = new HashSet<Guid> { rootId };
        var queue = new Queue<Guid>([rootId]);
        while (queue.Count > 0)
        {
            foreach (var child in childrenByParent[queue.Dequeue()])
            {
                if (result.Add(child))
                    queue.Enqueue(child);
            }
        }

        return result;
    }

    /// <summary>
    /// Builds the localized tree. Roots are nodes without a parent; a node whose parent is not in
    /// <paramref name="nodes"/> (e.g. the parent is inactive) is hidden together with its subtree.
    /// Siblings are ordered by DisplayOrder, then name.
    /// </summary>
    public static List<CategoryTreeNodeDto> BuildTree(IReadOnlyCollection<CategoryNodeSnapshot> nodes, bool arabic)
    {
        var childrenByParent = nodes
            .Where(n => n.ParentId.HasValue)
            .ToLookup(n => n.ParentId!.Value);

        List<CategoryTreeNodeDto> Build(IEnumerable<CategoryNodeSnapshot> level, HashSet<Guid> path) =>
            level
                .Where(n => !path.Contains(n.Id))
                .Select(n => new { Node = n, Name = arabic ? n.NameAr : n.NameEn })
                .OrderBy(x => x.Node.DisplayOrder)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => new CategoryTreeNodeDto
                {
                    Id = x.Node.Id,
                    Name = x.Name,
                    Slug = x.Node.Slug,
                    DisplayOrder = x.Node.DisplayOrder,
                    Children = Build(childrenByParent[x.Node.Id], [.. path, x.Node.Id])
                })
                .ToList();

        return Build(nodes.Where(n => n.ParentId is null), []);
    }
}
