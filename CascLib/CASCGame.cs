using System;
using System.IO;

namespace CASCLib
{
    public enum CASCGameType
    {
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
        D2R,
        Wlby,
        Viper,
        Odin,
        Lazarus,
        Fore,
        Zeus,
        Rtro,
        Anbs,
        D4,
        DRTL,
        DRTL2,
        WC1,
        WC2,
        Gryphon
    }

    public class CASCGame
    {
        public static CASCGameType? DetectLocalGame(string path, string product, string buildKey)
        {
            string[] dirs = Directory.GetDirectories(path, "*", SearchOption.AllDirectories);

            foreach (var dir in dirs)
            {
                string buildCfgPath = Path.Combine(dir, buildKey);
                if (File.Exists(buildCfgPath))
                {
                    using (Stream stream = new FileStream(buildCfgPath, FileMode.Open))
                    {
                        KeyValueConfig cfg = KeyValueConfig.ReadKeyValueConfig(stream);
                        string buildUid = cfg["build-uid"][0];
                        if (buildUid != product)
                            return null;
                        return DetectGameByUid(cfg["build-uid"][0]);
                    }
                }
            }
            return null;
        }

        public static CASCGameType DetectGameByUid(string uid)
        {
            return uid switch
            {
                _ when uid.StartsWith("hero") => CASCGameType.HotS,
                _ when uid.StartsWith("hs") => CASCGameType.Hearthstone,
                _ when uid.StartsWith("w3") => CASCGameType.WC3,
                _ when uid.StartsWith("s1") => CASCGameType.S1,
                _ when uid.StartsWith("s2") => CASCGameType.S2,
                _ when uid.StartsWith("wow") => CASCGameType.WoW,
                _ when uid.StartsWith("d3") => CASCGameType.D3,
                _ when uid.StartsWith("agent") => CASCGameType.Agent,
                _ when uid.StartsWith("pro") => CASCGameType.Overwatch,
                _ when uid.StartsWith("bna") => CASCGameType.Bna,
                _ when uid.StartsWith("clnt") => CASCGameType.Client,
                _ when uid.StartsWith("dst2") => CASCGameType.Destiny2,
                _ when uid.StartsWith("osi") => CASCGameType.D2R,
                _ when uid.StartsWith("wlby") => CASCGameType.Wlby,
                _ when uid.StartsWith("viper") => CASCGameType.Viper,
                _ when uid.StartsWith("odin") => CASCGameType.Odin,
                _ when uid.StartsWith("lazr") => CASCGameType.Lazarus,
                _ when uid.StartsWith("fore") => CASCGameType.Fore,
                _ when uid.StartsWith("zeus") => CASCGameType.Zeus,
                _ when uid.StartsWith("rtro") => CASCGameType.Rtro,
                _ when uid.StartsWith("anbs") => CASCGameType.Anbs,
                _ when uid.StartsWith("fenris") => CASCGameType.D4,
                _ when uid.StartsWith("drtl2") => CASCGameType.DRTL2,
                _ when uid.StartsWith("drtl") => CASCGameType.DRTL,
                _ when uid.StartsWith("war1") => CASCGameType.WC1,
                _ when uid.StartsWith("w2bn") => CASCGameType.WC2,
                _ when uid.StartsWith("gryphon") => CASCGameType.Gryphon,
                _ => throw new Exception("Unable to detect game type by uid")
            };
        }

        public static string GetDataFolder(CASCGameType gameType)
        {
            return gameType switch
            {
                CASCGameType.HotS => "HeroesData",
                CASCGameType.S2 => "SC2Data",
                CASCGameType.Hearthstone => "Hearthstone_Data",
                CASCGameType.WoW or CASCGameType.D3 or CASCGameType.D4 or CASCGameType.WC3 or CASCGameType.D2R => "Data",
                CASCGameType.Odin => "Data",
                CASCGameType.Overwatch => "data/casc",
                _ => throw new Exception("GetDataFolder called with unsupported gameType")
            };
        }

        public static bool SupportsLocaleSelection(CASCGameType gameType)
        {
            return gameType is CASCGameType.D3 or
                CASCGameType.WoW or
                CASCGameType.HotS or
                CASCGameType.S2 or
                CASCGameType.S1 or
                CASCGameType.Overwatch;
        }
    }
}
