using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;

namespace FindModTranslations
{
    public class ContributionWizardWindow : Window
    {
        private const string DatabaseGithubUrl = "https://github.com/notcheesex/FindModTranslations-Database";
        private const float RowHeight = 264f;

        private readonly List<ContributionDraft> drafts;
        private readonly string languageDisplayName;
        private readonly string[] languageFolders;
        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(940f, 720f);

        public ContributionWizardWindow(TranslationDatabase database)
        {
            languageDisplayName = database == null ? LanguageTarget.CurrentFolder() : database.LanguageDisplayName;
            languageFolders = database == null ? LanguageTarget.CandidateFolders(LanguageTarget.CurrentFolder()) : database.EffectiveLanguageFolders();
            drafts = BuildDrafts(ActiveModIndex.Create(languageFolders));
            if (drafts.Count == 0)
            {
                drafts.Add(ManualDraft());
            }

            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "FMT_Contribution_Title".Translate());
            Text.Font = GameFont.Small;

            Widgets.Label(new Rect(inRect.x, inRect.y + 38f, inRect.width, 50f), "FMT_Contribution_Intro".Translate(languageDisplayName));
            Widgets.Label(new Rect(inRect.x, inRect.y + 90f, inRect.width, 24f), "FMT_Contribution_Detected".Translate(drafts.Count, UsableDrafts().Count));

            Rect listOuter = new Rect(inRect.x, inRect.y + 118f, inRect.width, inRect.height - 206f);
            float viewHeight = Math.Max(listOuter.height - 20f, drafts.Count * RowHeight + 8f);
            Rect view = new Rect(0f, 0f, listOuter.width - 18f, viewHeight);
            Widgets.BeginScrollView(listOuter, ref scroll, view);

            float y = 0f;
            float visibleTop = scroll.y - 40f;
            float visibleBottom = scroll.y + listOuter.height + 40f;
            for (int i = 0; i < drafts.Count; i++)
            {
                DrawDraft(view, ref y, drafts[i], i, visibleTop, visibleBottom);
            }

            Widgets.EndScrollView();

            Rect helpRect = new Rect(inRect.x, inRect.yMax - 80f, inRect.width, 36f);
            Widgets.Label(helpRect, "FMT_Contribution_SubmitHelp".Translate());

            Rect buttons = new Rect(inRect.x, inRect.yMax - 38f, inRect.width, 34f);
            if (Widgets.ButtonText(new Rect(buttons.x, buttons.y, 142f, 34f), "FMT_Contribution_AddManual".Translate()))
            {
                drafts.Add(ManualDraft());
            }
            if (Widgets.ButtonText(new Rect(buttons.x + 150f, buttons.y, 128f, 34f), "FMT_Contribution_CopyJson".Translate()))
            {
                CopyJson();
            }
            if (Widgets.ButtonText(new Rect(buttons.x + 286f, buttons.y, 146f, 34f), "FMT_Contribution_OpenGithub".Translate()))
            {
                OpenGithub();
            }
            if (Widgets.ButtonText(new Rect(buttons.xMax - 118f, buttons.y, 118f, 34f), "FMT_Window_Close".Translate()))
            {
                Close();
            }
        }

