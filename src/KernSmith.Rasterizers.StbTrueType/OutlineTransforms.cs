using System.Runtime.CompilerServices;
using Stb = StbTrueTypeSharp.StbTrueType;

namespace KernSmith.Rasterizers.StbTrueType;

/// <summary>
/// Outline-level synthetic bold and italic transforms applied to stb_truetype vertex arrays.
/// </summary>
internal static class OutlineTransforms
{
    /// <summary>tan(12 degrees) -- matches FreeType's oblique angle.</summary>
    private const float ItalicShear = 0.2126f;

    /// <summary>Cosine threshold for sharp angle clamping (~160 degrees).</summary>
    private const float SharpAngleCosine = -0.9397f;

    /// <summary>
    /// Applies an italic shear transform to all vertices in the array.
    /// x' = x + y * tan(12 degrees)
    /// </summary>
    internal static unsafe void ApplyItalicShear(Stb.stbtt_vertex* vertices, int numVerts)
    {
        for (int i = 0; i < numVerts; i++)
        {
            ref var v = ref vertices[i];
            v.x = (short)(v.x + (int)(v.y * ItalicShear));
            v.cx = (short)(v.cx + (int)(v.cy * ItalicShear));
            v.cx1 = (short)(v.cx1 + (int)(v.cy1 * ItalicShear));
        }
    }

    /// <summary>
    /// Applies synthetic bold by shifting outline contour points outward/inward
    /// based on winding direction. Port of FreeType's FT_Outline_EmboldenXY algorithm.
    /// </summary>
    /// <param name="vertices">Pointer to the vertex array.</param>
    /// <param name="numVerts">Number of vertices.</param>
    /// <param name="strength">Emboldening strength in font units.</param>
    internal static unsafe void ApplyEmbolden(Stb.stbtt_vertex* vertices, int numVerts, float strength)
    {
        if (numVerts <= 0 || strength <= 0)
            return;

        // Parse contours: each vmove starts a new contour.
        // We process contour by contour.
        int contourStart = -1;

        for (int i = 0; i <= numVerts; i++)
        {
            bool isEnd = i == numVerts;
            bool isNewContour = !isEnd && vertices[i].type == 1; // vmove

            if ((isNewContour || isEnd) && contourStart >= 0)
            {
                int contourEnd = i - 1;
                EmboldenContour(vertices, contourStart, contourEnd, strength);
            }

            if (isNewContour)
                contourStart = i;
        }
    }

