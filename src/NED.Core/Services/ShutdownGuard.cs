namespace NED.Core;

/// <summary>
/// Most mezi hostem (WPF okno) a Blazor stranou pro „opravdu zavřít?" dialog.
/// NedCanvas sem při startu zaregistruje callback, který se zeptá na neuložené
/// změny; host (MainWindow.OnClosing) ho zavolá při pokusu o zavření okna.
///
/// Funguje díky tomu, že WPF i WebView sdílí stejný DI kontejner
/// (<c>blazorWebView.Services = App.ServiceProvider</c>) → tenhle singleton
/// je pro obě strany jedna instance.
/// </summary>
public sealed class ShutdownGuard
{
    /// <summary>
    /// Doptá se na neuložené změny. Vrací <c>true</c> = lze zavřít,
    /// <c>false</c> = uživatel zrušil. <c>null</c> (nepřiřazeno) = nikdo se
    /// neregistroval → host zavře bez ptaní.
    /// </summary>
    public Func<Task<bool>>? ConfirmClose { get; set; }
}
