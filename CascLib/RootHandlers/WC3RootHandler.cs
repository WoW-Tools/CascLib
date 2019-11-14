using System;
using System.Collections.Generic;
using System.IO;

namespace CASCLib
{
    public class Wc3RootHandler : RootHandlerBase
    {
        private Dictionary<ulong, RootEntry> RootData = new Dictionary<ulong, RootEntry>();

        public override int Count => RootData.Count;

        public Wc3RootHandler(BinaryReader stream, BackgroundWorkerEx worker)
        {
            worker?.ReportProgress(0, "Loading \"root\"...");

            using (StreamReader sr = new StreamReader(stream.BaseStream))
            {
                string line;

                while ((line = sr.ReadLine()) != null)
                {
                    string[] tokens = line.Split('|');

                    if (tokens.Length != 3 && tokens.Length != 4)
                        throw new Exception("tokens.Length != 3 && tokens.Length != 4");

                    string file;

                    LocaleFlags locale = LocaleFlags.All;

                    if (tokens[0].IndexOf(':') != -1)
                    {
                        string[] tokens2 = tokens[0].Split(':');

                        if (tokens2.Length == 2)
                            file = tokens2[0] + "\\" + tokens2[1];
                        else if (tokens2.Length == 3)
                            file = tokens2[0] + "\\" + tokens2[1] + "\\" + tokens2[2];
                        else if (tokens2.Length == 4)
                            file = tokens2[0] + "\\" + tokens2[1] + "\\" + tokens2[2] + "\\" + tokens2[3];
                        else
                            throw new Exception("tokens2.Length");
                    }
                    else
                    {
                        file = tokens[0];
                    }

                    if (!Enum.TryParse(tokens[2], out locale))
                    {
                        locale = LocaleFlags.All;
                    }

                    ulong fileHash = Hasher.ComputeHash(file);

                    RootData[fileHash] = new RootEntry()
                    {
                        LocaleFlags = locale,
                        ContentFlags = ContentFlags.None,
                        MD5 = tokens[1].ToByteArray().ToMD5()
                    };

                    CASCFile.Files[fileHash] = new CASCFile(fileHash, file);
                }
            }

            worker?.ReportProgress(100);
        }

        public override IEnumerable<KeyValuePair<ulong, RootEntry>> GetAllEntries()
        {
            return RootData;
        }

        public override IEnumerable<RootEntry> GetAllEntries(ulong hash)
        {
            if (RootData.TryGetValue(hash, out RootEntry rootEntry))
                yield return rootEntry;
        }

        // Returns only entries that match current locale and content flags
        public override IEnumerable<RootEntry> GetEntries(ulong hash)
        {
            return GetEntriesForSelectedLocale(hash);
        }

        public override void LoadListFile(string path, BackgroundWorkerEx worker = null)
        {

        }

        protected override CASCFolder CreateStorageTree()
        {
            var root = new CASCFolder("root");

            CountSelect = 0;

            foreach (var entry in RootData)
            {
                if ((entry.Value.LocaleFlags & Locale) == 0)
                    continue;

                CreateSubTree(root, entry.Key, CASCFile.Files[entry.Key].FullName);
                CountSelect++;
            }

            // Cleanup fake names for unknown files
            CountUnknown = 0;

            Logger.WriteLine("WC3RootHandler: {0} file names missing for locale {1}", CountUnknown, Locale);

            return root;
        }

        public override void Clear()
        {
            Root.Entries.Clear();
            CASCFile.Files.Clear();
        }

        public override void Dump()
        {

        }
    }
}
