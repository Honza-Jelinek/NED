# Dokumentace NEDa

**NED** = Node Editor — doméně slepý editor uzlových grafů (Blazor + WPF). Z manifestů
generovaných z anotovaných projektů sestaví paletu, nody a porty; graf uloží a vyexportuje do
verzované runtime smlouvy. Engine doménovou assembly při načítání nezná ani nespouští.

```text
anotovaný projekt ──ned-manifest──▶ *.nodes.json ──▶ NED.Core / NED.Shell.Wpf
       │                                                       │
 NED.Abstractions                                      .nedgraph.json
 (autorský kontrakt)                                          │
                                                    verzovaný runtime export
```

## Kudy začít

- **Chci přidat nový typ nodu** → [01-abstractions.md](01-abstractions.md) a [14-manifest.md](14-manifest.md).
- **Chci pochopit architekturu / vrstvy** → [06-project-structure.md](06-project-structure.md).
- **Zajímá mě, jak engine funguje uvnitř** → [03-ned-core-engine.md](03-ned-core-engine.md).
- **Používám editor a chci znát ovládání** → [11-editor-ux.md](11-editor-ux.md).
- **Píšu runtime konzumenta exportu** → [05-export.md](05-export.md) a jeho [JSON Schema](ned-export-v1.schema.json).
- **Stavím řídicí workflow** → [15-exec-graphs.md](15-exec-graphs.md).

## Mapa dokumentů

### Kontrakt a typy

- [01-abstractions.md](01-abstractions.md) — `NED.Abstractions`, atributy a marker `IGraphData`.
- [02-typed-ports.md](02-typed-ports.md) — kompatibilita, barvy a tvary typovaných portů.
- [14-manifest.md](14-manifest.md) — node pack manifesty, generování z projektu, workspace a hot reload.

### Engine a data

- [03-ned-core-engine.md](03-ned-core-engine.md) — katalog, modely, rendering a extension pointy.
- [04-persistence.md](04-persistence.md) — schéma 4, deklarace návratů a bezztrátový round-trip.
- [05-export.md](05-export.md) — datový/exec runtime export, pole, funkce, inlining a cyklus-guard.

### Projekt a distribuce

- [06-project-structure.md](06-project-structure.md) — vrstvy, závislosti a composition root.
- [07-distribution.md](07-distribution.md) — distribuce a úrovně customizace.

### Pokročilé funkce

- [10-subgraphs.md](10-subgraphs.md) — vkládání grafů, veřejné rozhraní, instance, inlining a volání.
- [11-editor-ux.md](11-editor-ux.md) — plátno, taby, undo/redo, dialogy a zkratky.
- [12-ports-and-fields.md](12-ports-and-fields.md) — přepínání vstupů port ↔ pole.
- [13-libraries-and-assets.md](13-libraries-and-assets.md) — knihovny, GUID identita a asset index.
- [15-exec-graphs.md](15-exec-graphs.md) — řídicí grafy, Return, exec funkce, plochý export a validace.

## Stav

Repo obsahuje `NED.Abstractions`, `NED.Core`, samostatný `NED.Shell.Wpf`, generátor
`ned-manifest` a ukázkový pack `Sandbox`. Editor načítá node typy z jazykově
neutrálních manifestů; `.NET` projekt lze vybrat a vygenerovat přímo z dialogu správy packů.
Manifesty i změny enable/disable se aplikují hot reloadem.

Runtime konzument exportu zatím v tomto repu není. Export verze 1 je proto popsaný schématem
a používá plná type id `"pack/node"`, aby jej šlo implementovat nezávisle na editoru.
