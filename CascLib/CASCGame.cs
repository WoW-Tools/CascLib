using System;
using System.IO;

namespace CASCLib
{
    public enum CASCGameType
    {
        Unknown,
        HotS,
        WoW,
        D3,
        S2,
        Agent,
        Hearthstone,
        Overwatch,
        Bna,
        Client,
        S1,
        WC3,
        Destiny2,
        D2R
    }

    public class CASCGame
    {
        static readonly string[] wowWinBins = new string[] { "Wow.exe", "WowT.exe", "WowB.exe", "WowClassic.exe", "WowClassicT.exe", "WowClassicB.exe" };
        static readonly string[] wowOsxBins = new string[] { "World of Warcraft.app", "World of Warcraft Test.app", "World of Warcraft Beta.app", "World of Warcraft Classic.app" };
        static readonly string[] wowSubFolders = new string[] { "_retail_", "_ptr_", "_beta_", "_alpha_", "_event1_", "_classic_", "_classic_beta_", "_classic_ptr_", "_classic_era_", "_classic_era_beta_", "_classic_era_ptr_" };

        public static CASCGameType DetectLocalGame(string path)
        {
            if (Directory.Exists(Path.Combine(path, "HeroesData")))
                return CASCGameType.HotS;

            if (Directory.Exists(Path.Combine(path, "SC2Data")))
                return CASCGameType.S2;

            if (Directory.Exists(Path.Combine(path, "Hearthstone_Data")))
                return CASCGameType.Hearthstone;

            if (Directory.Exists(Path.Combine(path, "Data")))
            {
                if (File.Exists(Path.Combine(path, "Diablo III.exe")))
                    return CASCGameType.D3;

                if (File.Exists(Path.Combine(path, "D2R.exe")))
                    return CASCGameType.D2R;

                if (File.Exists(Path.Combine(path, "Diablo II Resurrected Launcher.exe")))
                    return CASCGameType.D2R;

                if (File.Exists(Path.Combine(path, "Warcraft III.exe")))
                    return CASCGameType.WC3;

                if (File.Exists(Path.Combine(path, "Warcraft III Launcher.exe")))
                    return CASCGameType.WC3;

                if (File.Exists(Path.Combine(path, "x86", "Warcraft III.exe")))
                    return CASCGameType.WC3;

                if (File.Exists(Path.Combine(path, "x86_64", "Warcraft III.exe")))
                    return CASCGameType.WC3;

                if (File.Exists(Path.Combine(path, "_retail_", "x86_64", "Warcraft III.exe")))
                    return CASCGameType.WC3;

                if (File.Exists(Path.Combine(path, "_ptr_", "x86_64", "Warcraft III.exe")))
                    return CASCGameType.WC3;

                if (File.Exists(Path.Combine(path, "Agent.exe")))
                    return CASCGameType.Agent;

                if (File.Exists(Path.Combine(path, "Battle.net.exe")))
                    return CASCGameType.Bna;

                if (File.Exists(Path.Combine(path, "Overwatch.exe")))
                    return CASCGameType.Overwatch;

                if (File.Exists(Path.Combine(path, "Overwatch Launcher.exe")))
                    return CASCGameType.Overwatch;

                if (File.Exists(Path.Combine(path, "StarCraft.exe")))
                    return CASCGameType.S1;

                for (int i = 0; i < wowWinBins.Length; i++)
                {
                    if (File.Exists(Path.Combine(path, wowWinBins[i])))
                        return CASCGameType.WoW;
                }

                for (int i = 0; i < wowOsxBins.Length; i++)
                {
                    if (Directory.Exists(Path.Combine(path, wowOsxBins[i])))
                        return CASCGameType.WoW;
                }

                foreach (var subFolder in wowSubFolders)
                {
                    foreach (var wowBin in wowWinBins)
                    {
                        if (File.Exists(Path.Combine(path, subFolder, wowBin)))
                            return CASCGameType.WoW;
                    }

                    foreach (var wowBin in wowOsxBins)
                    {
                        if (Directory.Exists(Path.Combine(path, subFolder, wowBin)))
                            return CASCGameType.WoW;
                    }
                }
            }

            throw new Exception("Unable to detect game type by path");
        }

        public static CASCGameType DetectGameByUid(string uid)
        {
            if (uid.StartsWith("hero"))
                return CASCGameType.HotS;

            if (uid.StartsWith("hs"))
                return CASCGameType.Hearthstone;

            if (uid.StartsWith("w3"))
                return CASCGameType.WC3;

            if (uid.StartsWith("s1"))
                return CASCGameType.S1;

            if (uid.StartsWith("s2"))
                return CASCGameType.S2;

            if (uid.StartsWith("wow"))
                return CASCGameType.WoW;

            if (uid.StartsWith("d3"))
                return CASCGameType.D3;

            if (uid.StartsWith("agent"))
                return CASCGameType.Agent;

            if (uid.StartsWith("pro"))
                return CASCGameType.Overwatch;

            if (uid.StartsWith("bna"))
                return CASCGameType.Bna;

            if (uid.StartsWith("clnt"))
                return CASCGameType.Client;

            if (uid.StartsWith("dst2"))
                return CASCGameType.Destiny2;

            if (uid.StartsWith("osi"))
                return CASCGameType.D2R;

            throw new Exception("Unable to detect game type by uid");
        }

        public static string GetDataFolder(CASCGameType gameType)
        {
            if (gameType == CASCGameType.HotS)
                return "HeroesData";

            if (gameType == CASCGameType.S2)
                return "SC2Data";

            if (gameType == CASCGameType.Hearthstone)
                return "Hearthstone_Data";

            if (gameType == CASCGameType.WoW || gameType == CASCGameType.D3 || gameType == CASCGameType.WC3 || gameType == CASCGameType.D2R)
                return "Data";

            if (gameType == CASCGameType.Overwatch)
                return "data/casc";

            throw new Exception("GetDataFolder called with unsupported gameType");
        }

        public static bool SupportsLocaleSelection(CASCGameType gameType)
        {
            return gameType is CASCGameType.D3 or
                CASCGameType.WoW or
                CASCGameType.HotS or
                CASCGameType.S2 or
                CASCGameType.S1 or
                CASCGameType.WC3 or
                CASCGameType.D2R or
                CASCGameType.Overwatch;
        }
    }
}
