using System.Collections.Generic;
using UnityEngine;

/// <summary>Slot order is shared by the lobby, saved preferences and in-game input.</summary>
public sealed class PlayerWeaponLoadout
{
    public const int SlotCount = 3;
    private const string PreferencePrefix = "PlayerWeaponLoadout.Slot";
    private readonly List<string> pistols = new List<string>();
    private readonly List<string> primaries = new List<string>();
    private readonly string[] selected = new string[SlotCount];

    public string this[int slot] => selected[slot];
    private List<string> Options(int slot) => slot == 0 ? pistols : primaries;

    public void AddWeapon(string id, PlayerWeaponCategory category)
    {
        if (string.IsNullOrEmpty(id) || category == PlayerWeaponCategory.Launcher) return;
        var options = category == PlayerWeaponCategory.Secondary ? pistols : primaries;
        if (!options.Contains(id)) options.Add(id);
    }

    public void Restore()
    {
        for (int slot = 0; slot < SlotCount; slot++)
        {
            string saved = PlayerPrefs.GetString(PreferencePrefix + slot);
            var options = Options(slot);
            selected[slot] = null;
            foreach (string id in options)
            {
                if (slot == 2 && id == selected[1]) continue;
                if (selected[slot] == null || id == saved) selected[slot] = id;
            }
        }
    }

    public bool TryChoose(int slot, string id)
    {
        if (slot < 0 || slot >= SlotCount || !Options(slot).Contains(id)) return false;
        if (slot > 0 && selected[3 - slot] == id) return false;
        selected[slot] = id;
        for (int i = 0; i < SlotCount; i++)
            if (selected[i] != null) PlayerPrefs.SetString(PreferencePrefix + i, selected[i]);
        PlayerPrefs.Save();
        return true;
    }

    public string NextOption(int slot, int direction)
    {
        var options = Options(slot);
        int current = options.IndexOf(selected[slot]);
        for (int step = 1; step < options.Count; step++)
        {
            int index = (current + direction * step + options.Count) % options.Count;
            string candidate = options[index];
            if (slot == 0 || candidate != selected[3 - slot]) return candidate;
        }
        return null;
    }
}
