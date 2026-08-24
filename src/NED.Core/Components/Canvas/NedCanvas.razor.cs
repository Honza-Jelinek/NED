using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using NED.Core.Resources;
using Microsoft.AspNetCore.Components.Web;
using LanguageRegistry = NED.Core.Resources.LanguageRegistry;
using Microsoft.JSInterop;
using MudBlazor;
using NED.Abstractions;
using NED.Abstractions.Manifest;
using NED.Core.Assets;
using NED.Core.Persistence;
using NED.Core.NodePacks;
using Blazor.Diagrams;
using Blazor.Diagrams.Core.Behaviors;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;

namespace NED.Core;

/// <summary>
/// Drop-in komponenta NEDa: menu bar + tab bar + library panel + plátno.
/// Vizuální části jsou v dílčích komponentách; tady žije stav a logika.
/// File I/O (dialogy) řeší host přes callbacky — Core neví o WPF/OS.
/// </summary>
public partial class NedCanvas : IAsyncDisposable
{
    [Inject] private NedCatalog Catalog { get; set; } = default!;
    [Inject] private NedTheme Theme { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private AssetIndex AssetIndex { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IStringLocalizer<Resources.Strings> Loc { get; set; } = default!;
    [Inject] private ShutdownGuard Shutdown { get; set; } = default!;
    [Inject] private LanguageRegistry LangRegistry { get; set; } = default!;
    [Inject] private LayoutStore Layout { get; set; } = default!;
    [Inject] private INedNotifier Notifier { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private ProblemsService Problems { get; set; } = default!;

    private readonly MudTheme _mudTheme = new()
    {
        PaletteDark = new PaletteDark()
        {
            Primary = "#377799",
            Secondary = "#34C5BD",
            Background = "#0F111B",
            Surface = "#131D30",
            AppbarBackground = "#131D30",
            DrawerBackground = "#131D30",
            TextPrimary = "#E7EDF2",
            TextSecondary = "#95A7B5",
            Divider = "#294157",
            LinesDefault = "#294157",
            ActionDefault = "#34C5BD",
            ActionDisabled = "#687B89",
            ActionDisabledBackground = "#18243A"
        }
    };

    private readonly List<TabBase> _tabs = new();
    private readonly NavigationHistory _history = new();
    private readonly NedCommandRegistry _commands = new();
    private List<ValidationIssue> _issues = new();
    private Dictionary<NodeModel, IReadOnlyList<ValidationIssue>> _issuesByNode = new();
    private readonly HashSet<Guid> _catalogProblemIds = new();
    private TabBase? _active;
    private bool _paletteOpen;
    private bool _pendingRefresh;
    private bool _firstCanvasFixed;
    private bool _loading;
    private int _canvasGen;
    private IJSObjectReference? _keysModule;
    private DotNetObjectReference<NedCanvas>? _selfRef;
    private NedDockHost? _dockHost;

    // ── Node picker state ──────────────────────────
    private bool _pickerOpen;
    private double _pickerX, _pickerY;                 // client coords (popup + node placement)
    private PortModel? _linkSource;                     // zdroj při dropu linku (auto-connect)
    private PortModel? _dragSourcePort;                 // tracking ongoing-link drag
    private Func<NodeTypeDescriptor, bool>? _pickerFilter;
    private Func<AssetEntry, bool>? _pickerSubFilter;
    private string? _pickerContext;

    // ── Node context menu state ────────────────────
    private bool _nodeMenuOpen;
    private double _nodeMenuX, _nodeMenuY;
    private NodeModel? _nodeMenuTarget;

    // ── Input (port↔pole) context menu state ───────
    private NodeEditorBridge? _bridge;
    private bool _inputMenuOpen;
    private double _inputMenuX, _inputMenuY;
    private bool _inputMenuIsPort;
    private Action? _inputMenuToggle;

    // ── Box selection (Shift+drag) state ───────────
    private bool _boxActive;
    private bool _boxMoved;
    private double _boxStartX, _boxStartY, _boxCurX, _boxCurY;

    private double BoxLeft => Math.Min(_boxStartX, _boxCurX);
    private double BoxTop => Math.Min(_boxStartY, _boxCurY);
    private double BoxWidth => Math.Abs(_boxCurX - _boxStartX);
    private double BoxHeight => Math.Abs(_boxCurY - _boxStartY);

    /// <summary>Aktivní záložka.</summary>
    public TabBase? Active => _active;

    private EditorTab? ActiveEditor => _active as EditorTab;

    // ── Host callbacks ──────────────────────────────

    /// <summary>Quick-save: zapíše JSON na danou cestu. Host neotevírá dialog.</summary>
    [Parameter] public Func<string, string, Task>? OnWriteFile { get; set; }

    /// <summary>Save-as: host otevře dialog, vrátí zvolenou cestu (null = zrušeno).</summary>
    [Parameter] public Func<string, Task<string?>>? OnSaveAsRequested { get; set; }

    /// <summary>Save-as pro instance (.nedinst.json): host otevře dialog s příslušným filtrem. Parametry: (json, suggestedFileName).</summary>
    [Parameter] public Func<string, string?, Task<string?>>? OnSaveAsInstanceRequested { get; set; }

    /// <summary>Host dodá callback pro načtení souboru. Vrací (json, filePath), nebo null = zrušeno.</summary>
    [Parameter] public Func<Task<(string json, string path)?>>? OnLoadRequested { get; set; }

    /// <summary>Host dodá callback pro export. Parametr: čistý export JSON.</summary>
    [Parameter] public Func<string, Task>? OnExportRequested { get; set; }

    /// <summary>Host dodá callback pro výběr složky (WPF folder dialog). Vrací cestu, nebo null = zrušeno.</summary>
    [Parameter] public Func<Task<string?>>? OnPickFolderRequested { get; set; }

    /// <summary>Host vybere hotový node-pack manifest v nativním file dialogu.</summary>
    [Parameter] public Func<Task<string?>>? OnPickNodePackManifestRequested { get; set; }

    /// <summary>Host vybere zdroj node packu podle schopností zvoleného generator provideru.</summary>
    [Parameter] public Func<NodePackGeneratorDescriptor, Task<string?>>? OnPickNodePackSourceRequested { get; set; }

    /// <summary>Host otevře soubor v systémovém Průzkumníku (vyznačí ho). OS-specifické.</summary>
    [Parameter] public Func<string, Task>? OnRevealInExplorer { get; set; }

    /// <summary>Host otevře soubor v externím editoru (VS Code). OS-specifické.</summary>
    [Parameter] public Func<string, Task>? OnOpenInEditor { get; set; }

    /// <summary>Host smaže soubor (do koše). Vrací true = úspěšně smazáno. OS-specifické.</summary>
    [Parameter] public Func<string, Task<bool>>? OnDeleteFile { get; set; }

    /// <summary>Uživatel zvolil jazyk v menu. Host (composition root) ho perzistuje
    /// a aplikuje na celý proces — Core jen prohlásí volbu (culture kód, např. "en"/"cs").</summary>
    [Parameter] public EventCallback<string> OnLanguageChanged { get; set; }

    /// <summary>Host uloží layout dokovatelných panelů (golden-layout JSON) na fixní cestu.</summary>
    [Parameter] public Func<string, Task>? OnSaveLayout { get; set; }

    /// <summary>Host přečte uložený layout (JSON), nebo null pokud nic uloženo není.</summary>
    [Parameter] public Func<Task<string?>>? OnLoadLayout { get; set; }

    // ── Computed ─────────────────────────────────────

    private GraphSettings Settings => ActiveEditor?.Settings ?? new();

    private string OutputTypeLabel =>
        _active is null ? "No graph loaded"
        : _active is InstanceTab it ? $"Instance • {it.Data.TemplateName}"
        : $"{Settings.Flow} • Outputs: {DeclaredOutputsLabel}";

    private string DeclaredOutputsLabel => Settings.Outputs.Count == 0
        ? "—"
        : string.Join(", ", Settings.Outputs.Select(output =>
            $"{output.Name}: {NED.Abstractions.Manifest.TypeIds.Friendly(output.Type)}{(output.Multiple ? "[]" : "")}"));

    private string StatusText
    {
        get
        {
            if (_active is null) return OutputTypeLabel;
            if (ActiveEditor is not { } ed) return OutputTypeLabel;

            var s = $"{OutputTypeLabel}  •  Nodes: {ed.Diagram.Nodes.Count}   Links: {ed.Diagram.Links.Count}";

            var errors = _issues.Count(i => i.Severity == IssueSeverity.Error);
            var warnings = _issues.Count(i => i.Severity == IssueSeverity.Warning);
            if (errors > 0) s += $"   •  ⛔ {errors}";
            if (warnings > 0) s += $"{(errors > 0 ? "  " : "   •  ")}⚠ {warnings}";
            return s;
        }
    }

    /// <summary>Tooltip statusbaru — lokalizovaný výpis všech problémů (vč. graf-level bez nodu).</summary>
    private string? StatusIssueTooltip =>
        _issues.Count == 0 ? null
        : string.Join("\n", _issues.Select(LocalizeIssue));

    /// <summary>Naformátuje issue přes lokalizátor (klíč + args).</summary>
    private string LocalizeIssue(ValidationIssue issue) =>
        issue.Args is { Length: > 0 }
            ? string.Format(Loc[issue.MessageKey], issue.Args)
            : Loc[issue.MessageKey].Value;

    // ── Validace ─────────────────────────────────────

    /// <summary>Přepočítá validaci aktivního grafu a překreslí dotčené nody.</summary>
    private void Revalidate()
    {
        var affected = new HashSet<NodeModel>(_issuesByNode.Keys);   // staré

        if (ActiveEditor is { } ed)
        {
            foreach (var node in ed.Diagram.Nodes.OfType<DataNodeModel>()
                         .Where(node => node.DeclaresGraphOutputs))
                node.SyncDeclaredInputs(ed.Settings.Outputs);

            _issues = GraphValidator.Validate(ed.Diagram, AssetIndex, ed.Settings);
            _issuesByNode = _issues
                .Where(i => i.Node is not null)
                .GroupBy(i => i.Node!)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<ValidationIssue>)g.ToList());
        }
        else if (_active is InstanceTab it && it.TemplateInterface is null)
        {
            // Šablona instance neexistuje (smazaná/nenačitatelná) — bez ní nejde editovat ani exportovat.
            _issues = new() { new(IssueSeverity.Error, "Validation_MissingTemplate", null, [it.Data.TemplateName]) };
            _issuesByNode = new();
        }
        else
        {
            _issues = new();
            _issuesByNode = new();
        }

        foreach (var n in _issuesByNode.Keys) affected.Add(n);       // nové
        foreach (var n in affected) n.Refresh();

        Problems.SetValidation(_issues.Select(i => new ProblemEntry(
            ProblemSource.Validation,
            i.Severity == IssueSeverity.Error ? NedNoticeSeverity.Error : NedNoticeSeverity.Warning,
            i.MessageKey, i.Args, i.Node, ActionFor(i), DateTime.UtcNow)));

        // Details panel čte deklarace výstupů z diagramu — po přidání/smazání Output uzlu
        // nebo přejmenování jeho Labelu musí seznam v panelu odpovídat plátnu.
        PushInspectorState();
    }

    private NedNoticeAction? ActionFor(ValidationIssue i) => i switch
    {
        { MessageKey: "Validation_StaleSubgraph", Node: SubgraphNodeModel sg } => new CreateMissingSubgraphAction(sg),
        { MessageKey: "Validation_MissingTemplate" } when _active is InstanceTab it => new CreateMissingTemplateAction(it),
        { Node: not null } => new FocusNodeAction(i.Node!),
        _ => null,
    };

    private void RevalidateAndRender()
    {
        Revalidate();
        InvokeAsync(StateHasChanged);
    }

    private IReadOnlyList<ValidationIssue> IssuesFor(NodeModel node) =>
        _issuesByNode.TryGetValue(node, out var list) ? list : Array.Empty<ValidationIssue>();

    /// <summary>Vycentruje viewport aktivního tabu na daný node (no-op, pokud node do aktivního diagramu nepatří).</summary>
    private void FocusNode(NodeModel node)
    {
        if (ActiveEditor is not { } ed || !ed.Diagram.Nodes.Contains(node)) return;
        var container = ed.Diagram.Container;
        if (container is null) return;
        var zoom = ed.Diagram.Zoom;

        var cx = container.Width / 2;
        var cy = container.Height / 2;
        ed.Diagram.SetPan(cx - node.Position.X * zoom, cy - node.Position.Y * zoom);
        ed.Diagram.SelectModel(node, unselectOthers: true);
        ed.Diagram.Refresh();
    }

    private void GoToOutputNode()
    {
        if (ActiveEditor is not { } editor) return;

        var root = editor.Diagram.Nodes.OfType<DataNodeModel>()
            .FirstOrDefault(node => editor.Settings.IsExec
                ? node.IsExecEntryNode
                : node.IsOutputNode);
        if (root is not null) FocusNode(root);
    }

    private async Task OnProblemAction(ProblemEntry e)
    {
        switch (e.Action)
        {
            case FocusNodeAction f:
                FocusNode(f.Node);
                break;
            case CreateMissingSubgraphAction c:
                await OpenCreateSubgraphDialog(c.Node);
                break;
            case CreateMissingTemplateAction t:
                await OpenCreateTemplateDialog(t.Tab);
                break;
            case null when e.Node is not null:
                FocusNode(e.Node);
                break;
        }
    }

    private bool HasBuiltIn(string typeId) =>
        ActiveEditor?.Diagram.Nodes.OfType<DataNodeModel>().Any(node => node.TypeId == typeId) == true;

    private bool ShowInPalette(NodeTypeDescriptor t) =>
        BuiltInPaletteVisible(t, Settings, HasBuiltIn);

    /// <summary>
    /// Které vestavěné uzly dávají v tomhle grafu smysl. Rozhoduje jen tok — deklarace
    /// návratů sedí na grafu, takže Output uzel není co gatovat rolí; je to sink datového
    /// toku a v exec toku ho nahrazují Return uzly.
    ///
    /// Paleta je pohodlí, ne bezpečnost: singletony vynucuje validátor, protože soubor
    /// jde upravit ručně a undo umí singleton obnovit.
    /// </summary>
    internal static bool BuiltInPaletteVisible(
        NodeTypeDescriptor t, GraphSettings settings, Func<string, bool> hasBuiltIn) => t.Id switch
    {
        Manifest.BuiltInIds.GraphInput => true,
        Manifest.BuiltInIds.ExecEntry => settings.IsExec && !hasBuiltIn(Manifest.BuiltInIds.ExecEntry),
        Manifest.BuiltInIds.Output => !settings.IsExec && !hasBuiltIn(Manifest.BuiltInIds.Output),
        Manifest.BuiltInIds.Return => settings.IsExec,

        // Uzel s řídicím pinem v datovém grafu jen zabírá místo — exec hrany do exportu
        // nejdou. Opačně to neplatí: čisté datové uzly v exec grafu počítají vstupy kroků.
        _ => settings.IsExec || !NedCatalog.HasExecPort(t),
    };

    // ── Lifecycle ────────────────────────────────────

    protected override void OnInitialized()
    {
        AssetIndex.Changed += OnIndexChanged;
        Catalog.Changed += OnCatalogChanged;
        Notifier.Notified += OnNotice;
        Problems.ExecuteAction = OnProblemAction;
        Layout.SaveCallback = OnSaveLayout;
        Layout.LoadCallback = OnLoadLayout;
        _bridge = new NodeEditorBridge(() => ActiveEditor?.Settings.Id ?? Guid.Empty, OpenInputMenu, RecordUndo, OpenSubgraphFromNode, IssuesFor, RevalidateAndRender);
        Inspector.GraphEdited += OnGraphEdited;
        Shutdown.ConfirmClose = ConfirmCloseFromHost;
        _active = null;
        RegisterCommands();

        // Katalog vzniká dřív než zbytek DI, takže si problémy z načítání packů odkládá
        // stranou — tady je poprvé kam je poslat.
        PublishCatalogIssues();
    }

    private void OnCatalogChanged() => InvokeAsync(() =>
    {
        _loading = true;
        try
        {
            foreach (var tab in _tabs.OfType<EditorTab>())
                ReloadTab(tab, Catalog, AssetIndex, SubscribeDiagram);
        }
        finally
        {
            _loading = false;
        }

        PublishCatalogIssues();
        Revalidate();
        PushInspectorState();
        _pendingRefresh = true;
        StateHasChanged();
    });

    internal static bool ReloadTab(
        EditorTab tab,
        NedCatalog catalog,
        AssetIndex? assetIndex,
        Action<BlazorDiagram>? subscribeDiagram = null)
    {
        var diagram = tab.Diagram;
        var selectedIds = diagram.GetSelectedModels().OfType<NodeModel>()
            .Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var panX = diagram.Pan.X;
        var panY = diagram.Pan.Y;
        var zoom = diagram.Zoom;
        var document = GraphPersistence.ToDocument(diagram, tab.Settings, catalog);
        var before = GraphPersistence.Serialize(document);

        tab.Settings = GraphPersistence.LoadInto(diagram, document, catalog, assetIndex);
        subscribeDiagram?.Invoke(diagram);
        diagram.SetPan(panX, panY);
        diagram.SetZoom(zoom);
        foreach (var node in diagram.Nodes.Where(node => selectedIds.Contains(node.Id)))
            diagram.SelectModel(node, unselectOthers: false);

        tab.Added = diagram.Nodes.Count;
        var after = GraphPersistence.Serialize(GraphPersistence.ToDocument(diagram, tab.Settings, catalog));
        if (before == after) return false;

        tab.Undo.Reset(diagram, tab.Settings);
        tab.IsDirty = true;
        return true;
    }

    private void PublishCatalogIssues()
    {
        foreach (var id in _catalogProblemIds) Problems.RemoveOperation(id);
        _catalogProblemIds.Clear();

        foreach (var issue in Catalog.Issues)
        {
            var entry = new ProblemEntry(ProblemSource.Operation, issue.Severity, issue.MessageKey,
                issue.Args, null, issue.Action, DateTime.UtcNow);
            _catalogProblemIds.Add(entry.Id);
            Problems.AddOperation(entry);
        }
    }

    private void RegisterCommands()
    {
        _commands.RegisterFromAttributes(this);

        _commands.Configure("File/Save", c => c.Enabled = () => _active is not null);
        _commands.Configure("File/Save As", c => c.Enabled = () => _active is not null);
        _commands.Configure("File/Export", c => c.Enabled = () => _active is not null);
        _commands.Configure("Edit/Undo", c => c.Enabled = () => ActiveEditor?.Undo.CanUndo == true);
        _commands.Configure("Edit/Redo", c => c.Enabled = () => ActiveEditor?.Undo.CanRedo == true);
        _commands.Configure("Navigate/Back", c => c.Enabled = () => _history.CanBack);
        _commands.Configure("Navigate/Forward", c => c.Enabled = () => _history.CanForward);

        // WINDOW ▸ Panels generuje NedDockHost z registrovaných <NedPanel> (po prvním renderu).

        foreach (var lang in LangRegistry.Languages)
        {
            var culture = lang.Culture;
            var node = _commands.GetOrCreate($"Window/Language/{lang.NativeName}");
            node.Localize = false;   // NativeName je vlastní jméno jazyka, ne klíč
            node.Execute = async () => await SetLanguage(culture);
        }
    }

    private void RefreshDynamicCommands()
    {
        // Vždy postav od nuly — jinak by po zavření všech tabů zůstal prázdný "Tabs" uzel.
        _commands.Remove("Tabs");
        if (_tabs.Count == 0) return;

        var tabsRoot = _commands.GetOrCreate("Tabs");
        tabsRoot.Order = 100;
        foreach (var tab in _tabs)
        {
            var t = tab;
            // Přímo přidáváme child (ne GetOrCreate) — dva taby můžou mít stejný název.
            tabsRoot.Children.Add(new NedCommandNode(tab.Title, $"Tabs/{tab.Title}")
            {
                Icon = tab.Icon,
                Localize = false,   // titulek tabu je vlastní jméno
                Execute = () => { SwitchTab(t); return Task.CompletedTask; },
            });
        }
    }

    private void OpenPalette()
    {
        // Ctrl+K funguje jako toggle.
        if (_paletteOpen) { ClosePalette(); return; }
        RefreshDynamicCommands();
        _paletteOpen = true;
        StateHasChanged();
    }

    private void ClosePalette()
    {
        _paletteOpen = false;
        StateHasChanged();
    }

    /// <summary>
    /// Volá host (MainWindow.OnClosing) z UI vlákna při pokusu o zavření okna.
    /// Marshaluje na Blazor dispatcher, ať dialogy renderují korektně.
    /// </summary>
    private async Task<bool> ConfirmCloseFromHost()
    {
        var ok = true;
        await InvokeAsync(async () => ok = await ConfirmUnsavedTabs());
        return ok;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _selfRef = DotNetObjectReference.Create(this);
            _keysModule = await JS.InvokeAsync<IJSObjectReference>(
                "import", "./_content/NED.Core/ned-keys.js");
            await _keysModule.InvokeVoidAsync("register", _selfRef);
        }

        if (_pendingRefresh && ActiveEditor is not null)
        {
            _pendingRefresh = false;

            if (!_firstCanvasFixed)
            {
                _firstCanvasFixed = true;
                if (_keysModule is not null)
                    await _keysModule.InvokeVoidAsync("whenLayoutStable");
                _canvasGen++;
                StateHasChanged();
            }
            else
            {
                ActiveEditor.Diagram.Refresh();
            }
        }
    }

