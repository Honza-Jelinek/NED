using Blazor.Diagrams.Core.Models;

namespace NED.Core;

public enum ProblemSource { Validation, Operation }

/// <summary>Jeden řádek v Problems panelu — validační issue nebo operační chyba/varování.</summary>
public sealed record ProblemEntry(
    ProblemSource Source,
    NedNoticeSeverity Severity,
    string MessageKey,
    object?[]? Args,
    NodeModel? Node,
    NedNoticeAction? Action,
    DateTime TimestampUtc)
{
    public Guid Id { get; } = Guid.NewGuid();
}

/// <summary>
/// Scoped sběrnice pro Problems panel (stejný vzor jako <see cref="NodeInspector"/>) — panel se
/// renderuje v portal vrstvě mimo cascading scope NedCanvasu. Validační položky se při každé
/// revalidaci nahrazují vcelku (auto-refresh); operační položky se přidávají a zůstávají do
/// ručního smazání. Všechny mutace očekává výhradně z UI vlákna (volající si InvokeAsync
/// zajišťuje sám před voláním).
/// </summary>
public sealed class ProblemsService
{
    private const int MaxOperationEntries = 200;
    private readonly List<ProblemEntry> _operations = new();

    public IReadOnlyList<ProblemEntry> ValidationEntries { get; private set; } = Array.Empty<ProblemEntry>();
    public IReadOnlyList<ProblemEntry> OperationEntries => _operations;

    public int ErrorCount =>
        ValidationEntries.Count(e => e.Severity == NedNoticeSeverity.Error) +
        _operations.Count(e => e.Severity == NedNoticeSeverity.Error);

    public int WarningCount =>
        ValidationEntries.Count(e => e.Severity == NedNoticeSeverity.Warning) +
        _operations.Count(e => e.Severity == NedNoticeSeverity.Warning);

    /// <summary>Voláno při přepnutí/refreshi validace i pro klik na položku (focus, dialog…).</summary>
    public event Action? Changed;

    /// <summary>Nastavuje NedCanvas — exekutor prokliku (focus node, otevřít dialog…).</summary>
    public Func<ProblemEntry, Task>? ExecuteAction { get; set; }

    public void SetValidation(IEnumerable<ProblemEntry> entries)
    {
        ValidationEntries = entries.ToList();
        Changed?.Invoke();
    }

    public void AddOperation(ProblemEntry entry)
    {
        _operations.Insert(0, entry);
        while (_operations.Count > MaxOperationEntries)
            _operations.RemoveAt(_operations.Count - 1);
        Changed?.Invoke();
    }

    public void RemoveOperation(Guid id)
    {
        if (_operations.RemoveAll(e => e.Id == id) > 0)
            Changed?.Invoke();
    }

    public void ClearOperations()
    {
        if (_operations.Count == 0) return;
        _operations.Clear();
        Changed?.Invoke();
    }
}