        private void DrawDraft(Rect view, ref float y, ContributionDraft draft, int index, float visibleTop, float visibleBottom)
        {
            Rect box = new Rect(0f, y, view.width, RowHeight - 8f);
            if (box.yMax < visibleTop || box.y > visibleBottom)
            {
                y += RowHeight;
                return;
            }

            Color saved = GUI.color;
            GUI.color = index % 2 == 0 ? new Color(0.16f, 0.18f, 0.21f, 0.90f) : new Color(0.13f, 0.15f, 0.18f, 0.90f);
            Widgets.DrawBoxSolid(box, GUI.color);
            bool sourceReady = HasSourceIdentity(draft);
            bool translationLinkReady = HasTranslationLink(draft);
            bool ready = draft.included && sourceReady && translationLinkReady;
            GUI.color = ready ? new Color(0.45f, 0.75f, 0.48f, 0.80f) : draft.included ? new Color(0.88f, 0.58f, 0.28f, 0.85f) : new Color(0.50f, 0.50f, 0.50f, 0.65f);
            Widgets.DrawBox(box, 1);
            GUI.color = saved;

            float left = box.x + 12f;
            float top = box.y + 8f;
            float contentWidth = box.width - 24f;
            float includeWidth = 104f;

            GUI.color = new Color(0.72f, 0.78f, 0.86f, 1f);
            DrawClippedLabel(new Rect(left, top + 2f, contentWidth - includeWidth - 12f, 22f), draft.reason);
            GUI.color = saved;

            if (Widgets.ButtonText(new Rect(box.xMax - includeWidth - 10f, top, includeWidth, 26f), draft.included ? "FMT_Contribution_Included".Translate() : "FMT_Contribution_Skipped".Translate()))
            {
                draft.included = !draft.included;
            }

            Rect statusRect = new Rect(left, top + 30f, contentWidth, 22f);
            if (draft.included && !sourceReady)
            {
                GUI.color = new Color(1f, 0.62f, 0.38f, 1f);
                Widgets.Label(statusRect, "FMT_Contribution_MissingSourceIdentity".Translate());
                GUI.color = saved;
            }
            else if (draft.included && !translationLinkReady)
            {
                GUI.color = new Color(1f, 0.62f, 0.38f, 1f);
                Widgets.Label(statusRect, "FMT_Contribution_MissingTranslationLink".Translate());
                GUI.color = saved;
            }
            else
            {
                GUI.color = new Color(0.62f, 0.68f, 0.76f, 1f);
                Widgets.Label(statusRect, "FMT_Contribution_TranslationLinkHint".Translate());
                GUI.color = saved;
            }

            Widgets.Label(new Rect(left, top + 58f, contentWidth, 22f), "FMT_Contribution_Source".Translate());
            float fieldY = top + 80f;
            float nameWidth = 286f;
            float packageWidth = 286f;
            float steamWidth = 142f;
            float versionsWidth = Math.Max(118f, contentWidth - nameWidth - packageWidth - steamWidth - 24f);
            draft.sourceName = DrawField(new Rect(left, fieldY, nameWidth, 44f), "FMT_Contribution_SourceName".Translate(), draft.sourceName);
            draft.sourcePackageId = DrawField(new Rect(left + nameWidth + 8f, fieldY, packageWidth, 44f), "FMT_Contribution_SourcePackageId".Translate(), draft.sourcePackageId);
            draft.sourceSteamId = DrawField(new Rect(left + nameWidth + packageWidth + 16f, fieldY, steamWidth, 44f), "FMT_Contribution_SourceSteamId".Translate(), draft.sourceSteamId);
            draft.sourceGameVersions = DrawField(new Rect(left + nameWidth + packageWidth + steamWidth + 24f, fieldY, versionsWidth, 44f), "FMT_Contribution_SourceGameVersions".Translate(), draft.sourceGameVersions);

            Widgets.Label(new Rect(left, top + 128f, contentWidth, 22f), "FMT_Contribution_Translation".Translate());
            fieldY = top + 150f;
            float translationNameWidth = 354f;
            float translationPackageWidth = Math.Max(240f, contentWidth - translationNameWidth - 8f);
            float linkWidth = Math.Max(260f, contentWidth - steamWidth - 8f);
            draft.translationName = DrawField(new Rect(left, fieldY, translationNameWidth, 44f), "FMT_Contribution_TranslationName".Translate(), draft.translationName);
            draft.translationPackageId = DrawField(new Rect(left + translationNameWidth + 8f, fieldY, translationPackageWidth, 44f), "FMT_Contribution_TranslationPackageId".Translate(), draft.translationPackageId);
            fieldY += 48f;
            draft.translationSteamId = DrawField(new Rect(left, fieldY, steamWidth, 44f), "FMT_Contribution_TranslationSteamId".Translate(), draft.translationSteamId);
            draft.translationUrl = DrawField(new Rect(left + steamWidth + 8f, fieldY, linkWidth, 44f), "FMT_Contribution_TranslationUrl".Translate(), draft.translationUrl);

            y += RowHeight;
        }

