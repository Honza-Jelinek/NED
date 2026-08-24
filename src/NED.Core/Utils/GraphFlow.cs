namespace NED.Core;

/// <summary>
/// Čím po grafu teče řízení — jediná osa, kterou graf má.
///
/// Role (Graph / Subgraph / Template) tu bývala jako druhá osa, ale nesla jen pravidla
/// kolem Output uzlu. Když deklarace návratů přešly na graf, nezbylo co gatovat: vkládat
/// jde každý graf, instancovat ten, který to má zapnuté, a kořenovost není vlastnost
/// souboru, ale toho, jak ho zrovna používáš.
/// </summary>
public enum GraphFlow
{
    /// <summary>Data flow. Export je strom tažený pozpátku od Output uzlu.</summary>
    Data,

    /// <summary>Exec flow. Jeden ExecEntry, explicitní exec hrany, export je tabulka uzlů.</summary>
    Exec,
}
