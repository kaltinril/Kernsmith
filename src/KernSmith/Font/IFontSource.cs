namespace KernSmith.Font;

/// <summary>
/// Obtains raw font bytes (TTF/OTF/WOFF) from a source such as a web font CDN.
/// </summary>
/// <remarks>
/// KernSmith accepts raw font bytes from any source. File loading
/// (<see cref="System.IO.File.ReadAllBytes(string)"/>) and system font enumeration
/// (<see cref="ISystemFontProvider"/>) are already trivial, so this abstraction exists
/// primarily for non-trivial sources — most notably web fonts, which require fetching
/// and parsing CSS <c>@font-face</c> responses. The interface is async because the
/// primary use case (network fetches) is inherently asynchronous; synchronous sources
/// can return <see cref="System.Threading.Tasks.Task.FromResult{TResult}(TResult)"/>.
/// </remarks>
public interface IFontSource
{
    /// <summary>
    /// Gets font bytes by family name.
    /// </summary>
    /// <param name="family">Font family name (e.g., "Roboto").</param>
    /// <param name="weight">Font weight (100–900, where 400 is regular and 700 is bold).</param>
    /// <param name="style">Font style (normal or italic).</param>
    /// <param name="subset">Unicode subset to request (e.g., "latin", "cyrillic", "greek").</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The raw font file bytes (TTF/OTF/WOFF).</returns>
    Task<byte[]> GetFontAsync(
        string family,
        int weight = 400,
        FontStyle style = FontStyle.Normal,
        string subset = "latin",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists available font families from this source.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The available font family names.</returns>
    Task<IReadOnlyList<string>> ListFamiliesAsync(
        CancellationToken cancellationToken = default);
}
