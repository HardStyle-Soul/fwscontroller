using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("FWSController", "HardStyle", "1.1.1")]
    [Description("Automated firework shows with RF triggers")]
    public class FWSController : RustPlugin
    {
        [PluginReference] private Plugin CopyPaste;

        private const string PermUse = "fwscontroller.use";
        private const string RemoteShortname = "rf.detonator";
        private const float PasteTimeoutSeconds = 60f;
        private const float RaycastDistance = 1000f;
        private const float GroupStepDelay = 1.4f;
        private const float SequenceStepDelay = 37.5f;
        private const float CleanupDelayAfterLastTrigger = 50f;
        private const float RfPulseSeconds = 0.25f;

        private static readonly string[] DefaultPasteArgs = { "stability", "false" };
        private static readonly string[] CopyPasteNames = { "CopyPaste", "Copy Paste" };
        private static readonly HashSet<string> AllowedShortPrefabs = new HashSet<string>
        {
            "generator.small",
            "electrical.combiner.deployed"
        };

        private static readonly Dictionary<int, int[][]> ShowSequences = new Dictionary<int, int[][]>
        {
            [1] = Groups(Single(101)),
            [2] = Groups(Single(201)),
            [3] = Groups(Single(301), Single(302)),
            [4] = Groups(Single(401)),
            [5] = Groups(Group(501, 502), Single(503), Single(504)),
            [6] = Groups(Group(601, 602), Group(603, 604), Group(605, 606), Single(607), Single(608)),
            [7] = Groups(Group(701, 702), Group(703, 704), Group(705, 706), Group(707, 708), Group(709, 710), Single(711)),
            [8] = Groups(Single(801), Single(802)),
            [9] = Groups(Single(901)),
            [10] = Groups(Group(1001, 1002), Group(1003, 1004), Group(1005, 1006)),
            [11] = Groups(Group(1101, 1102), Single(1103), Single(1104), Single(1105)),
            [12] = Groups(Single(1201), Single(1202), Single(1203), Single(1204), Single(1205), Single(1206), Single(1207), Single(1208), Single(1209), Single(1210)),
            [13] = Groups(Single(1301), Single(1302), Single(1303), Single(1304), Single(1305)),
            [14] = Groups(Single(1401), Single(1402), Single(1403), Single(1404), Single(1405), Single(1406)),
            [15] = Groups(Single(1501)),
            [16] = Groups(Single(1601), Single(1602), Single(1603), Single(1604)),
            [17] = Groups(Group(1701, 1702)),
            [18] = Groups(Single(1801)),
            [19] = Groups(Single(1901), Single(1902), Single(1903)),
            [20] = Groups(Single(2001), Single(2002), Single(2003), Single(2004), Single(2005), Single(2006), Single(2007)),
            [21] = Groups(Single(2101), Single(2102), Single(2103), Single(2104))
        };

        private FieldInfo detonatorFrequencyField;

        private sealed class ShowInstance
        {
            public BasePlayer Player;
            public int ShowId;
            public int StartFrequency;
            public string Filename;
            public ulong RemoteItemUid;
            public bool Started;
            public bool PasteFinished;
            public bool RemoteGiven;
            public readonly List<BaseEntity> Entities = new List<BaseEntity>();
            public readonly Dictionary<int, List<RFReceiver>> ReceiversByFrequency = new Dictionary<int, List<RFReceiver>>();
            public readonly List<Timer> Timers = new List<Timer>();
        }

        private readonly Dictionary<int, ShowInstance> activeShows = new Dictionary<int, ShowInstance>();
        private readonly Dictionary<string, int> pendingByFilename = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ulong, ShowInstance> activeByRemoteUid = new Dictionary<ulong, ShowInstance>();

        private static int[] Single(int frequency) => new[] { frequency };
        private static int[] Group(params int[] frequencies) => frequencies;
        private static int[][] Groups(params int[][] groups) => groups;

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
        }

        private void OnServerInitialized()
        {
            FindCopyPaste();
            detonatorFrequencyField = typeof(Detonator).GetField("frequency", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private void Unload()
        {
            foreach (var show in new List<ShowInstance>(activeShows.Values))
                Cleanup(show, false);
        }

        [ChatCommand("fwsinfo")]
        private void CmdFireworkShows(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            player.ChatMessage(
                "<size=18><color=#FFA500><b>=== Firework Shows Help ===</b></color></size>\n" +
                "<color=#FFD700><b>Spawn:</b></color> <color=#00FFFF>/fws1</color> - <color=#00FFFF>/fws21</color>\n" +
                "<color=#FFD700><b>Delete:</b></color> <color=#FF7F7F>/fws1del</color> - <color=#FF7F7F>/fws21del</color>\n" +
                "<color=#FFD700><b>Height example:</b></color> <color=#00FFFF>/fws6 height 3</color>\n" +
                "<color=#ADFF2F>Use the given RF transmitter to start the show.</color>\n" +
                "<color=#FFB6C1>Required Permission: fwscontroller.use</color>");
        }

        [ChatCommand("fws1")] private void Cmd1(BasePlayer p, string c, string[] a) => TrySpawn(p, 1, a);
        [ChatCommand("fws2")] private void Cmd2(BasePlayer p, string c, string[] a) => TrySpawn(p, 2, a);
        [ChatCommand("fws3")] private void Cmd3(BasePlayer p, string c, string[] a) => TrySpawn(p, 3, a);
        [ChatCommand("fws4")] private void Cmd4(BasePlayer p, string c, string[] a) => TrySpawn(p, 4, a);
        [ChatCommand("fws5")] private void Cmd5(BasePlayer p, string c, string[] a) => TrySpawn(p, 5, a);
        [ChatCommand("fws6")] private void Cmd6(BasePlayer p, string c, string[] a) => TrySpawn(p, 6, a);
        [ChatCommand("fws7")] private void Cmd7(BasePlayer p, string c, string[] a) => TrySpawn(p, 7, a);
        [ChatCommand("fws8")] private void Cmd8(BasePlayer p, string c, string[] a) => TrySpawn(p, 8, a);
        [ChatCommand("fws9")] private void Cmd9(BasePlayer p, string c, string[] a) => TrySpawn(p, 9, a);
        [ChatCommand("fws10")] private void Cmd10(BasePlayer p, string c, string[] a) => TrySpawn(p, 10, a);
        [ChatCommand("fws11")] private void Cmd11(BasePlayer p, string c, string[] a) => TrySpawn(p, 11, a);
        [ChatCommand("fws12")] private void Cmd12(BasePlayer p, string c, string[] a) => TrySpawn(p, 12, a);
        [ChatCommand("fws13")] private void Cmd13(BasePlayer p, string c, string[] a) => TrySpawn(p, 13, a);
        [ChatCommand("fws14")] private void Cmd14(BasePlayer p, string c, string[] a) => TrySpawn(p, 14, a);
        [ChatCommand("fws15")] private void Cmd15(BasePlayer p, string c, string[] a) => TrySpawn(p, 15, a);
        [ChatCommand("fws16")] private void Cmd16(BasePlayer p, string c, string[] a) => TrySpawn(p, 16, a);
        [ChatCommand("fws17")] private void Cmd17(BasePlayer p, string c, string[] a) => TrySpawn(p, 17, a);
        [ChatCommand("fws18")] private void Cmd18(BasePlayer p, string c, string[] a) => TrySpawn(p, 18, a);
        [ChatCommand("fws19")] private void Cmd19(BasePlayer p, string c, string[] a) => TrySpawn(p, 19, a);
        [ChatCommand("fws20")] private void Cmd20(BasePlayer p, string c, string[] a) => TrySpawn(p, 20, a);
        [ChatCommand("fws21")] private void Cmd21(BasePlayer p, string c, string[] a) => TrySpawn(p, 21, a);

        [ChatCommand("fws1del")] private void Cmd1Del(BasePlayer p, string c, string[] a) => TryDelete(p, 1);
        [ChatCommand("fws2del")] private void Cmd2Del(BasePlayer p, string c, string[] a) => TryDelete(p, 2);
        [ChatCommand("fws3del")] private void Cmd3Del(BasePlayer p, string c, string[] a) => TryDelete(p, 3);
        [ChatCommand("fws4del")] private void Cmd4Del(BasePlayer p, string c, string[] a) => TryDelete(p, 4);
        [ChatCommand("fws5del")] private void Cmd5Del(BasePlayer p, string c, string[] a) => TryDelete(p, 5);
        [ChatCommand("fws6del")] private void Cmd6Del(BasePlayer p, string c, string[] a) => TryDelete(p, 6);
        [ChatCommand("fws7del")] private void Cmd7Del(BasePlayer p, string c, string[] a) => TryDelete(p, 7);
        [ChatCommand("fws8del")] private void Cmd8Del(BasePlayer p, string c, string[] a) => TryDelete(p, 8);
        [ChatCommand("fws9del")] private void Cmd9Del(BasePlayer p, string c, string[] a) => TryDelete(p, 9);
        [ChatCommand("fws10del")] private void Cmd10Del(BasePlayer p, string c, string[] a) => TryDelete(p, 10);
        [ChatCommand("fws11del")] private void Cmd11Del(BasePlayer p, string c, string[] a) => TryDelete(p, 11);
        [ChatCommand("fws12del")] private void Cmd12Del(BasePlayer p, string c, string[] a) => TryDelete(p, 12);
        [ChatCommand("fws13del")] private void Cmd13Del(BasePlayer p, string c, string[] a) => TryDelete(p, 13);
        [ChatCommand("fws14del")] private void Cmd14Del(BasePlayer p, string c, string[] a) => TryDelete(p, 14);
        [ChatCommand("fws15del")] private void Cmd15Del(BasePlayer p, string c, string[] a) => TryDelete(p, 15);
        [ChatCommand("fws16del")] private void Cmd16Del(BasePlayer p, string c, string[] a) => TryDelete(p, 16);
        [ChatCommand("fws17del")] private void Cmd17Del(BasePlayer p, string c, string[] a) => TryDelete(p, 17);
        [ChatCommand("fws18del")] private void Cmd18Del(BasePlayer p, string c, string[] a) => TryDelete(p, 18);
        [ChatCommand("fws19del")] private void Cmd19Del(BasePlayer p, string c, string[] a) => TryDelete(p, 19);
        [ChatCommand("fws20del")] private void Cmd20Del(BasePlayer p, string c, string[] a) => TryDelete(p, 20);
        [ChatCommand("fws21del")] private void Cmd21Del(BasePlayer p, string c, string[] a) => TryDelete(p, 21);

        private bool HasAccess(BasePlayer player)
        {
            if (player == null)
                return false;

            if (permission.UserHasPermission(player.UserIDString, PermUse))
                return true;

            player.ChatMessage("You don't have permission to use firework shows.");
            return false;
        }

        private void TrySpawn(BasePlayer player, int id, string[] args)
        {
            if (!HasAccess(player))
                return;

            SpawnShow(player, id, args);
        }

        private void TryDelete(BasePlayer player, int id)
        {
            if (!HasAccess(player))
                return;

            var startFreq = GetStartFrequency(id);
            ShowInstance show;
            if (!activeShows.TryGetValue(startFreq, out show))
            {
                player.ChatMessage($"Show {id} is not currently active.");
                return;
            }

            Cleanup(show, true);
        }

        private bool FindCopyPaste()
        {
            if (CopyPaste != null)
                return true;

            foreach (var name in CopyPasteNames)
            {
                CopyPaste = plugins.Find(name);
                if (CopyPaste != null)
                    return true;
            }

            return false;
        }

        private static int GetStartFrequency(int id) => 100 + id;
        private static string GetFilename(int id) => $"fwshow{id}";

        private static string[] BuildPasteArgs(string[] userArgs)
        {
            if (userArgs == null || userArgs.Length == 0)
                return DefaultPasteArgs;

            if ((userArgs.Length & 1) != 0)
                throw new ArgumentException("Paste arguments must be provided in pairs, for example: height 14");

            var finalArgs = new List<string>(userArgs.Length + 2) { "stability", "false" };
            for (var i = 0; i < userArgs.Length; i += 2)
            {
                var key = userArgs[i];
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (key.Equals("stability", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("s", StringComparison.OrdinalIgnoreCase))
                    continue;

                finalArgs.Add(key);
                finalArgs.Add(userArgs[i + 1]);
            }

            return finalArgs.ToArray();
        }

        private void SpawnShow(BasePlayer player, int id, string[] args)
        {
            if (!ShowSequences.ContainsKey(id))
            {
                player.ChatMessage($"Show {id} does not exist.");
                return;
            }

            if (!FindCopyPaste())
            {
                player.ChatMessage("CopyPaste plugin was not found.");
                return;
            }

            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, RaycastDistance))
            {
                player.ChatMessage("Look at the ground or a surface where the show should spawn.");
                return;
            }

            var startFreq = GetStartFrequency(id);
            if (activeShows.ContainsKey(startFreq))
            {
                player.ChatMessage($"Show {id} is already active.");
                return;
            }

            var filename = GetFilename(id);
            var show = new ShowInstance
            {
                Player = player,
                ShowId = id,
                StartFrequency = startFreq,
                Filename = filename
            };

            activeShows[startFreq] = show;
            pendingByFilename[filename] = startFreq;

            try
            {
                var rotationCorrection = player.GetNetworkRotation().eulerAngles.y * Mathf.Deg2Rad;
                var result = CopyPaste.CallHook("TryPasteFromVector3", hit.point, rotationCorrection, filename, BuildPasteArgs(args), null, null);

                var error = result as string;
                if (!string.IsNullOrEmpty(error))
                    throw new Exception(error);
            }
            catch (Exception ex)
            {
                ForgetShow(show);
                player.ChatMessage($"Paste failed: {ex.Message}. Example: /fws14 height 14");
                return;
            }

            AddTimer(show, timer.Once(PasteTimeoutSeconds, () =>
            {
                if (show.PasteFinished || !IsActive(show))
                    return;

                ForgetShow(show);
                player.ChatMessage($"Show {id} did not finish spawning. Check server console for CopyPaste errors.");
            }));
        }

        private void OnPasteFinished(List<BaseEntity> pastedEntities, string filename, IPlayer iPlayer, Vector3 startPos)
        {
            if (string.IsNullOrEmpty(filename))
                return;

            int startFreq;
            if (!pendingByFilename.TryGetValue(filename, out startFreq))
                return;

            ShowInstance show;
            if (!activeShows.TryGetValue(startFreq, out show))
                return;

            pendingByFilename.Remove(filename);
            show.PasteFinished = true;
            DestroyTimers(show);

            if (pastedEntities != null)
            {
                show.Entities.AddRange(pastedEntities);
                CacheReceivers(show, pastedEntities);
            }

            AddTimer(show, timer.Once(show.ShowId == 14 ? 7f : 5f, () =>
            {
                if (IsActive(show))
                    ActivatePoweredSwitches(show);
            }));

            if (show.RemoteGiven)
                return;

            GiveRemote(show.Player, show.ShowId, show.StartFrequency, show);
            show.RemoteGiven = true;
            show.Player?.ChatMessage($"Show {show.ShowId} spawned. Use the RF transmitter to start it.");
        }

        private void CacheReceivers(ShowInstance show, List<BaseEntity> pastedEntities)
        {
            foreach (var entity in pastedEntities)
            {
                var receiver = entity as RFReceiver;
                if (receiver == null || receiver.IsDestroyed)
                    continue;

                var frequency = receiver.GetFrequency();
                List<RFReceiver> receivers;
                if (!show.ReceiversByFrequency.TryGetValue(frequency, out receivers))
                {
                    receivers = new List<RFReceiver>();
                    show.ReceiversByFrequency[frequency] = receivers;
                }

                receivers.Add(receiver);
            }
        }

        private void GiveRemote(BasePlayer player, int id, int freq, ShowInstance show)
        {
            if (player == null || player.inventory == null)
                return;

            var item = ItemManager.CreateByName(RemoteShortname, 1);
            if (item == null)
            {
                player.ChatMessage("Failed to create RF transmitter.");
                return;
            }

            item.name = $"Firework Show {id}";
            item.text = $"Firework Show {id}";
            SetRemoteFrequencyData(item, freq);

            if (!item.MoveToContainer(player.inventory.containerBelt) && !item.MoveToContainer(player.inventory.containerMain))
            {
                item.Remove();
                player.ChatMessage("No inventory space for the RF transmitter.");
                return;
            }

            show.RemoteItemUid = item.uid.Value;
            activeByRemoteUid[show.RemoteItemUid] = show;
            player.Command("note.inv", item.info.itemid, 1);

            NextTick(() => EnsureRemoteFrequency(player, item, freq));
        }

        private void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            if (!IsRemote(newItem))
                return;

            ShowInstance show;
            if (!activeByRemoteUid.TryGetValue(newItem.uid.Value, out show) || show.Player == null || show.Player.userID != player.userID)
                return;

            NextTick(() => EnsureRemoteFrequency(player, newItem, show.StartFrequency));
        }

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null || !input.WasJustPressed(BUTTON.FIRE_PRIMARY))
                return;

            var item = player.GetActiveItem();
            if (!IsRemote(item))
                return;

            ShowInstance show;
            if (!activeByRemoteUid.TryGetValue(item.uid.Value, out show) || show.Player == null || show.Player.userID != player.userID)
                return;

            EnsureRemoteFrequency(player, item, show.StartFrequency);

            if (show.Started || !show.PasteFinished || !IsActive(show))
                return;

            show.Started = true;
            StartShow(show);
        }

        private static bool IsRemote(Item item)
        {
            return item?.info != null && item.info.shortname == RemoteShortname;
        }

        private static void SetRemoteFrequencyData(Item item, int freq)
        {
            if (item == null)
                return;

            try
            {
                item.instanceData = item.instanceData ?? new ProtoBuf.Item.InstanceData();
                item.instanceData.dataInt = freq;
                item.MarkDirty();
            }
            catch { }
        }

        private void EnsureRemoteFrequency(BasePlayer player, Item item, int freq)
        {
            SetRemoteFrequencyData(item, freq);

            var detonator = player?.GetHeldEntity() as Detonator;
            if (detonator == null)
                return;

            try
            {
                detonatorFrequencyField?.SetValue(detonator, freq);
                detonator.SendNetworkUpdateImmediate();
            }
            catch { }
        }

        private void ActivatePoweredSwitches(ShowInstance show)
        {
            foreach (var entity in show.Entities)
            {
                var sw = entity as ElectricSwitch;
                if (sw == null || sw.IsDestroyed || sw.HasFlag(BaseEntity.Flags.On) || sw.inputs == null)
                    continue;

                var powerIn = FindPowerInput(sw);
                if (powerIn == null)
                    continue;

                var source = powerIn.connectedTo?.Get();
                if (!IsAllowedPowerSource(source))
                    continue;

                try
                {
                    sw.SetSwitch(true);
                }
                catch
                {
                    using (var setFlags = sw.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate_Flags))
                    {
                        setFlags.Set(BaseEntity.Flags.On, true);
                    }
                }

                sw.SendNetworkUpdateImmediate();
            }
        }

        private static IOEntity.IOSlot FindPowerInput(ElectricSwitch sw)
        {
            foreach (var input in sw.inputs)
            {
                if (input == null || input.type != IOEntity.IOType.Electric)
                    continue;

                if (!string.IsNullOrEmpty(input.niceName) && !input.niceName.Equals("Power In", StringComparison.OrdinalIgnoreCase))
                    continue;

                return input;
            }

            return null;
        }

        private static bool IsAllowedPowerSource(IOEntity source)
        {
            if (source == null || source.IsDestroyed)
                return false;

            var shortPrefab = source.ShortPrefabName;
            if (!string.IsNullOrEmpty(shortPrefab) && AllowedShortPrefabs.Contains(shortPrefab.ToLowerInvariant()))
                return true;

            var prefab = source.PrefabName;
            return !string.IsNullOrEmpty(prefab) &&
                   (prefab.IndexOf("generators/generator.small.prefab", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    prefab.IndexOf("gates/combiner/electrical.combiner.deployed.prefab", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void StartShow(ShowInstance show)
        {
            int[][] sequence;
            if (!ShowSequences.TryGetValue(show.ShowId, out sequence))
                return;

            var triggerAt = 0f;
            var lastTriggerAt = 0f;

            foreach (var group in sequence)
            {
                for (var i = 0; i < group.Length; i++)
                {
                    var frequency = group[i];
                    var delay = triggerAt;
                    AddTimer(show, timer.Once(delay, () =>
                    {
                        if (IsActive(show))
                            TriggerShowFrequency(show, frequency);
                    }));

                    lastTriggerAt = delay;
                    if (i < group.Length - 1)
                        triggerAt += GroupStepDelay;
                }

                triggerAt += SequenceStepDelay;
            }

            AddTimer(show, timer.Once(lastTriggerAt + CleanupDelayAfterLastTrigger, () =>
            {
                if (IsActive(show))
                    Cleanup(show, true);
            }));

            show.Player?.ChatMessage($"Show {show.ShowId} started.");
        }

        private void TriggerShowFrequency(ShowInstance show, int freq)
        {
            List<RFReceiver> receivers;
            if (!show.ReceiversByFrequency.TryGetValue(freq, out receivers))
                return;

            foreach (var receiver in receivers)
            {
                if (receiver == null || receiver.IsDestroyed)
                    continue;

                receiver.RFSignalUpdate(true);
                AddTimer(show, timer.Once(RfPulseSeconds, () =>
                {
                    if (receiver != null && !receiver.IsDestroyed)
                        receiver.RFSignalUpdate(false);
                }));
            }
        }

        private void Cleanup(ShowInstance show, bool notifyPlayer)
        {
            if (show == null)
                return;

            DestroyTimers(show);

            for (var i = show.Entities.Count - 1; i >= 0; i--)
            {
                var entity = show.Entities[i];
                if (entity != null && !entity.IsDestroyed)
                    entity.Kill();
            }

            ForgetShow(show);
            show.Entities.Clear();
            show.ReceiversByFrequency.Clear();

            if (notifyPlayer)
                show.Player?.ChatMessage($"Show {show.ShowId} removed.");
        }

        private void ForgetShow(ShowInstance show)
        {
            activeShows.Remove(show.StartFrequency);
            pendingByFilename.Remove(show.Filename ?? GetFilename(show.ShowId));

            if (show.RemoteItemUid != 0)
                activeByRemoteUid.Remove(show.RemoteItemUid);
        }

        private bool IsActive(ShowInstance show)
        {
            ShowInstance current;
            return show != null && activeShows.TryGetValue(show.StartFrequency, out current) && ReferenceEquals(current, show);
        }

        private void AddTimer(ShowInstance show, Timer timerInstance)
        {
            if (show != null && timerInstance != null)
                show.Timers.Add(timerInstance);
        }

        private static void DestroyTimers(ShowInstance show)
        {
            if (show == null)
                return;

            for (var i = 0; i < show.Timers.Count; i++)
                show.Timers[i]?.Destroy();

            show.Timers.Clear();
        }
    }
}
