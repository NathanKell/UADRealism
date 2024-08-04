//#define LOGHULLSTATS
//#define LOGHULLSCALES
//#define LOGPARTSTATS
//#define LOGGUNSTATS

using System;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using Il2Cpp;
using Il2CppInterop.Runtime.Attributes;

#pragma warning disable CS8600
#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8618
#pragma warning disable CS8625

namespace TweaksAndFixes
{
    [RegisterTypeInIl2Cpp]
    public class FlagDatabase : MonoBehaviour
    {
        public class FlagDataStore
        {
            public enum FlagType
            {
                Default,
                Monarchy,
                Democracy,
                Communists,
                Fascists
            }

            public class FlagData
            {
                private Dictionary<FlagType, Sprite> _flagsByType = new Dictionary<FlagType, Sprite>();

                public Sprite Flag(FlagType type)
                {
                    if (_flagsByType.TryGetValue(type, out var sprite))
                        return sprite;

                    if (type != FlagType.Default && _flagsByType.TryGetValue(FlagType.Default, out sprite))
                        return sprite;

                    return null;
                }

                public void Add(Sprite sprite, FlagType type)
                {
                    _flagsByType[type] = sprite;
                }

                public void Add(string file, FlagType type)
                {
                    var sprite = Instance.GetSprite(file);
                    if (sprite == null)
                    {
                        Melon<TweaksAndFixes>.Logger.Msg("Reading flag from disk: " + file);

                        string basePath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "Flags");
                        if (!Directory.Exists(basePath))
                        {
                            Melon<TweaksAndFixes>.Logger.Error("Failed to find Flags directory: " + basePath);
                            return;
                        }
                        string filePath = Path.Combine(basePath, file);
                        if (!File.Exists(filePath))
                        {
                            Melon<TweaksAndFixes>.Logger.Error("Failed to find flag image file " + filePath);
                            return;
                        }

                        var rawData = File.ReadAllBytes(filePath);
                        // Unity is probably going to replace this with DXT5, annoyingly.
                        // And Texture2D.LoadImage() isn't supported. So we probably
                        // just accept these will be DXT5.
                        var tex = new Texture2D(256, 128, TextureFormat.DXT1, true);
                        if (!ImageConversion.LoadImage(tex, rawData))
                        {
                            Melon<TweaksAndFixes>.Logger.Error("Failed to load flag image file " + filePath);
                        }
                        //Debug.Log($"Texture {file} has size {tex.width}x{tex.height}@{tex.format}");
                        sprite = Sprite.Create(tex, new Rect(0, 0, 256, 128), new Vector2(128, 64));
                        Instance.AddSprite(file, sprite);
                    }

                    Add(sprite, type);
                }
            }

            public class NationalFlag : Serializer.IPostProcess
            {
                [Serializer.Field] public string name = string.Empty;
                [Serializer.Field] private string civilFlag = string.Empty;
                [Serializer.Field] private string navalFlag = string.Empty;
                [Serializer.Field] private string civilGovFlags = string.Empty;
                [Serializer.Field] private string navalGovFlags = string.Empty;

                private FlagData _civil = new FlagData();
                private FlagData _naval = new FlagData();

                public void PostProcess()
                {
                    //Debug.Log($"Processing flag entry {name}: {civilFlag}/{navalFlag}/{civilGovFlags}/{navalGovFlags}");

                    if (!string.IsNullOrEmpty(civilFlag))
                        _civil.Add(civilFlag, FlagType.Default);
                    if (!string.IsNullOrEmpty(navalFlag))
                        _naval.Add(navalFlag, FlagType.Default);

                    var dictCivil = Serializer.Human.HumanModToDictionary1D(civilGovFlags);
                    var dictNaval = Serializer.Human.HumanModToDictionary1D(navalGovFlags);

                    AddFlags(dictCivil, _civil);
                    AddFlags(dictNaval, _naval);
                }

                public Sprite DefaultFlag(bool naval)
                    => naval ? _naval.Flag(FlagType.Default) : _civil.Flag(FlagType.Default);

                public Sprite StringToFlag(string gameFlagType)
                {
                    bool isNaval;
                    string type;
                    if (gameFlagType.EndsWith("Naval"))
                    {
                        isNaval = true;
                        type = gameFlagType.Remove(gameFlagType.LastIndexOf("Naval"));
                    }
                    else
                    {
                        isNaval = false;
                        type = gameFlagType;
                    }
                    if (!Enum.TryParse<FlagType>(type.Substring(5), out var eType))
                        eType = FlagType.Default;

                    return isNaval ? _naval.Flag(eType) : _civil.Flag(eType);
                }

                private void AddFlags(Dictionary<string, List<string>> dict, FlagData data)
                {
                    foreach (var kvp in dict)
                    {
                        if (kvp.Value.Count == 0 || !Enum.TryParse<FlagType>(kvp.Key, out var type))
                            continue;

                        data.Add(kvp.Value[0], type);
                    }
                }
            }

            private Dictionary<string, NationalFlag> _flagsByCountry = new Dictionary<string, NationalFlag>();

