﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ThunderED.Helpers
{

    public static class DecompositionHelper
    {
        public static async Task<Dictionary<long, double>> GetPrices(double percent, List<long> ids)
        {
            try
            {
                var result = new Dictionary<long, double>();
                var tmp = await DbHelper.GetCustomSchemeValues(ids);
                var compIds = tmp.Select(a => a.ItemId).Distinct().ToList();
                var compPrices = await APIHelper.ESIAPI.GetFuzzPrice("Decomp", compIds);

                foreach (var id in ids)
                {
                    var items = tmp.Where(a => a.Id == id).ToList();
                    var resultValue = 0d;
                    foreach (var item in items)
                    {
                        var price = compPrices.FirstOrDefault(a => a.Id == item.ItemId);
                        resultValue += price == null
                            ? 0d
                            : price.Sell * (item.Quantity * (percent / 100d));
                    }

                    result.Add(id, resultValue);
                }

                return result;
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx(ex);
                return null;
            }
        }
    }
}
