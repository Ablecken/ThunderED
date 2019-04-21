﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ThunderED.Classes
{
    public static class Extension
    {
        public static T ReturnMinimum<T>(this T obj, T minimum)
        {
            return Convert.ToInt64(obj) == 0 ? minimum : obj;
        }

        public static string FirstLetterToUpper(this string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            if (str.Length > 1)
                return char.ToUpper(str[0]) + str.Substring(1);

            return str.ToUpper();
        }

        public static IEnumerable<string> SplitToLines(this string stringToSplit, int maxLineLength, string delimiter = " ", bool preserveDelimiter = false)
        {
            var words = stringToSplit.Split(delimiter);
            var line = new StringBuilder();
            var lastOne = words.LastOrDefault();
            foreach (var word in words)
            {
                if (word.Length + line.Length <= maxLineLength)
                {
                    line.Append(word + delimiter);
                }
                else
                {
                    if (line.Length > 0)
                    {
                        var res2 = line.ToString();
                        if ((!preserveDelimiter || lastOne == word) && res2.EndsWith(delimiter))
                            res2 = res2.Substring(0, res2.Length - delimiter.Length);
                        yield return res2;
                        line.Clear();
                    }
                    var overflow = word;
                    while (overflow.Length > maxLineLength)
                    {
                        yield return overflow.Substring(0, maxLineLength);
                        overflow = overflow.Substring(maxLineLength);
                    }
                    line.Append(overflow + delimiter);
                }
            }

            var res = line.ToString();
            if (res.EndsWith(delimiter))
                res = res.Substring(0, res.Length - delimiter.Length);
            yield return res;
        }

        public static IEnumerable<string> SplitBy(this string str, int chunkSize, bool remainingInFront = false)
        {
            var count = (int) Math.Ceiling(str.Length/(double) chunkSize);
            int Start(int index) => remainingInFront ? str.Length - (count - index) * chunkSize : index * chunkSize;
            int End(int index) => Math.Min(str.Length - Math.Max(Start(index), 0), Math.Min(Start(index) + chunkSize - Math.Max(Start(index), 0), chunkSize));
            return Enumerable.Range(0, count).Select(i => str.Substring(Math.Max(Start(i), 0),End(i)));
        }


        public static string ToKMB(this long num)
        {
            if (num > 999999999 || num < -999999999 )
            {
                return num.ToString("0,,,.###B", CultureInfo.InvariantCulture);
            }
            else
            if (num > 999999 || num < -999999 )
            {
                return num.ToString("0,,.##M", CultureInfo.InvariantCulture);
            }
            else
            if (num > 999 || num < -999)
            {
                return num.ToString("0,.#K", CultureInfo.InvariantCulture);
            }
            else
            {
                return num.ToString(CultureInfo.InvariantCulture);
            }
        }

        public static DateTime StartOfWeek(this DateTime dt, DayOfWeek startOfWeek)
        {
            int diff = (7 + (dt.DayOfWeek - startOfWeek)) % 7;
            return dt.AddDays(-1 * diff).Date;
        }

        public static void AddOnlyNew<TKey, TValue>(this IDictionary<TKey, TValue> dic, TKey key, TValue value)
        {
            if (!dic.ContainsKey(key))
                dic.Add(key, value);
        }

        public static IEnumerable<T> TakeSmart<T>(this IEnumerable<T> list, int count)
        {
            if (list == null) return null;
            return list.Count() > count ? list.Take(count): list;
        }

        public static IEnumerable<string> Split(this string str, int chunkSize)
        {
            IEnumerable<string> retVal = Enumerable.Range(0, str.Length / chunkSize)
                .Select(i => str.Substring(i * chunkSize, chunkSize));

            if (str.Length % chunkSize > 0)
                retVal = retVal.Append(str.Substring(str.Length / chunkSize * chunkSize, str.Length % chunkSize));

            return retVal;
        }

        public static void AddOrUpdateEx<TKey, TValue>(this IDictionary<TKey, TValue> dic, TKey key, TValue value)
        {
            if (dic.ContainsKey(key))
                dic[key] = value;
            else dic.Add(key, value);
        }

        public static TValue GetOrNull<TKey, TValue>(this IDictionary<TKey, TValue> dic, TKey key)
            where TValue: class
        {
            return dic.ContainsKey(key) ? dic[key] : null;
        }

        public static string FixedLength(this string value, int length)
        {
            if (string.IsNullOrEmpty(value)) return FillSpaces("", length);
            return length < value.Length ? value.Substring(0, length) : (value.Length < length ? FillSpaces(value, length) : value);
        }

        private static string FillSpaces(this string value, int length)
        {
            if (value.Length >= length) return value;
            var sb = new StringBuilder();
            sb.Append(value);
            for (int i = value.Length; i < length; i++)
                sb.Append(" ");
            return sb.ToString();
        }

        public static string FillSpacesBefore(this string value, int length)
        {
            if (value.Length >= length) return value;
            var sb = new StringBuilder();
            for (int i = 0; i < length - value.Length; i++)
                sb.Append(" ");
            sb.Append(value);
            return sb.ToString();
        }
    }

}
