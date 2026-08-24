using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using NED.Core;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;

namespace NED.Shell.Wpf;

public partial class MainWindow : Window
{
    // WebView2 pohltí boční tlačítka myši nativně — do DOMu (JS) se nikdy nedostanou.
    // Windows ale posílá pro tato tlačítka WM_APPCOMMAND až do top-level okna; tam je
    // zachytíme a přepošleme do Blazoru přes window.__nedNav (ned-keys.js).
    private const int WM_APPCOMMAND = 0x0319;
    private const int FAPPCOMMAND_MASK = 0xF000;
    private const int APPCOMMAND_BROWSER_BACKWARD = 1;
    private const int APPCOMMAND_BROWSER_FORWARD = 2;

    private CoreWebView2? _coreWebView2;

    public MainWindow()
    {
        InitializeComponent();

        // Set Services explicitly (instead of relying on DynamicResource timing)
        blazorWebView.Services = App.ServiceProvider;

        blazorWebView.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Routes)
        });

        blazorWebView.BlazorWebViewInitialized += OnBlazorWebViewInitialized;
    }

    /// <summary>
    /// Vypne vestavěnou navigaci WebView2 (swipe, browser akcelerátory) a uchová si
    /// odkaz na CoreWebView2, přes který se z WM_APPCOMMAND hooku volá zpět/vpřed.
    /// </summary>
    private void OnBlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
    {
        _coreWebView2 = e.WebView.CoreWebView2;

        var settings = _coreWebView2.Settings;
        settings.IsSwipeNavigationEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = true;

        // Pojistka: zamítni jakoukoli back/forward navigaci WebView2 (kdyby přeci jen vznikla).
        _coreWebView2.NavigationStarting += (_, args) =>
        {
            if (args.IsUserInitiated && args.NavigationKind == CoreWebView2NavigationKind.BackOrForward)
                args.Cancel = true;
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_APPCOMMAND) return IntPtr.Zero;

        // GET_APPCOMMAND_LPARAM(lParam) = (short)(HIWORD(lParam) & ~FAPPCOMMAND_MASK)
        int cmd = ((int)((long)lParam >> 16) & 0xFFFF) & ~FAPPCOMMAND_MASK;
        if (cmd == APPCOMMAND_BROWSER_BACKWARD)
        {
            TriggerNav("back");
            handled = true;
            return (IntPtr)1;
        }
        if (cmd == APPCOMMAND_BROWSER_FORWARD)
        {
            TriggerNav("forward");
            handled = true;
            return (IntPtr)1;
        }
        return IntPtr.Zero;
    }

    private void TriggerNav(string dir)
    {
        if (_coreWebView2 is null) return;
        // ExecuteScriptAsync běží na UI vlákně (= toto vlákno); fire-and-forget.
        _ = _coreWebView2.ExecuteScriptAsync($"window.__nedNav && window.__nedNav('{dir}')");
    }

    /// <summary>
    /// Před zavřením okna se přes <see cref="ShutdownGuard"/> doptá Blazor strany
    /// na neuložené změny. Dialog je async, zavírání okna je sync → zrušíme ho,
    /// počkáme na odpověď a teprve pak zavřeme doopravdy.
    /// </summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        var guard = App.ServiceProvider.GetService<ShutdownGuard>();
        if (guard?.ConfirmClose is null)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true; // zruš teď, doptej se asynchronně
        var canClose = await guard.ConfirmClose();
        if (canClose)
        {
            // Měření ukázalo, že ~3,5 s zavírání spotřebuje nativní teardown WPF okna
            // a WebView2 (msedgewebview2.exe) uvnitř Close() — managed cleanup ho
            // neovlivní. Neuložené změny jsou v tuhle chvíli už vyřešené, takže proces
            // ukončíme rovnou; child WebView2 proces se ukončí s rodičem.
            Environment.Exit(0);
        }
    }
}
