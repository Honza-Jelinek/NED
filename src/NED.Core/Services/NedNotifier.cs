using Blazor.Diagrams.Core.Models;

namespace NED.Core;

public enum NedNoticeSeverity { Info, Warning, Error }

/// <summary>Akce, kterou může notifikace nést (proklik z Problems panelu). Data-only — notice může vzniknout na non-UI vlákně.</summary>
public abstract record NedNoticeAction;

/// <summary>Vycentruje viewport aktivního tabu na daný node.</summary>
public sealed record FocusNodeAction(NodeModel Node) : NedNoticeAction;

/// <summary>Otevře dialog pro vytvoření chybějícího subgraf souboru odpovídajícího interface nodu.</summary>
public sealed record CreateMissingSubgraphAction(SubgraphNodeModel Node) : NedNoticeAction;

/// <summary>Otevře dialog pro vytvoření chybějícího template souboru pro danou instanci
/// (pole odvozená z <c>InstanceData.Values</c> — typy neznáme, stub je vytvoří jako string).</summary>
public sealed record CreateMissingTemplateAction(InstanceTab Tab) : NedNoticeAction;

/// <summary>Zpráva pro UI: klíč do Strings.resx + argumenty pro string.Format + volitelná akce (proklik).</summary>
public sealed record NedNotice(NedNoticeSeverity Severity, string MessageKey, object?[]? Args = null, NedNoticeAction? Action = null);

/// <summary>
/// Kanál pro hlášení chyb/varování z non-UI služeb (AssetIndex…) do UI (Snackbar / Problems panel).
/// Služby publikují resource klíče, ne hotový text — lokalizaci dělá až odběratel
/// v <see cref="NedCanvas"/>, který jediný zná <c>IStringLocalizer</c>.
/// </summary>
public interface INedNotifier
{
    /// <summary>Může přijít z libovolného vlákna (FileSystemWatcher) — odběratel musí marshalovat přes InvokeAsync.</summary>
    event Action<NedNotice>? Notified;
    void Info(string key, params object?[] args);
    void Warn(string key, params object?[] args);
    void Error(string key, params object?[] args);
    /// <summary>Plná kontrola — pro notice nesoucí akci.</summary>
    void Post(NedNotice notice);
}

public sealed class NedNotifier : INedNotifier
{
    public event Action<NedNotice>? Notified;

    public void Info(string key, params object?[] args) => Notified?.Invoke(new NedNotice(NedNoticeSeverity.Info, key, args));
    public void Warn(string key, params object?[] args) => Notified?.Invoke(new NedNotice(NedNoticeSeverity.Warning, key, args));
    public void Error(string key, params object?[] args) => Notified?.Invoke(new NedNotice(NedNoticeSeverity.Error, key, args));
    public void Post(NedNotice notice) => Notified?.Invoke(notice);
}
