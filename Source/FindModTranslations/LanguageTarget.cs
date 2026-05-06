using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Verse;

namespace FindModTranslations
{
    public static class LanguageTarget
    {
        public const string LegacyDatabaseLanguageFolder = "Russian";

        public static string CurrentFolder()
        {
            string prefsFolder = StaticStringMember(typeof(Prefs), "LangFolderName");
            if (!prefsFolder.NullOrEmpty())
            {
                return prefsFolder;
            }

            try
            {
                FieldInfo activeLanguage = typeof(LanguageDatabase).GetField("activeLanguage", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                string activeFolder = InstanceStringMember(activeLanguage == null ? null : activeLanguage.GetValue(null), "folderName");
                if (!activeFolder.NullOrEmpty())
                {
                    return activeFolder;
                }
            }
            catch
            {
            }

            return "English";
        }

        public static bool SameFolder(string a, string b)
        {
            return string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
        }

        public static bool EquivalentFolder(string a, string b)
        {
            if (SameFolder(a, b))
            {
                return true;
            }
            return SameFolder(CanonicalFolder(a), CanonicalFolder(b));
        }

        public static string CanonicalFolder(string folder)
        {
            string value = (folder ?? "").Trim();
            if (value.NullOrEmpty())
            {
                return "";
            }

            int parenthesis = value.IndexOf(" (", StringComparison.Ordinal);
            if (parenthesis > 0 && value.EndsWith(")", StringComparison.Ordinal))
            {
                value = value.Substring(0, parenthesis).Trim();
            }
            return value;
        }

        public static string[] CandidateFolders(string folder)
        {
            List<string> result = new List<string>();
            AddUnique(result, folder);
            AddUnique(result, CanonicalFolder(folder));
            return result.ToArray();
        }

        public static string SafeFolderName(string folder)
        {
            string value = (folder ?? "").Trim();
            if (value.NullOrEmpty())
            {
                return "";
            }
            if (value.IndexOf(Path.DirectorySeparatorChar) >= 0 || value.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                return "";
            }
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                if (value.IndexOf(invalid) >= 0)
                {
                    return "";
                }
            }
            return value;
        }

        public static string SafeFileToken(string folder)
        {
            if (folder.NullOrEmpty())
            {
                return "";
            }

            StringBuilder builder = new StringBuilder(folder.Length);
            foreach (char c in folder)
            {
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-')
                {
                    builder.Append(c);
                }
            }
            return builder.ToString();
        }

        public static void AddUnique(List<string> values, string value)
        {
            if (value.NullOrEmpty())
            {
                return;
            }
            foreach (string existing in values)
            {
                if (SameFolder(existing, value))
                {
                    return;
                }
            }
            values.Add(value);
        }

        private static string StaticStringMember(Type type, string name)
        {
            try
            {
                PropertyInfo property = type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                {
                    object value = property.GetValue(null, null);
                    return value == null ? "" : value.ToString();
                }

                FieldInfo field = type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    object value = field.GetValue(null);
                    return value == null ? "" : value.ToString();
                }
            }
            catch
            {
            }
            return "";
        }

        private static string InstanceStringMember(object obj, string name)
        {
            if (obj == null)
            {
                return "";
            }

            try
            {
                Type type = obj.GetType();
                PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                {
                    object value = property.GetValue(obj, null);
                    return value == null ? "" : value.ToString();
                }

                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    object value = field.GetValue(obj);
                    return value == null ? "" : value.ToString();
                }
            }
            catch
            {
            }
            return "";
        }
    }
}
