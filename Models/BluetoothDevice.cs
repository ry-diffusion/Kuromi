namespace Kuromi.Models;

public class BluetoothDeviceInfo
{
    public string Mac { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Paired { get; set; }
    public bool Connected { get; set; }
    public bool Trusted { get; set; }
    public string Icon { get; set; } = ""; // bluez "Icon" hint: audio-headset, input-mouse, ...
}
