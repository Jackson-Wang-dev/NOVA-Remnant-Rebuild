using System;
using System.Collections.Generic;

namespace Nova;

/// <summary>
/// Small per-profile metadata blob, persisted as its own JSON file (global_save.json).
/// Mirrors Nova1's GlobalSave, minus the checkpoint-file offsets (the checkpoint tree is now its own
/// file, see <see cref="SaveManager"/>) and minus nodeHashes (script-upgrade detection is out of scope
/// for now, see porting-guide.md decision log).
/// </summary>
public class GlobalSave
{
    public long Identifier { get; set; }
    public Dictionary<string, object> Data { get; set; } = [];

    public static GlobalSave Create()
    {
        return new GlobalSave { Identifier = DateTime.Now.ToBinary() };
    }
}
