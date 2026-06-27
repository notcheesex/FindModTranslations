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
    public class TranslationMatch
    {
        public ActiveModInfo activeMod;
        public ModTranslationEntry entry;
        public bool translationActive;
        public bool translationInstalled;
        public TranslationModInfo activeAlternative;
        public TranslationModInfo installedAlternative;
    }

    public static class TranslationFinder
    {
        public static List<TranslationMatch> FindMatches(TranslationDatabase db, ActiveModIndex activeIndex, bool ignoreActiveAlternatives)
        {
            List<TranslationMatch> matches = new List<TranslationMatch>();
            if (db == null || db.mods == null || activeIndex == null)
            {
                return matches;
            }

            foreach (ModTranslationEntry entry in db.mods)
            {
                ActiveModInfo active = activeIndex.FindSourceMatch(entry);
                if (active == null || entry.translation == null || active.hasBuiltInTargetLanguage || IsIgnoredSource(active, entry))
                {
                    continue;
                }

                TranslationModInfo activeAlternative = null;
                TranslationModInfo installedAlternative = null;
                if (entry.alternatives != null)
                {
                    activeAlternative = entry.alternatives.FirstOrDefault(a => activeIndex.ContainsActiveTranslation(a));
                    installedAlternative = ignoreActiveAlternatives
                        ? entry.alternatives.FirstOrDefault(a => activeIndex.ContainsInactiveInstalledTranslation(a))
                        : entry.alternatives.FirstOrDefault(a => activeIndex.ContainsInstalledTranslation(a));
                }

                bool translationActive = activeIndex.ContainsActiveTranslation(entry.translation);
                if (translationActive || (activeAlternative != null && ignoreActiveAlternatives))
                {
                    continue;
                }

                matches.Add(new TranslationMatch
                {
                    activeMod = active,
                    entry = entry,
                    translationActive = translationActive,
                    translationInstalled = activeIndex.ContainsInstalledTranslation(entry.translation),
                    activeAlternative = activeAlternative,
                    installedAlternative = installedAlternative
                });
            }

            List<TranslationMatch> deduped = BestPerSource(matches);
            deduped.Sort(CompareMatches);
            return deduped;
        }

        private static List<TranslationMatch> BestPerSource(List<TranslationMatch> matches)
        {
            Dictionary<string, TranslationMatch> best = new Dictionary<string, TranslationMatch>();
            foreach (TranslationMatch match in matches)
            {
                string key = SourceIdentity(match);
                if (!best.TryGetValue(key, out TranslationMatch current) || CompareBest(match, current) < 0)
                {
                    best[key] = match;
                }
            }
            return new List<TranslationMatch>(best.Values);
        }

        private static int CompareBest(TranslationMatch a, TranslationMatch b)
        {
            int result = StatusSortRank(a).CompareTo(StatusSortRank(b));
            if (result != 0) return result;
            result = MatchQuality(b).CompareTo(MatchQuality(a));
            if (result != 0) return result;
            return CompareText(a == null || a.entry == null || a.entry.translation == null ? "" : a.entry.translation.name, b == null || b.entry == null || b.entry.translation == null ? "" : b.entry.translation.name);
        }

        private static int CompareMatches(TranslationMatch a, TranslationMatch b)
        {
            int result = StatusSortRank(a).CompareTo(StatusSortRank(b));
            if (result != 0) return result;
            result = CompareText(a == null || a.activeMod == null ? "" : a.activeMod.name, b == null || b.activeMod == null ? "" : b.activeMod.name);
            if (result != 0) return result;
            return CompareText(a == null || a.entry == null || a.entry.translation == null ? "" : a.entry.translation.name, b == null || b.entry == null || b.entry.translation == null ? "" : b.entry.translation.name);
        }

        private static int CompareText(string a, string b)
        {
            return string.Compare(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private static string SourceIdentity(TranslationMatch match)
        {
            if (match == null || match.activeMod == null) return "";
            if (!match.activeMod.packageId.NullOrEmpty()) return ActiveModIndex.SafeLower(match.activeMod.packageId);
            if (!match.activeMod.steamId.NullOrEmpty()) return "steam:" + match.activeMod.steamId;
            return ActiveModIndex.SafeLower(match.activeMod.name);
        }

        private static int MatchQuality(TranslationMatch match)
        {
            if (match == null) return 0;
            int quality = 0;
            if (match.activeMod != null && match.entry != null && !match.activeMod.steamId.NullOrEmpty() && match.activeMod.steamId == match.entry.steamId) quality += 100;
            if (match.entry != null && match.entry.translation != null && !match.entry.translation.name.NullOrEmpty() && match.activeMod != null && !match.activeMod.name.NullOrEmpty())
            {
                string trName = ActiveModIndex.SafeLower(match.entry.translation.name);
                foreach (string token in ActiveModIndex.SafeLower(match.activeMod.name).Split(new[] { ' ', '-', '_', ':', '[', ']' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (token.Length > 2 && trName.Contains(token)) quality += 5;
                }
            }
            return quality;
        }

        private static bool IsIgnoredSource(ActiveModInfo active, ModTranslationEntry entry)
        {
            string packageId = ActiveModIndex.SafeLower(active == null ? null : active.packageId);
            if (packageId == "brrainz.harmony") return true;
            if (packageId == "ludeon.rimworld" || packageId.StartsWith("ludeon.rimworld.")) return true;

            packageId = ActiveModIndex.SafeLower(entry == null ? null : entry.packageId);
            if (packageId == "brrainz.harmony") return true;
            if (packageId == "ludeon.rimworld" || packageId.StartsWith("ludeon.rimworld.")) return true;

            string steamId = entry == null ? "" : entry.steamId;
            return steamId == "294100" || steamId == "1149640" || steamId == "1392840" || steamId == "1826140" || steamId == "2380740";
        }

        private static int StatusSortRank(TranslationMatch match)
        {
            if (match == null) return 99;
            if (match.activeAlternative != null) return 3;
            if (!match.translationInstalled && match.installedAlternative == null) return 0;
            if (match.translationInstalled) return 1;
            if (match.installedAlternative != null) return 2;
            return 4;
        }

    }
}
