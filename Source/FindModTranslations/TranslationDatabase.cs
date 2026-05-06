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
    public static class TranslationDatabaseParser
    {
        public static TranslationDatabase Parse(string json)
        {
            try
            {
                TranslationDatabase db = JsonUtility.FromJson<TranslationDatabase>(json);
                Normalize(db);
                if (db != null && db.mods != null && db.mods.Length > 0)
                {
                    return db;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Find Mod Translations] Unity JSON parser failed; falling back to legacy parser: " + ex.Message);
            }

            TranslationDatabase fallback = ParseLegacy(json);
            Normalize(fallback);
            return fallback;
        }

        private static void Normalize(TranslationDatabase db)
        {
            if (db == null) return;
            if (db.mods == null) db.mods = new ModTranslationEntry[0];
            if (db.collections == null) db.collections = new TranslationCollection[0];
            foreach (ModTranslationEntry entry in db.mods)
            {
                if (entry == null) continue;
                entry.author = FirstNonEmpty(entry.authors, entry.author);
                if (entry.gameVersions == null) entry.gameVersions = new string[0];
                if (entry.alternatives == null) entry.alternatives = new TranslationModInfo[0];
                Normalize(entry.translation);
                foreach (TranslationModInfo alternative in entry.alternatives)
                {
                    Normalize(alternative);
                }
            }
        }

        private static void Normalize(TranslationModInfo info)
        {
            if (info == null) return;
            info.author = FirstNonEmpty(info.authors, info.author);
            if (info.gameVersions == null) info.gameVersions = new string[0];
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!value.NullOrEmpty()) return value;
            }
            return "";
        }

        private static TranslationDatabase ParseLegacy(string json)
        {
            TranslationDatabase db = new TranslationDatabase();
            db.version = IntValue(json, "version", 1);
            db.updatedAt = StringValue(json, "updatedAt");
            db.language = FirstStringValue(json, "language");
            db.aliases = StringArray(json, "aliases");

            string modsArray = ArrayBody(json, "mods");
            List<ModTranslationEntry> mods = new List<ModTranslationEntry>();
            foreach (string modObject in ObjectBodies(modsArray))
            {
                ModTranslationEntry entry = new ModTranslationEntry
                {
                    name = StringValue(modObject, "name"),
                    packageId = StringValue(modObject, "packageId"),
                    steamId = StringValue(modObject, "steamId"),
                    author = FirstStringValue(modObject, "authors", "author"),
                    gameVersions = StringArray(modObject, "gameVersions"),
                    alternatives = new TranslationModInfo[0]
                };

                string translationObject = ObjectBodyForKey(modObject, "translation");
                if (!translationObject.NullOrEmpty())
                {
                    entry.translation = ParseTranslation(translationObject);
                }

                string alternativesArray = ArrayBody(modObject, "alternatives");
                if (!alternativesArray.NullOrEmpty())
                {
                    entry.alternatives = ObjectBodies(alternativesArray).Select(ParseTranslation).ToArray();
                }

                if (entry.translation != null)
                {
                    mods.Add(entry);
                }
            }
            db.mods = mods.ToArray();
            return db;
        }

        private static TranslationModInfo ParseTranslation(string obj)
        {
            return new TranslationModInfo
            {
                name = StringValue(obj, "name"),
                packageId = StringValue(obj, "packageId"),
                steamId = StringValue(obj, "steamId"),
                author = FirstStringValue(obj, "authors", "author"),
                gameVersions = StringArray(obj, "gameVersions"),
                url = StringValue(obj, "url"),
                notes = StringValue(obj, "notes")
            };
        }

        private static int IntValue(string json, string key, int fallback)
        {
            Match match = Regex.Match(json, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*(\\d+)");
            return match.Success && int.TryParse(match.Groups[1].Value, out int value) ? value : fallback;
        }

        private static string FirstStringValue(string json, params string[] keys)
        {
            foreach (string key in keys)
            {
                string value = StringValue(json, key);
                if (!value.NullOrEmpty()) return value;
            }
            return "";
        }

        private static string StringValue(string json, string key)
        {
            Match match = Regex.Match(json, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"])*)\\\"");
            return match.Success ? Regex.Unescape(match.Groups[1].Value) : "";
        }

        private static string[] StringArray(string json, string key)
        {
            string body = ArrayBody(json, key);
            if (body.NullOrEmpty()) return new string[0];
            List<string> values = new List<string>();
            foreach (Match match in Regex.Matches(body, "\\\"((?:\\\\.|[^\\\"])*)\\\""))
            {
                values.Add(Regex.Unescape(match.Groups[1].Value));
            }
            return values.ToArray();
        }

        private static string ArrayBody(string json, string key)
        {
            int keyIndex = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (keyIndex < 0) return "";
            int start = json.IndexOf('[', keyIndex);
            if (start < 0) return "";
            int end = Matching(json, start, '[', ']');
            return end > start ? json.Substring(start + 1, end - start - 1) : "";
        }

        private static string ObjectBodyForKey(string json, string key)
        {
            int keyIndex = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (keyIndex < 0) return "";
            int start = json.IndexOf('{', keyIndex);
            if (start < 0) return "";
            int end = Matching(json, start, '{', '}');
            return end > start ? json.Substring(start + 1, end - start - 1) : "";
        }

        private static IEnumerable<string> ObjectBodies(string arrayBody)
        {
            int index = 0;
            while (index < arrayBody.Length)
            {
                int start = arrayBody.IndexOf('{', index);
                if (start < 0) yield break;
                int end = Matching(arrayBody, start, '{', '}');
                if (end <= start) yield break;
                yield return arrayBody.Substring(start + 1, end - start - 1);
                index = end + 1;
            }
        }

        private static int Matching(string text, int start, char open, char close)
        {
            bool inString = false;
            bool escape = false;
            int depth = 0;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (escape)
                {
                    escape = false;
                    continue;
                }
                if (c == '\\' && inString)
                {
                    escape = true;
                    continue;
                }
                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }
                if (inString) continue;
                if (c == open) depth++;
                if (c == close)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }
    }


    [Serializable]
    public class TranslationDatabase
    {
        public int version = 1;
        public string updatedAt;
        public string language;
        public string[] aliases = new string[0];
        public ModTranslationEntry[] mods = new ModTranslationEntry[0];
        public TranslationCollection[] collections = new TranslationCollection[0];
        [NonSerialized] public string requestedLanguageFolder;
        [NonSerialized] public string loadedFrom;
        [NonSerialized] public bool unavailableForRequestedLanguage;

        public int ModCount => mods == null ? 0 : mods.Length;
        public string LanguageDisplayName => !language.NullOrEmpty() ? language : requestedLanguageFolder;

        public static TranslationDatabase Load(ModContentPack content)
        {
            string requestedLanguage = LanguageTarget.CurrentFolder();
            string dataRoot = Path.Combine(content.RootDir, "Data");
            string cacheRoot = RemoteDatabase.CacheRoot();
            List<string> skippedLanguages = new List<string>();
            foreach (DatabaseCandidate candidate in DatabaseCandidates(cacheRoot, requestedLanguage).Concat(DatabaseCandidates(dataRoot, requestedLanguage)))
            {
                if (!File.Exists(candidate.path))
                {
                    continue;
                }

                try
                {
                    string json = File.ReadAllText(candidate.path);
                    TranslationDatabase db = TranslationDatabaseParser.Parse(json);
                    db.PrepareLanguage(candidate.inferredLanguageFolder, requestedLanguage, candidate.path);
                    if (!db.SupportsLanguage(requestedLanguage))
                    {
                        skippedLanguages.Add(db.LanguageDisplayName);
                        continue;
                    }

                    Log.Message("[Find Mod Translations] Loaded database: " + db.ModCount + " mods, language " + db.LanguageDisplayName + ", version " + db.version + " from " + Path.GetFileName(candidate.path) + ".");
                    return db;
                }
                catch (Exception ex)
                {
                    Log.Error("[Find Mod Translations] Could not read translation database " + candidate.path + ": " + ex);
                }
            }

            TranslationDatabase empty = EmptyForLanguage(requestedLanguage);
            string skipped = skippedLanguages.Count == 0 ? "" : " Skipped databases for: " + string.Join(", ", skippedLanguages.ToArray()) + ".";
            Log.Message("[Find Mod Translations] No translation database for active language " + empty.LanguageDisplayName + "." + skipped);
            return empty;
        }

        public bool SupportsLanguage(string folder)
        {
            if (folder.NullOrEmpty())
            {
                return false;
            }
            if (LanguageTarget.EquivalentFolder(language, folder))
            {
                return true;
            }
            foreach (string supported in AllAliases())
            {
                if (LanguageTarget.EquivalentFolder(supported, folder))
                {
                    return true;
                }
            }
            return false;
        }

        public string[] EffectiveLanguageFolders()
        {
            List<string> folders = new List<string>();
            AddFolderAndCandidates(folders, requestedLanguageFolder);
            AddFolderAndCandidates(folders, language);
            foreach (string folder in AllAliases())
            {
                AddFolderAndCandidates(folders, folder);
            }
            return folders.ToArray();
        }

        private IEnumerable<string> AllAliases()
        {
            if (aliases != null)
            {
                foreach (string alias in aliases)
                {
                    yield return alias;
                }
            }
        }

        internal void PrepareLanguage(string inferredLanguageFolder, string requestedLanguage, string path)
        {
            if (aliases == null) aliases = new string[0];
            if (language.NullOrEmpty())
            {
                language = inferredLanguageFolder.NullOrEmpty() ? LanguageTarget.LegacyDatabaseLanguageFolder : inferredLanguageFolder;
            }
            requestedLanguageFolder = requestedLanguage;
            loadedFrom = path;
        }

        private static TranslationDatabase EmptyForLanguage(string requestedLanguage)
        {
            TranslationDatabase db = new TranslationDatabase();
            db.language = requestedLanguage;
            db.requestedLanguageFolder = requestedLanguage;
            db.unavailableForRequestedLanguage = true;
            db.mods = new ModTranslationEntry[0];
            db.collections = new TranslationCollection[0];
            return db;
        }

        private static IEnumerable<DatabaseCandidate> DatabaseCandidates(string dataRoot, string requestedLanguage)
        {
            foreach (string folder in LanguageTarget.CandidateFolders(requestedLanguage))
            {
                string token = LanguageTarget.SafeFileToken(folder);
                string safeFolder = LanguageTarget.SafeFolderName(folder);
                if (!token.NullOrEmpty())
                {
                    yield return new DatabaseCandidate(Path.Combine(dataRoot, "translations." + token + ".json"), folder);
                }
                if (!safeFolder.NullOrEmpty())
                {
                    yield return new DatabaseCandidate(Path.Combine(dataRoot, safeFolder, "translations.json"), folder);
                }
            }
            yield return new DatabaseCandidate(Path.Combine(dataRoot, "translations.json"), "");
        }

        private static void AddFolderAndCandidates(List<string> folders, string folder)
        {
            foreach (string candidate in LanguageTarget.CandidateFolders(folder))
            {
                LanguageTarget.AddUnique(folders, candidate);
            }
        }

        private class DatabaseCandidate
        {
            public readonly string path;
            public readonly string inferredLanguageFolder;

            public DatabaseCandidate(string path, string inferredLanguageFolder)
            {
                this.path = path;
                this.inferredLanguageFolder = inferredLanguageFolder;
            }
        }
    }

    [Serializable]
    public class ModTranslationEntry
    {
        public string name;
        public string packageId;
        public string steamId;
        public string authors;
        public string author;
        public string[] gameVersions = new string[0];
        public TranslationModInfo translation;
        public TranslationModInfo[] alternatives = new TranslationModInfo[0];
        public string notes;
    }

    [Serializable]
    public class TranslationModInfo
    {
        public string name;
        public string packageId;
        public string steamId;
        public string authors;
        public string author;
        public string[] gameVersions = new string[0];
        public string url;
        public string notes;
    }

    [Serializable]
    public class TranslationCollection
    {
        public string name;
        public string steamId;
        public string url;
        public string notes;
        public int itemCount;
        public TranslationModInfo[] items = new TranslationModInfo[0];
    }
}
