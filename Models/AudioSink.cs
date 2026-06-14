namespace Kuromi.Models;

/// <summary>A PipeWire/PulseAudio output sink.</summary>
public class AudioSink
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    public override string ToString() => Description;
}
