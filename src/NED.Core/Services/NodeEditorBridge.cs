using Blazor.Diagrams.Core.Models;

namespace NED.Core;

/// <summary>
/// Můstek z node widgetů (uvnitř ZBD canvasu) zpět do <c>NedCanvas</c>.
/// Cascaduje se dolů (IsFixed) — widget sám NedCanvas nezná.
/// </summary>
/// <param name="CurrentGraphId">
/// Live GUID právě editovaného grafu (čte se za běhu, proto Func — bridge je fixed,
/// ale aktivní tab se mění). Slouží k vyloučení self-reference v class dropdownech.
/// </param>
/// <param name="OpenInputMenu">
/// Otevře kontext menu vstupu na pozici kurzoru. Parametry: je teď port?,
/// akce která režim přepne, clientX, clientY. Widget si toggle akci sestaví sám,
/// takže můstek je node-agnostický (DataNode i SubgraphNode).
/// </param>
/// <param name="OpenSubgraph">
/// Otevře subgraf (podle GUID) k editaci v novém tabu — double-click na SubgraphNode.
/// Drill-in: nový tab dostane rodiče pro breadcrumb. Stale ref (smazaný asset) = no-op.
/// </param>
/// <param name="IssuesFor">Validační problémy konkrétního nodu (pro badge/outline). Prázdné = OK.</param>
/// <param name="Revalidate">Požádá NedCanvas o přepočet validace (po field/type změně ve widgetu).</param>
public sealed record NodeEditorBridge(
    Func<Guid> CurrentGraphId,
    Action<bool, Action, double, double> OpenInputMenu,
    Action RecordUndo,
    Action<Guid> OpenSubgraph,
    Func<NodeModel, IReadOnlyList<ValidationIssue>> IssuesFor,
    Action Revalidate);
