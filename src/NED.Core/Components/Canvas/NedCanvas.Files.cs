using MudBlazor;
using NED.Abstractions;
using NED.Core.Assets;
using NED.Core.Persistence;

namespace NED.Core;

public partial class NedCanvas
{
    // ── File menu akce ──────────────────────────────

    [NedCommand("File/New", Icon = Icons.Material.Filled.NoteAdd)]
    private async Task OnNew()
    {
        var parameters = new DialogParameters<NewGraphDialog>
        {
            { x => x.OutputTypes, AvailableOutputTypes() },
            { x => x.Initial, NED.Abstractions.Manifest.TypeIds.Any },
        };
        var options = new DialogOptions
        {
            CloseOnEscapeKey = true,
            MaxWidth = MaxWidth.ExtraSmall,
            FullWidth = true,
        };

        var dialog = await DialogService.ShowAsync<NewGraphDialog>(Loc["Dialog_NewGraph"], parameters, options);
        var result = await dialog.Result;
        if (result is null || result.Canceled || result.Data is not NewGraphResult res) return;

        _loading = true;
        var settings = new GraphSettings
        {
            Flow = res.Flow,
            Name = res.Name ?? "",
            Instanceable = AssetIndex.NewGraphInstanceable,
        };
        // Datový graf bez deklarace nic nepočítá, tak mu jednu založíme rovnou; další
        // se přidávají v Details panelu.
        if (res.OutputType is { } outputType)
            settings.Outputs.Add(new GraphOutput { Name = "Result", Type = outputType });
        var tab = CreateTab(settings, seedOutput: true);
        _loading = false;
        tab.IsDirty = true;
        _tabs.Add(tab);
        Activate(tab);
        StateHasChanged();
    }

    [NedCommand("File/Save", Shortcut = "Ctrl+S", Icon = Icons.Material.Filled.Save, Order = 10)]
    private async Task OnSave()
    {
        if (_active is null) return;

        if (_active is EditorTab ed)
        {
            if (ed.FilePath is not null && OnWriteFile is not null)
            {
                var doc = GraphPersistence.ToDocument(ed.Diagram, ed.Settings, Catalog);
                var json = GraphPersistence.Serialize(doc);
                try { await OnWriteFile(ed.FilePath, json); }
                catch { Notifier.Error("Notice_SaveFailed", Path.GetFileName(ed.FilePath)); return; }
                ed.IsDirty = false;
                AssetIndex.UpdateFile(ed.FilePath);
                Notifier.Info("Notice_Saved", Path.GetFileName(ed.FilePath));
            }
            else
            {
                await OnSaveAs();
            }
        }
        else if (_active is InstanceTab it)
        {
            if (it.FilePath is not null && OnWriteFile is not null)
            {
                var doc = InstancePersistence.ToDocument(it.Data);
                var json = InstancePersistence.Serialize(doc);
                try { await OnWriteFile(it.FilePath, json); }
                catch { Notifier.Error("Notice_SaveFailed", Path.GetFileName(it.FilePath)); return; }
                it.IsDirty = false;
                AssetIndex.UpdateFile(it.FilePath);
                Notifier.Info("Notice_Saved", Path.GetFileName(it.FilePath));
            }
            else
            {
                await OnSaveAs();
            }
        }
    }

    [NedCommand("File/Save As", Shortcut = "Ctrl+Shift+S", Icon = Icons.Material.Filled.SaveAs, Order = 11)]
    private async Task OnSaveAs()
    {
        if (_active is null) return;

        string? path;

        try
        {
            if (_active is EditorTab ed)
            {
                if (OnSaveAsRequested is null) return;
                var doc = GraphPersistence.ToDocument(ed.Diagram, ed.Settings, Catalog);
                var json = GraphPersistence.Serialize(doc);
                path = await OnSaveAsRequested(json);
            }
            else if (_active is InstanceTab it)
            {
                var doc = InstancePersistence.ToDocument(it.Data);
                var json = InstancePersistence.Serialize(doc);
                var suggestedName = string.IsNullOrWhiteSpace(it.Data.Name) ? null : it.Data.Name;
                if (OnSaveAsInstanceRequested is not null)
                    path = await OnSaveAsInstanceRequested(json, suggestedName);
                else if (OnSaveAsRequested is not null)
                    path = await OnSaveAsRequested(json);
                else return;
            }
            else return;
        }
        catch
        {
            Notifier.Error("Notice_SaveFailed", _active.Title);
            return;
        }

        if (path is not null)
        {
            _active.FilePath = path;
            _active.IsDirty = false;
            AssetIndex.UpdateFile(path);
            Notifier.Info("Notice_Saved", Path.GetFileName(path));
            StateHasChanged();
        }
    }

