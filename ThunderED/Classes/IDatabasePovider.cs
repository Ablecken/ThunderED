﻿using System.Collections.Generic;
using System.Threading.Tasks;
using ThunderED.Json.Internal;

namespace ThunderED.Classes
{
    /// <summary>
    /// Interface for database providers
    /// </summary>
    public interface IDatabasePovider
    {
        Task<T> Query<T>(string table, string field, Dictionary<string, object> where);
        Task<T> Query<T>(string table, string field, string whereField, object whereData);
        Task<List<T>> QueryList<T>(string table, string field, string whereField, object whereData);
        Task<List<T>> QueryList<T>(string table, string field, Dictionary<string, object> where);

        Task Update(string table, string setField, object setData, string whereField, object whereData);
        Task Update(string table, string setField, object setData, Dictionary<string, object> where);

        Task Delete(string table, string whereField = null, object whereValue = null);
        Task Delete(string table, Dictionary<string, object> where);
        Task RunCommand(string query2, bool silent);
        Task<T> SelectCache<T>(object whereValue, int maxDays);
        Task UpdateCache<T>(T data, object id, int days = 1);
        Task PurgeCache();
        Task InsertOrUpdate(string table, Dictionary<string, object> values);
        Task CleanupNotificationsList();
        Task DeleteWhereIn(string table, string field, List<long> list, bool not);
        Task<bool> RunScript(string file);
        Task<List<object[]>> SelectData(string table, string[] fields, Dictionary<string, object> where = null);
        Task<bool> IsEntryExists(string table, Dictionary<string, object> where);
        Task Insert(string table, Dictionary<string, object> values);
    }
}