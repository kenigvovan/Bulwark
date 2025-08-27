using System.Linq;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.MathTools;
using Vintagestory.API.Common.Entities;
using System;
using System.Collections.Concurrent;
using Vintagestory.GameContent;
using System.Text;


namespace Bulwark {
    public class FortificationModSystem : ModSystem {

        //=======================
        // D E F I N I T I O N S
        //=======================
            
        protected HashSet<Stronghold> strongholds = new();
        protected ICoreAPI api;
        protected ICoreServerAPI sapi => this.api as ICoreServerAPI;

        public delegate void NewStrongholdDelegate(Stronghold stronghold);
        public event NewStrongholdDelegate StrongholdAdded;
        public ConcurrentDictionary<int, long> GroupsCallbacks = new ConcurrentDictionary<int, long>();
        public ConcurrentDictionary<string, long> PlayersCallbacks = new ConcurrentDictionary<string, long>();
        private object lockVar = new();

        //===============================
        // I N I T I A L I Z A T I O N S
        //===============================

        public override void Start(ICoreAPI api) {
                base.Start(api);
                this.api = api;
            }


        public override void StartServerSide(ICoreServerAPI api) {

            base.StartServerSide(api);
            api.Event.DidPlaceBlock += this.PlaceBlockEvent;
            api.Event.DidBreakBlock += this.BreakBlockEvent;
            api.Event.PlayerDeath   += this.PlayerDeathEvent;
            //api.Event.PlayerDisconnect += this.PlayerDisconnectEvent;
            //api.Event.PlayerNowPlaying += this.PlayerNowPlayingEvent;
            api.Event.CanPlaceOrBreakBlock += this.CanPlaceBreakBlockEvent;
            api.Event.Timer(() =>
            {
                foreach (var stronghold in strongholds)
                {
                    int? groupID = stronghold.GroupUID.HasValue ? stronghold.GroupUID.Value : null;
                    string playerUid = stronghold.PlayerUID;
                    if (stronghold == null)
                    {
                        api.Logger.Error("Found null stronghold in the list, this should not happen!");
                        continue;
                    }
                    if(playerUid == null)
                    {
                        api.Logger.Error("Found null playerUid in stronghold, this should not happen!");
                        continue;
                    }

                    if (groupID == null)
                    {
                        var onlinePlayer = this.api.World.AllOnlinePlayers
                            .Where(p => p.PlayerUID == playerUid)
                            .FirstOrDefault();

                        if (onlinePlayer == null)
                        {
                            //player was offline but now is playing
                            if (!stronghold.AllPlayersOffline)
                            {
                                //do not register twice
                                if (PlayersCallbacks.TryGetValue(playerUid, out long callbackId))
                                {
                                    continue;
                                }
                                var newCallbackId = onlinePlayer.Entity.Api.Event.RegisterCallback((dt =>
                                {
                                    stronghold.AllPlayersOffline = true;
                                    if (playerUid != null)
                                    {
                                        PlayersCallbacks.Remove(playerUid, out long _);
                                    }
                                }), 1000 * BulwarkModSystem.config.SecondsBeforeStrongholdOffline);
                                if (PlayersCallbacks == null)
                                {
                                    api.Logger.Error("PlayersCallbacks is null, this should not happen!");
                                    continue;
                                }
                                PlayersCallbacks[playerUid] = newCallbackId;
                            }
                        }
                        else
                        {
                            if (stronghold.AllPlayersOffline)
                            {
                                if (PlayersCallbacks.TryGetValue(playerUid, out long callbackId2))
                                {
                                    onlinePlayer.Entity.Api.Event.UnregisterCallback(callbackId2);
                                }
                                stronghold.AllPlayersOffline = false;
                            }
                        }
                        continue;
                    }

                    if (groupID.HasValue)
                    {
                        if (PlayersCallbacks == null)
                        {
                            api.Logger.Error("GroupsCallbacks is null, this should not happen!");
                            continue;
                        }
                        if (this.sapi.Groups.PlayerGroupsById.TryGetValue(groupID.Value, out var group))
                        {
                            int playerOnlineCount = group.OnlinePlayers.Count;
                            if (playerOnlineCount <= 0 && !stronghold.AllPlayersOffline)
                            {
                                //do not register twice
                                if (GroupsCallbacks.TryGetValue(groupID.Value, out long callbackId))
                                {
                                    continue;
                                }
                                var newCallbackId = this.api.Event.RegisterCallback((dt =>
                                {
                                    stronghold.AllPlayersOffline = true;
                                }), 1000 * BulwarkModSystem.config.SecondsBeforeStrongholdOffline);
                            }
                            else if (playerOnlineCount > 0 && stronghold.AllPlayersOffline)
                            {
                                if (GroupsCallbacks.TryGetValue(groupID.Value, out long callbackId))
                                {
                                    this.api.Event.UnregisterCallback(callbackId);
                                }
                                stronghold.AllPlayersOffline = false;
                            }
                            else if (playerOnlineCount > 0 && !stronghold.AllPlayersOffline)
                            {
                                if (GroupsCallbacks.TryGetValue(groupID.Value, out long callbackId))
                                {
                                    this.api.Event.UnregisterCallback(callbackId);
                                }
                            }
                        }
                    }
                }
            }, BulwarkModSystem.config.SecondCheckForOfflineStronghold);


            api.ChatCommands
                .Create("stronghold")
                .RequiresPrivilege(Privilege.chat)
                .BeginSubCommand("name")
                    .WithDescription("Name the claimed area you are in")
                    .WithArgs(api.ChatCommands.Parsers.Word("name"))
                    .HandleWith(
                        args => {
                                
                            string callerUID = args.Caller.Player.PlayerUID;
                            if (this.strongholds?.FirstOrDefault(
                                stronghold => stronghold.PlayerUID == callerUID
                                && stronghold.Area.Contains(args.Caller.Player.Entity.ServerPos.AsBlockPos),
                                null
                            ) is Stronghold area) {
                                    
                                area.Name = args[0].ToString();
                                this.api.World.BlockAccessor.GetBlockEntity(area.Center).MarkDirty();

                            } else TextCommandResult.Success(Lang.Get("You're not in a stronghold you claimed"));
                            return TextCommandResult.Success();

                        } // ..
                    )
                .EndSubCommand()
                .BeginSubCommand("league")
                    .WithDescription("Affiliate the claimed area you are in with a group")
                    .WithArgs(api.ChatCommands.Parsers.Word("group name"))
                    .HandleWith(
                        args => {
                                
                            string callerUID = args.Caller.Player.PlayerUID;
                            if (this.strongholds?.FirstOrDefault(
                                stronghold => stronghold.PlayerUID == callerUID
                                && stronghold.Area.Contains(args.Caller.Player.Entity.ServerPos.AsBlockPos),
                                null
                            ) is Stronghold area) {
                                if ((this.api as ICoreServerAPI).Groups.GetPlayerGroupByName(args[0].ToString()) is PlayerGroup playerGroup) {

                                    area.ClaimGroup(playerGroup);
                                    this.api.World.BlockAccessor.GetBlockEntity(area.Center).MarkDirty();

                                } else TextCommandResult.Success(Lang.Get("No such group found"));
                            } else TextCommandResult.Success(Lang.Get("You're not in a stronghold you claimed"));
                            return TextCommandResult.Success();

                        } // ..
                    ) 
                .EndSubCommand()
                .BeginSubCommand("stopleague")
                    .WithDescription("Stops the affiliation with a group")
                    .WithArgs(api.ChatCommands.Parsers.Word("group name"))
                    .HandleWith(
                        args => {
                                
                            string callerUID = args.Caller.Player.PlayerUID;
                            if (this.strongholds?.FirstOrDefault(
                                stronghold => stronghold.PlayerUID == callerUID
                                && stronghold.Area.Contains(args.Caller.Player.Entity.ServerPos.AsBlockPos),
                                null
                            ) is Stronghold area) {
                                if ((this.api as ICoreServerAPI).Groups.GetPlayerGroupByName(args[0].ToString()) is PlayerGroup playerGroup) {

                                    area.UnclaimGroup();
                                    this.api.World.BlockAccessor.GetBlockEntity(area.Center).MarkDirty();

                                } else TextCommandResult.Success(Lang.Get("No such group found"));
                            } else TextCommandResult.Success(Lang.Get("You're not in a stronghold you claimed"));
                            return TextCommandResult.Success();

                        } // ..
                    )
                    ;
            api.ChatCommands.Create("bulwark").RequiresPrivilege(Privilege.controlserver)
                .BeginSubCommand("show")
                .HandleWith(
                args =>
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (var stronghold in this.strongholds)
                    {
                        sb.AppendLine(string.Format("Name: {0}, owner: {1}, groupId: {2}, allOffline: {3}, center: {4}",
                            stronghold.Name, stronghold.PlayerUID == null ? "null" : stronghold.PlayerUID, stronghold.GroupUID.HasValue ? stronghold.GroupUID.Value : "null",
                            stronghold.AllPlayersOffline, stronghold.Center.ToString()));
                    }
                    return TextCommandResult.Success(sb.ToString());
                })
                .EndSubCommand();
        }


