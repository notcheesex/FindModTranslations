using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using Verse;

namespace FindModTranslations
{
    public static class RemoteDatabase
    {
        private const string UrlTemplate = "https://raw.githubusercontent.com/notcheesex/FindModTranslations-Database/main/{0}/translations.json";
        private const string UserAgent = "FindModTranslations/1.0";
        private const int DownloadTimeoutMs = 15000;
        private static readonly object Gate = new object();
        private static string refreshingKey;
        private static string pendingKey;
        private static string pendingJson;
        private static string pendingUrl;
        private static string pendingLanguageFolder;
        private static readonly HashSet<string> startedKeys = new HashSet<string>();

        public static bool IsRefreshing(string requestedLanguage)
        {
            string key = RequestKey(requestedLanguage);
            lock (Gate)
            {
                return !refreshingKey.NullOrEmpty() && refreshingKey == key;
            }
        }

        public static void EnsureStarted(string requestedLanguage)
        {
            string key = RequestKey(requestedLanguage);
            if (key.NullOrEmpty())
            {
                return;
            }

            lock (Gate)
            {
                if (startedKeys.Contains(key) || refreshingKey == key || pendingKey == key)
                {
                    return;
                }
                startedKeys.Add(key);
                refreshingKey = key;
            }

            ThreadPool.QueueUserWorkItem(_ => Download(requestedLanguage, key));
        }

        public static bool TryApply(string requestedLanguage, out TranslationDatabase database)
        {
            database = null;
            string key = RequestKey(requestedLanguage);
            string json;
            string url;
            string languageFolder;

            lock (Gate)
            {
                if (pendingKey != key || pendingJson.NullOrEmpty())
                {
                    return false;
                }
                json = pendingJson;
                url = pendingUrl;
                languageFolder = pendingLanguageFolder;
                pendingKey = null;
                pendingJson = null;
                pendingUrl = null;
                pendingLanguageFolder = null;
            }

            try
            {
                TranslationDatabase loaded = TranslationDatabaseParser.Parse(json);
                loaded.PrepareLanguage(languageFolder, requestedLanguage, url);
                if (!loaded.SupportsLanguage(requestedLanguage))
                {
                    Log.Warning("[Find Mod Translations] Remote database does not support active language " + requestedLanguage + " from " + url + ".");
                    return false;
                }

                SaveCache(languageFolder, json);
                database = loaded;
                Log.Message("[Find Mod Translations] Loaded remote database: " + loaded.ModCount + " mods, language " + loaded.LanguageDisplayName + ", version " + loaded.version + " from " + url + ".");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[Find Mod Translations] Could not apply remote translation database: " + ex);
                return false;
            }
        }

        public static string CacheRoot()
        {
            return Path.Combine(GenFilePaths.ConfigFolderPath, "FindModTranslations");
        }

        private static void Download(string requestedLanguage, string key)
        {
            string error = "";
            foreach (string folder in LanguageTarget.CandidateFolders(requestedLanguage))
            {
                string safeFolder = LanguageTarget.SafeFolderName(folder);
                if (safeFolder.NullOrEmpty())
                {
                    continue;
                }

                string url = string.Format(UrlTemplate, Uri.EscapeDataString(safeFolder));
                try
                {
                    using (TimeoutWebClient client = new TimeoutWebClient(DownloadTimeoutMs))
                    {
                        client.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                        string json = client.DownloadString(url);
                        if (json.NullOrEmpty())
                        {
                            error = "empty response from " + url;
                            continue;
                        }

                        lock (Gate)
                        {
                            pendingKey = key;
                            pendingJson = json;
                            pendingUrl = url;
                            pendingLanguageFolder = folder;
                            refreshingKey = null;
                        }
                        return;
                    }
                }
                catch (WebException ex)
                {
                    error = ex.Message;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }

            lock (Gate)
            {
                if (refreshingKey == key)
                {
                    refreshingKey = null;
                }
            }
            Log.Warning("[Find Mod Translations] Could not download translation database for " + requestedLanguage + ": " + error);
        }

        private static void SaveCache(string languageFolder, string json)
        {
            string path = CachePath(languageFolder);
            if (path.NullOrEmpty())
            {
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);
        }

        private static string CachePath(string languageFolder)
        {
            string token = LanguageTarget.SafeFileToken(languageFolder);
            if (token.NullOrEmpty())
            {
                return "";
            }
            return Path.Combine(CacheRoot(), "translations." + token + ".json");
        }

        private static string RequestKey(string requestedLanguage)
        {
            return LanguageTarget.CanonicalFolder(requestedLanguage);
        }

        private class TimeoutWebClient : WebClient
        {
            private readonly int timeoutMs;

            public TimeoutWebClient(int timeoutMs)
            {
                this.timeoutMs = timeoutMs;
            }

            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = base.GetWebRequest(address);
                if (request != null)
                {
                    request.Timeout = timeoutMs;
                }
                return request;
            }
        }
    }
}
