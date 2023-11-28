using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ThunderED.Thd
{
    public class ThdDiscordGroup : INotifyPropertyChanged, IIdentifiable
    {
        private string _name = "Group";
        private long? _directorCharacterId = 0;
        private string _discordRole = "Role";

        public long Id { get; set; }

        [Required]
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        [Required]
        public string DiscordRole
        {
            get => _discordRole;
            set { _discordRole = value; OnPropertyChanged(); }
        }
        
        public long? DirectorCharacterId
        {
            get => _directorCharacterId;
            set { _directorCharacterId = value; OnPropertyChanged(); }
        }

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
}
