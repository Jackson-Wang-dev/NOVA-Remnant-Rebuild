using System.Collections.Generic;
using Godot;

namespace Nova;

/// <summary>
/// One-shot sound effects via a small growable AudioStreamPlayer pool. Not PropertyState-bound:
/// fire-and-forget, matches Nova1's SoundController (no restore semantics).
/// </summary>
public partial class SoundController : Node
{
    [Export]
    private string _audioFolder = "sound";

    private readonly List<AudioStreamPlayer> _pool = [];

    public override void _EnterTree()
    {
        ObjectManager.Instance.BindObject("sound", this);
    }

    public void PlayClip(string trackName, double volume = 0.5)
    {
        if (GameState.Instance.IsRestoring)
        {
            return;
        }
        var player = GetFreePlayer();
        var path = $"{Assets.ResourceRoot}audio/{_audioFolder}/{trackName}.ogg";
        player.Stream = GD.Load<AudioStream>(path);
        player.VolumeDb = (float)Mathf.LinearToDb(volume);
        player.Play();
    }

    private AudioStreamPlayer GetFreePlayer()
    {
        foreach (var player in _pool)
        {
            if (!player.Playing)
            {
                return player;
            }
        }
        var newPlayer = new AudioStreamPlayer { Bus = "Sfx" };
        AddChild(newPlayer);
        _pool.Add(newPlayer);
        return newPlayer;
    }
}
