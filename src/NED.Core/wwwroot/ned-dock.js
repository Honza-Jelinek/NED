// golden-layout interop pro NED.
//
// Model: VirtualLayout. golden-layout vlastní jen prázdný "gl-root" (splittery,
// hlavičky, drop-zóny). Skutečný obsah panelů renderuje Blazor do overlay vrstvy
// (.ne-portal-layer) a tento modul jen ABSOLUTNĚ POLOHUJE Blazor elementy podle
// obdélníků, které golden-layout hlásí. golden-layout nikdy nesáhne na Blazor DOM
// → odpadá celá třída bugů z přesouvání uzlů (drag, hide), co rozbila dockview.
//
// Popout (window.open = OS okno) je vypnutý — ve WebView2 nefunguje rozumně.

import { VirtualLayout, LayoutConfig } from './lib/golden-layout/golden-layout.esm.js';

// Jeden dock host na aplikaci.
let state = null;

// Mapa componentType → titulek tabu. Plní se z popisu panelů předaného z .NET
// (init), aby togglePanel uměl panel vrátit se správným titulkem.
let PANEL_TITLES = {};

/**
 * Sestaví výchozí golden-layout konfiguraci z popisu panelů.
 * @param panels  [{ id, title, width, dock, height }] — dokovatelné panely (bez editoru).
 *                dock: "side" (výchozí, řádek vedle editoru) nebo "bottom".
 * Layout: řádek = [side panely…, zamčený editor stack]; má-li nějaký panel dock="bottom",
 * celý tenhle řádek se obalí do sloupce a bottom panely se přidají pod něj.
 */
function buildDefaultConfig(panels) {
    const sidePanels = panels.filter(p => p.dock !== 'bottom');
    const bottomPanels = panels.filter(p => p.dock === 'bottom');

    const sideItems = sidePanels.map(p => ({
        type: 'component', componentType: p.id, title: p.title, width: p.width,
    }));
    // Zbytek šířky pro editor (100 − součet side panelů, min. 50 %).
    const usedWidth = sidePanels.reduce((s, p) => s + (p.width || 0), 0);
    const editorWidth = Math.max(50, 100 - usedWidth);

    const row = {
        type: 'row',
        content: [
            ...sideItems,
            {
                type: 'stack', width: editorWidth, isClosable: false, reorderEnabled: false,
                content: [
                    { type: 'component', componentType: 'editor', title: 'Editor', isClosable: false },
                ],
            },
        ],
    };

    let root = row;
    if (bottomPanels.length > 0) {
        const usedHeight = bottomPanels.reduce((s, p) => s + (p.height || 0), 0);
        row.height = Math.max(50, 100 - usedHeight);
        const bottomItems = bottomPanels.map(p => ({
            type: 'component', componentType: p.id, title: p.title, height: p.height,
        }));
        root = { type: 'column', content: [row, ...bottomItems] };
    }

    return {
        settings: {
            showPopoutIcon: false,        // žádná OS okna ve WebView2
            showMaximiseIcon: false,
            showCloseIcon: true,
            constrainDragToContainer: true,
            reorderEnabled: true,
        },
        dimensions: { headerHeight: 28, borderWidth: 4 },
        root,
    };
}

/**
 * Inicializuje dock. Voláno z NedDockHost po prvním renderu.
 * @param hostEl       .ne-dock-host element (obsahuje .ne-gl-root a .ne-portal-layer)
 * @param dotNetRef    DotNetObjectReference na NedDockHost
 * @param savedJson    uložený layout (JSON z getConfig) nebo null/prázdné
 * @param panels       [{ id, title, width }] — deklarované dokovatelné panely
 */
export function init(hostEl, dotNetRef, savedJson, panels) {
    dispose();

    const glRoot = hostEl.querySelector('.ne-gl-root');
    const portalLayer = hostEl.querySelector('.ne-portal-layer');
    if (!glRoot || !portalLayer) {
        console.error('ned-dock: chybí .ne-gl-root nebo .ne-portal-layer');
        return;
    }

    const panelList = panels ?? [];
    PANEL_TITLES = Object.fromEntries(panelList.map(p => [p.id, p.title]));

    const layout = new VirtualLayout(glRoot, bindComponent, unbindComponent);
    layout.resizeWithContainerAutomatically = true;   // GL si sám hlídá velikost rootu

    state = {
        layout, glRoot, portalLayer, dotNetRef,
        panels: panelList,
        glRootRect: null,
        saveTimer: 0,
        editorResizeTimer: 0,
    };

    // Před každou dávkou polohování si zacachuj rect rootu (perf).
    layout.beforeVirtualRectingEvent = () => {
        state.glRootRect = glRoot.getBoundingClientRect();
    };

    // Po každé změně layoutu (drag, dock, resize, zavření) ulož — debounced.
    layout.on('stateChanged', scheduleSave);

    loadInitial(savedJson);
}