    /// <summary>
    /// Computes a bounding box from transformed vertices, scaled to pixel space.
    /// Returns (x0, y0, x1, y1) where y0 is top (most negative after flip).
    /// </summary>
    internal static unsafe (int x0, int y0, int x1, int y1) ComputeBoundingBox(
        Stb.stbtt_vertex* vertices, int numVerts, float scaleX, float scaleY)
    {
        if (numVerts == 0)
            return (0, 0, 0, 0);

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        for (int i = 0; i < numVerts; i++)
        {
            ref var v = ref vertices[i];
            byte vtype = v.type;

            // Include on-curve point
            UpdateMinMax(v.x * scaleX, v.y * -scaleY, ref minX, ref minY, ref maxX, ref maxY);

            // Include control points for curves
            if (vtype == 3) // vcurve
            {
                UpdateMinMax(v.cx * scaleX, v.cy * -scaleY, ref minX, ref minY, ref maxX, ref maxY);
            }
            else if (vtype == 4) // vcubic
            {
                UpdateMinMax(v.cx * scaleX, v.cy * -scaleY, ref minX, ref minY, ref maxX, ref maxY);
                UpdateMinMax(v.cx1 * scaleX, v.cy1 * -scaleY, ref minX, ref minY, ref maxX, ref maxY);
            }
        }

        if (minX > maxX)
            return (0, 0, 0, 0);

        return (
            (int)MathF.Floor(minX),
            (int)MathF.Floor(minY),
            (int)MathF.Ceiling(maxX),
            (int)MathF.Ceiling(maxY));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UpdateMinMax(float x, float y,
        ref float minX, ref float minY, ref float maxX, ref float maxY)
    {
        if (x < minX) minX = x;
        if (x > maxX) maxX = x;
        if (y < minY) minY = y;
        if (y > maxY) maxY = y;
    }

    /// <summary>
    /// Emboldens a single contour by computing bisector normals and shifting points.
    /// </summary>
    private static unsafe void EmboldenContour(
        Stb.stbtt_vertex* vertices, int start, int end, float strength)
    {
        int count = end - start + 1;
        if (count < 2)
            return;

        // Compute signed area to determine winding direction.
        // In TrueType's y-up coordinate system, outer contours are clockwise,
        // which gives a NEGATIVE signed area from the shoelace formula.
        // Negative area = clockwise (outer contour) = expand outward.
        // Positive area = counter-clockwise (hole) = shrink inward.
        float signedArea = ComputeSignedArea(vertices, start, end);
        float direction = signedArea < 0 ? 1.0f : -1.0f;

        // Collect on-curve point indices for the contour.
        // We need to compute shifts for each vertex based on its neighbors.
        // We'll store computed shifts, then apply them all at once to avoid
        // modifying vertices while iterating.
        var shiftsX = new float[count];
        var shiftsY = new float[count];

        for (int i = 0; i < count; i++)
        {
            int curr = start + i;
            int prev = start + ((i - 1 + count) % count);
            int next = start + ((i + 1) % count);

            // Get point positions (on-curve points).
            float px = vertices[prev].x;
            float py = vertices[prev].y;
            float cx = vertices[curr].x;
            float cy = vertices[curr].y;
            float nx = vertices[next].x;
            float ny = vertices[next].y;

            // Incoming edge direction: prev -> curr
            float inDx = cx - px;
            float inDy = cy - py;
            float inLen = MathF.Sqrt(inDx * inDx + inDy * inDy);

            // Outgoing edge direction: curr -> next
            float outDx = nx - cx;
            float outDy = ny - cy;
            float outLen = MathF.Sqrt(outDx * outDx + outDy * outDy);

            if (inLen < 1e-6f && outLen < 1e-6f)
                continue;

            // Normalize
            if (inLen >= 1e-6f)
            {
                inDx /= inLen;
                inDy /= inLen;
            }
            else
            {
                inDx = outDx / outLen;
                inDy = outDy / outLen;
            }

            if (outLen >= 1e-6f)
            {
                outDx /= outLen;
                outDy /= outLen;
            }
            else
            {
                outDx = inDx;
                outDy = inDy;
            }

            // Unit normals perpendicular to each edge (rotated 90 degrees CCW).
            float inNx = -inDy;
            float inNy = inDx;
            float outNx = -outDy;
            float outNy = outDx;

            // Bisector: average of the two normals
            float bisX = inNx + outNx;
            float bisY = inNy + outNy;
            float bisLen = MathF.Sqrt(bisX * bisX + bisY * bisY);

            if (bisLen < 1e-6f)
            {
                // Normals cancel out (180-degree turn). Use incoming normal.
                bisX = inNx;
                bisY = inNy;
                bisLen = 1.0f;
            }
            else
            {
                bisX /= bisLen;
                bisY /= bisLen;
            }

            // Dot product of the two normals determines the miter scaling.
            float dot = inNx * outNx + inNy * outNy;

            // Scale factor: strength / (1 + dot)
            // When normals are parallel (dot=1), scale = strength/2 (correct).
            // When perpendicular (dot=0), scale = strength.
            // Clamp at sharp angles to prevent spikes.
            float scale;
            if (dot < SharpAngleCosine)
            {
                // Very sharp angle -- clamp to prevent spike artifacts.
                scale = strength;
            }
            else
            {
                scale = strength / (1.0f + dot);
            }

            // Also clamp to prevent thin segment collapse.
            // If the shift would exceed half the minimum adjacent segment length,
            // limit it to prevent self-intersection.
            float minSegLen = MathF.Min(inLen, outLen);
            if (minSegLen > 1e-6f && scale > minSegLen * 0.5f)
            {
                scale = minSegLen * 0.5f;
            }

            shiftsX[i] = bisX * scale * direction;
            shiftsY[i] = bisY * scale * direction;
        }

        // Apply shifts to all vertex fields.
        for (int i = 0; i < count; i++)
        {
            int idx = start + i;
            float sx = shiftsX[i];
            float sy = shiftsY[i];

            vertices[idx].x = (short)(vertices[idx].x + (int)MathF.Round(sx));
            vertices[idx].y = (short)(vertices[idx].y + (int)MathF.Round(sy));

            // Apply the same shift to control points. For curves, the control points
            // should move with the on-curve point to maintain the curve shape.
            byte vtype = vertices[idx].type;
            if (vtype == 3 || vtype == 4) // vcurve or vcubic
            {
                vertices[idx].cx = (short)(vertices[idx].cx + (int)MathF.Round(sx));
                vertices[idx].cy = (short)(vertices[idx].cy + (int)MathF.Round(sy));
            }
            if (vtype == 4) // vcubic
            {
                vertices[idx].cx1 = (short)(vertices[idx].cx1 + (int)MathF.Round(sx));
                vertices[idx].cy1 = (short)(vertices[idx].cy1 + (int)MathF.Round(sy));
            }
        }
    }

    /// <summary>
    /// Computes the signed area of a contour using the shoelace formula.
    /// Positive = clockwise (outer contour in TrueType), negative = counter-clockwise (hole).
    /// </summary>
    private static unsafe float ComputeSignedArea(Stb.stbtt_vertex* vertices, int start, int end)
    {
        float area = 0;
        int count = end - start + 1;

        for (int i = 0; i < count; i++)
        {
            int curr = start + i;
            int next = start + ((i + 1) % count);

            float x0 = vertices[curr].x;
            float y0 = vertices[curr].y;
            float x1 = vertices[next].x;
            float y1 = vertices[next].y;

            area += (x0 * y1) - (x1 * y0);
        }

        return area * 0.5f;
    }
}
