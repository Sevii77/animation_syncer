using System;
using System.Numerics;
using Dalamud.IoC;
using Dalamud.Hooking;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Graphics;

namespace AnimSync;

public class AnimSync: IDalamudPlugin {
	public string Name => "Animation Syncer";
	
	[PluginService][RequiredVersion("1.0")] public static DalamudPluginInterface Interface  {get; private set;} = null!;
	[PluginService][RequiredVersion("1.0")] public static ICommandManager        Commands   {get; private set;} = null!;
	[PluginService][RequiredVersion("1.0")] public static IPluginLog             Logger     {get; private set;} = null!;
	[PluginService][RequiredVersion("1.0")] public static IObjectTable           Objects    {get; private set;} = null!;
	[PluginService][RequiredVersion("1.0")] public static IGameInteropProvider   HookProv   {get; private set;} = null!;
	[PluginService][RequiredVersion("1.0")] public static ISigScanner            SigScanner {get; private set;} = null!;
	
	private const string command = "/animsync";
	private const float MAXDIST = 0.25f;
	private const float MAXROT = (float)Math.PI / 4; // 45 degrees
	
	private unsafe delegate void AnimRootDelegate(Skeleton* skelObj, nint b);
	private static Hook<AnimRootDelegate> AnimRootHook = null!;
	
	private static Dictionary<nint, (Vector3, Quaternion)> RootSyncs = new();
	
	public unsafe AnimSync() {
		AnimRootHook = HookProv.HookFromAddress<AnimRootDelegate>(SigScanner.ScanText("48 83 EC 08 8B 02"), AnimRoot);
		AnimRootHook.Enable();
		
		Interface.UiBuilder.Draw += Draw;
		Commands.AddHandler(command, new CommandInfo(OnCommand) {
			HelpMessage = "Resets all player and battlenpc animations to 0"
		});
	}
	
	public void Dispose() {
		AnimRootHook.Dispose();
		
		Interface.UiBuilder.Draw -= Draw;
		Commands.RemoveHandler(command);
	}
	
	private unsafe void Draw() {
		lock(RootSyncs)
			RootSyncs.Clear();
		
		foreach(var obj in Objects) {
			if(IsValidObject(obj)) {
				var actor = (Actor*)obj.Address;
				var syncs = new List<(GameObject, bool)>();
				
				foreach(var obj2 in Objects) {
					if(IsValidObject(obj2)) {
						var actor2 = (Actor*)obj2.Address;
						
						if(obj == obj2) continue;
						if(actor->Control->hkaAnimationControl.Binding.ptr->Animation.ptr->Duration != actor2->Control->hkaAnimationControl.Binding.ptr->Animation.ptr->Duration) continue;
						if(Math.Abs((obj2.Rotation - obj.Rotation + Math.PI) % (Math.PI * 2) - Math.PI) > MAXROT) continue;
						if(Vector3.Distance(obj.Position, obj2.Position) > MAXDIST) continue;
						
						syncs.Add((obj2, actor->Control->hkaAnimationControl.LocalTime != actor2->Control->hkaAnimationControl.LocalTime));
					}
				}
				
				lock(RootSyncs) {
					if(syncs.Count > 0) {
						var position = obj.Position;
						var rotations = new List<Quaternion>() {Quaternion.CreateFromAxisAngle(Vector3.UnitY, obj.Rotation)};
						var time = actor->Control->hkaAnimationControl.LocalTime;
						foreach(var (syncObj, syncTime) in syncs) {
							position += syncObj.Position;
							rotations.Add(Quaternion.CreateFromAxisAngle(Vector3.UnitY, syncObj.Rotation));
							
							if(syncTime)
								time = Math.Max(time, ((Actor*)syncObj.Address)->Control->hkaAnimationControl.LocalTime);
						}
						
						position /= syncs.Count + 1;
						
						Quaternion rotation;
						if(rotations.Count == 2)
							rotation = Quaternion.Slerp(rotations[0], rotations[1], 0.5f);
						else {
							// Havent actually tested this but it *should* be okey at averaging 2+ player rotations with the lossy netcode that is xiv
							var target = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0);
							rotations.Sort((a, b) => Math.Abs(Quaternion.Dot(target, a)).CompareTo(Math.Abs(Quaternion.Dot(target, b))));
							rotation = rotations[0];
							for(int i = 1; i < rotations.Count; i++)
								rotation = Quaternion.Slerp(rotation, rotations[i], 1f / (i + 1));
						}
						
						
						RootSyncs[(nint)((Actor*)obj.Address)->DrawObject->Skeleton] = (position, rotation);
						actor->Control->hkaAnimationControl.LocalTime = time;
						
						foreach(var (syncObj, syncTime) in syncs) {
							RootSyncs[(nint)((Actor*)syncObj.Address)->DrawObject->Skeleton] = (position, rotation);
							if(syncTime)
								((Actor*)syncObj.Address)->Control->hkaAnimationControl.LocalTime = time;
						}
							
					}
				}
			}
		}
	}
	
	private unsafe void OnCommand(string cmd, string args) {
		if(cmd != command)
			return;
		
		foreach(var obj in Objects) {
			if(IsValidObject(obj)) {
				var actor = (Actor*)obj.Address;
				actor->Control->hkaAnimationControl.LocalTime = 0;
			}
		}
	}
	
	private unsafe void AnimRoot(Skeleton* skeleton, nint b) {
		AnimRootHook.Original(skeleton, b);
		
		lock(RootSyncs) {
			if(RootSyncs.TryGetValue((nint)skeleton, out var sync)) {
				skeleton->Transform.Position = sync.Item1;
				skeleton->Transform.Rotation = sync.Item2;
			}
		}
		
		// lock(actorBones)
		// 	if(actorBones.TryGetValue((nint)((PartialSkeleton*)skelObj->Skeletons)->Pose, out var bones))
		// 		skelObj->Transform = bones.root.GetValue(bones.time);
	}
	
	private unsafe bool IsValidObject(GameObject obj) {
		return (obj.ObjectKind == ObjectKind.BattleNpc || obj.ObjectKind == ObjectKind.Player) && ((Actor*)obj.Address)->Control != null;
	}
}