        //===============================
        // I M P L E M E N T A T I O N S
        //===============================

        private void PlaceBlockEvent(IServerPlayer byPlayer, int oldblockId, BlockSelection blockSel, ItemStack withItemStack) 
        {
            if (blockSel    != null && byPlayer != null && !(withItemStack?.Collectible.Attributes?["siegeEquipment"]?.AsBool() ?? false)
                && !this.HasPrivilege(byPlayer, blockSel, out _)
            ) {

                var cloneIS = withItemStack.Clone();
                cloneIS.StackSize = 1;
                byPlayer.InventoryManager.TryGiveItemstack(cloneIS);
                //byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack = withItemStack;
                byPlayer.InventoryManager.ActiveHotbarSlot.MarkDirty();
                byPlayer.Entity.World.BlockAccessor.SetBlock(oldblockId, blockSel.Position);
                byPlayer.SendIngameError("stronghold-nobuildprivilege");

            }
        }


        private void BreakBlockEvent(IServerPlayer byPlayer, int oldblockId, BlockSelection blockSel) 
        {
            if (blockSel != null && byPlayer != null && !this.HasPrivilege(byPlayer, blockSel, out Stronghold stronghold)) 
            {
                if (byPlayer.Entity.World.Calendar.TotalHours - byPlayer.Entity.WatchedAttributes.GetDouble("strongholdBreakWarning") < 1) 
                {
                    if(stronghold.AllPlayersOffline)
                    {
                        return;
                    }
                    stronghold.IncreaseSiegeIntensity(0.5f);
                } 
                else 
                {
                    if (stronghold.AllPlayersOffline)
                    {
                        return;
                    }
                    byPlayer.Entity.WatchedAttributes.SetDouble("strongholdBreakWarning", byPlayer.Entity.World.Calendar.TotalHours);
                    byPlayer.SendIngameError("stronghold-nobreakprivilege-warning");
                }
            }
        }

