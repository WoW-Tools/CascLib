using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CASCLib
{
    [Flags]
    public enum LoadFlags
    {
        All = -1,
        None = 0,
        Download = 1,
        Install = 2,
        FileIndex = 4
    }

    public class VerBarConfig
    {
        private readonly List<Dictionary<string, string>> Data = new List<Dictionary<string, string>>();

        public int Count => Data.Count;

        public Dictionary<string, string> this[int index] => Data[index];

        public static VerBarConfig ReadVerBarConfig(Stream stream)
        {
            using (var sr = new StreamReader(stream))
                return ReadVerBarConfig(sr);
        }

        public static VerBarConfig ReadVerBarConfig(TextReader reader)
        {
            var result = new VerBarConfig();

            int lineNum = 0;

            string[] fields = null;

            string line;

            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) // skip empty lines and comments
                    continue;

                string[] tokens = line.Split(new char[] { '|' });

                if (lineNum == 0) // keys
                {
                    fields = new string[tokens.Length];

                    for (int i = 0; i < tokens.Length; ++i)
                    {
                        fields[i] = tokens[i].Split(new char[] { '!' })[0].Replace(" ", "");
                    }
                }
                else // values
                {
                    result.Data.Add(new Dictionary<string, string>());

                    for (int i = 0; i < tokens.Length; ++i)
                    {
                        result.Data[lineNum - 1].Add(fields[i], tokens[i]);
                    }
                }

                lineNum++;
            }

            return result;
        }
    }

    public class KeyValueConfig
    {
        private readonly Dictionary<string, List<string>> m_data = new Dictionary<string, List<string>>();

        public List<string> this[string key]
        {
            get
            {
                m_data.TryGetValue(key, out List<string> ret);
                return ret;
            }
        }

        public IReadOnlyDictionary<string, List<string>> Values => m_data;

        public static KeyValueConfig ReadKeyValueConfig(Stream stream)
        {
            var sr = new StreamReader(stream);
            return ReadKeyValueConfig(sr);
        }

        public static KeyValueConfig ReadKeyValueConfig(TextReader reader)
        {
            var result = new KeyValueConfig();
            string line;

            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) // skip empty lines and comments
                    continue;

                string[] tokens = line.Split(new char[] { '=' }, StringSplitOptions.RemoveEmptyEntries);

                if (tokens.Length != 2)
                    throw new Exception("KeyValueConfig: tokens.Length != 2");

                var values = tokens[1].Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var valuesList = values.ToList();
                result.m_data.Add(tokens[0].Trim(), valuesList);
            }
            return result;
        }
    }

    public class CASCConfig
    {
        KeyValueConfig _CDNConfig;

        List<KeyValueConfig> _Builds;

        VerBarConfig _BuildInfo;
        VerBarConfig _CdnsData;
        VerBarConfig _VersionsData;

        public string Region { get; private set; }
        public CASCGameType GameType { get; private set; }
        public static bool ValidateData { get; set; } = true;
        public static bool ThrowOnFileNotFound { get; set; } = true;
        public static bool ThrowOnMissingDecryptionKey { get; set; } = true;
        public static bool UseWowTVFS { get; set; } = false;
        public static bool UseOnlineFallbackForMissingFiles { get; set; } = true;
        public static LoadFlags LoadFlags { get; set; } = LoadFlags.FileIndex;

        private CASCConfig() { }

        public static CASCConfig LoadOnlineStorageConfig(string product, string region, bool useCurrentBuild = false, ILoggerOptions loggerOptions = null)
        {
            if (product == null)
                throw new ArgumentNullException(nameof(product));
            if (region == null)
                throw new ArgumentNullException(nameof(region));

            Logger.Init(loggerOptions);

            var config = new CASCConfig { OnlineMode = true, Region = region, Product = product };

            using (var ribbit = new RibbitClient("us"))
            using (var cdnsStream = ribbit.GetProductInfoStream(product, ProductInfoType.Cdns))
            //using (var cdnsStream = CDNIndexHandler.OpenFileDirect(string.Format("http://us.patch.battle.net:1119/{0}/cdns", product)))
            {
                config._CdnsData = VerBarConfig.ReadVerBarConfig(cdnsStream);
            }

            using (var ribbit = new RibbitClient("us"))
            using (var versionsStream = ribbit.GetProductInfoStream(product, ProductInfoType.Versions))
            //using (var versionsStream = CDNIndexHandler.OpenFileDirect(string.Format("http://us.patch.battle.net:1119/{0}/versions", product)))
            {
                config._VersionsData = VerBarConfig.ReadVerBarConfig(versionsStream);
            }

            CDNCache.Init(config);

            config.GameType = CASCGame.DetectGameByUid(product);

            if (File.Exists("fakecdnconfig"))
            {
                using (Stream stream = new FileStream("fakecdnconfig", FileMode.Open))
                {
                    config._CDNConfig = KeyValueConfig.ReadKeyValueConfig(stream);
                }
            }
            else if (File.Exists("fakecdnconfighash"))
            {
                string cdnKey = File.ReadAllText("fakecdnconfighash");
                using (Stream stream = CDNIndexHandler.OpenConfigFileDirect(config, cdnKey))
                {
                    config._CDNConfig = KeyValueConfig.ReadKeyValueConfig(stream);
                }
            }
            else
            {
                string cdnKey = config.GetVersionsVariable("CDNConfig").ToLower();
                //string cdnKey = "da4896ce91922122bc0a2371ee114423";
                using (Stream stream = CDNIndexHandler.OpenConfigFileDirect(config, cdnKey))
                {
                    config._CDNConfig = KeyValueConfig.ReadKeyValueConfig(stream);
                }
            }

            config.ActiveBuild = 0;

            config._Builds = new List<KeyValueConfig>();

            if (config._CDNConfig["builds"] != null)
            {
                for (int i = 0; i < config._CDNConfig["builds"].Count; i++)
                {
                    try
                    {
                        using (Stream stream = CDNIndexHandler.OpenConfigFileDirect(config, config._CDNConfig["builds"][i]))
                        {
                            var cfg = KeyValueConfig.ReadKeyValueConfig(stream);
                            config._Builds.Add(cfg);
                        }
                    }
                    catch
                    {
                        Console.WriteLine("Failed to load build {0}", config._CDNConfig["builds"][i]);
                    }
                }

                if (useCurrentBuild)
                {
                    string curBuildKey = config.GetVersionsVariable("BuildConfig");

                    int buildIndex = config._CDNConfig["builds"].IndexOf(curBuildKey);

                    if (buildIndex != -1)
                        config.ActiveBuild = buildIndex;
                }
            }

            if (File.Exists("fakebuildconfig"))
            {
                using (Stream stream = new FileStream("fakebuildconfig", FileMode.Open))
                {
                    var cfg = KeyValueConfig.ReadKeyValueConfig(stream);
                    config._Builds.Add(cfg);
                }
            }
            else if (File.Exists("fakebuildconfighash"))
            {
                string buildKey = File.ReadAllText("fakebuildconfighash");
                using (Stream stream = CDNIndexHandler.OpenConfigFileDirect(config, buildKey))
                {
                    var cfg = KeyValueConfig.ReadKeyValueConfig(stream);
                    config._Builds.Add(cfg);
                }
            }
            else
            {
                string buildKey = config.GetVersionsVariable("BuildConfig").ToLower();
                //string buildKey = "3b0517b51edbe0b96f6ac5ea7eaaed38";
                using (Stream stream = CDNIndexHandler.OpenConfigFileDirect(config, buildKey))
                {
                    var cfg = KeyValueConfig.ReadKeyValueConfig(stream);
                    config._Builds.Add(cfg);
                }
            }

            return config;
        }

        public static CASCConfig LoadLocalStorageConfig(string basePath, string product, ILoggerOptions loggerOptions = null)
        {
            if (basePath == null)
                throw new ArgumentNullException(nameof(basePath));
            if (product == null)
                throw new ArgumentNullException(nameof(product));

            string buildInfoPath = Path.Combine(basePath, ".build.info");

            if (!File.Exists(buildInfoPath))
                throw new Exception("Local mode not supported for this game!");

            Logger.Init(loggerOptions);

            var config = new CASCConfig { OnlineMode = false, BasePath = basePath, Product = product };

            using (Stream buildInfoStream = new FileStream(buildInfoPath, FileMode.Open))
            {
                config._BuildInfo = VerBarConfig.ReadVerBarConfig(buildInfoStream);
            }

            CASCGameType gameType;

            if (!HasConfigVariable(config._BuildInfo, "Product"))
            {
                var detectedGameType = CASCGame.DetectLocalGame(basePath, product, config.GetBuildInfoVariable("BuildKey"));
                if (detectedGameType.HasValue)
                    gameType = detectedGameType.Value;
                else
                    throw new Exception($"No product {product} found at {basePath}");
            }
            else
            {
                string productUid = config.GetBuildInfoVariable("Product");

                if (productUid == null)
                    throw new Exception($"No product {product} found at {basePath}");

                gameType = CASCGame.DetectGameByUid(product);
            }

            config.GameType = gameType;

            string dataFolder = CASCGame.GetDataFolder(config.GameType);

            config.ActiveBuild = 0;

            config._Builds = new List<KeyValueConfig>();

            if (File.Exists("fakebuildconfig"))
            {
                using (Stream stream = new FileStream("fakebuildconfig", FileMode.Open))
                {
                    var cfg = KeyValueConfig.ReadKeyValueConfig(stream);
                    config._Builds.Add(cfg);
                }
            }
            else if (File.Exists("fakebuildconfighash"))
            {
                string buildKey = File.ReadAllText("fakebuildconfighash");
                string buildCfgPath = Path.Combine(basePath, dataFolder, "config", buildKey.Substring(0, 2), buildKey.Substring(2, 2), buildKey);
                using (Stream stream = new FileStream(buildCfgPath, FileMode.Open))
                {
                    config._Builds.Add(KeyValueConfig.ReadKeyValueConfig(stream));
                }
            }
            else
            {
                string buildKey = config.GetBuildInfoVariable("BuildKey");
                //string buildKey = "5a05c58e28d0b2c3245954b6f4e2ae66";
                string buildCfgPath = Path.Combine(basePath, dataFolder, "config", buildKey.Substring(0, 2), buildKey.Substring(2, 2), buildKey);
                using (Stream stream = new FileStream(buildCfgPath, FileMode.Open))
                {
                    config._Builds.Add(KeyValueConfig.ReadKeyValueConfig(stream));
                }
            }

            if (File.Exists("fakecdnconfig"))
            {
                using (Stream stream = new FileStream("fakecdnconfig", FileMode.Open))
                {
                    config._CDNConfig = KeyValueConfig.ReadKeyValueConfig(stream);
                }
            }
            else if (File.Exists("fakecdnconfighash"))
            {
                string cdnKey = File.ReadAllText("fakecdnconfighash");
                string cdnCfgPath = Path.Combine(basePath, dataFolder, "config", cdnKey.Substring(0, 2), cdnKey.Substring(2, 2), cdnKey);
                using (Stream stream = new FileStream(cdnCfgPath, FileMode.Open))
                {
                    config._CDNConfig = KeyValueConfig.ReadKeyValueConfig(stream);
                }
            }
            else
            {
                string cdnKey = config.GetBuildInfoVariable("CDNKey");
                //string cdnKey = "23d301e8633baaa063189ca9442b3088";
                string cdnCfgPath = Path.Combine(basePath, dataFolder, "config", cdnKey.Substring(0, 2), cdnKey.Substring(2, 2), cdnKey);
                using (Stream stream = new FileStream(cdnCfgPath, FileMode.Open))
                {
                    config._CDNConfig = KeyValueConfig.ReadKeyValueConfig(stream);
                }
            }

            CDNCache.Init(config);

            return config;
        }

        public string BasePath { get; private set; }

        public bool OnlineMode { get; private set; }

        public int ActiveBuild { get; set; }

        public string VersionName { get { return GetBuildInfoVariable("Version") ?? GetVersionsVariable("VersionsName"); } }

        public string Product { get; private set; }

        public MD5Hash RootCKey => _Builds[ActiveBuild]["root"][0].FromHexString().ToMD5();

        public MD5Hash InstallCKey => _Builds[ActiveBuild]["install"][0].FromHexString().ToMD5();

        public string InstallSize => _Builds[ActiveBuild]["install-size"][0];

        public MD5Hash DownloadCKey => _Builds[ActiveBuild]["download"][0].FromHexString().ToMD5();

        public string DownloadSize => _Builds[ActiveBuild]["download-size"][0];

        //public MD5Hash PartialPriorityMD5 => _Builds[ActiveBuild]["partial-priority"][0].ToByteArray().ToMD5();

        //public string PartialPrioritySize => _Builds[ActiveBuild]["partial-priority-size"][0];

        public MD5Hash EncodingCKey => _Builds[ActiveBuild]["encoding"][0].FromHexString().ToMD5();

        public MD5Hash EncodingEKey => _Builds[ActiveBuild]["encoding"][1].FromHexString().ToMD5();

        public string EncodingSize => _Builds[ActiveBuild]["encoding-size"][0];

        public MD5Hash PatchEKey => _Builds[ActiveBuild]["patch"][0].FromHexString().ToMD5();

        public string PatchSize => _Builds[ActiveBuild]["patch-size"][0];

        public string BuildUID => _Builds[ActiveBuild]["build-uid"][0];

        public string BuildProduct => _Builds[ActiveBuild]["build-product"][0];

        public string BuildName => _Builds[ActiveBuild]["build-name"][0];

        public bool IsVfsRoot => _Builds[ActiveBuild]["vfs-root"] != null;

        public MD5Hash VfsRootCKey => _Builds[ActiveBuild]["vfs-root"][0].FromHexString().ToMD5();

        public MD5Hash VfsRootEKey => _Builds[ActiveBuild]["vfs-root"][1].FromHexString().ToMD5();

        public List<(MD5Hash CKey, MD5Hash EKey)> VfsRootList => GetVfsRootList();

        private List<(MD5Hash CKey, MD5Hash EKey)> GetVfsRootList()
        {
            if (!IsVfsRoot)
                return null;

            var list = new List<(MD5Hash CKey, MD5Hash EKey)>();

            var build = _Builds[ActiveBuild];

            var regex = new Regex("(^vfs-\\d+$)");

            foreach (var kvp in build.Values)
            {
                Match match = regex.Match(kvp.Key);
                if (match.Success)
                {
                    list.Add((kvp.Value[0].FromHexString().ToMD5(), kvp.Value[1].FromHexString().ToMD5()));
                }
            }

            return list;
        }

        private int cdnHostIndex;

        public string CDNHost
        {
            get
            {
                if (OnlineMode)
                {
                    var hosts = GetCdnsVariable("Hosts").Split(' ');

                    if (cdnHostIndex >= hosts.Length)
                        cdnHostIndex = 0;

                    return hosts[cdnHostIndex++];
                }
                else
                {
                    return GetBuildInfoVariable("CDNHosts").Split(' ')[0];
                }
            }
        }

        public string CDNPath => OnlineMode ? GetCdnsVariable("Path") : GetBuildInfoVariable("CDNPath");

        public string CDNUrl
        {
            get
            {
                if (OnlineMode)
                    return string.Format("http://{0}/{1}", GetCdnsVariable("Hosts").Split(' ')[0], GetCdnsVariable("Path"));
                else
                    return string.Format("http://{0}{1}", GetBuildInfoVariable("CDNHosts").Split(' ')[0], GetBuildInfoVariable("CDNPath"));
            }
        }

        public string GetBuildInfoVariable(string varName) => GetConfigVariable(_BuildInfo, "Product", Product, varName);

        public string GetVersionsVariable(string varName) => GetConfigVariable(_VersionsData, "Region", Region, varName);

        public string GetCdnsVariable(string varName) => GetConfigVariable(_CdnsData, "Name", Region, varName);

        private static bool HasConfigVariable(VerBarConfig config, string varName) => config[0].ContainsKey(varName);

        private static string GetConfigVariable(VerBarConfig config, string filterParamName, string filterParamValue, string varName)
        {
            if (config == null)
                return null;

            if (config.Count == 1 || !HasConfigVariable(config, filterParamName))
            {
                if (config[0].TryGetValue(varName, out string varValue))
                    return varValue;
                return null;
            }

            for (int i = 0; i < config.Count; i++)
            {
                var cfg = config[i];
                if (cfg.TryGetValue(filterParamName, out string paramValue) && paramValue == filterParamValue && cfg.TryGetValue(varName, out string varValue))
                    return varValue;
            }
            return null;
        }

        public List<string> Archives => _CDNConfig["archives"];

        public string ArchiveGroup => _CDNConfig["archive-group"][0];

        public List<string> PatchArchives => _CDNConfig["patch-archives"];

        public string PatchArchiveGroup => _CDNConfig["patch-archive-group"][0];

        public string FileIndex => _CDNConfig["file-index"][0];

        public List<KeyValueConfig> Builds => _Builds;
    }
}
