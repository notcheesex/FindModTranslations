using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace FindModTranslations
{
    [StaticConstructorOnStartup]
    public static class FindModTranslationsStartup
    {
        static FindModTranslationsStartup()
        {
            new Harmony("cheesex.findmodtranslations").PatchAll();
        }
    }

    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
    public static class MainMenuDrawer_MainMenuOnGUI_Patch
    {
        public static void Postfix()
        {
            FindModTranslationsMod.TryAutoShowFromMainMenu();
        }
    }


    public class FindModTranslationsMod : Mod
    {
        public static FindModTranslationsMod Instance;
        public static FindModTranslationsSettings Settings;
        public static TranslationDatabase Database;
        private static bool autoShowAttempted;
        private static MatchCache matchCache;

        public FindModTranslationsMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<FindModTranslationsSettings>();
            Database = TranslationDatabase.Load(content);
            RemoteDatabase.EnsureStarted(LanguageTarget.CurrentFolder());
        }

        public override string SettingsCategory()
        {
            return "FMT_Window_Title".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.CheckboxLabeled("FMT_Settings_ShowOnStartup".Translate(), ref Settings.showOnStartup);
            listing.CheckboxLabeled("FMT_Settings_OnlyShowWhenFound".Translate(), ref Settings.onlyShowWhenFound);
            listing.CheckboxLabeled("FMT_Settings_IgnoreActiveAlternatives".Translate(), ref Settings.ignoreActiveAlternatives);
            listing.Gap(8f);
            if (listing.ButtonText("FMT_Settings_CheckNow".Translate()))
            {
                RemoteDatabase.ForceRefresh(LanguageTarget.CurrentFolder());
                ShowWindow(autoOpened: false);
            }
            listing.Gap(8f);
            listing.Label(DatabaseStatusText());
            listing.End();
        }


        private static string DatabaseStatusText()
        {
            string currentLanguage = LanguageTarget.CurrentFolder();
            TranslationDatabase db = DatabaseForCurrentLanguage();
            bool updating = RemoteDatabase.IsRefreshing(currentLanguage);
            string remoteError = RemoteDatabase.LastError(currentLanguage);
            if (db == null)
            {
                string text = updating ? "FMT_Settings_DatabaseUpdating".Translate().ToString() : "FMT_Settings_DatabaseNotLoaded".Translate().ToString();
                return DatabaseStatusWithRemoteError(text, remoteError, updating);
            }
            if (db.unavailableForRequestedLanguage)
            {
                string text = updating ? "FMT_Settings_DatabaseUpdating".Translate().ToString() : "FMT_Settings_DatabaseUnavailableForLanguage".Translate(db.LanguageDisplayName).ToString();
                return DatabaseStatusWithRemoteError(text, remoteError, updating);
            }
            string updatedAt = db.updatedAt.NullOrEmpty() ? "FMT_Settings_DatabaseDateUnknown".Translate().ToString() : db.updatedAt;
            return DatabaseStatusWithRemoteError((updating ? "FMT_Settings_DatabaseLoadedUpdating" : "FMT_Settings_DatabaseLoaded").Translate(db.ModCount, updatedAt).ToString(), remoteError, updating);
        }

        private static string DatabaseStatusWithRemoteError(string text, string remoteError, bool updating)
        {
            if (updating || remoteError.NullOrEmpty())
            {
                return text;
            }
            return text + "\n" + "FMT_Settings_DatabaseRemoteError".Translate(remoteError);
        }

        public static void TryAutoShowFromMainMenu()
        {
            if (autoShowAttempted || Instance == null || Settings == null)
            {
                return;
            }

            if (!Settings.showOnStartup || Find.WindowStack == null)
            {
                return;
            }

            TranslationDatabase db = DatabaseForCurrentLanguage();
            if ((db == null || db.unavailableForRequestedLanguage || db.ModCount == 0) && RemoteDatabase.IsRefreshing(LanguageTarget.CurrentFolder()))
            {
                return;
            }

            autoShowAttempted = true;
            ShowWindow(autoOpened: true);
        }

        public static void ShowWindow(bool autoOpened)
        {
            TranslationDatabase db = DatabaseForCurrentLanguage();
            List<TranslationMatch> matches = new List<TranslationMatch>();
            if (db != null && !db.unavailableForRequestedLanguage && db.ModCount > 0)
            {
                ActiveModIndex active = ActiveModIndex.Create(db.EffectiveLanguageFolders());
                matches = CachedMatches(db, active, Settings.ignoreActiveAlternatives);
                int builtInTargetLanguage = active.mods.Count(m => m.hasBuiltInTargetLanguage);
                int builtInTargetLanguageEntries = active.mods.Sum(m => m.builtInTargetLanguageEntries);
                Log.Message("[Find Mod Translations] Active mods: " + active.mods.Count + ", built-in " + db.LanguageDisplayName + " ignored: " + builtInTargetLanguage + " (" + builtInTargetLanguageEntries + " entries), installed mods: " + active.installedMods.Count + ", database mods: " + db.ModCount + ", matches: " + matches.Count + ".");
            }
            else
            {
                Log.Message("[Find Mod Translations] No database loaded for active language " + (db == null ? LanguageTarget.CurrentFolder() : db.LanguageDisplayName) + ".");
            }
            if (autoOpened && Settings.onlyShowWhenFound && matches.Count == 0)
            {
                return;
            }
            Find.WindowStack.Add(new TranslationFinderWindow(matches, db));
        }

        private static List<TranslationMatch> CachedMatches(TranslationDatabase db, ActiveModIndex active, bool ignoreActiveAlternatives)
        {
            string databaseKey = (db == null ? 0 : db.version) + ":" + (db == null ? "" : db.updatedAt) + ":" + (db == null ? "" : db.requestedLanguageFolder) + ":" + (db == null ? "" : db.language) + ":" + (db == null ? "" : db.loadedFrom) + ":" + (db == null ? 0 : db.ModCount);
            string activeKey = active == null ? "" : active.Signature;
            if (matchCache != null && matchCache.databaseKey == databaseKey && matchCache.activeKey == activeKey && matchCache.ignoreActiveAlternatives == ignoreActiveAlternatives)
            {
                return matchCache.matches;
            }

            List<TranslationMatch> matches = TranslationFinder.FindMatches(db, active, ignoreActiveAlternatives);
            matchCache = new MatchCache
            {
                databaseKey = databaseKey,
                activeKey = activeKey,
                ignoreActiveAlternatives = ignoreActiveAlternatives,
                matches = matches
            };
            return matches;
        }

        private static TranslationDatabase DatabaseForCurrentLanguage()
        {
            if (Instance == null)
            {
                return Database;
            }

            string currentLanguage = LanguageTarget.CurrentFolder();
            RemoteDatabase.EnsureStarted(currentLanguage);
            if (RemoteDatabase.TryApply(currentLanguage, out TranslationDatabase remoteDb))
            {
                Database = remoteDb;
                matchCache = null;
                return Database;
            }

            if (Database == null || !LanguageTarget.SameFolder(Database.requestedLanguageFolder, currentLanguage))
            {
                Database = TranslationDatabase.Load(Instance.Content);
                matchCache = null;
                RemoteDatabase.EnsureStarted(currentLanguage);
            }
            return Database;
        }

        private class MatchCache
        {
            public string databaseKey;
            public string activeKey;
            public bool ignoreActiveAlternatives;
            public List<TranslationMatch> matches;
        }
    }

    public class FindModTranslationsSettings : ModSettings
    {
        public bool showOnStartup = true;
        public bool onlyShowWhenFound = true;
        public bool ignoreActiveAlternatives = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref showOnStartup, "showOnStartup", true);
            Scribe_Values.Look(ref onlyShowWhenFound, "onlyShowWhenFound", true);
            Scribe_Values.Look(ref ignoreActiveAlternatives, "ignoreActiveAlternatives", true);
        }
    }



}