    /// <summary>golden-layout přerozměřil/přesunul editor (dock/split/drag/init) — srovnej
    /// kontejner i porty Z.Blazor.Diagrams se skutečnou pozicí plátna a překresli.</summary>
    private async Task OnEditorResized()
    {
        if (ActiveEditor is null) return;

        // 1) Srovnej Container. golden-layout polohuje plátno přes inline style (left/top);
        //    to ZBD ResizeObserver (jen velikost) ani jeho MutationObserver (jen childList)
        //    nezachytí, takže Diagram.Container zůstane na původní pozici a VŠECHNY převody
        //    klient↔diagram se rozjedou (link se kreslí vedle kurzoru, hover/snapping míří
        //    mimo port). Refresh() Container NEpřečte — přečteme skutečný rect a vnutíme ho.
        if (_keysModule is not null)
        {
            await _keysModule.InvokeVoidAsync("whenLayoutStable");
            var rect = await _keysModule.InvokeAsync<Rectangle?>("canvasRect");
            if (rect is not null)
                ActiveEditor.Diagram.SetContainer(rect);
        }

        // 2) Přeměř porty proti (teď správnému) Containeru. ZBD měří pozice portů jen JEDNOU
        //    (PortRenderer: dokud !Initialized) a uloží je relativně k tehdejšímu Containeru;
        //    Refresh() je nepřeměří. Bez tohoto by zůstaly „zamrzlé" a snapping by míchal staré
        //    souřadnice portů s živým kurzorem. Viz PortModel.Initialized.
        foreach (var port in ActiveEditor.Diagram.Nodes.SelectMany(n => n.Ports))
        {
            port.Initialized = false;
            port.Refresh();
        }

        ActiveEditor.Diagram.Refresh();
    }

