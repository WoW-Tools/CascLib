using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CASCLib
{
    [Flags]
    public enum LocaleFlags : uint
    {
        All = 0xFFFFFFFF,
        None = 0,
        Unk1 = 0x1,
        enUS = 0x2,
        koKR = 0x4,
        Unk8 = 0x8,
        frFR = 0x10,
        deDE = 0x20,
        zhCN = 0x40,
        esES = 0x80,
        zhTW = 0x100,
        enGB = 0x200,
        enCN = 0x400,
        enTW = 0x800,
        esMX = 0x1000,
        ruRU = 0x2000,
        ptBR = 0x4000,
        itIT = 0x8000,
        ptPT = 0x10000,
        enSG = 0x01000000, // custom
        plPL = 0x02000000, // custom
        jaJP = 0x04000000, // custom
        trTR = 0x08000000, // custom
        arSA = 0x10000000, // custom
        All_WoW = enUS | koKR | frFR | deDE | zhCN | esES | zhTW | enGB | esMX | ruRU | ptBR | itIT | ptPT
    }

    [Flags]
    public enum ContentFlags : uint
    {
        None = 0,
        HighResTexture = 0x1, // seen on *.wlm files
        F00000002 = 0x2,
        F00000004 = 0x4, // install?
        Windows = 0x8, // added in 7.2.0.23436
        MacOS = 0x10, // added in 7.2.0.23436
        F00000020 = 0x20, // x86?
        F00000040 = 0x40, // x64?
        Alternate = 0x80, // many chinese models have this flag
        F00000100 = 0x100, // apparently client doesn't load files with this flag
        F00000800 = 0x800, // only seen on UpdatePlugin files
        F00008000 = 0x8000, // Windows ARM64?
        F00020000 = 0x20000, // new 9.0
        F00040000 = 0x40000, // new 9.0
        F00080000 = 0x80000, // new 9.0
        F00100000 = 0x100000, // new 9.0
        F00200000 = 0x200000, // new 9.0
        F00400000 = 0x400000, // new 9.0
        F00800000 = 0x800000, // new 9.0
        F02000000 = 0x2000000, // new 9.0
        F04000000 = 0x4000000, // new 9.0
        Encrypted = 0x8000000, // encrypted may be?
        NoNameHash = 0x10000000, // doesn't have name hash?
        F20000000 = 0x20000000, // added in 21737, used for many cinematics
        F40000000 = 0x40000000,
        NotCompressed = 0x80000000 // sounds have this flag
    }

    public readonly struct MD5Hash
    {
        public readonly ulong lowPart;
        public readonly ulong highPart;
    }

    public struct RootEntry
    {
        public MD5Hash cKey;
        public ContentFlags ContentFlags;
        public LocaleFlags LocaleFlags;
    }

    public static class FileDataHash
    {
        public static ulong ComputeHash(int fileDataId)
        {
            ulong baseOffset = 0xCBF29CE484222325UL;

            for (int i = 0; i < 4; i++)
            {
                baseOffset = 0x100000001B3L * ((((uint)fileDataId >> (8 * i)) & 0xFF) ^ baseOffset);
            }

            return baseOffset;
        }
    }

    public class ContentFlagsFilter
    {
        protected static bool Check(ContentFlags value, ContentFlags flag, bool include) => include ? (value & flag) != ContentFlags.None : (value & flag) == ContentFlags.None;

        public static IEnumerable<RootEntry> Filter(IEnumerable<RootEntry> entries, bool alternate, bool highResTexture)
        {
            IEnumerable<RootEntry> temp = entries;

            if (temp.Any(e => Check(e.ContentFlags, ContentFlags.Alternate, true)))
                temp = temp.Where(e => Check(e.ContentFlags, ContentFlags.Alternate, alternate));

            if (temp.Any(e => Check(e.ContentFlags, ContentFlags.HighResTexture, true)))
                temp = temp.Where(e => Check(e.ContentFlags, ContentFlags.HighResTexture, highResTexture));

            return temp;
        }
    }

    public class WowRootHandler : RootHandlerBase
    {
        private MultiDictionary<int, RootEntry> RootData = new MultiDictionary<int, RootEntry>();
        private Dictionary<int, ulong> FileDataStore = new Dictionary<int, ulong>();
        private Dictionary<ulong, int> FileDataStoreReverse = new Dictionary<ulong, int>();
        private HashSet<ulong> UnknownFiles = new HashSet<ulong>();

        public override int Count => RootData.Count;
        public override int CountTotal => RootData.Sum(re => re.Value.Count);
        public override int CountUnknown => UnknownFiles.Count;
        public IReadOnlyDictionary<int, List<RootEntry>> RootEntries => RootData;
        public IReadOnlyDictionary<int, ulong> FileDataToLookup => FileDataStore;

        public WowRootHandler(BinaryReader stream, BackgroundWorkerEx worker)
        {
            worker?.ReportProgress(0, "Loading \"root\"...");

            int magic = stream.ReadInt32();

            int numFilesTotal = 0, numFilesWithNameHash = 0, numFilesRead = 0;

            const int TSFMMagic = 0x4D465354;

            int headerSize;
            bool isLegacy;

            if (magic == TSFMMagic)
            {
                isLegacy = false;

                if (stream.BaseStream.Length < 12)
                    throw new Exception("build manifest is truncated");

                int field04 = stream.ReadInt32();
                int field08 = stream.ReadInt32();

                int version = field08;
                headerSize = field04;

                if (version != 1)
                {
                    numFilesTotal = field04;
                    numFilesWithNameHash = field08;
                    headerSize = 12;
                }
                else
                {
                    numFilesTotal = stream.ReadInt32();
                    numFilesWithNameHash = stream.ReadInt32();
                }
            }
            else
            {
                isLegacy = true;
                headerSize = 0;
                numFilesTotal = (int)(stream.BaseStream.Length / 28);
                numFilesWithNameHash = (int)(stream.BaseStream.Length / 28);
            }

            if (stream.BaseStream.Length < headerSize)
                throw new Exception("build manifest is truncated");

            stream.BaseStream.Position = headerSize;

            int blockIndex = 0;

            while (stream.BaseStream.Position < stream.BaseStream.Length)
            {
                int count = stream.ReadInt32();

                numFilesRead += count;

                ContentFlags contentFlags = (ContentFlags)stream.ReadUInt32();
                LocaleFlags localeFlags = (LocaleFlags)stream.ReadUInt32();

                if (localeFlags == LocaleFlags.None)
                    throw new InvalidDataException("block.LocaleFlags == LocaleFlags.None");

                if (contentFlags != ContentFlags.None && (contentFlags & (ContentFlags.HighResTexture | ContentFlags.Windows | ContentFlags.MacOS | ContentFlags.Alternate | ContentFlags.F00020000 | ContentFlags.F00080000 | ContentFlags.F00100000 | ContentFlags.F00200000 | ContentFlags.F00400000 | ContentFlags.F02000000 | ContentFlags.NotCompressed | ContentFlags.NoNameHash | ContentFlags.F20000000)) == 0)
                    throw new InvalidDataException("block.ContentFlags != ContentFlags.None");

                RootEntry[] entries = new RootEntry[count];
                int[] filedataIds = new int[count];

                int fileDataIndex = 0;

                for (var i = 0; i < count; ++i)
                {
                    entries[i].LocaleFlags = localeFlags;
                    entries[i].ContentFlags = contentFlags;

                    filedataIds[i] = fileDataIndex + stream.ReadInt32();
                    fileDataIndex = filedataIds[i] + 1;
                }

                //Console.WriteLine($"Block {blockIndex}: {contentFlags} {localeFlags} count {count}");

                ulong[] nameHashes = null;

                if (!isLegacy)
                {
                    for (var i = 0; i < count; ++i)
                        entries[i].cKey = stream.Read<MD5Hash>();

                    if ((contentFlags & ContentFlags.NoNameHash) == 0)
                    {
                        nameHashes = new ulong[count];

                        for (var i = 0; i < count; ++i)
                            nameHashes[i] = stream.ReadUInt64();
                    }
                }
                else
                {
                    nameHashes = new ulong[count];

                    for (var i = 0; i < count; ++i)
                    {
                        entries[i].cKey = stream.Read<MD5Hash>();
                        nameHashes[i] = stream.ReadUInt64();
                    }
                }

                for (var i = 0; i < count; ++i)
                {
                    int fileDataId = filedataIds[i];

                    //Logger.WriteLine("filedataid {0}", fileDataId);

                    ulong hash;

                    if (nameHashes == null)
                    {
                        hash = FileDataHash.ComputeHash(fileDataId);
                    }
                    else
                    {
                        hash = nameHashes[i];
                    }

                    RootData.Add(fileDataId, entries[i]);

                    //Console.WriteLine($"File: {fileDataId:X8} {hash:X16} {entries[i].cKey.ToHexString()}");

                    if (FileDataStore.TryGetValue(fileDataId, out ulong hash2))
                    {
                        if (hash2 == hash)
                        {
                            // duplicate, skipping
                        }
                        else
                        {
                            Logger.WriteLine("ERROR: got multiple hashes for filedataid {0}", fileDataId);
                        }
                        continue;
                    }

                    FileDataStore.Add(fileDataId, hash);
                    FileDataStoreReverse.Add(hash, fileDataId);

                    if (nameHashes != null)
                    {
                        // generate our custom hash as well so we can still find file without calling GetHashByFileDataId in some weird cases
                        ulong fileDataHash = FileDataHash.ComputeHash(fileDataId);
                        FileDataStoreReverse.Add(fileDataHash, fileDataId);
                    }
                }

                worker?.ReportProgress((int)(stream.BaseStream.Position / (float)stream.BaseStream.Length * 100));

                blockIndex++;
            }
        }

        public IEnumerable<RootEntry> GetAllEntriesByFileDataId(int fileDataId) => GetAllEntries(GetHashByFileDataId(fileDataId));

        public override IEnumerable<KeyValuePair<ulong, RootEntry>> GetAllEntries()
        {
            foreach (var set in RootData)
                foreach (var entry in set.Value)
                    yield return new KeyValuePair<ulong, RootEntry>(FileDataStore[set.Key], entry);
        }

        public IEnumerable<(int FileDataId, RootEntry Entry)> GetAllEntriesWithFileDataId()
        {
            foreach (var set in RootData)
                foreach (var entry in set.Value)
                    yield return (set.Key, entry);
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
                    IEnumerable<RootEntry> rootInfosLocaleOverride = ContentFlagsFilter.Filter(rootInfosLocale, OverrideArchive, PreferHighResTextures);

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

        public bool IsUnknownFile(ulong hash) => UnknownFiles.Contains(hash);

        public override void Clear()
        {
            RootData.Clear();
            RootData = null;
            FileDataStore.Clear();
            FileDataStore = null;
            FileDataStoreReverse.Clear();
            FileDataStoreReverse = null;
            UnknownFiles.Clear();
            UnknownFiles = null;
            Root?.Files.Clear();
            Root?.Folders.Clear();
            Root = null;
            CASCFile.Files.Clear();
        }

        public override void Dump(EncodingHandler encodingHandler = null)
        {
            Logger.WriteLine("WowRootHandler Dump:");

            foreach (var fd in RootData.OrderBy(r => r.Key))
            {
                string name;

                if (FileDataStore.TryGetValue(fd.Key, out ulong hash) && CASCFile.Files.TryGetValue(hash, out CASCFile file))
                    name = file.FullName;
                else
                    name = $"FILEDATA_{fd.Key}";

                Logger.WriteLine($"FileData: {fd.Key:D7} Hash: {hash:X16} Locales: {fd.Value.Aggregate(LocaleFlags.None, (a, b) => a | b.LocaleFlags)} Name: {name}");

                foreach (var entry in fd.Value)
                {
                    Logger.WriteLine($"\tcKey: {entry.cKey.ToHexString()} Locale: {entry.LocaleFlags} CF: {entry.ContentFlags}");

                    if (encodingHandler != null)
                    {
                        if (encodingHandler.GetEntry(entry.cKey, out var encodingEntry))
                        {
                            foreach (var eKey in encodingEntry.Keys)
                            {
                                var keys = encodingHandler.GetEncryptionKeys(eKey);
                                if (keys != null)
                                    Logger.WriteLine($"\teKey: {eKey.ToHexString()} TactKeys: {string.Join(",", keys.Select(k => $"{k:X16}"))} Size: {encodingEntry.Size}");
                                else
                                    Logger.WriteLine($"\teKey: {eKey.ToHexString()} TactKeys: NA Size: {encodingEntry.Size}");
                            }
                        }
                    }
                }
            }
        }
    }
}