            public void Load()
            {
                string basePath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (!Directory.Exists(basePath))
                {
                    Melon<TweaksAndFixes>.Logger.Error("Failed to find Mods directory: " + basePath);
                    return;
                }
                string filePath = Path.Combine(basePath, "flags.csv");
                if (!File.Exists(filePath))
                {
                    Melon<TweaksAndFixes>.Logger.Error("Failed to find Flags file " + filePath);
                    return;
                }

                Melon<TweaksAndFixes>.Logger.Msg("Loading flags database");
                Serializer.CSV.Read<Dictionary<string, NationalFlag>, string, NationalFlag>(_flagsByCountry, filePath, "name", true);
            }

            public Sprite GetFlag(PlayerData data, bool naval = false, Player player = null, int newYear = 0)
            {
                if (!_flagsByCountry.TryGetValue(data.name, out var flags))
                    return null;

                // Note we are explicitly returning nulls here,
                // because Unity objects override == and thus may not
                // *really* be null.
                if (player == null || player.isMajor)
                {
                    var flagString = GetFlagName(data, naval, player, newYear);
                    if (flagString != null)
                    {
                        var sprite = flags.StringToFlag(flagString);
                        if (sprite != null)
                            return sprite;

                        Melon<TweaksAndFixes>.Logger.Error($"Tried to find flag for major country {data.name} and they exist in the database, but sprite was not found for {flagString} or its default");
                        return null;
                    }
                }
                // Everyone else _just_ gets their default flags.
                var defSprite = flags.DefaultFlag(naval);
                if (defSprite != null)
                    return defSprite;

                Melon<TweaksAndFixes>.Logger.Error($"Tried to find flag for {data.name} and they exist in the database, but sprite was not found for {(naval ? "naval" : "civil")}");
                return null;
            }

            private static string GetFlagName(PlayerData data, bool naval = false, Player player = null, int newYear = 0)
            {
                int year = 1890;
                if (GameManager.IsCampaign || GameManager.IsNewGame)
                {
                    Player.PartyList party = Player.PartyList.none;
                    Player.GovernmentsTypes gov = Player.GovernmentsTypes.ConstitutionalMonarchy;
                    if (GameManager.IsNewGame)
                    {
                        gov = Player.GetGovernmentsTypeByYear(out party, null, data, newYear);
                    }
                    else if (player != null)
                    {
                        gov = player.government;
                        party = player.mainParty;
                    }
                    else
                    {
                        gov = PlayerController.Instance.government;
                        party = PlayerController.Instance.mainParty;
                    }
                    string govStr = gov == Player.GovernmentsTypes.Democracy ? party.ToString() : gov.ToString();
                    return Player.GetFolderFlagFromName(govStr, naval);
                }
                else
                {
                    if (newYear > 0)
                    {
                        year = newYear;
                    }
                    else
                    {
                        if (GameManager.IsCustomBattle || GameManager.IsCustomBattleSetup)
                        {
                            year = CampaignController.Instance.CurrentDate.AsDate().Year;
                        }
                        else
                        {
                            if (GameManager.IsMission)
                            {
                                if (G.ui != null && G.ui.showMissionInfo != null)
                                    year = G.ui.showMissionInfo.year;
                                else
                                    year = 0;
                            }
                        }
                    }

                    int idx = data.yearForGovernmentTypeList.Count - 1;
                    if (idx < 0)
                        return null;
                    while (idx > 0 && year > data.yearForGovernmentTypeList[idx])
                        --idx;

                    return Player.GetFolderFlagFromName(data.governmentTypeOnYearList[idx], naval);
                }
            }
        }

        public FlagDatabase(IntPtr ptr) : base(ptr) { }

        private static FlagDatabase _Instance = null;
        public static FlagDatabase Instance
        {
            get
            {
                if (_Instance == null)
                {
                    var go = new GameObject("FlagDataHolder");
                    go.AddComponent<FlagDatabase>();
                }
                return _Instance;
            }
        }

        private FlagDataStore _flagStore;
        private Il2CppSystem.Collections.Generic.Dictionary<string, Sprite> _sprites = new Il2CppSystem.Collections.Generic.Dictionary<string, Sprite>();

        private void Awake()
        {
            if (_Instance != null)
                GameObject.Destroy(_Instance);
            _Instance = this;
        }

        private void OnDestroy()
        {
            if (_Instance == this)
                _Instance = null;
        }

        public void AddSprite(string filename, Sprite sprite)
        {
            _sprites[filename] = sprite;
        }

        public Sprite GetSprite(string filename)
        {
            if (_sprites.TryGetValue(filename, out var sprite))
                return sprite;

            return null;
        }

        private void LoadStore()
        {
            _flagStore = new FlagDataStore();
            _flagStore.Load();
        }

        public Sprite GetFlag(PlayerData data, bool naval = false, Player player = null, int newYear = 0)
        {
            if (_flagStore == null)
                LoadStore();

            return _flagStore.GetFlag(data, naval, player, newYear);
        }
    }
}
