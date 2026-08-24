using System.Text.Json;
using NED.Abstractions.Manifest;

namespace NED.Core;

/// <summary>
/// Centrální styling NEDa — kategorie (barva headeru + ikona) i port barvy (typ → barva).
/// Konfigurovatelný přes <c>ned-theme.json</c>, s hardcoded defaults jako fallback.
/// </summary>
public sealed class NedTheme
{
    private readonly Dictionary<string, NodeStyle> _byCategory =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string> _portColors =
        new(StringComparer.OrdinalIgnoreCase);

    private string _anyPortColor = "#8395A3";

    public NodeStyle Fallback { get; private set; } = new("#377799", "•");

    // ── Category styling ────────────────────────────────

    public NodeStyle ForCategory(string category) =>
        _byCategory.TryGetValue(category, out var s) ? s : Fallback;

    /// <summary>Vzhled uzlu: co si typ určil v manifestu, jinak default jeho kategorie.</summary>
    public NodeStyle Resolve(NodeTypeDescriptor type)
    {
        var cat = ForCategory(type.Category);
        return new NodeStyle(type.Color ?? cat.Color, type.Icon ?? cat.Icon);
    }

    public NedTheme Set(string category, string color, string icon)
    {
        _byCategory[category] = new NodeStyle(color, icon);
        return this;
    }

    public NedTheme SetFallback(string color, string icon)
    {
        Fallback = new NodeStyle(color, icon);
        return this;
    }

    // ── Port colors ─────────────────────────────────────

    public string AnyPortColor => _anyPortColor;

    public NedTheme SetPortColor(string typeName, string color)
    {
        if (typeName.Equals("any", StringComparison.OrdinalIgnoreCase))
            _anyPortColor = color;
        else
            _portColors[typeName] = color;
        return this;
    }

    /// <summary>
    /// Barva portu podle type id. Klíčem je lidsky čitelný název (viz <see cref="TypeIds.Friendly"/>),
    /// aby konfigurace v ned-theme.json zůstala čitelná ("Double", ne "double" ani "pack/Typ").
    /// </summary>
    public string PortColorFor(string? typeId)
    {
        if (typeId is null) return _anyPortColor;
        var key = ShortToClr.TryGetValue(typeId, out var clr) ? clr : TypeIds.Friendly(typeId);
        return _portColors.TryGetValue(key, out var color) ? color : _anyPortColor;
    }

    // ── Factory ─────────────────────────────────────────

    public static NedTheme CreateDefault()
    {
        var t = new NedTheme();
        ApplyBuiltinDefaults(t);
        return t;
    }

    public static NedTheme LoadFromJson(string json)
    {
        var t = new NedTheme();
        ApplyBuiltinDefaults(t);
        ApplyJson(t, json);
        return t;
    }

    public static NedTheme LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return CreateDefault();
        return LoadFromJson(File.ReadAllText(path));
    }

    public static NedTheme LoadFromEmbedded()
    {
        var asm = typeof(NedTheme).Assembly;
        using var stream = asm.GetManifestResourceStream("NED.Core.ned-theme.json");
        if (stream is null) return CreateDefault();
        using var reader = new StreamReader(stream);
        return LoadFromJson(reader.ReadToEnd());
    }

    // ── Internals ───────────────────────────────────────

    private static void ApplyBuiltinDefaults(NedTheme t)
    {
        t.Set("Math", "#377799", "∑");
        t.Set("String", "#2F958E", "Ab");
        t.Set("Bool", "#B97D1B", "✓");
        t.Set("Output", "#3758CC", "➤");

        t.SetPortColor("any", "#8395A3");
        t.SetPortColor(TypeIds.Exec, "#E8E8E8");
        t.SetPortColor("Int32", "#4FB286");
        t.SetPortColor("Int64", "#4FB286");
        t.SetPortColor("Int16", "#4FB286");
        t.SetPortColor("Byte", "#4FB286");
        t.SetPortColor("Double", "#8BAF55");
        t.SetPortColor("Single", "#8BAF55");
        t.SetPortColor("Decimal", "#8BAF55");
        t.SetPortColor("String", "#B768A0");
        t.SetPortColor("Boolean", "#C84A50");
        t.SetPortColor("Object", "#3C8FB3");
        t.SetPortColor("Enum", "#2F958E");
        t.SetPortColor("Struct", "#4B6FB5");
    }

    private static readonly Dictionary<string, string> ShortToClr = new(StringComparer.OrdinalIgnoreCase)
    {
        ["int"] = "Int32", ["long"] = "Int64", ["short"] = "Int16", ["byte"] = "Byte",
        ["double"] = "Double", ["float"] = "Single", ["decimal"] = "Decimal",
        ["string"] = "String", ["bool"] = "Boolean",
    };

    private static void ApplyJson(NedTheme t, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("fallback", out var fb))
        {
            var c = fb.TryGetProperty("color", out var fc) ? fc.GetString() : t.Fallback.Color;
            var i = fb.TryGetProperty("icon", out var fi) ? fi.GetString() : t.Fallback.Icon;
            t.SetFallback(c!, i!);
        }

        if (root.TryGetProperty("categories", out var cats))
        {
            foreach (var cat in cats.EnumerateObject())
            {
                var color = cat.Value.TryGetProperty("color", out var cc) ? cc.GetString()! : "#888";
                var icon = cat.Value.TryGetProperty("icon", out var ci) ? ci.GetString()! : "•";
                t.Set(cat.Name, color, icon);
            }
        }

        if (root.TryGetProperty("portColors", out var ports))
        {
            foreach (var p in ports.EnumerateObject())
            {
                var name = ShortToClr.TryGetValue(p.Name, out var clr) ? clr : p.Name;
                t.SetPortColor(name, p.Value.GetString()!);
            }
        }
    }
}
