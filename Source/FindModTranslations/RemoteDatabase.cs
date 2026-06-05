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
        private static readonly TimeSpan FailedRetryDelay = TimeSpan.FromSeconds(60);
        private static readonly object Gate = new object();
        private static string refreshingKey;
        private static string pendingKey;
        private static string pendingJson;
        private static string pendingUrl;
        private static string pendingLanguageFolder;
        private static readonly Dictionary<string, RemoteRequestState> requestStates = new Dictionary<string, RemoteRequestState>();

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
            EnsureStarted(requestedLanguage, false);
        }

        public static void ForceRefresh(string requestedLanguage)
        {
            EnsureStarted(requestedLanguage, true);
        }

        public static string LastError(string requestedLanguage)
        {
            string key = RequestKey(requestedLanguage);
            lock (Gate)
            {
                RemoteRequestState state;
                return !key.NullOrEmpty() && requestStates.TryGetValue(key, out state) ? state.lastError : "";
            }
        }

        private static void EnsureStarted(string requestedLanguage, bool force)
        {
            string key = RequestKey(requestedLanguage);
            if (key.NullOrEmpty())
            {
                return;
            }

            lock (Gate)
            {
                RemoteRequestState state = StateForKey(key);
                DateTime now = DateTime.UtcNow;
                if (!refreshingKey.NullOrEmpty())
                {
                    return;
                }
                if (!pendingKey.NullOrEmpty())
                {
                    if (pendingKey == key)
                    {
                        return;
                    }
                    ClearPendingLocked();
                }

                if (!force)
                {
                    if (state.succeeded || state.nextRetryUtc > now)
                    {
                        return;
                    }
                }
                else
                {
                    state.succeeded = false;
                    state.nextRetryUtc = DateTime.MinValue;
                    state.lastError = "";
                }

                state.lastAttemptUtc = now;
                refreshingKey = key;
            }

            ThreadPool.QueueUserWorkItem(_ => Download(requestedLanguage, key));
        }

        public static bool TryApply(string requestedLanguage, out TranslationDatabase database)
        {
            database = null;
            string key = RequestKey(requestedLanguage);
            if (key.NullOrEmpty())
            {
                return false;
            }
            string json;
            string url;
            string languageFolder;

            lock (Gate)
            {
                if (pendingKey != key || pendingJson.NullOrEmpty())
                {
                    if (!pendingKey.NullOrEmpty() && pendingKey != key)
                    {
                        ClearPendingLocked();
                    }
                    return false;
                }
                json = pendingJson;
                url = pendingUrl;
                languageFolder = pendingLanguageFolder;
                ClearPendingLocked();
            }

            try
            {
                TranslationDatabase loaded = TranslationDatabaseParser.Parse(json);
                loaded.PrepareLanguage(languageFolder, requestedLanguage, url);
                if (!loaded.SupportsLanguage(requestedLanguage))
                {
                    Log.Warning("[Find Mod Translations] Remote database does not support active language " + requestedLanguage + " from " + url + ".");
                    MarkFailure(key, "remote database does not support active language");
                    return false;
                }

                SaveCacheBestEffort(languageFolder, json);
                database = loaded;
                MarkSucceeded(key);
                Log.Message("[Find Mod Translations] Loaded remote database: " + loaded.ModCount + " mods, language " + loaded.LanguageDisplayName + ", version " + loaded.version + " from " + url + ".");
                return true;
            }
            catch (Exception ex)
            {
                MarkFailure(key, ex.Message);
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
                MarkFailureLocked(key, error.NullOrEmpty() ? "no compatible remote database found" : error);
            }
            Log.Warning("[Find Mod Translations] Could not download translation database for " + requestedLanguage + ": " + error);
        }

        private static void SaveCacheBestEffort(string languageFolder, string json)
        {
            try
            {
                SaveCacheAtomic(languageFolder, json);
            }
            catch (Exception ex)
            {
                Log.Warning("[Find Mod Translations] Could not save remote database cache: " + ex.Message);
            }
        }

        private static void SaveCacheAtomic(string languageFolder, string json)
        {
            string path = CachePath(languageFolder);
            if (path.NullOrEmpty())
            {
                return;
            }
            string directory = Path.GetDirectoryName(path);
            if (!directory.NullOrEmpty())
            {
                Directory.CreateDirectory(directory);
            }
            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tempPath, json);
                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, null);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
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

        private static RemoteRequestState StateForKey(string key)
        {
            RemoteRequestState state;
            if (!requestStates.TryGetValue(key, out state))
            {
                state = new RemoteRequestState();
                requestStates.Add(key, state);
            }
            return state;
        }

        private static void ClearPendingLocked()
        {
            pendingKey = null;
            pendingJson = null;
            pendingUrl = null;
            pendingLanguageFolder = null;
        }

        private static void MarkSucceeded(string key)
        {
            lock (Gate)
            {
                RemoteRequestState state = StateForKey(key);
                state.succeeded = true;
                state.lastError = "";
                state.nextRetryUtc = DateTime.MaxValue;
                if (refreshingKey == key)
                {
                    refreshingKey = null;
                }
            }
        }

        private static void MarkFailure(string key, string error)
        {
            lock (Gate)
            {
                MarkFailureLocked(key, error);
            }
        }

        private static void MarkFailureLocked(string key, string error)
        {
            RemoteRequestState state = StateForKey(key);
            state.succeeded = false;
            state.lastError = error ?? "";
            state.nextRetryUtc = DateTime.UtcNow + FailedRetryDelay;
            if (refreshingKey == key)
            {
                refreshingKey = null;
            }
        }

        private class RemoteRequestState
        {
            public bool succeeded;
            public DateTime lastAttemptUtc = DateTime.MinValue;
            public DateTime nextRetryUtc = DateTime.MinValue;
            public string lastError = "";
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