    [NedCommand("File/Open", Icon = Icons.Material.Filled.FolderOpen, Order = 1)]
    private async Task OnLoad()
    {
        if (OnLoadRequested is null) return;
        var loaded = await OnLoadRequested();
        if (loaded is null) return;

        var (json, path) = loaded.Value;
        OpenFromJson(json, path);
    }

    [NedCommand("File/Export", Icon = Icons.Material.Filled.FileDownload, Order = 20)]
    private async Task OnExport()
    {
        if (_active is null || OnExportRequested is null) return;

        try
        {
            string output;
            IReadOnlyList<NedNotice> errors = Array.Empty<NedNotice>();
            if (_active is EditorTab ed)
            {
                output = GraphExporter.Export(ed.Diagram, ed.Settings, out errors, AssetIndex, Catalog);
            }
            else if (_active is InstanceTab it)
            {
                output = GraphExporter.ExportInstance(it.Data, AssetIndex, Catalog);
            }
            else return;

            await OnExportRequested(output);
            foreach (var e in errors) Notifier.Post(e);
        }
        catch
        {
            Notifier.Error("Notice_ExportFailed", _active.Title);
        }
    }

    [NedCommand("File/Manage Libraries", Icon = Icons.Material.Filled.LibraryBooks, Order = 30)]
    private async Task OnManageLibraries()
    {
        var parameters = new DialogParameters<ManageLibrariesDialog>
        {
            { x => x.OnBrowse, OnPickFolderRequested },
            { x => x.OnPickManifest, OnPickNodePackManifestRequested },
            { x => x.OnPickPackSource, OnPickNodePackSourceRequested },
        };
        var options = new DialogOptions
        {
            CloseOnEscapeKey = true,
            MaxWidth = MaxWidth.Small,
            FullWidth = true,
        };
        await DialogService.ShowAsync<ManageLibrariesDialog>(Loc["Dialog_Libraries"], parameters, options);
        StateHasChanged();
    }

    /// <summary>
    /// Pro jeden neuložený tab zobrazí dialog Save/Discard/Cancel.
    /// Vrací true = lze pokračovat (uloženo nebo zahozeno); false = uživatel
    /// zrušil (Cancel, nebo zrušil výběr souboru u „Save as").
    /// Čistý (neměněný) tab projde rovnou bez dialogu.
    /// </summary>
    private async Task<bool> ConfirmSaveTab(TabBase tab)
    {
        if (!tab.IsDirty) return true;

        // Ukaž uživateli, o kterém tabu se rozhoduje.
        _active = tab;
        StateHasChanged();

        var result = await DialogService.ShowMessageBoxAsync(
            Loc["UnsavedChanges_Title"],
            string.Format(Loc["UnsavedChanges_Message"], tab.Title),
            yesText: Loc["UnsavedChanges_Save"],
            noText: Loc["UnsavedChanges_Discard"],
            cancelText: Loc["Cancel"]);

        if (result is null) return false;     // Cancel
        if (result == false) return true;     // Discard → pokračuj

        // Save → ulož. Když uživatel zrušil „Save as", tab zůstane dirty —
        // ber to jako Cancel, ať o data nepřijde.
        await OnSave();
        return !tab.IsDirty;
    }

    /// <summary>
    /// Projde všechny neuložené taby a pro každý zobrazí dialog Save/Discard/Cancel.
    /// Vrací true = uživatel všechno vyřešil (uložil nebo zahodil); false = stiskl Cancel.
    /// </summary>
    private async Task<bool> ConfirmUnsavedTabs()
    {
        foreach (var tab in _tabs.Where(t => t.IsDirty).ToList())
            if (!await ConfirmSaveTab(tab))
                return false;
        return true;
    }

    // ── Library panel ────────────────────────────────

    private void OnOpenAsset(AssetEntry entry)
    {
        string json;
        try { json = File.ReadAllText(entry.Path); }
        catch { Notifier.Warn("Notice_FileReadFailed", entry.Name); return; }
        OpenFromJson(json, entry.Path);
    }