        private static string DrawField(Rect rect, string label, string value)
        {
            GUI.color = new Color(0.62f, 0.68f, 0.76f, 1f);
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 18f), label);
            GUI.color = Color.white;
            string result = Widgets.TextField(new Rect(rect.x, rect.y + 19f, rect.width, 24f), value ?? "");
            GUI.color = Color.white;
            return result;
        }

        private static void DrawClippedLabel(Rect rect, string text)
        {
            if (text.NullOrEmpty())
            {
                return;
            }

            string value = text;
            while (value.Length > 3 && Text.CalcSize(value).x > rect.width)
            {
                value = value.Substring(0, value.Length - 2);
            }
            if (value != text)
            {
                value = value.TrimEnd() + "…";
            }
            Widgets.Label(rect, value);
            if (value != text)
            {
                TooltipHandler.TipRegion(rect, text);
            }
        }

        private void CopyJson()
        {
            string json = BuildJsonSnippet();
            if (json.NullOrEmpty())
            {
                Messages.Message("FMT_Contribution_NoJson".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            GUIUtility.systemCopyBuffer = json;
            Messages.Message("FMT_Contribution_JsonCopied".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }

        private void OpenGithub()
        {
            string json = BuildJsonSnippet();
            if (!json.NullOrEmpty())
            {
                GUIUtility.systemCopyBuffer = json;
            }
            Application.OpenURL(DatabaseGithubUrl);
            Messages.Message((json.NullOrEmpty() ? "FMT_Contribution_GithubOpened" : "FMT_Contribution_GithubOpenedCopied").Translate(), MessageTypeDefOf.TaskCompletion, false);
        }

        private string BuildJsonSnippet()
        {
            List<string> objects = new List<string>();
            foreach (ContributionDraft draft in UsableDrafts())
            {
                objects.Add(JsonObject(draft));
            }
            return string.Join(",\n", objects.ToArray());
        }

        private List<ContributionDraft> UsableDrafts()
        {
            return drafts.Where(d => d.included && HasSourceIdentity(d) && HasTranslationIdentity(d)).ToList();
        }

        private static bool HasSourceIdentity(ContributionDraft draft)
        {
            return draft != null && (!Trimmed(draft.sourceName).NullOrEmpty() || !ActiveModIndex.SafeLower(draft.sourcePackageId).NullOrEmpty() || !DigitsOnly(draft.sourceSteamId).NullOrEmpty());
        }

        private static bool HasTranslationIdentity(ContributionDraft draft)
        {
            return HasTranslationLink(draft);
        }

        private static bool HasTranslationLink(ContributionDraft draft)
        {
            return draft != null && !SafeContributionUrl(draft.translationUrl, draft.translationSteamId).NullOrEmpty();
        }

        private static string JsonObject(ContributionDraft draft)
        {
            List<string> sourceProperties = new List<string>();
            AddStringProperty(sourceProperties, "name", draft.sourceName);
            AddStringProperty(sourceProperties, "packageId", ActiveModIndex.SafeLower(draft.sourcePackageId));
            AddStringProperty(sourceProperties, "steamId", DigitsOnly(draft.sourceSteamId));
            string[] gameVersions = SplitVersions(draft.sourceGameVersions);
            if (gameVersions.Length > 0)
            {
                sourceProperties.Add("\"gameVersions\": [" + string.Join(", ", gameVersions.Select(JsonString).ToArray()) + "]");
            }
            sourceProperties.Add("\"translation\": " + JsonTranslationObject(draft));
            return "{\n  " + string.Join(",\n  ", sourceProperties.ToArray()) + "\n}";
        }

        private static string JsonTranslationObject(ContributionDraft draft)
        {
            List<string> properties = new List<string>();
            AddStringProperty(properties, "name", draft.translationName);
            AddStringProperty(properties, "packageId", ActiveModIndex.SafeLower(draft.translationPackageId));
            AddStringProperty(properties, "steamId", DigitsOnly(draft.translationSteamId));
            AddStringProperty(properties, "url", SafeContributionUrl(draft.translationUrl, draft.translationSteamId));
            return "{\n    " + string.Join(",\n    ", properties.ToArray()) + "\n  }";
        }

        private static void AddStringProperty(List<string> properties, string name, string value)
        {
            string trimmed = Trimmed(value);
            if (trimmed.NullOrEmpty())
            {
                return;
            }
            properties.Add(JsonString(name) + ": " + JsonString(trimmed));
        }

        private static string SafeContributionUrl(string url, string steamId)
        {
            string sanitized = Trimmed(url);
            if (SteamUrlTools.IsSafeWorkshopUrl(sanitized))
            {
                return sanitized;
            }

            string id = DigitsOnly(steamId);
            if (!id.NullOrEmpty())
            {
                return "https://steamcommunity.com/sharedfiles/filedetails/?id=" + id;
            }
            return "";
        }

        private static string Trimmed(string value)
        {
            return (value ?? "").Trim();
        }

        private static string[] SplitVersions(string text)
        {
            if (text.NullOrEmpty())
            {
                return new string[0];
            }

            List<string> versions = new List<string>();
            foreach (string part in text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string version = part.Trim();
                if (!version.NullOrEmpty() && !versions.Contains(version))
                {
                    versions.Add(version);
                }
            }
            return versions.ToArray();
        }

        private static string JsonString(string value)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append('"');
            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < 32)
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }

        private List<ContributionDraft> BuildDrafts(ActiveModIndex activeIndex)
        {
            List<ContributionCandidate> candidates = AllModCandidates(activeIndex);
            List<ContributionCandidate> sourceCandidates = candidates.Where(c => !IsIgnoredMod(c.info)).ToList();
            List<ContributionDraft> result = new List<ContributionDraft>();
            HashSet<string> seen = new HashSet<string>();

            foreach (ContributionCandidate candidate in candidates)
            {
                ActiveModInfo info = candidate.info;
                if (info == null || IsIgnoredMod(info))
                {
                    continue;
                }

                string identity = CandidateIdentity(info);
                if (!seen.Add(identity))
                {
                    continue;
                }

                int languageEntries = candidate.active ? info.builtInTargetLanguageEntries : ActiveModIndex.CountTargetLanguageEntries(info, languageFolders);
                bool looksLikeTranslation = LooksLikeTranslation(info);
                if (languageEntries <= 0 && !looksLikeTranslation)
                {
                    continue;
                }

                SourceSuggestion suggestion = BestSourceFor(candidate, sourceCandidates);
                bool included = looksLikeTranslation || suggestion != null;
                result.Add(DraftFrom(candidate, suggestion, languageEntries, looksLikeTranslation, included));
            }

            result.Sort(CompareDrafts);
            return result;
        }

        private static List<ContributionCandidate> AllModCandidates(ActiveModIndex activeIndex)
        {
            List<ContributionCandidate> result = new List<ContributionCandidate>();
            HashSet<string> seen = new HashSet<string>();
            if (activeIndex == null)
            {
                return result;
            }

            foreach (ActiveModInfo mod in activeIndex.mods)
            {
                AddCandidate(result, seen, mod, true);
            }
            foreach (ActiveModInfo mod in activeIndex.installedMods)
            {
                AddCandidate(result, seen, mod, false);
            }
            return result;
        }

        private static void AddCandidate(List<ContributionCandidate> result, HashSet<string> seen, ActiveModInfo mod, bool active)
        {
            if (mod == null)
            {
                return;
            }

            string identity = CandidateIdentity(mod);
            if (seen.Add(identity))
            {
                result.Add(new ContributionCandidate(mod, active));
            }
        }

        private ContributionDraft DraftFrom(ContributionCandidate candidate, SourceSuggestion suggestion, int languageEntries, bool looksLikeTranslation, bool included)
        {
            ActiveModInfo source = suggestion == null ? null : suggestion.candidate.info;
            ActiveModInfo translation = candidate.info;
            ContributionDraft draft = new ContributionDraft();
            draft.included = included;
            draft.sourceName = source == null ? "" : source.name;
            draft.sourcePackageId = source == null ? "" : source.packageId;
            draft.sourceSteamId = source == null ? "" : source.steamId;
            draft.sourceGameVersions = source == null ? "" : VersionsText(source.gameVersions);
            draft.translationName = translation.name;
            draft.translationPackageId = translation.packageId;
            draft.translationSteamId = translation.steamId;
            draft.translationUrl = SafeContributionUrl("", translation.steamId);
            if (languageEntries > 0 && looksLikeTranslation)
            {
                draft.reason = "FMT_Contribution_ReasonLanguageAndName".Translate(languageEntries, languageDisplayName).ToString();
            }
            else if (languageEntries > 0)
            {
                draft.reason = "FMT_Contribution_ReasonLanguageContent".Translate(languageEntries, languageDisplayName).ToString();
            }
            else
            {
                draft.reason = "FMT_Contribution_ReasonName".Translate().ToString();
            }
            return draft;
        }

        private ContributionDraft ManualDraft()
        {
            return new ContributionDraft
            {
                included = true,
                reason = "FMT_Contribution_ReasonManual".Translate().ToString()
            };
        }

        private static int CompareDrafts(ContributionDraft a, ContributionDraft b)
        {
            int result = b.included.CompareTo(a.included);
            if (result != 0) return result;
            result = string.Compare(a.sourceName ?? "", b.sourceName ?? "", StringComparison.OrdinalIgnoreCase);
            if (result != 0) return result;
            return string.Compare(a.translationName ?? "", b.translationName ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private SourceSuggestion BestSourceFor(ContributionCandidate translation, List<ContributionCandidate> candidates)
        {
            SourceSuggestion best = null;
            foreach (ContributionCandidate candidate in candidates)
            {
                if (SameMod(translation.info, candidate.info))
                {
                    continue;
                }

                int score = SourceScore(translation, candidate);
                if (score < 35)
                {
                    continue;
                }

                if (best == null || score > best.score)
                {
                    best = new SourceSuggestion(candidate, score);
                }
            }
            return best;
        }

        private int SourceScore(ContributionCandidate translation, ContributionCandidate source)
        {
            string translationPackage = ActiveModIndex.SafeLower(translation.info.packageId);
            string sourcePackage = ActiveModIndex.SafeLower(source.info.packageId);
            string translationText = TextWithoutTranslationMarkers(translation.info.name + " " + translation.info.packageId);
            string sourceText = TextWithoutTranslationMarkers(source.info.name + " " + source.info.packageId);
            string compactTranslation = translationText.Replace(" ", "");
            string compactSource = sourceText.Replace(" ", "");
            int score = 0;

            if (!translationPackage.NullOrEmpty() && !sourcePackage.NullOrEmpty() && translationPackage.Contains(sourcePackage))
            {
                score += 100;
            }
            if (!compactSource.NullOrEmpty() && compactTranslation.Contains(compactSource))
            {
                score += 80;
            }

            HashSet<string> translationTokens = new HashSet<string>(Tokens(translationText));
            foreach (string token in Tokens(sourceText))
            {
                if (token.Length > 2 && translationTokens.Contains(token))
                {
                    score += 12;
                }
            }

            if (source.active)
            {
                score += 8;
            }
            if (LooksLikeTranslation(source.info))
            {
                score -= 25;
            }
            return score;
        }

        private bool LooksLikeTranslation(ActiveModInfo mod)
        {
            HashSet<string> tokens = new HashSet<string>(Tokens((mod == null ? "" : mod.name) + " " + (mod == null ? "" : mod.packageId)));
            foreach (string token in TranslationMarkerTokens())
            {
                if (tokens.Contains(token))
                {
                    return true;
                }
            }
            return false;
        }

        private string TextWithoutTranslationMarkers(string text)
        {
            HashSet<string> markers = new HashSet<string>(TranslationMarkerTokens());
            List<string> kept = new List<string>();
            foreach (string token in Tokens(text))
            {
                if (!markers.Contains(token))
                {
                    kept.Add(token);
                }
            }
            return string.Join(" ", kept.ToArray());
        }

        private IEnumerable<string> TranslationMarkerTokens()
        {
            yield return "translation";
            yield return "translations";
            yield return "translate";
            yield return "translated";
            yield return "localization";
            yield return "localisation";
            yield return "language";
            yield return "russian";
            yield return "русский";
            yield return "русская";
            yield return "рус";
            yield return "перевод";
            yield return "переводы";
            yield return "ru";

            foreach (string folder in languageFolders ?? new string[0])
            {
                foreach (string token in Tokens(folder))
                {
                    if (!token.NullOrEmpty())
                    {
                        yield return token;
                    }
                }
            }
            foreach (string token in Tokens(languageDisplayName))
            {
                if (!token.NullOrEmpty())
                {
                    yield return token;
                }
            }
        }

        private static IEnumerable<string> Tokens(string text)
        {
            string normalized = NormalizeText(text);
            if (normalized.NullOrEmpty())
            {
                yield break;
            }

            foreach (string token in normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                yield return token;
            }
        }

        private static string NormalizeText(string text)
        {
            StringBuilder builder = new StringBuilder();
            foreach (char c in ActiveModIndex.SafeLower(text))
            {
                builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
            }
            return Regex.Replace(builder.ToString(), "\\s+", " ").Trim();
        }

        private static bool IsIgnoredMod(ActiveModInfo mod)
        {
            string packageId = ActiveModIndex.SafeLower(mod == null ? "" : mod.packageId);
            if (packageId == "cheesex.findmodtranslations" || packageId == "brrainz.harmony")
            {
                return true;
            }
            if (packageId == "ludeon.rimworld" || packageId.StartsWith("ludeon.rimworld."))
            {
                return true;
            }

            string steamId = mod == null ? "" : mod.steamId;
            return steamId == "294100" || steamId == "1149640" || steamId == "1392840" || steamId == "1826140" || steamId == "2380740";
        }

        private static bool SameMod(ActiveModInfo a, ActiveModInfo b)
        {
            if (a == null || b == null)
            {
                return false;
            }
            if (!a.packageId.NullOrEmpty() && !b.packageId.NullOrEmpty() && ActiveModIndex.SafeLower(a.packageId) == ActiveModIndex.SafeLower(b.packageId))
            {
                return true;
            }
            if (!a.steamId.NullOrEmpty() && !b.steamId.NullOrEmpty() && a.steamId == b.steamId)
            {
                return true;
            }
            return !a.rootDir.NullOrEmpty() && !b.rootDir.NullOrEmpty() && ActiveModIndex.SafeLower(a.rootDir) == ActiveModIndex.SafeLower(b.rootDir);
        }

        private static string CandidateIdentity(ActiveModInfo mod)
        {
            if (mod == null)
            {
                return "";
            }
            if (!mod.packageId.NullOrEmpty())
            {
                return "package:" + ActiveModIndex.SafeLower(mod.packageId);
            }
            if (!mod.steamId.NullOrEmpty())
            {
                return "steam:" + mod.steamId;
            }
            if (!mod.rootDir.NullOrEmpty())
            {
                return "path:" + ActiveModIndex.SafeLower(mod.rootDir);
            }
            return "name:" + ActiveModIndex.SafeLower(mod.name);
        }

        private static string VersionsText(string[] versions)
        {
            if (versions == null || versions.Length == 0)
            {
                return "";
            }
            return string.Join(", ", versions.Where(v => !v.NullOrEmpty()).Distinct().ToArray());
        }

        private static string DigitsOnly(string value)
        {
            string text = (value ?? "").Trim();
            return Regex.IsMatch(text, "^\\d{6,}$") ? text : "";
        }

        private class ContributionDraft
        {
            public bool included;
            public string reason = "";
            public string sourceName = "";
            public string sourcePackageId = "";
            public string sourceSteamId = "";
            public string sourceGameVersions = "";
            public string translationName = "";
            public string translationPackageId = "";
            public string translationSteamId = "";
            public string translationUrl = "";
        }

        private class ContributionCandidate
        {
            public readonly ActiveModInfo info;
            public readonly bool active;

            public ContributionCandidate(ActiveModInfo info, bool active)
            {
                this.info = info;
                this.active = active;
            }
        }

        private class SourceSuggestion
        {
            public readonly ContributionCandidate candidate;
            public readonly int score;

            public SourceSuggestion(ContributionCandidate candidate, int score)
            {
                this.candidate = candidate;
                this.score = score;
            }
        }
    }
}
