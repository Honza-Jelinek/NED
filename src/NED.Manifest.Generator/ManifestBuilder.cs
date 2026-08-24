using System.Reflection;
using NED.Abstractions;
using NED.Abstractions.Manifest;

namespace NED.Manifest.Generator;

/// <summary>Typ uzlu, ze kterého nejde vyrobit instance — nemá bezparametrický konstruktor.</summary>
public sealed record ManifestWarning(string TypeName, string Reason);

/// <summary>
/// Přečte anotovanou assembly reflexí a poskládá <see cref="NodeManifest"/>.
///
/// Tohle je jediné místo v celém systému, kde nad doménovými typy ještě běží reflexe —
/// a běží při buildu na stroji autora, ne v editoru. Logika záměrně zrcadlí to, co dřív
/// dělal <c>DataNodeModel</c> v konstruktoru, aby se chování editoru nezměnilo.
/// </summary>
public static class ManifestBuilder
{
    public static NodeManifest Build(Assembly assembly, out IReadOnlyList<ManifestWarning> warnings)
    {
        var warn = new List<ManifestWarning>();
        var pack = PackOf(assembly);

        var manifest = new NodeManifest { Pack = pack };
        var enums = new Dictionary<string, EnumDescriptor>(StringComparer.Ordinal);

        foreach (var type in NodeTypes(assembly))
        {
            var descriptor = Describe(type, pack.Id, enums, warn);
            if (descriptor is not null) manifest.Types.Add(descriptor);
        }

        manifest.Types.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        manifest.Enums.AddRange(enums.Values.OrderBy(e => e.Id, StringComparer.Ordinal));

        warnings = warn;
        return manifest;
    }

    /// <summary>Pack identita z <c>[assembly: NodePack]</c>; bez atributu se odvodí z názvu assembly.</summary>
    private static PackInfo PackOf(Assembly assembly)
    {
        var attr = assembly.GetCustomAttribute<NodePackAttribute>();
        var name = assembly.GetName();
        return new PackInfo
        {
            Id = attr?.Id ?? name.Name!.ToLowerInvariant(),
            Name = attr?.Name ?? name.Name,
            Version = attr?.Version ?? name.Version?.ToString(),
        };
    }

