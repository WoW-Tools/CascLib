using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace CASCLib
{
    public abstract class CASCHandlerBase
    {
        protected LocalIndexHandler LocalIndex;
        protected CDNIndexHandler CDNIndex;
        protected FileIndexHandler FileIndex;

        protected static readonly Jenkins96 Hasher = new Jenkins96();

        protected readonly Dictionary<int, MemoryMappedFile> DataStreams = new Dictionary<int, MemoryMappedFile>();

        private static readonly object DataStreamLock = new object();

        public CASCConfig Config { get; protected set; }

        public CASCHandlerBase(CASCConfig config, BackgroundWorkerEx worker)
        {
            Config = config;

            Logger.WriteLine("CASCHandlerBase: loading CDN indices...");

            using (var _ = new PerfCounter("CDNIndexHandler.Initialize()"))
            {
                CDNIndex = CDNIndexHandler.Initialize(config, worker);
            }

            if ((CASCConfig.LoadFlags & LoadFlags.FileIndex) != 0)
            {
                using (var _ = new PerfCounter("FileIndexHandler()"))
                {
                    FileIndex = new FileIndexHandler(config);
                }
            }

            Logger.WriteLine("CASCHandlerBase: loaded {0} CDN indexes", CDNIndex.Count);

            if (!config.OnlineMode)
            {
                CDNCache.Enabled = false;

                Logger.WriteLine("CASCHandlerBase: loading local indices...");

                using (var _ = new PerfCounter("LocalIndexHandler.Initialize()"))
                {
                    LocalIndex = LocalIndexHandler.Initialize(config, worker);
                }

                Logger.WriteLine("CASCHandlerBase: loaded {0} local indexes", LocalIndex.Count);
            }
        }

        public abstract bool FileExists(int fileDataId);
        public abstract bool FileExists(string file);
        public abstract bool FileExists(ulong hash);

        public abstract Stream OpenFile(int filedata);
        public abstract Stream OpenFile(string name);
        public abstract Stream OpenFile(ulong hash);

        public void SaveFileTo(string fullName, string extractPath) => SaveFileTo(Hasher.ComputeHash(fullName), extractPath, fullName);
        public void SaveFileTo(int fileDataId, string fullName, string extractPath) => SaveFileTo(FileDataHash.ComputeHash(fileDataId), extractPath, fullName);
        public abstract void SaveFileTo(ulong hash, string extractPath, string fullName);

        public Stream OpenFile(in MD5Hash eKey)
        {
            MD5Hash tempEKey = eKey;
            if (FileIndex?.GetFullEKey(eKey, out var fullEKey) == true)
                tempEKey = fullEKey;

            try
            {
                if (Config.OnlineMode)
                    return OpenFileOnline(tempEKey);
                else
                    return OpenFileLocal(tempEKey);
            }
            catch (BLTEDecoderException exc) when (exc.ErrorCode == 3)
            {
                if (CASCConfig.ThrowOnMissingDecryptionKey)
                    throw;
                return null;
            }
            catch// (Exception exc) when (!(exc is BLTEDecoderException))
            {
                return OpenFileOnline(tempEKey);
            }
        }

        protected Stream OpenFileOnline(in MD5Hash eKey)
        {
            IndexEntry idxInfo = CDNIndex.GetIndexInfo(eKey);
            return OpenFileOnlineInternal(idxInfo, eKey);
        }

        protected Stream OpenFileOnlineInternal(IndexEntry idxInfo, in MD5Hash eKey)
        {
            Stream s;

            if (idxInfo != null)
                s = CDNIndex.OpenDataFile(idxInfo);
            else
                s = CDNIndex.OpenDataFileDirect(eKey);

            BLTEStream blte;

            try
            {
                blte = new BLTEStream(s, eKey);
            }
            catch (BLTEDecoderException exc) when (exc.ErrorCode == 0)
            {
                CDNCache.Instance.InvalidateFile(idxInfo != null ? Config.Archives[idxInfo.Index] : eKey.ToHexString());
                return OpenFileOnlineInternal(idxInfo, eKey);
            }

            return blte;
        }

        private Stream OpenFileLocal(in MD5Hash eKey)
        {
            Stream stream = GetLocalDataStream(eKey);

            return new BLTEStream(stream, eKey);
        }

        protected Stream GetLocalDataStream(in MD5Hash eKey)
        {
            IndexEntry idxInfo = LocalIndex.GetIndexInfo(eKey);
            return GetLocalDataStreamInternal(idxInfo, eKey);
        }

        protected Stream GetLocalDataStreamInternal(IndexEntry idxInfo, in MD5Hash eKey)
        {
            if (idxInfo == null)
            {
                string message = $"Local index missing: {eKey.ToHexString()}";
                Logger.WriteLine(message);
                throw new Exception(message);
            }

            MemoryMappedFile dataFile = GetDataStream(idxInfo.Index);

            var accessor = dataFile.CreateViewStream(idxInfo.Offset, idxInfo.Size, MemoryMappedFileAccess.Read);
            using (BinaryReader reader = new BinaryReader(accessor, Encoding.ASCII, true))
            {
                byte[] md5 = reader.ReadBytes(16);
                Array.Reverse(md5);

                if (!eKey.EqualsTo9(md5))
                    throw new Exception("local data corrupted");

                int size = reader.ReadInt32();

                if (size != idxInfo.Size)
                    throw new Exception("local data corrupted");

                //byte[] unkData1 = reader.ReadBytes(2);
                //int unkData2 = reader.ReadInt32();
                //int unkData3 = reader.ReadInt32();
                accessor.Position += 10;

                //byte[] data = reader.ReadBytes(idxInfo.Size - 30);

                //return new MemoryStream(data);
                return new NestedStream(accessor, idxInfo.Size - 30);
            }
        }

        public void SaveFileTo(in MD5Hash eKey, string path, string name)
        {
            MD5Hash tempEKey = eKey;
            if (FileIndex?.GetFullEKey(eKey, out var fullEKey) == true)
                tempEKey = fullEKey;

            try
            {
                if (Config.OnlineMode)
                    ExtractFileOnline(tempEKey, path, name);
                else
                    ExtractFileLocal(tempEKey, path, name);
            }
            catch
            {
                if (Config.OnlineMode || CASCConfig.UseOnlineFallbackForMissingFiles)
                    ExtractFileOnline(tempEKey, path, name);
            }
        }

        protected void ExtractFileOnline(in MD5Hash eKey, string path, string name)
        {
            IndexEntry idxInfo = CDNIndex.GetIndexInfo(eKey);
            ExtractFileOnlineInternal(idxInfo, eKey, path, name);
        }

        protected void ExtractFileOnlineInternal(IndexEntry idxInfo, in MD5Hash eKey, string path, string name)
        {
            if (idxInfo != null)
            {
                using (Stream s = CDNIndex.OpenDataFile(idxInfo))
                using (BLTEStream blte = new BLTEStream(s, eKey))
                {
                    blte.ExtractToFile(path, name);
                }
            }
            else
            {
                using (Stream s = CDNIndex.OpenDataFileDirect(eKey))
                using (BLTEStream blte = new BLTEStream(s, eKey))
                {
                    blte.ExtractToFile(path, name);
                }
            }
        }

        private void ExtractFileLocal(in MD5Hash key, string path, string name)
        {
            Stream stream = GetLocalDataStream(key);

            using (BLTEStream blte = new BLTEStream(stream, key))
            {
                blte.ExtractToFile(path, name);
            }
        }

        protected static BinaryReader OpenInstallFile(EncodingHandler enc, CASCHandlerBase casc)
        {
            if (!enc.GetEntry(casc.Config.InstallCKey, out EncodingEntry encInfo))
                throw new FileNotFoundException("encoding info for install file missing!");

            //ExtractFile(encInfo.Key, ".", "install");

            return new BinaryReader(casc.OpenFile(encInfo.Keys[0]));
        }

        protected static BinaryReader OpenDownloadFile(EncodingHandler enc, CASCHandlerBase casc)
        {
            if (!enc.GetEntry(casc.Config.DownloadCKey, out EncodingEntry encInfo))
                throw new FileNotFoundException("encoding info for download file missing!");

            //ExtractFile(encInfo.Key, ".", "download");

            return new BinaryReader(casc.OpenFile(encInfo.Keys[0]));
        }

        protected static BinaryReader OpenRootFile(EncodingHandler enc, CASCHandlerBase casc)
        {
            if (!enc.GetEntry(casc.Config.RootCKey, out EncodingEntry encInfo))
                throw new FileNotFoundException("encoding info for root file missing!");

            //ExtractFile(encInfo.Key, ".", "root");

            return new BinaryReader(casc.OpenFile(encInfo.Keys[0]));
        }

        protected static BinaryReader OpenEncodingFile(CASCHandlerBase casc)
        {
            //ExtractFile(Config.EncodingKey, ".", "encoding");

            return new BinaryReader(casc.OpenFile(casc.Config.EncodingEKey));
        }

        private MemoryMappedFile GetDataStream(int index)
        {
            lock (DataStreamLock)
            {
                if (DataStreams.TryGetValue(index, out MemoryMappedFile stream))
                    return stream;

                string dataFolder = CASCGame.GetDataFolder(Config.GameType);

                string dataFile = Path.Combine(Config.BasePath, dataFolder, "data", string.Format("data.{0:D3}", index));

                FileStream fs = new FileStream(dataFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream = MemoryMappedFile.CreateFromFile(fs, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);

                DataStreams[index] = stream;

                return stream;
            }
        }
    }
}
