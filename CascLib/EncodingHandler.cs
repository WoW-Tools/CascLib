using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CASCLib
{
    public struct EncodingEntry
    {
        public List<MD5Hash> Keys;
        public long Size;
    }

    public class EncodingHandler
    {
        private Dictionary<MD5Hash, EncodingEntry> EncodingData = new Dictionary<MD5Hash, EncodingEntry>(MD5HashComparer.Instance);
        private Dictionary<MD5Hash, MD5Hash> EKeyToCKey = new Dictionary<MD5Hash, MD5Hash>(MD5HashComparer9.Instance);
        private Dictionary<MD5Hash, List<ulong>> EncryptionData = new Dictionary<MD5Hash, List<ulong>>(MD5HashComparer.Instance);
        private const int CHUNK_SIZE = 4096;

        public int Count => EncodingData.Count;

        public EncodingHandler(BinaryReader stream, BackgroundWorkerEx worker)
        {
            worker?.ReportProgress(0, "Loading \"encoding\"...");

            stream.Skip(2); // EN
            byte Version = stream.ReadByte(); // must be 1
            byte CKeyLength = stream.ReadByte();
            byte EKeyLength = stream.ReadByte();
            int CKeyPageSize = stream.ReadInt16BE() * 1024; // KB to bytes
            int EKeyPageSize = stream.ReadInt16BE() * 1024; // KB to bytes
            int CKeyPageCount = stream.ReadInt32BE();
            int EKeyPageCount = stream.ReadInt32BE();
            byte unk1 = stream.ReadByte(); // must be 0
            int ESpecBlockSize = stream.ReadInt32BE();

            //stream.Skip(ESpecBlockSize);
            string[] strings = Encoding.ASCII.GetString(stream.ReadBytes(ESpecBlockSize)).Split(new[] { '\0' }, StringSplitOptions.None);

            //for (int i = 0; i < strings.Length; i++)
            //{
            //    Logger.WriteLine($"ESpec {i:D6} {strings[i]}");
            //}

            stream.Skip(CKeyPageCount * 32);
            //ValueTuple<MD5Hash, MD5Hash>[] cKeyPageData = new ValueTuple<MD5Hash, MD5Hash>[CKeyPageCount];

            //for (int i = 0; i < CKeyPageCount; i++)
            //{
            //    MD5Hash firstHash = stream.Read<MD5Hash>();
            //    MD5Hash blockHash = stream.Read<MD5Hash>();
            //    cKeyPageData[i] = (firstHash, blockHash);
            //}

            long chunkStart = stream.BaseStream.Position;

            for (int i = 0; i < CKeyPageCount; i++)
            {
                byte keysCount;

                while ((keysCount = stream.ReadByte()) != 0)
                {
                    long fileSize = stream.ReadInt40BE();
                    MD5Hash cKey = stream.Read<MD5Hash>();

                    EncodingEntry entry = new EncodingEntry()
                    {
                        Size = fileSize,
                        Keys = new List<MD5Hash>(keysCount)
                    };

                    // how do we handle multiple keys?
                    for (int ki = 0; ki < keysCount; ++ki)
                    {
                        MD5Hash eKey = stream.Read<MD5Hash>();
                        entry.Keys.Add(eKey);
                        EKeyToCKey.Add(eKey, cKey);
                        //Logger.WriteLine($"Encoding {i:D7} {ki:D2} {cKey.ToHexString()} {eKey.ToHexString()} {fileSize}");
                    }

                    EncodingData.Add(cKey, entry);
                }

                // each chunk is 4096 bytes, and zero padding at the end
                long remaining = CHUNK_SIZE - ((stream.BaseStream.Position - chunkStart) % CHUNK_SIZE);

                if (remaining == 0xFFF)
                {
                    stream.BaseStream.Position -= 1;
                    i++;
                    continue;
                }

                if (remaining > 0)
                    stream.BaseStream.Position += remaining;

                worker?.ReportProgress((int)((i + 1) / (float)CKeyPageCount * 100));
            }

            stream.Skip(EKeyPageCount * 32);
            //ValueTuple<MD5Hash, MD5Hash>[] eKeyPageData = new ValueTuple<MD5Hash, MD5Hash>[EKeyPageCount];

            //for (int i = 0; i < EKeyPageCount; i++)
            //{
            //    MD5Hash firstKey = stream.Read<MD5Hash>();
            //    MD5Hash blockHash = stream.Read<MD5Hash>();
            //    eKeyPageData[i] = (firstKey, blockHash);
            //}

            long chunkStart2 = stream.BaseStream.Position;

            Regex regex = new Regex(@"(?<=e:\{)([0-9a-fA-F]{16})(?=,)", RegexOptions.Compiled);

            for (int i = 0; i < EKeyPageCount; i++)
            {
                while (true)
                {
                    // each chunk is 4096 bytes, and zero padding at the end
                    long remaining = CHUNK_SIZE - ((stream.BaseStream.Position - chunkStart2) % CHUNK_SIZE);

                    if (remaining < 25)
                    {
                        stream.BaseStream.Position += remaining;
                        break;
                    }

                    MD5Hash eKey = stream.Read<MD5Hash>();
                    int eSpecIndex = stream.ReadInt32BE();
                    long fileSize = stream.ReadInt40BE();

                    if (eSpecIndex == -1)
                    {
                        stream.BaseStream.Position += remaining;
                        break;
                    }

                    string eSpec = strings[eSpecIndex];

                    var matches = regex.Matches(eSpec);
                    if (matches.Count != 0)
                    {
                        var keys = matches.Cast<Match>().Select(m => BitConverter.ToUInt64(m.Value.FromHexString(), 0)).ToList();
                        EncryptionData.Add(eKey, keys);
                        //Logger.WriteLine($"Encoding {i:D7} {eKey.ToHexString()} {eSpecIndex} {fileSize} {eSpec} {string.Join(",", keys.Select(x => $"{x:X16}"))}");
                    }
                    else
                    {
                        //Logger.WriteLine($"Encoding {i:D7} {eKey.ToHexString()} {eSpecIndex} {fileSize} {eSpec}");
                    }
                }
            }
            // string block till the end of file
        }

        public IEnumerable<KeyValuePair<MD5Hash, EncodingEntry>> Entries
        {
            get
            {
                foreach (var entry in EncodingData)
                    yield return entry;
            }
        }

        public bool GetEntry(in MD5Hash cKey, out EncodingEntry enc) => EncodingData.TryGetValue(cKey, out enc);

        public bool TryGetBestEKey(in MD5Hash cKey, out MD5Hash eKey)
        {
            if (!GetEntry(cKey, out var entry))
            {
                eKey = default;
                return false;
            }

            if (entry.Keys.Count == 1)
            {
                eKey = entry.Keys[0];
                return true;
            }

            foreach (var key in entry.Keys)
            {
                if (EncryptionData.TryGetValue(key, out var keyNames) && keyNames.Count == 1)
                {
                    if (KeyService.HasKey(keyNames[0]))
                    {
                        eKey = key;
                        return true;
                    }
                }
            }
            eKey = entry.Keys[0];
            return true;
        }

        public bool GetCKeyFromEKey(in MD5Hash eKey, out MD5Hash cKey)
        {
            return EKeyToCKey.TryGetValue(eKey, out cKey);
        }

        public IReadOnlyList<ulong> GetEncryptionKeys(in MD5Hash eKey)
        {
            EncryptionData.TryGetValue(eKey, out var keyNames);
            return keyNames;
        }

        public void Clear()
        {
            EncodingData.Clear();
            EncodingData = null;
            EncryptionData.Clear();
            EncryptionData = null;
        }
    }
}