    private static IEnumerable<Type> NodeTypes(Assembly assembly) =>
        SafeGetTypes(assembly)
            .Where(t => typeof(IGraphData).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

    private static NodeTypeDescriptor? Describe(
        Type type, string packId, Dictionary<string, EnumDescriptor> enums, List<ManifestWarning> warnings)
    {
        // Výchozí hodnoty polí se čtou z čerstvé instance — proto musí typ mít bezparametrický
        // konstruktor. Dřív se to projevilo až pádem editoru při kliknutí v paletě; tady se to
        // pozná při buildu.
        object? instance;
        try
        {
            instance = Activator.CreateInstance(type);
        }
        catch (Exception ex)
        {
            warnings.Add(new ManifestWarning(type.FullName ?? type.Name, ex.GetType().Name + ": " + ex.Message));
            return null;
        }
        if (instance is null)
        {
            warnings.Add(new ManifestWarning(type.FullName ?? type.Name, "Activator.CreateInstance vrátil null"));
            return null;
        }

        var info = type.GetCustomAttribute<NodeInfoAttribute>();
        var outputAttributes = type.GetCustomAttributes<NodeOutputAttribute>().ToList();

        var descriptor = new NodeTypeDescriptor
        {
            Id = TypeId(type, packId),
            Name = info?.Name ?? type.Name,
            Category = info?.Category ?? "General",
            Color = info?.Color,
            Icon = info?.Icon,
            Description = NedDescriptions.Of(type),
            Extends = Ancestors(type, packId),
        };

        if (type.GetCustomAttribute<NodeSinkAttribute>() is null)
        {
            if (outputAttributes.Count == 0) outputAttributes.Add(new NodeOutputAttribute());
            descriptor.Outputs.AddRange(outputAttributes.Select(a => new NodeOutputDescriptor
            {
                Name = a.Name,
                Type = TypeRef(a.Type ?? type, packId, enums),
                Description = NedDescriptions.Of(type),
                Multiple = a.Multiple,
                // Continue je výchozí a do manifestu se nepíše — jinak by pole přibylo
                // každému výstupu ve všech packech a committnuté manifesty by driftly.
                Role = a.ExecRole == ExecOutputRole.Continue ? null : a.ExecRole,
            }));
        }

        foreach (var prop in type.GetProperties())
        {
            var portAttr = prop.GetCustomAttribute<NodePortAttribute>();
            var fieldAttr = prop.GetCustomAttribute<NodeFieldAttribute>();
            if (portAttr is null && fieldAttr is null) continue;

            var clrType = Nullable.GetUnderlyingType(prop.PropertyType)
                          ?? (prop.PropertyType.IsArray ? prop.PropertyType.GetElementType()! : prop.PropertyType);

            var defaultValue = prop.CanRead ? prop.GetValue(instance) : null;
            var hasExplicit = defaultValue is not null && !Equals(defaultValue, TypeDefault(prop.PropertyType));

            descriptor.Inputs.Add(new NodeInputDescriptor
            {
                Name = portAttr?.Id ?? fieldAttr?.Id ?? prop.Name,
                Label = portAttr?.Label ?? fieldAttr?.Label ?? prop.Name,
                Kind = portAttr is not null ? InputKind.Port : InputKind.Field,
                Type = TypeRef(clrType, packId, enums),
                Default = Literal(defaultValue),
                HasExplicitDefault = hasExplicit,
                Description = NedDescriptions.Of(prop),
                Multiple = portAttr?.Multiple ?? false,
                Optional = portAttr?.Optional ?? hasExplicit,
                Details = fieldAttr?.Details ?? false,
                TypePicker = prop.GetCustomAttribute<NodeTypePickerAttribute>() is not null,
            });
        }

        return descriptor;
    }

    /// <summary>
    /// Předkové a rozhraní jako type id — nosič polymorfie portů.
    /// <c>IGraphData</c> se vynechává: odpovídá <see cref="TypeIds.Any"/>, který přijme vše implicitně.
    /// </summary>
    private static List<string> Ancestors(Type type, string packId)
    {
        var result = new List<string>();

        for (var b = type.BaseType; b is not null && b != typeof(object); b = b.BaseType)
            if (typeof(IGraphData).IsAssignableFrom(b))
                result.Add(TypeId(b, packId));

        foreach (var i in type.GetInterfaces())
            if (i != typeof(IGraphData) && typeof(IGraphData).IsAssignableFrom(i))
                result.Add(TypeId(i, packId));

        return result;
    }

    /// <summary>Type id pro odkaz z portu/pole. Skaláry mají krátká id, enumy se cestou zaregistrují.</summary>
    private static string TypeRef(Type type, string packId, Dictionary<string, EnumDescriptor> enums)
    {
        if (TypeIds.FromClrType.TryGetValue(type, out var scalar)) return scalar;

        if (type.IsEnum)
        {
            var id = TypeId(type, packId);
            if (!enums.ContainsKey(id))
                enums[id] = new EnumDescriptor { Id = id, Values = Enum.GetNames(type).ToList() };
            return id;
        }

        if (typeof(IGraphData).IsAssignableFrom(type)) return TypeId(type, packId);

        // Neznámý typ (host použil něco, co NED neumí zobrazit) — ať radši propadne na Any,
        // než aby se node vůbec neobjevil.
        return TypeIds.Any;
    }

    /// <summary>
    /// <c>"pack/local"</c>. Lokální část je <c>[NodeInfo(Id = …)]</c>, jinak jméno třídy —
    /// namespace se záměrně nepoužívá, aby ho šlo měnit bez dopadu na uložené grafy.
    /// Typ z cizí assembly si nese pack svého původu.
    /// </summary>
    private static string TypeId(Type type, string packId)
    {
        var owner = type.Assembly.GetCustomAttribute<NodePackAttribute>()?.Id
                    ?? type.Assembly.GetName().Name?.ToLowerInvariant()
                    ?? packId;
        var local = type.GetCustomAttribute<NodeInfoAttribute>()?.Id ?? type.Name;
        return owner + "/" + local;
    }

    private static object? TypeDefault(Type t) => t.IsValueType ? Activator.CreateInstance(t) : null;

    /// <summary>Enum se ukládá jako jméno hodnoty; ostatní se serializují, jak jsou.</summary>
    private static object? Literal(object? value) =>
        value?.GetType().IsEnum == true ? value.ToString() : value;

    private static IEnumerable<Type> SafeGetTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }
}
