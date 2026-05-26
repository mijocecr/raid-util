using System;
using System.IO;
using Newtonsoft.Json;
using RAID_Util.Models;

namespace RAID_Util.Services
{
    public static class ArrayConfigService
    {
        private static string BasePath =>
            $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/.config/raid-util/arrays";

        public static ArrayConfig Load(string arrayName)
        {
            try
            {
                Directory.CreateDirectory(BasePath);
                string path = Path.Combine(BasePath, $"{arrayName}.json");

                if (!File.Exists(path))
                    return new ArrayConfig(); // default config

                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<ArrayConfig>(json) ?? new ArrayConfig();
            }
            catch
            {
                return new ArrayConfig();
            }
        }

        public static void Save(string arrayName, ArrayConfig cfg)
        {
            Directory.CreateDirectory(BasePath);
            string path = Path.Combine(BasePath, $"{arrayName}.json");

            string json = JsonConvert.SerializeObject(cfg, Formatting.Indented);
            File.WriteAllText(path, json);
        }
    }
}