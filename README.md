## Description

This is a mod for Derail Valley to customize Demonstrator and garage spawns. For details, see [the mod page on Nexus Mods](https://www.nexusmods.com/derailvalley/mods/1546).

## API for other mods

When the player presses one of the force respawn buttons in the settings, this mod tears down and rebuilds the affected locomotives. Any references another mod holds to those cars are stale afterwards. To be told when that happens subscribe to `CustomDemonstrators.Api.ForceApplyEvents.Applied`:

```csharp
using CustomDemonstrators.Api;

ForceApplyEvents.Applied += kind =>
{
    if (kind == ForceApplyKind.Demonstrators)
        ReattachMyDemonstratorState();
};
```

The event is raised on the main thread once the respawn has finished, so the new cars and the comms radio are already in their final state when the event fires.

### Subscribing by reflection

If you'd rather not take a hard dependency on this mod, the same event can be subscribed to by reflection. Note that `Delegate.CreateDelegate` accepts a handler taking the enum's underlying `int` in place of a `ForceApplyKind`, which saves you from having to construct a delegate over a type you can't name:

```csharp
using System;
using System.Reflection;
using UnityModManagerNet;

private static int _demonstratorsKind = -1;

private static void SubscribeToForceApply()
{
    var mod = UnityModManager.FindMod("CustomDemonstrators");
    if (mod == null || !mod.Active || !mod.HasAssembly) return;

    var events = mod.Assembly.GetType("CustomDemonstrators.Api.ForceApplyEvents");
    var kindType = mod.Assembly.GetType("CustomDemonstrators.Api.ForceApplyKind");
    var applied = events?.GetEvent("Applied");
    if (applied == null || kindType == null) return;

    _demonstratorsKind = (int)Enum.Parse(kindType, "Demonstrators");

    applied.AddEventHandler(null, Delegate.CreateDelegate(applied.EventHandlerType,
        typeof(MyMod).GetMethod(nameof(OnForceApplied), BindingFlags.NonPublic | BindingFlags.Static)));
}

private static void OnForceApplied(int kind)
{
    if (kind == _demonstratorsKind)
        ReattachMyDemonstratorState();
}
```

Call this from somewhere that runs after this mod has loaded — its `Load` if you list `CustomDemonstrators` in your `LoadAfter`, or lazily on first use if you'd rather not care about mod order. To unsubscribe later, hold onto the delegate you passed to `AddEventHandler` and hand it to `RemoveEventHandler`.

## Building

Building the project requires some initial setup, after which running `dotnet build` will do a Debug build or running `dotnet build -c Release` will do a Release build.

### References Setup

After cloning the repository, some setup is required in order to successfully build the mod DLLs. You will need to create a new [Directory.Build.targets][references-url] file to specify your local reference paths. This file will be located in the main directory, next to MOD_NAME.sln.

Below is an example of the necessary structure. When creating your targets file, you will need to replace the reference paths with the corresponding folders on your system. Make sure to include semicolons **between** each of the paths and no semicolon after the last path. Also note that any shortcuts you might use in file explorer—such as %ProgramFiles%—won't be expanded in these paths. You have to use full, absolute paths.
```xml
<Project>
	<PropertyGroup>
		<ReferencePath>
			C:\Program Files (x86)\Steam\steamapps\common\Derail Valley\DerailValley_Data\Managed\
		</ReferencePath>
		<AssemblySearchPaths>
			$(AssemblySearchPaths);$(ReferencePath);$(ReferencePath.Trim())..\..\Mods\DVLangHelper\;$(ReferencePath.Trim())..\..\Mods\DVCustomCarLoader\
		</AssemblySearchPaths>
	</PropertyGroup>
</Project>
```

## Packaging

To package a build for distribution, you can run the `package.ps1` PowerShell script in the root of the project. If no parameters are supplied, it will create a .zip file ready for distribution in the dist directory. A post build event is configured to run this automatically after each successful Release build.

Linux: `pwsh ./package.ps1`
Windows: `powershell -executionpolicy bypass .\package.ps1`

### Parameters

Some parameters are available for the packaging script.

#### -NoArchive

Leave the package contents uncompressed in the output directory.

#### -OutputDirectory

Specify a different output directory.
For instance, this can be used in conjunction with `-NoArchive` to copy the mod files into your Derail Valley installation directory.

#### -ArchiveSuffix

Append a suffix to the archive's file name, e.g. `-ArchiveSuffix Debug` writes `dist/CustomDemonstratorsDebug.zip`.

## License

Source code is distributed under the MIT license.
See [LICENSE][license-url] for more information.

[license-url]: https://github.com/SharkBaitDLS/dv-stock-car-remover/blob/main/LICENSE
[references-url]: https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-your-build?view=vs-2022
