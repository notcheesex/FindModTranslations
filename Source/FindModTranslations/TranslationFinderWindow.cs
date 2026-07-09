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
    public class ConfirmOpenLinksDialog : Window
    {
        private readonly List<string> urls;
        public override Vector2 InitialSize => new Vector2(520f, 175f);

        public ConfirmOpenLinksDialog(List<string> urls)
        {
            this.urls = urls ?? new List<string>();
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, inRect.height - 48f), "FMT_Window_OpenAllConfirm".Translate(urls.Count));
            float y = inRect.yMax - 38f;
            if (Widgets.ButtonText(new Rect(inRect.xMax - 250f, y, 115f, 34f), "FMT_Window_OpenAllConfirmYes".Translate()))
            {
                foreach (string url in urls)
                {
                    SteamUrlTools.Open(url);
                }
                Close();
            }
            if (Widgets.ButtonText(new Rect(inRect.xMax - 125f, y, 115f, 34f), "CancelButton".Translate()))
            {
                Close();
            }
        }
    }

    [StaticConstructorOnStartup]
    public class TranslationFinderWindow : Window
    {
        private static readonly Texture2D SteamIcon = ContentFinder<Texture2D>.Get("UI/Steam", false);
        private static readonly Texture2D CopyIcon = ContentFinder<Texture2D>.Get("UI/Copy", false);
        private static readonly Texture2D SubscribeIcon = ContentFinder<Texture2D>.Get("UI/Subscribe", false);
        private readonly List<TranslationMatch> matches;
        private readonly TranslationDatabase database;
        private Vector2 scroll;
        private readonly HashSet<string> expanded = new HashSet<string>();

        public override Vector2 InitialSize => new Vector2(820f, 660f);

        public TranslationFinderWindow(List<TranslationMatch> matches, TranslationDatabase database)
        {
            this.matches = matches ?? new List<TranslationMatch>();
            this.database = database;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "FMT_Window_Title".Translate());
            Text.Font = GameFont.Small;

            string summary = matches.Count == 0
                ? "FMT_Window_NoneFound".Translate()
                : "FMT_Window_Found".Translate(matches.Count);
            Widgets.Label(new Rect(inRect.x, inRect.y + 38f, inRect.width, 24f), summary);
            bool showRestartHint = matches.Count > 0;

            Rect buttons = new Rect(inRect.x, inRect.yMax - 42f, inRect.width, 38f);
            float buttonX = buttons.x;
            if (Widgets.ButtonText(new Rect(buttonX, buttons.y, 175f, 34f), "FMT_Window_CopyFullList".Translate()))
            {
                string copyText = BuildCopyText();
                if (copyText.NullOrEmpty())
                {
                    Messages.Message("FMT_Message_NoNotInstalled".Translate(), MessageTypeDefOf.RejectInput, false);
                }
                else
                {
                    GUIUtility.systemCopyBuffer = copyText;
                    Messages.Message("FMT_Message_ListCopied".Translate(), MessageTypeDefOf.TaskCompletion, false);
                }
            }
            buttonX += 183f;
            if (Widgets.ButtonText(new Rect(buttonX, buttons.y, 105f, 34f), "FMT_Window_OpenAll".Translate()))
            {
                List<string> urls = NotInstalledUrls();
                if (urls.Count == 0)
                {
                    Messages.Message("FMT_Message_NoNotInstalled".Translate(), MessageTypeDefOf.RejectInput, false);
                }
                else
                {
                    Find.WindowStack.Add(new ConfirmOpenLinksDialog(urls));
                }
            }
            buttonX += 113f;
            if (SteamSubscribeTools.Available)
            {
                if (Widgets.ButtonText(new Rect(buttonX, buttons.y, 150f, 34f), "FMT_Window_SubscribeAll".Translate()))
                {
                    SubscribeAll();
                }
                buttonX += 158f;
            }
            if (Widgets.ButtonText(new Rect(buttonX, buttons.y, 165f, 34f), "FMT_Contribution_OpenButton".Translate()))
            {
                FindModTranslationsMod.ShowContributionWizard();
            }
            if (Widgets.ButtonText(new Rect(buttons.xMax - 105f, buttons.y, 105f, 34f), "FMT_Window_Close".Translate()))
            {
                Close();
            }

            if (showRestartHint)
            {
                Color savedColor = GUI.color;
                GUI.color = new Color(1f, 0.86f, 0.45f, 1f);
                Widgets.Label(new Rect(inRect.x, inRect.yMax - 66f, inRect.width, 22f), "FMT_Window_RestartEnableHint".Translate());
                GUI.color = savedColor;
            }

            Rect viewOuter = new Rect(inRect.x, inRect.y + 70f, inRect.width, inRect.height - (showRestartHint ? 142f : 118f));
            float height = Math.Max(viewOuter.height - 20f, ListHeight(viewOuter.height));
            Rect view = new Rect(0f, 0f, viewOuter.width - 18f, height);
            Widgets.BeginScrollView(viewOuter, ref scroll, view);

            float y = 0f;
            float visibleTop = scroll.y - 40f;
            float visibleBottom = scroll.y + viewOuter.height + 40f;
            if (matches.Count == 0)
            {
                string body = database != null && database.unavailableForRequestedLanguage
                    ? "FMT_Window_NoLanguageDatabaseBody".Translate(database.LanguageDisplayName).ToString()
                    : "FMT_Window_NoMatchesBody".Translate().ToString();
                Widgets.Label(new Rect(0f, y, view.width, 80f), body);
                y += 90f;
            }
            else
            {
                for (int i = 0; i < matches.Count; i++)
                {
                    DrawMatch(view, ref y, matches[i], i, visibleTop, visibleBottom);
                }
            }

            Widgets.EndScrollView();
        }

        private float ListHeight(float minimum)
        {
            float height = 80f;
            foreach (TranslationMatch match in matches)
            {
                height += 90f;
                if (IsExpanded(match) && match.entry.alternatives != null)
                {
                    height += match.entry.alternatives.Length * 34f;
                }
            }
            return Math.Max(minimum - 20f, height);
        }

        private bool IsExpanded(TranslationMatch match)
        {
            return expanded.Contains(MatchKey(match));
        }

        private static string MatchKey(TranslationMatch match)
        {
            if (match == null)
            {
                return "";
            }
            if (match.entry != null)
            {
                if (!match.entry.packageId.NullOrEmpty()) return match.entry.packageId;
                if (!match.entry.steamId.NullOrEmpty()) return match.entry.steamId;
                if (!match.entry.name.NullOrEmpty()) return match.entry.name;
            }
            if (match.activeMod != null && !match.activeMod.name.NullOrEmpty()) return match.activeMod.name;
            return "";
        }

        private void DrawMatch(Rect view, ref float y, TranslationMatch match, int index, float visibleTop, float visibleBottom)
        {
            bool expandedMatch = IsExpanded(match) && match.entry.alternatives != null;
            float totalHeight = 90f + (expandedMatch ? match.entry.alternatives.Length * 34f : 0f);
            if (y + totalHeight < visibleTop || y > visibleBottom)
            {
                y += totalHeight;
                return;
            }

            Rect box = new Rect(0f, y, view.width, 82f);
            TranslationModInfo translation = match.entry.translation;

            Color savedColor = GUI.color;
            GUI.color = index % 2 == 0 ? new Color(0.16f, 0.18f, 0.21f, 0.90f) : new Color(0.13f, 0.15f, 0.18f, 0.90f);
            Widgets.DrawBoxSolid(box, GUI.color);
            GUI.color = new Color(0.34f, 0.41f, 0.50f, 0.78f);
            Widgets.DrawBox(box, 1);
            GUI.color = StatusColor(match);
            Widgets.DrawBoxSolid(new Rect(box.x, box.y, 4f, box.height), GUI.color);
            GUI.color = savedColor;

            if (Mouse.IsOver(box))
            {
                Widgets.DrawHighlight(box);
            }

            float left = box.x + 14f;
            float rightColWidth = 166f;
            float contentRight = box.xMax - rightColWidth - 12f;
            float contentWidth = contentRight - left;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            GUI.color = Color.white;
            Widgets.Label(new Rect(left, y + 8f, contentWidth, 22f), match.activeMod.name);

            GUI.color = new Color(0.78f, 0.84f, 0.92f, 1f);
            Widgets.Label(new Rect(left, y + 30f, contentWidth, 22f), translation.name);

            Rect statusRect = new Rect(left, y + 54f, 210f, 20f);
            GUI.color = StatusColor(match);
            DrawClippedLabel(statusRect, StatusText(match));

            Rect versionRect = new Rect(statusRect.xMax + 8f, y + 54f, 42f, 20f);
            DrawVersions(versionRect, translation.gameVersions);

            GUI.color = new Color(0.56f, 0.61f, 0.68f, 1f);
            DrawClippedLabel(new Rect(versionRect.xMax + 12f, y + 54f, contentRight - versionRect.xMax - 12f, 20f), translation.author);
            GUI.color = savedColor;

            Rect subscribeRect = new Rect(box.xMax - 122f, y + 19f, 28f, 28f);
            DrawSubscribeButton(subscribeRect, translation);

            Rect openRect = new Rect(box.xMax - 82f, y + 19f, 28f, 28f);
            Rect copyRect = new Rect(box.xMax - 42f, y + 21f, 24f, 24f);
            if (IconButton(openRect, SteamIcon, "Steam"))
            {
                OpenTranslation(translation);
            }
            TooltipHandler.TipRegion(openRect, "FMT_Window_Open".Translate());

            if (IconButton(copyRect, CopyIcon, "Copy"))
            {
                GUIUtility.systemCopyBuffer = SteamUrlTools.UrlFor(translation);
                Messages.Message("FMT_Message_LinkCopied".Translate(), MessageTypeDefOf.TaskCompletion, false);
            }
            TooltipHandler.TipRegion(copyRect, "FMT_Window_Copy".Translate());

            if (match.entry.alternatives != null && match.entry.alternatives.Length > 0)
            {
                Rect altRect = new Rect(box.xMax - 126f, y + 54f, 104f, 24f);
                if (Widgets.ButtonText(altRect, "FMT_Window_AlternativesButton".Translate()))
                {
                    string key = MatchKey(match);
                    if (!expanded.Remove(key)) expanded.Add(key);
                }
                TooltipHandler.TipRegion(altRect, "FMT_Window_Alternatives".Translate(match.entry.alternatives.Length));
            }

            y += 90f;
            if (expandedMatch)
            {
                foreach (TranslationModInfo alt in SortedAlternatives(match))
                {
                    DrawAlternative(view, ref y, match, alt, visibleTop, visibleBottom);
                }
            }
        }

        private static IEnumerable<TranslationModInfo> SortedAlternatives(TranslationMatch match)
        {
            if (match == null || match.entry == null || match.entry.alternatives == null)
            {
                yield break;
            }
            foreach (TranslationModInfo alt in match.entry.alternatives.OrderBy(a => AlternativeSortRank(match, a)).ThenBy(a => a == null ? "" : a.name))
            {
                yield return alt;
            }
        }

        private static int AlternativeSortRank(TranslationMatch match, TranslationModInfo alt)
        {
            if (SameTranslation(alt, match == null ? null : match.activeAlternative)) return 0;
            if (SameTranslation(alt, match == null ? null : match.installedAlternative)) return 1;
            return 2;
        }

        private void DrawAlternative(Rect view, ref float y, TranslationMatch match, TranslationModInfo alt, float visibleTop, float visibleBottom)
        {
            Rect row = new Rect(18f, y, view.width - 18f, 28f);
            if (row.yMax < visibleTop || row.y > visibleBottom)
            {
                y += 34f;
                return;
            }

            bool installed = SameTranslation(alt, match.installedAlternative);
            bool active = SameTranslation(alt, match.activeAlternative);
            Color saved = GUI.color;
            GUI.color = installed || active ? new Color(0.15f, 0.16f, 0.14f, 0.92f) : new Color(0.10f, 0.12f, 0.15f, 0.82f);
            Widgets.DrawBoxSolid(row, GUI.color);
            GUI.color = installed || active ? new Color(1f, 0.78f, 0.35f, 0.90f) : new Color(0.28f, 0.34f, 0.42f, 0.65f);
            Widgets.DrawBox(row, 1);
            if (installed || active)
            {
                Widgets.DrawBoxSolid(new Rect(row.x, row.y, 3f, row.height), GUI.color);
            }
            GUI.color = saved;

            if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
            GUI.color = new Color(0.72f, 0.78f, 0.86f, 1f);
            Rect versionRect = new Rect(row.xMax - 164f, row.y + 4f, 42f, 22f);
            Rect badgeRect = new Rect(row.x + 250f, row.y + 4f, installed || active ? 88f : 0f, 22f);
            Rect authorRect = new Rect(row.x + (installed || active ? 346f : 250f), row.y + 4f, Math.Max(0f, versionRect.x - row.x - (installed || active ? 354f : 258f)), 22f);
            Widgets.Label(new Rect(row.x + 8f, row.y + 4f, 236f, 22f), alt.name);
            if (installed || active)
            {
                GUI.color = StatusColor(match);
                DrawClippedLabel(badgeRect, active ? "FMT_Window_ActiveBadge".Translate().ToString() : "FMT_Window_InstalledBadge".Translate().ToString());
            }
            GUI.color = new Color(0.56f, 0.61f, 0.68f, 1f);
            DrawClippedLabel(authorRect, alt.author);
            DrawVersions(versionRect, alt.gameVersions);
            Rect subscribeRect = new Rect(row.xMax - 110f, row.y + 3f, 22f, 22f);
            Rect openRect = new Rect(row.xMax - 78f, row.y + 3f, 22f, 22f);
            Rect copyRect = new Rect(row.xMax - 42f, row.y + 4f, 20f, 20f);
            DrawSubscribeButton(subscribeRect, alt);
            if (IconButton(openRect, SteamIcon, "Steam")) OpenTranslation(alt);
            if (IconButton(copyRect, CopyIcon, "Copy"))
            {
                GUIUtility.systemCopyBuffer = SteamUrlTools.UrlFor(alt);
                Messages.Message("FMT_Message_LinkCopied".Translate(), MessageTypeDefOf.TaskCompletion, false);
            }
            GUI.color = saved;
            y += 34f;
        }


        private static bool SameTranslation(TranslationModInfo a, TranslationModInfo b)
        {
            if (a == null || b == null) return false;
            if (!a.steamId.NullOrEmpty() && a.steamId == b.steamId) return true;
            if (!a.packageId.NullOrEmpty() && ActiveModIndex.SafeLower(a.packageId) == ActiveModIndex.SafeLower(b.packageId)) return true;
            return false;
        }

        private static void DrawSubscribeButton(Rect rect, TranslationModInfo translation)
        {
            SteamSubscribeStatus status = SteamSubscribeTools.StatusFor(translation);
            if (status != SteamSubscribeStatus.NotSubscribed)
            {
                return;
            }

            bool clicked = Widgets.ButtonInvisible(rect);
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }

            if (SubscribeIcon != null)
            {
                GUI.DrawTexture(rect, SubscribeIcon, ScaleMode.ScaleToFit, true);
            }

            TooltipHandler.TipRegion(rect, "FMT_Window_Subscribe".Translate());
            if (clicked)
            {
                HandleSubscribeClick(translation);
            }
        }

        private static void HandleSubscribeClick(TranslationModInfo translation)
        {
            bool requested = SteamSubscribeTools.TrySubscribe(translation, out SteamSubscribeStatus status);
            if (requested)
            {
                Messages.Message("FMT_Message_SubscribeRequested".Translate(), MessageTypeDefOf.TaskCompletion, false);
                return;
            }
            string key;
            MessageTypeDef messageType = MessageTypeDefOf.RejectInput;
            switch (status)
            {
                case SteamSubscribeStatus.Unavailable:
                    key = "FMT_Message_SubscribeUnavailable";
                    break;
                case SteamSubscribeStatus.InvalidId:
                    key = "FMT_Message_SubscribeInvalidId";
                    break;
                case SteamSubscribeStatus.Installed:
                    key = "FMT_Message_SubscribeInstalled";
                    messageType = MessageTypeDefOf.NeutralEvent;
                    break;
                case SteamSubscribeStatus.Downloading:
                    key = "FMT_Message_SubscribeDownloading";
                    messageType = MessageTypeDefOf.NeutralEvent;
                    break;
                case SteamSubscribeStatus.Subscribed:
                    key = "FMT_Message_SubscribeAlreadySubscribed";
                    messageType = MessageTypeDefOf.NeutralEvent;
                    break;
                default:
                    key = "FMT_Message_SubscribeUnavailable";
                    break;
            }
            Messages.Message(key.Translate(), messageType, false);
        }

        private static bool IconButton(Rect rect, Texture2D icon, string fallback)
        {
            bool clicked = Widgets.ButtonInvisible(rect);
            Color saved = GUI.color;
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }
            if (icon != null)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(rect.ContractedBy(fallback == "Steam" ? 4f : 3f), icon, ScaleMode.ScaleToFit, true);
            }
            else
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.white;
                Widgets.Label(rect, fallback);
                Text.Anchor = TextAnchor.UpperLeft;
            }
            GUI.color = saved;
            return clicked;
        }

        private static void DrawClippedLabel(Rect rect, string text)
        {
            if (text.NullOrEmpty()) return;
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

        private static Color StatusColor(TranslationMatch match)
        {
            if (match.translationActive) return new Color(0.45f, 0.95f, 0.55f, 1f);
            if (match.activeAlternative != null) return new Color(0.75f, 0.95f, 0.55f, 1f);
            if (match.translationInstalled) return new Color(1f, 0.78f, 0.35f, 1f);
            if (match.installedAlternative != null) return new Color(0.95f, 0.62f, 0.30f, 1f);
            return new Color(0.86f, 0.38f, 0.32f, 1f);
        }

        private static string StatusText(TranslationMatch match)
        {
            if (match.translationActive)
            {
                return "FMT_Window_StatusActiveTranslation".Translate();
            }
            if (match.activeAlternative != null)
            {
                return "FMT_Window_StatusActiveAlternative".Translate();
            }
            if (match.translationInstalled)
            {
                return "FMT_Window_StatusInstalledInactive".Translate();
            }
            if (match.installedAlternative != null)
            {
                return "FMT_Window_StatusInstalledAlternative".Translate();
            }
            return "FMT_Window_StatusNotInstalled".Translate();
        }

        private static void DrawVersions(Rect rect, string[] versions)
        {
            if (versions == null || versions.Length == 0) return;
            string current = VersionControl.CurrentVersionStringWithoutBuild;
            bool supportsCurrent = versions.Contains(current);
            string text = supportsCurrent ? current : versions[versions.Length - 1];
            Color saved = GUI.color;
            GUI.color = supportsCurrent ? new Color(0.45f, 0.95f, 0.55f, 1f) : new Color(0.86f, 0.38f, 0.32f, 1f);
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(rect, text);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = saved;
            TooltipHandler.TipRegion(rect, string.Join(", ", versions));
        }

        private List<string> NotInstalledUrls()
        {
            List<string> urls = new List<string>();
            foreach (TranslationMatch match in matches)
            {
                if (!match.translationInstalled && match.entry != null && match.entry.translation != null)
                {
                    if (match.activeAlternative != null)
                    {
                        continue;
                    }
                    string url = SteamUrlTools.UrlFor(match.entry.translation);
                    if (!url.NullOrEmpty())
                    {
                        urls.Add(url);
                    }
                }
            }
            return urls.Distinct().ToList();
        }

        private string BuildCopyText()
        {
            return string.Join("\n", NotInstalledUrls().ToArray());
        }

        private IEnumerable<TranslationModInfo> SubscribeAllCandidates()
        {
            HashSet<string> seen = new HashSet<string>();
            foreach (TranslationMatch match in matches)
            {
                if (match.translationInstalled || match.activeAlternative != null || match.entry == null || match.entry.translation == null)
                {
                    continue;
                }
                TranslationModInfo translation = match.entry.translation;
                if (SteamSubscribeTools.StatusFor(translation) != SteamSubscribeStatus.NotSubscribed)
                {
                    continue;
                }
                string key = !translation.steamId.NullOrEmpty() ? translation.steamId : SteamUrlTools.UrlFor(translation);
                if (!key.NullOrEmpty() && seen.Add(key))
                {
                    yield return translation;
                }
            }
        }

        private void SubscribeAll()
        {
            int requested = 0;
            foreach (TranslationModInfo translation in SubscribeAllCandidates())
            {
                if (SteamSubscribeTools.TrySubscribe(translation, out SteamSubscribeStatus _))
                {
                    requested++;
                }
            }
            if (requested == 0)
            {
                Messages.Message("FMT_Message_NoSubscribableTranslations".Translate(), MessageTypeDefOf.RejectInput, false);
            }
            else
            {
                Messages.Message("FMT_Message_SubscribeAllRequested".Translate(requested), MessageTypeDefOf.TaskCompletion, false);
            }
        }

        private static void OpenTranslation(TranslationModInfo translation)
        {
            string url = SteamUrlTools.UrlFor(translation);
            if (!url.NullOrEmpty())
            {
                SteamUrlTools.Open(url);
            }
        }
    }
}
