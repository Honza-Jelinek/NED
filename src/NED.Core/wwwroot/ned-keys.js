// Globální klávesové zkratky editoru. Registruje document-level keydown listener,
// který je nezávislý na fokusu a potlačí nativní akce WebView (Ctrl+S = uložit stránku).
let handler = null;

export function register(dotNetRef) {
    unregister();
    handler = (e) => {
        const key = e.key.toLowerCase();
        if (e.ctrlKey && key === 's') {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('HandleShortcut', e.shiftKey ? 'saveas' : 'save');
        }
        if (e.ctrlKey && key === 'z') {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('HandleShortcut', e.shiftKey ? 'redo' : 'undo');
        }
        if (e.ctrlKey && key === 'y') {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('HandleShortcut', 'redo');
        }
        // Alt+← / Alt+→ — navigace zpět/vpřed v historii tabů (jako prohlížeč/VS Code).
        if (e.altKey && key === 'arrowleft') {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('HandleShortcut', 'back');
        }
        if (e.altKey && key === 'arrowright') {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('HandleShortcut', 'forward');
        }
        if (e.ctrlKey && key === 'k') {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('HandleShortcut', 'palette');
        }
        if (key === 'home') {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('HandleShortcut', 'home');
        }
    };
    document.addEventListener('keydown', handler, true);

    // Boční tlačítka myši (back/forward) WebView2 pohltí nativně a do DOMu je nepošle.
    // Host (MainWindow, WM_APPCOMMAND) je zachytí a zavolá tuto funkci přes ExecuteScriptAsync.
    window.__nedNav = (dir) => dotNetRef.invokeMethodAsync('HandleShortcut', dir);
}

export function unregister() {
    if (handler) {
        document.removeEventListener('keydown', handler, true);
        handler = null;
    }
    window.__nedNav = undefined;
}

// Scrollne zvýrazněnou položku node-pickeru do viditelné oblasti.
export function scrollHighlight() {
    const el = document.querySelector('.np-popup .np-hl');
    if (el) el.scrollIntoView({ block: 'nearest' });
}

// Zafokusuje search input pickeru (spolehlivější než ElementReference ve WebView).
export function focusSearch() {
    const el = document.querySelector('.np-popup .np-search-input');
    if (el) el.focus();
}

// Command palette helpers.
export function focusPaletteSearch() {
    const el = document.querySelector('.ne-palette-search');
    if (el) el.focus();
}

export function scrollPaletteHighlight() {
    const el = document.querySelector('.ne-palette-hl');
    if (el) el.scrollIntoView({ block: 'nearest' });
}

export function clampPicker() {
    const popup = document.querySelector('.np-popup');
    if (!popup) return;
    const r = popup.getBoundingClientRect();
    const vw = window.innerWidth, vh = window.innerHeight;
    if (r.right > vw) popup.style.left = Math.max(0, vw - r.width) + 'px';
    if (r.bottom > vh) popup.style.top = Math.max(0, vh - r.height) + 'px';
}

// Počká, až se dotáhnou fonty a ustálí layout (2 animation frames).
// Po tomto je bezpečné nechat ZBD přeměřit pozice portů.
export function whenLayoutStable() {
    const fonts = document.fonts ? document.fonts.ready : Promise.resolve();
    const frames = new Promise(r =>
        requestAnimationFrame(() => requestAnimationFrame(r)));
    return Promise.all([fonts, frames]);
}

// Aktuální obdélník plátna ZBD (.diagram-canvas) v client souřadnicích.
// golden-layout polohuje plátno přes inline style (left/top) — to ZBD ResizeObserver
// (jen velikost) ani MutationObserver (jen childList) NEZACHYTÍ, takže jeho Container
// zůstane na původní (špatné) pozici. Touto funkcí ho z .NET přepíšeme na skutečný stav.
// Tvar {left,top,right,bottom} se deserializuje přímo do ZBD Rectangle (JsonConstructor).
export function canvasRect() {
    const el = document.querySelector('.diagram-canvas');
    if (!el) return null;
    const r = el.getBoundingClientRect();
    return { left: r.left, top: r.top, right: r.right, bottom: r.bottom };
}
