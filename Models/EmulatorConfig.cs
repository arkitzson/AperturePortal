namespace ApertureOS.Models;

/// <summary>One configured emulator: where it lives, where its ROMs live, and which console it
/// represents. The console is set once here rather than detected per-ROM, since it's a property of
/// the emulator itself, not something that varies game to game.</summary>
public class EmulatorConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string EmulatorPath { get; set; } = string.Empty;
    public string RomFolder { get; set; } = string.Empty;
    public string Console { get; set; } = string.Empty;
}
