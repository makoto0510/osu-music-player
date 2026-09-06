using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OsuMusicPlayer.App.ViewModels;

public sealed partial class EqualizerBandViewModel : ObservableObject
{
    private readonly Action<EqualizerBandViewModel> changed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GainText))]
    private double gain;

    public EqualizerBandViewModel(int index, string label, float frequency, Action<EqualizerBandViewModel> changed)
    {
        Index = index;
        Label = label;
        Frequency = frequency;
        this.changed = changed ?? throw new ArgumentNullException(nameof(changed));
    }

    public int Index { get; }

    public string Label { get; }

    public float Frequency { get; }

    public string GainText => string.Create(CultureInfo.InvariantCulture, $"{Gain:+0.#;-0.#;0} dB");

    /// <summary>Set by the owner while applying a preset so the change is not reported back.</summary>
    internal bool Suppress { get; set; }

    partial void OnGainChanged(double value)
    {
        if (!Suppress)
        {
            changed(this);
        }
    }
}