    /// <summary>Drill-in: double-click na SubgraphNode otevře referencovaný subgraf v novém tabu
    /// (rodičem je aktuální tab — pro breadcrumb). Stale ref (smazaný asset) = tichý no-op.</summary>
    private void OpenSubgraphFromNode(Guid subgraphId)
    {
        var asset = AssetIndex.Resolve(subgraphId);
        if (asset is null) return;
        string json;
        try { json = File.ReadAllText(asset.Path); }
        catch { Notifier.Warn("Notice_FileReadFailed", asset.Name); return; }
        OpenFromJson(json, asset.Path, openedFrom: _active);
    }

    /// <summary>Je <paramref name="ancestor"/> předkem <paramref name="node"/> v řetězci OpenedFrom?</summary>
    private static bool IsAncestor(TabBase ancestor, TabBase? node)
    {
        var seen = new HashSet<TabBase>();
        for (var t = node; t is not null && seen.Add(t); t = t.OpenedFrom)
            if (t == ancestor) return true;
        return false;
    }

    // ── Library context menu akce (soubory) ─────────

    private async Task RevealAsset(AssetEntry a)
    {
        if (OnRevealInExplorer is not null) await OnRevealInExplorer(a.Path);
    }

    private async Task OpenAssetInEditor(AssetEntry a)
    {
        if (OnOpenInEditor is not null) await OnOpenInEditor(a.Path);
    }

    private async Task RenameAsset(AssetEntry a)
    {
        var current = Path.GetFileNameWithoutExtension(a.Path).Replace(".nedgraph", "");
        var parameters = new DialogParameters<TextPromptDialog>
        {
            { x => x.Label, Loc["Dialog_Rename_Label"].Value },
            { x => x.OkText, Loc["Dialog_Rename"].Value },
            { x => x.Initial, current },
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.ExtraSmall, FullWidth = true };
        var dialog = await DialogService.ShowAsync<TextPromptDialog>(Loc["Dialog_Rename"], parameters, options);
        var result = await dialog.Result;
        if (result is null || result.Canceled || result.Data is not string newName) return;

        var (newPath, success) = AssetFileService.RenameAsset(a.Path, newName);
        if (!success)
        {
            await DialogService.ShowMessageBoxAsync(Loc["Dialog_Error"], Loc["Dialog_FileExists"]);
            return;
        }

        var tab = _tabs.FirstOrDefault(t =>
            string.Equals(t.FilePath, a.Path, StringComparison.OrdinalIgnoreCase));
        if (tab is not null && newPath is not null)
        {
            tab.FilePath = newPath;
            if (tab is EditorTab ed) ed.Settings.Name = newName;
        }

        if (newPath is not null) AssetIndex.MoveFile(a.Path, newPath);
        StateHasChanged();
    }

    private void DuplicateAsset(AssetEntry a)
    {
        var newPath = AssetFileService.DuplicateAsset(a.Path);
        if (newPath is not null)
        {
            AssetIndex.UpdateFile(newPath);
            StateHasChanged();
        }
        else
        {
            Notifier.Error("Notice_DuplicateFailed", a.Name);
        }
    }

    private async Task DeleteAsset(AssetEntry a)
    {
        var ok = await DialogService.ShowMessageBoxAsync(
            Loc["Dialog_DeleteFile"],
            string.Format(Loc["Dialog_MoveToTrash"], a.Name),
            yesText: Loc["Dialog_Delete"], cancelText: Loc["Cancel"]);
        if (ok != true) return;

        bool deleted;
        if (OnDeleteFile is not null)
            deleted = await OnDeleteFile(a.Path);
        else
        {
            try { File.Delete(a.Path); deleted = true; }
            catch { deleted = false; }
        }
        if (!deleted)
        {
            Notifier.Error("Notice_DeleteFailed", a.Name);
            return;
        }

        var deleteTab = _tabs.FirstOrDefault(t =>
            string.Equals(t.FilePath, a.Path, StringComparison.OrdinalIgnoreCase));
        if (deleteTab is not null) RemoveTab(deleteTab);

        AssetIndex.RemoveFile(a.Path);
        StateHasChanged();
    }

