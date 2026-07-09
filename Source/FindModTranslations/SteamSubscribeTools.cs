using System;
using System.Text.RegularExpressions;
using Steamworks;
using Verse;
using Verse.Steam;

namespace FindModTranslations
{
    public enum SteamSubscribeStatus
    {
        Unavailable,
        InvalidId,
        NotSubscribed,
        Subscribed,
        Downloading,
        Installed
    }

    public static class SteamSubscribeTools
    {
        public static bool Available
        {
            get
            {
                try
                {
                    return SteamManager.Initialized && SteamAPI.IsSteamRunning();
                }
                catch
                {
                    return false;
                }
            }
        }

        public static SteamSubscribeStatus StatusFor(TranslationModInfo translation)
        {
            if (!Available)
            {
                return SteamSubscribeStatus.Unavailable;
            }
            if (!TryPublishedFileId(translation, out PublishedFileId_t fileId))
            {
                return SteamSubscribeStatus.InvalidId;
            }
            try
            {
                uint state = SteamUGC.GetItemState(fileId);
                if (HasState(state, EItemState.k_EItemStateInstalled))
                {
                    return SteamSubscribeStatus.Installed;
                }
                if (HasState(state, EItemState.k_EItemStateDownloading) || HasState(state, EItemState.k_EItemStateDownloadPending))
                {
                    return SteamSubscribeStatus.Downloading;
                }
                if (HasState(state, EItemState.k_EItemStateSubscribed))
                {
                    return SteamSubscribeStatus.Subscribed;
                }
                return SteamSubscribeStatus.NotSubscribed;
            }
            catch
            {
                return SteamSubscribeStatus.Unavailable;
            }
        }

        public static bool TrySubscribe(TranslationModInfo translation, out SteamSubscribeStatus status)
        {
            status = StatusFor(translation);
            if (status == SteamSubscribeStatus.Unavailable || status == SteamSubscribeStatus.InvalidId)
            {
                return false;
            }
            if (status == SteamSubscribeStatus.Installed || status == SteamSubscribeStatus.Downloading || status == SteamSubscribeStatus.Subscribed)
            {
                if (TryPublishedFileId(translation, out PublishedFileId_t existingId) && status == SteamSubscribeStatus.Subscribed)
                {
                    TryDownload(existingId);
                }
                return false;
            }
            if (!TryPublishedFileId(translation, out PublishedFileId_t fileId))
            {
                status = SteamSubscribeStatus.InvalidId;
                return false;
            }
            try
            {
                SteamUGC.SubscribeItem(fileId);
                return true;
            }
            catch
            {
                status = SteamSubscribeStatus.Unavailable;
                return false;
            }
        }

        public static string TooltipKey(SteamSubscribeStatus status)
        {
            switch (status)
            {
                case SteamSubscribeStatus.Unavailable:
                    return "FMT_Window_SubscribeUnavailable";
                case SteamSubscribeStatus.InvalidId:
                    return "FMT_Window_SubscribeInvalidId";
                case SteamSubscribeStatus.Installed:
                    return "FMT_Window_SubscribeInstalled";
                case SteamSubscribeStatus.Downloading:
                    return "FMT_Window_SubscribeDownloading";
                case SteamSubscribeStatus.Subscribed:
                    return "FMT_Window_SubscribeSubscribed";
                default:
                    return "FMT_Window_Subscribe";
            }
        }

        private static bool TryPublishedFileId(TranslationModInfo translation, out PublishedFileId_t fileId)
        {
            fileId = PublishedFileId_t.Invalid;
            string id = translation == null ? "" : translation.steamId;
            if (id.NullOrEmpty() && translation != null && !translation.url.NullOrEmpty())
            {
                Match match = Regex.Match(translation.url, @"(?:\?|&)id=(\d{6,})");
                if (match.Success)
                {
                    id = match.Groups[1].Value;
                }
            }
            if (id.NullOrEmpty() || !ulong.TryParse(id, out ulong value) || value == 0UL)
            {
                return false;
            }
            fileId = new PublishedFileId_t(value);
            return true;
        }

        private static bool HasState(uint state, EItemState value)
        {
            return (state & (uint)value) != 0;
        }

        private static void TryDownload(PublishedFileId_t fileId)
        {
            try
            {
                SteamUGC.DownloadItem(fileId, true);
            }
            catch
            {
            }
        }
    }
}
