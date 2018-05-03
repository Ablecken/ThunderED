﻿namespace ThunderED.Json.ZKill
{
    public partial class JsonZKill
    {
        public class CharacterStats
        {
            public int allTimeSum { get; set; }
            public bool calcTrophies { get; set; }
            public int dangerRatio { get; set; }
            public int gangRatio { get; set; }
            public int id { get; set; }
            public long iskDestroyed { get; set; }
            public long iskLost { get; set; }
            public int nextTopRecalc { get; set; }
            public int pointsDestroyed { get; set; }
            public int pointsLost { get; set; }
            public int sequence { get; set; }
            public int shipsDestroyed { get; set; }
            public int shipsLost { get; set; }
            public int soloKills { get; set; }
            public int soloLosses { get; set; }
            public Topalltime[] topAllTime { get; set; }
            public Trophies trophies { get; set; }
            public string type { get; set; }
            public Activepvp activepvp { get; set; }
            public Info info { get; set; }
            public Toplist[] topLists { get; set; }
            public int[] topIskKillIDs { get; set; }
        }
    }
}