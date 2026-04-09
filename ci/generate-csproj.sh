#!/usr/bin/env bash
# Generates ASMLite.Editor.csproj and ASMLite.Runtime.csproj for compile checking.
# UNITY_PATH: directory containing UnityEngine.dll and UnityEditor.dll
# PACKAGE_PATH: root of the com.staples.asm-lite package
# CI_PROJECT_PATH: root of ci/unity-project
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

UNITY_PATH="${UNITY_PATH:-ci/unity-project/UnityManaged}"
PACKAGE_PATH="${PACKAGE_PATH:-Packages/com.staples.asm-lite}"
CI_PROJECT_PATH="${CI_PROJECT_PATH:-ci/unity-project}"

VRC_SDK_BASE="${REPO_ROOT}/${CI_PROJECT_PATH}/Packages/com.vrchat.base/Runtime/VRCSDK/Plugins/VRCSDKBase.dll"
VRC_SDK3A="${REPO_ROOT}/${CI_PROJECT_PATH}/Packages/com.vrchat.avatars/Runtime/VRCSDK/Plugins/VRCSDK3A.dll"
VRC_SDK3A_EDITOR="${REPO_ROOT}/${CI_PROJECT_PATH}/Packages/com.vrchat.avatars/Runtime/VRCSDK/Plugins/VRCSDK3A-Editor.dll"
VRC_DYNAMICS="${REPO_ROOT}/${CI_PROJECT_PATH}/Packages/com.vrchat.base/Runtime/VRCSDK/Plugins/VRC.Dynamics.dll"

OUT_DIR="${REPO_ROOT}/${CI_PROJECT_PATH}"

echo "Generating ASMLite.Runtime.csproj"
cat > "${OUT_DIR}/ASMLite.Runtime.csproj" <<CSPROJ
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <Nullable>disable</Nullable>
    <NoWarn>CS0649;CS0414</NoWarn>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="${REPO_ROOT}/${PACKAGE_PATH}/ASMLiteComponent.cs" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>${REPO_ROOT}/${UNITY_PATH}/UnityEngine.CoreModule.dll</HintPath>
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
    <TargetFramework>netstandard2.1</TargetFramework>
    <Nullable>disable</Nullable>
    <NoWarn>CS0649;CS0414</NoWarn>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="${REPO_ROOT}/${PACKAGE_PATH}/Editor/**/*.cs" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>${REPO_ROOT}/${UNITY_PATH}/UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.AnimationModule">
      <HintPath>${REPO_ROOT}/${UNITY_PATH}/UnityEngine.AnimationModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.IMGUIModule">
      <HintPath>${REPO_ROOT}/${UNITY_PATH}/UnityEngine.IMGUIModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.TextRenderingModule">
      <HintPath>${REPO_ROOT}/${UNITY_PATH}/UnityEngine.TextRenderingModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEditor.CoreModule">
      <HintPath>${REPO_ROOT}/${UNITY_PATH}/UnityEditor.CoreModule.dll</HintPath>
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
  <ItemGroup>
    <ProjectReference Include="${REPO_ROOT}/${CI_PROJECT_PATH}/ASMLite.Runtime.csproj" />
  </ItemGroup>
</Project>
CSPROJ

echo "Done. Build with:"
echo "  dotnet build ${OUT_DIR}/ASMLite.Runtime.csproj"
echo "  dotnet build ${OUT_DIR}/ASMLite.Editor.csproj"
