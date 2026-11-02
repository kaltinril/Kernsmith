using System.Globalization;
using System.Text.Json;

namespace KernSmith.HintParity.Legibility;

/// <summary>
/// Parses a <c>--weights</c> file into term weights for <see cref="LegibilityScore"/>. Two forms:
/// a JSON object of term name to weight (<c>{"topology": 0.2, ...}</c>; unlisted terms weigh
/// nothing), or the pairwise page's <c>weights.json</c> (recognised by its
/// <c>weights_per_sd</c> object), whose per-feature logistic weights are mapped onto the terms
/// through <see cref="FeatureTerms"/>: each feature adds its magnitude to its term when its
/// fitted sign agrees with the direction the term rewards, and nothing (noted) when it
/// contradicts it. Either way the result is normalised to sum to 1, so the score stays in 0..1.
/// </summary>
internal static class LegibilityWeights
{
    /// <summary>
    /// Which term each pairwise feature stands in for, and the sign of a fitted weight that agrees
    /// with it (+1: the term rewards more of the feature; −1: less). <c>gap_mean</c> has no fixed
    /// direction and no term; <c>separability</c> has no proxy on the page.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Term, int Sign)> FeatureTerms = new Dictionary<string, (string, int)>
    {
        ["ink_concentration"] = ("concentration", +1),
        ["mean_ink"] = ("fidelity", +1),
        ["sharpness"] = ("alignment", +1),
        ["broken_rings"] = ("brokenRings", -1),
        ["holes_128"] = ("topology", +1),
        ["topology_error"] = ("topologyError", -1),
        ["stem_width_var"] = ("stems", -1),
        ["gap_var"] = ("spacing", -1),
        ["touching_pairs"] = ("spacing", -1),
    };

    /// <summary>Reads and parses a weights file.</summary>
    public static (IReadOnlyDictionary<string, double> Weights, IReadOnlyList<string> Notes) Load(string path) =>
        Parse(File.ReadAllText(path));

    /// <summary>Parses either form; see the class remarks.</summary>
    /// <exception cref="ArgumentException">The JSON is not an object, names an unknown term or feature,
    /// has a negative or non-numeric weight, or leaves every term at zero.</exception>
    public static (IReadOnlyDictionary<string, double> Weights, IReadOnlyList<string> Notes) Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new ArgumentException($"Weights file is not valid JSON: {e.Message}", e);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Weights file must be a JSON object: either {term: weight} or a fit_bt.py weights.json.");

            return root.TryGetProperty("weights_per_sd", out var perSd)
                ? FromFit(perSd)
                : FromTerms(root);
        }
    }

    private static (IReadOnlyDictionary<string, double>, IReadOnlyList<string>) FromTerms(JsonElement root)
    {
        var weights = LegibilityScore.DefaultWeights.Keys.ToDictionary(k => k, _ => 0.0);
        var notes = new List<string>();
        foreach (var property in root.EnumerateObject())
        {
            if (!weights.ContainsKey(property.Name))
                throw new ArgumentException($"Unknown term '{property.Name}'; the terms are {string.Join(", ", weights.Keys)}.");
            if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetDouble(out double value) || !double.IsFinite(value))
                throw new ArgumentException($"Term '{property.Name}' needs a finite number, not {property.Value}.");
            if (value < 0)
                throw new ArgumentException($"Term '{property.Name}' has a negative weight ({value.ToString(CultureInfo.InvariantCulture)}); every term rewards legibility, so weights are non-negative.");
            weights[property.Name] = value;
        }

        foreach (var (term, value) in weights)
            if (value == 0)
                notes.Add($"{term}: not listed, weight 0");

        return (Normalise(weights), notes);
    }

    private static (IReadOnlyDictionary<string, double>, IReadOnlyList<string>) FromFit(JsonElement perSd)
    {
        if (perSd.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("weights_per_sd must be an object of feature name to weight.");

        var weights = LegibilityScore.DefaultWeights.Keys.ToDictionary(k => k, _ => 0.0);
        var notes = new List<string>();
        foreach (var property in perSd.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetDouble(out double value) || !double.IsFinite(value))
                throw new ArgumentException($"Feature '{property.Name}' needs a finite number, not {property.Value}.");

            if (!FeatureTerms.TryGetValue(property.Name, out var mapping))
            {
                if (Array.IndexOf(LegibilityFeatures.Names, property.Name) < 0)
                    throw new ArgumentException($"Unknown feature '{property.Name}'; the pairwise features are {string.Join(", ", LegibilityFeatures.Names)}.");
                notes.Add($"{property.Name}: no term stands in for it, ignored");
                continue;
            }

            double agreeing = value * mapping.Sign;
            if (agreeing > 0)
                weights[mapping.Term] += agreeing;
            else if (agreeing < 0)
                notes.Add($"{property.Name}: fitted {value.ToString("+0.###;-0.###", CultureInfo.InvariantCulture)} contradicts {mapping.Term}, which rewards {(mapping.Sign > 0 ? "more" : "less")} of it; dropped");
        }

        foreach (var (term, value) in weights)
            if (value == 0)
                notes.Add($"{term}: no feature maps to it, weight 0");

        return (Normalise(weights), notes);
    }

    private static IReadOnlyDictionary<string, double> Normalise(Dictionary<string, double> weights)
    {
        double sum = weights.Values.Sum();
        if (sum <= 0)
            throw new ArgumentException("The weights sum to zero: at least one term must carry weight (in a fit_bt.py file, at least one feature must agree with its term's direction).");
        return weights.ToDictionary(kv => kv.Key, kv => kv.Value / sum);
    }
}
