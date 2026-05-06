using System.Text.RegularExpressions;
using UnityEngine;

namespace FindModTranslations
{
    public static class SteamUrlTools
    {
        private const string SharedPrefix = "https://steamcommunity.com/sharedfiles/filedetails/?id=";
        private const string WorkshopPrefix = "https://steamcommunity.com/workshop/filedetails/?id=";

        public static string UrlFor(TranslationModInfo translation)
        {
            if (translation == null)
            {
                return "";
            }
            if (IsSteamId(translation.steamId))
            {
                return SharedPrefix + translation.steamId;
            }
            return IsSafeWorkshopUrl(translation.url) ? translation.url : "";
        }

        public static void Open(string url)
        {
            if (IsSafeWorkshopUrl(url))
            {
                Application.OpenURL(url);
            }
        }

        public static bool IsSafeWorkshopUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }
            return Regex.IsMatch(url, "^https://steamcommunity\\.com/(sharedfiles|workshop)/filedetails/\\?id=\\d+(&.*)?$");
        }

        private static bool IsSteamId(string value)
        {
            return !string.IsNullOrEmpty(value) && Regex.IsMatch(value, "^\\d{6,}$");
        }
    }
}
