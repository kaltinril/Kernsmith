using Gum.Mvvm;
using KernSmith.Ui.Models;

namespace KernSmith.Ui.ViewModels;

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

    public void ToggleCodepoint(int codepoint)
    {
        if (!_selectedCodepoints.Remove(codepoint))
            _selectedCodepoints.Add(codepoint);
        UpdateSummary();
    }

    public bool IsSelected(int codepoint) => _selectedCodepoints.Contains(codepoint);

    public void SelectRange(int start, int end)
    {
        for (int cp = start; cp <= end; cp++)
            _selectedCodepoints.Add(cp);
        UpdateSummary();
    }

    public void DeselectRange(int start, int end)
    {
        for (int cp = start; cp <= end; cp++)
            _selectedCodepoints.Remove(cp);
        UpdateSummary();
    }

    public void SelectAll(IEnumerable<int> codepoints)
    {
        foreach (var cp in codepoints)
            _selectedCodepoints.Add(cp);
        UpdateSummary();
    }

    public void Clear()
    {
        _selectedCodepoints.Clear();
        UpdateSummary();
    }

    public void AddFromText(string text)
    {
        foreach (var ch in text)
            _selectedCodepoints.Add(ch);
        UpdateSummary();
    }

    public CharacterSet ToCharacterSet()
    {
        return CharacterSet.FromChars(_selectedCodepoints);
    }

    public HashSet<int> GetSelectedCodepoints() => new(_selectedCodepoints);

    private void UpdateSummary()
    {
        SelectedCount = _selectedCodepoints.Count;
        SummaryText = _selectedCodepoints.Count == 0
            ? "No characters selected"
            : $"{_selectedCodepoints.Count} characters selected";
    }
}
