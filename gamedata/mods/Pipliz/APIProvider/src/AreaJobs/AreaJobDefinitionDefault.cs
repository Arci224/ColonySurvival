﻿using NPC;
using Pipliz.Collections;
using Pipliz.Helpers;
using System.Threading;

namespace Pipliz.Mods.APIProvider.AreaJobs
{
	using Areas;
	using JSON;

	public class AreaJobDefinitionDefault<T> : IAreaJobDefinition where T : IAreaJobDefinition
	{
		protected string fileName;
		protected string identifier;
		protected ushort[] stages;
		protected NPCType npcType;
		protected Shared.EAreaType areaType;
		protected SortedList<Colony, JSONNode> SavedJobs;
		protected JSONNode LoadedRoot;
		protected ManualResetEvent FinishedLoadingEvent = new ManualResetEvent(false);

		public virtual NPCType UsedNPCType { get { return npcType; } }
		public virtual string Identifier { get { return identifier; } }
		public virtual string FilePath { get { return $"gamedata/savegames/{ServerManager.WorldName}/areajobs/{fileName}.json"; } }
		public virtual Shared.EAreaType AreaType { get { return areaType; } }

		public virtual IAreaJob CreateAreaJob (Colony owner, JSONNode node)
		{
			Vector3Int min = Vector3Int.invalidPos;
			Vector3Int max = Vector3Int.invalidPos;

			JSONNode child;
			if (node.TryGetChild("min", out child)) {
				min.x = child.GetAsOrDefault("x", -1);
				min.y = child.GetAsOrDefault("y", -1);
				min.z = child.GetAsOrDefault("z", -1);
			}
			if (node.TryGetChild("max", out child)) {
				max.x = child.GetAsOrDefault("x", -1);
				max.y = child.GetAsOrDefault("y", -1);
				max.z = child.GetAsOrDefault("z", -1);
			}
			int npcID = node.GetAsOrDefault("npcID", 0);
			return CreateAreaJob(owner, min, max, npcID);
		}

		public virtual IAreaJob CreateAreaJob (Colony owner, Vector3Int min, Vector3Int max, int npcID = 0)
		{
			return new DefaultFarmerAreaJob<T>(owner, min, max, npcID);
		}

		public virtual void OnRemove (IAreaJob job)
		{

		}

		public virtual void CalculateSubPosition (IAreaJob job, ref Vector3Int positionSub)
		{
			if (stages == null || stages.Length < 2) {
				return;
			}

			bool hasSeeds = job.NPC.Colony.Stockpile.Contains(stages[0]);
			bool reversed = false;
			Vector3Int firstPlanting = Vector3Int.invalidPos;
			Vector3Int min = job.Minimum;
			Vector3Int max = job.Maximum;

			for (int x = min.x; x <= max.x; x++) {
				int z = reversed ? max.z : min.z;
				while (reversed ? (z >= min.z) : (z <= max.z)) {

					ushort type;
					Vector3Int possiblePositionSub = new Vector3Int(x, min.y, z);
					if (!World.TryGetTypeAt(possiblePositionSub, out type)) {
						return;
					}
					if (type == 0) {
						if (!hasSeeds && !firstPlanting.IsValid) {
							firstPlanting = possiblePositionSub;
						}
						if (hasSeeds) {
							positionSub = possiblePositionSub;
							return;
						}
					}
					if (type == stages[stages.Length - 1]) {
						positionSub = possiblePositionSub;
						return;
					}

					z = reversed ? z - 1 : z + 1;
				}
				reversed = !reversed;
			}

			if (firstPlanting.IsValid) {
				positionSub = firstPlanting;
				return;
			}

			int xRandom = Random.Next(min.x, max.x + 1);
			int zRandom = Random.Next(min.z, max.z + 1);
			positionSub = new Vector3Int(xRandom, min.y, zRandom);
		}

		static System.Collections.Generic.List<ItemTypes.ItemTypeDrops> GatherResults = new System.Collections.Generic.List<ItemTypes.ItemTypeDrops>();

