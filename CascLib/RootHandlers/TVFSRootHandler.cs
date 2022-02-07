using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
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
        public ReadOnlySpan<byte> EstTable;
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
        private List<(MD5Hash CKey, MD5Hash EKey)> VfsRootList = new List<(MD5Hash, MD5Hash)>();
        private List<string> fileTree = new List<string>();

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
            var rootEKey = config.VfsRootEKey;

            using (var rootFile = casc.OpenFile(rootEKey))
            using (var reader = new BinaryReader(rootFile))
            {
                CaptureDirectoryHeader(out var dirHeader, reader);

                StringBuilder PathBuffer = new StringBuilder();

                ParseDirectoryData(casc, ref dirHeader, PathBuffer);
            }

            foreach (var enc in casc.Encoding.Entries)
            {
                Logger.WriteLine($"ENC: {enc.Key.ToHexString()} {enc.Value.Size}");
                foreach (var key in enc.Value.Keys)
                    Logger.WriteLine($"    {key.ToHexString()}");
            }

            worker?.ReportProgress(100);
        }

        private static bool PathBuffer_AppendNode(StringBuilder pathBuffer, ref PathTableEntry pathEntry)
        {
            if ((pathEntry.NodeFlags & TVFS_PTE_PATH_SEPARATOR_PRE) != 0)
                pathBuffer.Append('/');

            for (int i = 0; i < pathEntry.Name.Length; i++)
                pathBuffer.Append((char)pathEntry.Name[i]);

            if ((pathEntry.NodeFlags & TVFS_PTE_PATH_SEPARATOR_POST) != 0)
                pathBuffer.Append('/');

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

        private ReadOnlySpan<byte> CaptureVfsSpanEntry(ref TVFS_DIRECTORY_HEADER dirHeader, ReadOnlySpan<byte> vfsSpanEntry, ref VfsRootEntry vfsRootEntry)
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

            vfsSpanEntry = vfsSpanEntry.Slice(itemSize);

            return vfsSpanEntry;
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
            for (int i = 0; i < VfsRootList.Count; i++)
            {
                if (VfsRootList[i].EKey.EqualsTo9(eKey))
                {
                    fullEKey = VfsRootList[i].EKey;
                    return true;
                }
            }
            fullEKey = default;
            return false;
        }

        private bool IsVfsSubDirectory(CASCHandler casc, ref TVFS_DIRECTORY_HEADER dirHeader, out TVFS_DIRECTORY_HEADER subHeader, in MD5Hash eKey)
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

        private void ParsePathFileTable(CASCHandler casc, ref TVFS_DIRECTORY_HEADER dirHeader, StringBuilder pathBuffer, ReadOnlySpan<byte> pathTable)
        {
            TVFS_DIRECTORY_HEADER subHeader;

            int savePos = pathBuffer.Length;

            while (pathTable.Length > 0)
            {
                pathTable = CapturePathEntry(pathTable, out var pathEntry);

                if (pathTable == default)
                    throw new InvalidDataException();

                PathBuffer_AppendNode(pathBuffer, ref pathEntry);

                if ((pathEntry.NodeFlags & TVFS_PTE_NODE_VALUE) != 0)
                {
                    if ((pathEntry.NodeValue & TVFS_FOLDER_NODE) != 0)
                    {
                        int dirLen = (pathEntry.NodeValue & TVFS_FOLDER_SIZE_MASK) - sizeof(int);

                        Debug.Assert((pathEntry.NodeValue & TVFS_FOLDER_SIZE_MASK) >= sizeof(int));

                        ParsePathFileTable(casc, ref dirHeader, pathBuffer, pathTable.Slice(0, dirLen));

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

                            vfsSpanEntry = CaptureVfsSpanEntry(ref dirHeader, vfsSpanEntry, ref vfsRootEntry);

                            if (vfsSpanEntry == default)
                                throw new InvalidDataException();

                            Logger.WriteLine($"VFS: {vfsRootEntry.ContentOffset:X8} {vfsRootEntry.ContentLength:D8} {vfsRootEntry.CftOffset:X8} {vfsRootEntry.eKey.ToHexString()} 0");

                            if (IsVfsSubDirectory(casc, ref dirHeader, out subHeader, vfsRootEntry.eKey))
                            {
                                pathBuffer.Append(':');

                                ParseDirectoryData(casc, ref subHeader, pathBuffer);
                            }
                            else
                            {
                                string fileName = pathBuffer.ToString();
                                fileTree.Add(fileName);
                                ulong fileHash = Hasher.ComputeHash(fileName);

                                tvfsRootData.Add(fileHash, new List<VfsRootEntry> { vfsRootEntry });

                                if (casc.Encoding.GetCKeyFromEKey(vfsRootEntry.eKey, out MD5Hash cKey))
                                {
                                    tvfsData.Add(fileHash, new RootEntry { LocaleFlags = LocaleFlags.All, ContentFlags = ContentFlags.None, cKey = cKey });
                                }
                                else
                                {
                                    tvfsData.Add(fileHash, new RootEntry { LocaleFlags = LocaleFlags.All, ContentFlags = ContentFlags.None, cKey = default });
                                }

                                string file;

                                if (fileName.IndexOf(':') != -1)
                                {
                                    string[] tokens2 = fileName.Split(':');

                                    if (tokens2.Length == 2 || tokens2.Length == 3 || tokens2.Length == 4)
                                        file = Path.Combine(tokens2);
                                    else
                                        throw new InvalidDataException("tokens2.Length");
                                }
                                else
                                {
                                    file = fileName;
                                }

                                CASCFile.Files[fileHash] = new CASCFile(fileHash, file);
                            }
                        }
                        else
                        {
                            string fileName = pathBuffer.ToString();
                            fileTree.Add(fileName);
                            ulong fileHash = Hasher.ComputeHash(fileName);

                            List<VfsRootEntry> vfsRootEntries = new List<VfsRootEntry>();

                            for (int dwSpanIndex = 0; dwSpanIndex < dwSpanCount; dwSpanIndex++)
                            {
                                VfsRootEntry vfsRootEntry = new VfsRootEntry();

                                vfsSpanEntry = CaptureVfsSpanEntry(ref dirHeader, vfsSpanEntry, ref vfsRootEntry);

                                if (vfsSpanEntry == default)
                                    throw new InvalidDataException();

                                Logger.WriteLine($"VFS: {vfsRootEntry.ContentOffset:X8} {vfsRootEntry.ContentLength:D8} {vfsRootEntry.CftOffset:X8} {vfsRootEntry.eKey.ToHexString()} {dwSpanIndex}");

                                vfsRootEntries.Add(vfsRootEntry);

                                if (casc.Encoding.GetCKeyFromEKey(vfsRootEntry.eKey, out MD5Hash cKey))
                                {
                                    throw new Exception("got CKey for EKey!");
                                }
                            }

                            tvfsData.Add(fileHash, new RootEntry { LocaleFlags = LocaleFlags.All, ContentFlags = ContentFlags.None, cKey = default });
                            tvfsRootData.Add(fileHash, vfsRootEntries);

                            string file;

                            if (fileName.IndexOf(':') != -1)
                            {
                                string[] tokens2 = fileName.Split(':');

                                if (tokens2.Length == 2 || tokens2.Length == 3 || tokens2.Length == 4)
                                    file = Path.Combine(tokens2);
                                else
                                    throw new InvalidDataException("tokens2.Length");
                            }
                            else
                            {
                                file = fileName;
                            }

                            CASCFile.Files[fileHash] = new CASCFile(fileHash, file);
                        }
                    }

                    pathBuffer.Remove(savePos, pathBuffer.Length - savePos);
                }
            }
        }

        private void ParseDirectoryData(CASCHandler casc, ref TVFS_DIRECTORY_HEADER dirHeader, StringBuilder pathBuffer)
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

            ParsePathFileTable(casc, ref dirHeader, pathBuffer, pathTable);
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

        public List<VfsRootEntry> GetVfsRootEntries(ulong hash)
        {
            tvfsRootData.TryGetValue(hash, out var vfsRootEntry);
            return vfsRootEntry;
        }

        public override void LoadListFile(string path, BackgroundWorkerEx worker = null)
        {

        }

        protected override CASCFolder CreateStorageTree()
        {
            var root = new CASCFolder("root");

            CountSelect = 0;

            foreach (var entry in tvfsData)
            {
                if ((entry.Value.LocaleFlags & Locale) == 0)
                    continue;

                CreateSubTree(root, entry.Key, CASCFile.Files[entry.Key].FullName);
                CountSelect++;
            }

            return root;
        }

        public override void Clear()
        {
            tvfsData.Clear();
            tvfsRootData.Clear();
            Root.Entries.Clear();
            CASCFile.Files.Clear();
        }

        public override void Dump(EncodingHandler encodingHandler = null)
        {

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
