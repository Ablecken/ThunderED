﻿using System;

namespace ThunderED.Json
{
    public partial class JsonClasses
    {
        public class AllianceData
        {
            public string name { get; set; }
            public string ticker { get; set; }
            public long creator_id { get; set; }
            public long creator_corporation_id { get; set; }
            public long executor_corporation_id { get; set; }
            public DateTime date_founded { get; set; }
            public long? faction_id { get; set; }
        }

        internal class AllianceIDLookup
        {
            public long[] alliance;
        }

        internal class FactionData
        {
            public long corporation_id;
            public string description;
            public long faction_id;
            public bool is_unique;
            public string name;
            public double size_factor;
            public long solar_system_id;
            public int station_count;
            public int station_system_count;
        }
    }
}