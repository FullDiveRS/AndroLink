namespace AndroLink.UI;

/// <summary>
/// Grid layout with Auto / Pixel / Star columns and rows, cell placement and
/// spans. Attached cell info is stored per child inside the grid instance.
/// </summary>
public class GridPanel : UIElement
{
    private sealed class CellInfo
    {
        public int Column;
        public int Row;
        public int ColSpan = 1;
        public int RowSpan = 1;
    }

    private readonly Dictionary<UIElement, CellInfo> _cells = new();
    private readonly List<LayoutLength> _columns = new();
    private readonly List<LayoutLength> _rows = new();

    public IReadOnlyList<LayoutLength> Columns => _columns;
    public IReadOnlyList<LayoutLength> Rows => _rows;

    public GridPanel(params LayoutLength[] columns) => _columns.AddRange(columns);

    public GridPanel AddColumn(LayoutLength length)
    {
        _columns.Add(length);
        return this;
    }

    public GridPanel AddRow(LayoutLength length)
    {
        _rows.Add(length);
        return this;
    }

    public GridPanel SetCell(UIElement element, int column, int row, int colSpan = 1, int rowSpan = 1)
    {
        _cells[element] = new CellInfo
        {
            Column = Math.Max(0, column),
            Row = Math.Max(0, row),
            ColSpan = Math.Max(1, colSpan),
            RowSpan = Math.Max(1, rowSpan),
        };
        return this;
    }

    private CellInfo CellOf(UIElement element) => _cells.GetValueOrDefault(element, new CellInfo());

    private List<UIElement> VisibleChildren() => VisualChildren.Where(c => c.Visible).ToList();

    protected override Size MeasureOverride(Size available)
    {
        var children = VisibleChildren();
        if (children.Count == 0) return Size.Empty;

        int nCols = Math.Max(1, _columns.Count);
        int nRows = Math.Max(1, _rows.Count);

        var colWidths = ResolveTracks(_columns, nCols, available.Width, children, horizontal: true, otherTracks: null);
        var rowHeights = ResolveTracks(_rows, nRows, available.Height, children, horizontal: false, otherTracks: colWidths);

        return new Size(colWidths.Sum(), rowHeights.Sum());
    }

    protected override void ArrangeOverride(Rect contentRect)
    {
        var children = VisibleChildren();
        if (children.Count == 0) return;

        int nCols = Math.Max(1, _columns.Count);
        int nRows = Math.Max(1, _rows.Count);

        var colWidths = ResolveTracks(_columns, nCols, contentRect.Width, children, horizontal: true, otherTracks: null);
        var rowHeights = ResolveTracks(_rows, nRows, contentRect.Height, children, horizontal: false, otherTracks: colWidths);

        var colX = new float[nCols];
        for (int i = 1; i < nCols; i++) colX[i] = colX[i - 1] + colWidths[i - 1];
        var rowY = new float[nRows];
        for (int i = 1; i < nRows; i++) rowY[i] = rowY[i - 1] + rowHeights[i - 1];

        foreach (var child in children)
        {
            var cell = CellOf(child);
            int c = Math.Min(cell.Column, nCols - 1);
            int r = Math.Min(cell.Row, nRows - 1);
            int cs = Math.Min(cell.ColSpan, nCols - c);
            int rs = Math.Min(cell.RowSpan, nRows - r);

            float cellW = 0, cellH = 0;
            for (int i = c; i < c + cs; i++) cellW += colWidths[i];
            for (int i = r; i < r + rs; i++) cellH += rowHeights[i];

            var slot = new Rect(contentRect.X + colX[c], contentRect.Y + rowY[r], cellW, cellH);
            child.MeasureInternal(new Size(cellW, cellH));
            child.ArrangeInternal(UIElement.AlignChild(slot, child.DesiredSize, child.HAlign, child.VAlign));
        }
    }

    /// <summary>
    /// Resolves one axis: pixels first, autos from measured children, stars
    /// share the remaining space. Children in auto tracks are measured with
    /// unbounded main axis and the resolved cross-track size (when known).
    /// </summary>
    private float[] ResolveTracks(
        List<LayoutLength> defs, int trackCount, float availableMain,
        List<UIElement> children, bool horizontal, float[]? otherTracks)
    {
        var resolved = new float[trackCount];

        float pixels = 0;
        for (int i = 0; i < trackCount; i++)
        {
            var def = i < defs.Count ? defs[i] : LayoutLength.Star();
            if (def.Type == LayoutLength.UnitType.Pixel)
            {
                resolved[i] = def.Value;
                pixels += def.Value;
            }
        }

        for (int i = 0; i < trackCount; i++)
        {
            var def = i < defs.Count ? defs[i] : LayoutLength.Star();
            if (def.Type != LayoutLength.UnitType.Auto) continue;

            float max = 0;
            foreach (var child in children)
            {
                var cell = CellOf(child);
                if (cell.Column >= trackCount || cell.Row >= trackCount) { }
                var trackIndex = horizontal ? cell.Column : cell.Row;
                if (trackIndex != i) continue;

                Size constraint;
                if (horizontal)
                {
                    constraint = new Size(float.PositiveInfinity, float.PositiveInfinity);
                }
                else
                {
                    float cellW = CrossSpan(cell.Column, cell.ColSpan, otherTracks, trackCount);
                    constraint = new Size(cellW, float.PositiveInfinity);
                }
                child.MeasureInternal(constraint);
                max = Math.Max(max, horizontal ? child.DesiredSize.Width : child.DesiredSize.Height);
            }
            resolved[i] = Math.Max(resolved[i], max);
            pixels += resolved[i];
        }

        float starSum = 0;
        for (int i = 0; i < trackCount; i++)
        {
            var def = i < defs.Count ? defs[i] : LayoutLength.Star();
            if (def.Type == LayoutLength.UnitType.Star) starSum += def.Value;
        }

        float remaining = Math.Max(0, availableMain - pixels);
        for (int i = 0; i < trackCount; i++)
        {
            var def = i < defs.Count ? defs[i] : LayoutLength.Star();
            if (def.Type == LayoutLength.UnitType.Star)
            {
                resolved[i] = starSum > 0 ? remaining * (def.Value / starSum) : 0;
            }
        }
        return resolved;
    }

    private static float CrossSpan(int start, int span, float[]? tracks, int trackCount)
    {
        if (tracks is null || tracks.Length == 0) return float.PositiveInfinity;
        float total = 0;
        int c = Math.Min(start, tracks.Length - 1);
        int s = Math.Min(span, tracks.Length - c);
        for (int i = c; i < c + s; i++) total += tracks[i];
        return total;
    }
}
