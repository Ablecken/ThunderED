﻿using System.Collections.Generic;
using Newtonsoft.Json;
using ThunderED.Classes;

namespace TED_ChatRelay.Classes
{
    public class RelaySetttings: SettingsBase<RelaySetttings>
    {
        public string EveLogsFolder { get; set; }
        public List<RelayChannel> RelayChannels { get; set; } = new List<RelayChannel>();
    }

    public class RelayChannel
    {
        public string EveChannelName { get; set; }
        public string Endpoint { get; set; }
        public string Code { get; set; }

        [JsonIgnore]
        public List<string> Pool { get; set; } = new List<string>();
    }
}
