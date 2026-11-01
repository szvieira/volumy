using Volumy.Core.Audio;

namespace Volumy.Core.Profiles;

public static class DeviceResolver
{
    /// <summary>Finds the current id for a saved device: exact id first, then friendly name.</summary>
    public static string? Resolve(DeviceRef? saved, IReadOnlyList<AudioDevice> available)
    {
        if (saved == null) return null;
        var byId = available.FirstOrDefault(d => d.Id == saved.Id);
        if (byId != null) return byId.Id;
        var byName = available.FirstOrDefault(d =>
            string.Equals(d.Name, saved.Name, StringComparison.OrdinalIgnoreCase));
        return byName?.Id;
    }

    public static string? Resolve(DeviceSetting saved, IReadOnlyList<AudioDevice> available) =>
        Resolve(new DeviceRef { Id = saved.Id, Name = saved.Name }, available);

    public static bool Matches(DeviceRef saved, AudioDevice device) =>
        saved.Id == device.Id ||
        string.Equals(saved.Name, device.Name, StringComparison.OrdinalIgnoreCase);

    public static DeviceRef? ToRef(string? id, IReadOnlyList<AudioDevice> available)
    {
        if (id == null) return null;
        var device = available.FirstOrDefault(d => d.Id == id);
        return device == null ? null : new DeviceRef { Id = device.Id, Name = device.Name };
    }
}
