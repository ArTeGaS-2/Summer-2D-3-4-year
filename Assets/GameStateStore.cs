using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class GameStateStore : MonoBehaviour
{
    [Serializable]
    public class StateEntry
    {
        public string key;
        public string value;
    }

    [Serializable]
    public class StatBinding
    {
        public string key;
        public string label;
        public TMP_Text text;
    }

    [Serializable]
    private class StateJson
    {
        public StateEntry[] state;
    }

    [Header("Default State")]
    [SerializeField] private List<StateEntry> initialState = new()
    {
        new StateEntry { key = "location", value = "Ліс" },
        new StateEntry { key = "time", value = "06:00" },
        new StateEntry { key = "hit_points", value = "30 / 30" },
        new StateEntry { key = "mana_points", value = "20 / 20" },
        new StateEntry { key = "stamina_points", value = "30 / 30" },
        new StateEntry { key = "strength", value = "0" },
        new StateEntry { key = "agility", value = "0" },
        new StateEntry { key = "intelligence", value = "0" }
    };

    [Header("UI")]
    [SerializeField] private TMP_Text locationText;
    [SerializeField] private TMP_Text timeText;
    [SerializeField] private List<StatBinding> statBindings = new();

    private readonly Dictionary<string, string> state = new();
    private bool initialized;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void Start()
    {
        AutoFindDefaultTexts();
        AutoBindPanelStats();
        RefreshUi();
    }

    public IReadOnlyDictionary<string, string> Snapshot
    {
        get
        {
            EnsureInitialized();
            return state;
        }
    }

    public string Get(string key, string fallback = "")
    {
        EnsureInitialized();
        return state.TryGetValue(NormalizeKey(key), out string value) ? value : fallback;
    }

    public void Set(string key, string value)
    {
        EnsureInitialized();
        state[NormalizeKey(key)] = value ?? "";
        RefreshUi();
    }

    public void ApplyChanges(IEnumerable<StateEntry> changes)
    {
        EnsureInitialized();

        if (changes == null)
        {
            return;
        }

        foreach (StateEntry change in changes)
        {
            if (change == null || string.IsNullOrWhiteSpace(change.key))
            {
                continue;
            }

            state[NormalizeKey(change.key)] = change.value ?? "";
        }

        RefreshUi();
    }

    public string ToJson()
    {
        EnsureInitialized();

        List<StateEntry> entries = new();
        foreach (KeyValuePair<string, string> item in state)
        {
            entries.Add(new StateEntry { key = item.Key, value = item.Value });
        }

        return JsonUtility.ToJson(new StateJson { state = entries.ToArray() });
    }

    public void LoadFromJson(string json)
    {
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        StateJson data = JsonUtility.FromJson<StateJson>(json);
        if (data?.state == null)
        {
            return;
        }

        state.Clear();
        ApplyChanges(data.state);
    }

    public void BindDefaultTexts(TMP_Text location, TMP_Text time)
    {
        if (locationText == null)
        {
            locationText = location;
        }

        if (timeText == null)
        {
            timeText = time;
        }

        RefreshUi();
    }

    private void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        state.Clear();

        foreach (StateEntry entry in initialState)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.key))
            {
                continue;
            }

            state[NormalizeKey(entry.key)] = entry.value ?? "";
        }

        AddDefaultStateIfMissing("location", "Ліс");
        AddDefaultStateIfMissing("time", "06:00");
        AddDefaultStateIfMissing("hit_points", "30 / 30");
        AddDefaultStateIfMissing("mana_points", "20 / 20");
        AddDefaultStateIfMissing("stamina_points", "30 / 30");
        AddDefaultStateIfMissing("strength", "0");
        AddDefaultStateIfMissing("agility", "0");
        AddDefaultStateIfMissing("intelligence", "0");
    }

    private void AutoFindDefaultTexts()
    {
        if (locationText == null)
        {
            locationText = FindTextByName("Location");
        }

        if (timeText == null)
        {
            timeText = FindTextByName("Time");
        }
    }

    private TMP_Text FindTextByName(string objectName)
    {
        GameObject found = GameObject.Find(objectName);
        return found == null ? null : found.GetComponent<TMP_Text>();
    }

    private void AutoBindPanelStats()
    {
        AddBindingIfMissing("hit_points", "Життя", "HitPoints");
        AddBindingIfMissing("mana_points", "Мана", "ManaPoints");
        AddBindingIfMissing("stamina_points", "Енергія", "StaminaPoints");
        AddBindingIfMissing("strength", "Сила", "Stat_Strenght");
        AddBindingIfMissing("agility", "Спритність", "Stat_Agility");
        AddBindingIfMissing("intelligence", "Інтелект", "Stat_Intelligence");
    }

    private void AddBindingIfMissing(string key, string label, string objectName)
    {
        string normalizedKey = NormalizeKey(key);

        foreach (StatBinding binding in statBindings)
        {
            if (binding != null && NormalizeKey(binding.key) == normalizedKey)
            {
                if (binding.text == null)
                {
                    binding.text = FindTextByName(objectName);
                }

                if (string.IsNullOrWhiteSpace(binding.label))
                {
                    binding.label = label;
                }

                return;
            }
        }

        TMP_Text text = FindTextByName(objectName);
        if (text == null)
        {
            return;
        }

        statBindings.Add(new StatBinding
        {
            key = normalizedKey,
            label = label,
            text = text
        });
    }

    private void RefreshUi()
    {
        if (!initialized)
        {
            return;
        }

        if (locationText != null)
        {
            locationText.text = FormatLine("Локація", Get("location", "Невідомо"));
        }

        if (timeText != null)
        {
            timeText.text = FormatLine("Час", Get("time", "??:??"));
        }

        foreach (StatBinding binding in statBindings)
        {
            if (binding == null || binding.text == null || string.IsNullOrWhiteSpace(binding.key))
            {
                continue;
            }

            string key = NormalizeKey(binding.key);
            string label = string.IsNullOrWhiteSpace(binding.label) ? binding.key : binding.label;
            binding.text.text = FormatLine(label, Get(key, ""));
        }
    }

    private string FormatLine(string label, string value)
    {
        return label + ": " + value;
    }

    private void AddDefaultStateIfMissing(string key, string value)
    {
        string normalizedKey = NormalizeKey(key);
        if (!state.ContainsKey(normalizedKey))
        {
            state[normalizedKey] = value;
        }
    }

    private string NormalizeKey(string key)
    {
        return string.IsNullOrWhiteSpace(key) ? "" : key.Trim().ToLowerInvariant();
    }
}