    private void OpenFromJson(string json, string path, TabBase? openedFrom = null)
    {
        var existing = _tabs.FirstOrDefault(t =>
            t.FilePath is not null && string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            // Drill-in do už otevřeného tabu: doplň rodiče jen pokud ho nemá a nevznikne cyklus.
            if (openedFrom is not null && existing.OpenedFrom is null
                && openedFrom != existing && !IsAncestor(existing, openedFrom))
                existing.OpenedFrom = openedFrom;
            Activate(existing);
            StateHasChanged();
            return;
        }

        if (path.EndsWith(".nedinst.json", StringComparison.OrdinalIgnoreCase))
        {
            var instDoc = InstancePersistence.Deserialize(json);
            if (instDoc is null) { Notifier.Error("Notice_FileReadFailed", Path.GetFileName(path)); return; }
            var data = InstancePersistence.FromDocument(instDoc);
            var template = AssetIndex.Resolve(data.TemplateId);
            var instTab = new InstanceTab
            {
                Data = data,
                FilePath = path,
                TemplateInterface = template?.Interface,
                OpenedFrom = openedFrom,
            };
            _tabs.Add(instTab);
            Activate(instTab);
            StateHasChanged();
            return;
        }

        var doc = GraphPersistence.Deserialize(json);
        if (doc is null) { Notifier.Error("Notice_FileReadFailed", Path.GetFileName(path)); return; }

        WarnAboutUnreadableParts(doc, Path.GetFileName(path));

        _loading = true;
        var tab = CreateTab();
        tab.Settings = GraphPersistence.LoadInto(tab.Diagram, doc, Catalog, AssetIndex);
        // Baseline undo = načtený stav → první uživatelská mutace se stane prvním undo krokem.
        // Bez toho by CreateTab() (Reset na prázdný diagram) plus LoadInto zanechal ne-nulový
        // rozdíl v baseline, a čekající deferred commit by po loadu založil falešný krok.
        tab.Undo.Reset(tab.Diagram, tab.Settings);
        _loading = false;
        tab.FilePath = path;
        tab.OpenedFrom = openedFrom;
        tab.Added = tab.Diagram.Nodes.Count;
        _tabs.Add(tab);
        Activate(tab);
        _pendingRefresh = true;
        StateHasChanged();
    }

