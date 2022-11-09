using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CASCLib
{
    public sealed class WowTVFSRootHandler : TVFSRootHandler
    {
        private readonly MultiDictionary<int, RootEntry> RootData = new MultiDictionary<int, RootEntry>();
        private readonly Dictionary<int, ulong> FileDataStore = new Dictionary<int, ulong>();
        private readonly Dictionary<ulong, int> FileDataStoreReverse = new Dictionary<ulong, int>();

        public override int Count => RootData.Count;
        public override int CountTotal => RootData.Sum(re => re.Value.Count);

        public WowTVFSRootHandler(BackgroundWorkerEx worker, CASCHandler casc) : base(worker, casc)
        {
            worker?.ReportProgress(0, "Loading \"root\"...");

            foreach (var tvfsEntry in fileTree)
            {
                if (tvfsEntry.Value.Orig.Length == 53)
                {
#if NET6_0_OR_GREATER
                    ReadOnlySpan<char> entryData = tvfsEntry.Value.Orig.AsSpan();
                    LocaleFlags locale = (LocaleFlags)int.Parse(entryData.Slice(0, 8), System.Globalization.NumberStyles.HexNumber);
                    ContentFlags content = (ContentFlags)int.Parse(entryData.Slice(8, 4), System.Globalization.NumberStyles.HexNumber);
                    int fileDataId = int.Parse(entryData.Slice(13, 8), System.Globalization.NumberStyles.HexNumber);
                    ReadOnlySpan<char> cKeySpan = entryData.Slice(21, 32);
                    MD5Hash cKey = Convert.FromHexString(cKeySpan).ToMD5();
#else
                    string entryData = tvfsEntry.Value.Orig;
                    LocaleFlags locale = (LocaleFlags)int.Parse(entryData.Substring(0, 8), System.Globalization.NumberStyles.HexNumber);
                    ContentFlags content = (ContentFlags)int.Parse(entryData.Substring(8, 4), System.Globalization.NumberStyles.HexNumber);
                    int fileDataId = int.Parse(entryData.Substring(13, 8), System.Globalization.NumberStyles.HexNumber);
                    byte[] cKeyBytes = entryData.Substring(21, 32).FromHexString();
                    MD5Hash cKey = cKeyBytes.ToMD5();
#endif
                    Logger.WriteLine($"{tvfsEntry.Value.Orig} {locale} {content} {fileDataId} {cKey.ToHexString()}");

                    ulong hash = FileDataHash.ComputeHash(fileDataId);
                    RootData.Add(fileDataId, new RootEntry { cKey = cKey, LocaleFlags = locale, ContentFlags = content });

                    if (FileDataStore.TryGetValue(fileDataId, out ulong hash2))
                    {
                        if (hash2 == hash)
                        {
                            // duplicate, skipping
                        }
                        else
                        {
                            Logger.WriteLine($"ERROR: got miltiple hashes for filedataid {fileDataId}: {hash:X16} {hash2:X16}");
                        }
                        continue;
                    }

                    FileDataStore.Add(fileDataId, hash);
                    FileDataStoreReverse.Add(hash, fileDataId);
                    SetHashDuplicate(tvfsEntry.Key, hash);
                }
                else
                {
                    Logger.WriteLine($"{tvfsEntry.Value.Orig} {LocaleFlags.All} {ContentFlags.None} {0}");
                }
            }

            worker?.ReportProgress(100);
        }

        public IEnumerable<RootEntry> GetAllEntriesByFileDataId(int fileDataId) => GetAllEntries(GetHashByFileDataId(fileDataId));

        public override IEnumerable<KeyValuePair<ulong, RootEntry>> GetAllEntries()
        {
            foreach (var set in RootData)
                foreach (var entry in set.Value)
                    yield return new KeyValuePair<ulong, RootEntry>(FileDataStore[set.Key], entry);
        }

        public override IEnumerable<RootEntry> GetAllEntries(ulong hash)
        {
            if (!FileDataStoreReverse.TryGetValue(hash, out int fileDataId))
                yield break;

            if (!RootData.TryGetValue(fileDataId, out List<RootEntry> result))
                yield break;

            foreach (var entry in result)
                yield return entry;
        }

        public IEnumerable<RootEntry> GetEntriesByFileDataId(int fileDataId) => GetEntries(GetHashByFileDataId(fileDataId));

        // Returns only entries that match current locale and override setting
        public override IEnumerable<RootEntry> GetEntries(ulong hash)
        {
            var rootInfos = GetAllEntries(hash);

            if (!rootInfos.Any())
                yield break;

            var rootInfosLocale = rootInfos.Where(re => (re.LocaleFlags & Locale) != LocaleFlags.None);

            if (rootInfosLocale.Count() > 1)
            {
                IEnumerable<RootEntry> rootInfosLocaleOverride;

                if (OverrideArchive)
                    rootInfosLocaleOverride = rootInfosLocale.Where(re => (re.ContentFlags & ContentFlags.Alternate) != ContentFlags.None);
                else
                    rootInfosLocaleOverride = rootInfosLocale.Where(re => (re.ContentFlags & ContentFlags.Alternate) == ContentFlags.None);

                if (rootInfosLocaleOverride.Any())
                    rootInfosLocale = rootInfosLocaleOverride;
            }

            foreach (var entry in rootInfosLocale)
                yield return entry;
        }

        public bool FileExist(int fileDataId) => RootData.ContainsKey(fileDataId);

        public ulong GetHashByFileDataId(int fileDataId)
        {
            FileDataStore.TryGetValue(fileDataId, out ulong hash);
            return hash;
        }

        public override void LoadListFile(string path, BackgroundWorkerEx worker = null)
        {
            //CASCFile.Files.Clear();

            using (var _ = new PerfCounter("WowRootHandler::LoadListFile()"))
            {
                worker?.ReportProgress(0, "Loading \"listfile\"...");

                if (!File.Exists(path))
                {
                    Logger.WriteLine("WowRootHandler: list file missing!");
                    return;
                }

                bool isCsv = Path.GetExtension(path) == ".csv";

                Logger.WriteLine($"WowRootHandler: loading listfile {path}...");

                using (var fs2 = File.Open(path, FileMode.Open))
                using (var sr = new StreamReader(fs2))
                {
                    string line;

                    char[] splitChar = isCsv ? new char[] { ';' } : new char[] { ' ' };

                    while ((line = sr.ReadLine()) != null)
                    {
                        string[] tokens = line.Split(splitChar, 2);

                        if (tokens.Length != 2)
                        {
                            Logger.WriteLine($"Invalid line in listfile: {line}");
                            continue;
                        }

                        if (!int.TryParse(tokens[0], out int fileDataId))
                        {
                            Logger.WriteLine($"Invalid line in listfile: {line}");
                            continue;
                        }

                        // skip invalid names
                        if (!RootData.ContainsKey(fileDataId))
                        {
                            Logger.WriteLine($"Invalid fileDataId in listfile: {line}");
                            continue;
                        }

                        string file = tokens[1];

                        ulong fileHash = FileDataStore[fileDataId];

                        if (!CASCFile.Files.ContainsKey(fileHash))
                            CASCFile.Files.Add(fileHash, new CASCFile(fileHash, file));
                        else
                            Logger.WriteLine($"Duplicate fileDataId {fileDataId} detected: {line}");

                        worker?.ReportProgress((int)(sr.BaseStream.Position / (float)sr.BaseStream.Length * 100));
                    }
                }

                Logger.WriteLine($"WowRootHandler: loaded {CASCFile.Files.Count} valid file names");
            }
        }

        protected override CASCFolder CreateStorageTree()
        {
            var root = new CASCFolder("root");

            // Reset counts
            CountSelect = 0;

            // Create new tree based on specified locale
            foreach (var rootEntry in RootData)
            {
                var rootInfosLocale = rootEntry.Value.Where(re => (re.LocaleFlags & Locale) != LocaleFlags.None);

                if (rootInfosLocale.Count() > 1)
                {
                    IEnumerable<RootEntry> rootInfosLocaleOverride;

                    if (OverrideArchive)
                        rootInfosLocaleOverride = rootInfosLocale.Where(re => (re.ContentFlags & ContentFlags.Alternate) != ContentFlags.None);
                    else
                        rootInfosLocaleOverride = rootInfosLocale.Where(re => (re.ContentFlags & ContentFlags.Alternate) == ContentFlags.None);

                    if (rootInfosLocaleOverride.Any())
                        rootInfosLocale = rootInfosLocaleOverride;
                }

                if (!rootInfosLocale.Any())
                    continue;

                string filename;

                ulong hash = FileDataStore[rootEntry.Key];

                if (!CASCFile.Files.TryGetValue(hash, out CASCFile file))
                {
                    filename = "unknown\\" + "FILEDATA_" + rootEntry.Key;
                }
                else
                {
                    filename = file.FullName;
                }

                CreateSubTree(root, hash, filename);
                CountSelect++;
            }

            Logger.WriteLine("WowRootHandler: {0} file names missing for locale {1}", CountUnknown, Locale);

            return root;
        }
    }
}
