using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace NED.Core;

/// <summary>
/// Code-behind dokovacího hostu. Je jediným zdrojem pravdy o dokovatelných panelech:
/// sbírá deklarativně zaregistrované <see cref="NedPanel"/>y, renderuje jejich obsah do
/// portálů, předá jejich popis do ned-dock.js (golden-layout) a vygeneruje z nich
/// WINDOW ▸ Panels příkazy. Dál marshaluje JS callbacky: perzistenci layoutu a
/// notifikaci o změně velikosti editoru (kvůli překreslení plátna).
/// </summary>
public partial class NedDockHost : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private LayoutStore Layout { get; set; } = default!;

    /// <summary>Deklarace dokovatelných panelů (<see cref="NedPanel"/> děti).</summary>
    [Parameter] public RenderFragment? Panels { get; set; }

    /// <summary>Obsah pevného editoru (tab bar + breadcrumb + plátno).</summary>
    [Parameter] public RenderFragment? Editor { get; set; }

    /// <summary>Registr příkazů — host do něj generuje WINDOW ▸ Panels toggly.</summary>
    [Parameter] public NedCommandRegistry? Commands { get; set; }

    /// <summary>Vyvolá se po (pře)registraci panelových příkazů — rodič překreslí menubar.</summary>
    [Parameter] public EventCallback OnCommandsChanged { get; set; }

    /// <summary>Vyvolá se, když golden-layout změní velikost editoru — host překreslí plátno.</summary>
    [Parameter] public EventCallback OnEditorResized { get; set; }

    private readonly List<NedPanel> _panels = [];
    private ElementReference _host;
    private IJSObjectReference? _module;
    private DotNetObjectReference<NedDockHost>? _selfRef;
    private bool _initialized;

    /// <summary>Zaregistruje panel (volá <see cref="NedPanel"/> při inicializaci).</summary>
    public void RegisterPanel(NedPanel panel)
    {
        if (!_panels.Contains(panel)) _panels.Add(panel);
    }

    /// <summary>Odregistruje panel (volá <see cref="NedPanel"/> při dispose).</summary>
    public void UnregisterPanel(NedPanel panel) => _panels.Remove(panel);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_initialized) return;

        if (firstRender)
        {
            // Panely se zaregistrovaly až během prvního renderu (cascading děti), takže
            // jejich portály ještě nejsou v DOM. Vynuť druhý render, který je vykreslí.
            StateHasChanged();
            return;
        }

        // Druhý render hotový → portály jsou v DOM, golden-layout je najde podle data-portal.
        _initialized = true;
        await InitDock();
    }

    private async Task InitDock()
    {
        _selfRef = DotNetObjectReference.Create(this);
        _module = await JS.InvokeAsync<IJSObjectReference>(
            "import", "./_content/NED.Core/ned-dock.js");

        var saved = await Layout.LoadAsync();
        var panelDtos = _panels
            .Select(p => new { id = p.Id, title = p.Title, width = p.DefaultWidth, dock = p.Dock, height = p.DefaultHeight })
            .ToArray();
        await _module.InvokeVoidAsync("init", _host, _selfRef, saved, panelDtos);

        RegisterPanelCommands();
        await OnCommandsChanged.InvokeAsync();
    }

    /// <summary>
    /// Vygeneruje WINDOW ▸ Panels toggly z registrovaných panelů. Vzor jako
    /// NedCanvas.RefreshDynamicCommands — postaví podstrom od nuly.
    /// </summary>
    private void RegisterPanelCommands()
    {
        if (Commands is null) return;

        Commands.Remove("Window/Panels");
        var root = Commands.GetOrCreate("Window/Panels");
        root.Localize = false;   // „Panels" je vlastní jméno, ne lokalizační klíč

        foreach (var p in _panels)
        {
            var id = p.Id;
            root.Children.Add(new NedCommandNode(p.Title, $"Window/Panels/{p.Title}")
            {
                Icon = p.Icon,
                Localize = false,   // titulek panelu je vlastní jméno
                Execute = async () => await TogglePanel(id),
            });
        }
    }

    /// <summary>JS hlásí změnu layoutu (debounced) — uložíme přes host.</summary>
    [JSInvokable]
    public Task OnLayoutPersist(string json) => Layout.SaveAsync(json);

    /// <summary>JS hlásí, že se editor přerozměřil — překresli plátno.</summary>
    [JSInvokable]
    public Task NotifyEditorResized() => OnEditorResized.InvokeAsync();

    /// <summary>Skryje/zobrazí dokovatelný panel (zavře/přidá komponentu). Editor je zamčený, neskrývá se.</summary>
    public async Task TogglePanel(string id)
    {
        if (_module is not null)
            await _module.InvokeVoidAsync("togglePanel", id);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_module is not null)
            {
                await _module.InvokeVoidAsync("dispose");
                await _module.DisposeAsync();
            }
        }
        catch { /* circuit už může být zavřený */ }
        _selfRef?.Dispose();
    }
}
