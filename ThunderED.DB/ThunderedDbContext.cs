﻿using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using ThunderED.Json;
using ThunderED.Thd;

namespace ThunderED
{
    public class ThunderedDbContext : DbContext
    {
        public DbSet<ThdAuthUser> Users { get; set; }
        public DbSet<ThdToken> Tokens { get; set; }
        public DbSet<ThdMiningNotification> MiningNotifications { get; set; }
        //public DbSet<JsonClasses.SystemName> Systems { get; set; }
        //public DbSet<JsonClasses.ConstellationData> Constellations { get; set; }
        //public DbSet<JsonClasses.RegionData> Regions { get; set; }

        public ThunderedDbContext()
        {
            //Database.EnsureCreated();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            #region ThdAuthUser
            modelBuilder.Entity<ThdAuthUser>().HasIndex(u => u.CharacterId).IsUnique();
            modelBuilder.Entity<ThdAuthUser>().HasKey(u => u.Id);
            modelBuilder.Entity<ThdAuthUser>().ToTable("auth_users");

            modelBuilder.Entity<ThdAuthUser>().Property(a => a.Id).HasColumnName("Id").ValueGeneratedOnAdd();
            modelBuilder.Entity<ThdAuthUser>().Property(a => a.CharacterId).HasColumnName("characterID");
            modelBuilder.Entity<ThdAuthUser>().Property(a => a.DiscordId).HasColumnName("discordID");
            modelBuilder.Entity<ThdAuthUser>().Property(a => a.GroupName).HasColumnName("groupName");
            modelBuilder.Entity<ThdAuthUser>().Property(a => a.RefreshToken).HasColumnName("refreshToken");
            modelBuilder.Entity<ThdAuthUser>().Property(a => a.AuthState).HasColumnName("authState");
            modelBuilder.Entity<ThdAuthUser>().Property(a => a.Data).HasColumnName("data");
            modelBuilder.Entity<ThdAuthUser>().Property(a => a.RegCode).HasColumnName("reg_code");
            modelBuilder.Entity<ThdAuthUser>().Property(a => a.CreateDate).HasColumnName("reg_date");
            modelBuilder.Entity<ThdAuthUser>().Property(a => a.DumpDate).HasColumnName("dump_date");
            modelBuilder.Entity<ThdAuthUser>().Property(a => a.MainCharacterId).HasColumnName("main_character_id");
            modelBuilder.Entity<ThdAuthUser>().Property(a => a.LastCheck).HasColumnName("last_check");
            modelBuilder.Entity<ThdAuthUser>().Property(a => a.Ip).HasColumnName("ip");

            #endregion

            #region ThdToken
            modelBuilder.Entity<ThdToken>().HasIndex(u => u.Id).IsUnique();
            modelBuilder.Entity<ThdToken>().HasKey(u => u.Id);
            modelBuilder.Entity<ThdToken>().HasIndex(u => u.CharacterId);
            modelBuilder.Entity<ThdToken>().ToTable("tokens");

            modelBuilder.Entity<ThdToken>().Property(a => a.Id).HasColumnName("id").ValueGeneratedOnAdd();
            modelBuilder.Entity<ThdToken>().Property(a => a.CharacterId).HasColumnName("character_id");
            modelBuilder.Entity<ThdToken>().Property(a => a.Token).HasColumnName("token");
            modelBuilder.Entity<ThdToken>().Property(a => a.Type).HasColumnName("type");
            modelBuilder.Entity<ThdToken>().HasOne(a => a.User).WithMany(a => a.Tokens)
                .HasForeignKey(a => a.CharacterId).HasPrincipalKey(a => a.CharacterId);
            #endregion

            #region ThdMiningNotification

            modelBuilder.Entity<ThdMiningNotification>().HasIndex(u => u.CitadelId).IsUnique();
            modelBuilder.Entity<ThdMiningNotification>().HasKey(u => u.CitadelId);
            modelBuilder.Entity<ThdMiningNotification>().ToTable("mining_notifications");

            modelBuilder.Entity<ThdMiningNotification>().Property(a => a.CitadelId).HasColumnName("citadel_id").ValueGeneratedNever();
            modelBuilder.Entity<ThdMiningNotification>().Property(a => a.OreComposition).HasColumnName("ore_composition");
            modelBuilder.Entity<ThdMiningNotification>().Property(a => a.Operator).HasColumnName("operator");
            modelBuilder.Entity<ThdMiningNotification>().Property(a => a.Date).HasColumnName("date");

            #endregion

        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            switch (DbSettingsManager.Settings.Database.DatabaseProvider.ToLower())
            {
                case "sqlite":
                    optionsBuilder.UseSqlite($"Data Source={Path.Combine(DbSettingsManager.DataDirectory, DbSettingsManager.Settings.Database.DatabaseFile)}");
                    break;
                case "mysql":
                    var cstring =
                        $"server={DbSettingsManager.Settings.Database.ServerAddress};UserId={DbSettingsManager.Settings.Database.UserId};Password={DbSettingsManager.Settings.Database.Password};database={DbSettingsManager.Settings.Database.DatabaseName};";
                    var v = ServerVersion.AutoDetect(cstring);
                    optionsBuilder.UseMySql(cstring, v);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
