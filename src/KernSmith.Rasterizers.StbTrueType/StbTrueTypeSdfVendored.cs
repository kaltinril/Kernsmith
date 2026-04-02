// ─────────────────────────────────────────────────────────────────────────────
// Vendored SDF rendering from stb_truetype.h by Sean Barrett (Public Domain).
// Original: https://github.com/nothings/stb/blob/master/stb_truetype.h
//
// Modified to accept pre-transformed vertices instead of reading from font data.
// This enables SDF rendering of synthetically bolded/italicized glyphs.
//
// License: Public Domain / MIT (dual-licensed in the original).
// ─────────────────────────────────────────────────────────────────────────────

using System.Runtime.CompilerServices;
using Stb = StbTrueTypeSharp.StbTrueType;

namespace KernSmith.Rasterizers.StbTrueType;

/// <summary>
/// Vendored SDF rendering from stb_truetype (Public Domain).
/// Modified to accept pre-transformed vertices instead of reading from font data.
/// This enables SDF rendering of synthetically bolded/italicized glyphs.
/// </summary>
internal static unsafe class StbTrueTypeSdfVendored
{
    // Vertex type constants matching stb_truetype.
    private const byte VMove = 1;
    private const byte VLine = 2;
    private const byte VCurve = 3;
    private const byte VCubic = 4;

    /// <summary>
    /// Renders an SDF bitmap from pre-modified glyph vertices.
    /// Equivalent to stbtt_GetGlyphSDF but accepts vertices directly.
    /// </summary>
    /// <param name="vertices">Pointer to the vertex array (may be pre-transformed).</param>
    /// <param name="numVerts">Number of vertices.</param>
    /// <param name="scale">Scale factor (from stbtt_ScaleForMappingEmToPixels or similar).</param>
    /// <param name="padding">Number of pixels to pad around the glyph SDF.</param>
    /// <param name="onEdgeValue">Byte value at distance 0 (typically 128).</param>
    /// <param name="pixelDistScale">Scales distance to byte range (typically 64.0).</param>
    /// <param name="width">Output bitmap width.</param>
    /// <param name="height">Output bitmap height.</param>
    /// <param name="xoff">X offset of the bitmap relative to the glyph origin.</param>
    /// <param name="yoff">Y offset of the bitmap relative to the glyph origin.</param>
    /// <returns>SDF bitmap as a managed byte array, or null for empty glyphs.</returns>
    internal static byte[]? GetGlyphSdfFromVertices(
        Stb.stbtt_vertex* vertices,
        int numVerts,
        float scale,
        int padding,
        byte onEdgeValue,
        float pixelDistScale,
        out int width,
        out int height,
        out int xoff,
        out int yoff)
    {
        width = 0;
        height = 0;
        xoff = 0;
        yoff = 0;

        if (numVerts <= 0 || vertices == null)
            return null;

        // Compute bounding box from vertices in font units, then scale to pixels.
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        for (int i = 0; i < numVerts; i++)
        {
            float px = vertices[i].x * scale;
            float py = vertices[i].y * -scale; // y-flip: font coords are y-up, bitmap is y-down

            if (px < minX) minX = px;
            if (px > maxX) maxX = px;
            if (py < minY) minY = py;
            if (py > maxY) maxY = py;

            byte vtype = vertices[i].type;
            if (vtype == VCurve || vtype == VCubic)
            {
                float cx = vertices[i].cx * scale;
                float cy = vertices[i].cy * -scale;
                if (cx < minX) minX = cx;
                if (cx > maxX) maxX = cx;
                if (cy < minY) minY = cy;
                if (cy > maxY) maxY = cy;
            }

            if (vtype == VCubic)
            {
                float cx1 = vertices[i].cx1 * scale;
                float cy1 = vertices[i].cy1 * -scale;
                if (cx1 < minX) minX = cx1;
                if (cx1 > maxX) maxX = cx1;
                if (cy1 < minY) minY = cy1;
                if (cy1 > maxY) maxY = cy1;
            }
        }

        if (minX > maxX || minY > maxY)
            return null;

        int ix0 = (int)MathF.Floor(minX) - padding;
        int iy0 = (int)MathF.Floor(minY) - padding;
        int ix1 = (int)MathF.Ceiling(maxX) + padding;
        int iy1 = (int)MathF.Ceiling(maxY) + padding;

        width = ix1 - ix0;
        height = iy1 - iy0;

        if (width <= 0 || height <= 0)
            return null;

        xoff = ix0;
        yoff = iy0;

        // Build the scaled/flipped edge list from vertices.
        // Pre-compute all edges as line segments and bezier curves in pixel space.
        var edges = BuildEdges(vertices, numVerts, scale);

        if (edges.Length == 0)
            return null;

        // Allocate and compute the SDF.
        var output = new byte[width * height];

        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
            {
                // Center of the pixel in bitmap space, offset to world space.
                float cx = ix0 + px + 0.5f;
                float cy = iy0 + py + 0.5f;

                float minDist = float.MaxValue;
                int crossings = 0;

                for (int e = 0; e < edges.Length; e++)
                {
                    ref readonly var edge = ref edges[e];

                    switch (edge.Type)
                    {
                        case EdgeType.Line:
                            LineDistance(cx, cy, edge.X0, edge.Y0, edge.X1, edge.Y1,
                                ref minDist, ref crossings);
                            break;

                        case EdgeType.Quad:
                            QuadDistance(cx, cy, edge.X0, edge.Y0,
                                edge.Cx, edge.Cy, edge.X1, edge.Y1,
                                ref minDist, ref crossings);
                            break;

                        case EdgeType.Cubic:
                            CubicDistance(cx, cy, edge.X0, edge.Y0,
                                edge.Cx, edge.Cy, edge.Cx1, edge.Cy1,
                                edge.X1, edge.Y1,
                                ref minDist, ref crossings);
                            break;
                    }
                }

                float dist = MathF.Sqrt(minDist);

                // Sign: inside if odd crossing count (non-zero winding rule).
                if ((crossings & 1) != 0)
                    dist = -dist;

                // Map signed distance to byte value.
                float val = onEdgeValue + dist * pixelDistScale;
                output[py * width + px] = (byte)Math.Clamp((int)(val + 0.5f), 0, 255);
            }
        }

