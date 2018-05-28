﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using ThunderED.Helpers;

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
        public static IConfigurationRoot Root { get; private set; }
        public static string DatabaseFilePath { get; }

        public static DateTime NextNotificationCheck { get; set; }
        public static string DefaultUserAgent = "ThunderED";

        public static ThunderSettings Settings { get; private set; }

        public static void Prepare()
        {
            UpdateSettings();

            FileSettingsPath = Path.Combine(RootDirectory, "settings.json");

            FileTemplateMain = Path.Combine(RootDirectory, "Templates", "main.html");
            FileTemplateAuth = Path.Combine(RootDirectory, "Templates", "auth.html");
            FileTemplateAuth2 = Path.Combine(RootDirectory, "Templates", "auth2.html");
            FileTemplateAuth3 = Path.Combine(RootDirectory, "Templates", "auth3.html");
            FileTemplateAuthNotifyFail = Path.Combine(RootDirectory, "Templates", "authNotifyFail.html");
            FileTemplateAuthNotifySuccess = Path.Combine(RootDirectory, "Templates", "authNotifySuccess.html");
            FileTemplateTimersPage = Path.Combine(RootDirectory, "Templates", "timersMain.html");
            FileTemplateMailAuthSuccess = Path.Combine(RootDirectory, "Templates", "mailAuthSuccess.html");
        }


        static SettingsManager()
        {
            RootDirectory = Path.GetDirectoryName(new Uri(Assembly.GetEntryAssembly().CodeBase).LocalPath);
            DatabaseFilePath = Path.Combine(RootDirectory, "edb.db");

            if (!File.Exists(DatabaseFilePath))
            {
                File.Copy(Path.Combine(RootDirectory,"edb.def.db"), DatabaseFilePath);
                IsNew = true;
            }
        }

        public static void UpdateSettings()
        {
            Root = new ConfigurationBuilder()
                .SetBasePath(RootDirectory)
                .AddJsonFile("settings.json", optional: true, reloadOnChange: true).Build();
            
            if (GetBool("config", "moduleNotificationFeed") && SQLHelper.Provider != null)
              NextNotificationCheck = DateTime.Parse(SQLHelper.SQLiteDataQuery<string>("cacheData", "data", "name", "nextNotificationCheck").GetAwaiter().GetResult());

            //new 
            Settings = ThunderSettings.Load(FileSettingsPath);
        }

        public static bool GetBool(string section, string field)
        {
            return Convert.ToBoolean(Root.GetSection(section)[field]);
        }

        public static short GetShort(string section, string field)
        {
            return Convert.ToInt16(Root.GetSection(section)[field]);
        }

        public static ulong GetULong(string section, string field)
        {
            return Convert.ToUInt64(Root.GetSection(section)[field]);
        }

        public static long GetLong(string section, string field)
        {
            return Convert.ToInt64(Root.GetSection(section)[field]);
        }

        public static string Get(string section, string field)
        {
            return Root.GetSection(section)[field];
        }

        public static List<IConfigurationSection> GetSubList(string section)
        {
            return Root.GetSection(section).GetChildren().ToList();
        }

        public static List<IConfigurationSection> GetSubList(string section, string section2)
        {
            return Root.GetSection(section).GetSection(section2).GetChildren().ToList();
        }

        public static List<IConfigurationSection> GetSubList(IConfigurationSection section, string section2)
        {
            return section.GetSection(section2).GetChildren().ToList();
        }

        public static T[] GetArray<T>(string section, string field)
        {
            var f = Root.GetSection(section)?.GetChildren()?.FirstOrDefault(a => a.Key == field);
            if(f == null) return new T[0];

            return f.GetChildren()?.Select(a=> {
                var type = typeof(T);
                if(type == typeof(string))
                    return (T)(object)a.Value;
                if(type == typeof(int))
                    return (T)(object)Convert.ToInt32(a.Value);
                throw new Exception("Unknown type!");
            }).ToArray();
        }

        public static T[] GetArray<T>(IConfigurationSection group, string field)
        {
            var f = group?.GetChildren()?.FirstOrDefault(a => a.Key == field);
            if(f == null) return new T[0];

            return f.GetChildren()?.Select(a=> {
                var type = typeof(T);
                if(type == typeof(string))
                    return (T)(object)a.Value;
                if(type == typeof(int))
                    return (T)(object)Convert.ToInt32(a.Value);
                throw new Exception("Unknown type!");
            }).ToArray();
        }

        public static T GetSubValue<T>(string section, string section2)
        {
            return (T)(object)Root.GetSection(section).GetSection(section2).Value;
        }

        public static int GetInt(string section, string field)
        {
            return Convert.ToInt32(Root.GetSection(section)[field]);
        }


    }

   
}
