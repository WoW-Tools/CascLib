using System;
using System.Collections.Generic;
using System.IO;

namespace CASCLib
{
    public sealed class CASCHandlerLite : CASCHandlerBase
    {
        private readonly Dictionary<ulong, MD5Hash> HashToEKey = new Dictionary<ulong, MD5Hash>();
        private readonly Dictionary<int, ulong> FileDataIdToHash = new Dictionary<int, ulong>();

        private CASCHandlerLite(CASCConfig config, LocaleFlags locale, BackgroundWorkerEx worker) : base(config, worker)
        {
            if (config.GameType != CASCGameType.WoW)
                throw new Exception("Unsupported game " + config.BuildUID);

            Logger.WriteLine("CASCHandlerLite: loading encoding data...");

            EncodingHandler EncodingHandler;

            using (var _ = new PerfCounter("new EncodingHandler()"))
            {
                using (var fs = OpenEncodingFile(this))
                    EncodingHandler = new EncodingHandler(fs, worker);
            }

            Logger.WriteLine("CASCHandlerLite: loaded {0} encoding data", EncodingHandler.Count);

            Logger.WriteLine("CASCHandlerLite: loading root data...");

            WowRootHandler RootHandler;

            using (var _ = new PerfCounter("new RootHandler()"))
            {
                using (var fs = OpenRootFile(EncodingHandler, this))
                    RootHandler = new WowRootHandler(fs, worker);
            }

            Logger.WriteLine("CASCHandlerLite: loaded {0} root data", RootHandler.Count);

            RootHandler.SetFlags(locale, false, false, false);

            RootEntry rootEntry;

            foreach (var entry in RootHandler.GetAllEntries())
            {
                rootEntry = entry.Value;

                if ((rootEntry.LocaleFlags == locale || (rootEntry.LocaleFlags & locale) != LocaleFlags.None) && (rootEntry.ContentFlags & ContentFlags.Alternate) == ContentFlags.None)
                {
                    if (EncodingHandler.GetEntry(rootEntry.cKey, out EncodingEntry enc))
                    {
                        if (!HashToEKey.ContainsKey(entry.Key))
                        {
                            HashToEKey.Add(entry.Key, enc.Keys[0]);
                            FileDataIdToHash.Add(RootHandler.GetFileDataIdByHash(entry.Key), entry.Key);
                        }
                    }
                }
            }

            RootHandler.Clear();
            RootHandler = null;
            EncodingHandler.Clear();
            EncodingHandler = null;
            GC.Collect();

            Logger.WriteLine("CASCHandlerLite: loaded {0} files", HashToEKey.Count);
        }

        public static CASCHandlerLite OpenStorage(LocaleFlags locale, CASCConfig config, BackgroundWorkerEx worker = null)
        {
            return Open(locale, worker, config);
        }

        public static CASCHandlerLite OpenLocalStorage(string basePath, LocaleFlags locale, string product, BackgroundWorkerEx worker = null)
        {
            CASCConfig config = CASCConfig.LoadLocalStorageConfig(basePath, product);

            return Open(locale, worker, config);
        }

        public static CASCHandlerLite OpenOnlineStorage(string product, LocaleFlags locale, string region = "us", BackgroundWorkerEx worker = null)
        {
            CASCConfig config = CASCConfig.LoadOnlineStorageConfig(product, region);

            return Open(locale, worker, config);
        }

        private static CASCHandlerLite Open(LocaleFlags locale, BackgroundWorkerEx worker, CASCConfig config)
        {
            using (var _ = new PerfCounter("new CASCHandlerLite()"))
            {
                return new CASCHandlerLite(config, locale, worker);
            }
        }

        public override bool FileExists(int fileDataId) => FileDataIdToHash.ContainsKey(fileDataId);

        public override bool FileExists(string file) => FileExists(Hasher.ComputeHash(file));

        public override bool FileExists(ulong hash) => HashToEKey.ContainsKey(hash);

        public override Stream OpenFile(int filedata)
        {
            if (FileDataIdToHash.TryGetValue(filedata, out ulong hash))
                return OpenFile(hash);

            return null;
        }

        public override Stream OpenFile(string name) => OpenFile(Hasher.ComputeHash(name));

        public override Stream OpenFile(ulong hash)
        {
            if (HashToEKey.TryGetValue(hash, out MD5Hash eKey))
                return OpenFile(eKey);

            if (CASCConfig.ThrowOnFileNotFound)
                throw new FileNotFoundException($"{hash:X16}");

            return null;
        }

        public override void SaveFileTo(ulong hash, string extractPath, string fullName)
        {
            if (HashToEKey.TryGetValue(hash, out MD5Hash eKey))
            {
                SaveFileTo(eKey, extractPath, fullName);
                return;
            }

            if (CASCConfig.ThrowOnFileNotFound)
                throw new FileNotFoundException($"{hash:X16}");
        }
    }
}
