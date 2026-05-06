using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEngine;
using Verse;

namespace FindModTranslations
{
    public class ActiveModInfo
    {
        public string name;
        public string packageId;
        public string steamId;
        public bool hasBuiltInTargetLanguage;
        public int builtInTargetLanguageEntries;
        public object meta;
    }

    public class ActiveModIndex
    {
        public List<ActiveModInfo> mods = new List<ActiveModInfo>();
        public List<ActiveModInfo> installedMods = new List<ActiveModInfo>();
        public string Signature;

        private Dictionary<string, ActiveModInfo> activeBySteam = new Dictionary<string, ActiveModInfo>();
        private Dictionary<string, ActiveModInfo> activeByPackage = new Dictionary<string, ActiveModInfo>();
        private Dictionary<string, ActiveModInfo> activeByName = new Dictionary<string, ActiveModInfo>();
        private HashSet<string> activeSteamIds = new HashSet<string>();
        private HashSet<string> activePackageIds = new HashSet<string>();
        private HashSet<string> installedSteamIds = new HashSet<string>();
        private HashSet<string> installedPackageIds = new HashSet<string>();
        private static List<ActiveModInfo> cachedInstalledMods;
        private static DateTime cachedInstalledModsUtc = DateTime.MinValue;
        private static readonly TimeSpan InstalledModsCacheTtl = TimeSpan.FromSeconds(20);

        public static ActiveModIndex Create(string[] targetLanguageFolders)
        {
            ActiveModIndex index = new ActiveModIndex();
            foreach (ModContentPack mod in LoadedModManager.RunningModsListForReading)
            {
                index.mods.Add(ActiveMod(mod, targetLanguageFolders));
            }
            index.installedMods = InstalledMods();
            index.BuildLookups();
            return index;
        }

        public bool ContainsActiveTranslation(TranslationModInfo translation)
        {
            return ContainsTranslation(translation, activePackageIds, activeSteamIds);
        }

        public bool ContainsInstalledTranslation(TranslationModInfo translation)
        {
            return ContainsTranslation(translation, installedPackageIds, installedSteamIds) || ContainsActiveTranslation(translation);
        }

        public bool ContainsInactiveInstalledTranslation(TranslationModInfo translation)
        {
            return ContainsTranslation(translation, installedPackageIds, installedSteamIds) && !ContainsActiveTranslation(translation);
        }

        public ActiveModInfo FindSourceMatch(ModTranslationEntry entry)
        {
            if (entry == null) return null;
            if (!entry.steamId.NullOrEmpty() && activeBySteam.TryGetValue(entry.steamId, out ActiveModInfo bySteam))
            {
                return bySteam;
            }

            if (!entry.packageId.NullOrEmpty())
            {
                string packageId = SafeLower(entry.packageId);
                if (activeByPackage.TryGetValue(packageId, out ActiveModInfo byPackage) && SourceCanMatchWithoutSteam(byPackage, entry))
                {
                    return byPackage;
                }
            }

            if (!entry.name.NullOrEmpty())
            {
                string name = SafeLower(entry.name);
                if (activeByName.TryGetValue(name, out ActiveModInfo byName) && SourceCanMatchWithoutSteam(byName, entry))
                {
                    return byName;
                }
            }
            return null;
        }

        private static bool ContainsTranslation(TranslationModInfo translation, HashSet<string> packageIds, HashSet<string> steamIds)
        {
            if (translation == null) return false;
            if (!translation.packageId.NullOrEmpty() && packageIds.Contains(SafeLower(translation.packageId))) return true;
            if (!translation.steamId.NullOrEmpty() && steamIds.Contains(translation.steamId)) return true;
            return false;
        }

        private void BuildLookups()
        {
            activeBySteam.Clear();
            activeByPackage.Clear();
            activeByName.Clear();
            activeSteamIds.Clear();
            activePackageIds.Clear();
            installedSteamIds.Clear();
            installedPackageIds.Clear();

            foreach (ActiveModInfo mod in mods)
            {
                AddLookup(activeBySteam, mod.steamId, mod, false);
                AddLookup(activeByPackage, mod.packageId, mod, true);
                AddLookup(activeByName, mod.name, mod, true);
                AddSet(activeSteamIds, mod.steamId, false);
                AddSet(activePackageIds, mod.packageId, true);
            }
            foreach (ActiveModInfo mod in installedMods)
            {
                AddSet(installedSteamIds, mod.steamId, false);
                AddSet(installedPackageIds, mod.packageId, true);
            }
            Signature = BuildSignature();
        }

