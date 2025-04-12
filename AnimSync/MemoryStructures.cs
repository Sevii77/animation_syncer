using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.Havok.Animation.Playback.Control.Default;

namespace AnimSync;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct Actor {
	[FieldOffset(0xF0)] public DrawObject* DrawObject;
	
	public hkaDefaultAnimationControl* Control {
		get {
			if(DrawObject == null) return null;
			
			var skeleton = DrawObject->Skeleton;
			if(skeleton == null) return null;
			
			var partials = skeleton->PartialSkeletons;
			if(partials == null) return null;
			
			var hkaSkeleton = partials->GetHavokAnimatedSkeleton(0);
			if(hkaSkeleton == null) return null;
			if(hkaSkeleton->AnimationControls.Length == 0) return null;
			
			return hkaSkeleton->AnimationControls[0].Value;
		}
	}
}

[StructLayout(LayoutKind.Explicit)]
public unsafe struct DrawObject {
	[FieldOffset(0xA0)] public Skeleton* Skeleton;
}