        return output;
    }

    // ── Edge types ──────────────────────────────────────────────────────

    private enum EdgeType : byte
    {
        Line,
        Quad,
        Cubic
    }

    private struct Edge
    {
        public EdgeType Type;
        public float X0, Y0;   // Start point
        public float X1, Y1;   // End point
        public float Cx, Cy;   // Control point 1 (quad/cubic)
        public float Cx1, Cy1; // Control point 2 (cubic only)
    }

    // ── Edge building ───────────────────────────────────────────────────

    private static Edge[] BuildEdges(Stb.stbtt_vertex* vertices, int numVerts, float scale)
    {
        // Count edges first (lines and curves, not moves).
        int edgeCount = 0;
        for (int i = 0; i < numVerts; i++)
        {
            byte vtype = vertices[i].type;
            if (vtype == VLine || vtype == VCurve || vtype == VCubic)
                edgeCount++;
        }

        if (edgeCount == 0)
            return [];

        var edges = new Edge[edgeCount];
        int idx = 0;
        float startX = 0, startY = 0; // Contour start (for implicit close)
        float prevX = 0, prevY = 0;

        for (int i = 0; i < numVerts; i++)
        {
            ref readonly var v = ref vertices[i];
            byte vtype = v.type;

            float vx = v.x * scale;
            float vy = v.y * -scale; // y-flip

            if (vtype == VMove)
            {
                startX = vx;
                startY = vy;
                prevX = vx;
                prevY = vy;
            }
            else if (vtype == VLine)
            {
                edges[idx++] = new Edge
                {
                    Type = EdgeType.Line,
                    X0 = prevX,
                    Y0 = prevY,
                    X1 = vx,
                    Y1 = vy
                };
                prevX = vx;
                prevY = vy;
            }
            else if (vtype == VCurve)
            {
                float cx = v.cx * scale;
                float cy = v.cy * -scale;

                edges[idx++] = new Edge
                {
                    Type = EdgeType.Quad,
                    X0 = prevX,
                    Y0 = prevY,
                    Cx = cx,
                    Cy = cy,
                    X1 = vx,
                    Y1 = vy
                };
                prevX = vx;
                prevY = vy;
            }
            else if (vtype == VCubic)
            {
                float cx = v.cx * scale;
                float cy = v.cy * -scale;
                float cx1 = v.cx1 * scale;
                float cy1 = v.cy1 * -scale;

                edges[idx++] = new Edge
                {
                    Type = EdgeType.Cubic,
                    X0 = prevX,
                    Y0 = prevY,
                    Cx = cx,
                    Cy = cy,
                    Cx1 = cx1,
                    Cy1 = cy1,
                    X1 = vx,
                    Y1 = vy
                };
                prevX = vx;
                prevY = vy;
            }
        }

        return edges;
    }

    // ── Distance computations ───────────────────────────────────────────

    /// <summary>
    /// Computes the squared distance from point (px, py) to the line segment (ax, ay)-(bx, by)
    /// and updates the crossing counter for winding determination.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void LineDistance(
        float px, float py,
        float ax, float ay, float bx, float by,
        ref float minDist, ref int crossings)
    {
        float dx = bx - ax;
        float dy = by - ay;

        // Parametric closest point on line segment.
        float len2 = dx * dx + dy * dy;
        float t;
        if (len2 > 0)
        {
            t = ((px - ax) * dx + (py - ay) * dy) / len2;
            t = Math.Clamp(t, 0f, 1f);
        }
        else
        {
            t = 0;
        }

        float closestX = ax + t * dx;
        float closestY = ay + t * dy;
        float distX = px - closestX;
        float distY = py - closestY;
        float dist2 = distX * distX + distY * distY;

        if (dist2 < minDist)
            minDist = dist2;

        // Ray crossing test: horizontal ray from (px, py) going right.
        // Check if the segment crosses the horizontal line y = py.
        if ((ay <= py && by > py) || (by <= py && ay > py))
        {
            // Compute x-coordinate of intersection.
            float intersectT = (py - ay) / (by - ay);
            float intersectX = ax + intersectT * (bx - ax);
            if (intersectX > px)
                crossings++;
        }
    }

    /// <summary>
    /// Computes the squared distance from point (px, py) to a quadratic bezier curve
    /// defined by (x0, y0), control (cx, cy), end (x1, y1).
    /// Uses subdivision for accurate distance computation.
    /// </summary>
    private static void QuadDistance(
        float px, float py,
        float x0, float y0,
        float cx, float cy,
        float x1, float y1,
        ref float minDist, ref int crossings)
    {
        // Subdivide the quadratic bezier and compute distance to each sub-segment.
        // More subdivisions = more accurate but slower. 16 steps is a good balance
        // matching stb_truetype's approach.
        const int steps = 16;

        float prevX = x0;
        float prevY = y0;

        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            float it = 1 - t;

            // Quadratic bezier evaluation: B(t) = (1-t)^2*P0 + 2(1-t)t*C + t^2*P1
            float bx = it * it * x0 + 2 * it * t * cx + t * t * x1;
            float by = it * it * y0 + 2 * it * t * cy + t * t * y1;

            LineDistance(px, py, prevX, prevY, bx, by, ref minDist, ref crossings);

            prevX = bx;
            prevY = by;
        }
    }

    /// <summary>
    /// Computes the squared distance from point (px, py) to a cubic bezier curve
    /// defined by (x0, y0), control1 (cx, cy), control2 (cx1, cy1), end (x1, y1).
    /// Uses subdivision for accurate distance computation.
    /// </summary>
    private static void CubicDistance(
        float px, float py,
        float x0, float y0,
        float cx, float cy,
        float cx1, float cy1,
        float x1, float y1,
        ref float minDist, ref int crossings)
    {
        // Subdivide the cubic bezier. Use more steps than quadratic for accuracy.
        const int steps = 24;

        float prevX = x0;
        float prevY = y0;

        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            float it = 1 - t;

            // Cubic bezier evaluation: B(t) = (1-t)^3*P0 + 3(1-t)^2*t*C1 + 3(1-t)*t^2*C2 + t^3*P1
            float bx = it * it * it * x0
                      + 3 * it * it * t * cx
                      + 3 * it * t * t * cx1
                      + t * t * t * x1;
            float by = it * it * it * y0
                      + 3 * it * it * t * cy
                      + 3 * it * t * t * cy1
                      + t * t * t * y1;

            LineDistance(px, py, prevX, prevY, bx, by, ref minDist, ref crossings);

            prevX = bx;
            prevY = by;
        }
    }
}