    /// <summary>
    /// Řekne dopředu, co z otevíraného souboru editor nepřečte celé. Bez toho by se uzly
    /// chybějícího packu jen tiše proměnily v placeholdery a uživatel by neměl jak zjistit proč.
    /// Načtení pokračuje v obou případech — hlásíme, neblokujeme.
    /// </summary>
    private void WarnAboutUnreadableParts(GraphDocument doc, string fileName)
    {
        if (doc.SchemaVersion > GraphDocument.CurrentSchemaVersion)
            Notifier.Warn("Notice_NewerSchema", fileName, doc.SchemaVersion);

        var loaded = Catalog.Packs.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var missing in (doc.Settings.RequiredPacks ?? []).Where(p => !loaded.Contains(p.Id)))
            Notifier.Warn("Notice_MissingPack", missing.Id, fileName);
    }

    private IReadOnlyList<string> AvailableOutputTypes() => Catalog.SelectableTypes();

    /// <summary>Proklik z Problems panelu (E4 stale subgraph ref) — dialog pro vytvoření
    /// chybějícího subgraf souboru se stejným Id a rozhraním, jaké node na plátně už očekává.</summary>
    private async Task OpenCreateSubgraphDialog(SubgraphNodeModel node)
    {
        var parameters = new DialogParameters<CreateSubgraphDialog>
        {
            { x => x.SubgraphName, node.SubgraphName },
            { x => x.Roots, AssetIndex.Roots },
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.ExtraSmall, FullWidth = true };
        var dialog = await DialogService.ShowAsync<CreateSubgraphDialog>(Loc["Dialog_CreateSubgraph"], parameters, options);
        var result = await dialog.Result;
        if (result is null || result.Canceled || result.Data is not (string dir, string name)) return;

        var path = AssetFileService.CreateSubgraphStub(dir, name, node.SubgraphId, node.Interface);
        if (path is null)
        {
            Notifier.Error("Notice_SubgraphCreateFailed", name);
            return;
        }

        AssetIndex.UpdateFile(path);
        Notifier.Info("Notice_SubgraphCreated", name);
        OpenSubgraphFromNode(node.SubgraphId);
    }

    /// <summary>Proklik z Problems panelu (chybějící template instance) — dialog pro vytvoření
    /// template souboru se stejným Id a poli odvozenými z <c>InstanceData.Values</c> (typ string,
    /// uživatel doladí v Details panelu).</summary>
    private async Task OpenCreateTemplateDialog(InstanceTab tab)
    {
        var parameters = new DialogParameters<CreateSubgraphDialog>
        {
            { x => x.SubgraphName, tab.Data.TemplateName },
            { x => x.Roots, AssetIndex.Roots },
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.ExtraSmall, FullWidth = true };
        var dialog = await DialogService.ShowAsync<CreateSubgraphDialog>(Loc["Dialog_CreateTemplate"], parameters, options);
        var result = await dialog.Result;
        if (result is null || result.Canceled || result.Data is not (string dir, string name)) return;

        // Poškozený/prázdný TemplateId (Guid.Empty) nejde použít jako identitu šablony
        // (TryRead by soubor s Empty Id odmítl). Vyrob novou identitu, propoj instanci a ulož,
        // ať link přežije reload. U platné-ale-smazané reference (běžný případ) se Id zachová.
        var templateId = tab.Data.TemplateId;
        if (templateId == Guid.Empty)
        {
            templateId = Guid.NewGuid();
            tab.Data.TemplateId = templateId;
            if (tab.FilePath is not null && OnWriteFile is not null)
            {
                var idoc = InstancePersistence.ToDocument(tab.Data);
                try { await OnWriteFile(tab.FilePath, InstancePersistence.Serialize(idoc)); AssetIndex.UpdateFile(tab.FilePath); }
                catch { Notifier.Error("Notice_SaveFailed", tab.Title); return; }
            }
        }

        var path = AssetFileService.CreateTemplateStub(dir, name, templateId, tab.Data.Values);
        if (path is null)
        {
            Notifier.Error("Notice_TemplateCreateFailed", name);
            return;
        }

        AssetIndex.UpdateFile(path);
        Notifier.Info("Notice_TemplateCreated", name);
    }

    // ── Instance akce ────────────────────────────────

    private void OnNewInstance(AssetEntry templateEntry)
    {
        var data = new InstanceData
        {
            TemplateId = templateEntry.Id,
            TemplateName = templateEntry.Name,
        };
        foreach (var inp in templateEntry.Interface.Inputs)
            data.Values[inp.Name] = inp.Default ?? "";

        var tab = new InstanceTab
        {
            Data = data,
            TemplateInterface = templateEntry.Interface,
        };
        _tabs.Add(tab);
        Activate(tab);
        StateHasChanged();
    }

    private void OnOpenInstance(InstanceEntry inst)
    {
        var existing = _tabs.OfType<InstanceTab>()
            .FirstOrDefault(t => t.FilePath is not null
                && string.Equals(t.FilePath, inst.Path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Activate(existing);
            StateHasChanged();
            return;
        }

        try
        {
            var json = File.ReadAllText(inst.Path);
            var doc = InstancePersistence.Deserialize(json);
            if (doc is null) { Notifier.Error("Notice_FileReadFailed", inst.Name); return; }
            var data = InstancePersistence.FromDocument(doc);
            var template = AssetIndex.Resolve(data.TemplateId);

            var tab = new InstanceTab
            {
                Data = data,
                FilePath = inst.Path,
                TemplateInterface = template?.Interface,
            };
            _tabs.Add(tab);
            Activate(tab);
            StateHasChanged();
        }
        catch
        {
            Notifier.Warn("Notice_FileReadFailed", inst.Name);
        }
    }

    private async Task OnDeleteInstance(InstanceEntry inst)
    {
        var ok = await DialogService.ShowMessageBoxAsync(
            Loc["Dialog_DeleteInstance"],
            string.Format(Loc["Dialog_MoveToTrash"], inst.Name),
            yesText: Loc["Dialog_Delete"], cancelText: Loc["Cancel"]);
        if (ok != true) return;

        bool deleted;
        if (OnDeleteFile is not null)
            deleted = await OnDeleteFile(inst.Path);
        else
        {
            try { File.Delete(inst.Path); deleted = true; }
            catch { deleted = false; }
        }
        if (!deleted)
        {
            Notifier.Error("Notice_DeleteFailed", inst.Name);
            return;
        }

        var tab = _tabs.OfType<InstanceTab>()
            .FirstOrDefault(t => string.Equals(t.FilePath, inst.Path, StringComparison.OrdinalIgnoreCase));
        if (tab is not null) RemoveTab(tab);

        AssetIndex.RemoveFile(inst.Path);
        StateHasChanged();
    }
}
