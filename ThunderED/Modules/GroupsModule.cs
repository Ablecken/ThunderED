using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using ThunderED.Classes;
using ThunderED.Classes.Enums;
using ThunderED.Helpers;
using ThunderED.Thd;

namespace ThunderED.Modules
{
    public partial class GroupsModule : AppModuleBase
    {
        public sealed override LogCat Category => LogCat.Groups;

        public override async Task Initialize()
        {
            await LogHelper.LogModule("Initializing Groups module...", Category);
            
            //await WebPartInitialization();
        }

        public override async Task Run(object prm)
        {
            if (IsRunning || !APIHelper.IsDiscordAvailable) return;
            IsRunning = true;
            try
            {

            }
            finally
            {
                IsRunning = false;
            }
        }
    }
}
