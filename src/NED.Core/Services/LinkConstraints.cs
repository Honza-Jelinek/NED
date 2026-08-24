using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using NED.Abstractions.Manifest;

namespace NED.Core;

/// <summary>
/// Co se stane se sousedními linky, když uživatel dotáhne nový. Čistá práce s grafem —
/// bydlí mimo <see cref="NedCanvas"/>, aby na to šly napsat testy bez renderu komponenty.
///
/// Pravidla se aplikují na <b>právě dotažený</b> link; ruší se ty ostatní, takže nový
/// vždycky vyhraje (známé „přetáhni jinam a starý spoj zmizí").
/// </summary>
public static class LinkConstraints
{
    public static void Apply(BaseLinkModel link)
    {
        // Duplicity první: dva linky mezi týmž párem portů nepřidají nic, jen se překreslí
        // přes sebe a v exportu z nich vypadne dvakrát totéž. Platí i pro Multiple vstupy,
        // které jinak víc linků přijímají — víc linků od RŮZNÝCH producentů.
        RemoveDuplicates(link);
        SingleInput(link);
        SingleExecOutput(link);
    }

    /// <summary>Neposílatelný vstup drží max jeden link; <c>Multiple</c> je pole a bere víc.</summary>
    private static void SingleInput(BaseLinkModel link)
    {
        var input = PortOf(link, PortAlignment.Left);
        if (input is null || input.Multiple) return;
        RemoveOthers(link, input);
    }

    /// <summary>
    /// Exec výstup říká „co se provede dál" — dvě pokračování naráz nedávají smysl.
    /// Datové výstupy se naopak větví běžně (na to je v exportu <c>$ref</c>), takže tohle
    /// je jediné místo, kde se omezuje výstupní strana.
    /// </summary>
    private static void SingleExecOutput(BaseLinkModel link)
    {
        var output = PortOf(link, PortAlignment.Right);
        if (output is null || output.DataType != TypeIds.Exec) return;
        RemoveOthers(link, output);
    }

    private static void RemoveDuplicates(BaseLinkModel link)
    {
        if (link.Source.Model is not PortModel source || link.Target.Model is not PortModel target) return;

        foreach (var other in source.Links.Where(candidate => candidate != link).ToList())
        {
            var otherSource = other.Source.Model as PortModel;
            var otherTarget = other.Target.Model as PortModel;
            if ((otherSource == source && otherTarget == target)
                || (otherSource == target && otherTarget == source))
                link.Diagram?.Links.Remove(other);
        }
    }

    private static void RemoveOthers(BaseLinkModel link, PortModel port)
    {
        foreach (var other in port.Links.Where(candidate => candidate != link).ToList())
            link.Diagram?.Links.Remove(other);
    }

    private static TypedPortModel? PortOf(BaseLinkModel link, PortAlignment alignment)
    {
        if (link.Source.Model is TypedPortModel source && source.Alignment == alignment) return source;
        if (link.Target.Model is TypedPortModel target && target.Alignment == alignment) return target;
        return null;
    }
}
