using System.Reflection;

namespace NED.Core;

public sealed class NedCommandNode
{
    public string Name { get; }
    public string FullPath { get; }
    public string? Icon { get; set; }
    public string? Shortcut { get; set; }
    public int Order { get; set; }
    public Func<bool>? Enabled { get; set; }
    public Func<Task>? Execute { get; set; }
    /// <summary>true = label se lokalizuje přes Loc[klíč]. false = vlastní jméno (taby, jazyky).</summary>
    public bool Localize { get; set; } = true;
    public List<NedCommandNode> Children { get; } = [];

    public bool IsGroup => Children.Count > 0;
    public bool IsEnabled => Enabled?.Invoke() ?? true;

    public NedCommandNode(string name, string fullPath)
    {
        Name = name;
        FullPath = fullPath;
    }
}

public sealed class NedCommandRegistry
{
    private readonly List<NedCommandNode> _roots = [];
    public IReadOnlyList<NedCommandNode> Roots => _roots;

    /// <summary>Odvodí lokalizační klíč z cesty: "File/Save As" → "File_SaveAs".</summary>
    public static string KeyOf(string path) => path.Replace("/", "_").Replace(" ", "");

    public NedCommandNode GetOrCreate(string path)
    {
        var segments = path.Split('/');
        var list = _roots;
        NedCommandNode? current = null;
        var pathSoFar = "";

        foreach (var seg in segments)
        {
            pathSoFar = pathSoFar.Length == 0 ? seg : $"{pathSoFar}/{seg}";
            var found = list.FirstOrDefault(n => n.Name == seg);
            if (found is null)
            {
                found = new NedCommandNode(seg, pathSoFar);
                list.Add(found);
            }
            current = found;
            list = found.Children;
        }

        return current!;
    }

    public NedCommandNode? Find(string path)
    {
        var segments = path.Split('/');
        IReadOnlyList<NedCommandNode> list = _roots;
        NedCommandNode? current = null;

        foreach (var seg in segments)
        {
            current = list.FirstOrDefault(n => n.Name == seg);
            if (current is null) return null;
            list = current.Children;
        }

        return current;
    }

    public void Configure(string path, Action<NedCommandNode> configure)
    {
        var node = Find(path);
        if (node is not null) configure(node);
    }

    public void RemoveChildren(string parentPath)
    {
        var parent = Find(parentPath);
        parent?.Children.Clear();
    }

    /// <summary>Odstraní uzel (i s podstromem) z jeho rodiče. No-op pokud neexistuje.</summary>
    public void Remove(string path)
    {
        var segments = path.Split('/');
        List<NedCommandNode> containing = _roots;
        NedCommandNode? node = null;

        for (var i = 0; i < segments.Length; i++)
        {
            node = containing.FirstOrDefault(n => n.Name == segments[i]);
            if (node is null) return;
            if (i < segments.Length - 1) containing = node.Children;
        }

        if (node is not null) containing.Remove(node);
    }

    public IEnumerable<NedCommandNode> AllLeaves()
    {
        return Collect(_roots);

        static IEnumerable<NedCommandNode> Collect(IReadOnlyList<NedCommandNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (n.IsGroup)
                {
                    foreach (var child in Collect(n.Children))
                        yield return child;
                }
                else
                {
                    yield return n;
                }
            }
        }
    }

    public void RegisterFromAttributes(object target)
    {
        var methods = target.GetType().GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<NedCommandAttribute>();
            if (attr is null) continue;

            var node = GetOrCreate(attr.Path);
            node.Icon = attr.Icon;
            node.Shortcut = attr.Shortcut;
            node.Order = attr.Order;

            if (method.ReturnType == typeof(Task))
            {
                node.Execute = (Func<Task>)Delegate.CreateDelegate(typeof(Func<Task>), target, method);
            }
            else
            {
                var action = (Action)Delegate.CreateDelegate(typeof(Action), target, method);
                node.Execute = () => { action(); return Task.CompletedTask; };
            }
        }
    }
}
