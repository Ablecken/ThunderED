using System;
using System.Linq;
using System.Threading.Tasks;

using ThunderED.Classes;
using ThunderED.Helpers;
using ThunderED.Thd;

namespace ThunderED.Modules
{
    public partial class GroupsModule
    {
        public static Task<bool> HasWebAccess(WebAuthUserData usr)
        {
            return Task.FromResult(true);
        }

    }
}
