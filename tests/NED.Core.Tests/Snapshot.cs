using System.Runtime.CompilerServices;

namespace NED.Core.Tests;

/// <summary>
/// Golden-master snapshoty. Schválený obsah leží v <c>Snapshots/{name}.json</c> vedle zdrojáků
/// (ne v build outputu — jinak by se schválení neuložilo do gitu).
///
/// Chybí-li soubor, test ho zapíše a selže s výzvou ke kontrole. Neshoda zapíše
/// <c>{name}.received.json</c> vedle schváleného, ať jde rozdíl porovnat diffem.
/// </summary>
public static class Snapshot
{
    public static void Match(string actual, string name, [CallerFilePath] string callerPath = "")
    {
        var dir = Path.Combine(Path.GetDirectoryName(callerPath)!, "Snapshots");
        Directory.CreateDirectory(dir);
        var approved = Path.Combine(dir, name + ".json");

        // Normalizuj konce řádků — git na Windows jinak dělá falešné neshody.
        actual = actual.Replace("\r\n", "\n").TrimEnd('\n');

        if (!File.Exists(approved))
        {
            File.WriteAllText(approved, actual);
            Assert.Fail($"Snapshot '{name}' neexistoval — zapsán do {approved}. Zkontroluj obsah a commitni.");
        }

        var expected = File.ReadAllText(approved).Replace("\r\n", "\n").TrimEnd('\n');
        if (expected == actual) return;

        var received = Path.Combine(dir, name + ".received.json");
        File.WriteAllText(received, actual);
        Assert.Fail($"Snapshot '{name}' nesedí. Porovnej:\n  {approved}\n  {received}");
    }
}
