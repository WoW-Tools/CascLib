using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CASCLib
{
    public struct WowVfsRootEntry
    {
        public MD5Hash cKey;
        public ContentFlags ContentFlags;
        public LocaleFlags LocaleFlags;
        public MD5Hash eKey;
        public int ContentOffset; // not used
        public int ContentLength;
        public int CftOffset; // only used once and not need to be stored
    }

    public class ContentFlagsFilterVfs : ContentFlagsFilter
    {
        public static IEnumerable<WowVfsRootEntry> Filter(IEnumerable<WowVfsRootEntry> entries, bool alternate, bool highResTexture)
        {
            IEnumerable<WowVfsRootEntry> temp = entries;

            if (temp.Any(e => Check(e.ContentFlags, ContentFlags.Alternate, true)))
                temp = temp.Where(e => Check(e.ContentFlags, ContentFlags.Alternate, alternate));

            if (temp.Any(e => Check(e.ContentFlags, ContentFlags.HighResTexture, true)))
                temp = temp.Where(e => Check(e.ContentFlags, ContentFlags.HighResTexture, highResTexture));

            return temp;
        }
    }

    public sealed class WowTVFSRootHandler : TVFSRootHandler
    {
        private readonly MultiDictionary<int, WowVfsRootEntry> RootData = new MultiDictionary<int, WowVfsRootEntry>();
        private readonly Dictionary<int, ulong> FileDataStore = new Dictionary<int, ulong>();
        private readonly Dictionary<ulong, int> FileDataStoreReverse = new Dictionary<ulong, int>();
        private readonly HashSet<ulong> UnknownFiles = new HashSet<ulong>();
        public IReadOnlyDictionary<int, List<WowVfsRootEntry>> RootEntries => RootData;

        public override int Count => RootData.Count;
        public override int CountTotal => RootData.Sum(re => re.Value.Count);
        public override int CountUnknown => UnknownFiles.Count;

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

                    ulong hash = FileDataHash.ComputeHash(fileDataId);

#if DEBUG
                    Logger.WriteLine($"{tvfsEntry.Value.Orig} {tvfsEntry.Key:X16} {hash:X16} {locale} {content} {fileDataId} {cKey.ToHexString()}");
#endif
                    var vfsEntries = base.GetVfsRootEntries(tvfsEntry.Key);

                    if (vfsEntries.Count != 1)
                        throw new Exception("vfsEntries.Count != 1");

                    RootData.Add(fileDataId, new WowVfsRootEntry { cKey = cKey, LocaleFlags = locale, ContentFlags = content, eKey = vfsEntries[0].eKey, ContentLength = vfsEntries[0].ContentLength, ContentOffset = vfsEntries[0].ContentOffset, CftOffset = vfsEntries[0].CftOffset });

                    if (FileDataStore.TryGetValue(fileDataId, out ulong hash2))
                    {
                        if (hash2 == hash)
                        {
                            // duplicate, skipping
                        }
                        else
                        {
                            Logger.WriteLine($"ERROR: got multiple hashes for filedataid {fileDataId}: {hash:X16} {hash2:X16}");
                        }
                        continue;
                    }

                    FileDataStore.Add(fileDataId, hash);
                    FileDataStoreReverse.Add(hash, fileDataId);
                    //SetHashDuplicate(tvfsEntry.Key, hash);
                }
#if DEBUG
                else
                {
                    Logger.WriteLine($"{tvfsEntry.Value.Orig} {LocaleFlags.All} {ContentFlags.None} {0}");
                }
#endif
            }

            worker?.ReportProgress(100);
        }

        public IEnumerable<RootEntry> GetAllEntriesByFileDataId(int fileDataId) => GetAllEntries(GetHashByFileDataId(fileDataId));

        //public override IEnumerable<KeyValuePair<ulong, RootEntry>> GetAllEntries()
        //{
        //    foreach (var set in RootData)
        //        foreach (var entry in set.Value)
        //            yield return new KeyValuePair<ulong, RootEntry>(FileDataStore[set.Key], entry);
        //}

        //public override IEnumerable<RootEntry> GetAllEntries(ulong hash)
        //{
        //    if (!FileDataStoreReverse.TryGetValue(hash, out int fileDataId))
        //        yield break;

        //    if (!RootData.TryGetValue(fileDataId, out List<RootEntry> result))
        //        yield break;

        //    foreach (var entry in result)
        //        yield return entry;
        //}

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
                IEnumerable<RootEntry> rootInfosLocaleOverride = ContentFlagsFilter.Filter(rootInfosLocale, OverrideArchive, PreferHighResTextures);

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

        public override List<VfsRootEntry> GetVfsRootEntries(ulong hash)
        {
            if (!FileDataStoreReverse.TryGetValue(hash, out int fileDataId))
                return null;

            if (!RootData.TryGetValue(fileDataId, out List<WowVfsRootEntry> result))
                return null;

            var rootInfos = result;

            if (!rootInfos.Any())
                return null;

            var rootInfosLocale = rootInfos.Where(re => (re.LocaleFlags & Locale) != LocaleFlags.None);

            if (rootInfosLocale.Count() > 1)
            {
                IEnumerable<WowVfsRootEntry> rootInfosLocaleOverride = ContentFlagsFilterVfs.Filter(rootInfosLocale, OverrideArchive, PreferHighResTextures);

                if (rootInfosLocaleOverride.Any())
                    rootInfosLocale = rootInfosLocaleOverride;
            }

            return rootInfosLocale.Select(e => new VfsRootEntry { eKey = e.eKey, ContentLength = e.ContentLength, ContentOffset = e.ContentOffset, CftOffset = e.CftOffset }).ToList();
        }

        public int GetFileDataIdByHash(ulong hash)
        {
            FileDataStoreReverse.TryGetValue(hash, out int fid);
            return fid;
        }

        public int GetFileDataIdByName(string name) => GetFileDataIdByHash(Hasher.ComputeHash(name));

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
            UnknownFiles.Clear();

            // Create new tree based on specified locale
            foreach (var rootEntry in RootData)
            {
                var rootInfosLocale = rootEntry.Value.Where(re => (re.LocaleFlags & Locale) != LocaleFlags.None);

                if (rootInfosLocale.Count() > 1)
                {
                    IEnumerable<WowVfsRootEntry> rootInfosLocaleOverride = ContentFlagsFilterVfs.Filter(rootInfosLocale, OverrideArchive, PreferHighResTextures);

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
                    UnknownFiles.Add(hash);
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