    /// <summary>Volá JS listener pro globální zkratky (Ctrl+S / Ctrl+Shift+S).</summary>
    [JSInvokable]
    public async Task HandleShortcut(string action)
    {
        if (action == "save") await OnSave();
        else if (action == "saveas") await OnSaveAs();
        else if (action == "undo") PerformUndo();
        else if (action == "redo") PerformRedo();
        else if (action == "back") NavigateBack();
        else if (action == "forward") NavigateForward();
        else if (action == "palette") OpenPalette();
        else if (action == "home") GoToOutputNode();
    }

    /// <summary>INedNotifier.Notified může přijít z libovolného vlákna (watcher) — marshaluj na UI.
    /// Info = krátké potvrzení → Snackbar. Warning/Error → trvalý záznam v Problems panelu.</summary>
    private void OnNotice(NedNotice n) => InvokeAsync(() =>
    {
        if (n.Severity == NedNoticeSeverity.Info)
        {
            var message = n.Args is { Length: > 0 } ? string.Format(Loc[n.MessageKey], n.Args) : Loc[n.MessageKey].Value;
            Snackbar.Add(message, Severity.Info);
            return;
        }

        Problems.AddOperation(new ProblemEntry(ProblemSource.Operation, n.Severity, n.MessageKey, n.Args, null, n.Action, DateTime.UtcNow));
        StateHasChanged();
    });

