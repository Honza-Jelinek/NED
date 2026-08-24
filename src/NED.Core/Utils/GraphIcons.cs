namespace NED.Core;

/// <summary>
/// Ikona assetu podle toku. Na jednom místě, protože ji potřebuje záložka i knihovní panel.
/// </summary>
public static class GraphIcons
{
    public static string For(GraphFlow flow) => flow == GraphFlow.Exec ? "▶" : "◆";
}
