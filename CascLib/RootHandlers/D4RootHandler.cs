using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CASCLib
{
    public class D4RootHandler : TVFSRootHandler
    {
        private CoreTOCParserD4 tocParser;
        private CASCHandler cascHandler;
        private readonly Dictionary<int, int> sharedPayloads = new Dictionary<int, int>();
        private readonly Dictionary<int, string> encryptedNames = new Dictionary<int, string>();
        private readonly Dictionary<int, (int group, ulong keyId)> encryptedSNOs = new Dictionary<int, (int group, ulong keyId)>();

        public D4RootHandler(BackgroundWorkerEx worker, CASCHandler casc) : base(worker, casc)
        {
            cascHandler = casc;

            worker?.ReportProgress(0, "Loading \"root\"...");

            // Parse CoreTOC.dat
            var coreTocEntry = GetVfsRootEntries(Hasher.ComputeHash("Base\\CoreTOC.dat")).FirstOrDefault();

            using (var file = casc.OpenFile(coreTocEntry.eKey))
                tocParser = new CoreTOCParserD4(file);

            // Parse CoreTOCSharedPayloadsMapping.dat
            var vfsCoreTocSharedPayloads = GetVfsRootEntries(Hasher.ComputeHash("Base\\CoreTOCSharedPayloadsMapping.dat"));
            // check if it exist (older versions missing it)
            if (vfsCoreTocSharedPayloads != null)
            {
                var coreTocSharedPayloads = vfsCoreTocSharedPayloads.FirstOrDefault();

                using (var file = casc.OpenFile(coreTocSharedPayloads.eKey))
                using (var br = new BinaryReader(file))
                {
                    int unk1 = br.ReadInt32();
                    int count = br.ReadInt32();

                    //using StreamWriter sw = new StreamWriter("CoreTOCSharedPayloadsMapping.txt");

                    for (int i = 0; i < count; i++)
                    {
                        int snoID = br.ReadInt32();
                        int sharedSnoID = br.ReadInt32();

                        sharedPayloads.Add(snoID, sharedSnoID);

                        //var sno1 = tocParser.GetSNO(snoID);
                        //var sno2 = tocParser.GetSNO(sharedSnoID);

                        //sw.WriteLine($"{snoID} {sno1.GroupId} {sno1.Name} -> {sharedSnoID} {sno2.GroupId} {sno2.Name}");
                    }
                }
            }

            // Parse EncryptedSNOs.dat and collect encryption keys
            var vfsEncryptedSNOs = GetVfsRootEntries(Hasher.ComputeHash("Base\\EncryptedSNOs.dat"));
            if (vfsEncryptedSNOs != null)
            {
                var encryptedSNOsEntry = vfsEncryptedSNOs.FirstOrDefault();

                using (var file = casc.OpenFile(encryptedSNOsEntry.eKey))
                {
                    using (var br = new BinaryReader(file))
                    {
                        int unkHash = br.ReadInt32();
                        int count = br.ReadInt32();

                        for (int i = 0; i < count; i++)
                        {
                            int snoGroup = br.ReadInt32();
                            int snoID = br.ReadInt32();
                            ulong keyID = br.ReadUInt64();

                            encryptedSNOs.Add(snoID, (snoGroup, keyID));
                        }
                    }
                }
            }

            // Parse EncryptedNameDict-0xXXXXXXXXXXXXXXXX.dat files
#if NETSTANDARD2_0
            var encKeys = new HashSet<ulong>();
            foreach (var encSNO in encryptedSNOs)
                encKeys.Add(encSNO.Value.keyId);
#else
            var encKeys = encryptedSNOs.Select(e => e.Value.keyId).ToHashSet();
#endif
            foreach (var encKey in encKeys)
            {
                if (!KeyService.HasKey(BinaryPrimitives.ReverseEndianness(encKey)))
                    continue;

                var encDictPath = $"Base\\EncryptedNameDict-0x{encKey:X16}.dat";
                var encDictEntries = GetVfsRootEntries(Hasher.ComputeHash(encDictPath));

                if (encDictEntries == null)
                    continue;

                var encDictEntry = encDictEntries.FirstOrDefault();

                try
                {
                    using (var file = casc.OpenFile(encDictEntry.eKey))
                    {
                        using (var br = new BinaryReader(file))
                        {
                            int unkHash = br.ReadInt32();
                            int count = br.ReadInt32();

                            var snoIDs = new List<int>(count);

                            for (int i = 0; i < count; i++)
                            {
                                int snoGroup = br.ReadInt32();
                                int snoID = br.ReadInt32();

                                snoIDs.Add(snoID);
                            }

                            for (int i = 0; i < count; i++)
                            {
                                var name = br.ReadCString();
                                encryptedNames[snoIDs[i]] = name;
                            }
                        }
                    }
                }
                catch (BLTEDecoderException)
                {
                    // Unknown key name
                }
            }

            // TODO: handle base/CoreTOCReplacedSnosMapping.dat?

            worker?.ReportProgress(100);
        }

        public override void Clear()
        {
            tocParser = null;
            cascHandler = null;
            base.Clear();
        }

        public override void LoadListFile(string path, BackgroundWorkerEx worker = null)
        {
            worker?.ReportProgress(0, "Loading \"listfile\"...");

            Logger.WriteLine("D4RootHandler: loading file names...");

            Logger.WriteLine("D4RootHandler: loaded {0} file names", 0);
        }

        protected override CASCFolder CreateStorageTree()
        {
            var root = base.CreateStorageTree();

            CountSelect = 0;

            string[] locales = Enum.GetNames(typeof(LocaleFlags));
            string[] folders = new string[] { "Base", "Speech", "Text" };
            string[] subfolders = new string[] { "child", "meta", "payload", "paylow", "paymed" };
            HashSet<string> payloadFolders = new HashSet<string> { "payload", "paylow", "paymed" };

            List<string> filesToRemove = new List<string>();

            void CreateNewFileEntry(CASCFile file, string newName)
            {
                ulong newHash = Hasher.ComputeHash(newName);
                SetHashDuplicate(file.Hash, newHash);

                CreateSubTree(root, newHash, newName);
                CountSelect++;
            }

            void CreateSNOEntry(int snoid, CASCFile file, string folder, string subfolder, int subid = -1)
            {
                SNOInfoD4 sno = tocParser.GetSNO(snoid);
                if (sno != null)
                {
                    if (encryptedSNOs.ContainsKey(snoid))
                    {
                        if (encryptedNames.TryGetValue(snoid, out string encName))    // Override with encrypted name
                            sno.Name = encName;
                        else
                            sno.Name = $"_encrypted_{snoid}";
                    }

                    string newName;

                    if (subid == -1)
                        newName = Path.Combine(folder, subfolder, sno.GroupId.ToString(), $"{sno.Name}{sno.Ext}");
                    else
                        newName = Path.Combine(folder, subfolder, sno.GroupId.ToString(), $"{sno.Name}-{subid}{sno.Ext}");

                    CreateNewFileEntry(file, newName);
                }
                else
                {
                    Logger.WriteLine($"SNO {snoid} (file {file.FullName}) not found!");
                }
            }

            void CleanupFolder(CASCFolder folder)
            {
                foreach (var file in filesToRemove)
                    folder.Files.Remove(file);
                filesToRemove.Clear();
            }

            foreach (var folder in folders)
            {
                ProcessFolder(folder);
            }

            foreach (var locale in locales)
            {
                foreach (var folder in folders)
                {
                    ProcessFolder(locale + "_" + folder);
                }
            }

            void ProcessFolder(string folder)
            {
                if (root.Folders.TryGetValue(folder, out var folder1))
                {
                    foreach (var subfolder in subfolders)
                    {
                        if (folder1.Folders.TryGetValue(subfolder, out var subfolder1))
                        {
                            foreach (var child in subfolder1.Files)
                            {
                                if (child.Key.Contains('-'))
                                {
                                    string[] tokens = child.Key.Split('-');

                                    if (tokens.Length != 2)
                                        continue;

                                    int snoid = int.Parse(tokens[0]);
                                    int subId = int.Parse(tokens[1]);
                                    CreateSNOEntry(snoid, child.Value, folder, subfolder, subId);
                                }
                                else
                                {
                                    int snoid = int.Parse(child.Key);
                                    CreateSNOEntry(snoid, child.Value, folder, subfolder);
                                }
                                filesToRemove.Add(child.Key);
                            }

                            CleanupFolder(subfolder1);
                        }
                    }
                }
            }

            foreach (var sharedPayload in sharedPayloads)
            {
                //var sno1 = tocParser.GetSNO(sharedPayload.Key);
                var sno2 = tocParser.GetSNO(sharedPayload.Value);

                // shared payloads seems to be only used for textures which are in "Base" folder
                if (root.Folders.TryGetValue("Base", out var baseFolder))
                {
                    foreach (var payloadFolder in payloadFolders)
                    {
                        if (baseFolder.Folders.TryGetValue(payloadFolder, out var subfolder1))
                        {
                            if (subfolder1.Folders.TryGetValue($"{sno2.GroupId}", out var subfolder2))
                            {
                                if (subfolder2.Files.TryGetValue($"{sno2.Name}{sno2.Ext}", out var file))
                                {
                                    CreateSNOEntry(sharedPayload.Key, file, "Base", subfolder1.Name);
                                }
                            }
                        }
                    }
                }
            }

            // move "package" files
            foreach (var folder in folders)
            {
                foreach (var locale in locales)
                {
                    var fileKey = $"{locale}.{folder}";
                    if (root.Files.TryGetValue(fileKey, out var file))
                    {
                        string newName = $"Packages\\{fileKey}";
                        CreateNewFileEntry(file, newName);
                        filesToRemove.Add(fileKey);
                    }
                }
            }

            CreateNewFileEntry(root.Files["Base"], $"Packages\\enUS.Base");
            filesToRemove.Add("Base");

            CleanupFolder(root);

            Logger.WriteLine($"D4RootHandler: {CountUnknown} file names missing for locale {Locale}");

            return root;
        }

        public Stream OpenFile(string prefix, D4FolderType folderType, int snoId, int subId = -1)
        {
            SNOInfoD4 sno = tocParser.GetSNO(snoId);

            if (sno == null)
                return null;

            string fileName;

            if (subId == -1)
                fileName = Path.Combine(prefix, folderType.ToString(), sno.GroupId.ToString(), $"{sno.Name}{sno.Ext}");
            else
                fileName = Path.Combine(prefix, folderType.ToString(), sno.GroupId.ToString(), $"{sno.Name}-{subId}{sno.Ext}");

            ulong fileHash = Hasher.ComputeHash(fileName);

            return cascHandler.OpenFile(fileHash);
        }
    }

    public enum D4FolderType
    {
        Child,
        Meta,
        Payload,
        PayLow,
        PayMed
    }

    public class SNOInfoD4
    {
        public SNOGroupD4 GroupId;
        public string Name;
        public string Ext;
    }

    public enum SNOGroupD4
    {
        Unknown = -3,
        Code = -2,
        None = -1,
        Actor = 1,
        NPCComponentSet = 2,
        AIBehavior = 3,
        AIState = 4,
        AmbientSound = 5,
        Anim = 6,
        Anim2D = 7,
        AnimSet = 8,
        Appearance = 9,
        Hero = 10,
        Cloth = 11,
        Conversation = 12,
        ConversationList = 13,
        EffectGroup = 14,
        Encounter = 15,
        Explosion = 17,
        FlagSet = 18,
        Font = 19,
        GameBalance = 20,
        Global = 21,
        LevelArea = 22,
        Light = 23,
        MarkerSet = 24,
        Observer = 26,
        Particle = 27,
        Physics = 28,
        Power = 29,
        Quest = 31,
        Rope = 32,
        Scene = 33,
        Script = 35,
        ShaderMap = 36,
        Shader = 37,
        Shake = 38,
        SkillKit = 39,
        Sound = 40,
        StringList = 42,
        Surface = 43,
        Texture = 44,
        Trail = 45,
        UI = 46,
        Weather = 47,
        World = 48,
        Recipe = 49,
        Condition = 51,
        TreasureClass = 52,
        Account = 53,
        Material = 57,
        Lore = 59,
        Reverb = 60,
        Music = 62,
        Tutorial = 63,
        AnimTree = 67,
        Vibration = 68,
        wWiseSoundBank = 71,
        Speaker = 72,
        Item = 73,
        PlayerClass = 74,
        FogVolume = 76,
        Biome = 77,
        Wall = 78,
        SoundTable = 79,
        Subzone = 80,
        MaterialValue = 81,
        MonsterFamily = 82,
        TileSet = 83,
        Population = 84,
        MaterialValueSet = 85,
        WorldState = 86,
        Schedule = 87,
        VectorField = 88,
        Storyboard = 90,
        Territory = 92,
        AudioContext = 93,
        VOProcess = 94,
        DemonScroll = 95,
        QuestChain = 96,
        LoudnessPreset = 97,
        ItemType = 98,
        Achievement = 99,
        Crafter = 100,
        HoudiniParticlesSim = 101,
        Movie = 102,
        TiledStyle = 103,
        Affix = 104,
        Reputation = 105,
        ParagonNode = 106,
        MonsterAffix = 107,
        ParagonBoard = 108,
        SetItemBonus = 109,
        StoreProduct = 110,
        ParagonGlyph = 111,
        ParagonGlyphAffix = 112,
        Challenge = 114,
        MarkingShape = 115,
        ItemRequirement = 116,
        Boost = 117,
        Emote = 118,
        Jewelry = 119,
        PlayerTitle = 120,
        Emblem = 121,
        Dye = 122,
        FogOfWar = 123,
        ParagonThreshold = 124,
        AIAwareness = 125,
        TrackedReward = 126,
        CollisionSettings = 127,
        Aspect = 128,
        ABTest = 129,
        Stagger = 130,
        EyeColor = 131,
        Makeup = 132,
        MarkingColor = 133,
        HairColor = 134,
        DungeonAffix = 135,
        Activity = 136,
        Season = 137,
        HairStyle = 138,
        FacialHair = 139,
        Face = 140,
        MercenaryClass = 141,
        PassivePowerContainer = 142,
        MountProfile = 143,
        AICoordinator = 144,
        CrafterTab = 145,
        TownPortalCosmetic = 146,
        AxeTest = 147,
        Wizard = 148,
        FootstepTable = 149,
        Modal = 150,
        CollectiblePower = 151,
        AppearanceSet = 152,
        Preset = 153,
        PreviewComposition = 154,
        SpawnPool = 155,
        Unknown_156 = 156, // .rdx
        BattlePassTier = 157, // .bpt
        Unknown_158 = 158, // .zon
        Unknown_159 = 159, // .ggu
        MAX_SNO_GROUPS = 160,
    }

    public class CoreTOCParserD4
    {
        private const int MAX_SNO_GROUPS = 160;

        public unsafe struct TOCHeader
        {
            public int numSnoGroups;
            //[MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_SNO_GROUPS)]
            public fixed int entryCounts[MAX_SNO_GROUPS];
            //[MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_SNO_GROUPS)]
            public fixed int entryOffsets[MAX_SNO_GROUPS];
            //[MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_SNO_GROUPS)]
            public fixed int entryUnkCounts[MAX_SNO_GROUPS];
            public int unk;
        }

        readonly Dictionary<int, SNOInfoD4> snoDic = new Dictionary<int, SNOInfoD4>();

        public IReadOnlyDictionary<int, SNOInfoD4> SnoData => snoDic;

        readonly Dictionary<SNOGroupD4, string> extensions = new Dictionary<SNOGroupD4, string>()
        {
            [(SNOGroupD4)0] = "",
            [(SNOGroupD4)1] = ".acr",
            [(SNOGroupD4)2] = ".npc",
            [(SNOGroupD4)3] = ".aib",
            [(SNOGroupD4)4] = ".ais",
            [(SNOGroupD4)5] = ".ams",
            [(SNOGroupD4)6] = ".ani",
            [(SNOGroupD4)7] = ".an2",
            [(SNOGroupD4)8] = ".ans",
            [(SNOGroupD4)9] = ".app",
            [(SNOGroupD4)10] = ".hro",
            [(SNOGroupD4)11] = ".clt",
            [(SNOGroupD4)12] = ".cnv",
            [(SNOGroupD4)13] = ".cnl",
            [(SNOGroupD4)14] = ".efg",
            [(SNOGroupD4)15] = ".enc",
            [(SNOGroupD4)16] = "",
            [(SNOGroupD4)17] = ".xpl",
            [(SNOGroupD4)18] = ".flg",
            [(SNOGroupD4)19] = ".fnt",
            [(SNOGroupD4)20] = ".gam",
            [(SNOGroupD4)21] = ".glo",
            [(SNOGroupD4)22] = ".lvl",
            [(SNOGroupD4)23] = ".lit",
            [(SNOGroupD4)24] = ".mrk",
            [(SNOGroupD4)25] = "",
            [(SNOGroupD4)26] = ".obs",
            [(SNOGroupD4)27] = ".prt",
            [(SNOGroupD4)28] = ".phy",
            [(SNOGroupD4)29] = ".pow",
            [(SNOGroupD4)30] = "",
            [(SNOGroupD4)31] = ".qst",
            [(SNOGroupD4)32] = ".rop",
            [(SNOGroupD4)33] = ".scn",
            [(SNOGroupD4)34] = "",
            [(SNOGroupD4)35] = ".scr",
            [(SNOGroupD4)36] = ".shm",
            [(SNOGroupD4)37] = ".shd",
            [(SNOGroupD4)38] = ".shk",
            [(SNOGroupD4)39] = ".skl",
            [(SNOGroupD4)40] = ".snd",
            [(SNOGroupD4)41] = "",
            [(SNOGroupD4)42] = ".stl",
            [(SNOGroupD4)43] = ".srf",
            [(SNOGroupD4)44] = ".tex",
            [(SNOGroupD4)45] = ".trl",
            [(SNOGroupD4)46] = ".ui",
            [(SNOGroupD4)47] = ".wth",
            [(SNOGroupD4)48] = ".wrl",
            [(SNOGroupD4)49] = ".rcp",
            [(SNOGroupD4)50] = "",
            [(SNOGroupD4)51] = ".cnd",
            [(SNOGroupD4)52] = ".trs",
            [(SNOGroupD4)53] = ".acc",
            [(SNOGroupD4)54] = "",
            [(SNOGroupD4)55] = "",
            [(SNOGroupD4)56] = "",
            [(SNOGroupD4)57] = ".mat",
            [(SNOGroupD4)58] = "",
            [(SNOGroupD4)59] = ".lor",
            [(SNOGroupD4)60] = ".rev",
            [(SNOGroupD4)61] = "",
            [(SNOGroupD4)62] = ".mus",
            [(SNOGroupD4)63] = ".tut",
            [(SNOGroupD4)64] = "",
            [(SNOGroupD4)65] = "",
            [(SNOGroupD4)66] = "",
            [(SNOGroupD4)67] = ".ant",
            [(SNOGroupD4)68] = ".vib",
            [(SNOGroupD4)69] = "",
            [(SNOGroupD4)70] = "",
            [(SNOGroupD4)71] = ".wsb",
            [(SNOGroupD4)72] = ".spk",
            [(SNOGroupD4)73] = ".itm",
            [(SNOGroupD4)74] = ".pcl",
            [(SNOGroupD4)75] = "",
            [(SNOGroupD4)76] = ".fog",
            [(SNOGroupD4)77] = ".bio",
            [(SNOGroupD4)78] = ".wal",
            [(SNOGroupD4)79] = ".sdt",
            [(SNOGroupD4)80] = ".sbz",
            [(SNOGroupD4)81] = ".mtv",
            [(SNOGroupD4)82] = ".mfm",
            [(SNOGroupD4)83] = ".tst",
            [(SNOGroupD4)84] = ".pop",
            [(SNOGroupD4)85] = ".mvs",
            [(SNOGroupD4)86] = ".wst",
            [(SNOGroupD4)87] = ".sch",
            [(SNOGroupD4)88] = ".vfd",
            [(SNOGroupD4)89] = "",
            [(SNOGroupD4)90] = ".stb",
            [(SNOGroupD4)91] = "",
            [(SNOGroupD4)92] = ".ter",
            [(SNOGroupD4)93] = ".auc",
            [(SNOGroupD4)94] = ".vop",
            [(SNOGroupD4)95] = ".dss",
            [(SNOGroupD4)96] = ".qc",
            [(SNOGroupD4)97] = ".lou",
            [(SNOGroupD4)98] = ".itt",
            [(SNOGroupD4)99] = ".ach",
            [(SNOGroupD4)100] = ".crf",
            [(SNOGroupD4)101] = ".hps",
            [(SNOGroupD4)102] = ".vid",
            [(SNOGroupD4)103] = ".tsl",
            [(SNOGroupD4)104] = ".aff",
            [(SNOGroupD4)105] = ".rep",
            [(SNOGroupD4)106] = ".pgn",
            [(SNOGroupD4)107] = ".maf",
            [(SNOGroupD4)108] = ".pbd",
            [(SNOGroupD4)109] = ".set",
            [(SNOGroupD4)110] = ".prd",
            [(SNOGroupD4)111] = ".gph",
            [(SNOGroupD4)112] = ".gaf",
            [(SNOGroupD4)113] = "",
            [(SNOGroupD4)114] = ".cha",
            [(SNOGroupD4)115] = ".msh",
            [(SNOGroupD4)116] = ".irq",
            [(SNOGroupD4)117] = ".bst",
            [(SNOGroupD4)118] = ".emo",
            [(SNOGroupD4)119] = ".jwl",
            [(SNOGroupD4)120] = ".pt",
            [(SNOGroupD4)121] = ".emb",
            [(SNOGroupD4)122] = ".dye",
            [(SNOGroupD4)123] = ".fow",
            [(SNOGroupD4)124] = ".pth",
            [(SNOGroupD4)125] = ".aia",
            [(SNOGroupD4)126] = ".trd",
            [(SNOGroupD4)127] = ".col",
            [(SNOGroupD4)128] = ".asp",
            [(SNOGroupD4)129] = ".abt",
            [(SNOGroupD4)130] = ".stg",
            [(SNOGroupD4)131] = ".eye",
            [(SNOGroupD4)132] = ".mak",
            [(SNOGroupD4)133] = ".mcl",
            [(SNOGroupD4)134] = ".hcl",
            [(SNOGroupD4)135] = ".dax",
            [(SNOGroupD4)136] = ".act",
            [(SNOGroupD4)137] = ".sea",
            [(SNOGroupD4)138] = ".har",
            [(SNOGroupD4)139] = ".fhr",
            [(SNOGroupD4)140] = ".fac",
            [(SNOGroupD4)141] = ".mrc",
            [(SNOGroupD4)142] = ".ppc",
            [(SNOGroupD4)143] = ".mpp",
            [(SNOGroupD4)144] = ".aic",
            [(SNOGroupD4)145] = ".ctb",
            [(SNOGroupD4)146] = ".tpc",
            [(SNOGroupD4)147] = ".axe",
            [(SNOGroupD4)148] = ".wiz",
            [(SNOGroupD4)149] = ".fst",
            [(SNOGroupD4)150] = ".mdl",
            [(SNOGroupD4)151] = ".cpw",
            [(SNOGroupD4)152] = ".aps",
            [(SNOGroupD4)153] = ".pst",
            [(SNOGroupD4)154] = ".pvc",
            [(SNOGroupD4)155] = ".spn",
            [(SNOGroupD4)156] = ".rdx",
            [(SNOGroupD4)157] = ".bpt",
            [(SNOGroupD4)158] = ".zon",
            [(SNOGroupD4)159] = ".ggu",
        };

        public unsafe CoreTOCParserD4(Stream stream)
        {
            using (var br = new BinaryReader(stream))
            {
                int numSnoGroups = br.ReadInt32();

                //if (numSnoGroups != NUM_SNO_GROUPS)
                //    return;

                int[] entryCounts = new int[numSnoGroups];

                for (int i = 0; i < entryCounts.Length; i++)
                {
                    entryCounts[i] = br.ReadInt32();
                }

                int[] entryOffsets = new int[numSnoGroups];

                for (int i = 0; i < entryOffsets.Length; i++)
                {
                    entryOffsets[i] = br.ReadInt32();
                }

                int[] entryUnkCounts = new int[numSnoGroups];

                for (int i = 0; i < entryUnkCounts.Length; i++)
                {
                    entryUnkCounts[i] = br.ReadInt32();
                }

                int unk1 = br.ReadInt32();

                int headerSize = 4 + numSnoGroups * (4 + 4 + 4) + 4;

                for (int i = 0; i < numSnoGroups; i++)
                {
                    if (entryCounts[i] > 0)
                    {
                        br.BaseStream.Position = entryOffsets[i] + headerSize;

                        for (int j = 0; j < entryCounts[i]; j++)
                        {
                            SNOGroupD4 snoGroup = (SNOGroupD4)br.ReadInt32();
                            int snoId = br.ReadInt32();
                            int pName = br.ReadInt32();

                            long oldPos = br.BaseStream.Position;
                            br.BaseStream.Position = entryOffsets[i] + headerSize + 12 * entryCounts[i] + pName;
                            string name = br.ReadCString();
                            br.BaseStream.Position = oldPos;

                            snoDic.Add(snoId, new SNOInfoD4() { GroupId = snoGroup, Name = name, Ext = extensions.TryGetValue(snoGroup, out var ext) ? ext : $".{(int)snoGroup:D3}" });
                        }
                    }
                }
            }
        }

        public SNOInfoD4 GetSNO(int id)
        {
            snoDic.TryGetValue(id, out SNOInfoD4 sno);
            return sno;
        }
    }
}
