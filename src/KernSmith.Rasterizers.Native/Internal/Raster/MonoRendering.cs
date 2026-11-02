namespace KernSmith.Rasterizers.Native.Internal.Raster;

/// <summary>How <see cref="AntiAliasMode.None"/> output is made from an outline.</summary>
internal enum MonoRendering
{
    /// <summary>Area coverage thresholded at 50%, then <see cref="DropoutControl"/>: Native's
    /// original rule, kept byte-identical for <see cref="HintLevel.Default"/>, <see cref="HintLevel.None"/>
    /// and <see cref="HintLevel.Light"/>.</summary>
    Threshold,

    /// <summary>
    /// <see cref="MonoStrokeRasterizer"/>: thin strokes drawn one pixel wide at their centre
    /// line, everything else centre-sampled, and counters kept open. What <see cref="HintLevel.Normal"/>
    /// renders, after its whole-pixel fit.
    /// </summary>
    Strokes,
}
