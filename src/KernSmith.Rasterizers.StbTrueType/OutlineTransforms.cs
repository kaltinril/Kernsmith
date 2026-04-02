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
    /// Emboldens a single contour. Faithful port of FreeType's FT_Outline_EmboldenXY:
    /// uses unnormalized bisector with halved strength, plus a uniform half-strength
    /// offset applied to all points. FreeType's clamping (min-segment / sin) prevents
    /// thin features like the A's counter apex from collapsing.
    /// </summary>
    private static unsafe void EmboldenContour(
        Stb.stbtt_vertex* vertices, int start, int end, float strength)
    {
        int count = end - start + 1;
        if (count < 2)
            return;

        // FreeType halves the strength: the bold effect comes from half-strength
        // lateral shift + half-strength uniform offset on every point.
        float halfStrength = strength * 0.5f;

        var shiftsX = new float[count];
        var shiftsY = new float[count];

        for (int i = 0; i < count; i++)
        {
            int curr = start + i;
            int prev = start + ((i - 1 + count) % count);
            int next = start + ((i + 1) % count);

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
            {
                // Degenerate point — still gets the uniform offset below.
                continue;
            }

            // Normalize edge directions
            if (inLen >= 1e-6f)
            {
                inDx /= inLen;
                inDy /= inLen;
            }
            else
            {
                inDx = outDx / outLen;
                inDy = outDy / outLen;
                inLen = outLen;
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
                outLen = inLen;
            }

            // dot(in, out) — cosine of angle between edges
            float dot = inDx * outDx + inDy * outDy;

            // d = 1 + dot, matching FreeType's (d + 0x10000)
            float d = 1.0f + dot;

            if (d < 0.0625f) // ~160° threshold, matches FreeType's -0xF000
            {
                // Very sharp angle — zero lateral shift to prevent spikes.
                // Point still receives the uniform offset.
                continue;
            }

            // Unnormalized bisector of the two edge normals (90° CCW rotation).
            // NOT normalizing is key: the natural magnitude sqrt(2(1+dot)) damps
            // the shift at sharp angles, preventing counter collapse.
            float bisX = -inDy + (-outDy); // inNx + outNx
            float bisY = inDx + outDx;      // inNy + outNy

            // Cross product magnitude |in × out| = |sin(angle)|
            float q = MathF.Abs(outDx * inDy - outDy * inDx);

            // Minimum adjacent segment length
            float l = MathF.Min(inLen, outLen);

            // FreeType's clamping: use normal scaling when halfStrength * q <= l * d,
            // otherwise clamp to l/q to prevent thin segments from collapsing.
            if (q > 1e-6f && halfStrength * q > l * d)
            {
                // Clamped: shift = bisector * l / q
                float clampScale = l / q;
                shiftsX[i] = bisX * clampScale;
                shiftsY[i] = bisY * clampScale;
            }
            else
            {
                // Normal: shift = bisector * halfStrength / d
                float scale = halfStrength / d;
                shiftsX[i] = bisX * scale;
                shiftsY[i] = bisY * scale;
            }
        }

        // Apply lateral shifts + uniform half-strength offset to all vertices.
        // The uniform offset matches FreeType's `points[i] += xstrength + shift`.
        for (int i = 0; i < count; i++)
        {
            int idx = start + i;
            float sx = shiftsX[i] + halfStrength;
            float sy = shiftsY[i] + halfStrength;

            vertices[idx].x = (short)(vertices[idx].x + (int)MathF.Round(sx));
            vertices[idx].y = (short)(vertices[idx].y + (int)MathF.Round(sy));

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

}
