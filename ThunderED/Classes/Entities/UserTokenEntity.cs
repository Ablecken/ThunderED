﻿using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace ThunderED.Classes.Entities
{
   /* [Obsolete("Maintained for upgrade possibility")]
    public class UserTokenEntity
    {
        public long CharacterId { get; set; }
        public string CharacterName { get; set; }
        public ulong DiscordUserId { get; set; }
        public string RefreshToken { get; set; }
        public string GroupName { get; set; }
        public string Permissions { get; set; }
        public int AuthState { get; set; }

        [JsonIgnore]
        public List<string> PermissionsList => string.IsNullOrEmpty(Permissions) ? new List<string>() : Permissions.Split(',').ToList();
    }*/
}
