using Gum.Mvvm;
using KernSmith.Ui.Models;

namespace KernSmith.Ui.ViewModels;

/// <summary>
/// Manages the set of selected Unicode codepoints for bitmap font generation.
/// Supports preset application, manual toggle, range select/deselect, and text import.
/// </summary>
public class CharacterGridViewModel : ViewModel
{
    private readonly HashSet<int> _selectedCodepoints = new();

    public int SelectedCount { get => Get<int>(); set => Set(value); }
    public bool HasFont { get => Get<bool>(); set => Set(value); }
    public string SummaryText { get => Get<string>(); set => Set(value); }

    // Preset management
    public CharacterSetPreset ActivePreset { get => Get<CharacterSetPreset>(); set => Set(value); }

    public CharacterGridViewModel()
    {
        SummaryText = "No characters selected";
        ActivePreset = CharacterSetPreset.Ascii;
        ApplyPreset(CharacterSetPreset.Ascii);
    }

    /// <summary>
    /// Replaces all selected codepoints with those defined by the given preset.
    /// </summary>
    public void ApplyPreset(CharacterSetPreset preset)
    {
        _selectedCodepoints.Clear();
        var charSet = preset switch
        {
            CharacterSetPreset.Ascii => CharacterSet.Ascii,
            CharacterSetPreset.ExtendedAscii => CharacterSet.ExtendedAscii,
            CharacterSetPreset.Latin => CharacterSet.Latin,
            _ => CharacterSet.Ascii
        };
        foreach (var cp in charSet.GetCodepoints())
            _selectedCodepoints.Add(cp);

        ActivePreset = preset;
        UpdateSummary();
    }

    /// <summary>
    /// Toggles a single codepoint in or out of the selection.
    /// </summary>
    public void ToggleCodepoint(int codepoint)
    {
        if (!_selectedCodepoints.Remove(codepoint))
            _selectedCodepoints.Add(codepoint);
        UpdateSummary();
    }

    public bool IsSelected(int codepoint) => _selectedCodepoints.Contains(codepoint);

    /// <summary>
    /// Returns true if every codepoint in the inclusive range [start, end] is selected.
    /// </summary>
    public bool IsRangeFullySelected(int start, int end)
    {
        for (int cp = start; cp <= end; cp++)
        {
            if (!_selectedCodepoints.Contains(cp))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Adds all codepoints in the inclusive range [start, end] to the selection.
    /// </summary>
    public void SelectRange(int start, int end)
    {
        for (int cp = start; cp <= end; cp++)
            _selectedCodepoints.Add(cp);
        UpdateSummary();
    }

    /// <summary>
    /// Removes all codepoints in the inclusive range [start, end] from the selection.
    /// </summary>
    public void DeselectRange(int start, int end)
    {
        for (int cp = start; cp <= end; cp++)
            _selectedCodepoints.Remove(cp);
        UpdateSummary();
    }

    /// <summary>
    /// Adds every codepoint in the enumerable to the selection.
    /// </summary>
    public void SelectAll(IEnumerable<int> codepoints)
    {
        foreach (var cp in codepoints)
            _selectedCodepoints.Add(cp);
        UpdateSummary();
    }

    /// <summary>
    /// Removes all codepoints from the selection.
    /// </summary>
    public void Clear()
    {
        _selectedCodepoints.Clear();
        UpdateSummary();
    }

    /// <summary>
    /// Adds every unique character in the string to the selection.
    /// </summary>
    public void AddFromText(string text)
    {
        foreach (var ch in text)
            _selectedCodepoints.Add(ch);
        UpdateSummary();
    }

    /// <summary>
    /// Converts the current selection into a <see cref="CharacterSet"/> for generation.
    /// </summary>
    public CharacterSet ToCharacterSet()
    {
        return CharacterSet.FromChars(_selectedCodepoints);
    }

    /// <summary>Returns a defensive copy of the selected codepoints set.</summary>
    public HashSet<int> GetSelectedCodepoints() => new(_selectedCodepoints);

    private void UpdateSummary()
    {
        SelectedCount = _selectedCodepoints.Count;
        SummaryText = _selectedCodepoints.Count == 0
            ? "No characters selected"
            : $"{_selectedCodepoints.Count} characters selected";
    }
}