function bindComponent(container, itemConfig) {
    const type = itemConfig.componentType;
    const el = state.portalLayer.querySelector(`[data-portal="${type}"]`);
    if (!el) {
        console.error(`ned-dock: nenalezen portal pro "${type}"`);
        return { virtual: true, component: undefined };
    }
    // golden-layout si "component" drží pro vlastní účely (unbind); vracíme element.

    el.style.position = 'absolute';

    container.virtualRectingRequiredEvent = (cont, width, height) => {
        const root = state.glRootRect ?? state.glRoot.getBoundingClientRect();
        const r = cont.element.getBoundingClientRect();
        el.style.left = (r.left - root.left) + 'px';
        el.style.top = (r.top - root.top) + 'px';
        el.style.width = width + 'px';
        el.style.height = height + 'px';
        el.style.display = 'block';   // pozor: '' by spadlo na CSS `.ne-portal{display:none}`
        if (type === 'editor') scheduleEditorResize();
    };

    container.virtualVisibilityChangeRequiredEvent = (cont, visible) => {
        el.style.display = visible ? 'block' : 'none';
    };

    container.virtualZIndexChangeRequiredEvent = (cont, logicalZIndex, defaultZIndex) => {
        el.style.zIndex = defaultZIndex;
    };

    return { virtual: true, component: el };
}

function unbindComponent(container) {
    // Blazor vlastní DOM panelu — nemažeme ho, jen schováme, kdyby panel zmizel z layoutu.
    const type = container.componentType;
    const el = state?.portalLayer.querySelector(`[data-portal="${type}"]`);
    if (el) el.style.display = 'none';
}

function loadInitial(savedJson) {
    if (savedJson) {
        try {
            const resolved = JSON.parse(savedJson);
            state.layout.loadLayout(LayoutConfig.fromResolved(resolved));
            return;
        } catch (e) {
            console.warn('ned-dock: neplatný uložený layout, beru default', e);
        }
    }
    state.layout.loadLayout(buildDefaultConfig(state.panels));
}

function scheduleSave() {
    if (!state) return;
    clearTimeout(state.saveTimer);
    state.saveTimer = setTimeout(() => {
        try {
            const json = JSON.stringify(state.layout.saveLayout());
            state.dotNetRef.invokeMethodAsync('OnLayoutPersist', json);
        } catch (e) { /* circuit může být zavřený */ }
    }, 500);
}

function scheduleEditorResize() {
    if (!state) return;
    clearTimeout(state.editorResizeTimer);
    state.editorResizeTimer = setTimeout(() => {
        try { state.dotNetRef.invokeMethodAsync('NotifyEditorResized'); }
        catch (e) { /* circuit může být zavřený */ }
    }, 80);
}

// ── Skrývání / zobrazování panelů ──────────────────────
// "Skrýt" = zavřít komponentu (zmizí i s tabem). "Zobrazit" = přidat zpět.
// Editor se nikdy neskrývá (je zamčený, isClosable:false). PANEL_TITLES nahoře
// se plní z popisu panelů v init().

function findComponent(type) {
    let found = null;
    const walk = (item) => {
        if (!item || found) return;
        if (item.isComponent && item.componentType === type) { found = item; return; }
        (item.contentItems || []).forEach(walk);
    };
    walk(state?.layout.rootItem);
    return found;
}

/** Je panel daného typu právě v layoutu? */
export function isPanelVisible(type) {
    return !!findComponent(type);
}

/** Skryje/zobrazí panel. Vrací nový stav viditelnosti. */
export function togglePanel(type) {
    if (!state) return false;
    const existing = findComponent(type);
    if (existing) {
        existing.close();
        return false;
    }
    state.layout.addComponent(type, undefined, PANEL_TITLES[type] ?? type);
    const added = findComponent(type);
    if (added?.parentItem?.isStack) {
        added.parentItem.setActiveComponentItem(added, true);
    }
    return true;
}

/** Vrátí aktuální layout jako JSON (pro perzistenci na vyžádání). */
export function getConfig() {
    return state ? JSON.stringify(state.layout.saveLayout()) : null;
}

export function dispose() {
    if (!state) return;
    clearTimeout(state.saveTimer);
    clearTimeout(state.editorResizeTimer);
    try { state.layout.destroy(); } catch (e) { /* už zničeno */ }
    state = null;
}