    public async ValueTask DisposeAsync()
    {
        AssetIndex.Changed -= OnIndexChanged;
        Catalog.Changed -= OnCatalogChanged;
        Notifier.Notified -= OnNotice;
        Problems.ExecuteAction = null;
        Inspector.GraphEdited -= OnGraphEdited;
        // Odregistruj most jen pokud patří téhle instanci (ne nově vytvořené).
        if (Shutdown.ConfirmClose == ConfirmCloseFromHost) Shutdown.ConfirmClose = null;
        try
        {
            if (_keysModule is not null)
            {
                await _keysModule.InvokeVoidAsync("unregister");
                await _keysModule.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NED keyboard module cleanup failed: {ex.Message}");
        }
        _selfRef?.Dispose();
    }

    // ── Node helpers ────────────────────────────────

    public DataNodeModel AddNode(NodeTypeDescriptor type)
    {
        if (ActiveEditor is not { } tab) return null!;
        var pos = new Point(120 + (tab.Added % 4) * 260, 80 + (tab.Added / 4) * 240);
        tab.Added++;
        var node = new DataNodeModel(type, pos, id: null, Catalog);
        node.SyncDeclaredInputs(Settings.Outputs);
        tab.Diagram.Nodes.Add(node);
        return node;
    }

    public SubgraphNodeModel AddSubgraphNode(AssetEntry asset)
    {
        if (ActiveEditor is not { } tab) return null!;
        var pos = new Point(120 + (tab.Added % 4) * 260, 80 + (tab.Added / 4) * 240);
        tab.Added++;
        var node = new SubgraphNodeModel(asset, pos);
        tab.Diagram.Nodes.Add(node);
        return node;
    }

    private async Task SetLanguage(string culture)
    {
        if (!await ConfirmUnsavedTabs()) return;
        await OnLanguageChanged.InvokeAsync(culture);
    }
}