        private string BuildSignature()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("active:");
            AppendSignature(builder, mods);
            builder.Append("|installed:");
            AppendSignature(builder, installedMods);
            return builder.ToString();
        }

        private static void AppendSignature(StringBuilder builder, List<ActiveModInfo> list)
        {
            foreach (ActiveModInfo mod in list)
            {
                builder.Append(SafeLower(mod.packageId)).Append('@').Append(mod.steamId ?? "").Append(';');
            }
        }

        private static bool SourceCanMatchWithoutSteam(ActiveModInfo active, ModTranslationEntry entry)
        {
            return active != null && (entry == null || entry.steamId.NullOrEmpty() || active.steamId.NullOrEmpty() || active.steamId == entry.steamId);
        }

        private static void AddLookup(Dictionary<string, ActiveModInfo> dictionary, string key, ActiveModInfo value, bool lower)
        {
            if (key.NullOrEmpty() || value == null) return;
            string normalized = lower ? SafeLower(key) : key;
            if (!dictionary.ContainsKey(normalized))
            {
                dictionary.Add(normalized, value);
            }
        }

        private static void AddSet(HashSet<string> set, string key, bool lower)
        {
            if (key.NullOrEmpty()) return;
            set.Add(lower ? SafeLower(key) : key);
        }

        private static List<ActiveModInfo> InstalledMods()
        {
            if (cachedInstalledMods != null && DateTime.UtcNow - cachedInstalledModsUtc < InstalledModsCacheTtl)
            {
                return cachedInstalledMods;
            }

            List<ActiveModInfo> result = new List<ActiveModInfo>();
            try
            {
                PropertyInfo property = typeof(ModLister).GetProperty("AllInstalledMods", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object value = property == null ? null : property.GetValue(null, null);
                System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;
                if (enumerable != null)
                {
                    foreach (object item in enumerable)
                    {
                        ActiveModInfo info = ActiveModMeta(item);
                        if (!info.packageId.NullOrEmpty())
                        {
                            result.Add(info);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Find Mod Translations] Could not read installed mod list: " + ex.Message);
            }
            AddLocalMods(result);
            cachedInstalledMods = result;
            cachedInstalledModsUtc = DateTime.UtcNow;
            return result;
        }


        private static void AddLocalMods(List<ActiveModInfo> result)
        {
            try
            {
                string modsRoot = GenFilePaths.ModsFolderPath;
                if (modsRoot.NullOrEmpty() || !Directory.Exists(modsRoot)) return;
                foreach (string dir in Directory.GetDirectories(modsRoot))
                {
                    try
                    {
                        ActiveModInfo info = AboutXmlMod(dir);
                        if (info == null || info.packageId.NullOrEmpty()) continue;
                        if (!result.Any(m => SafeLower(m.packageId) == SafeLower(info.packageId)))
                        {
                            result.Add(info);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[Find Mod Translations] Could not read local mod metadata from " + dir + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Find Mod Translations] Could not scan local Mods folder: " + ex.Message);
            }
        }

        private static ActiveModInfo AboutXmlMod(string rootDir)
        {
            string about = Path.Combine(rootDir, "About", "About.xml");
            if (!File.Exists(about)) return null;
            XmlDocument document = new XmlDocument();
            document.XmlResolver = null;
            document.Load(about);
            string name = XmlText(document, "name");
            string packageId = XmlText(document, "packageId");
            return new ActiveModInfo
            {
                name = name.NullOrEmpty() ? Path.GetFileName(rootDir) : name,
                packageId = SafeLower(packageId),
                steamId = PublishedFileId(rootDir)
            };
        }

        private static string XmlText(XmlDocument document, string tag)
        {
            XmlNode node = document.SelectSingleNode("/ModMetaData/" + tag);
            return node == null ? "" : (node.InnerText ?? "").Trim();
        }

        private static string PublishedFileId(string rootDir)
        {
            string path = Path.Combine(rootDir, "About", "PublishedFileId.txt");
            if (!File.Exists(path)) path = Path.Combine(rootDir, "PublishedFileId.txt");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
        }

        private static ActiveModInfo ActiveModMeta(object meta)
        {
            return new ActiveModInfo
            {
                name = StringMember(meta, "Name") ?? StringMember(meta, "name") ?? "<unnamed>",
                packageId = SafeLower(StringMember(meta, "PackageId") ?? StringMember(meta, "packageId") ?? StringMember(meta, "PackageIdNonUnique") ?? ""),
                steamId = StringMember(meta, "PublishedFileId") ?? "",
                meta = meta
            };
        }

        private static string StringMember(object obj, string name)
        {
            if (obj == null) return "";
            Type type = obj.GetType();
            PropertyInfo prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null)
            {
                object value = prop.GetValue(obj, null);
                return value == null ? "" : value.ToString();
            }
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                object value = field.GetValue(obj);
                return value == null ? "" : value.ToString();
            }
            return "";
        }

        private static ActiveModInfo ActiveMod(ModContentPack mod, string[] targetLanguageFolders)
        {
            int targetLanguageEntries = CountBuiltInTargetLanguageEntries(mod, targetLanguageFolders);
            return new ActiveModInfo
            {
                name = mod.Name ?? "<unnamed>",
                packageId = SafeLower(mod.PackageId),
                steamId = PublishedId(mod),
                builtInTargetLanguageEntries = targetLanguageEntries,
                hasBuiltInTargetLanguage = targetLanguageEntries > 0
            };
        }

        private static int CountBuiltInTargetLanguageEntries(ModContentPack mod, string[] targetLanguageFolders)
        {
            if (mod == null) return 0;
            try
            {
                string root = mod.RootDir;
                if (root.NullOrEmpty() || !Directory.Exists(root)) return 0;
                int count = 0;
                foreach (string targetLanguageFolder in targetLanguageFolders ?? new string[0])
                {
                    string safeFolder = LanguageTarget.SafeFolderName(targetLanguageFolder);
                    if (safeFolder.NullOrEmpty()) continue;
                    DirectoryInfo language = new DirectoryInfo(Path.Combine(root, "Languages", safeFolder));
                    if (!language.Exists) continue;
                    foreach (FileInfo file in language.EnumerateFiles("*.xml", SearchOption.AllDirectories))
                    {
                        if (file.FullName.IndexOf(Path.DirectorySeparatorChar + "WordInfo" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            continue;
                        }
                        count += CountLanguageDataEntries(file.FullName);
                        if (count > 0) return count;
                    }
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        private static int CountLanguageDataEntries(string path)
        {
            try
            {
                XmlDocument document = new XmlDocument();
                document.XmlResolver = null;
                document.Load(path);
                XmlNode root = document.DocumentElement;
                if (root == null || root.Name != "LanguageData") return 0;
                return CountTextLeaves(root);
            }
            catch
            {
                return 0;
            }
        }

        private static int CountTextLeaves(XmlNode node)
        {
            int count = 0;
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                bool hasElementChild = false;
                foreach (XmlNode grandChild in child.ChildNodes)
                {
                    if (grandChild.NodeType == XmlNodeType.Element)
                    {
                        hasElementChild = true;
                        break;
                    }
                }
                if (!hasElementChild && !child.InnerText.NullOrEmpty())
                {
                    count++;
                }
                else
                {
                    count += CountTextLeaves(child);
                }
            }
            return count;
        }

        private static string PublishedId(ModContentPack mod)
        {
            try
            {
                MethodInfo method = typeof(ModContentPack).GetMethod("GetPublishedFileId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object value = method == null ? null : method.Invoke(mod, null);
                string id = value == null ? "" : value.ToString();
                return id == "0" ? "" : id;
            }
            catch
            {
                return "";
            }
        }

        public static string SafeLower(string value)
        {
            return (value ?? "").Trim().ToLowerInvariant();
        }
    }
}
