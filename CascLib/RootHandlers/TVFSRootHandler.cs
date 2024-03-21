using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace CASCLib
{
    ref struct TVFS_DIRECTORY_HEADER
    {
        public uint Magic;
        public byte FormatVersion;
        public byte HeaderSize;
        public byte EKeySize;
        public byte PatchKeySize;
        public int Flags;
        public int PathTableOffset;
        public int PathTableSize;
        public int VfsTableOffset;
        public int VfsTableSize;
        public int CftTableOffset;
        public int CftTableSize;
        public ushort MaxDepth;
        public int EstTableOffset;
        public int EstTableSize;
        public int CftOffsSize;
        public int EstOffsSize;
        public ReadOnlySpan<byte> PathTable;
        public ReadOnlySpan<byte> VfsTable;
        public ReadOnlySpan<byte> CftTable;
        //public ReadOnlySpan<byte> EstTable;
    }

    public ref struct PathBuffer
    {
        public Span<byte> Data;
        public int Position;

        public PathBuffer()
        {
            Data = new byte[512];
        }

        public void Append(byte value)
        {
            Data[Position++] = value;
        }

        public void Append(ReadOnlySpan<byte> values)
        {
            values.CopyTo(Data.Slice(Position));
            Position += values.Length;
        }

        public unsafe string GetString()
        {
#if NETSTANDARD2_0
            fixed (byte* ptr = Data)
                return Encoding.ASCII.GetString(ptr, Position);
#else
            return Encoding.ASCII.GetString(Data.Slice(0, Position));
#endif
        }
    }

    public struct VfsRootEntry
    {
        public MD5Hash eKey;
        public int ContentOffset; // not used
        public int ContentLength;
        public int CftOffset; // only used once and not need to be stored
    }

    public class TVFSRootHandler : RootHandlerBase
    {
        private Dictionary<ulong, RootEntry> tvfsData = new Dictionary<ulong, RootEntry>();
        private Dictionary<ulong, List<VfsRootEntry>> tvfsRootData = new Dictionary<ulong, List<VfsRootEntry>>();
        private List<(MD5Hash CKey, MD5Hash EKey)> VfsRootList;
        private HashSet<MD5Hash> VfsRootSet = new HashSet<MD5Hash>(MD5HashComparer9.Instance);
        protected readonly Dictionary<ulong, (string Orig, string New)> fileTree = new Dictionary<ulong, (string, string)>();

        private const uint TVFS_ROOT_MAGIC = 0x53465654;

        private const int TVFS_PTE_PATH_SEPARATOR_PRE = 0x0001;
        private const int TVFS_PTE_PATH_SEPARATOR_POST = 0x0002;
        private const int TVFS_PTE_NODE_VALUE = 0x0004;

        private const uint TVFS_FOLDER_NODE = 0x80000000;
        private const int TVFS_FOLDER_SIZE_MASK = 0x7FFFFFFF;

        ref struct PathTableEntry
        {
            public ReadOnlySpan<byte> Name;
            public int NodeFlags;
            public int NodeValue;
        }

        public TVFSRootHandler(BackgroundWorkerEx worker, CASCHandler casc)
        {
            worker?.ReportProgress(0, "Loading \"root\"...");

            var config = casc.Config;
            VfsRootList = config.VfsRootList;

            foreach (var vfsRoot in VfsRootList)
                VfsRootSet.Add(vfsRoot.EKey);

            var rootEKey = config.VfsRootEKey;

            using (var rootFile = casc.OpenFile(rootEKey))
            using (var reader = new BinaryReader(rootFile))
            {
                CaptureDirectoryHeader(out var dirHeader, reader);

                PathBuffer PathBuffer = new PathBuffer();

                ParseDirectoryData(casc, ref dirHeader, ref PathBuffer);
            }

            //foreach (var enc in casc.Encoding.Entries)
            //{
            //    Logger.WriteLine($"ENC: {enc.Key.ToHexString()} {enc.Value.Size}");
            //    foreach (var key in enc.Value.Keys)
            //        Logger.WriteLine($"    {key.ToHexString()}");
            //}

            worker?.ReportProgress(100);
        }

        private static bool PathBuffer_AppendNode(ref PathBuffer pathBuffer, in PathTableEntry pathEntry)
        {
            if ((pathEntry.NodeFlags & TVFS_PTE_PATH_SEPARATOR_PRE) != 0)
                pathBuffer.Append((byte)'/');

            pathBuffer.Append(pathEntry.Name);

            if ((pathEntry.NodeFlags & TVFS_PTE_PATH_SEPARATOR_POST) != 0)
                pathBuffer.Append((byte)'/');

            return true;
        }

        private bool CaptureDirectoryHeader(out TVFS_DIRECTORY_HEADER dirHeader, BinaryReader reader)
        {
            dirHeader = new TVFS_DIRECTORY_HEADER();

            dirHeader.Magic = reader.ReadUInt32();
            if (dirHeader.Magic != TVFS_ROOT_MAGIC)
                throw new InvalidDataException();

            dirHeader.FormatVersion = reader.ReadByte();
            if (dirHeader.FormatVersion != 1)
                throw new InvalidDataException();

            dirHeader.HeaderSize = reader.ReadByte();
            if (dirHeader.HeaderSize < 8)
                throw new InvalidDataException();

            dirHeader.EKeySize = reader.ReadByte();
            if (dirHeader.EKeySize != 9)
                throw new InvalidDataException();

            dirHeader.PatchKeySize = reader.ReadByte();
            if (dirHeader.PatchKeySize != 9)
                throw new InvalidDataException();

            dirHeader.Flags = reader.ReadInt32BE();
            dirHeader.PathTableOffset = reader.ReadInt32BE();
            dirHeader.PathTableSize = reader.ReadInt32BE();
            dirHeader.VfsTableOffset = reader.ReadInt32BE();
            dirHeader.VfsTableSize = reader.ReadInt32BE();
            dirHeader.CftTableOffset = reader.ReadInt32BE();
            dirHeader.CftTableSize = reader.ReadInt32BE();
            dirHeader.MaxDepth = reader.ReadUInt16BE();
            dirHeader.EstTableOffset = reader.ReadInt32BE();
            dirHeader.EstTableSize = reader.ReadInt32BE();

            static int GetOffsetFieldSize(int dwTableSize)
            {
                return dwTableSize switch
                {
                    > 0xffffff => 4,
                    > 0xffff => 3,
                    > 0xff => 2,
                    _ => 1
                };
            }

            dirHeader.CftOffsSize = GetOffsetFieldSize(dirHeader.CftTableSize);
            dirHeader.EstOffsSize = GetOffsetFieldSize(dirHeader.EstTableSize);

            reader.BaseStream.Position = dirHeader.PathTableOffset;
            dirHeader.PathTable = reader.ReadBytes(dirHeader.PathTableSize);

            reader.BaseStream.Position = dirHeader.VfsTableOffset;
            dirHeader.VfsTable = reader.ReadBytes(dirHeader.VfsTableSize);

            reader.BaseStream.Position = dirHeader.CftTableOffset;
            dirHeader.CftTable = reader.ReadBytes(dirHeader.CftTableSize);

            // reading this causes crash on some CoD games...
            //reader.BaseStream.Position = dirHeader.EstTableOffset;
            //dirHeader.EstTable = reader.ReadBytes(dirHeader.EstTableSize);

            return true;
        }

        private ReadOnlySpan<byte> CaptureVfsSpanCount(ref TVFS_DIRECTORY_HEADER dirHeader, int dwVfsOffset, ref byte SpanCount)
        {
            ReadOnlySpan<byte> VfsFileTable = dirHeader.VfsTable;
            ReadOnlySpan<byte> pbVfsFileEntry = VfsFileTable.Slice(dwVfsOffset);

            if (pbVfsFileEntry.Length == 0)
                return default;

            SpanCount = pbVfsFileEntry[0];
            pbVfsFileEntry = pbVfsFileEntry.Slice(1);

            return (1 <= SpanCount && SpanCount <= 224) ? pbVfsFileEntry : default;
        }

        private int CaptureVfsSpanEntry(ref TVFS_DIRECTORY_HEADER dirHeader, scoped ReadOnlySpan<byte> vfsSpanEntry, ref VfsRootEntry vfsRootEntry)
        {
            ReadOnlySpan<byte> cftFileTable = dirHeader.CftTable;
            int itemSize = sizeof(int) + sizeof(int) + dirHeader.CftOffsSize;

            int contentOffset = vfsSpanEntry.ReadInt32BE();
            int contentLength = vfsSpanEntry.Slice(4).ReadInt32BE();
            int cftOffset = vfsSpanEntry.Slice(4 + 4).ReadInt32(dirHeader.CftOffsSize);

            vfsRootEntry.ContentOffset = contentOffset;
            vfsRootEntry.ContentLength = contentLength;
            vfsRootEntry.CftOffset = cftOffset;

            ReadOnlySpan<byte> cftFileEntry = cftFileTable.Slice(cftOffset);
            ReadOnlySpan<byte> eKeySlice = cftFileEntry.Slice(0, dirHeader.EKeySize);
            Span<byte> eKey = stackalloc byte[16];
            eKeySlice.CopyTo(eKey);

            vfsRootEntry.eKey = Unsafe.As<byte, MD5Hash>(ref eKey[0]);

            return itemSize;
        }

        private ReadOnlySpan<byte> CapturePathEntry(ReadOnlySpan<byte> pathTable, out PathTableEntry pathEntry)
        {
            pathEntry = new PathTableEntry();

            if (pathTable.Length > 0 && pathTable[0] == 0)
            {
                pathEntry.NodeFlags |= TVFS_PTE_PATH_SEPARATOR_PRE;
                pathTable = pathTable.Slice(1);
            }

            if (pathTable.Length > 0 && pathTable[0] != 0xFF)
            {
                byte len = pathTable[0];
                pathTable = pathTable.Slice(1);

                pathEntry.Name = pathTable.Slice(0, len);
                pathTable = pathTable.Slice(len);
            }

            if (pathTable.Length > 0 && pathTable[0] == 0)
            {
                pathEntry.NodeFlags |= TVFS_PTE_PATH_SEPARATOR_POST;
                pathTable = pathTable.Slice(1);
            }

            if (pathTable.Length > 0)
            {
                if (pathTable[0] == 0xFF)
                {
                    if (1 + sizeof(int) > pathTable.Length)
                        return default;

                    pathEntry.NodeValue = pathTable.Slice(1).ReadInt32BE();
                    pathEntry.NodeFlags |= TVFS_PTE_NODE_VALUE;
                    pathTable = pathTable.Slice(1 + sizeof(int));
                }
                else
                {
                    pathEntry.NodeFlags |= TVFS_PTE_PATH_SEPARATOR_POST;
                    Debug.Assert(pathTable[0] != 0);
                }
            }

            return pathTable;
        }

        private bool IsVfsFileEKey(in MD5Hash eKey, out MD5Hash fullEKey)
        {
#if NET6_0_OR_GREATER
            return VfsRootSet.TryGetValue(eKey, out fullEKey);
#else
            if (VfsRootSet.Contains(eKey))
            {
                foreach (MD5Hash hash in VfsRootSet)
                {
                    if (hash.EqualsTo9(eKey))
                    {
                        fullEKey = hash;
                        return true;
                    }
                }
            }
            fullEKey = default;
            return false;
#endif
        }

        private bool IsVfsSubDirectory(CASCHandler casc, out TVFS_DIRECTORY_HEADER subHeader, scoped in MD5Hash eKey)
        {
            if (IsVfsFileEKey(eKey, out var fullEKey))
            {
                using (var vfsRootFile = casc.OpenFile(fullEKey))
                using (var reader = new BinaryReader(vfsRootFile))
                {
                    CaptureDirectoryHeader(out subHeader, reader);
                }
                return true;
            }

            subHeader = default;
            return false;
        }

        private void ParsePathFileTable(CASCHandler casc, ref TVFS_DIRECTORY_HEADER dirHeader, ref PathBuffer pathBuffer, ReadOnlySpan<byte> pathTable)
        {
            int savePos = pathBuffer.Position;

            while (pathTable.Length > 0)
            {
                pathTable = CapturePathEntry(pathTable, out var pathEntry);

                if (pathTable == default)
                    throw new InvalidDataException();

                PathBuffer_AppendNode(ref pathBuffer, pathEntry);

                if ((pathEntry.NodeFlags & TVFS_PTE_NODE_VALUE) != 0)
                {
                    if ((pathEntry.NodeValue & TVFS_FOLDER_NODE) != 0)
                    {
                        int dirLen = (pathEntry.NodeValue & TVFS_FOLDER_SIZE_MASK) - sizeof(int);

                        Debug.Assert((pathEntry.NodeValue & TVFS_FOLDER_SIZE_MASK) >= sizeof(int));

                        ParsePathFileTable(casc, ref dirHeader, ref pathBuffer, pathTable.Slice(0, dirLen));

                        pathTable = pathTable.Slice(dirLen);
                    }
                    else
                    {
                        byte dwSpanCount = 0;

                        ReadOnlySpan<byte> vfsSpanEntry = CaptureVfsSpanCount(ref dirHeader, pathEntry.NodeValue, ref dwSpanCount);
                        if (vfsSpanEntry == default)
                            throw new InvalidDataException();

                        if (dwSpanCount == 1)
                        {
                            VfsRootEntry vfsRootEntry = new VfsRootEntry();

                            int itemSize = CaptureVfsSpanEntry(ref dirHeader, vfsSpanEntry, ref vfsRootEntry);
                            vfsSpanEntry = vfsSpanEntry.Slice(itemSize);

                            if (vfsSpanEntry == default)
                                throw new InvalidDataException();

                            //Logger.WriteLine($"VFS: {vfsRootEntry.ContentOffset:X8} {vfsRootEntry.ContentLength:D9} {vfsRootEntry.CftOffset:X8} {vfsRootEntry.eKey.ToHexString()} 0");

                            if (IsVfsSubDirectory(casc, out var subHeader, vfsRootEntry.eKey))
                            {
                                pathBuffer.Append((byte)'/');

                                ParseDirectoryData(casc, ref subHeader, ref pathBuffer);
                            }
                            else
                            {
                                //string fileName = pathBuffer.ToString();
                                string fileName = pathBuffer.GetString();
                                string fileNameNew = MakeFileName(fileName);
                                ulong fileHash = Hasher.ComputeHash(fileNameNew);
                                fileTree.Add(fileHash, (fileName, fileNameNew));

                                tvfsRootData.Add(fileHash, new List<VfsRootEntry> { vfsRootEntry });

                                //if (casc.Encoding.GetCKeyFromEKey(vfsRootEntry.eKey, out MD5Hash cKey))
                                //{
                                //    tvfsData.Add(fileHash, new RootEntry { LocaleFlags = LocaleFlags.All, ContentFlags = ContentFlags.None, cKey = cKey });
                                //}
                                //else
                                //{
                                //    tvfsData.Add(fileHash, new RootEntry { LocaleFlags = LocaleFlags.All, ContentFlags = ContentFlags.None, cKey = default });
                                //}
                            }
                        }
                        else
                        {
                            //string fileName = pathBuffer.ToString();
                            string fileName = pathBuffer.GetString();
                            string fileNameNew = MakeFileName(fileName);
                            ulong fileHash = Hasher.ComputeHash(fileNameNew);
                            fileTree.Add(fileHash, (fileName, fileNameNew));

                            List<VfsRootEntry> vfsRootEntries = new List<VfsRootEntry>(dwSpanCount);

                            for (int dwSpanIndex = 0; dwSpanIndex < dwSpanCount; dwSpanIndex++)
                            {
                                VfsRootEntry vfsRootEntry = new VfsRootEntry();

                                int itemSize = CaptureVfsSpanEntry(ref dirHeader, vfsSpanEntry, ref vfsRootEntry);
                                vfsSpanEntry = vfsSpanEntry.Slice(itemSize);

                                if (vfsSpanEntry == default)
                                    throw new InvalidDataException();

                                //Logger.WriteLine($"VFS: {vfsRootEntry.ContentOffset:X8} {vfsRootEntry.ContentLength:D9} {vfsRootEntry.CftOffset:X8} {vfsRootEntry.eKey.ToHexString()} {dwSpanIndex}");

                                vfsRootEntries.Add(vfsRootEntry);

                                //if (casc.Encoding.GetCKeyFromEKey(vfsRootEntry.eKey, out MD5Hash cKey))
                                //{
                                //    throw new Exception("got CKey for EKey!");
                                //}
                            }

                            //tvfsData.Add(fileHash, new RootEntry { LocaleFlags = LocaleFlags.All, ContentFlags = ContentFlags.None, cKey = default });
                            tvfsRootData.Add(fileHash, vfsRootEntries);
                        }
                    }

                    //pathBuffer.Remove(savePos, pathBuffer.Length - savePos);
                    pathBuffer.Position = savePos;
                }
            }
        }

        private static string MakeFileName(string data)
        {
            return data;
            //string file = data;

            //if (data.IndexOf(':') != -1)
            //{
            //    StringBuilder sb = new StringBuilder(data);

            //    for (int i = 0; i < sb.Length; i++)
            //    {
            //        if (sb[i] == ':')
            //            sb[i] = '\\';
            //    }

            //    file = sb.ToString();

            //    //string[] tokens2 = data.Split(':');

            //    //if (tokens2.Length == 2 || tokens2.Length == 3 || tokens2.Length == 4)
            //    //    file = Path.Combine(tokens2);
            //    //else
            //    //    throw new InvalidDataException("tokens2.Length");
            //}
            //else
            //{
            //    file = data;
            //}
            //return file;
        }

        private void ParseDirectoryData(CASCHandler casc, ref TVFS_DIRECTORY_HEADER dirHeader, ref PathBuffer pathBuffer)
        {
            ReadOnlySpan<byte> pathTable = dirHeader.PathTable;

            if (1 + sizeof(int) < pathTable.Length)
            {
                if (pathTable[0] == 0xFF)
                {
                    int dwNodeValue = pathTable.Slice(1).ReadInt32BE();

                    if ((dwNodeValue & TVFS_FOLDER_NODE) == 0)
                        throw new InvalidDataException();

                    int pathFileTableSize = dwNodeValue & TVFS_FOLDER_SIZE_MASK;

                    if (pathFileTableSize > pathTable.Length)
                        throw new InvalidDataException();

                    pathTable = pathTable.Slice(1 + sizeof(int));
                }
            }

            ParsePathFileTable(casc, ref dirHeader, ref pathBuffer, pathTable);
        }

        public override IEnumerable<KeyValuePair<ulong, RootEntry>> GetAllEntries()
        {
            return tvfsData;
        }

        public override IEnumerable<RootEntry> GetAllEntries(ulong hash)
        {
            if (tvfsData.TryGetValue(hash, out RootEntry rootEntry))
                yield return rootEntry;
        }

        public override IEnumerable<RootEntry> GetEntries(ulong hash)
        {
            return GetEntriesForSelectedLocale(hash);
        }

        public virtual List<VfsRootEntry> GetVfsRootEntries(ulong hash)
        {
            tvfsRootData.TryGetValue(hash, out var vfsRootEntry);
            return vfsRootEntry;
        }

        public void SetHashDuplicate(ulong oldHash, ulong newHash)
        {
            if (tvfsRootData.TryGetValue(oldHash, out var vfsRootEntry))
            {
                tvfsRootData[newHash] = vfsRootEntry;
                fileTree[newHash] = fileTree[oldHash];
            }
            if (tvfsData.TryGetValue(oldHash, out var rootEntry))
            {
                tvfsData[newHash] = rootEntry;
            }
        }

        public override void LoadListFile(string path, BackgroundWorkerEx worker = null)
        {

        }

        protected override CASCFolder CreateStorageTree()
        {
            var root = new CASCFolder("root");

            CountSelect = 0;

            foreach (var entry in tvfsRootData)
            {
                CreateSubTree(root, entry.Key, fileTree[entry.Key].New);
                CountSelect++;
            }

            return root;
        }

        public override void Clear()
        {
            tvfsData.Clear();
            tvfsRootData.Clear();
            Root.Files.Clear();
            Root.Folders.Clear();
            CASCFile.Files.Clear();
        }

        public override void Dump(EncodingHandler encodingHandler = null)
        {
#if DEBUG
            Logger.WriteLine("TVFSRootHandler Dump:");

            Dictionary<ulong, int> keyCounts = new Dictionary<ulong, int>();
            keyCounts[0] = 0;

            foreach (var fd in tvfsRootData)
            {
                if (!fileTree.TryGetValue(fd.Key, out var name))
                {
                    Logger.WriteLine($"Can't get name for Hash: {fd.Key:X16}");
                    continue;
                }

                Logger.WriteLine($"Hash: {fd.Key:X16} Name: {name.Orig}");

                foreach (var entry in fd.Value)
                {
                    Logger.WriteLine($"\teKey: {entry.eKey.ToHexString()} ContentLength {entry.ContentLength} ContentOffset {entry.ContentOffset} CftOffset {entry.CftOffset}");

                    if (encodingHandler != null)
                    {
                        if (encodingHandler.GetCKeyFromEKey(entry.eKey, out MD5Hash cKey))
                        {
                            if (encodingHandler.GetEntry(cKey, out var encodingEntry))
                            {
                                foreach (var eKey in encodingEntry.Keys)
                                {
                                    var keys = encodingHandler.GetEncryptionKeys(eKey);
                                    if (keys != null)
                                    {
                                        Logger.WriteLine($"\teKey: {eKey.ToHexString()} cKey: {cKey.ToHexString()} TactKeys: {string.Join(",", keys.Select(k => $"{k:X16}"))} Size: {encodingEntry.Size}");

                                        foreach (var key in keys)
                                        {
                                            if (!keyCounts.ContainsKey(key))
                                                keyCounts[key] = 0;

                                            keyCounts[key]++;
                                        }
                                    }
                                    else
                                    {
                                        Logger.WriteLine($"\teKey: {eKey.ToHexString()} cKey: {cKey.ToHexString()} TactKeys: NA Size: {encodingEntry.Size}");
                                        keyCounts[0]++;
                                    }
                                }
                            }
                            else
                            {
                                Logger.WriteLine($"\tEncodingEntry: NA");
                            }
                        }
                        else
                        {
                            Logger.WriteLine($"\tcKey: NA");
                        }
                    }
                }
            }

            foreach (var kv in keyCounts)
            {
                Logger.WriteLine($"Key: {kv.Key:X16} Count: {kv.Value}");
            }
#endif
        }
    }

    static class SpanExtensions
    {
        public static int ReadInt32(this ReadOnlySpan<byte> source, int numBytes)
        {
            int Value = 0;

            if (numBytes > 0)
                Value = (Value << 0x08) | source[0];
            if (numBytes > 1)
                Value = (Value << 0x08) | source[1];
            if (numBytes > 2)
                Value = (Value << 0x08) | source[2];
            if (numBytes > 3)
                Value = (Value << 0x08) | source[3];

            return Value;
        }

        public static int ReadInt32BE(this ReadOnlySpan<byte> source)
        {
            return BinaryPrimitives.ReadInt32BigEndian(source);
        }
    }
}
