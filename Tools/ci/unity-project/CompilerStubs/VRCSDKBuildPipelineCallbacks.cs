using UnityEngine;

namespace VRC.SDKBase.Editor.BuildPipeline
{
    public enum VRCSDKRequestedBuildType
    {
        Avatar,
        Scene
    }

    public interface IVRCSDKBuildRequestedCallback
    {
        int callbackOrder { get; }
        bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType);
    }

    public interface IVRCSDKPreprocessAvatarCallback
    {
        int callbackOrder { get; }
        bool OnPreprocessAvatar(GameObject avatarRoot);
    }
}