        private bool CanPlaceBreakBlockEvent(IServerPlayer byPlayer, BlockSelection blockSel, out string claimant)
        {
            foreach (Stronghold stronghold in this.strongholds)
                if (stronghold.Area.Contains(blockSel.Position))
                {
                   if(stronghold.AllPlayersOffline)
                    {
                        claimant = "No players online.";
                        return false;
                    }
                }
            claimant = "";
            return true;
        }


        private void PlayerDeathEvent(IServerPlayer forPlayer, DamageSource damageSource) 
        {
            if (this.strongholds.FirstOrDefault(area => area.PlayerUID == forPlayer.PlayerUID 
            || (forPlayer.Groups?.Any(group => group.GroupUid == area.GroupUID) ?? false), null) is Stronghold stronghold) 
            {
                Entity byEntity = damageSource.CauseEntity ?? damageSource.SourceEntity;
                if(byEntity is null)
                {
                    return;
                }
                if (byEntity is EntityPlayer playerCause
                    && stronghold.Area.Contains(byEntity.ServerPos.AsBlockPos)
                    && !(playerCause.Player.Groups?.Any(group => group.GroupUid == stronghold.GroupUID) ?? false
                        || playerCause.PlayerUID == stronghold.PlayerUID)
                ) stronghold.IncreaseSiegeIntensity(1f, byEntity);

                //todo byplayer is null
                else if (byEntity.WatchedAttributes.GetString("guardedPlayerUid") is string playerUid
                    && this.api.World.PlayerByUid(playerUid) is IPlayer byPlayer
                    && stronghold.Area.Contains(byEntity.ServerPos.AsBlockPos)
                    && !(byPlayer.Groups?.Any(group => group.GroupUid == stronghold.GroupUID) ?? false
                        || byPlayer.PlayerUID == stronghold.PlayerUID)
                    ) stronghold.IncreaseSiegeIntensity(1f, damageSource.CauseEntity);
            }
        }


