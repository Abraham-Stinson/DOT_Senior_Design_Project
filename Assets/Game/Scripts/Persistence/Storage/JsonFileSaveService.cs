using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Game.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace Game.Persistence
{
    public sealed class JsonFileSaveService : ISaveService
    {
        private readonly string _filePath;
        private readonly JsonSerializerSettings _jsonSettings;

        public JsonFileSaveService(string filePath = null)
        {
            _filePath = string.IsNullOrWhiteSpace(filePath)
                ? Path.Combine(Application.persistentDataPath, "player_profile.json")
                : filePath;

            _jsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            };
        }

        public string FilePath => _filePath;

        public PlayerProfileData Load()
        {
            if (!File.Exists(_filePath))
            {
                return new PlayerProfileData();
            }

            string json = File.ReadAllText(_filePath);
            return DeserializeProfile(json);
        }

        public void Save(PlayerProfileData data)
        {
            (string json, string tempPath) = PrepareWrite(data);
            File.WriteAllText(tempPath, json);
            CommitTempFile(tempPath);
        }

        public async Task<PlayerProfileData> LoadAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (!File.Exists(_filePath))
            {
                return new PlayerProfileData();
            }

            string json = await File.ReadAllTextAsync(_filePath, ct).ConfigureAwait(false);
            return DeserializeProfile(json);
        }

        public async Task SaveAsync(PlayerProfileData data, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            (string json, string tempPath) = PrepareWrite(data);
            await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);
            CommitTempFile(tempPath);
        }

        private PlayerProfileData DeserializeProfile(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new PlayerProfileData();
            }

            PlayerProfileData loaded = JsonConvert.DeserializeObject<PlayerProfileData>(json, _jsonSettings);
            return loaded ?? new PlayerProfileData();
        }

        private (string json, string tempPath) PrepareWrite(PlayerProfileData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            string directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonConvert.SerializeObject(data, _jsonSettings);
            return (json, _filePath + ".tmp");
        }

        private void CommitTempFile(string tempPath)
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }

            File.Move(tempPath, _filePath);
        }
    }
}
