using System;

namespace ILM.Systems.UI
{
    // Binding payloads. GenericUIWriter serializes field names verbatim, so they are camelCase
    // and must match the interfaces in useToolState.ts and usePinnedStyles.ts.

    /// <summary>Pinned style ids for the UI dropdowns.</summary>
    public class PinnedStylesVM
    {
        public int[] lineStyles = Array.Empty<int>();
        public int[] areaStyles = Array.Empty<int>();
    }

    /// <summary>Everything the panel renders except the camera-dependent popover anchors
    /// (those travel as <see cref="SegmentPointVM"/>).</summary>
    public class PanelStateVM
    {
        public bool isActive;
        public int toolState;
        public int areaVertexCount;
        public int currentAreaStyle;
        public int selectedNodeIndex = -1;
        public int currentStyle;
        public bool vanillaHidden;
        public int lastClickedLine = -1;
        public int lastClickedTick;
        public int hoveredLineInGame = -1;
        public int hoveredAreaInGame = -1;
        public bool freeDraw;
        // MarkingNodeToolSystem.LineMode: 0 snap to points, 1 free draw, 2 edit.
        public int lineMode;
        // Area open in edit mode (handles and vertices shown), -1 for none.
        public int editArea = -1;
        // The selection is a road stretch (mid-road editing), not a junction.
        public bool selectedIsRoad;
        public int freePointCount;
        // MarkingNodeToolSystem.EditTarget, set by the panel tab: 0 lines, 1 areas, 2 points.
        public int editTarget;
        // MarkingNodeToolSystem.SnapFlags: 1 existing geometry, 2 90 degrees, 4 road side,
        // 8 guide lines, 16 length.
        public int snapMask;
        // Free point id under the cursor in the world, -1 for none.
        public int hoveredPointInGame = -1;
        public FreePointVM[] points = Array.Empty<FreePointVM>();
        public bool canUndo;
        public bool canRedo;
        public LineVM[] lines = Array.Empty<LineVM>();
        public AreaVM[] areas = Array.Empty<AreaVM>();
    }

    public class LineVM
    {
        public int lineIndex;
        public int style;
        public int curv;
        // Hand-shaped with drag handles; the curvature stepper then shows "custom".
        public bool handles;
        public SegmentVM[] segments = Array.Empty<SegmentVM>();
    }

    public class SegmentVM
    {
        public int lineIndex;
        public int segmentIndex; // dense per-line counter, stable within one topology pass
        public float tStart;
        public float tEnd;
        public bool visible;
        public int style;
        public float lengthM;
    }

    /// <summary>A user-placed free point, for the Points tab.</summary>
    public class FreePointVM
    {
        public int id;
        // Inside the junction rather than on one of its roads.
        public bool onJunction;
        public int lineCount;
        public int areaCount;
    }
    public class AreaVM
    {
        public int areaIndex;
        public int styleId;
        public bool visible;
        public int vertexCount;
        public int pieceCount;
        public int visiblePieces;
    }

    /// <summary>Screen anchor of one in-world popover (CSS px, origin top-left). Segment
    /// anchors carry (lineIndex, segmentIndex); area anchors carry areaIndex with the segment
    /// fields at -1. A popover without an anchor is hidden.</summary>
    public class SegmentPointVM
    {
        public int lineIndex;
        public int segmentIndex;
        public int areaIndex = -1;
        public float x;
        public float y;
        // Camera-distance popover scale in [0.65, 1], see PopoverScale.
        public float scale = 1f;
    }
}
