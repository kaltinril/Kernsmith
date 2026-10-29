/// <summary>
/// Locates the committed test fonts in <c>tests/KernSmith.Tests/Fixtures</c> for every benchmark class.
/// </summary>
internal static class BenchmarkFixtures
{
    /// <summary>Reads a fixture font's bytes.</summary>
    /// <param name="name">File name inside <c>tests/KernSmith.Tests/Fixtures</c>, e.g. <c>Roboto-Regular.ttf</c>.</param>
    public static byte[] ReadFont(string name) => File.ReadAllBytes(FindFont(name));

    /// <summary>Returns the full path of a fixture font.</summary>
    /// <param name="name">File name inside <c>tests/KernSmith.Tests/Fixtures</c>, e.g. <c>Roboto-Regular.ttf</c>.</param>
    /// <exception cref="FileNotFoundException">No ancestor of the app or working directory holds the fixture.</exception>
    public static string FindFont(string name)
    {
        // BenchmarkDotNet runs each case in a child process several directories below the repo
        // (bin/Release/<tfm>/<job>/bin/Release/<tfm>), so walk up rather than use fixed relative paths.
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "tests", "KernSmith.Tests", "Fixtures", name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        throw new FileNotFoundException($"Test font not found: {name}");
    }
}
