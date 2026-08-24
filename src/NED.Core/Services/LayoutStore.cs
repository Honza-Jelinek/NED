namespace NED.Core;

/// <summary>
/// Most mezi NED Core a hostitelem pro perzistenci layoutu dokovatelných panelů.
/// Core neví, kam/jak se layout ukládá — hostitel (composition root) dodá callbacky
/// (zápis/čtení souboru), stejně jako u ostatních I/O operací. JSON je serializovaný
/// golden-layout config (viz ned-dock.js).
/// </summary>
public sealed class LayoutStore
{
    /// <summary>Host zapíše layout JSON (fixní cesta, bez dialogu). Nastavuje NedCanvas z parametru.</summary>
    public Func<string, Task>? SaveCallback { get; set; }

    /// <summary>Host přečte uložený layout JSON (nebo null, pokud nic). Nastavuje NedCanvas z parametru.</summary>
    public Func<Task<string?>>? LoadCallback { get; set; }

    public Task SaveAsync(string json)
        => SaveCallback is not null ? SaveCallback(json) : Task.CompletedTask;

    public Task<string?> LoadAsync()
        => LoadCallback is not null ? LoadCallback() : Task.FromResult<string?>(null);
}
