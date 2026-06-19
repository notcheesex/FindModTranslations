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
        public string rootDir;
        public string[] gameVersions = new string[0];
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
        private Dictionary<string, int> activePackageCounts = new Dictionary<string, int>();
        private Dictionary<string, int> installedPackageCounts = new Dictionary<string, int>();
        private static List<ActiveModInfo> cachedInstalledMods;
        private static DateTime cachedInstalledModsUtc = DateTime.MinValue;
        private static readonly TimeSpan InstalledModsCacheTtl = TimeSpan.FromSeconds(20);
        private static readonly object CacheGate = new object();
        private static readonly Dictionary<string, BuiltInLanguageCacheEntry> builtInLanguageCache = new Dictionary<string, BuiltInLanguageCacheEntry>();
        private static readonly Dictionary<string, LocalModCacheEntry> localModCache = new Dictionary<string, LocalModCacheEntry>();

        public static ActiveModIndex Create(string[] targetLanguageFolders)
        {
            ActiveModIndex index = new ActiveModIndex();
            foreach (ModContentPack mod in LoadedModManager.RunningModsListForReading)
            {
                index.mods.Add(ActiveMod(mod, targetLanguageFolders, true));
            }
            index.installedMods = InstalledMods();
            index.BuildLookups();
            return index;
        }

        public static ActiveModIndex CreateActiveOnlyFast()
        {
            ActiveModIndex index = new ActiveModIndex();
            foreach (ModContentPack mod in LoadedModManager.RunningModsListForReading)
            {
                index.mods.Add(ActiveMod(mod, null, false));
            }
            index.BuildLookups();
            return index;
        }

        public static int CountTargetLanguageEntries(ActiveModInfo mod, string[] targetLanguageFolders)
        {
            if (mod == null || mod.rootDir.NullOrEmpty())
            {
                return 0;
            }
            return CountBuiltInTargetLanguageEntries(mod.rootDir, targetLanguageFolders);
        }

        public bool ContainsActiveTranslation(TranslationModInfo translation)
        {
            return ContainsTranslation(translation, activePackageIds, activeSteamIds, activePackageCounts);
        }

        public bool ContainsInstalledTranslation(TranslationModInfo translation)
        {
            return ContainsTranslation(translation, installedPackageIds, installedSteamIds, installedPackageCounts) || ContainsActiveTranslation(translation);
        }

        public bool ContainsInactiveInstalledTranslation(TranslationModInfo translation)
        {
            return ContainsTranslation(translation, installedPackageIds, installedSteamIds, installedPackageCounts) && !ContainsActiveTranslation(translation);
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

        private static bool ContainsTranslation(TranslationModInfo translation, HashSet<string> packageIds, HashSet<string> steamIds, Dictionary<string, int> packageCounts)
        {
            if (translation == null) return false;
            if (!translation.steamId.NullOrEmpty() && steamIds.Contains(translation.steamId)) return true;
            if (!translation.packageId.NullOrEmpty())
            {
                string packageId = SafeLower(translation.packageId);
                if (packageIds.Contains(packageId) && (translation.steamId.NullOrEmpty() || PackageIdIsUnambiguous(packageId, packageCounts)))
                {
                    return true;
                }
            }
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
            activePackageCounts.Clear();
            installedPackageCounts.Clear();

            foreach (ActiveModInfo mod in mods)
            {
                AddLookup(activeBySteam, mod.steamId, mod, false);
                AddLookup(activeByPackage, mod.packageId, mod, true);
                AddLookup(activeByName, mod.name, mod, true);
                AddSet(activeSteamIds, mod.steamId, false);
                AddSet(activePackageIds, mod.packageId, true);
                AddCount(activePackageCounts, mod.packageId);
            }
            foreach (ActiveModInfo mod in installedMods)
            {
                AddSet(installedSteamIds, mod.steamId, false);
                AddSet(installedPackageIds, mod.packageId, true);
                AddCount(installedPackageCounts, mod.packageId);
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
                builder.Append(SafeLower(mod.packageId)).Append('@').Append(mod.steamId ?? "").Append('#').Append(mod.builtInTargetLanguageEntries).Append(';');
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

        private static void AddCount(Dictionary<string, int> counts, string key)
        {
            if (key.NullOrEmpty()) return;
            string normalized = SafeLower(key);
            int count;
            counts.TryGetValue(normalized, out count);
            counts[normalized] = count + 1;
        }

        private static bool PackageIdIsUnambiguous(string packageId, Dictionary<string, int> packageCounts)
        {
            int count;
            return packageCounts == null || !packageCounts.TryGetValue(packageId, out count) || count <= 1;
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
                HashSet<string> knownPackageIds = new HashSet<string>();
                foreach (ActiveModInfo mod in result)
                {
                    if (!mod.packageId.NullOrEmpty())
                    {
                        knownPackageIds.Add(SafeLower(mod.packageId));
                    }
                }
                foreach (string dir in Directory.GetDirectories(modsRoot))
                {
                    try
                    {
                        ActiveModInfo info = AboutXmlMod(dir);
                        if (info == null || info.packageId.NullOrEmpty()) continue;
                        if (knownPackageIds.Add(SafeLower(info.packageId)))
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
            string cacheKey = SafeLower(rootDir);
            string stamp = FileStamp(about) + "|" + FileStamp(PublishedFileIdPath(rootDir));
            lock (CacheGate)
            {
                LocalModCacheEntry cached;
                if (localModCache.TryGetValue(cacheKey, out cached) && cached.stamp == stamp)
                {
                    return cached.info;
                }
            }

            XmlDocument document = new XmlDocument();
            document.XmlResolver = null;
            document.Load(about);
            string name = XmlText(document, "name");
            string packageId = XmlText(document, "packageId");
            ActiveModInfo info = new ActiveModInfo
            {
                name = name.NullOrEmpty() ? Path.GetFileName(rootDir) : name,
                packageId = SafeLower(packageId),
                steamId = PublishedFileId(rootDir),
                rootDir = rootDir,
                gameVersions = SupportedVersions(document)
            };
            lock (CacheGate)
            {
                localModCache[cacheKey] = new LocalModCacheEntry(stamp, info);
            }
            return info;
        }

        private static string[] SupportedVersions(XmlDocument document)
        {
            XmlNode versions = document.SelectSingleNode("/ModMetaData/supportedVersions");
            if (versions == null)
            {
                return new string[0];
            }

            List<string> result = new List<string>();
            foreach (XmlNode node in versions.ChildNodes)
            {
                if (node.NodeType != XmlNodeType.Element)
                {
                    continue;
                }
                string value = (node.InnerText ?? "").Trim();
                if (!value.NullOrEmpty())
                {
                    result.Add(value);
                }
            }
            return result.ToArray();
        }

        private static string[] SupportedVersionsFromAbout(string rootDir)
        {
            try
            {
                string about = Path.Combine(rootDir, "About", "About.xml");
                if (rootDir.NullOrEmpty() || !File.Exists(about))
                {
                    return new string[0];
                }

                XmlDocument document = new XmlDocument();
                document.XmlResolver = null;
                document.Load(about);
                return SupportedVersions(document);
            }
            catch
            {
                return new string[0];
            }
        }

        private static string XmlText(XmlDocument document, string tag)
        {
            XmlNode node = document.SelectSingleNode("/ModMetaData/" + tag);
            return node == null ? "" : (node.InnerText ?? "").Trim();
        }

        private static string PublishedFileId(string rootDir)
        {
            string path = PublishedFileIdPath(rootDir);
            return path.NullOrEmpty() ? "" : File.ReadAllText(path).Trim();
        }

        private static string PublishedFileIdPath(string rootDir)
        {
            string path = Path.Combine(rootDir, "About", "PublishedFileId.txt");
            if (File.Exists(path)) return path;
            path = Path.Combine(rootDir, "PublishedFileId.txt");
            return File.Exists(path) ? path : "";
        }

        private static string FileStamp(string path)
        {
            if (path.NullOrEmpty() || !File.Exists(path))
            {
                return "missing";
            }
            FileInfo info = new FileInfo(path);
            return info.LastWriteTimeUtc.Ticks + ":" + info.Length;
        }

        private static ActiveModInfo ActiveModMeta(object meta)
        {
            return new ActiveModInfo
            {
                name = FirstStringMember(meta, "Name", "name") ?? "<unnamed>",
                packageId = SafeLower(FirstStringMember(meta, "PackageId", "packageId", "PackageIdNonUnique") ?? ""),
                steamId = FirstStringMember(meta, "PublishedFileId", "publishedFileId") ?? "",
                rootDir = FirstStringMember(meta, "RootDir", "rootDir", "Folder", "folder") ?? "",
                gameVersions = StringArrayMember(meta, "SupportedVersions", "supportedVersions", "SupportedVersionsReadOnly", "supportedVersionsReadOnly"),
                meta = meta
            };
        }

        private static string FirstStringMember(object obj, params string[] names)
        {
            foreach (string name in names)
            {
                string value = StringMember(obj, name);
                if (!value.NullOrEmpty())
                {
                    return value;
                }
            }
            return "";
        }

        private static string StringMember(object obj, string name)
        {
            object value = MemberValue(obj, name);
            return value == null ? "" : value.ToString();
        }

        private static string[] StringArrayMember(object obj, params string[] names)
        {
            foreach (string name in names)
            {
                object value = MemberValue(obj, name);
                if (value == null)
                {
                    continue;
                }

                string[] strings = value as string[];
                if (strings != null)
                {
                    return strings.Where(v => !v.NullOrEmpty()).ToArray();
                }

                string text = value as string;
                if (!text.NullOrEmpty())
                {
                    return new[] { text };
                }

                System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;
                if (enumerable == null)
                {
                    continue;
                }

                List<string> result = new List<string>();
                foreach (object item in enumerable)
                {
                    string itemText = item == null ? "" : item.ToString();
                    if (!itemText.NullOrEmpty())
                    {
                        result.Add(itemText);
                    }
                }
                if (result.Count > 0)
                {
                    return result.ToArray();
                }
            }
            return new string[0];
        }

        private static object MemberValue(object obj, string name)
        {
            if (obj == null) return null;
            Type type = obj.GetType();
            PropertyInfo prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null)
            {
                return prop.GetValue(obj, null);
            }
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                return field.GetValue(obj);
            }
            return null;
        }

        private static ActiveModInfo ActiveMod(ModContentPack mod, string[] targetLanguageFolders, bool countTargetLanguage)
        {
            int targetLanguageEntries = countTargetLanguage ? CountBuiltInTargetLanguageEntries(mod, targetLanguageFolders) : 0;
            string root = mod == null ? "" : mod.RootDir;
            return new ActiveModInfo
            {
                name = mod.Name ?? "<unnamed>",
                packageId = SafeLower(mod.PackageId),
                steamId = PublishedId(mod),
                rootDir = root,
                gameVersions = countTargetLanguage ? SupportedVersionsFromAbout(root) : new string[0],
                builtInTargetLanguageEntries = targetLanguageEntries,
                hasBuiltInTargetLanguage = targetLanguageEntries > 0
            };
        }

        private static int CountBuiltInTargetLanguageEntries(ModContentPack mod, string[] targetLanguageFolders)
        {
            if (mod == null) return 0;
            return CountBuiltInTargetLanguageEntries(mod.RootDir, targetLanguageFolders);
        }

        private static int CountBuiltInTargetLanguageEntries(string root, string[] targetLanguageFolders)
        {
            try
            {
                if (root.NullOrEmpty() || !Directory.Exists(root)) return 0;
                List<string> languageFiles;
                string stamp = BuiltInLanguageStamp(root, targetLanguageFolders, out languageFiles);
                string cacheKey = BuiltInLanguageCacheKey(root, targetLanguageFolders);
                lock (CacheGate)
                {
                    BuiltInLanguageCacheEntry cached;
                    if (builtInLanguageCache.TryGetValue(cacheKey, out cached) && cached.stamp == stamp)
                    {
                        return cached.count;
                    }
                }

                int count = 0;
                foreach (string file in languageFiles)
                {
                    count += CountLanguageDataEntries(file);
                    if (count > 0) break;
                }
                lock (CacheGate)
                {
                    builtInLanguageCache[cacheKey] = new BuiltInLanguageCacheEntry(stamp, count);
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        private static string BuiltInLanguageStamp(string root, string[] targetLanguageFolders, out List<string> languageFiles)
        {
            languageFiles = new List<string>();
            StringBuilder builder = new StringBuilder();
            foreach (string targetLanguageFolder in targetLanguageFolders ?? new string[0])
            {
                string safeFolder = LanguageTarget.SafeFolderName(targetLanguageFolder);
                if (safeFolder.NullOrEmpty()) continue;
                string languageRoot = Path.Combine(root, "Languages", safeFolder);
                builder.Append(SafeLower(safeFolder)).Append('=');
                if (!Directory.Exists(languageRoot))
                {
                    builder.Append("missing;");
                    continue;
                }

                foreach (string path in Directory.EnumerateFiles(languageRoot, "*.xml", SearchOption.AllDirectories).Where(p => !IsWordInfoPath(p)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    FileInfo info = new FileInfo(path);
                    languageFiles.Add(path);
                    builder.Append(path).Append(':').Append(info.LastWriteTimeUtc.Ticks).Append(':').Append(info.Length).Append(';');
                }
            }
            return builder.ToString();
        }

        private static string BuiltInLanguageCacheKey(string root, string[] targetLanguageFolders)
        {
            return SafeLower(root) + "|" + LanguageFoldersKey(targetLanguageFolders);
        }

        private static string LanguageFoldersKey(string[] targetLanguageFolders)
        {
            StringBuilder builder = new StringBuilder();
            foreach (string targetLanguageFolder in targetLanguageFolders ?? new string[0])
            {
                string safeFolder = LanguageTarget.SafeFolderName(targetLanguageFolder);
                if (!safeFolder.NullOrEmpty())
                {
                    builder.Append(SafeLower(safeFolder)).Append(';');
                }
            }
            return builder.ToString();
        }

        private static bool IsWordInfoPath(string path)
        {
            return path.IndexOf(Path.DirectorySeparatorChar + "WordInfo" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf(Path.AltDirectorySeparatorChar + "WordInfo" + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0;
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

        private class BuiltInLanguageCacheEntry
        {
            public readonly string stamp;
            public readonly int count;

            public BuiltInLanguageCacheEntry(string stamp, int count)
            {
                this.stamp = stamp;
                this.count = count;
            }
        }

        private class LocalModCacheEntry
        {
            public readonly string stamp;
            public readonly ActiveModInfo info;

            public LocalModCacheEntry(string stamp, ActiveModInfo info)
            {
                this.stamp = stamp;
                this.info = info;
            }
        }

        public static string SafeLower(string value)
        {
            return (value ?? "").Trim().ToLowerInvariant();
        }
    }
}