        private void PlayerDisconnectEvent(IServerPlayer byPlayer)
        {
            lock (lockVar)
            {
                foreach (var stronghold in strongholds)
                {
                    if (stronghold.PlayerUID == byPlayer.PlayerUID && stronghold.GroupUID == null)
                    {
                        var newCallbackId = byPlayer.Entity.Api.Event.RegisterCallback((dt =>
                        {
                            stronghold.AllPlayersOffline = true;
                            //(byPlayer.Entity.Api as ICoreServerAPI).Logger.Notification("offline now 1");
                        }), 1000 * BulwarkModSystem.config.SecondsBeforeStrongholdOffline);
                        PlayersCallbacks[byPlayer.PlayerUID] = newCallbackId;
                    }
                    //player is member of the stronghold group
                    if (byPlayer.Groups?.Any(group => group.GroupUid == stronghold.GroupUID) ?? false)
                    {
                        var group = (byPlayer.Entity.Api as ICoreServerAPI).Groups.GetPlayerGroupByName(stronghold.GroupName);
                        if (group.OnlinePlayers.Count - 1 <= 0)
                        {
                            if (GroupsCallbacks.TryGetValue(group.Uid, out long callbackId))
                            {
                                byPlayer.Entity.Api.Event.UnregisterCallback(callbackId);
                            }
                            stronghold.LastGroupMemberLeft = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            var newCallbackId = byPlayer.Entity.Api.Event.RegisterCallback((dt =>
                            {
                                var group = (byPlayer.Entity.Api as ICoreServerAPI).Groups.GetPlayerGroupByName(stronghold.GroupName);
                                if (group != null && group.OnlinePlayers.Count < 1)
                                {
                                    stronghold.AllPlayersOffline = true;
                                   // (byPlayer.Entity.Api as ICoreServerAPI).Logger.Notification("offline now 2");
                                }
                            }), 1000 * BulwarkModSystem.config.SecondsBeforeStrongholdOffline);
                            GroupsCallbacks[group.Uid] = newCallbackId;
                        }
                    }
                }
            }       
        }

        private void PlayerNowPlayingEvent(
           IServerPlayer byPlayer
       )
        {
            lock (lockVar)
            {
                foreach (var stronghold in strongholds)
                {
                    if (stronghold.PlayerUID == byPlayer.PlayerUID)
                    {
                        if (PlayersCallbacks.TryGetValue(byPlayer.PlayerUID, out long callbackId))
                        {
                            byPlayer.Entity.Api.Event.UnregisterCallback(callbackId);
                        }
                        stronghold.AllPlayersOffline = false;
                        //(byPlayer.Entity.Api as ICoreServerAPI).Logger.Notification("not offline now 1");
                        continue;
                    }
                    //player is member of the stronghold group
                    if (byPlayer.Groups?.Any(group => group.GroupUid == stronghold.GroupUID) ?? false)
                    {
                        var group = (byPlayer.Entity.Api as ICoreServerAPI).Groups.GetPlayerGroupByName(stronghold.GroupName);

                        if (GroupsCallbacks.TryGetValue(group.Uid, out long callbackId))
                        {
                            byPlayer.Entity.Api.Event.UnregisterCallback(callbackId);
                        }
                        stronghold.LastGroupMemberLeft = 0;
                        stronghold.AllPlayersOffline = false;
                        //(byPlayer.Entity.Api as ICoreServerAPI).Logger.Notification("not offline now 2");
                    }
                }
            }
        } // void ..

        public bool TryRegisterStronghold(Stronghold stronghold) {

                stronghold.Api = this.api;
                if (this.strongholds.Contains(stronghold))                              return true;
                else if (this.strongholds.Any(x => x.Area.Intersects(stronghold.Area))) return false;
                else this.strongholds.Add(stronghold);

                stronghold.UpdateRef = stronghold.Api
                    .Event
                    .RegisterGameTickListener(stronghold.Update, 2000, 1000);

                this.StrongholdAdded?.Invoke(stronghold);
                return true;
            } // void ..


            public void RemoveStronghold(Stronghold stronghold) {
                if (stronghold is not null) {
                    if (stronghold.UpdateRef.HasValue) stronghold.Api.Event.UnregisterGameTickListener(stronghold.UpdateRef.Value);
                    this.strongholds.Remove(stronghold);
                } // if ..
            } // void ..


            public bool TryGetStronghold(BlockPos pos, out Stronghold value) {
                if (this.strongholds?.FirstOrDefault(stronghold => stronghold.Area.Contains(pos), null) is Stronghold area) {
                    value = area;
                    return true;
                } else  {
                    value = null;
                    return false;
                } // if ..
            } // bool ..


            public bool HasPrivilege(
                IPlayer byPlayer,
                BlockSelection blockSel,
                out Stronghold area
            ) {

                area = null;

                if (this.strongholds == null)     return true;
                if (this.strongholds?.Count == 0) return true;

                bool privilege = true;
                foreach (Stronghold stronghold in this.strongholds)
                    if (stronghold.Area.Contains(blockSel.Position)) {

                        area = stronghold;

                        if (stronghold.PlayerUID == null)               return true;
                        if (stronghold.PlayerUID == byPlayer.PlayerUID) return true;
                        if (!(byPlayer.Groups?.Any(group => group.GroupUid == stronghold.GroupUID) ?? false))
                            return false;

                    } // foreach ..

                return privilege;

            } // bool ..
    } // class ..
} // namespace ..
