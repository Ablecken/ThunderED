﻿using System;
using System.IO;
using System.Reflection;

namespace ThunderED.Classes
{
    /// <summary>
    /// Use partial class to implement additional methods
    /// </summary>
    public static partial class SettingsManager
    {
        
        public static bool IsNew { get; set; }
        public static string FileSettingsPath;
        public static string FileTemplateMain;
        public static string FileTemplateAuth;
        public static string FileTemplateAuth2;
        public static string FileTemplateAuth3;
        public static string FileTemplateAuthNotifyFail;
        public static string FileTemplateAuthNotifySuccess;
        public static string FileTemplateTimersPage;
        public static string FileTemplateMailAuthSuccess;
        public static string RootDirectory { get; }
        public static string DatabaseFilePath { get; private set; }

        public static DateTime NextNotificationCheck { get; set; }
        public static string DefaultUserAgent = "ThunderED";

        public static ThunderSettings Settings { get; private set; }

        public static string Prepare()
        {
            try
            {
                FileSettingsPath = Path.Combine(RootDirectory, "settings.json");
                UpdateSettings();
                FileTemplateMain = Path.Combine(RootDirectory, "Templates", "main.html");
                FileTemplateAuth = Path.Combine(RootDirectory, "Templates", "auth.html");
                FileTemplateAuth2 = Path.Combine(RootDirectory, "Templates", "auth2.html");
                FileTemplateAuth3 = Path.Combine(RootDirectory, "Templates", "auth3.html");
                FileTemplateAuthNotifyFail = Path.Combine(RootDirectory, "Templates", "authNotifyFail.html");
                FileTemplateAuthNotifySuccess = Path.Combine(RootDirectory, "Templates", "authNotifySuccess.html");
                FileTemplateTimersPage = Path.Combine(RootDirectory, "Templates", "timersMain.html");
                FileTemplateMailAuthSuccess = Path.Combine(RootDirectory, "Templates", "mailAuthSuccess.html");
                return null;
            }
            catch (Exception ex)
            {
                return $"ERROR -> {ex.Message}";
            }
        }


        static SettingsManager()
        {
            RootDirectory = Path.GetDirectoryName(new Uri(Assembly.GetEntryAssembly().CodeBase).LocalPath);
        }

        public static void UpdateSettings()
        {
            Settings = ThunderSettings.Load(FileSettingsPath);      
            DatabaseFilePath = Settings.Config.DatabaseFile == "edb.db" ? Path.Combine(RootDirectory, "edb.db") : Settings.Config.DatabaseFile;
            if (!File.Exists(DatabaseFilePath))
            {
                File.Copy(Path.Combine(RootDirectory,"edb.def.db"), DatabaseFilePath);
                IsNew = true;
            }
        }
    }
}
