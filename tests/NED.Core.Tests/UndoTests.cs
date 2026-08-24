namespace NED.Core.Tests;

public class UndoTests
{
    /// <summary>Commit bez skutečné změny nesmí zapsat krok (jinak by undo „klikalo naprázdno").</summary>
    [Fact]
    public void CommitIfChanged_NoChange_RecordsNothing()
    {
        var catalog = TestGraph.Catalog();
        var (diagram, settings) = TestGraph.AddTwoNumbers(catalog);
        var undo = new UndoManager();
        undo.Reset(diagram, settings);

        Assert.False(undo.CommitIfChanged(diagram, settings));
        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void Undo_RestoresPreviousState()
    {
        var catalog = TestGraph.Catalog();
        var (diagram, settings) = TestGraph.AddTwoNumbers(catalog);
        var undo = new UndoManager();
        undo.Reset(diagram, settings);

        var before = diagram.Nodes.Count;
        diagram.Add(catalog, TestGraph.Number, 0, 400, values: ("Value", 99d));
        Assert.True(undo.CommitIfChanged(diagram, settings));

        undo.Undo(diagram, settings, catalog, null);

        Assert.Equal(before, diagram.Nodes.Count);
        Assert.False(undo.CanUndo);
        Assert.True(undo.CanRedo);
    }

    /// <summary>
    /// Historie je pevný kruhový buffer (100 kroků). Po přetečení musí jít odundovat
    /// přesně 100× a pak skončit — bez výjimky a bez zacyklení na přepsaných slotech.
    /// </summary>
    [Fact]
    public void RingBuffer_OverflowsWithoutLosingConsistency()
    {
        var catalog = TestGraph.Catalog();
        var (diagram, settings) = TestGraph.AddTwoNumbers(catalog);
        var undo = new UndoManager();
        undo.Reset(diagram, settings);

        for (var i = 0; i < 105; i++)
        {
            diagram.Add(catalog, TestGraph.Number, i, i, values: ("Value", (double)i));
            Assert.True(undo.CommitIfChanged(diagram, settings));
        }

        var steps = 0;
        while (undo.CanUndo)
        {
            undo.Undo(diagram, settings, catalog, null);
            steps++;
            Assert.True(steps <= 100, "kruhový buffer vydal víc kroků, než má kapacitu");
        }

        Assert.Equal(100, steps);
    }
}
