#!/usr/bin/env bash
# Generates ASMLite.Editor.csproj and ASMLite.Runtime.csproj for compile checking.
# UNITY_PATH: directory containing UnityEngine.dll and UnityEditor.dll
# PACKAGE_PATH: root of the com.staples.asm-lite package
# CI_PROJECT_PATH: root of ci/unity-project
set -euo pipefail

UNITY_ENGINE_DLL="${UNITY_PATH}/UnityEngine.dll"
UNITY_EDITOR_DLL="${UNITY_PATH}/UnityEditor.dll"
VRC_SDK_BASE="${CI_PROJECT_PATH}/Packages/com.vrchat.base/Runtime/VRCSDK/Plugins/VRCSDKBase.dll"
VRC_SDK3A="${CI_PROJECT_PATH}/Packages/com.vrchat.avatars/Runtime/VRCSDK/Plugins/VRCSDK3A.dll"
VRC_SDK3A_EDITOR="${CI_PROJECT_PATH}/Packages/com.vrchat.avatars/Runtime/VRCSDK/Plugins/VRCSDK3A-Editor.dll"
VRC_DYNAMICS="${CI_PROJECT_PATH}/Packages/com.vrchat.base/Runtime/VRCSDK/Plugins/VRC.Dynamics.dll"

OUT_DIR="${CI_PROJECT_PATH}"

echo "Generating ASMLite.Runtime.csproj"
cat > "${OUT_DIR}/ASMLite.Runtime.csproj" <<CSPROJ
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <Nullable>disable</Nullable>
    <NoWarn>CS0649;CS0108;CS0414;CS1998</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="${PACKAGE_PATH}/ASMLiteComponent.cs" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="UnityEngine">
      <HintPath>${UNITY_ENGINE_DLL}</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="VRCSDKBase">
      <HintPath>${VRC_SDK_BASE}</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
CSPROJ

echo "Generating ASMLite.Editor.csproj"
cat > "${OUT_DIR}/ASMLite.Editor.csproj" <<CSPROJ
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <Nullable>disable</Nullable>
    <NoWarn>CS0649;CS0108;CS0414;CS1998</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="${PACKAGE_PATH}/Editor/**/*.cs" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="UnityEngine">
      <HintPath>${UNITY_ENGINE_DLL}</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEditor">
      <HintPath>${UNITY_EDITOR_DLL}</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="VRCSDKBase">
      <HintPath>${VRC_SDK_BASE}</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="VRC.SDK3A">
      <HintPath>${VRC_SDK3A}</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="VRC.SDK3A.Editor">
      <HintPath>${VRC_SDK3A_EDITOR}</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="VRC.Dynamics">
      <HintPath>${VRC_DYNAMICS}</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
  <ProjectReference Include="${OUT_DIR}/ASMLite.Runtime.csproj" />
</Project>
CSPROJ

echo "Done. Build with:"
echo "  dotnet build ${OUT_DIR}/ASMLite.Runtime.csproj"
echo "  dotnet build ${OUT_DIR}/ASMLite.Editor.csproj"
