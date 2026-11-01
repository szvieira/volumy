namespace Volumy.Core.Audio;

public enum DeviceFlow
{
    Playback,
    Capture,
}

/// <summary>Matches the Windows ERole values.</summary>
public enum DeviceRole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

public sealed record AudioDevice(string Id, string Name, DeviceFlow Flow);

/// <summary>One app that currently has an audio session. All sessions of the same exe are grouped.</summary>
public sealed record AppAudio(string Exe, string DisplayName, string? ExePath, float Volume, bool Muted);
