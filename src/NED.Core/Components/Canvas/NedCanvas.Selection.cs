using Microsoft.AspNetCore.Components.Web;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

namespace NED.Core;

public partial class NedCanvas
{
    private void OnCanvasMouseDown(MouseEventArgs e)
    {
        if (ActiveEditor is null || !e.ShiftKey || e.Button != 0) return;
        if (NodeAt(e.ClientX, e.ClientY) is not null) return;

        _boxActive = true;
        _boxMoved = false;
        _boxStartX = _boxCurX = e.ClientX;
        _boxStartY = _boxCurY = e.ClientY;
    }

    private void OnCanvasMouseMove(MouseEventArgs e)
    {
        if (!_boxActive) return;
        _boxCurX = e.ClientX;
        _boxCurY = e.ClientY;
        if (Math.Abs(_boxCurX - _boxStartX) > 3 || Math.Abs(_boxCurY - _boxStartY) > 3)
            _boxMoved = true;
        StateHasChanged();
    }

    private void OnCanvasMouseUp(MouseEventArgs e)
    {
        if (!_boxActive) return;
        _boxActive = false;
        if (_boxMoved) SelectInBox();
        StateHasChanged();
    }

    private void SelectInBox()
    {
        if (ActiveEditor is not { } ed) return;
        var a = ed.Diagram.GetRelativeMousePoint(_boxStartX, _boxStartY);
        var b = ed.Diagram.GetRelativeMousePoint(_boxCurX, _boxCurY);
        double minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X);
        double minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);

        ed.Diagram.UnselectAll();
        foreach (var n in ed.Diagram.Nodes)
        {
            if (n.Size is null) continue;
            var hit = n.Position.X <= maxX && n.Position.X + n.Size.Width >= minX
                   && n.Position.Y <= maxY && n.Position.Y + n.Size.Height >= minY;
            if (hit) ed.Diagram.SelectModel(n, unselectOthers: false);
        }
    }
}