		public virtual void OnNPCAtJob (IAreaJob job, ref Vector3Int positionSub, ref NPCBase.NPCState state, ref bool shouldDumpInventory)
		{
			if (stages == null || stages.Length < 2) {
				state.SetCooldown(1.0);
				return;
			}
			state.JobIsDone = true;
			if (positionSub.IsValid) {
				ushort type;
				if (World.TryGetTypeAt(positionSub, out type)) {
					ushort typeSeeds = stages[0];
					ushort typeFinal = stages[stages.Length - 1];
					if (type == 0) {
						if (state.Inventory.TryGetOneItem(typeSeeds)
							|| job.NPC.Colony.Stockpile.TryRemove(typeSeeds)) {
							ushort typeBelow;
							if (World.TryGetTypeAt(positionSub.Add(0, -1, 0), out typeBelow)) {
								// check for fertile below
								if (ItemTypes.GetType(typeBelow).IsFertile) {
									ServerManager.TryChangeBlock(positionSub, typeSeeds, job.Owner.Owners[0], ServerManager.SetBlockFlags.DefaultAudio);
									state.SetCooldown(1.0);
									shouldDumpInventory = false;
								} else {
									// not fertile below
									AreaJobTracker.RemoveJob(job);
									state.SetCooldown(2.0);
								}
							} else {
								// didn't load this part of the world
								state.SetCooldown(Random.NextFloat(3f, 6f));
							}
						} else {
							state.SetIndicator(new Shared.IndicatorState(2f, typeSeeds, true, false));
							shouldDumpInventory = state.Inventory.UsedCapacity > 0f;
						}
					} else if (type == typeFinal) {
						if (ServerManager.TryChangeBlock(positionSub, 0, job.Owner.Owners[0], ServerManager.SetBlockFlags.DefaultAudio)) {
							GatherResults.Clear();
							var results = ItemTypes.GetType(typeFinal).OnRemoveItems;
							for (int i = 0; i < results.Count; i++) {
								GatherResults.Add(results[i]);
							}

							ModLoader.TriggerCallbacks(ModLoader.EModCallbackType.OnNPCGathered, job as IJob, positionSub, GatherResults);

							job.NPC.Inventory.Add(GatherResults);
						}
						state.SetCooldown(1.0);
						shouldDumpInventory = false;
					} else {
						shouldDumpInventory = state.Inventory.UsedCapacity > 0f;
						state.SetCooldown(5.0);
					}
				} else {
					state.SetCooldown(Random.NextFloat(3f, 6f));
				}
				positionSub = Vector3Int.invalidPos;
			} else {
				state.SetCooldown(10.0);
			}
		}

		public virtual void StartLoading ()
		{
			ThreadPool.QueueUserWorkItem(AsyncLoad);
		}

		protected virtual void AsyncLoad (object obj)
		{
			try {
				JSON.Deserialize(FilePath, out LoadedRoot, false);
			} catch (System.Exception e) {
				Log.WriteException(e);
			} finally {
				FinishedLoadingEvent.Set();
			}
		}

		public virtual void FinishLoading ()
		{
			while (!FinishedLoadingEvent.WaitOne(500)) {
				Log.Write("Waiting for {0} to finish loading...", typeof(T));
			}
			FinishedLoadingEvent = null;
			if (LoadedRoot != null) {
				LoadJSON(LoadedRoot);
				LoadedRoot = null;
			}
		}

		public virtual void LoadJSON (JSONNode node)
		{
			JSONNode table = node.GetAs<JSONNode>("table");
			foreach (var pair in table.LoopObject()) {
				Colony colony = ServerManager.ColonyTracker.Get(int.Parse(pair.Key));
				JSONNode array = pair.Value;
				for (int i = 0; i < array.ChildCount; i++) {
					var job = CreateAreaJob(colony, array[i]);
					if (!AreaJobTracker.RegisterAreaJob(job)) {
						job.OnRemove();
					}
				}
			}
		}

		public virtual void SaveJob (Colony owner, JSONNode data)
		{
			if (SavedJobs == null) {
				SavedJobs = new SortedList<Colony, JSONNode>(10);
			}
			JSONNode array;
			if (!SavedJobs.TryGetValue(owner, out array)) {
				array = new JSONNode(NodeType.Array);
				SavedJobs[owner] = array;
			}
			array.AddToArray(data);
		}

		public virtual void FinishSaving ()
		{
			Application.StartAsyncQuitToComplete(delegate ()
			{
				if (Application.IsQuiting) {
					Log.Write("Saving {0}", fileName);
				}

				JSONNode root = new JSONNode();
				root.SetAs("version", 0);
				JSONNode table = new JSONNode();
				root.SetAs("table", table);
				if (SavedJobs != null) {
					for (int i = 0; i < SavedJobs.Count; i++) {
						Colony c = SavedJobs.GetKeyAtIndex(i);
						JSONNode n = SavedJobs.GetValueAtIndex(i);
						table.SetAs(c.ColonyID.ToString(), n);
					}
					SavedJobs = null;
				}

				string filePath = FilePath;
				IOHelper.CreateDirectoryFromFile(filePath);
				JSON.Serialize(filePath, root, 3);
			});
		}

		// TODO: colony parameter
		protected void SetLayer (Vector3Int min, Vector3Int max, ushort type, int layer, Players.Player owner)
		{
			int yLayer = min.y + layer;
			for (int x = min.x; x <= max.x; x++) {
				for (int z = min.z; z <= max.z; z++) {
					ServerManager.TryChangeBlock(new Vector3Int(x, yLayer, z), type, owner);
				}
			}
		}
	}
}
