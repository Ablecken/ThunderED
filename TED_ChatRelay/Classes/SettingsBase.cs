﻿#region License Information (GPL v3)

/*
    Copyright (c) Jaex

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace ThunderED.Classes
{
    public abstract class SettingsBase<T> where T : SettingsBase<T>, new()
    {
        [JsonIgnore]
        public string FilePath { get; private set; }

        public bool Save(string filePath)
        {
            FilePath = filePath;

            return SaveInternal(this, FilePath, true);
        }

        public bool Save()
        {
            return Save(FilePath);
        }

        public async Task SaveAsync(string filePath)
        {
            await Task.Run(() => Save(filePath));
        }

        public async Task SaveAsync()
        {
            await SaveAsync(FilePath);
        }

        public static T Load(string filePath)
        {
            T setting;

            if (!File.Exists(filePath))
            {
                setting = new T {FilePath = filePath};
                setting.Save();
            }
            else
            {
                setting = LoadInternal(filePath, true);
                if (setting != null)
                {
                    setting.FilePath = filePath;
                }

            }



            return setting;
        }

        private static bool SaveInternal(object obj, string filePath, bool createBackup)
        {
            var typeName = obj.GetType().Name;
            Console.WriteLine("{0} save started: {1}", typeName, filePath);

            var isSuccess = false;

            try
            {
                if (!string.IsNullOrEmpty(filePath))
                {
                    lock (obj)
                    {
                        if (!string.IsNullOrEmpty(filePath) && !Directory.Exists(filePath))
                        {
                            Directory.CreateDirectory(filePath);
                        }

                        var tempFilePath = filePath + ".temp";

                        using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
                        using (var streamWriter = new StreamWriter(fileStream))
                        using (var jsonWriter = new JsonTextWriter(streamWriter))
                        {
                            jsonWriter.Formatting = Formatting.Indented;
                            var serializer = new JsonSerializer {ContractResolver = new WritablePropertiesOnlyResolver()};
                            serializer.Converters.Add(new StringEnumConverter());
                            serializer.Serialize(jsonWriter, obj);
                            jsonWriter.Flush();
                        }

                        if (File.Exists(filePath))
                        {
                            if (createBackup)
                            {
                                File.Copy(filePath, filePath + ".bak", true);
                            }

                            File.Delete(filePath);
                        }

                        File.Move(tempFilePath, filePath);

                        isSuccess = true;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                Console.WriteLine("{0} save {1}: {2}", typeName, isSuccess ? "successful" : "failed", filePath);
            }

            return isSuccess;
        }

        internal class WritablePropertiesOnlyResolver : DefaultContractResolver
        {
            protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
            {
                var props = base.CreateProperties(type, memberSerialization);
                return props.Where(p => p.Writable).ToList();
            }
        }

        private static T LoadInternal(string filePath, bool checkBackup)
        {
            var typeName = typeof(T).Name;

            if (!string.IsNullOrEmpty(filePath))
            {
                //Console.WriteLine("{0} load started: {1}", typeName, filePath);

                try
                {
                    if (File.Exists(filePath))
                    {
                        using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            if (fileStream.Length > 0)
                            {
                                T settings;

                                using (var streamReader = new StreamReader(fileStream))
                                using (var jsonReader = new JsonTextReader(streamReader))
                                {
                                    var serializer = new JsonSerializer();
                                    serializer.Converters.Add(new StringEnumConverter());
                                    serializer.ObjectCreationHandling = ObjectCreationHandling.Replace;
                                    serializer.Error += (sender, e) =>
                                    {
                                        e.ErrorContext.Handled = true;
                                    };
                                    settings = serializer.Deserialize<T>(jsonReader);
                                }

                                if (settings == null)
                                {
                                    throw new Exception(typeName + " object is null.");
                                }

                               // Console.WriteLine("{0} load finished: {1}", typeName, filePath);

                                return settings;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    throw;
                }

                if (checkBackup)
                {
                    return LoadInternal(filePath + ".bak", false);
                }
            }

            //Console.WriteLine("{0} not found. Loading new instance.", typeName);

            return new T();
        }
    }
}