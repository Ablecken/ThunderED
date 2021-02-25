﻿using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using Blazored.Modal;
using Blazored.Modal.Services;
using Microsoft.AspNetCore.ProtectedBrowserStorage;
using Radzen;
using THDWebServer.Pages.Modals;
using ThunderED;

namespace THDWebServer.Classes
{
    public static class Extensions
    {
        public static List<T> ApplyAjaxFilters<T>(this List<T> list, LoadDataArgs args, out int count)
        {
            return ApplyAjaxFilters(list.AsEnumerable(), args, out count);
        }

        public static List<T> ApplyAjaxFilters<T>(this IEnumerable<T> list, LoadDataArgs args, out int count)
        {
            var query = list.AsQueryable();
            if (!string.IsNullOrEmpty(args.Filter))
                query = query.Where(args.Filter);
            count = query.Count();

            if (!string.IsNullOrEmpty(args.OrderBy))
                query = query.OrderBy(args.OrderBy);
            if (args.Skip.HasValue)
                query = query.Skip(args.Skip.Value);
            if (args.Top.HasValue)
                query = query.Take(args.Top.Value);
            return query.ToList();
        }

        public static async Task<T> GetAndClear<T>(this ProtectedSessionStorage storage, string name)
        {
            if (string.IsNullOrEmpty(name)) return default;
            var value = await storage.GetAsync<T>(name);
            await storage.DeleteAsync(name);
            return value;
        }

        public static async Task SafeSet(this ProtectedSessionStorage storage, string name, object value)
        {
            if(string.IsNullOrEmpty(name)) return;
            await storage.SetAsync(name, value);
        }

        public static async Task SafeSet(this ProtectedSessionStorage storage, KeyValuePair<string, object> pair)
        {
            if (string.IsNullOrEmpty(pair.Key)) return;
            await storage.SetAsync(pair.Key, pair.Value);
        }

        public static async Task<bool> ShowConfirm(this IModalService modal, string header = null, string message = null)
        {
            var options = new ModalOptions() { HideCloseButton = true };
            var parameters = new ModalParameters();
            parameters.Add("Message", message);
            return !(await modal.Show<Confirm>(header ?? LM.Get("webConfirmation"), parameters, options).Result).Cancelled;
        }
    }
}
