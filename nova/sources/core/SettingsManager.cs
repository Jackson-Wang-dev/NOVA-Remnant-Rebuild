using System.Collections.Generic;
using Godot;
using Newtonsoft.Json;

namespace Nova;

/// <summary>
/// Owns the Master/Bgm/Bgs/Voice/Sfx audio bus volumes: creates the four non-Master buses (routed to
/// Master) on startup, and persists each bus's linear 0-1 volume to user://settings.json.
///
/// Mirrors Nova1's ConfigManager+AudioVolumeReader split (GlobalVolume/BGMVolume/SEVolume/VoiceVolume
/// in PlayerPrefs, applied to AudioSource.volume), but lets Godot's bus graph do the multiplication:
/// each AudioStreamPlayer's own VolumeDb (the "script volume" set by audio.gd's play/volume/sound) and
/// its bus's VolumeDb add in the dB domain, which is equivalent to Nova1's scriptVolume*configVolume
/// without doing that multiplication by hand. Settings volume is intentionally a separate persistence
/// store from SaveManager (user://settings.json, not user://save/), matching PlayerPrefs being
/// independent of the save system in Nova1 - this is an install/player-level setting, not part of any
/// save slot. No UIVolume-equivalent bus: nova2 has no UI sound effects yet (see porting-guide.md).
/// </summary>
public class SettingsManager : ISingleton
{
    private const string SettingsPath = "user://settings.json";

    // "Master" always exists; the other four are created in EnsureBuses if missing.
    public static readonly string[] Buses = ["Master", "Bgm", "Bgs", "Voice", "Sfx"];

    private class SettingsData
    {
        public Dictionary<string, float> BusVolumes { get; set; } = [];
    }

    private SettingsData _data;

    public void OnEnter()
    {
        EnsureBuses();
        _data = ReadJson<SettingsData>(SettingsPath) ?? new SettingsData();
        foreach (var bus in Buses)
        {
            ApplyVolume(bus, GetVolume(bus));
        }
    }

    public void OnReady() { }

    public void OnExit() { }

    private static void EnsureBuses()
    {
        foreach (var bus in Buses)
        {
            if (bus == "Master" || AudioServer.GetBusIndex(bus) != -1)
            {
                continue;
            }

            var idx = AudioServer.BusCount;
            AudioServer.AddBus(idx);
            AudioServer.SetBusName(idx, bus);
            AudioServer.SetBusSend(idx, "Master");
        }
    }

    // Linear 0-1, matching Nova1's PlayerPrefs convention. No Pow(value, gamma) perceptual curve yet
    // (porting-guide.md M7: start linear, revisit once a real volume slider exists to judge feel).
    public float GetVolume(string bus) => _data.BusVolumes.GetValueOrDefault(bus, 1.0f);

    public void SetVolume(string bus, float value)
    {
        value = Mathf.Clamp(value, 0.0f, 1.0f);
        _data.BusVolumes[bus] = value;
        ApplyVolume(bus, value);
        WriteJson(SettingsPath, _data);
    }

    private static void ApplyVolume(string bus, float value)
    {
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(bus), (float)Mathf.LinearToDb(value));
    }

    private static T ReadJson<T>(string path) where T : class
    {
        if (!FileAccess.FileExists(path))
        {
            return null;
        }

        return JsonConvert.DeserializeObject<T>(Utils.GetFileAsText(path));
    }

    private static void WriteJson<T>(string path, T data)
    {
        using var fs = Utils.OpenFile(path, FileAccess.ModeFlags.Write);
        fs.StoreString(JsonConvert.SerializeObject(data, Formatting.Indented));
    }

    public static SettingsManager Instance => NovaController.Instance.GetObj<SettingsManager>();
}
