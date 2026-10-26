using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SliceNServe.Core
{
    /// <summary>Everything that outlives a run. JsonUtility-friendly: no dictionaries, no properties.</summary>
    [Serializable]
    public sealed class SaveData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public int cash;
        public int runsPlayed;
        public int bestServed;
        public List<UpgradeLevel> upgrades = new List<UpgradeLevel>();

        public int LevelOf(string upgradeId)
        {
            for (int i = 0; i < upgrades.Count; i++)
                if (upgrades[i].id == upgradeId) return upgrades[i].level;
            return 0;
        }

        public void SetLevel(string upgradeId, int level)
        {
            for (int i = 0; i < upgrades.Count; i++)
            {
                if (upgrades[i].id != upgradeId) continue;
                upgrades[i].level = level;
                return;
            }
            upgrades.Add(new UpgradeLevel { id = upgradeId, level = level });
        }
    }

    /// <summary>Keyed by <c>UpgradeDefinition.Id</c>, never the asset name, so renames are safe.</summary>
    [Serializable]
    public sealed class UpgradeLevel
    {
        public string id;
        public int level;
    }

    /// <summary>Where the save lives. A file on device; memory in tests, so a test run never
    /// touches the player's progress.</summary>
    public interface ISaveStore
    {
        string Read();
        void Write(string json);
        void SetAsideCorrupt();
    }

    public sealed class FileSaveStore : ISaveStore
    {
        private readonly string _path;

        public FileSaveStore(string path) => _path = path;

        public static FileSaveStore Default() => new FileSaveStore(Path.Combine(Application.persistentDataPath, "save.json"));

        public string Read() => File.Exists(_path) ? File.ReadAllText(_path) : null;

        /// <summary>Write-then-replace, so a crash mid-write leaves the previous save intact.</summary>
        public void Write(string json)
        {
            string temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            if (!File.Exists(_path)) { File.Move(temp, _path); return; }

            try { File.Replace(temp, _path, null); }
            catch (PlatformNotSupportedException)
            {
                // Some mobile filesystems lack an atomic replace; delete-then-move is the fallback.
                File.Delete(_path);
                File.Move(temp, _path);
            }
        }

        public void SetAsideCorrupt()
        {
            if (!File.Exists(_path)) return;
            string aside = _path + ".corrupt";
            if (File.Exists(aside)) File.Delete(aside);
            File.Move(_path, aside);
        }
    }

    public sealed class MemorySaveStore : ISaveStore
    {
        public string Json;
        public string Read() => Json;
        public void Write(string json) => Json = json;
        public void SetAsideCorrupt() => Json = null;
    }

    public sealed class SaveService
    {
        /// <summary>When set, the next bootstrap uses this store instead of the device file.
        /// Tests set it so the scene never reads or writes the player's real save.</summary>
        public static ISaveStore StoreOverride { get; set; }

        private readonly ISaveStore _store;

        public SaveData Data { get; private set; }

        public SaveService(ISaveStore store)
        {
            _store = store;
            Data = Load();
        }

        private SaveData Load()
        {
            string json;
            try { json = _store.Read(); }
            catch (Exception e)
            {
                Debug.LogWarning($"SaveService: could not read the save ({e.Message}); starting fresh.");
                return new SaveData();
            }

            if (string.IsNullOrEmpty(json)) return new SaveData();

            SaveData data;
            try { data = JsonUtility.FromJson<SaveData>(json); }
            catch (Exception) { data = null; }

            if (data == null || data.version < 1)
            {
                // Never crash or silently overwrite on a bad file: keep it aside for inspection.
                Debug.LogWarning("SaveService: the save was unreadable; it was set aside and a fresh one started.");
                _store.SetAsideCorrupt();
                return new SaveData();
            }

            if (data.upgrades == null) data.upgrades = new List<UpgradeLevel>();
            // Migrations go here, one version at a time, when CurrentVersion moves past 1.
            data.version = SaveData.CurrentVersion;
            return data;
        }

        public void Save()
        {
            try { _store.Write(JsonUtility.ToJson(Data, prettyPrint: true)); }
            catch (Exception e) { Debug.LogWarning($"SaveService: could not write the save ({e.Message})."); }
        }

        /// <summary>Erases all progress. For a settings "reset" button and for tests.</summary>
        public void Reset()
        {
            Data = new SaveData();
            Save();
        }
    }
}
